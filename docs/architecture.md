# Architecture

SampleMobile is a proof of concept for one idea: **notifications belong to the application; push providers are replaceable delivery adapters.** Firebase Cloud Messaging is one adapter among several, confined to the Infrastructure project.

## Layers and the dependency rule

```text
SampleMobile.Domain          entities, value objects, domain events, retry policy. No packages at all.
        ▲
SampleMobile.Application     use cases (one file each), ports (interfaces), provider-neutral DTOs.
        ▲                    References only Microsoft.Extensions.*.Abstractions.
SampleMobile.Infrastructure  EF Core + Npgsql, ASP.NET Core Identity, JWT issuing, Firebase Admin SDK,
        ▲                    simulator, outbox processor, delivery worker.
SampleMobile.Web             composition root: Blazor dashboard, mobile REST controllers, auth policies.
```

Project references enforce the rule at compile time: Application can't `using Microsoft.EntityFrameworkCore` or `FirebaseAdmin`. `tests/.../Architecture/DependencyRuleTests.cs` also fails the build if Domain or Application ever reference EF Core, Npgsql, ASP.NET Core or `FirebaseAdmin`/`Google.*`, or if Web references Firebase.

## Data model (PostgreSQL)

All notification tables live in the `notification` schema; Identity tables live in `identity`; the sample business table lives in `demo`. Primary keys are UUIDv7 and every identifier is snake_case.

| Table | Purpose | Notable constraints |
|---|---|---|
| `notification.notifications` | Canonical content: type, title, body, `metadata` (jsonb), `resource_id`. | Unique `deduplication_key` (nullable). |
| `notification.recipients` | One row per user per notification; holds `read_at`. | Unique (`notification_id`, `user_id`). |
| `notification.device_registrations` | Installation ↔ provider ↔ address, enabled flag, timestamps. | Unique (`installation_id`, `provider_id`). |
| `notification.delivery_jobs` | One push of one notification to one registration: status, attempts, lease, last error. | Unique (`notification_id`, `device_registration_id`); `xmin` concurrency token. |
| `notification.delivery_attempts` | Every attempt, including abandoned and not-sent ones. | Unique (`delivery_job_id`, `attempt_number`). |
| `notification.outbox_messages` | Domain events written in the producing transaction. | Index (`status`, `next_attempt_at`). |
| `demo.demo_alerts` | Sample business data that raises `DemoAlertRaised`. | |

Content and read state never depend on delivery: a notification with zero delivery jobs (inbox-only) is perfectly valid.

## Two ways a notification is created

```text
Dashboard "Compose"                                Dashboard "Raise DemoAlert"
  SendNotificationCommand                            RaiseDemoAlertCommand
        │                                                  │  DemoAlert row + outbox row (one transaction)
        │                                                  ▼
        │                                           OutboxProcessor (FOR UPDATE SKIP LOCKED, own transaction)
        │                                                  │  DemoAlertNotificationHandler
        ▼                                                  ▼  intent with DeduplicationKey = "demo-alert-raised:{eventId}"
  INotificationPublisher.PublishAsync(intent) ◄────────────┘
        │  validates, resolves targets, saves Notification + recipients + DeliveryJobs in ONE transaction
        ▼  pg_notify('sample_mobile_delivery')
  DeliveryWorker → claim (SKIP LOCKED + lease) → INotificationDispatcher → IPushNotificationProvider
```

- **Transactional outbox.** `UnitOfWork` turns every domain event queued on a tracked aggregate into an `outbox_messages` row inside the same `SaveChanges`. The alert can't exist without its event, and an event can't exist without its alert. Events are never dispatched in-process.
- **Idempotent event handling.** The outbox processor handles each row in its own transaction while holding the row lock, then marks it processed in that same transaction. A crash before commit means it runs again, and the handler's deduplication key (backed by a unique index) turns the replay into a no-op.
- **Persist first, dispatch later.** Nothing talks to a provider inside a use case. An FCM outage can't roll back a notification or any domain data; it only produces failed or retried delivery attempts.

## Delivery

### The provider port

```csharp
public interface IPushNotificationProvider {
    string ProviderId { get; }                    // "fcm", "simulated", …
    PushProviderDescriptor Describe();           // configured? simulated?
    Task<PushDispatchResult> SendAsync(PushDispatchRequest request, CancellationToken ct);
}
```

`PushDispatchResult.Outcome` is one of five values:

| Outcome | Meaning | Dispatcher reaction |
|---|---|---|
| `Accepted` | The provider took the message. **Not** proof of display on a device. | Job → `ProviderAccepted`. |
| `TransientFailure` | Network, throttling, outage, timeout, or an unexpected exception. | `RetryScheduled` with backoff (honours `Retry-After`), or `Failed` when the budget is spent. |
| `InvalidRegistration` | The provider *explicitly* confirmed the address is dead (FCM `UNREGISTERED`). | `Failed`; registration disabled (`invalidatedByProvider`). |
| `AuthenticationFailure` | Credentials or configuration are missing or rejected. | `Failed`, device untouched; requeue from the dashboard after fixing the configuration. |
| `PermanentFailure` | The message can't ever succeed as sent. | `Failed`. |

`IPushProviderResolver` matches a registration's `provider_id` to an adapter **exactly**. There is no fallback, so an FCM token is never handed to the simulator, APNs, OneSignal or anything else. A registration whose provider has no adapter fails with `no_compatible_provider` without being sent.

### Worker mechanics

- **Claiming.** `SELECT … FOR UPDATE SKIP LOCKED` inside a short transaction sets `status = Processing`, a `locked_until` lease and a new `InProgress` attempt. The provider call happens afterwards, outside any transaction.
- **No busy polling.** Committing new or requeued work runs `pg_notify`, and `PostgresWorkListener` (one `LISTEN` connection per process) wakes the worker. Otherwise the worker sleeps until the next `next_attempt_at` or lease expiry, capped at `MaxIdleWait` (30 s) as a safety net.
- **Retries.** Bounded exponential backoff: `RetryBaseDelay × 2^(n−1)`, capped at `RetryMaxDelay`, up to `MaxAttempts` per run. A requeue starts a fresh run.
- **Concurrency safety.** Several workers or app instances never claim the same job. If a worker stalls past its lease, another reclaims the job and the stalled attempt is recorded as `Abandoned`. The `xmin` concurrency token stops the stale worker from overwriting the newer result.
- **Shutdown.** On a stop signal, in-flight jobs are released immediately (attempt `Abandoned`, job due again) instead of waiting for the lease to expire.

### At-least-once, not exactly-once

Exactly-once delivery to an external system is not achievable here. If the process dies after FCM accepted a message but before the outcome was committed, the lease expires and the job is sent again. The history shows the lost attempt as `Abandoned (outcome unknown)`. These mitigations keep the duplicate harmless:

- FCM `collapse_key` and the Android notification `tag` both equal the notification id, so a second copy replaces the first in the tray.
- Push data carries only the notification id, and the client renders from the inbox API and de-duplicates by id.
- Read state lives on the server, so a duplicate push can't "unread" anything.

## When Firebase is unavailable

| Situation | What happens |
|---|---|
| Firebase not configured | FCM adapter stays registered; FCM jobs fail immediately with `firebase_not_configured` (no network call). Devices stay enabled; notifications are in the inbox. The dashboard shows the provider as *Not configured*. |
| Bad credentials (missing file, malformed key, rejected service account) | `firebase_credentials_invalid` / `firebase_auth_rejected` → `Failed`, no retries, devices untouched. Fix the configuration, then **Requeue**. |
| FCM outage, throttling, network errors | `TransientFailure` → retries with backoff (and `Retry-After`), up to `MaxAttempts`. |
| Wrong Firebase project (`SENDER_ID_MISMATCH`, `NOT_FOUND`, `PERMISSION_DENIED`) | Treated as configuration errors, **not** invalid tokens, so a misconfiguration can't mass-disable valid devices. |
| Everything else | The inbox API, read state, dashboard, simulator and outbox keep working; none of them call Firebase. |

## Adding a provider (e.g. APNs or OneSignal)

1. Write `Infrastructure/Push/<Provider>/<Provider>PushNotificationProvider.cs` implementing `IPushNotificationProvider`, with its own options class and error classifier that map vendor errors to the five outcomes. Only that folder references the vendor SDK.
2. Register it in `Infrastructure/DependencyInjection.cs → AddPushProviders` (`services.AddSingleton<IPushNotificationProvider, …>()`).
3. Bind its configuration section and document the secrets.
4. Clients register devices with the new provider id and its address format.

None of the following change: Domain entities, `NotificationPublisher`, `NotificationDispatcher`, the use cases, the mobile API, or the dashboard. The resolver and dashboard discover the new provider through DI.

## Security notes

- Provider addresses (FCM tokens) are effectively credentials for pushing to a device. They are never returned by the mobile API, are masked on the dashboard (`fXyz12…a9Bc`), and are masked in every log line.
- Firebase credentials are read from a file path outside the repo, from user-secrets or an environment variable, or from application default credentials. They are never logged or committed.
- The dashboard cookie and the mobile JWT are pinned to separate authorization policies: a cookie can't call the mobile API (no CSRF exposure) and a mobile token can't open the dashboard.
- The mobile user id always comes from the token's `sub` claim, never from a request body. Another user's resources return `404`, so ids can't be probed.

## Deliberate departures from the house conventions

| Convention | What this POC does instead | Why |
|---|---|---|
| Allman braces | K&R braces | The repository's `.editorconfig` mandates them, and the conventions defer to `.editorconfig`. |
| Domain events dispatched in-process after commit, best-effort ("safe to lose") | All domain events go through the transactional outbox; handlers are at-least-once and idempotent | Turning an event into a notification must not be lost. |
| Seeding via `UseSeeding` | `DatabaseInitializer` seeds through `UserManager` | Identity users need password hashing, normalization and security stamps. |
| Three mirrored test projects | One `SampleMobile.Tests` project with `Domain/`, `Application/`, `Infrastructure/`, `Integration/`, `Architecture/` folders | The specification names a single test project. |
| Strict JSON that omits nulls | Nulls are always written | The contract requires `"readAt": null`. |

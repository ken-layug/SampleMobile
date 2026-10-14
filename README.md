# SampleMobile: provider-agnostic notifications

An ASP.NET Core 10 proof of concept for managing push notifications **independently of Firebase or any other delivery provider**. Notifications, per-user read state and delivery history live in PostgreSQL and belong to the application. Firebase Cloud Messaging is one replaceable adapter in the Infrastructure layer, next to an in-process **simulated provider** that runs without any Firebase account.

The one web process hosts three things:

- a **Blazor admin dashboard**: compose notifications, raise a sample domain event, inspect devices, delivery history and inboxes, and run simulator scenarios;
- a **mobile REST API** for the companion [.NET MAUI Android app](src/SampleMobile.Mobile/README.md) (inbox, read state, device registration, JWT auth);
- **background processing**: a transactional-outbox processor and a delivery worker that claims jobs with `FOR UPDATE SKIP LOCKED`, retries with bounded backoff, and wakes on PostgreSQL `LISTEN/NOTIFY` instead of polling.

See [docs/architecture.md](docs/architecture.md) for the design, provider boundaries and delivery semantics, and [docs/mobile-api-contract.md](docs/mobile-api-contract.md) for the client contract.

New to Firebase but comfortable with .NET? Start with the [Firebase messaging flow guide](docs/firebase-messaging-flow.md), which explains the Firebase concepts and traces registration, sending, Android handling, and logout with sequence and class diagrams. The [learning documentation review](docs/learning-documentation-review.md) records the original findings and their resolution.

## Prerequisites

- .NET SDK 10.0.4xx (`dotnet --list-sdks`)
- Docker Desktop, for the local PostgreSQL and the integration tests
- Optional: the [Aspire CLI](https://aspire.dev) 13.6, to run everything with one command and get the dashboard
- The .NET **Android workload** (`dotnet workload install maui-android`, or Visual Studio's .NET MAUI workload) with Android SDK 36 and JDK 17/21. `SampleMobile.slnx` includes the MAUI app, so `dotnet build` and `dotnet test` at the root need it.
- Optional: a Firebase project and an Android device or Google Play emulator, for real FCM pushes

The MAUI app is not part of the Aspire AppHost; it is launched separately against the running backend. See [src/SampleMobile.Mobile/README.md](src/SampleMobile.Mobile/README.md).

## Quick start (no Firebase needed)

With Aspire (recommended):

```bash
aspire start          # PostgreSQL + pgAdmin containers, then the web app once the database is up
aspire stop
```

The [AppHost](src/SampleMobile.AppHost/AppHost.cs) starts a persistent PostgreSQL 17 container with a data volume and a generated password (kept in the AppHost's user-secrets), plus pgAdmin, and injects `ConnectionStrings:SampleMobile` into the web app. The Aspire dashboard (URL printed by `aspire start`) shows each resource's health, console logs, structured logs, traces and metrics. pgAdmin opens from the `pgadmin` resource there.

Without Aspire:

```bash
docker compose up -d
dotnet tool restore
dotnet run --project src/SampleMobile.Web --launch-profile http
```

Either way:

1. Open <http://localhost:5010>. In Development the app applies migrations and seeds demo data on startup.
2. Sign in with the demo **administrator**. The user names and passwords for all demo accounts are under `DemoSeed` in [`src/SampleMobile.Web/appsettings.Development.json`](src/SampleMobile.Web/appsettings.Development.json). They are created only when the environment is `Development`.
3. Go to **Simulated provider tests** and run the four scenarios, or **Compose** a notification to *Alice*, whose seeded virtual device uses the simulated provider.

Seeded data:

| Account | Role | Notes |
|---|---|---|
| `admin` | Admin | Dashboard only (cookie sign-in). |
| `alice` | Mobile user | Has a virtual device on the `simulated` provider. |
| `bob` | Mobile user | No devices, so notifications to Bob are inbox-only until a device registers. |

With compose, PostgreSQL listens on **localhost:5433**, not 5432, so it doesn't clash with a locally installed PostgreSQL. To use another port, set `SAMPLE_MOBILE_PG_PORT` and override the connection string. The Aspire database is a separate container on a dynamic port with its own data. `dotnet ef` commands use the connection string in `appsettings.Development.json`, so they target the compose database.

In Development, `/health` (readiness, including a PostgreSQL check) and `/alive` (liveness) are mapped by the shared `SampleMobile.ServiceDefaults` project, which also exports OpenTelemetry when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire sets it).

## Configuration

Settings use standard ASP.NET Core configuration: `appsettings*.json`, then [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) in Development, then environment variables (`Section__Key`).

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings:SampleMobile` | Development: the docker-compose database | PostgreSQL connection string. Required. |
| `Jwt:SigningKey` | — | HMAC key for mobile tokens, at least 32 bytes. **Required outside Development**; Development falls back to an ephemeral key (tokens die on restart). |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:AccessTokenLifetime` | `SampleMobile` / `SampleMobile.MobileApi` / `08:00:00` | Token claims and lifetime. |
| `Firebase:CredentialsPath` | — | Path to a Firebase service-account JSON **outside the repo**. |
| `Firebase:CredentialsJson` | — | The service-account JSON itself (user-secrets or `Firebase__CredentialsJson`). |
| `Firebase:UseApplicationDefaultCredentials` | `false` | Use Google application default credentials (`GOOGLE_APPLICATION_CREDENTIALS`, workload identity). |
| `Firebase:ProjectId` | from the credentials | Optional override. |
| `Firebase:ValidateOnly` | `false` | FCM dry run: validates messages and tokens, delivers nothing. History shows `validate-only:` message ids. |
| `Firebase:AndroidChannelId` | — | Android notification channel the app creates. |
| `Delivery:*` | see `appsettings.json` | `MaxAttempts`, `RetryBaseDelay`, `RetryMaxDelay`, `LeaseDuration`, `SendTimeout`, `BatchSize`, `MaxParallelism`, `MaxIdleWait`, `WorkerEnabled`. Development shortens the backoff to 3 s so retries are easy to watch. |
| `Outbox:*` | see `appsettings.json` | `Enabled`, `MaxAttempts`, retry delays, `MaxIdleWait`. |
| `Push:Simulated:Latency` | `00:00:00.250` | Artificial simulator latency. |
| `Database:ApplyMigrationsOnStartup` | `true` in Development | Apply EF Core migrations at startup. |
| `DemoSeed:*` | enabled in Development | Demo accounts and the seeded virtual device. Ignored outside Development. |

User-secrets examples (stored in your user profile, never in the repo):

```bash
dotnet user-secrets --project src/SampleMobile.Web set "Jwt:SigningKey" "<at least 32 random characters>"
```

```bash
dotnet user-secrets --project src/SampleMobile.Web set "Firebase:CredentialsPath" "C:\secrets\sample-mobile-firebase-adminsdk.json"
```

Environment-variable equivalents: `Jwt__SigningKey`, `Firebase__CredentialsPath`, `Firebase__CredentialsJson`, `ConnectionStrings__SampleMobile`.

## Database and migrations

Development applies migrations on startup. To manage them by hand (the `dotnet-ef` tool is pinned in `dotnet-tools.json`):

```bash
dotnet ef database update --project src/SampleMobile.Infrastructure --startup-project src/SampleMobile.Web
```

```bash
dotnet ef migrations add <Name> --project src/SampleMobile.Infrastructure --startup-project src/SampleMobile.Web --output-dir Persistence/Migrations
```

To reset the local database completely:

```bash
docker compose down -v
```

Schemas: `notification` (notifications, recipients, device registrations, delivery jobs and attempts, outbox), `identity` (ASP.NET Core Identity), `demo` (the sample business table). All identifiers are snake_case and primary keys are UUIDv7.

For a progressive exercise with expected evidence at each stage, follow [Trace one notification end to end](docs/firebase-messaging-flow.md#trace-one-notification-end-to-end).

## Dashboard tour

| Page | What it shows |
|---|---|
| **Overview** | Total notifications, pending / provider-accepted / failed deliveries, simulated dispatches, active devices, provider status, outbox counters. |
| **Compose** | *Direct use case*: recipient, type, title, body, optional resource id and metadata, and the recipient's delivery targets (each registration shows its own provider; incompatible or unconfigured ones are flagged). *Domain event*: raise a `DemoAlert`, which travels through the transactional outbox. |
| **Registered devices** | User, installation, platform, provider, masked address, enabled/disabled reason, last seen and updated. |
| **Delivery history** | Every delivery job with provider, attempt count, normalized status, last error and last attempt; click through to individual attempts. Failed or cancelled jobs can be requeued. |
| **Simulated provider tests** | Four one-click scenarios (accept, transient → retry → accept, permanent failure, invalid registration), live history and the simulator's in-memory message log. |
| **Inbox inspector** | Any user's persisted inbox and read state, exactly as the mobile API returns it. |

## Sample workflows

**Dashboard execution scope.** Interactive components call [DashboardSender](src/SampleMobile.Web/Components/DashboardSender.cs), which creates a DI scope per operation. A Blazor circuit outlives an HTTP request; this avoids sharing one scoped DbContext across overlapping clicks and refreshes.

**Manual notification.** Compose → choose *Alice* → keep the virtual device checked → save. The notification and one delivery job are stored together. The worker wakes through `NOTIFY`, sends through the simulator, and the job shows *Provider accepted* and *Simulated*. Alice's inbox (Inbox inspector or `GET /api/mobile/notifications`) contains it as unread.

**Domain event through the outbox.** Compose → *Raise DemoAlert* for *Bob*. A `demo.demo_alerts` row and an outbox row commit together. The outbox processor turns the event into a `demo.alert` notification (deduplicated by event id) for each of Bob's enabled devices, or inbox-only if he has none.

**Retries and failures.** Simulated provider tests → *Transient failure, then retry*: attempts 1 and 2 fail and are rescheduled 3 s and then 6 s later; attempt 3 is accepted. *Invalid device registration* disables that scenario's virtual device (visible under Registered devices).

**Firebase unavailable.** Register an `fcm` device (via the mobile API) and send it a notification without configuring Firebase. The job fails with `firebase_not_configured`, the device stays enabled, and the notification is in the inbox. After configuring credentials, use **Requeue** on the job. See [What happens when Firebase is unavailable](docs/architecture.md#when-firebase-is-unavailable).

## Firebase setup (real Android pushes)

1. In the [Firebase console](https://console.firebase.google.com/), create a project (or reuse one) and **add an Android app** whose package name matches the MAUI app's `ApplicationId`. Download `google-services.json` **for the MAUI project**. It is client configuration; the backend doesn't use it.
2. Go to *Project settings → Service accounts → Generate new private key*. Save the JSON **outside this repository**, for example `C:\secrets\sample-mobile-firebase-adminsdk.json`. The repo's `.gitignore` also blocks `*firebase-adminsdk*.json` and `*service-account*.json` as a safety net.
3. Point the backend at the key with user-secrets (`Firebase:CredentialsPath`, shown above) or `Firebase__CredentialsPath`, or put the JSON in `Firebase:CredentialsJson`.
4. Optionally set `Firebase:ValidateOnly=true` to check the key and a device token without delivering anything. Optionally set `Firebase:AndroidChannelId` to the channel the app creates.
5. Restart the backend. **Overview → Registered push providers** shows `fcm` as *Configured*. This means configuration is present, not that credentials have been validated; the SDK initializes on the first send.
6. Follow the [Android client setup](src/SampleMobile.Mobile/README.md#firebase-setup) using the same Firebase project, then the [worked learning exercise](docs/firebase-messaging-flow.md#trace-one-notification-end-to-end). Disable `ValidateOnly` for an actual device delivery test.

The setup chain is Firebase project → Android package/client configuration → device token → authenticated API registration → backend credentials → FCM send. The mobile JWT belongs to this backend, not Firebase Authentication. The flow guide explains [what each success checkpoint proves](docs/firebase-messaging-flow.md#setup-checkpoints).

The service account needs permission to send FCM messages (the default *Firebase Admin SDK* service account has it). Credentials are never logged; device tokens appear masked.

## Mobile API

- Contract and client guidance: [docs/mobile-api-contract.md](docs/mobile-api-contract.md)
- OpenAPI document: [docs/openapi/mobile-v1.json](docs/openapi/mobile-v1.json), or live at `/openapi/v1.json` in Development
- Interactive reference (Development): <http://localhost:5010/scalar>
- Manual test harness: [src/SampleMobile.Web/SampleMobile.Web.http](src/SampleMobile.Web/SampleMobile.Web.http). It has one request per endpoint plus the error paths (401, 400 validation, 404 cross-user). It is written for Rider's HTTP client; the response scripts store the token and check status codes.

## Tests

```bash
dotnet test
```

This runs the tests in two projects (use the test runner output for current counts):

- `tests/SampleMobile.Tests`: domain, application (with NSubstitute), adapters, architecture rules, and **PostgreSQL integration tests**.
- `tests/SampleMobile.Mobile.Tests`: the MAUI app's provider-neutral core. These cover contract JSON, auth/401 handling, registration sync and logout cleanup, push handling, the inbox and the mobile dependency rule. They need no device, Firebase or Docker.

The backend integration tests start a throwaway `postgres:17-alpine` container through Testcontainers, so Docker must be running. To run without Docker:

```bash
dotnet test -- --filter-not-trait "Category=Integration"
```

`global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xunit v3 uses on the .NET 10 SDK.

## Manual end-to-end checks

**A. Simulated provider (no Firebase, no device).**

1. `docker compose up -d`, run the app, and sign in as the admin.
2. Simulated provider tests → run all four scenarios. Expect:
   - acceptance: 1 attempt, *Provider accepted*;
   - transient: 3 attempts with growing gaps, then *Provider accepted*;
   - permanent: *Failed* after 1 attempt;
   - invalid registration: *Failed*, and under Registered devices the scenario device shows *Disabled: invalidated by provider*.
3. Compose a notification to Alice. It appears in Delivery history and in the Inbox inspector.
4. Raise a DemoAlert for Bob. The Overview outbox counter moves from pending to processed, and Bob's inbox shows a `demo.alert`.

**B. Mobile API with curl.** Follow the [curl walkthrough](docs/mobile-api-contract.md#curl-walkthrough):

- log in;
- register a `simulated` device;
- compose a notification to that user in the dashboard;
- list it, mark it read twice and confirm `readAt` is unchanged;
- delete the registration.

Also confirm:

- requests without a token get `401`;
- another user's notification returns `404`.

**C. Android device with FCM** (needs the [MAUI app](src/SampleMobile.Mobile/README.md) and Firebase credentials; its README has the full device checklist):

1. Configure Firebase (above) and put the app's `google-services.json` in `src/SampleMobile.Mobile/Platforms/Android/`. On an emulator, the default backend URL `http://10.0.2.2:5010` works as is. For a USB device, run `adb reverse tcp:5010 tcp:5010` and use `http://localhost:5010`. For a phone on the same Wi-Fi under `aspire start`, use `http://<your-LAN-IP>:5010` (see the app README for the firewall and cleartext steps).
2. Run the app (`dotnet build src/SampleMobile.Mobile -t:Run -f net10.0-android`) and sign in as `alice`. The app registers itself with `provider: "fcm"` and the FCM registration token.
3. Registered devices shows the Android `fcm` registration (token masked).
4. Compose to Alice, targeting the Android registration. Delivery history shows *Provider accepted* with an FCM message id, and the device shows the notification. Tapping it should open the item fetched from the inbox API.
5. Uninstall the app (or clear its data) and send again. FCM eventually reports `UNREGISTERED` and the registration is disabled (*invalidated by provider*). FCM may take a while to report it.
6. With the device in airplane mode, sends are still *Provider accepted*. Acceptance is not delivery.

## POC limits

See [POC limits and operational boundaries](docs/firebase-messaging-flow.md#poc-limits-and-operational-boundaries): the mobile inbox loads the newest 50 items, push is not guaranteed, background tray text precedes API authorization, and production operations require further design.

## Project layout

```text
src/
  SampleMobile.Domain/          Entities/ (Notification, DeviceRegistration, DeliveryJob, DemoAlert…), Events/, Services/
  SampleMobile.Application/     Abstractions/ (ports: Push, Repositories, Identity), Features/ (one use case per file),
                                Services/ (publisher, dispatcher, provider resolver), Events/Handlers/, Contracts/
  SampleMobile.Infrastructure/  Persistence/ (EF Core, migrations, outbox, repositories, seeding), Identity/, Authentication/,
                                Push/Firebase/, Push/Simulated/, BackgroundServices/ (worker, outbox processor, LISTEN)
  SampleMobile.Web/             Program.cs, Controllers/Mobile/, Components/ (Blazor dashboard), OpenApi/
  SampleMobile.ServiceDefaults/ OpenTelemetry, health endpoints, service discovery, HTTP resilience
  SampleMobile.AppHost/         Aspire AppHost: PostgreSQL, pgAdmin, web app
  SampleMobile.Mobile.Core/     MAUI client core (net10.0, no MAUI/Firebase): Api/, Session/, Notifications/, Push/, ViewModels/
  SampleMobile.Mobile/          .NET MAUI Android app: Views/, Services/, Platforms/Android/ (Notifications/, Push/Firebase/)
tests/SampleMobile.Tests/       Domain/, Application/, Infrastructure/, Architecture/, Integration/
tests/SampleMobile.Mobile.Tests/ Api/, Session/, Push/, Notifications/, Architecture/ (mobile core)
docs/                           architecture.md, mobile-api-contract.md, openapi/mobile-v1.json
```

## Troubleshooting

- **`ports are not available … 5432`**: another PostgreSQL is running. Compose already maps to 5433. If 5433 is also taken, set `SAMPLE_MOBILE_PG_PORT` and update the connection string.
- **`aspire start` fails with a file lock (MSB3491/CS2012)**: an earlier run still holds the build output. Run `aspire stop` and start again.
- **Mobile `401` after restarting the backend**: Development uses an ephemeral JWT key unless `Jwt:SigningKey` is set. Set it with user-secrets for stable tokens.
- **`firebase_not_configured` / `firebase_credentials_invalid` in Delivery history**: expected without (valid) Firebase credentials. Fix the configuration, restart, and requeue the job.
- **`dotnet test` fails to start containers**: start Docker Desktop, or exclude the integration tests as shown above.

# Learning documentation review

Reviewed 2026-10-14 against the current repository. Audience: intermediate-to-advanced .NET developers familiar with ASP.NET Core, Clean Architecture, ports/adapters, separation of concerns, and DI, but with no Firebase experience.

## Resolution status

The documentation corrections below were applied on 2026-10-14. The original assessment and findings are retained as a historical review, not as a list of outstanding documentation defects. Application behavior was not changed.

| Finding | Resolution |
|---|---|
| 1. Payload and display | Architecture, API principles, and shared-device guidance now distinguish API-fetched content from Android background text. |
| 2. Collapse and duplicates | References separate collapsing, tray replacement, bounded recent-ID tracking, and inbox identity. |
| 3. Logout | References use durable old-JWT cleanup followed by local sign-out and deferred DELETE/reset; the flow guide owns the detailed sequence. |
| 4. Rotation exercise | The Android checklist separates forced re-registration, observed SDK rotation, and new installation. |
| 5. Setup chain | Root/client setup sections link through the flow guide and its [checkpoint table](firebase-messaging-flow.md#setup-checkpoints). |
| 6. Progressive learning | Added a [worked exercise](firebase-messaging-flow.md#trace-one-notification-end-to-end) with illustrative correlation evidence and failure branches. |
| 7. POC boundaries | Added [limits and operational guidance](firebase-messaging-flow.md#poc-limits-and-operational-boundaries), including first-page-only mobile history and failed-outbox investigation. |
| 8. Reference details | Replaced the unused wrapper example, clarified Android targets, added PowerShell examples, explained dashboard scopes, removed fixed test counts, and linked SDK compatibility notes to pinned packages/source. |

Live device acceptance, observed SDK rotation, and production hardening remain separate validation/design work. The learning exercise deliberately does not claim these were performed.

## Original assessment

| Criterion | Verdict | Why |
|---|---|---|
| Complete | Substantial POC reference coverage, but not yet a complete Firebase learning path. | Setup, contracts, provider outcomes, persistence, Android lifecycle, and manual checks are covered. The Firebase identity/credential model, an end-to-end source walkthrough, and precise delivery limitations needed a bridge. |
| Cohesive | Architecturally cohesive; cross-document semantics need alignment. | The ownership of content and the provider boundary are consistent. Payload wording, logout ordering, collapse behavior, and a device test give conflicting or overbroad impressions. |
| Easy to understand | Good for the stated .NET background; uneven for a Firebase newcomer. | Readers can follow the backend patterns without introductory Clean Architecture material. The jump from server configuration to Android SDK callbacks assumes Firebase knowledge and requires switching between several long documents. |

The best improvement is a focused Firebase walkthrough, not more introductory material about DI or interfaces. The added [Firebase messaging flow guide](firebase-messaging-flow.md) supplies the concepts, four sequence diagrams, two class diagrams, code reading order, and a debugging route. It supplements the existing references. The findings below capture the pre-correction state; the resolution table above records the applied changes.

## What already works well

- [README](../README.md): a runnable simulator-first route, configuration matrix, dashboard tour, and observable manual scenarios. Readers can learn persistence and retries before setting up Firebase.
- [Architecture](architecture.md): clear transaction boundaries, provider outcomes, leases, outbox idempotency, and acceptance-versus-delivery distinction. These are useful explanations even for an experienced backend developer.
- [API contract](mobile-api-contract.md): concrete JSON, status codes, ownership rules, registration lifecycle, and read-state semantics. The checked-in OpenAPI and HTTP harness provide complementary reference formats.
- [Mobile README](../src/SampleMobile.Mobile/README.md): unusually detailed networking, permission, foreground/background, offline cleanup, and troubleshooting coverage.
- Source comments and focused tests explain the rationale behind many non-obvious choices. The new guide links directly to representative implementations and tests.

## Original prioritized gaps and corrections

### 1. High: distinguish API-rendered UI from automatic background display

The architecture says content comes from the API and is never rendered from payload text; the API principles describe a push as carrying only identifiers. Later, the API payload section and mobile README correctly explain the notification title/body block and automatic background display. A Firebase newcomer can reasonably read the earlier claims as a guarantee that all displayed text passes through current-user API authorization.

[FcmMessageFactory](../src/SampleMobile.Infrastructure/Push/Firebase/FcmMessageFactory.cs) sends both blocks. [FirebaseAndroidPushTransport](../src/SampleMobile.Mobile/Platforms/Android/Push/Firebase/FirebaseAndroidPushTransport.cs) drops payload text only on the app callback path. Android may already have displayed background text before a tap causes an authorized fetch. This follows [Firebase's documented handling](https://firebase.google.com/docs/cloud-messaging/android/receive-messages).

**Correction:** qualify “push carries only identifiers” as “the data block carries identifiers.” State that API content is authoritative for app-controlled rendering, while the OS background tray uses the notification block. Explain that session-generation checks and `404` protect subsequent app processing, not text already shown by Android. The new flow guide makes this distinction explicit; align the architecture and contract summaries with it.

### 2. High: separate collapse behavior, tray replacement, and deduplication

The architecture, API contract, and mobile README group `collapse_key` and Android `tag` together as duplicate protection. [FcmMessageFactory](../src/SampleMobile.Infrastructure/Push/Firebase/FcmMessageFactory.cs) sets both, but they operate at different stages. Firebase documents that notification messages ignore `collapse_key`; collapsible-message storage also has a four-key limit. See [Firebase collapse semantics](https://firebase.google.com/docs/cloud-messaging/customize-messages/collapsible-message-types).

**Correction:** teach four separate mechanisms: provider-side collapsing before delivery; Android tray replacement by tag; the bounded in-memory recent-ID cache in [PushEventProcessor](../src/SampleMobile.Mobile.Core/Push/PushEventProcessor.cs); and canonical inbox/read state on the backend. Avoid implying exactly-once behavior or retention of every offline notification. The new guide includes this caveat; existing duplicate-protection explanations still need alignment.

### 3. Medium: give logout one authoritative explanation

The API contract instructs DELETE before discarding the access token and draws that order. [SessionService.LogoutAsync](../src/SampleMobile.Mobile.Core/Session/SessionService.cs) instead persists a cleanup record with the old JWT, clears the live session, then processes DELETE and provider reset. The mobile README is closer to the code.

**Correction:** distinguish retaining credentials for durable cleanup from retaining the active session. Link the API lifecycle summary to the implemented sequence. Explain that [DeviceRegistrationSync](../src/SampleMobile.Mobile.Core/Push/DeviceRegistrationSync.cs) can attempt provider reset even after a transient DELETE failure, and drops expired-JWT DELETE work while retaining reset work. Do not teach immediate device revocation as an offline guarantee.

### 4. Medium: correct the token-rotation exercise

Row 7 of the mobile manual checklist groups “Refresh registration” and “clear app data” as ways to test token refresh and expects the same installation ID. [SessionBootstrapper.RefreshRegistrationAsync](../src/SampleMobile.Mobile.Core/Session/SessionBootstrapper.cs) obtains the current address and forces a registration POST; it does not force Firebase to rotate the token. [InstallationIdProvider](../src/SampleMobile.Mobile.Core/Push/InstallationIdProvider.cs) generates a new GUID when its Preferences value is missing, which happens after clearing app data.

**Correction:** split this into three exercises: forced re-registration (same installation; token may be unchanged), actual token rotation (`OnNewToken`, same installation), and cleared app data/new installation (new installation ID). Describe a test as rotation only when a changed token or callback is actually observed.

### 5. Medium: explain the Firebase setup chain and its success criteria

The root and mobile READMEs contain the needed steps but split them across documents. A newcomer needs to connect Firebase project → registered Android package → compiled client configuration → SDK token → authenticated backend registration → server credentials → send. “Configured,” “Registered,” “Provider accepted,” and “Displayed” are different checkpoints.

[FirebaseOptions.IsConfigured](../src/SampleMobile.Infrastructure/Push/Firebase/FirebaseOptions.cs) checks configuration presence. [FirebaseMessagingClient](../src/SampleMobile.Infrastructure/Push/Firebase/FirebaseMessagingClient.cs) initializes credentials lazily. Neither a green configuration indicator nor a saved registration proves a successful real send.

**Improvement:** use the new guide's terminology and sequence as the learning entry point, while keeping setup commands in the existing READMEs. Explicitly identify `ValidateOnly` as non-delivering and keep backend service-account credentials distinct from client configuration and mobile JWTs.

### 6. Medium: make learning progressive and troubleshooting evidence-driven

The current docs offer useful scenarios, but no single guided exercise connects a notification ID to its delivery job, attempt, FCM result, mobile callback, API fetch, and read state. There is also no explicit recommended source reading order for a Firebase newcomer.

**Improvement:** follow simulator acceptance → one real foreground send → background/tap → offline recovery → invalid registration. For each, show which screen/log/API proves that stage, and what it does not prove. The new guide provides a source reading order and troubleshooting table. A future worked trace with sanitized example observations would complete this learning path.

### 7. Medium: clarify what this POC does not demonstrate

The API already excludes refresh tokens and device delivery receipts. Additional limits are scattered or implicit: [NotificationInboxService](../src/SampleMobile.Mobile.Core/Notifications/NotificationInboxService.cs) refreshes only the first 50 items; recent push deduplication is not persistent; high-priority sends and channel configuration do not prove visible delivery; production deployment/credential rotation and failed-outbox operational recovery are not taught as a runbook.

**Improvement:** add a concise POC limits section linking to relevant code and existing checklists. Treat production operations as an optional next-stage topic, not a prerequisite for learning Firebase. Do not imply that a refresh necessarily loads the entire historical inbox.

### 8. Low: tighten repository-specific reference details

- The contract mentions `IFirebaseCloudMessaging.GetTokenAsync()`, while this app uses the native Android binding through `FirebaseAndroidPushTransport`; label external wrapper examples or replace them with the actual entry point.
- The contract lists Windows/macOS MAUI hosts although this app targets Android. Label those URLs hypothetical rather than supported client targets.
- The curl walkthrough uses Bash and `jq`; label those prerequisites and consider a PowerShell equivalent for this Windows-oriented repository.
- Explain [DashboardSender](../src/SampleMobile.Web/Components/DashboardSender.cs) briefly where the learning path enters Compose: it creates a scope per operation because Blazor circuit scope differs from HTTP request scope. This is a repository-specific concern, not a reason to reteach DI.
- Hard-coded test counts and SDK deprecation claims are maintenance hotspots. Link version-sensitive statements to the pinned package/source or upstream release evidence and avoid presenting counts as permanent facts.

## Original suggested follow-up order (completed as documentation work)

1. Align payload/display, collapse, logout, and token-refresh wording across existing documents.
2. Use the new flow guide as the Firebase learning bridge, linked from the root README and architecture document.
3. Add one sanitized end-to-end worked trace, with expected observations and failure branches.
4. Add a concise POC limitations section and optional operational notes; keep vendor setup details in one authoritative location.

## Review scope and limits

This was a documentation-to-source review of the root README, architecture, mobile API contract, mobile README, and the corresponding publisher, dispatcher, outbox, Firebase adapters, registration, session, inbox, navigation, and detail workflows. It is not an exhaustive application security or concurrency audit. Firebase's official receiving and collapse documentation was consulted for the relevant external semantics.

No application behavior was changed. No backend stack, Android device, or live FCM send was exercised, and no application tests were run for this documentation-only change. The checked-in OpenAPI snapshot was not regenerated or compared against a live endpoint. Treat runtime/device acceptance as pending; source agreement alone cannot establish delivery behavior.

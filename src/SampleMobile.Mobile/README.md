# SampleMobile.Mobile: .NET MAUI Android notification receiver

An Android-first .NET MAUI 10 app that receives notifications from the SampleMobile backend.

**The backend owns everything:** notification records, per-user read state and delivery history. Push is only a hint that something changed, and Firebase Cloud Messaging (FCM) is the first and only push transport so far. The app fetches the authoritative notification from the REST API by id, so the inbox works with or without Firebase.

The app is **not** launched by Aspire. Start the backend first with `aspire start` or `dotnet run`, then run the app separately. See [Run](#run).

Start with the [Firebase learning guide](../../docs/firebase-messaging-flow.md) for concepts, diagrams, and a worked trace.

## Contents

- [Projects and boundaries](#projects-and-boundaries)
- [Prerequisites](#prerequisites)
- [Android project setup](#android-project-setup)
- [Firebase setup](#firebase-setup)
- [Backend URL and networking](#backend-url-and-networking)
- [Development HTTPS](#development-https)
- [Run](#run)
- [Login and registration flow](#login-and-registration-flow)
- [Verify incoming notifications](#verify-incoming-notifications)
- [Android notification lifecycle](#android-notification-lifecycle)
- [When FCM is unavailable](#when-fcm-is-unavailable)
- [Adding another push transport](#adding-another-push-transport)
- [Tests](#tests)
- [Manual Android test checklist](#manual-android-test-checklist)
- [Troubleshooting](#troubleshooting)

## Projects and boundaries

```text
src/SampleMobile.Mobile.Core/     net10.0. No MAUI, Android or Firebase.
  Api/            Contract DTOs, MobileJsonContext (source-generated, strict like the backend), MobileApiClient,
                  BearerTokenHandler (attaches the token, expires the session on 401), ApiResult
  Session/        SessionStore (SecureStorage), SessionService (login, restore, logout), SessionBootstrapper
  Notifications/  NotificationInboxService (app-owned inbox, keyed by backend id)
  Push/           IPushTransport, InboxOnlyPushTransport, PushTransportSelector, DeviceRegistrationSync,
                  PushEventProcessor, InstallationIdProvider, TokenMasker
  Navigation/     NavigationCoordinator (defers a notification tap until signed in and the shell is shown)
  ViewModels/     Login, Home, Inbox, NotificationDetail, Diagnostics (CommunityToolkit.Mvvm)
  Platform/       Ports the app implements: ISecureStore, IAppPreferences, INotificationPermissionService,
                  ILocalNotificationPresenter, INavigationService, INetworkStatus, IMainThreadDispatcher

src/SampleMobile.Mobile/          net10.0-android, MAUI. ApplicationId xyz.kiaranet.samplemobile.
  Views/, AppShell, App, MauiProgram   XAML pages (compiled bindings) and the composition root
  Services/                            MAUI adapters for the Core ports
  Platforms/Android/Notifications/     Channel, local notifications, tap intents, POST_NOTIFICATIONS (not tied to a provider)
  Platforms/Android/Push/Firebase/     The only code that uses the Firebase SDK

tests/SampleMobile.Mobile.Tests/  xunit v3 tests of Core. No device, Firebase or Android workload needed.
```

How the boundary is enforced:

- `tests/SampleMobile.Mobile.Tests/Architecture/MobileDependencyRuleTests.cs` fails if Core references any `Xamarin.*`, `Firebase*`, `Google.*`, `Microsoft.Maui*` or `Mono.Android` assembly.
- The same test fails if Core contains the `fcm` provider id.

Taken together, view models, models and API DTOs cannot see Firebase.

## Prerequisites

| Requirement | Notes |
|---|---|
| .NET SDK 10.0.4xx | Same as the backend. |
| Workloads | `android` and `maui-android`, installed by Visual Studio's ".NET Multi-platform App UI" workload or with `dotnet workload install maui-android`. The whole `SampleMobile.slnx` includes this project, so `dotnet build`/`dotnet test` at the repo root need it. |
| Android SDK | Platform 36 (Android 16) and build-tools 36. Visual Studio installs them under `C:\Program Files (x86)\Android\android-sdk`. |
| JDK 17 or 21 | If the build picks an unsupported JDK, pass `-p:JavaSdkDirectory="C:\Program Files\Eclipse Adoptium\jdk-21…"` or set it in Visual Studio's Android options. |
| Device | An emulator **with Google Play** (for example a "Google Play" system image) or a physical device. Push needs Google Play services; the inbox does not. |

The app's minimum API is 26 (Android 8.0), which guarantees notification channels. It targets API 36.

If a `dotnet` SDK update leaves the workloads behind, Android builds fail with `NETSDK1147 … wasm-tools`. Run `dotnet workload update` from an elevated terminal, or update Visual Studio.

## Android project setup

Package versions are central, in `Directory.Packages.props` under "Mobile":

| Package | Why |
|---|---|
| `Microsoft.Maui.Controls` 10.0.110 | Listed explicitly: central package management turns off MAUI's implicit reference. |
| `Xamarin.Firebase.Messaging` 125.1.3 | The official dotnet/android-libraries binding of firebase-messaging 25.x. It ships a `net10.0-android36.0` asset and brings Play Services Base/Basement/Tasks. Basement supplies the `GoogleServicesJson` build action. |
| `Xamarin.AndroidX.*.Ktx`, `Lifecycle.LiveData`, `Lifecycle.Process` | Firebase raises Fragment, Lifecycle and Collection above what MAUI pins. These pins keep the Kotlin `-ktx` companions in step; without them D8 fails with "Type androidx.fragment.app.FragmentKt is defined multiple times". |

Cross-platform wrappers (Plugin.Firebase, Plugin.FirebasePushNotifications) were not used. They pull in iOS code and hide the Android callbacks this app needs to control.

Manifest and other build-time settings:

- **No auto backup:** `allowBackup="false"` in `Platforms/Android/AndroidManifest.xml`. The installation id must never be cloned onto another device, and restored SecureStorage values can't be decrypted anyway.
- **Permissions:** `INTERNET`, `ACCESS_NETWORK_STATE` and `POST_NOTIFICATIONS` (Android 13+).
- **FCM meta-data:** added from `Platforms/Android/Push/Firebase/FirebasePushTransportExtensions.cs`, so the manifest itself stays provider-neutral:
  - default notification channel `sample_mobile_default`
  - default notification icon
  - `firebase_messaging_auto_init_enabled=false`: no token is minted before a user signs in.
- **Network security config:** chosen by build configuration in `Platforms/Android/MainApplication.cs` (see [Development HTTPS](#development-https)).
- **Single activity:** `MainActivity` is `SingleTop`, so a tap while the app is open arrives in `OnNewIntent`.

## Firebase setup

Firebase is optional. Without it the app runs in inbox-only mode. If Firebase is new to you, first read the [concepts and diagrams](../../docs/firebase-messaging-flow.md), then follow these client setup steps and the linked backend setup.

1. In the [Firebase console](https://console.firebase.google.com/), open (or create) the project the backend uses, then **add an Android app** with package name **`xyz.kiaranet.samplemobile`**.
2. Download `google-services.json` and put it at `src/SampleMobile.Mobile/Platforms/Android/google-services.json`.
   - It is git-ignored.
   - The csproj includes it as `GoogleServicesJson` only when the file exists, so the app builds either way.
   - It is client configuration, not a secret, but it is per developer project.
3. Configure the backend's service-account key (`Firebase:CredentialsPath`), as described in the root [README](../../README.md#firebase-setup-real-android-pushes).
   - Never put that key, or any server credential, in the app.
4. Optional: set the backend's `Firebase:AndroidChannelId` to `sample_mobile_default`. The app also declares it as FCM's default channel, so this changes nothing visible.
5. Rebuild and reinstall the app (`google-services.json` is compiled into resources).

The app uses only Firebase Cloud Messaging. Firebase Authentication, Firestore and Realtime Database are not used.

About registration tokens:

- This implementation uses the versions pinned in [Directory.Packages.props](../../Directory.Packages.props). Compatibility comments and scoped deprecation suppressions live beside the actual calls in [FirebaseAndroidPushTransport](Platforms/Android/Push/Firebase/FirebaseAndroidPushTransport.cs) and [SampleFirebaseMessagingService](Platforms/Android/Push/Firebase/SampleFirebaseMessagingService.cs); recheck them when upgrading SDKs.
- The backend's provider id `fcm` means "address = FCM registration token" (see `FcmMessageFactory`), so the transport uses the token APIs on purpose, with a scoped warning suppression.
- Moving to FIDs would be a new provider id with its own address type.

## Backend URL and networking

The login screen has a **Backend URL** field. It is stored in Preferences and can be changed without rebuilding.

| Where the app runs | URL | Backend |
|---|---|---|
| Android emulator (Debug default) | `http://10.0.2.2:5010` | `dotnet run --project src/SampleMobile.Web --launch-profile http`, or `aspire start` (web keeps port 5010) |
| USB-connected phone (recommended) | `http://localhost:5010` | Same, plus `adb reverse tcp:5010 tcp:5010`. No firewall rule or LAN exposure needed. |
| Phone on the same Wi-Fi | `http://<dev-machine-LAN-IP>:5010` | `aspire start` (it listens on all interfaces), or `dotnet run` with `--urls http://0.0.0.0:5010`. Allow inbound TCP 5010 through the firewall (Private profile). Then add the IP as a `<domain>` in `Platforms/Android/Resources/xml/network_security_config_debug.xml`, because cleartext is otherwise refused. |

The `10.0.2.2` alias works with Aspire too: the AppHost runs the web project with its `http` profile on port 5010. In run mode it binds that endpoint to `0.0.0.0` (`AppHost.cs`), so the same port also answers on the LAN IP. Aspire does not start the app; launch it separately.

## Development HTTPS

TLS validation is never disabled. There is no `ServerCertificateCustomValidationCallback` anywhere. The app sends its requests through Android's native handler (`AndroidMessageHandler`), so Android's network security config applies.

| Build | Network security config | What it allows |
|---|---|---|
| Release | `xml/network_security_config.xml` | HTTPS only, system CAs only. |
| Debug | `xml/network_security_config_debug.xml` | Cleartext HTTP **only** to `10.0.2.2`, `localhost` and `127.0.0.1`. In debuggable builds only (`<debug-overrides>`), it also trusts user-installed CAs. |

To use HTTPS during development, pick one of these:

- **Dev tunnel or reverse proxy with a public certificate.** For example: `devtunnel host -p 5010 --allow-anonymous`, then use the `https://…devtunnels.ms` URL. Nothing to trust on the device.
- **The ASP.NET Core dev certificate over USB.**
  1. Export it with `dotnet dev-certs https --export-path dev-cert.crt --format Pem`.
  2. Install it on the device under *Settings → Security → Encryption & credentials → Install a certificate → CA certificate*.
  3. Run `adb reverse tcp:7277 tcp:7277`, start the backend with the `https` profile, and use `https://localhost:7277`.

  The dev certificate is issued for `localhost` only, so this does not work through `10.0.2.2` or a LAN IP.

## Run

1. Start the backend:
   - **Without Aspire:** run `docker compose up -d`, then `dotnet run --project src/SampleMobile.Web --launch-profile http`.
   - **With Aspire:** `aspire start`.
2. Start an emulator (Android Device Manager, or `emulator -avd <name>`), or connect a device.
3. Deploy and launch the app:

   ```bash
   dotnet build src/SampleMobile.Mobile -t:Run -f net10.0-android
   ```

   Or select the `SampleMobile.Mobile` project and an Android target in Visual Studio or Rider.
4. Sign in with a demo account. The mobile users and their passwords are in `src/SampleMobile.Web/appsettings.Development.json` under `DemoSeed:MobileUsers`.

## Login and registration flow

```text
Login ─► POST /api/mobile/auth/login ─► token in SecureStorage (never Preferences)
      ─► pending sign-out cleanup, if any (unregister old user, reset old FCM token)
      ─► select transport: FCM if google-services.json is built in and Play services work, else inbox-only
      ─► installationId = Preferences["installationId"] ?? new GUID   (stable; never the token)
      ─► request POST_NOTIFICATIONS (Android 13+, once; nothing waits for the answer)
      ─► FCM token (auto-init enabled only now) ─► persisted locally before any network call
      ─► POST /api/mobile/device-registrations { installationId, "android", "fcm", token }
      ─► GET /api/mobile/notifications
```

| Situation | What happens |
|---|---|
| App start with a stored session | The same pipeline runs. The registration is posted once per launch, which also updates the backend's `lastSeenAt`. |
| FCM rotates the token (`onNewToken`) | The new token is persisted, then posted if a user is signed in. This also works in a process FCM started without UI. |
| Same user and token on an ordinary sync | An unchanged fingerprint (user + provider + installation + token) skips the POST unless work is dirty. Login, the first successful app-start sync, and manual refresh force a POST. |
| Network down or 5xx | The latest token is kept and marked dirty. Retries come after 15 s, 1 min and 5 min, on reconnect, on resume and at the next launch. This is not polling. |
| `409` | Retried once, per the contract. |
| `400` | Recorded and not retried. |
| `401` | The session expires; sign in again. |

On **logout**, the app:

1. Persists a cleanup record holding the user's token.
2. Signs out, which clears the token, the inbox, any pending navigation and the app's tray notifications.
3. Calls `DELETE /api/mobile/device-registrations/{installationId}?provider=fcm` with that token.
4. Attempts to reset the FCM token even if DELETE failed transiently. Until cleanup succeeds, offline logout does not guarantee remote revocation or recall of queued messages.

See the [authoritative logout sequence](../../docs/firebase-messaging-flow.md#4-logout-and-offline-cleanup). The saved cleanup JWT is separate from the cleared active session.

If either step fails offline, it stays pending and is retried on reconnect, on resume and at the next launch:

- The DELETE is dropped once the saved token has expired (the default JWT lifetime is 8 h, measured from issuance).
- The FCM token reset is retried until it succeeds; it is the revocation that still works after the token expires.

**Shared device:** the next login always posts the registration, and the backend moves the installation to the new user. A successful registration supersedes any older cleanup for the same installation and provider. An API fetch for another user's notification returns `404` and is ignored. This does not prevent Android from having already displayed background payload text before the fetch.

## Verify incoming notifications

1. Sign in on the device as `alice`. **Home** should show:
   - Push transport: Firebase Cloud Messaging
   - Device registration: Registered
2. In the dashboard (`http://localhost:5010`, admin account from `DemoSeed:Admin`), open **Registered devices**. The Android `fcm` registration appears with its token masked.
3. Open **Compose**, pick Alice, keep the Android registration ticked, and send. **Delivery history** shows *Provider accepted* with an FCM message id. Alternatively, use *Raise DemoAlert*.
4. Check the device:
   - **In the foreground:** a heads-up notification built from the API data, and the item in the inbox.
   - **In the background:** the system tray notification. Tapping it opens the detail page.
5. Open **Diagnostics** (Debug builds) to see the last push event: kind, provider, notification id, type and outcome. It never shows the text, the access token or the full FCM token.

**Inbox-only mode:** sign in with **Inbox-only mode** ticked, or without `google-services.json`. Registered devices then lists a `simulated` registration. Composing to it delivers nothing to the device; pull to refresh to see the notification.

## Android notification lifecycle

The backend sends a `notification` block (title, body) plus `data { notificationId, type, resourceId? }`, at high priority, with Android `tag` = `collapse_key` = notificationId.

| App state | What Android/FCM does | What the app does |
|---|---|---|
| Foreground | Calls `FirebaseMessagingService.OnMessageReceived` and shows nothing itself. | `GET /notifications/{id}`, then updates the inbox and posts a local notification with **API** text, with tag = id. If the API is unreachable it shows a content-free "New notification" and fetches on the next redelivery or refresh. A 404 (not this user's) shows nothing. |
| Background | Shows the tray notification itself, from the `notification` block. **`OnMessageReceived` is not called.** | Nothing until the user taps or returns; on resume the inbox refreshes. |
| Terminated (swiped away, normal close) | Same as background: Android shows the tray notification. | The tap starts the process. `MainActivity.OnCreate` reads `notificationId` from the launch intent extras. The session is restored (or the user signs in), then the detail page fetches the notification from the API. |
| Force-stopped (Settings → Force stop), or device offline | Android does not deliver FCM to force-stopped apps. Offline messages may arrive later within FCM's TTL, subject to collapsing and other delivery limits. | The next launch or refresh loads the newest 50 inbox items from the backend. Push delivery is never guaranteed. |

How duplicate effects are reduced:

Provider-side collapsing is separate from all three mechanisms below. The factory sets `collapse_key`, but Firebase notification messages ignore it; collapsible-message storage also has a four-key limit. See [Firebase collapse semantics](https://firebase.google.com/docs/cloud-messaging/customize-messages/collapsible-message-types).

- **Tray entries:** the local notification uses the same tag (notification id) and id (`0`) as FCM's own tray entry, so a notification shown by the app and one shown by Android replace each other instead of stacking.
- **Repeated deliveries:** the processor tracks in-flight IDs and the most recent 200 handled IDs in memory. The cache resets on session changes and process restart; it is not durable exactly-once processing.
- **Inbox items:** the inbox is keyed by backend id.

How taps are handled:

- FCM's tray taps and the app's own notifications produce the same launch intent: `MainActivity` with the backend data keys as extras.
- `Platforms/Android/Notifications/NotificationTapIntents` reads it in `OnCreate`, on a cold start (only when there is no saved state), or in `OnNewIntent` (app running).
- It ignores intents replayed from Recents (`LaunchedFromHistory`) and removes the extras once handled.
- A missing or malformed id opens the inbox instead.

## When FCM is unavailable

| Situation | Result |
|---|---|
| No `google-services.json` in the build | Transport status `NotConfigured`. The app runs **inbox-only**: it registers as provider `simulated`, and the Home screen says so. |
| No Google Play services (emulator image without Play) | `Unavailable`, which also falls back to inbox-only. |
| Play services present but FCM unreachable (offline, `SERVICE_NOT_AVAILABLE`) | The app stays on FCM. Address retrieval is retried on reconnect or resume, and the previous registration stays valid. |
| `FIS_AUTH_ERROR` / `AUTHENTICATION_FAILED` | A permanent error, shown in Diagnostics. It usually means the `google-services.json` is for another package name or project. |

In every case the following still work:

- sign-in;
- the inbox: list, pull-to-refresh, detail, mark as read;
- notification history, which lives on the backend;
- refresh on launch and on resume.

There is no background polling.

## Adding another push transport

Use OneSignal or another provider as an example. These parts change:

1. **Android adapter:** add `Platforms/Android/Push/<Name>/` with a class implementing `IPushTransport`:
   - `ProviderId` matches the backend's new provider id.
   - Initialize the SDK in `InitializeAsync`.
   - Return the provider's address from `GetRegistrationAddressAsync`.
   - Raise `AddressChanged` on rotation and `MessageReceived` for in-app deliveries.
   - Raise `NotificationOpened` if the SDK reports taps through a callback.
   - Implement `ActivateAsync` / `ResetRegistrationAsync` for sign-in and sign-out.
2. **Registration:** add a `Use<Name>PushTransport()` extension that registers it, and replace the single `UseFirebasePushTransport()` line in `MauiProgram`. Add the provider's NuGet package and any manifest entries.
3. **Backend:** implement `IPushNotificationProvider` in `Infrastructure/Push/<Name>/` and add one line in `AddPushProviders`. See [docs/architecture.md](../../docs/architecture.md#adding-a-provider-eg-apns-or-onesignal).

These do not change:

- the API client, DTOs and session handling;
- the inbox and view models;
- registration sync, logout cleanup and deduplication;
- the local notification presenter and the tap intent handling.

What is honestly provider-specific: SDK initialization, how and when the provider issues and rotates addresses, its callbacks (some SDKs display notifications themselves), its Android manifest requirements, and whether taps arrive as intents or callbacks. The abstraction keeps those inside the adapter; it does not pretend they don't exist.

## Tests

```bash
dotnet test --project tests/SampleMobile.Mobile.Tests
```

These tests use the real Core service graph with an in-memory backend stub at the bottom of the real HTTP pipeline, a fake transport and a fake clock. They cover:

- contract JSON (exact registration body, strict numbers, sample payloads);
- the bearer token and 401 handling;
- API error mapping;
- idempotent and retried registration sync, including racing token rotations;
- logout cleanup (offline, expired token, superseded by a new login);
- push → fetch → inbox → one local notification;
- deduplication by backend id;
- mark as read;
- missing or invalid ids and taps before login;
- inbox-only mode;
- the dependency rule.

## Manual Android test checklist

Use a Google Play emulator or a physical device, the backend running, and `google-services.json` plus the backend service account configured.

| # | Scenario | Steps | Expected |
|---|---|---|---|
| 1 | Foreground | App open on Inbox. Compose to the signed-in user. | Heads-up notification with the API title and body. The item appears at the top of the inbox, unread. One tray entry. |
| 2 | Background | Press Home. Compose. | Android shows the tray notification. Tap it: the app opens on the detail page, which is marked read. |
| 3 | Closed normally | Swipe the app away from Recents. Compose. Tap the notification. | Cold start, session restored, detail page opens. Back goes to Home. |
| 4 | Tap navigation | Send two notifications. Tap the older tray entry. | The detail page of that id, not the latest. |
| 5 | Permission denied | Fresh install on Android 13+, deny the prompt. | Home shows "Blocked". No tray entries, but the inbox still fills on refresh. "Allow notifications" opens app settings. |
| 6 | Device offline | Airplane mode, then compose (backend reports *Provider accepted*). Turn the network back on. | Nothing arrives while offline. After reconnecting, FCM may deliver; TTL is only one constraint. The newest inbox page refreshes on resume either way. |
| 7a | Forced re-registration | Diagnostics → Refresh registration. | A new POST, same installation ID, updated `lastSeenAt`; the token may be unchanged. This does not prove rotation. |
| 7b | Actual token rotation | With a debugger attached, observe a real SDK `OnNewToken` callback and follow address sync; do not clear app data. | A changed address is registered under the same installation ID. If no rotation is observed, leave this check pending. Do not log or copy the full token. |
| 7c | New installation | Record the installation ID, clear app data, then sign in again. | A new installation ID is generated. This tests installation replacement, not same-installation token rotation. |
| 8 | Backend down | Stop the backend, pull to refresh, sign in. | An error banner, not a crash. Registration shows "Waiting to retry". After restarting the backend, resume the app: it registers and refreshes. |
| 9 | Firebase not configured | Remove `google-services.json`, rebuild, sign in. | Home: "Inbox-only (no real push)". Registered devices lists `simulated`. Compose, then pull to refresh shows the notification. |
| 10 | Logout and shared device | Sign out as alice (online), sign in as bob, then compose to alice. | Alice's registration shows *Disabled: unregistered* or moves to bob. A new send should not target Bob through Alice's registration. An old in-flight background payload may still be displayed; fetching Alice's detail as Bob returns `404`. |
| 11 | Offline logout | Airplane mode, sign out, network on, reopen the app. | With an unexpired saved JWT, DELETE disables Alice's registration and token reset completes. If that JWT has expired, DELETE is dropped; verify successful token reset instead of expecting the backend row to be disabled. |

See also the [worked learning exercise](../../docs/firebase-messaging-flow.md#trace-one-notification-end-to-end) and [POC limits](../../docs/firebase-messaging-flow.md#poc-limits-and-operational-boundaries). A permission grant or high-priority send does not guarantee heads-up presentation; Android channel/user settings still apply.

## Troubleshooting

- **`NETSDK1147 … wasm-tools`:** the SDK and workload packs are out of step. Run `dotnet workload update` (elevated) or update Visual Studio.
- **D8 "Type … is defined multiple times":** an AndroidX `-ktx` package fell behind its base package. Align it in `Directory.Packages.props`.
- **"Cleartext HTTP traffic to … not permitted":** use `10.0.2.2`/`localhost` (with `adb reverse`), add your LAN IP to `network_security_config_debug.xml`, or use HTTPS. Release builds never allow cleartext.
- **Login works, then everything returns 401 after a backend restart:** in Development without `Jwt:SigningKey`, tokens die with the process. Sign in again.
- **Transport shows `NotConfigured`:** `google-services.json` is missing from `Platforms/Android/`, or the app wasn't rebuilt after adding it.
- **No notification while the app is in the foreground:** check Home → Notification permission. A 404 for the id (another user's notification) is deliberately ignored.

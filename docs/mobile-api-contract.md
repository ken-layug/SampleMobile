# Mobile API contract (v1)

This is the contract between the SampleMobile backend and the .NET MAUI Android client. It is provider-neutral: the app talks only to these endpoints. Firebase (or any other push provider) only delivers a *hint* that something changed.

The machine-readable version is [`openapi/mobile-v1.json`](openapi/mobile-v1.json). While the backend runs in Development, it is also served at `GET /openapi/v1.json`, with an interactive UI at `/scalar`.

## Principles the client must follow

1. **The inbox API is the source of truth.** The push **data block** carries `notificationId`, `type` and optional `resourceId`. For app-controlled rendering, fetch `GET /api/mobile/notifications/{notificationId}` (or refresh the list) and use the API response. The separate **notification block** contains title/body that Android displays automatically in the background, before API authorization or session checks run.
2. **Pushes can be duplicated, delayed or lost.** Backend send attempts are at-least-once; device delivery is not guaranteed, and a provider "accepting" a message does not guarantee the device shows it. De-duplicate by `notificationId`, and refresh the inbox on app start and on resume.
3. **Read state lives on the server.** Call `POST .../{id}/read` when the user opens a notification. Read state is per user and independent of any push.
4. **Ignore unknown `type` values** rather than failing. New types may be added at any time.

## Base URL

| Where the app runs | Base URL (backend on the dev machine, `http` launch profile) |
|---|---|
| Android emulator | `http://10.0.2.2:5010` |
| Physical device on the same LAN | `http://<dev-machine-LAN-IP>:5010`. Start the backend with `--urls http://0.0.0.0:5010` and allow the port through the firewall. |
| USB-connected Android phone | `http://localhost:5010` after `adb reverse tcp:5010 tcp:5010`. |

The shipped client targets Android only. See the [mobile networking guide](../src/SampleMobile.Mobile/README.md#backend-url-and-networking) for device configuration.

Development serves plain HTTP so that emulators work. Android needs cleartext traffic enabled for these hosts in debug builds (a `network_security_config` that allows `10.0.2.2` and your LAN IP). Every other environment must use HTTPS.

## Conventions

- JSON, UTF-8, `camelCase` property names. Request property names must match exactly; matching is case-sensitive.
- Unknown request properties are ignored. Duplicate properties and numbers sent as strings are rejected (`400`).
- IDs are UUID strings. Timestamps are ISO 8601 (`2026-10-13T10:00:00.123456+00:00`) and are always UTC. Parse them as `DateTimeOffset`.
- `null` values are always serialized, never omitted (for example `"readAt": null`).
- Enum-like strings are camelCase (`android`, `invalidatedByProvider`).
- Errors use RFC 9457 problem details (`application/problem+json`); see [Errors](#errors).
- Every endpoint except login requires `Authorization: Bearer <accessToken>`.

## Authentication

### `POST /api/mobile/auth/login`

Anonymous. Exchanges a user name and password for a bearer token. Development seeds demo accounts; their credentials are in `src/SampleMobile.Web/appsettings.Development.json` under `DemoSeed:MobileUsers`. Production credentials are never hard-coded.

Request:

```json
{
  "userName": "alice",
  "password": "<password>"
}
```

`200 OK`:

```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "tokenType": "Bearer",
  "expiresAt": "2026-10-13T18:00:00+00:00",
  "expiresIn": 28800
}
```

| Status | Meaning |
|---|---|
| `200` | Signed in. |
| `400` | `userName` or `password` is missing (validation problem). |
| `401` | Wrong credentials, unknown user, or the account is temporarily locked (5 failures → 5 minutes). All three return the same response on purpose. |

Token handling:

- The token is a signed JWT whose `sub` is the user id. The client should treat it as opaque; there is no refresh token.
- On any `401` from another endpoint, sign in again. In Development without a configured `Jwt:SigningKey`, tokens also become invalid whenever the backend restarts.
- Store the token in `SecureStorage`, never in preferences.

## Device registrations

A registration ties one **installation** of the app, signed in as one user, to one push **provider** and its provider-specific **address**.

- `installationId`: generate a UUID once per installation (e.g. with `Guid.NewGuid()`) and persist it in `Preferences`. It is not the push token and must not change when the token rotates.
- `provider`: the push provider the address belongs to. Supported ids are listed in the `400` error when an unsupported one is sent:
  - `fcm`: `address` is the **FCM registration token** obtained through `FirebaseMessaging.Instance.GetToken()` in [FirebaseAndroidPushTransport](../src/SampleMobile.Mobile/Platforms/Android/Push/Firebase/FirebaseAndroidPushTransport.cs).
  - `simulated`: for testing without Firebase. `address` can be any string. Nothing reaches the device, but notifications still appear in the inbox.
- `platform`: `android`, `ios` or `virtual`.

### `POST /api/mobile/device-registrations`

Registers or refreshes this installation. It is idempotent per (`installationId`, `provider`). Call it:

- after every successful login,
- on every app start while signed in (it also updates `lastSeenAt`),
- whenever the provider issues a new token (`OnNewToken` / token-refresh callback).

The owner is always the signed-in user, never something in the body. If the same installation was previously registered by a different user (a shared phone), it moves to the caller. If another installation still holds the same address, that stale registration is disabled with `tokenReassigned`.

Request:

```json
{
  "installationId": "0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10",
  "platform": "android",
  "provider": "fcm",
  "address": "fcm-registration-token"
}
```

`201 Created` (new registration; `Location: /api/mobile/device-registrations`) or `200 OK` (existing registration refreshed):

```json
{
  "id": "01a137b6-f940-7e9b-a438-0b5927a6002d",
  "installationId": "0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10",
  "platform": "android",
  "provider": "fcm",
  "enabled": true,
  "disabledReason": null,
  "createdAt": "2026-10-13T10:00:00+00:00",
  "updatedAt": "2026-10-13T10:00:00+00:00",
  "lastSeenAt": "2026-10-13T10:00:00+00:00"
}
```

The provider address is never returned. `disabledReason` is `null` while enabled, otherwise one of:

- `unregistered`: the app called DELETE.
- `invalidatedByProvider`: the provider confirmed the token is dead.
- `tokenReassigned`: another installation now holds the token.

Registering again re-enables the registration.

| Status | Meaning |
|---|---|
| `201` / `200` | Created / refreshed. |
| `400` | Missing field, unknown `platform`, or unsupported `provider`. |
| `401` | Missing or invalid token. |
| `409` | Two registrations for the same installation raced. Retry once. |

### `GET /api/mobile/device-registrations`

Lists the caller's registrations, including disabled ones, newest first. `200 OK` with an array of the objects above.

### `DELETE /api/mobile/device-registrations/{installationId}?provider=fcm`

Disables this installation's registration for future dispatch; it does not recall messages already accepted by FCM. On logout, the client securely persists cleanup with the old JWT before clearing the active session, then attempts this DELETE and provider-token reset. Pending cleanup can retry offline failures without keeping the user signed in. See the [logout sequence](firebase-messaging-flow.md#4-logout-and-offline-cleanup). DELETE is idempotent; the disabled row preserves delivery history.

| Status | Meaning |
|---|---|
| `204` | Unregistered (or already unregistered). |
| `400` | `provider` query parameter missing. |
| `401` | Missing or invalid token. |
| `404` | No such registration **for this user**. Another user's registration looks exactly like a missing one. |

## Notification inbox

### The notification object

```json
{
  "id": "01a137b2-10b3-7eb7-a480-92b0b0848c5c",
  "type": "demo.alert",
  "title": "Warning alert",
  "body": "Disk usage on the demo server crossed 90%.",
  "createdAt": "2026-10-13T10:00:00+00:00",
  "readAt": null,
  "resourceId": "01a137b2-10b3-7eb7-a480-92b0b0848c5c",
  "metadata": {
    "severity": "warning",
    "source": "demo-alert"
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | UUID | Same value as `notificationId` in push data. |
| `type` | string | `demo.alert`, `demo.message`, `system.announcement`, `demo.simulation`; more may appear. |
| `title`, `body` | string | Display text. Title ≤ 200 characters, body ≤ 2000 characters. |
| `createdAt` | timestamp | When the notification was created. |
| `readAt` | timestamp or `null` | When **this user** marked it read. |
| `resourceId` | string or `null` | Optional id of the business object the notification is about (e.g. the alert id). |
| `metadata` | object (string → string) | Optional extra values; may be empty `{}`. |

### `GET /api/mobile/notifications?page=1&pageSize=20&unreadOnly=false`

Returns the caller's notifications, newest first.

| Query | Default | Notes |
|---|---|---|
| `page` | `1` | 1-based. |
| `pageSize` | `20` | 1–100. |
| `unreadOnly` | `false` | Only unread notifications. |

`200 OK`:

```json
{
  "items": [ { "id": "...", "type": "demo.alert", "title": "...", "body": "...", "createdAt": "...", "readAt": null, "resourceId": null, "metadata": {} } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "unreadCount": 3
}
```

`totalCount` counts notifications matching the filter. `unreadCount` is the user's overall unread count (use it for badges). Invalid paging returns `400`.

### `GET /api/mobile/notifications/{notificationId}`

`200 OK` with one notification object. `404` if it doesn't exist **or isn't addressed to the caller**.

### `POST /api/mobile/notifications/{notificationId}/read`

Marks the notification read for the caller. It is idempotent: repeating the call returns the original `readAt`. No request body. `200 OK` with the updated notification object, or `404` as above.

## Push payload (FCM)

The backend sends an FCM message with both a `notification` block (title and body, so Android shows it even when the app is in the background) and a `data` block:

| Data key | Always present | Value |
|---|---|---|
| `notificationId` | yes | UUID of the notification. |
| `type` | yes | Notification type. |
| `resourceId` | no | Present when the notification has one. |

Android specifics:

- High priority.
- Android notification `tag` is `notificationId`, allowing replacement of the matching tray entry. The factory also sets `collapse_key`, but notification messages ignore it; it is not a duplicate-delivery guarantee. See [collapse versus deduplication](firebase-messaging-flow.md#failure-semantics-and-a-practical-debugging-route).
- If `Firebase:AndroidChannelId` is configured, the notification uses that channel. The app must create the channel.

Client handling:

1. In `OnMessageReceived` (foreground) or when the user taps a tray notification (the intent extras carry the same data keys), read `notificationId`.
2. Fetch `GET /api/mobile/notifications/{notificationId}`. On `404`, ignore it: the notification may belong to a user who has since signed out on this device.
3. Show or update the item from the API response, then refresh the unread badge.

The `simulated` provider never reaches a device. It exists so the backend and its delivery history can be exercised without Firebase.

## Recommended client lifecycle

```text
App start
  ├─ installationId = Preferences["installationId"] ?? new UUID (persist it)
  ├─ token in SecureStorage? ──no──► show login
  └─ yes ─► GET /notifications (refresh inbox; on 401 → login)
            └─ POST /device-registrations { installationId, "android", "fcm", fcmToken }

Login succeeded ─► store token ─► POST /device-registrations ─► GET /notifications
FCM token refreshed ─► POST /device-registrations (same installationId, new address)
Push received ─► GET /notifications/{notificationId} ─► render
User opens a notification ─► POST /notifications/{id}/read
Logout ─► persist cleanup with old JWT ─► clear active session
       ─► attempt DELETE with saved JWT and reset provider token
       └─ retain unfinished cleanup for reconnect/resume/next launch
```

## Errors

Errors use RFC 9457 problem details. Validation errors list the offending fields under `errors`, keyed by JSON property name:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "provider": ["Provider 'onesignal' is not supported. Supported providers: fcm, simulated."]
  },
  "traceId": "00-…"
}
```

Other errors:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Notification not found.",
  "traceId": "00-…"
}
```

| Status | When |
|---|---|
| `400` | Validation failed (`errors` present) or the request is malformed. |
| `401` | Missing, expired or invalid bearer token; wrong login credentials. |
| `404` | The resource doesn't exist or isn't the caller's. |
| `409` | Concurrent registration conflict. Retry once. |
| `500` | Unexpected server error. Details stay in the server log; quote `traceId` when reporting it. |

## curl walkthrough

Requires **Bash**, `curl`, and `jq`. Run against the local development backend. For Windows PowerShell, use the equivalent walkthrough below.

```bash
BASE=http://localhost:5010
TOKEN=$(curl -s -X POST $BASE/api/mobile/auth/login -H "Content-Type: application/json" \
  -d '{"userName":"alice","password":"<password from appsettings.Development.json>"}' | jq -r .accessToken)

curl -s -X POST $BASE/api/mobile/device-registrations -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"installationId":"0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10","platform":"android","provider":"simulated","address":"sim-curl-device"}'

curl -s "$BASE/api/mobile/notifications?page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
curl -s -X POST $BASE/api/mobile/notifications/<id>/read -H "Authorization: Bearer $TOKEN"
curl -s -X DELETE "$BASE/api/mobile/device-registrations/0b6f0c7e-2b8f-4f4e-9a37-3c1f6c7f5a10?provider=simulated" \
  -H "Authorization: Bearer $TOKEN"
```

To produce a notification for the device, sign in to the dashboard (`/`) as the demo administrator and use **Compose** or **Raise DemoAlert**.

## PowerShell walkthrough

Run from the repository root with the development backend running. This uses `Invoke-RestMethod` and the development seed password already in the local config; it does not print the JWT or password. The registration is simulated, so no device token is required.

```powershell
$apiBase = 'http://localhost:5010'
$demoConfig = Get-Content ./src/SampleMobile.Web/appsettings.Development.json -Raw | ConvertFrom-Json
$demoUser = $demoConfig.DemoSeed.MobileUsers | Where-Object UserName -eq 'alice' | Select-Object -First 1
$loginBody = @{ userName = $demoUser.UserName; password = $demoUser.Password } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Method Post -Uri "$apiBase/api/mobile/auth/login" -ContentType 'application/json' -Body $loginBody
$authHeaders = @{ Authorization = "Bearer $($loginResult.accessToken)" }
$installationId = [guid]::NewGuid().ToString()
$registrationBody = @{
    installationId = $installationId
    platform = 'virtual'
    provider = 'simulated'
    address = 'sim-powershell-device'
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$apiBase/api/mobile/device-registrations" -Headers $authHeaders -ContentType 'application/json' -Body $registrationBody
```

Compose a uniquely titled notification for Alice in the dashboard, selecting this registration. Then inspect it and verify idempotent read state:

```powershell
$inbox = Invoke-RestMethod -Uri "$apiBase/api/mobile/notifications?page=1&pageSize=20" -Headers $authHeaders
$inbox.items | Select-Object id, title, readAt
$notificationId = Read-Host 'Paste the notification id from the list'
$notificationUrl = "$apiBase/api/mobile/notifications/$notificationId"
Invoke-RestMethod -Uri $notificationUrl -Headers $authHeaders
$firstRead = Invoke-RestMethod -Method Post -Uri "$notificationUrl/read" -Headers $authHeaders
$secondRead = Invoke-RestMethod -Method Post -Uri "$notificationUrl/read" -Headers $authHeaders
$firstRead.readAt -eq $secondRead.readAt  # Expected: True
Invoke-RestMethod -Method Delete -Uri "$apiBase/api/mobile/device-registrations/${installationId}?provider=simulated" -Headers $authHeaders
Remove-Variable authHeaders, loginResult, loginBody, demoUser, demoConfig
```

For the real-FCM learning exercise, reuse only the login and inbox inspection portions; the Android app registers its own FCM address. Do not mark the item read from PowerShell until you have observed the foreground/tap behavior you want to test.

## Out of scope for v1

- Refresh tokens. Sign in again on `401`.
- Delivery receipts from the device. The server only knows that a provider *accepted* a message; read state is the only device-side confirmation.
- Firebase Installation ID (FID) targeting. This repository defines `fcm` as an FCM registration-token address. See the pinned packages in [Directory.Packages.props](../Directory.Packages.props) and the compatibility comments in [FcmMessageFactory](../src/SampleMobile.Infrastructure/Push/Firebase/FcmMessageFactory.cs). A future address-type change requires an explicit provider/contract decision rather than silently reinterpreting existing addresses.
- The API supports paging, but the current Android inbox refresh loads only the first 50 items. See [POC limits](firebase-messaging-flow.md#poc-limits-and-operational-boundaries).

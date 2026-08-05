# Implementation Blueprint: Native mobile shells (Android + iOS)

**Status:** BLUEPRINT — not a spec. ⚠️ Write `spec.md` with numbered ACs before implementing anything past
Phase 0; `features/multi-tenant-cloud/plan.md`'s **R-3** is the standing lesson (no spec ⇒ nothing to verify
against).
**Created:** 2026-08-05
**Approach:** Option 2 — two thin native shells over the server's own origin, the mobile analogue of `desktop/`
**Target topology:** **`HostedMultiTenant`** (`features/multi-tenant-cloud/plan.md`)
**Distribution:** public **App Store + Play Store**
**Depends on:** `multi-tenant-cloud` **US-1** (`DeploymentProfile`) and **US-2** (`ITenantScope`) for Phases 3+.
Phase 0 depends on nothing.
**Branch:** off `main`. ⚠️ Not off `feature/audit-sections-3-to-10`.

---

## Summary

Two ~350-line shells — Kotlin/`WebView` and Swift/`WKWebView` — in a new top-level **`mobile/`**, each loading the
hosted origin (`https://clinics.example.tn`) that Caddy already terminates. The web app is not forked, not
re-implemented, and not statically exported: the shell renders **the server's own bundle**, which is exactly why
`features/mobile-tablet-responsive/spec.md` put `desktop/` out of scope — *"It renders whatever the browser
renders, so it inherits every improvement here and imposes no constraint of its own."*

On top of that: a **3-message bridge** (print · blob download · push token), an **OS push** path, a **client-version
floor**, and the web-side fixes that a webview makes load-bearing.

### `HostedMultiTenant` deletes four of the fourteen gaps outright

Choosing the hosted profile over the LAN one is the single largest simplification available:

| Gap on a LAN install | Under `HostedMultiTenant` |
|---|---|
| Self-signed CA; iOS `.mobileconfig` leaves the root **inert** until the user flips *Certificats de confiance*; **AC-46** (398-day cap) unverified; a **DHCP lease change breaks TLS** | **Gone.** Caddy serves a real CA on a real domain. No pinning, no QR pairing, no `TrustController`, no SAN problem. `ExposesTrustEndpoints` = ✗ |
| Push structurally impossible — FCM/APNs are internet services and `NotificationJob` is `IInternetProbe`-gated for that reason | **Works.** Internet-connected by construction |
| A native Auth0 client + PKCE + `offline_access` + a native redirect URI | **Not needed.** `UsesLocalAccounts` ✓ — `LocalAuthService` mints HS256 tokens and `POST /api/auth/login` already returns `{accessToken, refreshToken, expiresAt, refreshExpiresAt}` **in the body**. The Next BFF is not on a native client's critical path |
| App Review **cannot reach a LAN server** | **Gone.** A reviewer reaches the hosted domain. Public-store distribution becomes viable |

And one convergence worth naming: **`multi-tenant-cloud`'s owed decision #3 *is* this blueprint's Phase 2.** That
plan already records *"the shell has no version check; a cloud API that moves under N pinned clients breaks them
silently."* Phase 2 closes it for **both** shells — desktop and mobile — so it is shared work, not duplicated work.

### One correction to the option analysis, and one trade worth re-examining

**A minimal bridge is unavoidable — I understated this.** Three things cannot work in a bare webview:

1. **Print.** Android `WebView` does not implement `window.print()` at all (needs `PrintManager` +
   `createPrintDocumentAdapter()`); on iOS `WKWebView` it is at best unreliable and needs
   `viewPrintFormatter()` + `UIPrintInteractionController`. The app's two print paths are
   `window.open()` + `window.print()` (`document-editor-content.tsx:1942,1984`).
2. **`blob:` downloads on Android.** `DownloadListener` **never fires for `blob:` URLs**, and Android `WebView`
   does not implement `navigator.share` — so `lib/download.ts`'s coarse path (`canShare` → false →
   `window.open(blob:)`) delivers nothing. All nine correct call sites plus the four hand-rolled ones fail.
3. **Push token handoff** — the token is native-only by definition.

So the invariant is not "no bridge". It is: **the bridge is feature-detected and the web app behaves exactly as it
does in a browser when it is absent.** No fork, no shell-only code path, no second contract to keep in sync — one
`window.__clinicShell` object with a `version` field and three methods, each guarded at the call site.

**And the trade to re-examine:** a fixed hosted domain removes the objection that killed Capacitor. `server.url` is
build-time-fixed, which is fatal for a per-clinic LAN address but **fine for one hosted domain** — and Capacitor
would give the bridge, push, camera and share as plugins written once instead of twice.
**I still recommend hand-written shells**, for two concrete reasons: (a) the repo's bar for a thin client is
~300 lines with **zero** toolchain and a standalone solution deliberately outside `api/ClinicManagement.sln`
(`desktop/CLAUDE.md`) — a Node build inside `mobile/` is a third toolchain to keep alive in a repo with **no CI**;
(b) the bridge surface is three messages, so Capacitor's win is small while its dependency surface is large.
The blueprint keeps Phase 1 self-contained, so swapping in Capacitor changes only Phase 1.

---

## Sequencing

| Phase | Depends on | Ships | Gate |
|---|---|---|---|
| **0 — Web fixes** | nothing | Immediately, on any branch. Improves the browser too | `check:responsive` + `tsc` + `build` + eye pass |
| **1 — The two shells** | Phase 0 | `mobile/` + bridge | Operator-verified on real hardware (R-1 class) |
| **2 — Client-version floor** | nothing | Closes `multi-tenant-cloud` owed decision #3 | Unit + manual |
| **3 — OS push** | `multi-tenant-cloud` US-1 + US-2 | Device registry, outbox, senders | Unit + `verify-schema` |
| **4 — Native capability** | Phase 1 | Camera, biometrics, native viewer | Operator-verified |
| **5 — Store submission** | 1–4 | Two listings | External |

⚠️ **Phase 3 cannot land before `multi-tenant-cloud` US-2.** A new Hangfire job must call `UseSystemWide(...)` or
`SystemWideCallerCoverageTests` fails the build — and if that guard did not exist yet, the job would read **zero
rows and log success**, which is R-1 of that plan.

---

## Phase 0 — Web fixes (no dependency, benefits every mobile browser)

### Files to create

| Path | Purpose |
|---|---|
| `web/public/icon-192.png`, `icon-512.png`, `icon-maskable-512.png`, `apple-icon.png`, `icon-light-32x32.png`, `icon-dark-32x32.png`, `icon.svg` | The assets `layout.tsx:21-37` already declares and that **all 404 today**. Maskable needs the 40 % safe zone or Android crops the logo |
| `web/app/print.css` (or a `@media print` block in `globals.css`) | AC-40. Today the **only** print CSS in the repo is a string injected into `window.open` (`document-editor-content.tsx:2008`), so printing any screen prints the sidebar and the AI launcher |

### Files to modify

| Path | Change |
|---|---|
| `web/app/manifest.ts` | Populate `icons` — the `icons: []` at `:7-16` is deliberate *because the files were missing*; that reason expires here. Add `id`, and keep `display: "standalone"` |
| `web/app/layout.tsx` | Add `appleWebApp: { capable: true, statusBarStyle: "default", title }` to `metadata`; add `themeColor` to the **`viewport`** export (Next 15 reads it there — `manifest.ts`'s `theme_color` alone emits no `<meta>`) |
| `web/components/patient-files-manager.tsx:252-258` · `web/components/factures/invoices-table.tsx:258-265,302-310` · `web/components/document-editor-content.tsx:1840-1847` | **The four hand-rolled `<a download>` blob paths → `downloadBlob()`.** They silently deliver nothing on iOS Safari and on Android WebView. `lib/download.ts` is already correct; these four never learned about it — the repo's `fixes-dont-propagate` shape verbatim |
| `web/components/document-editor-content.tsx:1790` | The `file-saver` `saveAs()` Word export is a **third** delivery mechanism → route through `downloadBlob` too, so there is one answer to « télécharger » |
| `web/app/patients/[id]/page.tsx:2714-2718` · `web/components/patient-files-manager.tsx:741-745` · `web/components/document-editor-content.tsx:3491-3495` | The three `blob:`-src `<iframe>` PDF previews. `#toolbar=0&navpanes=0` are **Adobe/Chromium-only** params, and **Android WebView has no PDF viewer at all** — the iframe renders blank. On a coarse pointer, replace the inline preview with `downloadBlob`'s already-correct share/open path |
| `web/lib/download.ts` | Add `window.__clinicShell?.saveFile(...)` as the **first** branch of `downloadBlob`, before the `canShare` check. Absent shell ⇒ identical behaviour to today |
| `web/components/ai-chat.tsx:190` | `webkitSpeechRecognition` is unsupported on iOS — hide the mic when `!('webkitSpeechRecognition' in window)` rather than shipping a dead button |
| `web/scripts/check-responsive.mjs` | Two new derived checks (below), and delete the stale `PENDING_PARTS = new Set(["P7","P8"])` at `:33` — P8 shipped and nothing is tagged, so it is vestigial |
| `.claude/rules/frontend-web.md` § 15 | It still lists *"No manifest and no real icons"* and *"No LAN device-trust page (P8)"* as open. Both are now wrong |

### The two new mechanical checks

House rule from `check-responsive.mjs` and `LEARNINGS.md`: *derive the surfaces, never hand-maintain an expectation
list, never add a per-file exemption.*

- **`blob-delivery`** — fail any `a.download =` / `saveAs(` / `.click()`-on-anchor outside `web/lib/download.ts`.
  Derived: it greps for the *pattern*, so a fifth site added next year fails on arrival.
- **`pdf-viewer-params`** — fail `#toolbar=0` / `navpanes=0` anywhere. Both are no-ops outside Chromium and their
  presence is the tell that a path assumed a desktop PDF plugin.

Both are greps, so **no FE test runner is needed** — which is what keeps `LEARNINGS.md:225`'s *"standing up a test
runner is a prerequisite"* from being triggered.

---

## Phase 1 — The two shells

### `mobile/` layout (sibling of `desktop/`, own toolchain, in neither solution)

```
mobile/
├── CLAUDE.md                       # the AI map; states "not CI-runnable, operator-verified"
├── README.md                       # build + signing + store steps for an operator
├── shared/
│   └── bridge.md                   # THE contract: 3 messages, versioned. Single source of truth
├── android/                        # Gradle, Kotlin, minSdk 26
│   └── app/src/main/java/tn/clinicmanagement/shell/
│       ├── MainActivity.kt         # the 5 view states
│       ├── ServerConfig.kt         # SharedPreferences; mirrors desktop/ServerConfig.cs
│       ├── ShellBridge.kt          # @JavascriptInterface — 3 methods, nothing else
│       ├── FileChooser.kt          # WebChromeClient.onShowFileChooser
│       └── PushService.kt          # FirebaseMessagingService
└── ios/                            # Xcode, Swift, iOS 16+
    └── ClinicShell/
        ├── ShellView.swift         # WKWebView + the 5 view states
        ├── ServerConfig.swift      # UserDefaults
        ├── ShellBridge.swift       # WKScriptMessageHandler
        └── PushDelegate.swift      # UNUserNotificationCenter + APNs
```

### Five view states — `desktop/MainWindow.xaml`'s four, plus one

`WebView` · `Connecting` · `ServerConfig` · `Unreachable` · **`UpdateRequired`** (new — Phase 2's 426). All French.
Two non-obvious things to carry over from `desktop/MainWindow.xaml.cs`:

- **Navigate, don't assign a source.** `desktop` uses `CoreWebView2.Navigate(url)` rather than `Source =` precisely
  so *Réessayer* re-attempts when the URL is unchanged (`:85-87`). Android: `webView.reload()` vs `loadUrl`. iOS:
  `WKWebView.reload()` vs re-assigning the request.
- **Persist the address, don't bake it in.** One binary fits every deployment because the address is stored, not
  compiled (`packaging/README.md:398`). Even with a single hosted domain, keep it overridable — it is how staging
  and App Review get pointed somewhere real.

### The bridge — exactly three messages

```ts
// injected by the shell before first paint; feature-detected at every call site
interface ClinicShell {
  version: string                       // shell semver — also what Phase 2 sends as X-Client-Version
  platform: 'android' | 'ios'
  saveFile(base64: string, fileName: string, mimeType: string): void
  print(): void
  onPushToken(cb: (token: string, platform: string) => void): void
}
declare global { interface Window { __clinicShell?: ClinicShell } }
```

**Invariant, and it is the whole design:** every call site is `window.__clinicShell?.x() ?? <today's behaviour>`.
No `if (isShell)` fork, no shell-only screen, no capability the browser loses. Adding a fourth message requires
editing `mobile/shared/bridge.md` **and** bumping `version`.

⚠️ `saveFile` takes **base64, not a `blob:` URL** — this is the entire point. Android's `DownloadListener` cannot
see a `blob:`, so the JS side must read it (`FileReader.readAsDataURL`) and hand over bytes. Budget for a size cap
and a French toast above it; a 40 MB radiograph through a `@JavascriptInterface` string will OOM.

### Platform requirements that are not optional

| Platform | Requirement | Consequence if skipped |
|---|---|---|
| Android | `WebChromeClient.onShowFileChooser` | **All 6 `<input type="file">` sites silently do nothing** — including the CSV patient import |
| Android | `WebViewAssetLoader` **not** needed (remote origin), but `setDomStorageEnabled(true)` **is** | The 3 `localStorage` keys + 1 `sessionStorage` key stop working; the sidebar and AI prefs break |
| Android | `mixedContentMode = NEVER_ALLOW` | Weakens TLS for no gain — Caddy is HTTPS-only |
| iOS | `WKWebView` + `allowsBackForwardNavigationGestures = true` | The Android back gesture already has a history entry (`adoption-qa-j/progress.md:339`); iOS should get the swipe |
| iOS | `WKWebsiteDataStore.default()` (persistent, not `nonPersistent`) | The `local_session` cookie is dropped on every launch ⇒ login on every cold start |
| both | Deep links on the app's own host (App Links / Universal Links) | Notification taps cannot open the right screen. The 21 `RealtimeResource` keys already imply the routes |
| both | `env(safe-area-inset-*)` reaches the page | `--bottom-inset` (`globals.css:282`) is already correct and would be wasted; iOS needs `viewportFit=cover`, which `layout.tsx:53-58` already sets |

### The connectivity inversion — a real gap, confirmed

`web/lib/connectivity/connectivity.tsx:57-58`: outside Local mode the provider sets `ONLINE_DEFAULT` and
**never polls**. `GET /api/connectivity` **404s** when not `SelfHostedLan`. So in `HostedMultiTenant` a phone that
loses cellular believes it is online, and the only offline signal left is `ApiError(status: 0)` per call site —
which `LEARNINGS.md:191` already flags as insufficient.

`multi-tenant-cloud` records this as **owed decision #1** and observes the probe is *"inverted for this topology"*.
The mobile-appropriate answer, and the one to propose there rather than invent here:
**client→server reachability judged client-side** (a cheap `HEAD` against the hosted origin, or the shell's own
`NetworkCallback`/`NWPathMonitor` pushed in), keeping *server→internet* egress as the separate Local-only bit it is
today. Two axes, two owners — the same split the existing `ConnectivityState` already models
(`serverReachable` / `internetReachable`), so no new shape is needed.

**Do not** build an offline cache here. `mobile-tablet-responsive/spec.md:458` argued it out, and
`multi-tenant-cloud`'s owed decision #1 adds the decisive reason: **gapless per-year invoice numbering cannot be
reconciled after a partition** without gaps or duplicates, and both are legally significant in Tunisia.

---

## Phase 2 — Client-version floor (closes `multi-tenant-cloud` owed decision #3)

Today skew is invisible **only because the server serves the bundle**. A shipped binary ends that.

### Create

| Path | Purpose |
|---|---|
| `api/ClinicManagement.API/Controllers/MetaController.cs` | `GET /api/meta/client-requirements` → `{ minimumShellVersion, currentShellVersion, storeUrls }`. `[AllowAnonymous]` — a too-old client must be able to ask **before** it can authenticate |
| `api/ClinicManagement.API/Middleware/ClientVersionMiddleware.cs` | Reads `X-Client-Version`; below the floor ⇒ **426** `{ error, code: "client_too_old" }` |
| `api/ClinicManagement.UnitTests/Api/ClientVersionMiddlewareTests.cs` | Absent header ⇒ pass · malformed ⇒ pass · below ⇒ 426 · equal ⇒ pass · above ⇒ pass |

### Modify

- `api/ClinicManagement.API/Program.cs` — register **after** `UseAuthentication` and **before** `MapControllers`,
  beside `LocalAuthEnforcementMiddleware`.
- `api/ClinicManagement.UnitTests/Api/ControllerAuthorizationCoverageTests.cs` — add `Meta.ClientRequirements` to
  **`ExpectedAnonymous`**. That set is asserted **equal in both directions**, so this is mandatory, not optional.
- `packaging/.../appsettings.Production.json` template — `Clients:MinimumShellVersion`. The **operator-owned** file,
  never truncated on upgrade (`InstallConfiguration.cs`).

**Deliberate limits, worth stating in the DTO rather than discovering later:**
- **An absent header passes.** Browsers send none, and they are the majority client. The floor is a UX guard against
  a stale binary, **not** a security control — anyone can strip a header.
- **426, not 401.** A too-old client must not look logged-out; that would send the user to re-enter a password that
  will not help.
- **The floor is a version, not a feature matrix.** Anything finer needs a published contract, which is Phase 5's
  non-goal below.

---

## Phase 3 — OS push (⚠️ requires `multi-tenant-cloud` US-1 + US-2)

### The structural fact, and why it is a `DeploymentProfile` capability

FCM and APNs are internet-only. `NotificationJob` is already `IInternetProbe`-gated for exactly this reason, while
`StockExpiryJob` and `BackupJob` deliberately are **not** (their alerts are in-app, so they must work offline).
Push therefore **cannot** work on `SelfHostedLan` — on Android only a foreground service with a permanent
notification, and on iOS **nothing at all**.

So push is a **named capability**, not a mode check:

```csharp
// Infrastructure/Deployment/DeploymentProfile.cs  — ADD ONE property to US-1's type
public bool SupportsOsPush { get; }   // FCM/APNs reachable + credentials configured
// SelfHostedLan ✗ · HostedMultiTenant ✓ · CloudBrowser ✓
```

⚠️ **Do not write `IsLocalMode(`** anywhere in this phase. US-1's `DeploymentProfileCoverageTests` scans the sources
and asserts the only remaining occurrence is inside `DeploymentProfile.Resolve` itself.

### Create

| Path | Purpose |
|---|---|
| `Domain/Entities/DeviceRegistration.cs` | `AggregateRoot<Guid>`: `ClinicId`, `UserId` (**`string`** — `User.Id` *is* the `sub`), `Platform`, `Token`, `ShellVersion`, `LastSeenAt`, `IsActive`. `static Create()`, `private set`, `Deactivate()`/`Touch()` mutators |
| `Domain/Repositories/IDeviceRegistrationRepository.cs` | `GetActiveForUsersAsync(IEnumerable<string>)` — **batched**, never per-user |
| `Infrastructure/Persistence/Configurations/DeviceRegistrationConfiguration.cs` | Auto-discovered. Unique index on `(Token)`; index on `(ClinicId, UserId, IsActive)` |
| `Infrastructure/Repositories/DeviceRegistrationRepository.cs` | |
| `Domain/Entities/PushDelivery.cs` | The **outbox**. `Pending`/`Sent`/`Failed`/**`Blocked`** — reuse `NotificationStatus`'s four-state shape and `adoption-qa-l` L3's lesson: a row that cannot send must **leave the scan**, or unsendable rows accumulate at the front of an oldest-first `.Take(50)` and starve every later row for ever |
| `Application/Common/Interfaces/IPushSender.cs` | Mirrors `IReminderChannelSender`'s **shape**, cannot *be* one (that takes `phoneE164` + `ResolvedReminderSettings`) |
| `Infrastructure/Services/FcmPushSender.cs`, `ApnsPushSender.cs` | Over a shared `HttpPushSender`, 15 s-bounded, never throws |
| `API/Controllers/PushDevicesController.cs` | `POST /api/push-devices` (register/refresh), `DELETE /api/push-devices/{token}` |
| `API/BackgroundJobs/PushDispatchJob.cs` | Hangfire minutely, `[DisableConcurrentExecution]`, `[AutomaticRetry(3)]`, batch cap, retention purge — `NotificationJob`'s template |
| `Migrations/…AddPushDeviceRegistrations.cs` | Two tables |
| Tests | `DeviceRegistrationTenantIsolationTests` (incl. an **`Unset`-scope** case per US-2's testing strategy), `PushDispatchJobTests`, `PushFanOutTests` |

### Key signatures

```csharp
public enum PushSendOutcome { Sent, TransientFailure, TokenInvalid, NotConfigured }
public sealed record PushSendResult(PushSendOutcome Outcome, string? Error);

public interface IPushSender
{
    DevicePlatform Platform { get; }
    Task<PushSendResult> SendAsync(
        string deviceToken, PushPayload payload, CancellationToken ct = default);
}
```

⚠️ **`TokenInvalid` is a fourth outcome the reminder seam does not have, and it is load-bearing.** FCM/APNs return
`UNREGISTERED` / `410 Gone` when an app is uninstalled. That is **terminal per device, not per message**: the row
must fail *and* `Deactivate()` the registration, or every future notification retries a dead token for ever.

### Wiring

- `Infrastructure/Extensions.cs` — the two senders + the repository, scoped. Credentials resolved from settings via
  `IReminderSecretProtector`, **never** read from `IConfiguration` directly (`IReminderChannelSender`'s doc comment
  states that rule explicitly).
- `API/Program.cs` — register `PushDispatchJob` recurring **only when `profile.SupportsOsPush`**, exactly as
  `NotificationJob`/`EInvoiceOutboxJob` are registered today.
- **Hook point: decorate `INotificationGenerator` in DI.** All 7 `NotificationCategory` values already funnel
  through its 13 methods, and its documented contract — *persists + broadcasts, never throws, logs at Error, never
  fails the core operation* — is exactly what push needs. One decorator reaches every category; touching 13 call
  sites would be the `fixes-dont-propagate` shape.

### Pitfalls

- ⚠️ **`PushDispatchJob` must call `UseSystemWide("push dispatch reads every clinic's queued sends")`** or
  US-2's `SystemWideCallerCoverageTests` fails the build. Without US-2 that guard does not exist and the job
  would read **zero rows and log success** — R-1 of that plan, exactly.
- ⚠️ **`StaffNotification` is deliberately one row per event with zero fan-out** (per-user state lives in
  `NotificationRead`). Push needs one send **per device**. Fan out in the outbox writer, never by changing
  `StaffNotification`.
- ⚠️ **`ActorUserId` is excluded from their own feed.** The push fan-out must honour that, or the dentist who
  cancels an appointment gets buzzed about their own action.
- ⚠️ **A push payload must carry no PHI.** The hub already carries only a resource key, never content
  (`ClinicHub.cs:19-22`). A lock-screen banner is visible to anyone holding the phone: send
  « Nouveau rendez-vous » + a deep link, never a patient name. This is the one place where following the existing
  pattern is also the compliance answer.
- ⚠️ `PushDevicesController` **404s when `!SupportsOsPush`**, matching `AuthController`'s and `TrustController`'s
  capability 404s. `ControllerAuthorizationCoverageTests` demands a named policy on every action: **`AnyClinicRole`**
  (a secretary's phone must register).
- ⚠️ **`restore-backup` bumps every `User.TokenVersion`.** Sessions die; **device registrations must not**, or a
  restore silently unsubscribes the whole clinic.
- ⚠️ **The signing key is per install** (`LocalAuthConfig`). A device paired with staging holds tokens invalid in
  production — key stored credentials by server identity.

---

## Phase 4 — Native capability (also Apple 4.2.2's answer)

| Capability | Why it earns its place |
|---|---|
| **Camera capture** | The phone becomes the intra-oral capture device — a **different and better** answer to `adoption-qa-l` **L7**'s imaging bridge, which is currently blocked on *"a browser cannot launch a local process — this must go through the desktop shell or a small local helper"*. Needs no bridge message: Android's `onShowFileChooser` and iOS's native picker both offer the camera for `<input type="file" accept="image/*">`, which `patient-files-manager.tsx:477` already renders |
| **Biometric unlock** | The strongest mitigation for the **30-min inactivity logout** (`session.tsx:107,181-229`) — Face/Touch ID to resume instead of retyping a password at the chair with gloves |
| **Native PDF viewer** | Android WebView has none. Hand the `saveFile` bytes to `PdfRenderer` / `QLPreviewController` |
| **Native print** | Bridge message 2 |
| **Deep links** | Notification tap → the right screen |

⚠️ **Refresh rotation is the one backend change this phase should force.** `RefreshTokenCommandHandler` returns
`RefreshToken = string.Empty` and `RefreshExpiresAt = null` — so the 12 h refresh token never rotates and the user
**re-enters their password every 12 h**. On a phone that is the difference between "the app" and "that website".
`GenerateRefreshToken` already exists and both DTO fields are already read by the BFF, so rotation is contained:
mint a new refresh token on each exchange and populate them. Trade to state out loud: a rotated token invalidates
its predecessor, so a response lost in flight logs the user out — bounded here because `client.ts`'s
`inFlightToken` makes the exchange single-flight, and `token_version` still provides global revocation. Do **not**
instead extend the lifetime to 30 days: a long-lived non-rotating token in a webview cookie is a worse posture.

---

## Phase 5 — Store submission

| Item | Detail |
|---|---|
| **Apple 4.2.2** | A pure webview wrapper gets rejected. The credible answer is Phase 3 + 4: push, camera capture, biometrics, native print/share. Submit *after* them, not before |
| **Health data** | Privacy manifest + data-collection disclosures on both stores. A hosted deployment **does** collect PHI, so answer honestly; also the **INPDP declaration (loi 2004-63)** in `multi-tenant-cloud`'s owed decision #5 |
| **Review access** | A reviewer needs a working tenant: a demo clinic on the hosted domain with seeded data, credentials in the review notes, and `must_change_password` **off** for that account (or review dead-ends on the forced-change screen) |
| **Build infrastructure** | ⚠️ **iOS requires macOS.** This repo is win32 with **no CI**. Either a Mac or Xcode Cloud / Codemagic, plus Apple Developer $99/yr. This is the hardest practical constraint in the whole blueprint and it is not solvable in-repo |
| **Versioning** | `mobile/` has its own version, and Phase 2's floor reads it. Note that `packaging/` already duplicates a hardcoded `1.0.0` across **three** files with no coupling — do not make it four; have the shells read one source |

---

## Test strategy

Backend guards that **will** fight this — each is derived and fails on arrival, not on a list someone forgot:

| Guard | What it demands |
|---|---|
| `ControllerAuthorizationCoverageTests` | A named policy on **every** action, no exemption list; `Meta.ClientRequirements` added to `ExpectedAnonymous` (**equality in both directions**) |
| `SystemWideCallerCoverageTests` (US-2) | `PushDispatchJob` contains a `UseSystemWide` call |
| `DeploymentProfileCoverageTests` (US-1) | No new `IsLocalMode(` anywhere |
| `*TenantIsolationTests` | A new one for `DeviceRegistration`, incl. the `Unset`-scope case |
| `verify-schema` | Run **before and after** the Phase 3 migration and diff. It is the only migration gate — no test touches a DB |
| `reconcile-money` | Before/after, diffed. Nothing here is financial, so **the diff must be empty** — that is the assertion |

New unit tests: `ClientVersionMiddlewareTests`, `PushDispatchJobTests` (starvation: a `Blocked` row must leave the
scan), `PushFanOutTests` (actor excluded; one row per device; `TokenInvalid` deactivates), `DeploymentProfileTests`
extended for `SupportsOsPush`.

Web gate, unchanged and load-bearing: `npm run check:responsive` (+2 new checks) · `npx tsc --noEmit` ·
`npm run build` · a recorded eye pass at **320 / 390 / 820 / 1180 / 1440** plus landscape and a keyboard.

**Operator-verified, not CI-runnable** (R-1 class, as `desktop/CLAUDE.md` states for the WPF shell):
both shells on **physical** hardware — and note the repo has **never had an iOS device**
(`mobile-tablet-responsive/plan.md:592`, AC-46). Minimum walk: cold-start login · 12 h refresh boundary ·
background → push tap → correct screen · all 6 file inputs incl. camera · every download path · print ·
the four recovery states + `UpdateRequired` · rotation and Split View **without a remount** (crossing 768 px must
not remount — `.claude/rules/frontend-web.md` § 5).

⚠️ **Smart App Control** blocks the test runner on this machine (`0x800711C7`): build to a scratch
`-p:BaseOutputPath` and run `dotnet vstest`; a red run is not evidence until `bin/`+`obj/` are cleared and
`dotnet build-server shutdown` has run.

---

## Deliberately out of scope (each argued, not merely listed)

1. **Offline operation and any client cache.** `mobile-tablet-responsive/spec.md:458` argued it, and
   `multi-tenant-cloud` owed decision #1 supplies the decisive reason: **gapless per-year invoice numbering cannot
   be reconciled after a partition** — gaps and duplicates are both legally significant. Online-only, and the app
   should **say so** rather than imply otherwise.
2. **A published/generated API contract.** Swagger is Development-only with **no `AddSecurityDefinition`**, there is
   no versioning, and the success envelope is `Result<T>`-wrapped on some endpoints and a bare DTO on others with
   **no marker**. A webview client needs none of this — but a *native* client (Option 4) needs all of it, so this
   is the prerequisite that decides whether one is ever viable. Enabling `UseSwagger()` outside Development
   (loopback-only, like `/hangfire`) plus one `AddSecurityDefinition` call is a very small change with a large
   payoff and is worth doing on its own merits.
3. **`SelfHostedLan` mobile support.** It technically works today via the shipped trust page, but it carries every
   gap `HostedMultiTenant` deletes — the inert iOS root, AC-46, DHCP/SAN — **and cannot have push at all**. Point
   LAN clinics at the responsive web app; `packaging/README.md:444`'s existing fallback already says
   *"Local-mode phone support falls back to Cloud."*
4. **mDNS / Zeroconf discovery.** There is one hosted domain; there is nothing to discover.
5. **Auth0 native (PKCE) support.** `HostedMultiTenant` uses `UsesLocalAccounts`. Auth0 stays supported for
   `CloudBrowser` in a browser, and the backend validates by Authority + Audience so it needs no change if a native
   Auth0 client is ever added.
6. **A pre-existing bug this blueprint does not fix but should be filed:** `CertificateProvisioner.TryLoadExisting`
   never checks whether the stored leaf's SANs still cover `LanAddresses.IPv4()`, so a **DHCP lease change reuses a
   certificate that does not cover the new address**. Irrelevant under `HostedMultiTenant` (Caddy), which is exactly
   why it will keep hiding — worth a `/capture-followup`.

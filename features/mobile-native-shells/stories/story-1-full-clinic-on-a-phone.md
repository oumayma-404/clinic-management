# Story 1: Full — The clinic works from a phone

**Status:** APPROVED
**Story Status:** in-progress — **Parts 1 and 2 implemented** (2026-08-05); Parts 3–8 not started. Live part status
and the per-part session log are in [`progress.md`](./progress.md).
**Layer:** Full ⚠️ *(deliberate departure from the single-layer rule — see Notes)*
**Depends On:** None
**Blocks:** None

## Objective

A dentist and their assistant(e) can install the clinic app from the App Store or Google Play, sign in with their
existing account, and stay signed in through a working day — receiving a notification on a locked phone when an
appointment moves, photographing a tooth straight into the patient's file, resuming with a fingerprint, and printing
and downloading documents that actually arrive — while the same web bundle keeps behaving **exactly** as it does in a
browser, with no fork and no shell-only code path.

The work runs in **eight ordered parts**. The first three ship real value with no phone in existence: Part 1 fixes
four defects live in mobile browsers today, and Part 2 removes the twice-a-day password prompt for every existing
user. Three later parts cannot be executed on this machine and are gated explicitly rather than half-built.

## Acceptance Criteria

_From spec (`../spec.md`) — all 77, grouped by the part that satisfies them:_

**Part 1 — Phase 0, web fixes** — *implemented 2026-08-05*
- [x] AC-1 · AC-3 — all seven assets exist, generated from one SVG master, byte-identical across runs; `themeColor` moved to the **`viewport`** export and `appleWebApp` added
- [~] AC-2 — the manifest lists the 192/512/maskable trio (two entries, never `"any maskable"` on one file). **The install-tile half needs a device** — owed
- [~] AC-4 — all five bypassing call sites now use `downloadBlob`. **The "delivers a file on iOS Safari" half needs an iPhone** — owed
- [x] AC-5 — `blob-delivery`, derived from the three mechanisms (`.download =` · `saveAs(` · `createElement("a")`), no exemption list, **proved to fail** on a probe
- [x] AC-6 · AC-7 — the fragment is gone from both sites; a coarse pointer gets the file through one shared component, measured at a true 320 px
- [~] AC-9 — the print rules are verified by rendering (chrome gone, dark-on-white, cage released, 4-page PDF). **The three named surfaces and a printer** — owed
- [x] AC-10 — `PENDING_PARTS` deleted, § 15 rewritten. ⚠️ All three of its claims were stale: P8's LAN trust page **has landed** (`TrustController`, commit `3ca0b17`)
- [x] AC-11 — **already satisfied before this part; no change made.** See finding F-1
- [x] AC-12 — 13/13 checks · `tsc` 0 errors · build exit 0 with the **same single** pre-existing `node_modules` warning
- [x] AC-69 — recorded in `mobile-tablet-responsive/spec.md` as a dated amendment that supersedes rather than reverses

*`[~]` = the code half is done and gated; a physical-device half is recorded as owed in `progress.md`, not claimed.*

**Part 2 — Phase 2b, session continuity** — *implemented 2026-08-05*
- [x] AC-35 — the handler mints a fresh credential + expiry, and `bff/auth/token` re-sets the cookie through the one shared writer. **The browser half needs a running stack** — owed
- [x] AC-36 — all four guards pinned: bumped `TokenVersion` refuses · deactivated refuses · non-local refuses · `mustChangePassword` **surfaces**; every refusal shares one message
- [~] AC-37 — the mechanism is in place and tested; **the felt behaviour (active all day vs. idle past the window) needs a running stack** — owed
- [~] AC-38 — the response body is unchanged, so a client ignoring both fields is unaffected. **The desktop shell was not exercised** — owed
- [x] AC-39 — asserted **positively**: the same credential exchanges twice, both succeed, each mints its own. The property is recorded in the handler's doc comment

*`[~]` = the code half is done; a running-stack half is recorded as owed in `progress.md`, not claimed.*

**Part 3 — Phase 2, client version floor**
- [ ] AC-28 · AC-29 — the floor is readable anonymously, and that route is exempt from the floor itself
- [ ] AC-30 · AC-31 · AC-32 — 426 + `code` on every other route; the web client attaches the header from the bridge; absent/malformed accepted unchanged
- [ ] AC-33 — a below-floor shell shows `UpdateRequired` and does **not** present itself as signed out, on both the launch and mid-session paths
- [ ] AC-34 — the floor is operator-owned configuration, effective without a code change
- [ ] AC-70 · AC-71 — no new mode check; every new action carries an explicit named policy

**Parts 4 & 5 — Phase 1, the shells**
- [ ] AC-13 … AC-27 — installable artifact · session survives a cold start · the five French states · « Réessayer » on an unchanged address · a persisted, changeable address · six file inputs + camera · every delivery path in-shell incl. the size refusal · OS print · safe-area insets · no remount on rotation/Split View · back and swipe-back · off-origin navigation leaves the webview · bridge absent ⇒ browser behaviour · the contract document agrees with the shell
- [ ] AC-74 — a newer shell against an older server degrades like a browser
- [ ] AC-76 — the forced-password-change path is completable inside the shell

**Part 6 — Phase 3, OS push**
- [ ] AC-40 … AC-55 — registration incl. **rebind** on a shared device and per-platform refusal · push for the five time-critical categories only · actor excluded · audience equals the feed's · quiet hours · no identifying payload · deep-link tap · invalid token deactivates · a `Blocked` row leaves the scan · per-platform availability stated · tenant isolation · survives a restore · never fails the causing operation
- [ ] AC-70 … AC-73 — no mode check · named policies · the job declares its cross-clinic read · `verify-schema` diffed and `reconcile-money` **empty**
- [ ] AC-75 — permission declined or revoked leaves the app fully working, with no false claim that push is active

**Part 7 — Phase 4, native capability**
- [ ] AC-8 — the official forms (BS1, arrêt de travail) keep a working preview and Print, or say why in French
- [ ] AC-56 … AC-64 — camera into the record · biometric resume without a password, cookie intact · no stored password · graceful fallback with no biometrics · a working PDF viewer on Android · reachability on a mobile network, with an absent egress signal read as absent
- [ ] AC-77 — an upload interrupted by backgrounding never leaves a truncated attachment

**Part 8 — Phase 5, stores**
- [ ] AC-65 … AC-68 — both listings live and installable · health-data declarations · a reachable seeded demo tenant not blocked by a forced password change · one version source

_Story-specific:_

- [ ] Every part boundary is a **separate commit**, so the story is resumable at a boundary (R-1)
- [ ] Each blocked part is **left unstarted with its reason recorded**, never partially implemented
- [ ] `progress.md` records the eye pass (320 / 390 / 820 / 1180 / 1440 + landscape + keyboard) for every part touching `web/`

## Entry Criteria

Before starting this story, ensure:

- [ ] Working on a branch off **`main`** — ⚠️ **not** off `feature/audit-sections-3-to-10`, which carries unrelated in-flight work
- [ ] The working tree is understood before staging: it carries **25+ modified files** from other work, so run `git diff HEAD --numstat` before any `git add` or the commit swallows in-flight changes
- [ ] `docker compose up -d` (postgres + minio), the API and `web` run locally
- [ ] Baseline recorded **before the first edit**: `npm run check:responsive`, `npx tsc --noEmit`, `npm run build` — AC-12 compares the warning count against it
- [ ] A physical iPhone with Safari is reachable for Part 1's AC-4 (the criterion that fails today cannot be verified in a desktop browser)
- [ ] Read `../plan.md` — this file is the execution view; the plan carries the reasoning and the deviations
- [ ] ⚠️ **Re-check the two `multi-tenant-cloud` dependencies before Part 6** — they move independently of this
      feature. As of 2026-08-05, verified in code: **US-1 has landed** (`DeploymentProfile.cs`, 13 capabilities, both
      guards live) and **US-2 has not** (no `ITenantScope`, no `SystemWideCallerCoverageTests`)

## Steps

### Part 1 — Phase 0: the web fixes a webview makes load-bearing ✅ EXECUTABLE

1. **Produce the icon assets**
   - Draw `web/branding/icon.svg` — a simple geometric mark on the app's own tokens (accent on the `#fdfdfe`
     background `manifest.ts` already declares)
   - Write `web/scripts/generate-icons.py` (PIL 12.3 is present; ImageMagick is **not** — the `convert` on PATH is
     the Windows FAT utility). Deterministic resampling, so a re-run produces no diff (R-9)
   - Emit all seven assets into `web/public/`; `apple-icon.png` at 180 px with **no alpha**; maskable with the 40 %
     safe zone
2. **Make the app installable**
   - `web/app/manifest.ts` — populate `icons`, add `id`, delete the now-false "deliberately empty" comment
   - `web/app/layout.tsx` — `appleWebApp` on `metadata`; `themeColor` in the **`viewport`** export (`:53-58`)
3. **One way to deliver a file**
   - `web/lib/download.ts` — shell `saveFile` branch **first** (before `canShare`), and the **25 MB** refusal
     *before* `URL.createObjectURL`, with a French message naming the limit. Guard `window`. ⚠️ Shell path only — a
     browser has no base64 marshalling to run out of memory on
   - Route all five bypassing sites through `downloadBlob`: `patient-files-manager.tsx:252-258`,
     `invoices-table.tsx:258-265` and `:302-310`, `document-editor-content.tsx:1840-1847`, and the `file-saver`
     `saveAs()` at `:1790`
4. **PDF previews that work without a plugin**
   - Replace the two viewer-fragment iframes with coarse-pointer delivery:
     `patients/[id]/page.tsx:2714-2718`, `patient-files-manager.tsx:741-745`
   - ⚠️ **Do not touch** `document-editor-content.tsx:3491` — the official-form preview carries no fragment and *is*
     the print path (`contentWindow.print()` at `:1942`). Part 7 owns it
5. **Printing prints the document**
   - Add a `@media print` block to `globals.css`: suppress the rail, bottom bar, AI launcher and toaster; release
     `dvh` caps; force a light surface
6. **No inert controls, no stale claims**
   - `ai-chat.tsx:190` — render the mic only when `'webkitSpeechRecognition' in window`
   - `check-responsive.mjs` — delete `PENDING_PARTS` (`:33`); add the `blob-delivery` and `pdf-viewer-params` derived
     checks (greps, no exemption list)
   - Rewrite `.claude/rules/frontend-web.md` § 15; record the non-goal amendment in
     `features/mobile-tablet-responsive/spec.md`
7. **Gate, then commit Part 1**

### Part 2 — Phase 2b: the session lasts the working day ✅ EXECUTABLE

1. **Issue a fresh credential on every exchange**
   - `RefreshTokenCommandHandler` — call the existing `_localAuthService.GenerateRefreshToken(user)` and populate
     `RefreshToken` / `RefreshExpiresAt` (`string.Empty` / `null` today at `:72-86`)
   - Leave every guard intact: audience validation, `TokenVersion` equality, `IsActive`, `MustChangePassword`
     surfaced rather than refused
2. **Store it — the half that was missing**
   - Extract the cookie write out of `bff/auth/local-login/route.ts` into one shared helper; `Secure` from the
     explicit config flag, **never** re-derived from `NODE_ENV` or the internal proxy leg (R-8)
   - `bff/auth/token/route.ts` — re-set `SESSION_COOKIE` through that helper on success. Keep 401-clears and
     429/503-leave-alone exactly as they are
3. **Pin the guards and the property**
   - `RefreshTokenCommandHandlerTests`: fresh credential and expiry · bumped `TokenVersion` refuses · inactive
     refuses · `mustChangePassword` surfaces · one indistinguishable message
   - Record in the handler's doc comment: **sliding expiry, not revoking rotation** — a superseded credential stays
     valid until its own expiry because it is stateless, and `TokenVersion` is the only revocation
     (LEARNINGS `:61`, at the call site)
4. **Gate, then commit Part 2**

### Part 3 — Phase 2: a stale app says so ✅ EXECUTABLE

1. **Publish the floor**
   - `MetaController` + `GET /api/meta/client-requirements` (`[AllowAnonymous]`), reading floor, current release and
     both store URLs from operator configuration
   - Add `Meta.ClientRequirements` to `ExpectedAnonymous` (`ControllerAuthorizationCoverageTests:39-53`) — asserted
     equal **in both directions**, so mandatory
2. **Refuse a stale client**
   - `ClientVersionMiddleware` — absent/malformed ⇒ pass; below the floor ⇒ **426** with the canonical `{ error }`
     body plus `code: "client_too_old"`; **exempt the meta route itself**
   - Register in `Program.cs` after `ExceptionMiddleware` (`:545`) and **before** `UseAuthentication` (`:547`) — so a
     stale client's login 426s rather than 401ing (AC-33). ⚠️ Deviation from the blueprint, reasoned in the plan
   - `ClientVersionMiddlewareTests`: absent · malformed · below · equal · above · meta-route-exempt
3. **Make the header true everywhere**
   - `client.ts` — fold `createHeaders`/`formDataHeaders` (`:395-410`) into one exported `apiHeaders(token, kind)`
     adding `X-Client-Version` from `window.__clinicShell?.version`, and nothing when the bridge is absent
   - Replace the headers object at all **14** raw-`fetch` sites in `billing.ts`, `clinics.ts`, `doctors.ts`,
     `export.ts`, `invoices.ts`, `medical-documents.ts`, `patient-files.ts`, `treatment-plans.ts`.
     ⚠️ **Headers only — response handling untouched** (R-5)
   - Add the `api-headers` derived check: any `Authorization.*Bearer` literal outside `client.ts` fails the gate
4. **Surface it to the user**
   - `client.ts` exposes a 426 hook; `<ClientVersionGate>` in `layout.tsx` renders the full-screen French update
     state with the store link — and works in a plain browser
   - Add `Clients:MinimumShellVersion` + `Clients:StoreUrls` to the operator-owned `appsettings.Production.json`
     template
5. **Gate, then commit Part 3**

### Part 4 — Phase 1: the Android shell ⚠️ EXECUTABLE, subject to R-12

1. **Check the tooling first — do not assume**
   - Verify a JDK and the Android SDK are present. If absent, **stop**: Part 4 becomes blocked on tooling like
     Part 5, and Parts 1–3 still ship
2. **Carve out `mobile/`**
   - Sibling of `desktop/`, own toolchain, in neither `api/ClinicManagement.sln` nor `web/`
   - `CLAUDE.md` (state "not CI-runnable, operator-verified"), `README.md`, and `shared/bridge.md` as the single
     contract carrying the per-phase method set and the version rule
3. **The address and the five states**
   - `ServerConfig.kt` — `SharedPreferences`, porting `desktop/ServerConfigStore.ParseAddress` (bare host ·
     `host:port` · full URL). Never compiled in
   - `MainActivity.kt` — the five French states with « Réessayer », « Changer de serveur… », « Recharger ». Use
     `webView.reload()`, never a re-assigned URL, or « Réessayer » does nothing on an unchanged address
     (`desktop/MainWindow.xaml.cs:85-87`)
   - Read the floor over **native HTTP before creating the webview**; below it, show `UpdateRequired` and never load
     the app
4. **Configure the webview so the app actually works**
   - `setDomStorageEnabled(true)` (else the three `localStorage` keys and the sessionStorage key die), persistent
     cookies, `mixedContentMode = NEVER_ALLOW`
   - Declare the configuration changes the activity handles so **rotation does not recreate it** — without this
     AC-23 is unachievable no matter what the web app does
   - `FileChooser.kt` — `WebChromeClient.onShowFileChooser` with the camera for `accept="image/*"`. Without it all
     six file inputs silently do nothing, including the CSV import
5. **Let the platform have what it refuses to give the webview**
   - `ExternalNavigation.kt` — off-origin top-level navigations → Custom Tabs, returning to the webview when the
     callback redirects back to the origin. Verify with « Connecter Google Agenda », which **Google refuses to serve
     in a webview**
6. **The bridge — Phase 1's three methods**
   - `ShellBridge.kt`: `saveFile(base64, name, mime)` (write + offer open/share), `print()` via `PrintManager` +
     `createPrintDocumentAdapter()` (Android has no `window.print()`), `onPushToken(cb)` (registered, inert until
     Part 6). Inject `version` and `platform` before first paint
   - Route the forced-password-change 403 (`code: "must_change_password"`) to the change-password screen
7. **Operator-verify on physical hardware, then commit Part 4**

### Part 5 — Phase 1: the iOS shell 🔒 BLOCKED

> **Do not start.** Unblocked by macOS + Xcode (or Xcode Cloud / Codemagic) **and** an Apple Developer Program
> membership. Not solvable in this repo: win32, no CI, and per `mobile-tablet-responsive/plan.md:592` the project
> **has never had an iOS device**. ⚠️ Do not mark done on a simulator — it does not faithfully exercise persistent
> cookies, print or biometrics.

1. Mirror Part 4 in Swift, with four non-optional platform specifics:
   - `WKWebView` with **`WKWebsiteDataStore.default()`** (persistent, never `nonPersistent`), or the `local_session`
     cookie dies on every cold start and the user logs in every launch
   - `allowsBackForwardNavigationGestures = true`
   - `ShellBridge.swift` as a `WKScriptMessageHandler`; print via `viewPrintFormatter()` +
     `UIPrintInteractionController` — ⚠️ **verify on hardware**, `window.print()` is unreliable in `WKWebView`
   - `ExternalNavigation.swift` via `ASWebAuthenticationSession`
2. File inputs need **no** bridge work — `WKWebView` offers camera, library and Files natively
3. Run Part 4's whole checklist on a physical iPhone **and** an iPad (Split View crossing 768 px is part of AC-23)

### Part 6 — Phase 3: a backgrounded phone still knows 🔒 BLOCKED

> **Do not start.** Unblocked by `features/multi-tenant-cloud` **US-2** (`ITenantScope`) merged.
> ⚠️ **US-1 has already landed** — `Infrastructure/Deployment/DeploymentProfile.cs` exists with 13 capabilities and
> both its guards are live — so US-2 is the only remaining dependency. Without it the dispatch job reads **zero rows
> and logs success** (R-1 of that plan). Read `ITenantScope` before executing (R-3).

1. **Per-platform capability** — add a **14th** capability, `SupportsOsPush(DevicePlatform)`, to the **existing**
   `DeploymentProfile` (FCM for Android, APNs for iOS, each = the kind permits it **and** credentials are present).
   Not one boolean. Add it to `For(kind)`'s matrix for all three kinds and extend `DeploymentProfileTests`.
   ⚠️ This is the **first** capability partly derived from configuration rather than from `Kind` alone — the shape
   LEARNINGS `:45` warns about. Keep the `Kind` half in the matrix so `SelfHostedLan` is ✗ **whatever** an operator
   configures (R-4). Never write `IsLocalMode(` — the coverage guard **fails the build today**
2. **The registry and the outbox** — `DeviceRegistration` (clinic, `UserId` as **`string`**, platform, token, shell
   version, last-seen, active; unique on the token, which is what makes rebinding one deterministic write) and
   `PushDelivery` (+ **`SendNotBefore`**, four-state status with a non-terminal **`Blocked`** mirroring
   `NotificationStatus` — a row that cannot send must **leave the scan** or it starves every later row)
3. **Registration** — `PushDevicesController` (`AnyClinicRole`): register · refresh · **rebind** a token bound to
   another user, deactivating the prior binding, **never a 409** · refuse an unsupported platform with a French
   reason rather than queueing · `DELETE {token}` · the controller 404s where neither platform is supported
4. **Senders** — `IPushSender` + `FcmPushSender`/`ApnsPushSender` over a shared `HttpPushSender`: 15 s-bounded, never
   throws. **`TokenInvalid` is load-bearing** — `UNREGISTERED`/`410 Gone` is terminal *per device*, so the row fails
   **and** `Deactivate()`s the registration. Credentials via `IReminderSecretProtector`, never `IConfiguration`
5. **Fan out by decorating `INotificationGenerator`** — one hook reaches all seven categories; editing its 13 methods
   would be the `fixes-dont-propagate` shape. Audience = the in-app feed's **minus `ActorUserId`**, asserted against
   the feed. Payload = category + fixed French label + appointment id, nothing else. Five time-critical categories
   only. Quiet hours by setting `SendNotBefore`, not by re-testing the clock each scan
6. **`PushDispatchJob`** on `NotificationJob`'s template: `[DisableConcurrentExecution]`, `[AutomaticRetry(3)]`,
   bounded batch, retention that never deletes a `Pending` row, and
   **`UseSystemWide("push dispatch reads every clinic's queued sends")`** or US-2's guard fails the build. Re-check
   recipient eligibility **at dispatch** — an OS banner bypasses every request-time check. Register only when the
   profile supports push
7. **Migration** (two tables, no existing table changes, no backfill) + `PushFanOutTests`, `PushDispatchJobTests`,
   `DeviceRegistrationTenantIsolationTests` (incl. the **`Unset`-scope** case)

### Part 7 — Phase 4: the phone becomes an instrument ⚠️ PARTLY BLOCKED

> Web and Android halves executable after Part 4. The iOS halves need Part 5; the deep-link tap needs Part 6.

1. **Camera** — free from Part 4's `onShowFileChooser`; `patient-files-manager.tsx:477` already renders
   `accept="image/*"`. Verify the photo attaches with the **same validation as an uploaded file**
2. **Biometric resume** — amend `mobile/shared/bridge.md` and **bump the version** for the biometric method (FR-6's
   per-phase set). In `session.tsx`'s Local inactivity path (`:181-229`), when the bridge is present **await the
   shell's result instead of calling `logout()`**; three failures fall back to the password; the cookie is **never
   cleared on success**; no password on the device. Absent bridge ⇒ byte-identical to today
3. **Native viewer** — Android `PdfRenderer`, iOS `QLPreviewController`, reached through `saveFile`'s open path. This
   is also what makes the **official forms** printable in a shell: route the BS1 / arrêt-de-travail preview and
   « Imprimer » through the native viewer and OS print service; where neither exists, a French message and the file.
   A blank frame beside an inert « Imprimer » fails (AC-8)
4. **Deep links** — App Links / Universal Links on the app's own host. ⚠️ **Build the destination first:** there is
   **no `/appointments/[id]` route** and the notification panel navigates nowhere, though
   `AppointmentsController.GET/{id}` is documented as the deep-link target. Add `?focus=<id>` to `/appointments` —
   the agenda opens on that day with the appointment selected. A new dynamic route is deliberately avoided (R-7)
5. **Reachability, two axes** — client→server judged client-side on **every** deployment; server→internet egress only
   where the probe exists and **absent, never inferred false**, elsewhere.
   ⚠️ **The API half is already correct and the bug is live.** `ConnectivityController:40` gates on
   `ExposesTrustEndpoints`, which is **`false` for `HostedMultiTenant`**, so the probe **404s** there — while
   `connectivity.tsx:44-45` still polls whenever `AUTH_MODE === 'local'`, which that profile uses. A 404 sets
   `res.ok = false` → `internetReachable: false` **permanently**: the AI chat and Google controls go dark behind an
   undismissable French warning telling a dentist on cellular to check the *local network*.
   **Web-side only:** stop deriving the poll from `AUTH_MODE`, treat a **404 as "egress signal absent"** (features
   enabled, no warning), add the client→server axis, fix the wording. `SelfHostedLan` stays **byte-identical** (R-4b)
6. **Backgrounded mid-upload** — completes, or fails in French with the file still selectable. Never a truncated
   attachment on a patient's record

### Part 8 — Phase 5: two store listings 🔒 BLOCKED

> **Do not start.** Unblocked by an Apple Developer Program membership + a Google Play developer account + a public
> domain a reviewer can reach + the four deferred business decisions. ⚠️ A bundle identifier **cannot be changed
> after first submission**. Submit only after Parts 6 and 7 — Apple's 4.2.2 rejects a bare webview wrapper, and push,
> camera, biometrics and native print are the answer to it.

1. **One version source** in `mobile/`, reported as the shell's `version` and sent as `X-Client-Version`. ⚠️
   `packaging/` already duplicates a hardcoded `1.0.0` across **three** files with no coupling — do not make it four
2. **Privacy declarations** on both stores, answered honestly: a hosted deployment **does** hold PHI
3. **A seeded, synthetic-only demo clinic** on the hosted domain, credentials in the review notes, with
   `must_change_password` **off** for that account or review dead-ends on the forced-change screen
4. **Submit**; both listings live and installable on a clean device, opening to a working sign-in
5. Note the **INPDP declaration (loi 2004-63)** as a real prerequisite for a public launch — a gate on AC-65

## Files to Create/Modify

The full inventory lives in [`../plan.md`](../plan.md) § *Files to Modify/Create*, with a `Part` column on every row.
Summary of the blast radius per part:

| Part | Creates | Modifies |
|------|---------|----------|
| 1 | 9 (icon master + generator + 7 assets) | 8 (`manifest.ts`, `layout.tsx`, `download.ts`, 4 components, `globals.css`, `ai-chat.tsx`, `check-responsive.mjs`, 2 docs) |
| 2 | 1 test class | 3 (`RefreshTokenCommand.cs`, `bff/auth/token/route.ts`, `bff/auth/local-login/route.ts`) |
| 3 | 4 (`MetaController`, `ClientVersionMiddleware`, its tests, `ClientVersionGate`) | 12 (`Program.cs`, coverage tests, `client.ts`, 8 API modules, config template) |
| 4 | ~9 (`mobile/` + Android sources) | 0 |
| 5 | ~6 (iOS sources) | 0 |
| 6 | ~18 (entities, enums, repos, configs, senders, controller, job, migration, 3 test classes) | 4 (`DeploymentProfile`, `Extensions.cs`, `Program.cs`, coverage tests) |
| 7 | 0–2 | 5 (`session.tsx`, `connectivity.tsx`, `ConnectivityController`, `appointments/page.tsx`, `bridge.md`) |
| 8 | store metadata + a demo seed | version source |

## Verification Steps

**Per part, before its commit:**

- [ ] **Part 1** — every icon resolves with no 404; install on Chrome/Android and Safari/iOS gives an uncropped tile;
      on a **physical iPhone in Safari** the patient file, invoice PDF, e-invoice XML, document PDF and Word export
      each deliver a file; printing `/factures`, a patient record and a document yields document content only
- [ ] **Part 2** — the cookie the browser holds after an exchange carries the **new** credential and a later expiry;
      a user active all day is never prompted by time alone; the desktop shell still works
- [ ] **Part 3** — anonymous `GET /api/meta/client-requirements` returns 200 **even from a below-floor client**; a
      below-floor header 426s everywhere else; a browser session sends no header and behaves as before; a manual walk
      of all 14 migrated sites shows **no changed error message**
- [ ] **Part 4** — cold-start login survives a relaunch; five states reachable; six file inputs open the picker with
      camera; every delivery path works in-shell and a >25 MB file is refused with the limit named; OS print; no
      remount on rotation; the Google OAuth hand-off leaves and returns; with `window.__clinicShell` **deleted at
      runtime** every affected screen behaves as in a browser
- [ ] **Part 6** — backgrounded/locked, the five categories each produce an OS notification; the four operational
      ones produce none while still appearing in the feed; a restore bumping every `TokenVersion` does **not**
      deactivate registrations
- [ ] **Part 7** — a photo appears in the files list on the web app; resume past the limit needs biometrics and the
      cookie is **still present** afterwards; nothing persisted contains a password; a BS1 opens and prints on
      Android; on a deployment with no probe the AI chat and Google controls stay **enabled** with no warning

**Verification commands:**

```bash
# web/ — the whole gate (no test runner, no ESLint, no CI)
cd web
npm run check:responsive       # 13 checks after Parts 1 and 3; all enforced once PENDING_PARTS is gone
npx tsc --noEmit
npm run build                  # warning count must equal the pre-change baseline (AC-12)
# then LOOK at it: 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard -> record in progress.md

# backend tests — ⚠️ Smart App Control blocks the runner (0x800711C7)
cd api
dotnet build -p:BaseOutputPath=<scratch>/    # build to a scratch path, never in-tree
dotnet vstest <scratch>/.../ClinicManagement.UnitTests.dll
# A RED RUN IS NOT EVIDENCE until bin/ + obj/ are cleared and `dotnet build-server shutdown` has run.

# Part 6 only — the schema and money gates, run BEFORE and AFTER the migration and diffed
cd api/ClinicManagement.API
dotnet run -- verify-schema      # exit 0 clean / 1 couldn't run / 2 drift found
dotnet run -- reconcile-money    # diff must be EMPTY — nothing here is financial
```

## Exit Criteria

This story is complete when:

- [ ] Parts 1, 2, 3 are landed, each as its own commit, with their gates green
- [ ] Part 4 is landed **or** explicitly recorded as blocked on tooling (R-12) with the check's result written down
- [ ] Parts 5, 6, 8 are either landed (once unblocked) **or** left unstarted with their unblock condition recorded —
      **never partially implemented**
- [ ] Part 7's web and Android halves are landed; its iOS halves are gated behind Part 5
- [ ] `npm run check:responsive` + `npx tsc --noEmit` + `npm run build` pass, warning count equal to baseline
- [ ] The backend suite is green under the Smart App Control procedure above
- [ ] For Part 6: `verify-schema` diffed before/after and `reconcile-money` diff **empty**
- [ ] `progress.md` records the eye pass for every part that touched `web/`, plus the operator hardware walk for
      Parts 4/5/7
- [ ] No new `IsLocalMode(` occurrence; no bare `[Authorize]` added; every new action carries a named policy
- [ ] All verification steps pass for every landed part

## Notes

**Why one story, and why `Layer: Full`.** `plan.md` records this as a deliberate choice (**R-1**) and `/break-plan`
was invoked with `one us`, so the single-layer BE/FE default is overridden rather than forgotten. The **eight parts**
carry the structure the layer split would have: each is a vertical increment that compiles and passes its own gate,
and **each boundary is a commit point**. If a session runs out, split at a boundary — never inside a part. This
decision persists; it is not re-opened next session.

**AC-69 is pulled forward into Part 1.** The spec files it under Phase 5, but it is a one-line documentation edit with
no dependency. Left inside a BLOCKED part it would simply never happen, and `mobile-tablet-responsive/spec.md` would
keep contradicting this feature.

**Two deliberate deviations from the blueprint**, both reasoned in `plan.md`: `ClientVersionMiddleware` is registered
**before** `UseAuthentication` (a stale client's login should 426, not 401 — AC-33), and the official-form iframe is
**kept** rather than swept into AC-7 (it carries no viewer fragment and *is* the print path).

**Three findings that came out of exploration, not the spec** — each is real work, not a nicety:
- **14 raw-`fetch` sites across 8 modules** hand-write `Authorization: Bearer` and would silently miss
  `X-Client-Version` (invoice PDFs, patient files, receipts, every CSV export). One exported header builder plus a
  derived check, headers only (R-5).
- **The deep-link target does not exist.** No `/appointments/[id]` route; the notification panel navigates nowhere.
  A tap would land on nothing (R-7).
- **Google refuses OAuth in an embedded webview.** « Connecter Google Agenda » is a plain top-level navigation, so
  without Part 4's interception it dead-ends and strands the webview with no way back.

**Three risks inherited from `LEARNINGS.md`, not guessed:** R-4 is `:45`'s capability-vs-configuration trap (it
previously flipped Cloud's `UseHttpsRedirection` from always to never, and `SupportsOsPush` is the first capability
that has to touch configuration at all); R-8 is `:67`'s `Secure`-cookie failure; R-5 is `:37`'s warning that unifying
a wrapper changes the error message a legacy caller saw. `:61` — *stateless JWT means server-side state changes don't
take effect until expiry* — is the Part 2 property, already recorded by this repo before the challenge found it again.

**One dependency moved during planning, in our favour.** `multi-tenant-cloud` **US-1 landed** between the spec being
challenged and this breakdown: `DeploymentProfile` now exists with 13 capabilities, so Part 6's blocker narrowed to
US-2 alone, AC-70's guard is live rather than future, and Part 7's connectivity work lost its API half. It also
turned the challenge's connectivity finding from a prediction into a **live defect** — `ExposesTrustEndpoints` is ✗
for `HostedMultiTenant`, so the probe 404s while the web still polls. Re-verify both dependencies before Part 6;
they move on their own schedule.

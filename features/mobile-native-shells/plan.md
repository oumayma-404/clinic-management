# Implementation Plan: The clinic on a phone — native Android & iOS shells

**Status:** APPROVED
**Challenged:** No
**Created:** 2026-08-05
**Approved:** 2026-08-05 — one story by explicit choice (R-1); three parts BLOCKED with unblock conditions stated.
**Spec:** [features/mobile-native-shells/spec.md](./spec.md) (Challenged: Yes — 4 Critical · 8 Major · 2 Minor resolved)
**Blueprint:** [blueprint.md](./blueprint.md) — ⚠️ **superseded** where it disagrees; it carries a note listing the
nine of its claims the challenge corrected.
**Branch:** off `main`. ⚠️ Not off `feature/audit-sections-3-to-10`.

---

## Overview

**One story, eight ordered parts** — chosen deliberately by the user against the session-sizing default. Every part
is a *vertical* increment (assets → domain → API → client → shell → docs), never a technical-layer grouping, so
`/implement-story` can land and commit **part by part** and a part boundary is the natural split point if a session
runs out. See **R-1**; the decision persists and is not re-litigated next session.

The architecture is the spec's, and three of its decisions carry the whole plan:

1. **The web app is never forked and never depends on the shell.** Every shell capability is feature-detected off
   `window.__clinicShell`; with the object absent, behaviour is byte-identical to today's browser. This is why
   Part 1 (pure web) ships alone and improves every mobile browser with no shell in existence.
2. **The bridge's method set is enumerated per phase, not fixed at three** (spec FR-6, resolved by the challenge).
   Part 4 implements three; Part 7 amends the contract document and bumps the version for two more.
3. **Where a platform forbids something, the user gets an explicit French message — never a dead control.** Three
   named exceptions drive real work: Google refuses OAuth in a webview (Part 4 hands off-origin navigations to the
   system browser), Android has no PDF viewer (Part 7), and push is impossible per-platform without credentials
   (Part 6 refuses registration rather than queueing it).

**Three parts cannot be executed on this machine and are marked BLOCKED with their unblock condition.** They are
planned to the same depth as the rest — `/implement-story` stops at the first blocked part with a clear reason
rather than half-building it.

> ### ⚠️ Verified against the code, not the spec: `multi-tenant-cloud` **US-1 has landed**
>
> `Infrastructure/Deployment/DeploymentProfile.cs` exists with **13 capabilities**, `DeploymentProfileCoverageTests`
> and `DeploymentProfileTests` are both present, and `IsLocalMode(` now survives **only** in its own definition, in
> `DeploymentProfile.Resolve`'s back-compat derivation, and in one test. Three consequences for this plan:
>
> 1. **Part 6's blocker narrows to US-2 alone** (`ITenantScope` / `SystemWideCallerCoverageTests` — neither exists).
>    `SupportsOsPush(platform)` becomes a **14th capability on a real file**, not a property on a hypothetical type.
> 2. **AC-70 is enforced by a live guard.** Part 3 must ask a named capability, never `IsLocalMode(` — a new
>    occurrence **fails the build today**, not "once US-1 lands".
> 3. **Part 7's connectivity work shrinks, and its premise is now live rather than predicted.**
>    `ConnectivityController:40` already gates on `ExposesTrustEndpoints`, which is **`false` for
>    `HostedMultiTenant`** — so the API returns **404** there, while `connectivity.tsx` still polls whenever
>    `AUTH_MODE === 'local'`, which that profile uses (`UsesLocalAccounts: true`). The defect the challenge
>    predicted is reachable in code now. **The API half needs no change**; the remaining work is web-side only.

| Part | Slice | Status |
|---|---|---|
| 1 | Phase 0 — web fixes | **EXECUTABLE** |
| 2 | Phase 2b — session continuity | **EXECUTABLE** |
| 3 | Phase 2 — client version floor | **EXECUTABLE** |
| 4 | Phase 1 — the Android shell | **EXECUTABLE**, subject to a JDK/Android-SDK check (R-12) |
| 5 | Phase 1 — the iOS shell | **BLOCKED** — macOS + Apple Developer Program |
| 6 | Phase 3 — OS push | **BLOCKED** — `multi-tenant-cloud` **US-2** only (US-1 has landed) |
| 7 | Phase 4 — native capability | **PARTLY BLOCKED** — web + Android halves executable; iOS halves need Part 5 |
| 8 | Phase 5 — store submission | **BLOCKED** — store accounts + 4 deferred business decisions |

**Ordering note — Part 2 before Part 3, both before the shells.** Session continuity (Phase 2b) is server-side, has
no dependency, and removes the twice-daily password prompt for *existing* users — it is the largest user win per line
of code in the whole feature and does not need a phone to be worth shipping. The version floor (Part 3) comes next
because Part 4's shell reads its endpoint at launch.

**Acceptance-criteria coverage:** AC-1…AC-12 → Part 1 · AC-35…AC-39 → Part 2 · AC-28…AC-34 → Part 3 ·
AC-13…AC-27 → Parts 4/5 · AC-40…AC-55 → Part 6 · AC-56…AC-64 → Part 7 · AC-65…AC-69 → Part 8 ·
cross-cutting AC-70…AC-73 → Parts 3/6 · edge cases AC-74…AC-77 → Parts 4/6/7.

---

## Files to Modify/Create

### Files to Create

| File | Purpose | Part |
|------|---------|------|
| `web/branding/icon.svg` | The icon master — a simple geometric mark in the app's own accent on its background token. Editable; a designer replaces this one file. | 1 |
| `web/scripts/generate-icons.py` | Committed, reproducible rasteriser (PIL 12.3, already present). Emits all seven assets. Deterministic resampling so a re-run produces no diff (R-9). | 1 |
| `web/public/icon-192.png`, `icon-512.png`, `icon-maskable-512.png`, `apple-icon.png`, `icon-light-32x32.png`, `icon-dark-32x32.png`, `icon.svg` | The seven assets `layout.tsx:21-37` already declares and that **all 404 today**. Maskable carries the 40 % safe zone or Android crops it; `apple-icon.png` is 180 px with **no alpha** (iOS renders transparency black). | 1 |
| `api/ClinicManagement.API/Controllers/MetaController.cs` | `GET /api/meta/client-requirements` → `{ minimumShellVersion, currentShellVersion, storeUrls }`. `[AllowAnonymous]` **and** exempt from the floor (AC-29). | 3 |
| `api/ClinicManagement.API/Middleware/ClientVersionMiddleware.cs` | Reads `X-Client-Version`; below the floor ⇒ **426** `{ error, code: "client_too_old" }` through the canonical body. | 3 |
| `api/ClinicManagement.UnitTests/Api/ClientVersionMiddlewareTests.cs` | absent · malformed · below · equal · above · **meta route exempt**. | 3 |
| `web/components/client-version-gate.tsx` | Renders the full-screen French « mettre à jour » state on a 426. Mounted in `layout.tsx`; works in a plain browser too. | 3 |
| `mobile/CLAUDE.md`, `mobile/README.md` | The AI map and the operator build/signing guide. States "not CI-runnable, operator-verified" as `desktop/CLAUDE.md` does. | 4 |
| `mobile/shared/bridge.md` | **THE** contract: the per-phase method set, the version, and the rule that a change edits this file *and* bumps `version`. | 4 |
| `mobile/android/…/MainActivity.kt` | The five French states + the native launch version check. | 4 |
| `mobile/android/…/ServerConfig.kt` | `SharedPreferences`; mirrors `desktop/ServerConfig.cs`'s `ParseAddress` (bare host · `host:port` · full URL). | 4 |
| `mobile/android/…/ShellBridge.kt` | `@JavascriptInterface` — Phase 1's three methods, nothing else. | 4 |
| `mobile/android/…/FileChooser.kt` | `WebChromeClient.onShowFileChooser` — without it **all six** `<input type="file">` sites silently do nothing. | 4 |
| `mobile/android/…/ExternalNavigation.kt` | Off-origin top-level navigations → Custom Tabs. This is what makes Google OAuth reachable (AC-25). | 4 |
| `mobile/ios/ClinicShell/*.swift` | The iOS mirror: `ShellView`, `ServerConfig`, `ShellBridge`, `ExternalNavigation`. | 5 |
| `Domain/Entities/DeviceRegistration.cs`, `PushDelivery.cs` | The registry and the outbox. | 6 |
| `Domain/Enums/DevicePlatform.cs`, `PushDeliveryStatus.cs` | Four-state status incl. non-terminal **`Blocked`**, mirroring `NotificationStatus`. | 6 |
| `Domain/Repositories/IDeviceRegistrationRepository.cs`, `IPushDeliveryRepository.cs` | `GetActiveForUsersAsync(IEnumerable<string>)` — **batched**, never per-user. | 6 |
| `Infrastructure/Persistence/Configurations/DeviceRegistrationConfiguration.cs`, `PushDeliveryConfiguration.cs` | Auto-discovered. Unique index on the token; index on `(ClinicId, UserId, IsActive)`; index on `(Status, SendNotBefore)` for the scan. | 6 |
| `Infrastructure/Repositories/DeviceRegistrationRepository.cs`, `PushDeliveryRepository.cs` | | 6 |
| `Application/Common/Interfaces/IPushSender.cs` | Mirrors `IReminderChannelSender`'s *shape*; cannot *be* one. Four outcomes incl. **`TokenInvalid`**. | 6 |
| `Infrastructure/Services/FcmPushSender.cs`, `ApnsPushSender.cs`, `HttpPushSender.cs` | 15 s-bounded, never throws. Credentials via `IReminderSecretProtector`, never `IConfiguration` directly. | 6 |
| `Application/Common/Services/PushNotificationGeneratorDecorator.cs` | Decorates `INotificationGenerator` in DI — **one** hook reaches all seven categories. | 6 |
| `API/Controllers/PushDevicesController.cs` | `POST /api/push-devices` (register/refresh/**rebind**), `DELETE /api/push-devices/{token}`. `AnyClinicRole`. | 6 |
| `API/BackgroundJobs/PushDispatchJob.cs` | Hangfire minutely, `NotificationJob`'s template. | 6 |
| `Infrastructure/Migrations/…AddPushDeviceRegistrations.cs` | Two tables. No existing table changes, no backfill. | 6 |
| `api/ClinicManagement.UnitTests/…/PushFanOutTests.cs`, `PushDispatchJobTests.cs`, `DeviceRegistrationTenantIsolationTests.cs` | Audience equals the feed's minus the actor · one row per device · `TokenInvalid` deactivates · a `Blocked` row leaves the scan · quiet hours · `Unset`-scope isolation. | 6 |

### Files to Modify

| File | Changes | Part |
|------|---------|------|
| `web/app/manifest.ts` | Populate `icons` (the `icons: []` at `:36` is deliberate *because the files were missing* — that reason expires); add `id`; keep `display: "standalone"`. Delete the now-false comment block. | 1 |
| `web/app/layout.tsx` | Add `appleWebApp` to `metadata`; add `themeColor` to the **`viewport`** export (`:53-58`) — Next 15 reads it there, so `manifest.ts`'s `theme_color` alone emits no `<meta>`. Mount `<ClientVersionGate>`. | 1, 3 |
| `web/lib/download.ts` | Add the shell `saveFile` branch **first** (before `canShare`), and the **25 MB** refusal *before* the blob is read. Guard `window` (LEARNINGS `:151`). ⚠️ The cap applies **only on the shell path** — a browser has no base64 marshalling to run out of memory on. | 1 |
| `web/components/patient-files-manager.tsx` | `:252-258` hand-rolled `<a download>` → `downloadBlob`. `:741-745` iframe → coarse-pointer delivery. `:477`'s file input needs no change (Part 7 gets the camera free). | 1 |
| `web/components/factures/invoices-table.tsx` | `:258-265` (invoice PDF) and `:302-310` (e-invoice XML) → `downloadBlob`. Both silently deliver nothing on iOS Safari today. | 1 |
| `web/components/document-editor-content.tsx` | `:1840-1847` → `downloadBlob`; `:1790` `saveAs()` (file-saver, a **third** mechanism) → `downloadBlob`. ⚠️ Leave the `isOfficialForm` iframe at `:3491` and its `contentWindow.print()` at `:1942` **alone** — spec AC-8 keeps them; Part 7 routes them natively. | 1 |
| `web/app/patients/[id]/page.tsx` | `:2714-2718` iframe → coarse-pointer delivery. | 1 |
| `web/app/globals.css` | Add a `@media print` block: hide the rail, the bottom bar, the AI launcher and the toaster; drop `dvh` constraints; force a light surface. Today the **only** print CSS in the repo is a string injected into a popup. | 1 |
| `web/components/ai-chat.tsx` | `:190` — hide the mic when `!('webkitSpeechRecognition' in window)`. Dead on every iPhone today (AC-11). | 1 |
| `web/scripts/check-responsive.mjs` | Delete `PENDING_PARTS` at `:33`; add three derived checks — `blob-delivery`, `pdf-viewer-params`, `api-headers`. All greps, so no FE runner is needed (LEARNINGS `:225`). | 1, 3 |
| `.claude/rules/frontend-web.md` § 15 | It still lists the manifest, the icons and the LAN trust page as open, and `PENDING_PARTS` as the source of truth. All now wrong. | 1 |
| `features/mobile-tablet-responsive/spec.md` | Record the amendment to its "no native application" non-goal (AC-69). ⚠️ **Deviation:** the spec files this under Phase 5, but it is a one-line doc edit with no dependency — left in a BLOCKED part it would simply never happen, and the two documents would keep disagreeing. | 1 |
| `Application/Features/Auth/Commands/RefreshTokenCommand.cs` | Mint a fresh refresh token via the existing `GenerateRefreshToken` and populate `RefreshToken` + `RefreshExpiresAt` (both are `string.Empty`/`null` today, `:72-86`). | 2 |
| `web/app/bff/auth/token/route.ts` | **Re-set `SESSION_COOKIE`** with the rotated credential and its new expiry (`:44-49` returns only the access token and re-sets no cookie — the missing half). Share one cookie-writing helper with `local-login/route.ts` so the `Secure`/`httpOnly`/`path` attributes cannot diverge (LEARNINGS `:67`). | 2 |
| `api/ClinicManagement.API/Program.cs` | Register `ClientVersionMiddleware` after `ExceptionMiddleware` (`:545`) and **before** `UseAuthentication` (`:547`). ⚠️ **Deviation from the blueprint**, which said after authentication: the refusal needs no principal, and placing it before means a stale client's *login* also 426s instead of 401ing — which is exactly AC-33's "must not present itself as signed out". Register `PushDispatchJob` recurring only when the profile supports push (Part 6). | 3, 6 |
| `api/ClinicManagement.UnitTests/Api/ControllerAuthorizationCoverageTests.cs` | Add `Meta.ClientRequirements` to `ExpectedAnonymous` (`:39-53`). The set is asserted **equal in both directions**, so this is mandatory, not optional. Add `PushDevices.*` policies in Part 6. | 3, 6 |
| `web/lib/api/client.ts` | Export one `apiHeaders(token, kind)` builder folding today's `createHeaders`/`formDataHeaders` (`:395-410`) and adding `X-Client-Version` from `window.__clinicShell?.version`. Expose the 426 hook `<ClientVersionGate>` listens on. | 3 |
| `web/lib/api/billing.ts`, `clinics.ts`, `doctors.ts`, `export.ts`, `invoices.ts`, `medical-documents.ts`, `patient-files.ts`, `treatment-plans.ts` | **14 raw-`fetch` sites** hand-write `Authorization: Bearer` and would not send the header. Replace the headers object with `apiHeaders(...)`. ⚠️ **Headers only — response handling untouched**, so no legacy caller's error message changes (LEARNINGS `:37`, R-5). | 3 |
| `packaging/.../appsettings.Production.json` template | `Clients:MinimumShellVersion` + `Clients:StoreUrls`. The **operator-owned** layer, never truncated on upgrade (`InstallConfiguration`). | 3 |
| `web/lib/auth/session.tsx` | The Local provider's inactivity path (`:181-229`): when the bridge is present, await the shell's biometric result instead of `logout()`. The cookie is **not** cleared on success. Absent bridge ⇒ byte-identical to today. | 7 |
| `web/lib/connectivity/connectivity.tsx` | Stop deriving "do I poll?" from `AUTH_MODE` (`:44-45`); gate on the deployment capability, treat a probe 404 as **signal absent** (not offline), and drop « réseau local » from the wording — a phone on cellular is not on the clinic's LAN. | 7 |
| ~~`api/.../ConnectivityController.cs`~~ | ✅ **Already done by US-1** — `:40` gates on `ExposesTrustEndpoints`. **No change needed.** It is *why* the web-side fix is now urgent: that capability is ✗ for `HostedMultiTenant`, so the probe 404s there. | 7 |
| `Infrastructure/Deployment/DeploymentProfile.cs` | ✅ **Exists** (US-1 landed) with 13 capabilities. Add a **14th**, per-platform `SupportsOsPush(DevicePlatform)` — not one boolean (spec FR-10) — to the type *and* to `For(kind)`'s matrix for all three kinds. | 6 |
| `api/ClinicManagement.UnitTests/Infrastructure/Deployment/DeploymentProfileTests.cs` | Extend the capability matrix for `SupportsOsPush`, including that `SelfHostedLan` stays ✗ regardless of configuration (R-4). | 6 |
| `Application/Common/Services/NotificationGenerator.cs` + `Extensions.cs` | Decorate rather than edit: the decorator is registered over the existing registration. Touching its 13 methods would be the `fixes-dont-propagate` shape. | 6 |
| `web/app/appointments/page.tsx` | Accept `?focus=<appointmentId>` and open the agenda on that appointment's day with it selected — the deep-link destination. ⚠️ **There is no `/appointments/[id]` route and the notification panel navigates nowhere today**, while `AppointmentsController.GET/{id}` is documented as "notification deep-link target". A destination has to exist before a notification tap can land (R-7). | 7 |

---

## Implementation Stories

### US-1: The clinic works from a phone — from the browser fixes through two store-ready shells

**Goal:** A dentist and their assistant(e) can install the app from a store, sign in once and stay signed in through
a working day, be notified on a locked phone, photograph a tooth into the record, print, download, and be told
plainly when their app is too old — while the same web bundle keeps behaving exactly as it does in a browser.
**Blocked by:** None (Part 1). Parts run in order; each part's own blockers are stated on it.
**Layers:** assets · domain · API · BFF · web client · two native shells · docs · store operations

> ⚠️ **This is one story by explicit choice (R-1).** Commit at every part boundary. A part is a vertical increment
> and is independently shippable; if a session runs out, split at the boundary — never inside a part.

---

#### Part 1 — Phase 0: the web fixes a webview makes load-bearing · **EXECUTABLE** · AC-1…AC-12, AC-69

*Ships alone, on any branch, with no shell in existence. Fixes four defects live in the browser today.*

1. Draw `web/branding/icon.svg` — a simple geometric mark using the app's own tokens (accent on the `#fdfdfe`
   background `manifest.ts` already declares). Write `web/scripts/generate-icons.py` (PIL) emitting the seven assets
   with deterministic resampling; run it; commit source, script and output.
2. `web/app/manifest.ts` — populate `icons` with the 192/512/maskable trio, add `id`, delete the stale
   "deliberately empty" comment. **AC-1, AC-2.**
3. `web/app/layout.tsx` — add `appleWebApp: { capable, statusBarStyle, title }` to `metadata`; move/add `themeColor`
   into the **`viewport`** export. **AC-3.**
4. `web/lib/download.ts` — insert the shell `saveFile` branch as the **first** path and the 25 MB refusal *before*
   `URL.createObjectURL`, with a French message naming the limit and offering another route. Shell-path only.
   **AC-20** (verified in Part 4).
5. Route all five bypassing call sites through `downloadBlob`: `patient-files-manager.tsx:252-258`,
   `invoices-table.tsx:258-265` and `:302-310`, `document-editor-content.tsx:1840-1847`, and the `file-saver`
   `saveAs()` at `:1790`. **AC-4.**
6. `check-responsive.mjs` — add the `blob-delivery` check (any `a.download =` / `saveAs(` / anchor `.click()`
   outside `web/lib/download.ts`) and `pdf-viewer-params` (`toolbar=0` / `navpanes=0` anywhere). Derived greps, no
   exemption list. **AC-5, AC-6.**
7. Replace the two viewer-fragment iframes with coarse-pointer delivery through `downloadBlob`:
   `patients/[id]/page.tsx:2714-2718`, `patient-files-manager.tsx:741-745`. **AC-7.**
   ⚠️ **Do not touch** `document-editor-content.tsx:3491` — it is the official-form (BS1 / arrêt de travail)
   preview, it carries no fragment, and it *is* the print path. **AC-8** is Part 7's.
8. Add the `@media print` block to `globals.css`: suppress the rail, bottom bar, AI launcher and toaster; release
   `dvh` height caps; force a light surface. Verify on `/factures`, a patient record and a document. **AC-9.**
9. `ai-chat.tsx:190` — feature-detect `webkitSpeechRecognition` and render nothing when absent. **AC-11.**
10. Delete `PENDING_PARTS` (`:33`); rewrite `.claude/rules/frontend-web.md` § 15 so it no longer claims the
    manifest, icons or trust page are missing, nor `PENDING_PARTS` as its source of truth. **AC-10.**
11. Record the non-goal amendment in `features/mobile-tablet-responsive/spec.md`. **AC-69** (deviation noted above).

**Validation:**
- [ ] `npm run check:responsive` (13 checks, all enforced) · `npx tsc --noEmit` · `npm run build` — warning count
      identical to the pre-change baseline. **AC-12.**
- [ ] Every declared icon resolves — no 404 in the network panel; install from Chrome/Android and Safari/iOS gives
      an uncropped tile.
- [ ] On a real iPhone in Safari: patient file, invoice PDF, e-invoice XML, document PDF and the Word export each
      **deliver a file**. This is the criterion that fails today.
- [ ] Print `/factures`, a patient record and a document — document content only.
- [ ] Eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard; recorded in `progress.md`.

---

#### Part 2 — Phase 2b: the session lasts the working day · **EXECUTABLE** · AC-35…AC-39

*No dependency. The largest user win per line in the feature, and it needs no phone.*

1. `RefreshTokenCommandHandler` — call the existing `_localAuthService.GenerateRefreshToken(user)` and populate
   `RefreshToken` / `RefreshExpiresAt` on the returned `LoginResultDto`. Leave every existing guard untouched:
   audience validation, `TokenVersion` equality, `IsActive`, and `MustChangePassword` surfaced rather than refused.
2. Extract the session-cookie write out of `bff/auth/local-login/route.ts` into one shared helper (attributes:
   `httpOnly`, `path`, `Secure` from the explicit config flag — **never** re-derived from `NODE_ENV` or the internal
   request scheme, LEARNINGS `:67`).
3. `bff/auth/token/route.ts` — on a successful exchange, re-set the cookie through that helper with the new
   credential and expiry. Keep the 401-clears-the-cookie and 429/503-leave-it-alone behaviour exactly as it is.
4. Write `RefreshTokenCommandHandlerTests`: a fresh credential and expiry are returned · a bumped `TokenVersion`
   still refuses · an inactive account still refuses · `mustChangePassword` still surfaces · all three refusals share
   one indistinguishable message. **AC-36** — the existing guards are exactly what must *not* regress while the
   credential starts sliding.
5. State the property in the handler's doc comment: **sliding expiry, not revoking rotation** — a superseded
   credential stays valid until its own expiry because it is stateless, and `TokenVersion` remains the only
   revocation. This is LEARNINGS `:61` recorded at the call site.

**Validation:**
- [ ] The cookie the browser holds after an exchange carries the **new** credential and a later expiry. **AC-35** —
      asserted on the cookie, not only the API response.
- [ ] A user active all day is never prompted for a password by time alone; a user idle past the window is.
      **AC-37** — the criterion AC-35 exists to serve.
- [ ] A client ignoring both new fields is unaffected; the desktop shell still works. **AC-38.**
- [ ] No test asserts that a superseded credential is rejected while unexpired. **AC-39.**
- [ ] `dotnet vstest` green (see Testing Strategy for the Smart App Control procedure).

---

#### Part 3 — Phase 2: a stale app says so · **EXECUTABLE** · AC-28…AC-34, AC-70, AC-71

1. `MetaController` + `GET /api/meta/client-requirements`, `[AllowAnonymous]`, reading the floor, the current release
   and both store URLs from operator configuration. **AC-28.**
2. Add `Meta.ClientRequirements` to `ExpectedAnonymous`. Both-direction equality makes this mandatory.
3. `ClientVersionMiddleware` — parse `X-Client-Version`; absent or malformed ⇒ pass; below the floor ⇒ **426** with
   the canonical `{ error }` body plus `code: "client_too_old"`; **exempt the meta route itself**. **AC-29…AC-32.**
4. Register it in `Program.cs` after `ExceptionMiddleware` and before `UseAuthentication` (deviation and reason
   above). Do **not** write `IsLocalMode(` anywhere — **AC-70**.
5. `ClientVersionMiddlewareTests` — absent · malformed · below · equal · above · meta-route-exempt.
6. `client.ts` — fold `createHeaders`/`formDataHeaders` into one exported `apiHeaders(token, kind)` that adds
   `X-Client-Version` from `window.__clinicShell?.version` and adds nothing when the bridge is absent. **AC-31.**
7. Replace the hand-written headers object at all **14** raw-`fetch` sites across the eight modules with
   `apiHeaders(...)`. Headers only — leave every response path alone (R-5).
8. Add the `api-headers` derived check: any `Authorization.*Bearer` literal outside `client.ts` fails the gate. This
   is what makes AC-31 true by enforcement rather than by discipline.
9. `client.ts` surfaces a 426 through a hook; `<ClientVersionGate>` (mounted in `layout.tsx`) renders the
   full-screen French update state with the store link — and works in a plain browser. **AC-33**, mid-session half.
10. Add `Clients:MinimumShellVersion` and `Clients:StoreUrls` to the operator-owned `appsettings.Production.json`
    template. **AC-34.**

**Validation:**
- [ ] Anonymous `GET /api/meta/client-requirements` → 200 **including from a below-floor client** (AC-29).
- [ ] A below-floor `X-Client-Version` → 426 + `code` on every route but the meta one; absent/malformed → unchanged.
- [ ] Every API call in a browser session carries no such header and behaves exactly as before.
- [ ] `check:responsive` fails if a `Bearer` literal is reintroduced outside `client.ts`.
- [ ] Manual walk of all 14 migrated sites: invoice PDF, e-invoice XML, receipt, devis PDF, installment receipt,
      patient file up/download, document PDF, cachet, logo, every CSV export. **No error message changed** (R-5).
- [ ] `ControllerAuthorizationCoverageTests` green in both modes. **AC-71.**

---

#### Part 4 — Phase 1: the Android shell · **EXECUTABLE**, subject to R-12 · AC-13…AC-27, AC-74, AC-76

1. Create `mobile/` as a sibling of `desktop/` — own toolchain, in neither `api/ClinicManagement.sln` nor `web/`.
   Write `CLAUDE.md` (stating "not CI-runnable, operator-verified"), `README.md`, and `shared/bridge.md` as the
   single contract with its per-phase method set and version rule. **AC-27.**
2. `ServerConfig.kt` — persist the address in `SharedPreferences`, porting `desktop/ServerConfigStore.ParseAddress`
   (bare host · `host:port` · full URL). Never compile the address in. **AC-17.**
3. `MainActivity.kt` — the five mutually-exclusive French states (`WebView` · `Connecting` · `ServerConfig` ·
   `Unreachable` · `UpdateRequired`) with « Réessayer », « Changer de serveur… », « Recharger ». Use
   `webView.reload()`, never a re-assigned URL, so « Réessayer » re-attempts an unchanged address — `desktop`'s
   hard-won detail at `MainWindow.xaml.cs:85-87`. **AC-15, AC-16.**
4. **Before creating the webview**, read `GET /api/meta/client-requirements` over native HTTP and show
   `UpdateRequired` if below the floor — the app is never loaded. **AC-33**, launch half.
5. WebView configuration: `setDomStorageEnabled(true)` (else the three `localStorage` keys and the sessionStorage key
   die), persistent cookies, `mixedContentMode = NEVER_ALLOW`, `viewportFit=cover` already set by `layout.tsx`.
   Declare the configuration changes the activity handles so **rotation does not recreate it** — otherwise AC-23 is
   unachievable no matter what the web app does. **AC-14, AC-22, AC-23.**
6. `FileChooser.kt` — `WebChromeClient.onShowFileChooser`, with the camera offered for `accept="image/*"`. Without
   it all six file inputs silently do nothing, including the CSV import. **AC-18.**
7. `ExternalNavigation.kt` — any top-level navigation off the hosted origin goes to Custom Tabs; the callback's
   redirect back to the origin returns to the webview. Verify with « Connecter Google Agenda », which **Google
   refuses to serve in a webview**. **AC-25.**
8. `ShellBridge.kt` — Phase 1's three methods and nothing else: `saveFile(base64, name, mime)` (write + offer to
   open/share), `print()` via `PrintManager` + `createPrintDocumentAdapter()` (Android has no `window.print()`),
   `onPushToken(cb)` (registered, inert until Part 6). Inject `version` and `platform` before first paint. **AC-26.**
9. Handle the forced-password-change path: the 403 carrying `code: "must_change_password"` must route to the
   change-password screen inside the shell. **AC-76.**
10. Back gesture navigates within the app; safe-area insets reach the page so `--bottom-inset` (already correct at
    `globals.css:282`) does its job. **AC-22, AC-24.**

**Validation:**
- [ ] Installable artifact opens the hosted app full-screen, no browser chrome. **AC-13.**
- [ ] Sign in, kill the app, relaunch — **still signed in** (persistent cookie store). **AC-14.**
- [ ] All five states reachable in a test, all French, unreachable naming the address and the reason.
- [ ] All six file inputs open the picker; an image input offers the camera. **AC-18.**
- [ ] Every Part 1 delivery path delivers a file **in the shell**, where blob downloads and `navigator.share` are
      both unavailable. A >25 MB file is refused with the limit named. **AC-19, AC-20.**
- [ ] Print through the OS print service. **AC-21.**
- [ ] Rotation and split-screen **do not remount** — typed input in an open dialog survives. **AC-23.**
- [ ] With `window.__clinicShell` deleted at runtime, every affected screen behaves exactly as in a browser.
      **AC-26.**
- [ ] A route the server does not have is an ordinary French error on that screen, never a crash or blank app.
      **AC-74.**
- [ ] Operator-verified on **physical** hardware; walk recorded in `progress.md`.

---

#### Part 5 — Phase 1: the iOS shell · 🔒 **BLOCKED** · AC-13…AC-27 (iOS half)

> **Unblocked by:** a macOS machine with Xcode (or Xcode Cloud / Codemagic) **and** an Apple Developer Program
> membership. Not solvable in this repo — it is win32 with no CI, and per `mobile-tablet-responsive/plan.md:592`
> **the project has never had an iOS device**. Do not mark this part done on a simulator alone.

Mirrors Part 4 one-for-one in Swift, with four platform specifics that are not optional:

1. `WKWebView` with **`WKWebsiteDataStore.default()`** (persistent, never `nonPersistent`) — otherwise the
   `local_session` cookie is dropped on every cold start and the user logs in every launch.
2. `allowsBackForwardNavigationGestures = true` for swipe-back.
3. `ShellBridge.swift` as a `WKScriptMessageHandler`; print via `viewPrintFormatter()` +
   `UIPrintInteractionController` (`window.print()` is unreliable in `WKWebView`) — ⚠️ **verify on hardware**.
4. `ExternalNavigation.swift` using `ASWebAuthenticationSession` for the off-origin hand-off.
5. File inputs need **no** bridge work — `WKWebView` offers camera, library and Files natively.

**Validation:** the whole of Part 4's checklist, on a physical iPhone **and** an iPad (Split View crossing 768 px is
part of AC-23).

---

#### Part 6 — Phase 3: a backgrounded phone still knows · 🔒 **BLOCKED** · AC-40…AC-55, AC-70…AC-73, AC-75

> **Unblocked by:** `features/multi-tenant-cloud` **US-2** (`ITenantScope`) merged. ⚠️ **US-1 has already landed** —
> `DeploymentProfile` exists with 13 capabilities — so this part's only remaining dependency is the tenant scope.
> Without US-2, `PushDispatchJob` would read **zero rows and log success** — R-1 of that plan, and the exact failure
> this dependency exists to prevent.

1. Add a **14th capability** to the existing `Infrastructure/Deployment/DeploymentProfile.cs`: a **per-platform**
   `SupportsOsPush(DevicePlatform)` (FCM reachable + credentials for Android; APNs for iOS). Not one boolean — the
   half-configured install is the likely one. Add it to the `For(kind)` matrix for all three kinds and extend
   `DeploymentProfileTests`. Never write `IsLocalMode(` — `DeploymentProfileCoverageTests` **already fails the build**
   on a new occurrence (**AC-70**).
   ⚠️ Unlike the 13 existing capabilities, this one is **not** derivable from `Kind` alone: FCM/APNs credentials are
   configuration. Resolve it as `Kind`-permits **and** credentials-present, and keep the `Kind` half in the matrix so
   `SelfHostedLan` is ✗ regardless of what an operator configures — that boundary is the one `LEARNINGS.md :45`
   protects, and `DeploymentProfile`'s own doc comment explains why every other capability avoids configuration.
2. `DeviceRegistration` aggregate root: `ClinicId`, `UserId` (**`string`** — `User.Id` *is* the `sub`), `Platform`,
   `Token`, `ShellVersion`, `LastSeenAt`, `IsActive`; `static Create`, `private set`, `Deactivate()`/`Touch()`.
   Clinic-owned, so it takes the standard query filter. Unique on the token — which is what makes **rebinding** one
   deterministic write.
3. `PushDelivery` outbox: recipient device, payload reference, status, attempts, failure reason, **`SendNotBefore`**.
   Four-state status with a non-terminal **`Blocked`**, mirroring `NotificationStatus` and `adoption-qa-l` L3's
   lesson — a row that cannot send must **leave the scan**, or unsendable rows accumulate at the front of an
   oldest-first `.Take(N)` and starve every later row for ever. **AC-50.**
4. `PushDevicesController` — `POST` registers, refreshes, or **rebinds** a token bound to another user (deactivating
   the prior binding, **never a 409**); refuses an unsupported platform with a French reason rather than queueing it;
   `DELETE {token}` deregisters. `AnyClinicRole` — a secretary's phone must register. The controller 404s where the
   deployment supports neither platform. **AC-40…AC-42, AC-51, AC-52, AC-71.**
5. `IPushSender` + `FcmPushSender`/`ApnsPushSender` over a shared `HttpPushSender`: 15 s-bounded, never throws, four
   outcomes. **`TokenInvalid` is load-bearing** — FCM/APNs answer `UNREGISTERED`/`410 Gone` for an uninstalled app,
   which is terminal *per device*, so the row fails **and** `Deactivate()`s the registration. **AC-49.**
6. Fan out in the outbox writer via a **decorator** over `INotificationGenerator` — one hook reaches all seven
   categories; editing its 13 methods would be the `fixes-dont-propagate` shape. Audience = **the in-app feed's
   audience minus `ActorUserId`**, asserted against the feed rather than a second list. Payload carries a category, a
   fixed French label and the appointment id — nothing else. Only the five time-critical categories push. Apply the
   **21:00→08:00 clinic-local floor** by setting `SendNotBefore`, not by re-testing the clock each scan.
   **AC-43…AC-47, AC-55.**
7. `PushDispatchJob` — Hangfire minutely on `NotificationJob`'s template: `[DisableConcurrentExecution]`,
   `[AutomaticRetry(3)]`, bounded batch, retention purge that never deletes a `Pending` row, and
   **`UseSystemWide("push dispatch reads every clinic's queued sends")`** or US-2's guard fails the build
   (**AC-72**). Re-check recipient eligibility at dispatch — a queued row must not outlive a deactivation or a clinic
   change, because an OS banner bypasses every request-time check. Register it only when the profile supports push.
8. Migration for the two tables. No existing table changes, no backfill.
9. Tests: `PushFanOutTests` (audience equals the feed's · actor excluded · one row per device · `TokenInvalid`
   deactivates · payload carries no name/amount/free text), `PushDispatchJobTests` (a `Blocked` row leaves the scan ·
   quiet hours defer to 08:00 · a push failure never touches the causing operation), `DeviceRegistrationTenantIsolationTests`
   (another clinic's row reads as not-found, **plus the `Unset`-scope case**). **AC-53…AC-55.**

**Validation:**
- [ ] App backgrounded or phone locked: booking, cancelling, rescheduling, the ~24 h reminder and a post-visit review
      each produce an OS notification on both platforms. **AC-43.**
- [ ] Low stock, expiring stock, stale backup and failed reminder produce **none**, while still appearing in the
      feed. **AC-44.**
- [ ] A restore that bumps every `TokenVersion` does **not** deactivate device registrations. **AC-54.**
- [ ] Permission declined or later revoked: the app works fully, the state is visible with a route to OS settings, no
      repeated prompting, nothing claims push is active. **AC-75.**
- [ ] `verify-schema` before and after the migration, **diffed**; `reconcile-money` diff **empty** — nothing here is
      financial, and that emptiness *is* the assertion. **AC-73.**

---

#### Part 7 — Phase 4: the phone becomes an instrument · ⚠️ **PARTLY BLOCKED** · AC-56…AC-64, AC-77

> Web and Android halves are executable after Part 4. The iOS halves need Part 5.

1. **Camera capture** — free from Part 4's `onShowFileChooser` and iOS's native picker;
   `patient-files-manager.tsx:477` already renders `accept="image/*"`. Verify the photo attaches with the **same
   validation as an uploaded file**. **AC-56.**
2. **Biometric resume** — amend `mobile/shared/bridge.md` and bump the version for a biometric method (FR-6's
   per-phase set). In `session.tsx`, when the bridge is present, the inactivity path awaits the shell's result
   instead of calling `logout()`; three failures fall back to the password screen; the cookie is **never cleared on
   success**; no password is stored on the device. Absent bridge ⇒ byte-identical. **AC-57…AC-60.**
3. **Native PDF viewer** — Android `PdfRenderer`, iOS `QLPreviewController`, reached through `saveFile`'s open path.
   This is also what makes the **official forms** printable in a shell: route the BS1 / arrêt-de-travail preview and
   « Imprimer » through the native viewer and OS print service, and where neither exists show a French message and
   offer the file. A blank frame beside an inert « Imprimer » fails. **AC-8, AC-61.**
4. **Deep links** — App Links / Universal Links on the app's own host. ⚠️ **A destination must be built first:**
   there is no `/appointments/[id]` route and the notification panel navigates nowhere, while the API's
   `GET /api/appointments/{id}` is documented as the deep-link target. Add `?focus=<id>` to `/appointments` — the
   agenda opens on that day with the appointment selected. A new dynamic route is deliberately avoided: the spec puts
   "a tablet- or phone-specific redesign of any existing screen" out of scope (R-7). **AC-48** (Part 6 verifies the
   tap).
5. **Reachability, two axes** — client→server judged client-side on every deployment; server→internet egress only
   where the probe exists, and **absent, never inferred false**, elsewhere.
   ⚠️ **The API half is already done and the bug is live.** `ConnectivityController:40` gates on
   `ExposesTrustEndpoints`, **`false` for `HostedMultiTenant`**, so the probe 404s there — while
   `connectivity.tsx:44-45` still polls whenever `AUTH_MODE === 'local'`, which that profile uses. A 404 sets
   `res.ok = false` → `internetReachable: false` **permanently**, disabling the AI chat and the Google controls
   behind an undismissable French warning that tells a dentist on cellular to check the *local network*.
   The remaining work is **web-side only**: stop deriving the poll from `AUTH_MODE`, treat a **404 as "egress signal
   absent"** (enable the gated features, show no warning) rather than as offline, add the client→server reachability
   axis, and fix the wording. ⚠️ `SelfHostedLan` behaviour must stay **byte-identical** (R-4). **AC-62…AC-64.**
6. **Backgrounded mid-upload** — either the upload completes or it fails with a French message and the file is still
   selectable. Never a truncated attachment on a patient's record. **AC-77.**

**Validation:**
- [ ] A photo taken in the shell appears in the files list on the web app.
- [ ] Resume past the limit requires biometrics and restores the session **without a password**; the cookie is still
      present afterwards; a device with no enrolment falls back with no dead control and no error dialog.
- [ ] Nothing the shell persists contains a password. **AC-59.**
- [ ] A PDF — including a BS1 — opens in a working viewer on Android and prints.
- [ ] Losing the mobile network surfaces unreachable within one poll and recovers; on a deployment with no probe the
      AI chat and Google controls stay **enabled** and no « pas de connexion internet » warning appears.

---

#### Part 8 — Phase 5: two store listings · 🔒 **BLOCKED** · AC-65…AC-68

> **Unblocked by:** an Apple Developer Program membership + a Google Play developer account + a **public domain a
> reviewer can reach**, and the four deferred business decisions (hosted domain · bundle identifiers and display
> name · demo-tenant data policy · who owns the store accounts). ⚠️ A bundle identifier **cannot be changed after
> first submission**. Submit only after Parts 6 and 7 — Apple's guideline 4.2.2 rejects a bare webview wrapper, and
> push, camera, biometrics and native print are the answer to it.
> *(AC-69 was pulled forward into Part 1 — it is a one-line doc edit and would otherwise be stranded here.)*

1. One version source in `mobile/`, reported as the shell's `version` and sent as `X-Client-Version`. ⚠️ `packaging/`
   already duplicates a hardcoded `1.0.0` across three files with no coupling — do not make it four. **AC-68.**
2. Privacy manifest and data-collection declarations on both stores, answered honestly: a hosted deployment **does**
   hold PHI. **AC-66.**
3. A seeded **synthetic-only** demo clinic on the hosted domain, credentials in the review notes, with
   `must_change_password` **off** for that account or review dead-ends on the forced-change screen. **AC-67.**
4. Submit; both listings live and installable on a clean device, opening to a working sign-in. **AC-65.**
5. Note the INPDP declaration (loi 2004-63) named in `multi-tenant-cloud`'s owed decision #5 as a real prerequisite
   for a **public launch** — a gate on AC-65, not on implementation.

---

## Testing Strategy

**Backend — xUnit + Moq.** New: `ClientVersionMiddlewareTests` (Part 3), `RefreshTokenCommandHandlerTests` (Part 2),
`PushFanOutTests` / `PushDispatchJobTests` / `DeviceRegistrationTenantIsolationTests` (Part 6). Extended:
`ControllerAuthorizationCoverageTests` (Parts 3, 6), `DeploymentProfileTests` (Part 6).

Four **derived** guards will fight this feature and must be satisfied, not worked around: the authorization coverage
test (a named policy on every action, `[AllowAnonymous]` equal in **both** directions), `SystemWideCallerCoverageTests`
(US-2 — a Hangfire job must declare its cross-clinic read), `DeploymentProfileCoverageTests` (US-1 — no
`IsLocalMode(`), and `RealtimeResourceResolverTests` if any new command lands in a broadcasting namespace.

⚠️ **Smart App Control blocks the test runner on this machine** (`0x800711C7`). Build to a scratch
`-p:BaseOutputPath` and run `dotnet vstest`; **a red run is not evidence** until `bin/` + `obj/` are cleared and
`dotnet build-server shutdown` has run.

**Web — there is no test runner, no working ESLint and no CI.** The gate *is*: `npm run check:responsive` (13 checks
after Parts 1 and 3, all enforced once `PENDING_PARTS` is gone) · `npx tsc --noEmit` · `npm run build`, then an eye
pass at **320 / 390 / 820 / 1180 / 1440** plus a landscape phone plus a keyboard, recorded in `progress.md`. Both new
Part 1 checks and Part 3's are expressed as **greps** precisely so LEARNINGS `:225` ("standing up an FE runner is a
prerequisite") is not triggered mid-feature.

**Schema — `verify-schema` is the only migration gate** (nothing in the test project touches a database). Run it
before and after Part 6's migration and diff. Run `reconcile-money` before and after too: nothing here is financial,
so an **empty** diff is the assertion.

**Shells — operator-verified, not CI-runnable** (R-1 class, as `desktop/CLAUDE.md` states for the WPF shell).
Minimum walk per platform: cold-start login · the refresh boundary · background → push tap → correct screen · all six
file inputs including the camera · every download path including the >25 MB refusal · print · the five states · rotation
and Split View **without a remount** · the Google OAuth hand-off and the return.

---

## Risk Register

| ID | Risk | Likelihood | Impact | Part | Mitigation |
|----|------|------------|--------|------|------------|
| **R-1** | **One story spanning ~8 parts and 60+ files cannot complete in one session** — the sizing default was overridden deliberately. | High | Med | all | Parts are ordered vertical increments; **commit at every part boundary** and resume at the next. Split only at a boundary, never inside a part. Decision persists — do not re-ask. |
| **R-2** | **iOS is unverifiable here.** win32, no CI, and the project has never had an iOS device. A simulator does not exercise persistent cookies, print or biometrics faithfully. | High | High | 5, 7 | Part 5 stays BLOCKED with an explicit unblock condition; Part 4 (Android) proves the bridge contract first so iOS is a port, not a design. Do not mark done on a simulator. |
| **R-3** | **Part 6 depends on US-2, whose seams are still DRAFT** (`ITenantScope`'s three states, `SystemWideCallerCoverageTests`' derivation). ⚠️ **Reduced:** US-1 has landed, so `DeploymentProfile`'s shape is now fixed and readable. | Low | High | 6 | Read `ITenantScope` before executing Part 6; `SupportsOsPush` is one added capability on an existing file, so the US-1 coupling is closed. |
| **R-4** | **`SupportsOsPush` is the first capability derived from configuration, not from `Kind` alone** — which is precisely the `httpsConfigured` shape LEARNINGS `:45` records, and which `DeploymentProfile`'s own doc comment says every existing capability avoids. | Med | High | 6 | Keep the `Kind` half in the `For(kind)` matrix so `SelfHostedLan` is ✗ **whatever** an operator configures; only the credentials half is config-derived. Extend `DeploymentProfileTests`' matrix so the boundary is asserted, not assumed. |
| **R-4b** | **The connectivity bug is live, not prospective**, and the fix touches a shared provider: in `HostedMultiTenant` the probe 404s (`ExposesTrustEndpoints` ✗) while the web still polls on `AUTH_MODE === 'local'`, permanently disabling the AI chat and Google controls behind a false warning. | High | Med | 7 | Web-side only — the API half is already correct. Treat a 404 as *signal absent* (features enabled, no warning), never as offline. `SelfHostedLan` must stay byte-identical. |
| **R-5** | **The 14-site header migration touches money and clinical download paths with no FE test runner** — invoice PDFs, receipts, patient files, exports. | Med | High | 3 | Headers only; response handling untouched, so no error shape changes (LEARNINGS `:37`). Manual walk of all 14 listed in Part 3's validation. The `api-headers` check prevents regression. |
| **R-6** | **A global `@media print` block can affect every screen**, and Tailwind v4's config is CSS-based, so a `print:` variant collision is possible. | Med | Med | 1 | Scope by hiding named shell elements rather than broad resets; verify the three named surfaces plus one dialog and one card list; `npm run build` + eye pass. |
| **R-7** | **The deep-link target does not exist.** No `/appointments/[id]` route; the notification panel navigates nowhere, though the API endpoint is documented as its target. A tap would land on nothing. | High | Med | 7 | Add `?focus=<id>` to `/appointments` rather than a new route — the spec puts a redesign of an existing screen out of scope. Verified as part of AC-48. |
| **R-8** | **Re-setting the session cookie can break login** — a `Secure` flag re-derived from `NODE_ENV` or the internal proxy leg silently stops the browser storing it (LEARNINGS `:67`, and the YARP internal-leg trap at `:121`). | Med | High | 2 | One shared cookie-writing helper for `local-login` and `token`, driven by the explicit config flag. Test over both HTTP loopback and the HTTPS front door. |
| **R-9** | **Generated binary icons churn the diff** if a re-run is not deterministic. | Low | Low | 1 | Pin PIL resampling and output options in the script; commit the SVG master so regeneration is verifiable rather than trusted. |
| **R-10** | **25 MB base64 across a `@JavascriptInterface` string can OOM a low-memory Android device** (~33 MB of string). | Med | Med | 1, 4 | Refuse **before** reading the blob; the limit is configuration, not a rebuild; measure real radiographs on a real device and lower it if needed. |
| **R-11** | **Placing `ClientVersionMiddleware` before `UseAuthentication` deviates from the blueprint** and sits near `ExceptionMiddleware`. | Low | Med | 3 | Deliberate: the refusal needs no principal, and it makes a stale client's login 426 rather than 401 (AC-33). Emit through the canonical `{ error }` body so `ExceptionMiddleware`'s contract is not shadowed; `ClientVersionMiddlewareTests` pins the body. |
| **R-12** | **Android buildability on this machine is unverified** — no JDK/Android SDK check has been run. | Med | Med | 4 | Check for a JDK + Android SDK as Part 4's first step and report rather than assume. If absent, Part 4 becomes BLOCKED on tooling like Part 5, and Parts 1–3 still ship. |
| **R-13** | **`saveFile`'s semantics carry two jobs** (save, and open for the viewer), so an ambiguous contract could fork per platform. | Low | Med | 4, 7 | `mobile/shared/bridge.md` is the single contract and states both behaviours for the one method; the version bump in Part 7 is where any change is recorded. |

---

## Breaking Changes

- **`POST /api/auth/refresh` now returns a populated `refreshToken` and `refreshExpiresAt`.** Additive — both fields
  already exist on the response type, and a client that ignores them keeps working (AC-38). The BFF begins storing
  them, so a session's effective lifetime changes from a fixed 12 h to sliding.
- **A new 426 status appears on API routes** for clients sending a below-floor `X-Client-Version`. Browsers send no
  such header and are unaffected (AC-32); so are the BFF's server-side hops.
- **Eight API modules' header construction changes shape.** No behavioural change intended; R-5 covers the
  verification.
- **`@media print` now applies app-wide.** Printing any screen changes from "sidebar and AI launcher included" to
  document content only — the intended fix, but visible everywhere at once.
- **`PENDING_PARTS` removal enforces three checks immediately**, so any latent violation in `web/` fails the gate on
  the first run rather than later.
- **No public API contract is removed, and no money read changes.** `reconcile-money` must diff empty.

## Migrations

Only **Part 6**: one migration, two new tables (device registrations, push delivery outbox). **No existing table
changes and no backfill.** `verify-schema` runs before and after and is diffed — it is the only gate, since nothing
in the test project touches a database. ⚠️ `restore-backup` bumps every `User.TokenVersion`; device registrations
must survive it (AC-54), so the restore path is checked explicitly rather than assumed.

Parts 1, 2, 3, 4, 5, 7 and 8 require **no** schema change.

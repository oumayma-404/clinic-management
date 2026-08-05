# Exploration: Native mobile shells (Android + iOS)

**Date:** 2026-08-05
**Method:** three parallel `Explore` sweeps (web frontend readiness · API hosting/auth/CORS/push/files/versioning ·
packaging/tests/offline/feature-workflow), plus a direct read of `features/multi-tenant-cloud/plan.md` and
verification greps. Every claim below carries a path, and most carry a line.
**Consumed by:** `blueprint.md` (the challenged architecture decision) and `spec.md` (this feature).

---

## 1. What already exists — the expensive half of "mobile" shipped

| Asset | Evidence |
|---|---|
| A written, **mechanically enforced** device contract | `.claude/rules/frontend-web.md` (16 §), `web/scripts/check-responsive.mjs` — 11 checks, **all passing, exit 0** |
| Table→cards parity, exact | **28** `<Table[\s>]` across 23 files vs **28** `<CardList[\s>]` across 23 files, + 4 argued exemptions; `card-fallback` check derives the table list rather than listing it |
| Coarse-pointer touch floor | `globals.css:38` `@custom-variant coarse`, `:438`/`:503-507` `.touch-target`, `:479-484`/`:516` 44 px input floors. 51 `coarse:` usages in 20 files, 85 `touch-target` in 38 |
| Mobile viewport already correct | `web/app/layout.tsx:53-58` — `viewportFit: "cover"`, `interactiveWidget: "resizes-content"`; `maximumScale`/`userScalable` deliberately unset |
| Bottom-edge ownership | `globals.css:282` `--bottom-inset: calc(var(--bottom-bar-h) + env(safe-area-inset-bottom, 0px))`; `app-shell.tsx`, `bottom-nav.tsx`; `AppShell` on **25 of 29** `page.tsx` |
| Dialog→sheet | `components/ui/dialog.tsx:126-139` `mobile?: keyof typeof DIALOG_MOBILE_VARIANTS`, default `"bottom"`; 15 `mobile="sheet"` in 14 files |
| A web manifest | `web/app/manifest.ts` — `display:"standalone"`, `lang:"fr"`, `orientation:"any"` |
| Standalone-mode awareness | `dashboard-header.tsx:45,247` — `useMediaQuery("(display-mode: standalone)")` gates an in-app `router.back()` (AC-37) |
| iOS-aware blob delivery | `web/lib/download.ts` — 3 paths; `canShare({files})` → OS share sheet · coarse without file-share → `window.open` (**iOS Safari ignores `<a download>` on `blob:`**) · fine → anchor. `REVOKE_DELAY_MS = 60_000` because a synchronous revoke loses on iOS |
| Locked-phone-safe inactivity timer | `web/lib/auth/session.tsx:107,181-229` — absolute `lastActivityAtMs` re-checked on `visibilitychange`, because a frozen tab's `setTimeout` never fires |
| **A LAN device-trust flow for iOS *and* Android** | `API/Controllers/TrustController.cs` (4 × `[AllowAnonymous]`): `GET /api/trust` (self-contained French HTML), `/ca.crt` (DER, Android), `/profile.mobileconfig` (iOS), `/qr.png`. `Startup/TrustPortGate.cs` refuses every other path on port 5080. `Infrastructure/Security/AppleTrustProfile.cs` derives both UUIDs from the CA |
| **Auth already native-shaped** | `AuthController.Login` returns `{accessToken, refreshToken, expiresAt, refreshExpiresAt, mustChangePassword, user}` **in the body**; `AuthController.Refresh:89-104` takes `{refreshToken}` from the body. **The API sets no cookie — the Next BFF does.** So the BFF is not on a native client's critical path |
| A vetted thin-client precedent | `desktop/ClinicManagement.DesktopShell` — ~300 lines C# + 83 XAML, **one** PackageReference, standalone `.sln` outside `api/ClinicManagement.sln`, 4 view states, address in `%AppData%\ClinicManagement\server.json`, **zero** platform integration (no file picker, no print hook, no deep link, no JS bridge) |
| A hosted front door | `deploy/Caddyfile` + `deploy/docker-compose.prod.yml` — `cloud-deployment` shipped; TLS terminated once, `/api/*` → `api:5000`, everything else → `web:3000` |

---

## 2. The fourteen gaps

| # | Gap | Evidence |
|---|---|---|
| 1 | **Zero icon assets.** `manifest.ts:7-16` ships `icons: []` *deliberately, because the files are missing*; the 4 icons `layout.tsx:21-37` declares all 404. `web/public/` holds 5 untouched `create-next-app` SVGs | `manifest.ts`, `layout.tsx`, `web/public/` |
| 2 | **4 hand-rolled `<a download>` blob paths bypass `lib/download.ts`** → silently deliver nothing on iOS Safari; a 5th mechanism (`file-saver` `saveAs`) is a third answer to « télécharger » | `patient-files-manager.tsx:252-258`, `invoices-table.tsx:258-265,302-310`, `document-editor-content.tsx:1840-1847`; `saveAs` at `:1790` |
| 3 | **3 `blob:`-src `<iframe>` PDF previews** using `#toolbar=0&navpanes=0` — **Adobe/Chromium-only params**, and **Android WebView has no PDF viewer at all** | `patients/[id]/page.tsx:2714-2718`, `patient-files-manager.tsx:741-745`, `document-editor-content.tsx:3491-3495` |
| 4 | **Print = 2 × `window.open` + 1 `iframe.print()`; no `@media print` anywhere.** The only print CSS in the repo is a string injected into a popup | `document-editor-content.tsx:1942,1947,1984,2008`; `.claude/rules/frontend-web.md` § 15 |
| 5 | No `appleWebApp` metadata; no `themeColor` in the **`viewport`** export (Next 15 reads it there, so no `<meta name="theme-color">` is emitted); no splash | `web/app/layout.tsx` |
| 6 | **No OS push at any layer.** Zero repo hits for `FCM|APNs|Firebase|WebPush|VAPID|DeviceToken|DeviceRegistration|serviceWorker`. `ClinicHub.cs:19-22` carries only a resource key, and a SignalR socket delivers **only while foregrounded** — iOS tears it down seconds after backgrounding | repo-wide grep; `Hubs/ClinicHub.cs` |
| 7 | **`/api/auth/refresh` does not rotate.** `RefreshToken` stays `string.Empty` and `RefreshExpiresAt` stays `null`, so the 12 h refresh token never slides ⇒ **password re-entry every 12 h** | `RefreshTokenCommand.cs:36-93`; `LoginResultDto` doc comments; `LocalAuthConfig.cs:20-21` (30 min access / 720 min refresh) |
| 8 | **No client↔server version negotiation anywhere.** Zero hits for `MinClientVersion|X-Client-Version|clientVersion|api/version`. Harmless **only because the server serves the web bundle** | grep across `API/{Controllers,Program.cs,Startup}`, `web/lib`, `desktop/` |
| 9 | **Static export structurally blocked** — 7 causes, one fatal: `middleware.ts` **is** the entire Auth0 login/logout/callback surface (`auth0.middleware(request)`, and `app/bff/auth/[...auth0]/` is an **empty directory**) | `next.config.ts:4` (`output:'standalone'`), `:26` (`async headers()`); `middleware.ts:52,61`; 5 handlers under `app/bff/auth/` all `runtime='nodejs'`; `layout.tsx` reads `AUTH_MODE` server-side; 3 dynamic segments with **zero** `generateStaticParams`; `next/image` in 4 files (3 without `unoptimized`); `next/font/google` |
| 10 | `fetch('/bff/auth/token')` is **relative and the only token source** for REST *and* SignalR; `apiPost/Put/Delete/*FormData` never resolve the base against an origin (only `apiGet` does); 21 × `credentials:'include'`; the hub URL resolves against `window.location.origin` | `client.ts:362,418-419,436-478`; `clinic-hub.ts:60-65` |
| 11 | **CORS allow-list is exactly `["http://localhost:3000"]`**, and `AllowCredentials()` structurally forbids a wildcard | `Program.cs:329-339`; `Infrastructure/CorsOrigins.cs:52-57`; `appsettings.json:134-137,162` |
| 12 | **LAN cert trust is a two-step on iOS** — the `.mobileconfig` leaves the root **inert** until *Certificats de confiance* is toggled, and nothing served can flip it. Plus **AC-46 unverified** (5-y leaf vs Apple's 398-day cap) and **a DHCP lease change breaks TLS** (SANs fixed at generation; `TryLoadExisting` never re-checks them) | `AppleTrustProfile.cs:17-23`; `CertificateProvisioner.cs`; `TrustController.cs:264-265`; `packaging/README.md:430-444`; `mobile-tablet-responsive/plan.md:592` |
| 13 | **No contract a second client could build against**: no versioning (`[ApiVersion]` absent, every route a flat literal), Swagger **`IsDevelopment()`-only** with **no `AddSecurityDefinition`**, no committed OpenAPI, no codegen — and the success envelope is **inconsistent per endpoint** (`Result<T>`-wrapped on auth/clinics/users/reminder-settings, bare DTO elsewhere, **no marker**). The TS contract is a **1269-line hand-maintained** `types.ts` with nothing asserting the mirror | `Program.cs:189-209,491-495`; `web/lib/api/types.ts`; `web/lib/CLAUDE.md` |
| 14 | **Nobody has considered the stores.** No repo mention of App Store, Play Store, review, privacy manifest, or a Mac build | repo-wide grep |

**Secondary, each real:** `POST /api/auth/setup` is **loopback-gated** (`LocalRequest.IsLoopback`) so a LAN device can never do first-run setup · Google Calendar OAuth redirects to `FrontendUrl` (`appsettings.json:116`, `GoogleCalendarController.cs:412`) so it needs a system-browser hop · the Auth0 tenant is a **Regular Web App** (`AUTH0_SETUP.md:15`), i.e. confidential, with scope `openid profile email` and **zero** mention of PKCE / Native / `offline_access` · `ai-chat.tsx:190` `webkitSpeechRecognition` is dead on iOS · `LocalAuthEnforcementMiddleware` does a **DB round trip per authenticated request** and returns `{error, code:"must_change_password"}` with **403**, which any client must route on.

---

## 3. `HostedMultiTenant` (`features/multi-tenant-cloud/plan.md`, DRAFT) removes four gaps

Verified **not yet built**: no `DeploymentProfile.cs`, no `ITenantScope`/`TenantScope` anywhere under `api/`.

| Gap | Under `HostedMultiTenant` |
|---|---|
| **12** cert trust | **Gone** — Caddy, real CA, real domain. `ExposesTrustEndpoints` ✗ |
| **6** push impossible offline | **Solvable** — internet-connected by construction |
| Auth0 native app + PKCE | **Not needed** — `UsesLocalAccounts` ✓, tokens in the body |
| **14** review can't reach a LAN server | **Gone** — public store viable |

**Convergence:** that plan's **owed decision #3** is verbatim this feature's version floor — *"the shell has no version
check; a cloud API that moves under N pinned clients breaks them silently."*

**Constraints it imposes on anything added here:**
- `DeploymentProfile` becomes the mode authority — **do not write `IsLocalMode(`**; `DeploymentProfileCoverageTests`
  (US-1, derived) asserts the only occurrence is inside `Resolve`.
- `ITenantScope` is three-valued (`Unset`/`Clinic`/`SystemWide`). **`SystemWideCallerCoverageTests` (US-2, derived)
  requires every Hangfire job class to contain a `UseSystemWide` call.** Without it a new job reads **zero rows and
  logs success** — R-1 of that plan.
- US-3: `Register` **404s**, `setup` becomes a console verb ⇒ onboarding is admin-provisioned + temp password +
  forced change.
- US-6: per-clinic rate-limit partition; CSP promoted from `Report-Only` to enforcing (behind a flag).
- Owed decision #1 (offline) records the decisive reason against a client cache: **gapless per-year invoice
  numbering cannot be reconciled after a partition** — gaps and duplicates are both legally significant.

---

## 4. Webview realities that force a small bridge

| Platform | Fact | Consequence |
|---|---|---|
| Android `WebView` | **`window.print()` not implemented** | Both print paths dead; needs `PrintManager` + `createPrintDocumentAdapter()` |
| Android `WebView` | **`DownloadListener` never fires for `blob:`**, and `navigator.share` is a Chrome feature **absent from WebView** | `downloadBlob`'s `canShare` → false → `window.open(blob:)` → nothing. **All** delivery paths fail |
| Android `WebView` | **No PDF viewer at all** | Gap #3's iframes render blank |
| Android `WebView` | `WebChromeClient.onShowFileChooser` must be implemented | Otherwise **all 6** `<input type="file">` sites silently do nothing — including the CSV import |
| Android `WebView` | `setDomStorageEnabled(true)` required | The 3 `localStorage` keys + 1 `sessionStorage` key stop working |
| iOS `WKWebView` | `window.print()` unreliable → `viewPrintFormatter()` + `UIPrintInteractionController` | ⚠️ **Verify on hardware** |
| iOS `WKWebView` | `WKWebsiteDataStore.default()` (persistent) required | Else the `local_session` cookie dies on every cold start |
| iOS `WKWebView` | `<input type="file">` works natively (camera + library + Files) | Camera capture needs **no** bridge message |
| both | Report-Only CSP already allows `img-src 'self' data: blob:` and `frame-src 'self' blob:` | Blob rendering is anticipated; ⚠️ re-check when US-6 promotes CSP to enforcing |

⇒ **Three bridge messages are unavoidable: print · blob save · push token.** Feature-detected, so the web app
behaves identically when absent.

**Capacitor re-examined:** a *fixed* hosted domain removes the objection that killed it (`server.url` is build-time
only). Rejected anyway — the repo's bar for a thin client is ~300 lines with **zero** toolchain and a solution
deliberately outside `api/`, there is **no CI**, and a three-message bridge is a small win against a large
dependency surface.

---

## 5. Guard tests that will fight this feature

All derived — they fail on arrival, not on a list someone forgot to edit.

| Guard | Demand |
|---|---|
| `Api/ControllerAuthorizationCoverageTests.cs` | A **named policy on every action, no exemption list**; `[AllowAnonymous]` set must **equal** `ExpectedAnonymous` **in both directions**; no bare class-level `[Authorize]`; every defined policy applied and registered (`[Theory]` over both modes) |
| `Common/Behaviors/RealtimeResourceResolverTests.cs` | Backend-emitted keys **equal** those parsed out of `web/lib/realtime/clinic-hub.ts`, both directions. Locates the TS file by **`[CallerFilePath]`** and **throws** rather than skipping. Both allow-lists asserted **empty** |
| `Api/ClinicalRecordAccessTests.cs` | The charter as data; `Every_Action_Of_These_Controllers_Is_Classified_By_This_Test` fails on an unclassified new action |
| 9 × `*TenantIsolationTests` | Another clinic's row reads as "not found"; US-2 adds an `Unset`-scope case per aggregate |
| `Features/Common/ConcurrencyConflictTests.cs` | Every `Entity<>` carries the token; `ConflictException` escapes the catch-alls |
| `verify-schema` console verb | **The only migration gate** — nothing in the test project touches a DB. Run before/after and diff |
| `reconcile-money` console verb | Before/after, diffed |

**`web/` has no test runner, no working ESLint, no CI.** Gate = `npm run check:responsive` + `npx tsc --noEmit` +
`npm run build` + a recorded eye pass at **320 / 390 / 820 / 1180 / 1440**. `features/LEARNINGS.md:225` names
standing up an FE runner an **explicit prerequisite** if a phase needs automated FE coverage — avoided here by
expressing both new checks as greps in `check-responsive.mjs`.

⚠️ **Smart App Control** blocks the runner (`0x800711C7`): build to a scratch `-p:BaseOutputPath`, run
`dotnet vstest`; a red run is not evidence until `bin/`+`obj/` are cleared and `dotnet build-server shutdown` ran.

---

## 6. House workflow facts

- **76** dirs under `features/`. Minimal feature = `spec.md` + `progress.md`; full pipeline = `exploration.md` →
  `spec.md` → `design.md` (+`mockups/`) → `plan.md` → `stories/`.
- `mobile-tablet-responsive/spec.md` is the format model: front matter (Status · Challenged · **Type** · **Scope** ·
  Created · Exploration · Feature) → Overview → **What Changes** (per phase) → **Acceptance Criteria numbered
  continuously** (AC-1 … AC-51, *not* `AC-x.y`) → Data/Schema Changes → **Out of Scope (each bullet argued)** →
  Edge Cases (critical only) → Verification.
- `features/LEARNINGS.md` entries that bear directly here: `:27` judge reachability at the server in LAN topologies ·
  `:34` one HTTP client wrapper · `:39` reflection allow-list as a regression net · `:151` guard `window` in
  server-importable modules · `:191` `ApiError(0)` must be handled at every call site · `:225` no FE runner.
- **Nothing in the repo previously planned a native app.** `mobile-tablet-responsive/spec.md:460` makes it an
  explicit non-goal — *"not as a Capacitor or React Native shell. **If one is wanted later, nothing here precludes
  it.**"* This feature is that later, so the spec **amends** that non-goal rather than contradicting it silently.
- Branch: off `main`. ⚠️ Not off `feature/audit-sections-3-to-10`, which carries unrelated in-flight work.

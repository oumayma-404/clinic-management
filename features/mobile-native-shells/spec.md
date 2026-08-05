# Feature Specification: The clinic on a phone — native Android & iOS shells

**Status:** DRAFT
**Challenged:** No — run `/challenge-spec` before `/plan-feature`
**Type:** Large (6 phases; Phase 0 ships independently, Phases 1–5 in order)
**Scope:** FE + BE + a new `mobile/` client + packaging/store operations
**Created:** 2026-08-05
**Exploration:** `features/mobile-native-shells/exploration.md`
**Blueprint:** `features/mobile-native-shells/blueprint.md` (carries the challenged architecture decision)
**Depends on:** `features/multi-tenant-cloud/plan.md` — **US-1** (`DeploymentProfile`) and **US-2** (`ITenantScope`)
**Branch:** off `main`. ⚠️ Not off `feature/audit-sections-3-to-10`
**Feature:** Put the product on the App Store and Google Play as two thin native shells that render the hosted
server's **own** web bundle — so a dentist and their assistant(e) can work from a phone or tablet with real
notifications, a camera, biometric resume, and working print and downloads — without forking the frontend,
duplicating the API contract, or reimplementing a single screen.

---

## Overview

The app is already usable on a phone. `features/mobile-tablet-responsive/` P1–P6 shipped a written, **mechanically
enforced** device contract: 28 tables each with a card form, 44 px targets gated on a **coarse pointer** rather than
a width, dialogs that become sheets below `md:`, `dvh` everywhere, one owner for the bottom edge, and an `AppShell`
on 25 of 29 pages. What it deliberately stopped short of is stated in its own words (`spec.md:460`):

> **A native application.** … This feature closes that gap as **responsive web plus home-screen install**, not as a
> **Capacitor** or **React Native** shell. **If one is wanted later, nothing here precludes it.**

This is that later. **It amends that non-goal rather than contradicting it silently.**

Three facts set the shape of the work.

**One — the hosted topology removes most of the hard problems.** `features/multi-tenant-cloud/plan.md` introduces
`HostedMultiTenant`: hosted data, own JWT, Caddy as the single front door on a real domain with a real certificate.
That deletes four whole problem classes outright — no self-signed CA to distribute, no iOS *Certificats de
confiance* two-step that nothing served can flip, no DHCP-lease TLS breakage, no Auth0 native client with PKCE (our
own `LocalAuthService` already returns both tokens **in the response body**), and no "App Review cannot reach a LAN
server". Mobile is only worth doing on this topology, and this spec targets it exclusively.

**Two — the web app must not be forked, and it does not have to be.** The shells load the server's own origin, so
`middleware.ts` works, `/bff/auth/token` resolves, all 21 `credentials: 'include'` calls work, the SignalR hub URL
resolves correctly, and no CORS entry is needed. This is exactly why `mobile-tablet-responsive` put `desktop/` out
of scope: *"It renders whatever the browser renders, so it inherits every improvement here and imposes no
constraint of its own."* A static export would instead mean **reimplementing Cloud login**, because `middleware.ts`
*is* the entire Auth0 surface — there are no `/auth/*` route files at all.

**Three — a webview is not a browser, and three things genuinely break in it.** Android `WebView` does not
implement `window.print()`, its `DownloadListener` **never fires for `blob:` URLs**, it has **no `navigator.share`**
and **no PDF viewer at all**. So the app's two print paths, every download path, and all three PDF previews fail
there — and four download call sites are already broken on iOS Safari today because they bypass the correct helper.
Fixing those is Phase 0: it is web work, it improves every mobile **browser** too, and it closes the two criteria
`mobile-tablet-responsive` P7 left open — that spec's **AC-36** (icon assets) and **AC-40** (print stylesheet).
⚠️ Those two numbers belong to *that* spec; this spec's own AC-36 and AC-40 are unrelated.

The governing rules, stated once and used to settle every scope question below:

> **1. The web app never depends on the shell.** Every shell capability is feature-detected; with the bridge absent
> the app behaves exactly as it does in a browser. One bundle, one contract, no shell-only code path.
>
> **2. No capability is removed by a layout or platform decision.** Where a platform genuinely forbids something,
> the user gets an explicit French message — never a silently dead control.
>
> **3. A limit is a field, not a footnote.** Where a figure or feature is bounded (push unavailable on a LAN
> install, a file too large to hand across the bridge), the product says so where the user is looking — the way
> `ceilingIsDefault`, `seesThisClinicOnly` and `ClinicWideOutgoings` already do.

---

## What Changes

### Phase 0 — The web fixes a webview makes load-bearing *(no dependency; ships first, alone)*

*Closes `mobile-tablet-responsive` P7's open AC-36 and AC-40, and adds four findings that spec never had.*

**Icons exist.** The four icons `layout.tsx:21-37` declares do not exist, so `apple-touch-icon` 404s and
`manifest.ts:7-16` ships `icons: []` **deliberately**, with a comment saying why. That reason expires here. Real
192/512/maskable/apple assets land, the manifest lists them, `appleWebApp` metadata appears, and `themeColor` moves
into the **`viewport`** export — Next 15 reads it there, so `manifest.ts`'s `theme_color` alone currently emits no
`<meta>` at all.

**There is one way to deliver a file.** `web/lib/download.ts` is already correct and already three-path
device-aware. **Four call sites never learned about it** and hand-roll an `<a download>` on a `blob:` URL — which
iOS Safari ignores, so they silently deliver nothing — and a fifth uses `file-saver` as a third mechanism. All five
route through the one helper, and a **derived** check in `check-responsive.mjs` fails any future sixth.

**A PDF preview works where there is no PDF plugin.** The three `<iframe>` previews pass `#toolbar=0&navpanes=0`,
which are Adobe/Chromium-only parameters, and render **blank** in Android WebView. On a coarse pointer the inline
preview is replaced by the already-correct share/open path.

**Printing prints the document.** There is no `@media print` anywhere; the only print CSS in the repo is a string
injected into a `window.open` popup, so printing any screen prints the sidebar and the AI launcher.

### Phase 1 — Two shells, and a bridge of exactly three messages

A Kotlin/`WebView` app and a Swift/`WKWebView` app in a new top-level **`mobile/`** — own toolchain, own
`CLAUDE.md`, in neither the .NET solution nor `web/`, exactly how `desktop/` is carved out. Each loads the hosted
origin, remembers it, and renders **five** French states: `WebView` · `Connecting` · `ServerConfig` ·
`Unreachable` · **`UpdateRequired`** (Phase 2's). `desktop/`'s two hard-won details carry over: *navigate*, never
re-assign a source, or « Réessayer » does nothing when the address is unchanged; and persist the address rather
than compiling it in, so one binary serves production, staging and App Review.

The bridge is **three feature-detected messages** — `saveFile` · `print` · `onPushToken` — plus a `version`. It
takes **base64 bytes, not a `blob:` URL**, because Android cannot see a blob. Adding a fourth message requires
editing the contract document and bumping the version.

### Phase 2 — A stale app says so *(closes `multi-tenant-cloud` owed decision #3)*

Skew is invisible today **only because the server serves the bundle**. A shipped binary ends that, and that plan
already records the consequence: *"a cloud API that moves under N pinned clients breaks them silently."* The shell
sends its version; below a configured floor the API answers **426** with `code: "client_too_old"`, and the shell
shows `UpdateRequired` with a store link. An **absent** header passes, because browsers send none.

### Phase 3 — A backgrounded phone still knows *(⚠️ requires multi-tenant US-1 + US-2)*

A SignalR socket delivers only while the app is foregrounded; iOS tears it down seconds after backgrounding. So
**time-critical** staff notifications — the three appointment categories, the ~24 h reminder, and the post-visit
review — also go out as an OS push. Operational alerts (low stock, expiring stock, stale backup, failed reminder)
deliberately do **not**: a channel that buzzes at 22:00 about a box of gloves gets muted, and then the urgent ones
are lost with it.

Push is a **named capability on `DeploymentProfile`**, not a mode check, because FCM and APNs are internet
services: `SelfHostedLan` cannot have it at all, and the product must say so rather than ship a feature that never
fires.

### Phase 4 — The phone becomes an instrument, not a viewer

Camera capture into the patient record — the phone *is* the imaging device, which is a **different and better**
answer to `adoption-qa-l` L7's bridge than the desktop-shell-vs-local-helper question that currently blocks it.
Biometric resume, because a 30-minute password prompt at the chair with gloved hands is a policy people work
around. A native PDF viewer for the platform that has none. Deep links, so a notification tap lands on the right
screen. And **refresh-token rotation**, because the token never slides today and staff retype a password twice a
day.

### Phase 5 — Two store listings

Apple's guideline 4.2.2 rejects a bare webview wrapper, which is why submission comes **after** Phases 3–4 give it
push, camera, biometrics and native print. A seeded demo clinic on the hosted domain makes review possible at all.

---

## User Stories

### US-1: One way to deliver a file, print, and install *(Phase 0)*
As a **dentist using the app in any mobile browser**, I want downloads, PDF previews, printing and home-screen
install to actually work, so that the app is usable on a phone before any store app exists.

### US-2: Install from the store and sign in *(Phase 1)*
As a **secretary**, I want to install the clinic app from the App Store or Google Play and sign in with my existing
account, so that I reach the agenda without typing a server address or a URL.

### US-3: A stale app tells me to update *(Phase 2)*
As a **staff member whose phone has not updated in months**, I want a clear French message telling me to update,
so that I am not left guessing at broken screens or a login that appears to fail.

### US-4: My session lasts the working day *(Phase 4, backend)*
As a **dentist**, I want to stay signed in across a normal day, so that I am not retyping a password twice a day
between patients.

### US-5: My phone tells me when something changes *(Phase 3)*
As a **dentist away from the desk**, I want a notification on my locked phone when an appointment is booked,
cancelled or moved, so that I learn about it without keeping the app open.

### US-6: The phone is part of the clinical record *(Phase 4)*
As a **dentist**, I want to photograph a tooth straight into the patient's file and resume the app with my
fingerprint, so that the phone earns its place at the chair.

### US-7: The apps are installable from both stores *(Phase 5)*
As a **clinic owner**, I want to find the app in the App Store and Google Play, so that onboarding a new assistant
means "install the app", not "let me send you a file".

---

## Functional Requirements

### FR-1: One file-delivery path
All file delivery — patient files, invoice PDFs, e-invoice XML, document PDFs, the Word export, CSV exports — shall
go through the single existing helper. The helper shall try, in order: the shell's `saveFile` when present; the OS
share sheet when the platform offers file sharing; a new tab; a direct download. It shall never require a
`blob:` URL to survive a navigation. A file above a configured size shall be refused with an explicit French
message naming the limit, never a silent failure.

### FR-2: PDF preview without a PDF plugin
On a coarse pointer, a PDF shall be delivered through FR-1 rather than embedded in an inline frame. Viewer-specific
URL fragments shall not appear anywhere. Where a preview cannot be shown, the user shall be offered the file.

### FR-3: Print produces the document
Printing any screen shall produce the document content without navigation chrome, the sidebar, or the assistant
launcher. In a shell, print shall be handed to the OS print service. Where printing is unavailable, the user shall
see a French message and be offered the file instead.

### FR-4: Installable identity
The app shall present a complete icon set (including a maskable variant with the platform safe zone), a theme
colour, and Apple web-app metadata, such that neither a browser install nor a store listing shows a blank or
cropped tile.

### FR-5: Shell shape
Each shell shall render the hosted origin in a full-screen webview with persistent cookie storage, DOM storage
enabled, no mixed content, back/forward gestures, and safe-area insets reaching the page. It shall present exactly
five mutually exclusive French states and offer « Réessayer », « Changer de serveur… » and « Recharger ». It shall
persist the server address per install and shall not compile it in.

### FR-6: Bridge contract
The shell shall expose exactly three methods and a version on a single global object. Every web-side call site
shall be feature-detected, and behaviour with the object absent shall be identical to today's browser behaviour.
The contract shall live in one document, and a change to it shall require a version bump.

### FR-7: Native file input and camera
Every existing file input shall open the platform picker, and an image input shall additionally offer the camera.
No file input shall be inert.

### FR-8: Client version floor
The API shall accept a client-version header, expose the current floor and the current release anonymously, and
refuse a client below the floor with a distinct status and machine-readable code plus a French message. An absent
or malformed header shall be accepted. The floor shall be operator-configurable without a redeploy.

### FR-9: Session continuity
Each refresh exchange shall issue a fresh refresh credential with a new expiry, so an active user is not forced to
re-authenticate on a fixed clock. Global revocation, deactivation and forced-password-change behaviour shall be
unchanged. A failed exchange shall be indistinguishable between expired, revoked and forged.

### FR-10: OS push
Time-critical staff notifications shall be delivered as an OS notification to each of the recipient's registered
devices, in addition to the existing in-app feed. The actor shall not be notified of their own action. A payload
shall carry no patient-identifying content. A tap shall open the screen the notification refers to. A device token
reported invalid by the platform shall be deactivated, not retried. Push shall be available only where the
deployment supports it, and where it does not the product shall say so.

### FR-11: Device registration
A signed-in user shall be able to register and deregister a device for push. A registration shall belong to one
clinic and one user, shall record its platform and shell version, and shall survive a database restore.

### FR-12: Biometric resume
Where the device offers biometrics or a device credential, resuming a backgrounded app past the inactivity limit
shall require that check instead of a password. The check shall re-authorise the existing session only; it shall
not extend the underlying credential's lifetime, and it shall not store a password on the device. A device with no
biometrics shall fall back to the password.

### FR-13: Reachability on a mobile network
The app shall detect loss of reachability to the server from the client side and surface it, so that a phone
leaving coverage does not present itself as online. Server-to-internet egress shall remain a separate, independent
signal.

### FR-14: Store readiness
Each app shall carry the platform's required privacy declarations for health-adjacent data, a stable bundle
identity, a single source for its version, and a reachable demo tenant with seeded data and credentials for review.

---

## Acceptance Criteria

*Numbered continuously, per the house convention.*

**Phase 0 — web fixes**

- **AC-1:** Icon assets exist for 192, 512, a maskable 512 with the safe zone honoured, an Apple touch icon, the
  light/dark 32 px favicons and the SVG — i.e. every icon the app declares resolves, with no 404.
- **AC-2:** The manifest lists those icons; installing from Chrome on Android and from Safari on iOS produces a
  correct, uncropped home-screen tile.
- **AC-3:** `themeColor` is emitted as a `<meta>` tag, and Apple web-app metadata is present.
- **AC-4:** All five file-delivery call sites that bypass the shared helper now use it; downloading a patient file,
  an invoice PDF, an e-invoice XML, a document PDF and the Word export each **deliver a file on iOS Safari**.
- **AC-5:** A mechanical check fails on any anchor-download, `saveAs`, or programmatic anchor click outside the
  shared helper. The check derives its targets; it carries no per-file exemption list.
- **AC-6:** No viewer-specific URL fragment (`toolbar`, `navpanes`) remains anywhere; a mechanical check enforces it.
- **AC-7:** On a coarse pointer, opening a PDF in the patient record, the files manager and the document editor
  delivers the file rather than embedding a frame; the user always ends up with a viewable document.
- **AC-8:** Printing `/factures`, a patient record and a document produces only document content — no sidebar, no
  navigation, no assistant launcher.
- **AC-9:** The stale `PENDING_PARTS` set is removed from the responsive checker, and `.claude/rules/frontend-web.md`
  § 15 no longer lists the manifest, the icons or the LAN trust page as missing.
- **AC-10:** The voice-input control is absent, not inert, where speech recognition is unsupported.
- **AC-11:** `npm run check:responsive`, `npx tsc --noEmit` and `npm run build` all pass, with the warning count
  identical to the pre-change baseline.

**Phase 1 — the shells**

- **AC-12:** Both shells build to an installable artifact and open the hosted app full-screen, with no address bar
  and no browser chrome.
- **AC-13:** Signing in works, and the session **survives a cold start** — a relaunched app is still signed in.
- **AC-14:** Each shell presents exactly the five states, all French, each reachable in a test: connected · loading
  · address entry · unreachable (naming the address and the reason) · update required.
- **AC-15:** « Réessayer » re-attempts the **same** address successfully after the server returns.
- **AC-16:** The address persists across relaunch and is changeable from the UI without reinstalling.
- **AC-17:** All six file inputs open the platform picker on both platforms; an image input offers the camera.
- **AC-18:** Every file-delivery path from AC-4 delivers a file **in both shells**, including on Android where blob
  downloads and file sharing are unavailable.
- **AC-19:** Printing works from both shells through the OS print service.
- **AC-20:** Safe-area insets reach the page: the bottom navigation clears the home indicator on a notched iPhone
  and the gesture bar on Android.
- **AC-21:** Rotation and iPad Split View **do not remount** the app across the 768 px hinge, and typed input in an
  open dialog survives the change.
- **AC-22:** The back gesture on Android and the swipe-back on iOS navigate within the app rather than closing it.
- **AC-23:** With the bridge object deleted at runtime, every affected screen behaves exactly as it does in a
  browser — no error, no dead control.
- **AC-24:** The bridge contract document lists exactly three methods, and the shell reports a version.

**Phase 2 — version floor**

- **AC-25:** An anonymous request returns the minimum supported client version, the current release, and both store
  URLs — and it succeeds **without** authentication, so a too-old client can ask before signing in.
- **AC-26:** A client below the floor receives **426** with the canonical `{ error }` body plus
  `code: "client_too_old"`, on every API route.
- **AC-27:** A request with no client-version header, or a malformed one, is accepted unchanged — the web app is
  unaffected.
- **AC-28:** A below-floor shell shows the `UpdateRequired` state with a working store link, and does **not**
  present itself as signed out.
- **AC-29:** The floor is read from operator-owned configuration and takes effect without a code change; the
  anonymous route is added to the pinned anonymous-endpoint set (which is asserted equal in both directions).

**Phase 4 — session continuity** *(backend; lands with or before Phase 3)*

- **AC-30:** A refresh exchange returns a **new** refresh credential and a new expiry; an active client is never
  forced to re-authenticate on a fixed clock.
- **AC-31:** A deactivated account, a bumped token version, and a forced password change all still take effect on
  the next exchange, with one indistinguishable failure message for expired, revoked and forged.
- **AC-32:** A user active all day is not prompted for a password by the passage of time alone; a user idle past
  the refresh window is.
- **AC-33:** The web app and the desktop shell continue to work unchanged against the new behaviour.

**Phase 3 — OS push**

- **AC-34:** A device registers on first sign-in and re-registers when its token changes; deregistration on sign-out
  stops delivery to that device.
- **AC-35:** With the app **backgrounded or the phone locked**, booking, cancelling and rescheduling an appointment,
  the ~24 h reminder, and a post-visit review each produce an OS notification on both platforms.
- **AC-36:** Low stock, expiring stock, stale backup and failed reminder produce **no** OS notification, while still
  appearing in the in-app feed.
- **AC-37:** The actor receives no push for their own action.
- **AC-38:** No notification payload contains a patient name or any other identifying detail — verified by reading
  what is sent, not by inspecting a banner.
- **AC-39:** Tapping a notification opens the screen it refers to, from cold start and from background.
- **AC-40:** A token the platform reports as unregistered deactivates that device registration and is not retried.
- **AC-41:** A registration whose channel is unconfigured leaves the dispatch scan rather than accumulating at its
  front — a queue of unsendable rows cannot starve later ones.
- **AC-42:** Where the deployment does not support push, the registration route is absent, no dispatch job is
  registered, and the settings surface states plainly that OS notifications are unavailable on this installation.
- **AC-43:** One clinic's device registrations are unreadable from another clinic, including when no clinic is in
  scope.
- **AC-44:** A database restore does not silently unsubscribe a clinic's devices.
- **AC-45:** A push send never fails, delays or rolls back the clinical or financial operation that caused it.

**Phase 4 — native capability**

- **AC-46:** A photo taken in the shell attaches to the patient's record and appears in the files list on the web
  app, with the same validation as an uploaded file.
- **AC-47:** Resuming past the inactivity limit requires a biometric or device-credential check and restores the
  session **without a password**; three failures fall back to the password screen.
- **AC-48:** No password is stored on the device — verified by inspecting what the shell persists.
- **AC-49:** A device without biometrics falls back to the password with no dead control and no error.
- **AC-50:** A PDF opens in a working viewer on Android.
- **AC-51:** Losing the mobile network surfaces an unreachable-server state within one poll interval and recovers
  automatically; server-to-internet egress remains a separate signal that does not mask it.

**Phase 5 — stores**

- **AC-52:** Both listings are live, installable on a clean device, and the app opens to a working sign-in.
- **AC-53:** Each app declares its data collection for health-adjacent data on both stores.
- **AC-54:** A reviewer can sign in to a seeded demo clinic on the hosted domain with the credentials in the review
  notes, and is **not** blocked by a forced password change.
- **AC-55:** Each app's version comes from one source, and the version floor of Phase 2 reads it — the hardcoded
  version already duplicated across three packaging files does not become four.
- **AC-56:** The blueprint's amendment to `mobile-tablet-responsive`'s "no native application" non-goal is recorded
  in that spec, so the two documents do not disagree.

**Cross-cutting**

- **AC-57:** No new occurrence of the retired mode check exists anywhere; the deployment question is asked as a
  named capability.
- **AC-58:** Every new API action carries an explicit named policy; no bare authorize attribute is added.
- **AC-59:** Any new background job declares its cross-clinic read explicitly, so it cannot silently read nothing.
- **AC-60:** Schema verification passes before and after the migration, and the money reconciliation diff is
  **empty** — nothing here touches money.

---

## API Endpoints

### Client requirements *(new)*
```
GET /api/meta/client-requirements
(anonymous — a too-old client must be able to ask before it can authenticate)

Response 200:
{
  "minimumShellVersion": "1.2.0",
  "currentShellVersion": "1.4.1",
  "storeUrls": { "ios": "https://...", "android": "https://..." }
}
```

### Any API route, called by a too-old client *(new behaviour)*
```
X-Client-Version: 1.0.0        (absent or malformed ⇒ accepted unchanged)

Response 426:
{ "error": "Cette version de l'application n'est plus prise en charge. Veuillez la mettre à jour.",
  "code": "client_too_old" }
```

### Device registration *(new)*
```
POST /api/push-devices
Authorization: Bearer <token>          Policy: any clinic role
Absent where the deployment does not support push (404)

Request:
{ "token": "…", "platform": "ios" | "android", "shellVersion": "1.4.1" }

Response 200: { "id": "…", "registeredAt": "…" }
Response 400/404/409: { "error": "…" }
```
```
DELETE /api/push-devices/{token}
Authorization: Bearer <token>          Policy: any clinic role
Response 204 · Response 404: { "error": "…" }
```

### Refresh *(modified — response gains two already-defined fields)*
```
POST /api/auth/refresh                 (anonymous, rate-limited)

Request:  { "refreshToken": "…" }

Response 200:
{ "isSuccess": true,
  "value": { "accessToken": "…", "expiresAt": "…",
             "refreshToken": "…",        // NEW — was always empty
             "refreshExpiresAt": "…",    // NEW — was always null
             "mustChangePassword": false, "user": { … } },
  "error": null }

Response 401: { "error": "…" }   // one message for expired · revoked · forged
```
⚠️ The two fields already exist on the response type and the web BFF already reads them. This is a **behaviour**
change, not a contract change — but a client that ignored them keeps working, which is what makes it safe.

---

## Device & Interface Behaviour

**Leading device:** the **phone**, for the first time in this repo. Every prior feature led with the tablet at the
chair. Here the shell's own screens exist *only* on a handset or tablet, and there is no desktop fallback for them.

The web app inside the webview inherits the whole existing contract unchanged
(`.claude/rules/frontend-web.md`) — this spec adds nothing to it and removes nothing from it. What is specific here
is the **native** surface:

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| The five shell states | Full-screen, single column, one primary action at the bottom within thumb reach; message text wraps at 320 px without truncation | Same, centred with a max width — a full-width form on a 10″ tablet reads as an error page | n/a |
| Server address entry | Full-screen; keyboard-aware so the primary action stays visible; URL keyboard type; the field accepts a bare host, `host:port` or a full URL, as `desktop/ServerConfig.ParseAddress` already does | Same, centred | n/a |
| Biometric prompt | OS-native sheet; a visible ≥44 px « Utiliser le mot de passe » escape is always present | Same | n/a |
| Update-required state | Full-screen, one store button, and the current/required versions shown as text so a phone call can diagnose it | Same | n/a |

- **Touch paths:** every shell control is a real ≥44 px target; nothing in the shell is hover-revealed, since there
  is no hover on these devices.
- **Named exceptions** — each shows an explicit French message, never a dead control:
  - **OS push on a self-hosted LAN install.** Structurally impossible (FCM/APNs are internet services; on iOS there
    is no alternative at all). The settings surface states it.
  - **Biometrics on a device with none enrolled.** Falls back to the password.
  - **A file above the bridge size limit.** Refused with the limit named, and offered another route.
- **Orientation:** the app declares `orientation: "any"`; crossing the 768 px hinge by rotation or Split View must
  not remount (AC-21).

---

## Data / Schema Changes

Two new tables, one migration, **nothing financial**.

| Table | Shape | Notes |
|---|---|---|
| Device registrations | Clinic, user, platform, token, shell version, last-seen, active flag | Clinic-owned aggregate root, so it takes the standard clinic query filter. Unique on the token; indexed for the per-user batched read. ⚠️ Must **survive** a restore that bumps every token version |
| Push delivery outbox | Recipient device, payload reference, status, attempts, failure reason | Four-state status including a **non-terminal blocked** state, so an unsendable row leaves the dispatch scan instead of accumulating at its front (the starvation defect already fixed once for reminders) |

No existing table changes. No backfill. Schema verification runs **before and after** and is diffed — it is the
only migration gate, since nothing in the test project touches a database.

---

## Out of Scope

Each argued, not merely listed.

- **Offline operation and any client-side cache.** `mobile-tablet-responsive/spec.md:458` argued it out, and
  `multi-tenant-cloud`'s owed decision #1 supplies the decisive reason: **gapless per-year invoice numbering cannot
  be reconciled after a partition** — gaps and duplicates are both legally significant in Tunisia. The app is
  online-only and **says so** (FR-13) rather than implying otherwise.
- **A statically exported or bundled web app.** It would require reimplementing Cloud login, because `middleware.ts`
  *is* the entire Auth0 surface and no `/auth/*` route files exist. That is a second auth implementation in a
  codebase whose documented dominant defect is that fixes do not propagate.
- **A React Native or Flutter client.** There is nothing to build it against: no API versioning, Swagger is
  Development-only with no security definition, no committed contract, and the success envelope is `Result<T>`-wrapped
  on some endpoints and a bare DTO on others **with no marker**. A webview client needs none of that; a native one
  needs all of it. That contract work is the real prerequisite, and it is a feature of its own.
- **Mobile support on a self-hosted LAN install.** It technically works today through the shipped trust page, but it
  carries every problem the hosted topology deletes — the inert iOS root, the unverified 398-day leaf, DHCP/SAN
  breakage — **and cannot have push at all**. `packaging/README.md:444`'s existing fallback already says Local-mode
  phone support falls back to Cloud.
- **Per-user notification preferences.** The right long-term answer, but it needs a preferences store and a settings
  surface — a feature on top of this one. The time-critical set is the default until then.
- **Server discovery (mDNS/Zeroconf).** There is one hosted domain. Nothing to discover.
- **Auth0 native sign-in with PKCE.** The target topology uses our own accounts. The backend validates by issuer and
  audience, so adding a native Auth0 client later needs no backend change.
- **A tablet-specific or phone-specific redesign of any existing screen.** The shells render the shipped bundle.
  Any layout defect found is a defect against the existing contract and is fixed there, not forked here.
- **Auto-update of the shells.** The stores do it. `packaging/README.md:402` already puts client auto-update out of
  scope for the desktop shell.
- **A fix for the certificate SAN staleness** (a DHCP lease change reuses a certificate that no longer covers the
  address, because the existing-certificate check never re-verifies its SANs). Irrelevant on the hosted topology,
  which is precisely why it will keep hiding — file it as a follow-up instead of fixing it here.

---

## Edge Cases (critical only)

### EC-1: The shell is newer than the server
- **Scenario:** A store auto-update installs a shell built against a newer API than the clinic's server.
- **Expected:** The version floor protects the *old client* case, not this one. A newer shell must degrade like a
  browser would — an absent route is an ordinary French error on that screen, never a crash or a blank app. No
  feature-capability negotiation is introduced.

### EC-2: Push arrives for a user who has been deactivated or moved clinic
- **Scenario:** A notification is queued, then the account is deactivated or its clinic changes before dispatch.
- **Expected:** Delivery is suppressed. Enforcement already happens per request on the API; the dispatch path must
  re-check rather than trust the queued row, because an OS banner bypasses every request-time check.

### EC-3: The same device is used by two staff members
- **Scenario:** A shared tablet; one user signs out, another signs in.
- **Expected:** Sign-out deregisters the device for the departing user before the new registration. A notification
  for the previous user must never reach the new one — the failure here is a patient-relevant leak on a lock screen.

### EC-4: A refresh response is lost in flight after rotation
- **Scenario:** The server rotates the credential; the response never arrives.
- **Expected:** The client is signed out and must sign in again — accepted, and stated. The exchange is single-flight
  on the client, so the window is one request. Silent acceptance of a superseded credential is **not** the answer,
  because it would defeat rotation.

### EC-5: A very large file crosses the bridge
- **Scenario:** A 40 MB radiograph is downloaded in the shell.
- **Expected:** Refused above a configured limit, with a French message naming the limit and an alternative route.
  Never an out-of-memory crash — bytes cross the bridge as a string.

### EC-6: The user denies notification permission, or revokes it later
- **Scenario:** Permission declined at first launch, or turned off in OS settings months later.
- **Expected:** The app works fully; the in-app feed is unaffected. The state is visible with a route to OS
  settings. No repeated prompting, and no pretence that push is active.

### EC-7: Biometric hardware is present but the user has enrolled none
- **Scenario:** A phone with a fingerprint sensor and no fingerprint registered.
- **Expected:** The password path, with no dead control and no error dialog. Distinguished from a *failed* check.

### EC-8: Sign-in on a phone whose account must change its password
- **Scenario:** An admin-provisioned account with a temporary password — the normal onboarding path on the target
  topology.
- **Expected:** The forced-change screen is reachable and completable in the shell, and the 403 carrying the
  machine-readable code is routed on rather than being treated as bad credentials.

### EC-9: The app is backgrounded mid-upload
- **Scenario:** A photo is uploading when the user takes a call.
- **Expected:** Either the upload completes or it fails with a French message and the file is still selectable —
  never a silently truncated attachment on a patient's record.

---

## Non-Functional Hints

- **Performance:** the shell adds no round trip of its own; first paint is the server's. A push fan-out reads
  devices **batched per recipient set**, never per user. Note the per-request account round trip that already exists
  on the authenticated path, and the global request limiter, when judging a chatty client.
- **Security:** no password stored on the device; no patient-identifying content in a push payload; biometric
  re-authorises an existing session and does not extend the underlying credential; the version floor is a **UX
  guard, not a security control**, since any client can omit a header — say so rather than relying on it.
- **Accessibility:** every shell control has a real accessible name; the biometric sheet always offers a visible
  password escape; state messages are text, not colour — an unreachable state must be legible to a screen reader
  and to someone reading it out over the phone; `prefers-reduced-motion` is honoured by the web app already and the
  shell adds no animation of its own.
- **Scalability:** push dispatch is a bounded batch with retention, per the existing outbox pattern; a per-clinic
  partition on the rate limiter matters once a fleet of phones sits behind one clinic address.
- **Privacy/compliance:** the hosted topology holds patient data, so the store declarations must be answered
  honestly, and the INPDP declaration named in `multi-tenant-cloud`'s owed decision #5 is a real prerequisite for a
  public launch — not for this spec's implementation, but for AC-52.

---

## Dependencies

- **`features/multi-tenant-cloud/plan.md` US-1 and US-2** — hard, for Phase 3 only. US-1 gives the named capability
  the push surface gates on; US-2 gives the tenant scope, without which a new background job would read **zero rows
  and log success**. Phases 0, 1, 2 and the session-continuity story do **not** depend on it.
- **`features/cloud-deployment/`** (shipped) — the hosted front door and single origin the shells point at.
- **Apple Developer Program and a macOS build environment.** ⚠️ The hardest practical dependency and the only one
  not solvable in this repo: it is win32 with no CI. Either a Mac or a hosted build service.
- **A Google Play developer account**, and FCM/APNs credentials for the target deployment.
- **A seeded demo clinic** on the hosted domain, for review (AC-54).

---

## Open Questions

- [ ] **Which hosted domain** do the shells default to, and is a staging address needed as a build variant or only
      through the address-entry screen?
- [ ] **Bundle identifiers and display name** for both stores — `tn.clinicmanagement.*` matches the existing
      Apple-profile identifier, but the product name shown on a home screen has not been settled.
- [ ] **The bridge file-size limit** (AC-5 / EC-5) — needs a number, informed by the largest radiograph in practice.
- [ ] **Deep-link target list** — a notification tap needs a route per category; the agenda and the patient record
      are obvious, the post-visit review less so.
- [ ] **Does the demo tenant hold synthetic data only?** It must, if a reviewer signs into it — but that means
      someone maintains a seed.
- [ ] **Who owns the store accounts** (the clinic, or the vendor), since it determines who can ship an update.

⚠️ These must be resolved or explicitly deferred before this spec moves to APPROVED.

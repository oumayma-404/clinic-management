# Feature Specification: The clinic on a phone — native Android & iOS shells

**Status:** DRAFT — four open questions explicitly deferred (none blocks Phase 0; only the domain blocks Phase 1)
**Challenged:** Yes — `/challenge-spec` 2026-08-05: **4 Critical · 8 Major · 2 Minor** plus 6 corrections, all
resolved in-document. Criteria renumbered continuously to **AC-1 … AC-77** (was 60 + 13 suffixed).
⚠️ `blueprint.md` carries a superseded-by note listing the nine of its claims this pass corrected — read this file,
not that one, wherever they disagree.
**Type:** Large (7 slices; Phase 0 ships independently, then 1 · 2 · **2b** · 3 · 4 · 5 in order)
**Scope:** FE + BE + a new `mobile/` client + packaging/store operations
**Created:** 2026-08-05
**Exploration:** `features/mobile-native-shells/exploration.md`
**Blueprint:** `features/mobile-native-shells/blueprint.md` (carries the challenged architecture decision)
**Depends on:** a **deployed** hosted origin (Phases 1–2, 2b, 4) · `features/multi-tenant-cloud/plan.md` **US-2**
(Phase 3 only; **US-1 landed 2026-08-05**) — see Dependencies for the per-phase split
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

**Three — a webview is not a browser, and several things genuinely break in it.** Android `WebView` does not
implement `window.print()`, its `DownloadListener` **never fires for `blob:` URLs**, it has **no `navigator.share`**
and **no PDF viewer at all**. So the app's two print paths, every download path and all three PDF previews fail
there — and four download call sites are already broken on iOS Safari today because they bypass the correct helper.
**Two more surface only once you look for them, and neither is a webview limitation the shell can absorb:** Google
**refuses OAuth inside an embedded webview** as policy, so « Connecter Google Agenda » — a plain top-level
navigation today — dead-ends on a Google error page with no way back (FR-5); and the inactivity limit *destroys* the
session on resume rather than suspending it, which is why biometric resume needs a change in `web/` and not only in
`mobile/` (FR-12).

Fixing the web-side half is Phase 0: it is web work, it improves every mobile **browser** too, and it closes the two
criteria `mobile-tablet-responsive` P7 left open — that spec's **AC-36** (icon assets) and **AC-40** (print
stylesheet). ⚠️ Those two numbers belong to *that* spec; this spec's own AC-36 and AC-40 are unrelated.

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

**A PDF preview works where there is no PDF plugin.** **Two** of the three `<iframe>` previews pass
`#toolbar=0&navpanes=0` — Adobe/Chromium-only parameters that render **blank** in Android WebView — and on a coarse
pointer those two are replaced by the already-correct share/open path. ⚠️ The **third** (the document editor) is the
**official-form** preview: it carries no fragment, it *is* the print path, and it is the alignment check made before
a sheet of genuine CNAM paper is consumed. It keeps its frame and gets its own answer (FR-2, AC-8) with Phase 4's
native viewer.

**Printing prints the document.** There is no `@media print` anywhere; the only print CSS in the repo is a string
injected into a `window.open` popup, so printing any screen prints the sidebar and the AI launcher.

**A control the platform cannot honour is absent, not inert.** `ai-chat.tsx`'s microphone uses
`webkitSpeechRecognition`, which does not exist on iOS — so today every iPhone shows a button that does nothing.
It is hidden where speech recognition is unsupported (AC-11), which is rule 2 applied to the smallest case in the
feature.

**The gate stops claiming what has landed.** `check-responsive.mjs`'s `PENDING_PARTS` still holds `P7`/`P8`, and
`.claude/rules/frontend-web.md` § 15 still lists the manifest, the icons and the LAN trust page as missing — all of
which this phase either lands or has been shipped elsewhere. Both are corrected here, and the two new mechanical
checks are enforced from arrival rather than parked (AC-10).

### Phase 1 — Two shells, and a bridge of three messages

A Kotlin/`WebView` app and a Swift/`WKWebView` app in a new top-level **`mobile/`** — own toolchain, own
`CLAUDE.md`, in neither the .NET solution nor `web/`, exactly how `desktop/` is carved out. Each loads the hosted
origin, remembers it, and renders **five** French states: `WebView` · `Connecting` · `ServerConfig` ·
`Unreachable` · **`UpdateRequired`** (Phase 2's). `desktop/`'s two hard-won details carry over: *navigate*, never
re-assign a source, or « Réessayer » does nothing when the address is unchanged; and persist the address rather
than compiling it in, so one binary serves production, staging and App Review.

The bridge starts at **three feature-detected messages** — `saveFile` · `print` · `onPushToken` — plus a `version`
and a `platform`. It takes **base64 bytes, not a `blob:` URL**, because Android cannot see a blob. The set is
**enumerated per phase** (FR-6): Phase 4 amends it for the biometric gate and the native viewer, and every
amendment edits the contract document and bumps the version. Nothing is added implicitly.

### Phase 2 — A stale app says so *(closes `multi-tenant-cloud` owed decision #3)*

Skew is invisible today **only because the server serves the bundle**. A shipped binary ends that, and that plan
already records the consequence: *"a cloud API that moves under N pinned clients breaks them silently."*

The shell **asks natively at launch** — one anonymous read of the floor before the webview is created — and shows
`UpdateRequired` with a store link without loading the app. The `X-Client-Version` header and the **426**
`code: "client_too_old"` refusal are the backstop for a floor that moves under a running app, attached and
surfaced by the web client because a webview cannot see its own `fetch` responses. An **absent** header passes,
because browsers send none — and so do the BFF's own server-side hops.

### Phase 2b — The session lasts the working day *(no dependency; ships before Phase 3)*

`POST /api/auth/refresh` returns `RefreshToken = string.Empty` and `RefreshExpiresAt = null`, so the 12 h credential
**never slides** and staff retype a password twice a day. On a phone that is the difference between "the app" and
"that website". Each exchange now mints a fresh credential with a new expiry — and the BFF **stores** it, which is
the half that was missing (`bff/auth/token` returns only the access token today and re-sets no cookie), so a
server-only change would have rotated something nobody keeps.

⚠️ Numbered **2b**, not 4: it depends on nothing `multi-tenant-cloud` supplies, it is what makes the shells worth
installing, and leaving it inside Phase 4 would queue it behind a DRAFT plan. See FR-9 for what this **is not** —
the credential is stateless, so this is sliding expiry, not revoking rotation.

### Phase 3 — A backgrounded phone still knows *(⚠️ requires multi-tenant US-2; US-1 has landed)*

A SignalR socket delivers only while the app is foregrounded; iOS tears it down seconds after backgrounding. So
**time-critical** staff notifications — the three appointment categories, the ~24 h reminder, and the post-visit
review — also go out as an OS push. Operational alerts (low stock, expiring stock, stale backup, failed reminder)
deliberately do **not**: a channel that buzzes at 22:00 about a box of gloves gets muted, and then the urgent ones
are lost with it.

Push is a **named capability on `DeploymentProfile`**, not a mode check, because FCM and APNs are internet
services: `SelfHostedLan` cannot have it at all, and the product must say so rather than ship a feature that never
fires.

⚠️ **And the capability is answered per platform, not once.** FCM and APNs carry separate credentials, so the
half-configured install is the likely one — and with a single boolean, FCM-only means the route stays open, iPhones
register, and their rows sit permanently `Blocked` while their owners are told nothing. The capability therefore
takes the platform: registration for an unsupported platform is **refused with a French reason** rather than
accepted into a queue that will never drain, and the settings surface states availability per platform.

### Phase 4 — The phone becomes an instrument, not a viewer

Camera capture into the patient record — the phone *is* the imaging device, which is a **different and better**
answer to `adoption-qa-l` L7's bridge than the desktop-shell-vs-local-helper question that currently blocks it.
Biometric resume, because a 30-minute password prompt at the chair with gloved hands is a policy people work
around — and because the limit currently *destroys* the session on resume, this is the one Phase 4 item that also
changes `web/` (FR-12). A native PDF viewer for the platform that has none, which is also what makes the official
forms printable in a shell (FR-2). Deep links, so a notification tap lands on the right screen.

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

### US-4: My session lasts the working day *(Phase 2b — backend + BFF)*
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
`blob:` URL to survive a navigation. A file above a configured size — **25 MB**, the default — shall be refused
with an explicit French message naming the limit and offering another route, never a silent failure.

⚠️ **The limit exists because bytes cross the bridge as a base64 *string*** (Android's `DownloadListener` cannot see
a `blob:`), so 25 MB of file is ~33 MB of JavaScript string marshalled through a `@JavascriptInterface` call, and a
large radiograph is where a low-memory Android device runs out. It is **configurable**, so the figure can move once
real files are measured; note it is deliberately *above* the existing 8 MB multipart import cap and applies only to
delivery, not upload.

### FR-2: PDF preview without a PDF plugin
On a coarse pointer, a PDF shall be delivered through FR-1 rather than embedded in an inline frame. Viewer-specific
URL fragments shall not appear anywhere. Where a preview cannot be shown, the user shall be offered the file.

**The two official forms are the exception, and they get their own answer.** The BS1 bulletin and the arrêt de
travail are printed onto genuine pre-printed CNAM paper, so their preview *is* the alignment check made before a
sheet is consumed — and that same frame **is the print path** (`isOfficialForm` → `contentWindow.print()`). Their
preview shall therefore be kept and routed, in a shell, through the native viewer and the OS print service; where
neither is available the user shall get an explicit French message and be offered the file. A blank frame beside an
inert « Imprimer » is the one outcome forbidden.

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

**A navigation off the hosted origin shall leave the webview** and open in the platform's system-browser surface,
returning to the app when the flow redirects back to the origin.

⚠️ **This is not a nicety — one existing feature is otherwise unreachable and lands the user in a dead end.**
« Connecter Google Agenda » does a **top-level navigation** to `accounts.google.com`
(`web/lib/api/google-calendar.ts` → `window.location.href = authUrl`, reached from `/appointments`), and Google
**refuses OAuth in an embedded webview** as policy (`disallowed_useragent`). Inside a webview the admin therefore
gets a Google error page **with no way back to the app**. Interception is a navigation-delegate rule rather than a
bridge message, and it covers every other off-origin link at the same time; the OAuth callback's redirect to
`FrontendUrl` is what brings the webview back.

### FR-6: Bridge contract
The shell shall expose, on a single global object, a **version**, a **platform**, and a method set **enumerated
per phase** in one contract document:

| Phase | Methods |
|---|---|
| 1 | `saveFile` · `print` · `onPushToken` |
| 4 | the additional methods AC-57 (biometric gate) and AC-61 (native viewer) require, each named in that phase |

Every web-side call site shall be feature-detected, and behaviour with the object absent shall be identical to
today's browser behaviour. A change to the set shall require editing the contract document **and** bumping the
version. ⚠️ The invariant is *feature detection*, not a fixed count — a count that a later phase must exceed is a
criterion that fails on arrival rather than a contract that holds.

### FR-7: Native file input and camera
Every existing file input shall open the platform picker, and an image input shall additionally offer the camera.
No file input shall be inert.

### FR-8: Client version floor
The API shall accept a client-version header, expose the current floor and the current release anonymously, and
refuse a client below the floor with a distinct status and machine-readable code plus a French message. An absent
or malformed header shall be accepted. The floor shall be operator-configurable without a redeploy. The
floor-reporting route shall itself be **exempt from the floor** — a client being told to update must be able to
read where to update from.

**Detection has two paths, and the native one is primary.** At launch, before loading the webview, the shell shall
read the floor over **native HTTP** and present `UpdateRequired` without loading the app at all. The header and the
refusal status are the **backstop**, for the floor moving under an already-running app; the web client shall attach
the shell's version to every API call and shall surface the refusal as its own full-screen French state.

⚠️ **The backstop cannot be surfaced by the shell, which is why the launch check exists.** A webview does not
observe `fetch`/XHR responses — `WebViewClient.onReceivedHttpError` and `WKNavigationDelegate` fire for frame and
subresource loads the webview itself makes — so a 426 on an API call is visible only to the JavaScript that made
it. For the same reason the header must be attached by the **web client** (`web/lib/api/client.ts` and the hub
connection, from `window.__clinicShell?.version`): a webview can add headers to a document load, never to a
subresource request. That is Phase 2 work in `web/`, and the surfaced state works in a plain browser too.

⚠️ **Server-side BFF hops send no such header and are therefore accepted**, so a stale shell can still mint an
access token and will *look* signed in while every screen refuses — which is precisely the state AC-33 must handle,
and why the refusal must not read as signed-out.

### FR-9: Session continuity
Each refresh exchange shall issue a fresh refresh credential with a new expiry, and **the client shall persist it**,
so an active user is not forced to re-authenticate on a fixed clock. Global revocation, deactivation and
forced-password-change behaviour shall be unchanged. A failed exchange shall be indistinguishable between expired,
revoked and forged.

⚠️ **This is sliding expiry, not revoking rotation, and the spec says so rather than letting the stronger reading
stand.** The refresh credential is a **stateless** HS256 JWT with no `jti` and no server-side record
(`LocalAuthService.ValidateRefreshToken` yields only subject + token version), so a **superseded credential stays
valid until its own expiry** — there is nothing to revoke per token. Per-token revocation would need a
refresh-session table, which is deliberately **out of scope** here (see Out of Scope). Global revocation remains
`User.TokenVersion`, which the exchange already re-checks against live account state on **every** call, so
deactivation, an admin password reset and a role change all still take effect immediately.

⚠️ **The client half is required work in `web/`, not an optional nicety.** `web/app/bff/auth/token/route.ts`
currently returns only `{ accessToken, expiresAt }` from the exchange and **never re-sets `SESSION_COOKIE`** — the
cookie's `expires` is written once at login from `refreshExpiresAt` (`bff/auth/local-login/route.ts`). A
backend-only change therefore rotates a credential nobody stores and the user is still signed out on the original
clock. The exchange handler shall re-set the session cookie with the new credential and the new expiry, preserving
its existing `httpOnly` / `path` / `Secure` attributes and its existing 401-clears-the-cookie behaviour.

### FR-10: OS push
All five time-critical categories refer to **one visit**, so a tap shall open **that appointment** — one route, not a
table per category (`GET /api/appointments/{id}` already exists as the in-app feed's deep-link target and already
404s an other-clinic id).

Time-critical staff notifications shall be delivered as an OS notification, in addition to the existing in-app
feed, to **the registered devices of the same audience the in-app feed reaches, minus the actor** — every active
staff member of the clinic. A payload shall carry no patient-identifying content. A tap shall open the screen the
notification refers to. A device token reported invalid by the platform shall be deactivated, not retried. Push
shall be available only where the deployment supports it **for that device's platform**, and where it does not the
product shall say so.

⚠️ **The audience is defined by reference, not restated.** `StaffNotification` is one clinic-wide row per event
with per-user `NotificationRead` markers and no write-time fan-out, so there is no recipient list to read — the
audience is a *rule*, and two copies of it would eventually disagree about who was told. Fan-out happens in the
outbox writer, one row per device, never by changing `StaffNotification`.

**Quiet hours.** A send resolving between **21:00 and 08:00 clinic-local shall be deferred to 08:00**, reusing the
reminder outbox's existing floor rather than a second rule. ⚠️ Without it the exclusion of operational alerts buys
nothing: the ~24 h reminder for an 08:00 appointment resolves the previous evening, and a phone buzzing at 22:00 is
muted whatever the category was.

⚠️ **Volume is an accepted cost, stated rather than discovered.** A five-person practice means five phones per
booking. The escape hatch is per-user preferences, deliberately out of scope — so if the muting the spec warns
about happens in practice, that is the feature to build, not a quiet narrowing of the category list.

### FR-11: Device registration
A signed-in user shall be able to register and deregister a device for push. A registration shall belong to one
clinic and one user, shall record its platform and shell version, and shall survive a database restore.
**Registering a token already bound to another user shall rebind it to the caller and deactivate the prior
binding**, in one write.

⚠️ **Rebinding, not a conflict, is what makes EC-3 hold.** On a shared tablet the departing user's sign-out is the
tidy path but not a reliable one — the app can be killed, the session can lapse, `restore-backup` bumps every
`TokenVersion` — so the *normal* case is the arriving user registering a token that is still bound to someone else.
Refusing that leaves the previous user's binding live, which is the lock-screen leak this edge case exists to
prevent. Registering a token already bound to the **caller** is a refresh, not an error. Neither case returns a
conflict.

### FR-12: Biometric resume
Where the device offers biometrics or a device credential, resuming a backgrounded app past the inactivity limit
shall require that check instead of a password. The check shall re-authorise the existing session only; it shall
not extend the underlying credential's lifetime, and it shall not store a password on the device. A device with no
biometrics shall fall back to the password.

⚠️ **This requires a change in `web/`, not only in `mobile/`, and the spec says so because the shape is not
obvious.** The inactivity limit is owned by the **web app** and it *destroys* the session rather than suspending
it — `web/lib/auth/session.tsx`'s Local provider clears the session cookie via `/bff/auth/local-logout` and
navigates to `/login`, and it fires on **`visibilitychange`**, i.e. exactly on resume. So by the time a native
sheet could appear there is no session left to re-authorise. The inactivity path shall therefore, **when the bridge
is present**, await the shell's biometric result instead of logging out, and log out only on refusal or after three
failures; the session cookie is never cleared on the success path and the underlying refresh credential's lifetime
is untouched. With the bridge absent the path is byte-identical to today. (This timer is Local-mode-only and
deliberately so — Cloud has none, Auth0 owns lifetime there — and `HostedMultiTenant` uses local accounts, so it
is precisely the mode that has it.)

### FR-13: Reachability on a mobile network
**Two axes, two owners.** *Client→server reachability* shall be judged **client-side on every deployment** and
surfaced, so a phone leaving coverage does not present itself as online. *Server→internet egress* shall remain a
separate signal that exists **only where the deployment exposes the probe**; where it does not, it shall be
**absent**, never inferred false. Neither shall be derived from the auth mode. The French wording shall not name
the local network.

⚠️ **The premise this requirement was first written on is wrong, and the real defect is the opposite one.** The web
provider decides whether to poll from **`AUTH_MODE`** (`connectivity.tsx` — `mode === "local"`), while the API's
probe is gated on the *deployment* (`ConnectivityController` 404s outside Local mode today, and
`multi-tenant-cloud` makes it an `ExposesTrustEndpoints` capability, **✗ on `HostedMultiTenant`**). Since that
topology uses local accounts, the two disagree: the provider **does** poll, gets a **404**, reads `res.ok === false`
as `internetReachable: false`, and so **permanently** disables the AI chat and the Google Calendar controls behind
an undismissable French warning that tells a dentist on cellular to *« Vérifiez votre connexion au réseau local »*.
A phone that wrongly believes it is online would have been the lesser bug. Fixing the gate is therefore part of this
requirement, not a side effect of it, and it repairs a live defect on the hosted topology whether or not a shell
exists.

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
  ⚠️ There are **two** such sites, not three — the patient record and the files manager. The document editor's frame
  carries no fragment (verified), which is why it is handled by AC-8 instead.
- **AC-7:** On a coarse pointer, opening a PDF in the patient record **or** the files manager delivers the file
  rather than embedding a frame; the user always ends up with a viewable document.
- **AC-8:** The **official forms** (BS1 bulletin, arrêt de travail) keep their preview: in a shell the preview and
  « Imprimer » route through the native viewer and the OS print service, and where neither exists the user gets a
  French message and is offered the file. A blank frame beside an inert « Imprimer » fails this criterion. *(The
  shell half lands with Phase 4's viewer — AC-61 — so Phase 0 changes nothing here.)*
- **AC-9:** Printing `/factures`, a patient record and a document produces only document content — no sidebar, no
  navigation, no assistant launcher.
- **AC-10:** The stale `PENDING_PARTS` set is removed from the responsive checker, and `.claude/rules/frontend-web.md`
  § 15 no longer lists the manifest, the icons or the LAN trust page as missing.
- **AC-11:** The voice-input control is absent, not inert, where speech recognition is unsupported.
- **AC-12:** `npm run check:responsive`, `npx tsc --noEmit` and `npm run build` all pass, with the warning count
  identical to the pre-change baseline.

**Phase 1 — the shells**

- **AC-13:** Both shells build to an installable artifact and open the hosted app full-screen, with no address bar
  and no browser chrome.
- **AC-14:** Signing in works, and the session **survives a cold start** — a relaunched app is still signed in.
- **AC-15:** Each shell presents exactly the five states, all French, each reachable in a test: connected · loading
  · address entry · unreachable (naming the address and the reason) · update required.
- **AC-16:** « Réessayer » re-attempts the **same** address successfully after the server returns.
- **AC-17:** The address persists across relaunch and is changeable from the UI without reinstalling.
- **AC-18:** All six file inputs open the platform picker on both platforms; an image input offers the camera.
- **AC-19:** Every file-delivery path from AC-4 delivers a file **in both shells**, including on Android where blob
  downloads and file sharing are unavailable.
- **AC-20:** A file above the configured limit (25 MB default) is refused with a French message **naming** the
  limit and offering another route, and the refusal happens before the blob is read — no crash, no silent nothing,
  and the limit is changeable without a rebuild.
- **AC-21:** Printing works from both shells through the OS print service.
- **AC-22:** Safe-area insets reach the page: the bottom navigation clears the home indicator on a notched iPhone
  and the gesture bar on Android.
- **AC-23:** Rotation and iPad Split View **do not remount** the app across the 768 px hinge, and typed input in an
  open dialog survives the change. ⚠️ On Android this is not free: the activity is recreated on a configuration
  change by default, which destroys the webview and reloads the app — the shell must retain it (declare the
  configuration changes it handles, or restore the webview's state), or the criterion is unachievable no matter what
  the web app does.
- **AC-24:** The back gesture on Android and the swipe-back on iOS navigate within the app rather than closing it.
- **AC-25:** A navigation off the hosted origin opens in the system browser and the webview stays on the app —
  verified with « Connecter Google Agenda », which Google **refuses** to serve inside a webview, and which returns
  to the app when the callback redirects back. The webview is never left stranded on a foreign error page.
- **AC-26:** With the bridge object deleted at runtime, every affected screen behaves exactly as it does in a
  browser — no error, no dead control.
- **AC-27:** The bridge contract document's method set, the shell's implemented set, and FR-6's phase table all
  agree — and the shell reports a version. (Phase 1's set is `saveFile` · `print` · `onPushToken`.)

**Phase 2 — version floor**

- **AC-28:** An anonymous request returns the minimum supported client version, the current release, and both store
  URLs — and it succeeds **without** authentication, so a too-old client can ask before signing in.
- **AC-29:** That route is **exempt from the floor itself**: a below-floor client calling it gets 200, not 426.
  Otherwise the one route that says where to update is the one route a stale client cannot read.
- **AC-30:** A client below the floor receives **426** with the canonical `{ error }` body plus
  `code: "client_too_old"`, on every API route but AC-29's.
- **AC-31:** The web client attaches `X-Client-Version` from the bridge on **every** API call and on the hub
  connection, and attaches nothing when the bridge is absent.
- **AC-32:** A request with no client-version header, or a malformed one, is accepted unchanged — the web app in a
  browser, and every server-side BFF hop, are unaffected.
- **AC-33:** A below-floor shell shows the `UpdateRequired` state with a working store link, and does **not**
  present itself as signed out. Both paths are tested: **at launch** the shell decides from its own native read of
  AC-28's route and never loads the webview; **mid-session**, a floor raised under a running app turns the next
  refusal into a full-screen French update state rather than a stack of error toasts.
- **AC-34:** The floor is read from operator-owned configuration and takes effect without a code change; the
  anonymous route is added to the pinned anonymous-endpoint set (which is asserted equal in both directions).

**Phase 2b — session continuity** *(backend + BFF; depends on nothing Phase 3 depends on, so it ships in sequence
here rather than waiting behind `multi-tenant-cloud`)*

- **AC-35:** A refresh exchange returns a **new** refresh credential and a new expiry, **and the session cookie
  carries them afterwards** — asserted on the cookie the client holds, not only on the API response, since a
  rotated credential nobody stores changes nothing a user can feel.
- **AC-36:** A deactivated account, a bumped token version, and a forced password change all still take effect on
  the next exchange, with one indistinguishable failure message for expired, revoked and forged.
- **AC-37:** A user active all day is not prompted for a password by the passage of time alone; a user idle past
  the refresh window is. **This is the criterion AC-35 exists to serve** — a backend-only change passes AC-35's
  first clause and fails this one.
- **AC-38:** The web app in a browser and the desktop shell continue to work against the new behaviour, and a
  client that ignores both new fields is not broken by them.
- **AC-39:** A superseded credential is **not** rejected while unexpired (EC-4), and the spec's Security notes say
  so — a test asserting the opposite would pin a property this design does not claim.

**Phase 3 — OS push**

- **AC-40:** A device registers on first sign-in and re-registers when its token changes; deregistration on sign-out
  stops delivery to that device.
- **AC-41:** Registering a token already bound to **another** user rebinds it to the caller and deactivates the
  prior binding, with **no conflict response** — verified without any sign-out in between, since that is the shared-
  tablet case (EC-3). Registering a token bound to the caller is a refresh.
- **AC-42:** Registering a platform this deployment cannot push to is **refused with a French reason**, and the row
  is not created — a registration must never land in a queue that cannot drain.
- **AC-43:** With the app **backgrounded or the phone locked**, booking, cancelling and rescheduling an appointment,
  the ~24 h reminder, and a post-visit review each produce an OS notification on both platforms.
- **AC-44:** Low stock, expiring stock, stale backup and failed reminder produce **no** OS notification, while still
  appearing in the in-app feed.
- **AC-45:** The actor receives no push for their own action, and the audience otherwise **equals** the in-app
  feed's for the same event — asserted against the feed rather than against a second list.
- **AC-46:** A send resolving between 21:00 and 08:00 clinic-local is deferred to 08:00, on the same clinic-local
  rule the reminder outbox uses; a send inside working hours is not delayed.
- **AC-47:** A notification payload carries **only** a category, a fixed French category label
  (« Nouveau rendez-vous ») and **opaque record ids for routing** — and no patient name, phone, act, tooth,
  diagnosis, amount or free text of any kind. Verified by reading **what is sent**, not by inspecting a banner.
  ⚠️ Stated this way because "no identifying detail" and AC-48's deep link contradict each other: a routing id *is*
  a record reference, and the resolvable line is the same one `ClinicHub` already draws — a key, never content.
  Nothing beyond the fixed label may reach a lock screen.
- **AC-48:** Tapping a notification opens the screen it refers to, from cold start and from background.
- **AC-49:** A token the platform reports as unregistered deactivates that device registration and is not retried.
- **AC-50:** A registration whose channel is unconfigured leaves the dispatch scan rather than accumulating at its
  front — a queue of unsendable rows cannot starve later ones.
- **AC-51:** Where the deployment supports push for **neither** platform, the registration route is absent, no
  dispatch job is registered, and the settings surface states plainly that OS notifications are unavailable on this
  installation.
- **AC-52:** Where it supports **one** platform, the settings surface says so **per platform**, and a device on the
  unsupported one is refused at registration (AC-42) rather than accepted and never delivered to. A half-configured
  install must not read as a working one.
- **AC-53:** One clinic's device registrations are unreadable from another clinic, including when no clinic is in
  scope.
- **AC-54:** A database restore does not silently unsubscribe a clinic's devices.
- **AC-55:** A push send never fails, delays or rolls back the clinical or financial operation that caused it.

**Phase 4 — native capability**

- **AC-56:** A photo taken in the shell attaches to the patient's record and appears in the files list on the web
  app, with the same validation as an uploaded file.
- **AC-57:** Resuming past the inactivity limit requires a biometric or device-credential check and restores the
  session **without a password**; three failures fall back to the password screen. The session cookie is **not**
  cleared on the success path — verified by reading what the browser holds after a successful resume, since a
  passing banner and a destroyed session look identical from the outside.
- **AC-58:** With the bridge absent, the inactivity path is unchanged: the session is cleared and the user lands
  on `/login` with their place remembered, exactly as in a browser today.
- **AC-59:** No password is stored on the device — verified by inspecting what the shell persists.
- **AC-60:** A device without biometrics falls back to the password with no dead control and no error.
- **AC-61:** A PDF opens in a working viewer on Android.
- **AC-62:** Losing the mobile network surfaces an unreachable-server state within one poll interval and recovers
  automatically; server-to-internet egress remains a separate signal that does not mask it.
- **AC-63:** On a deployment with **no** egress probe, the absent signal reads as *absent*: the AI chat and the
  Google Calendar controls are **enabled**, and no « pas de connexion internet » warning appears. A 404 from the
  probe must not be read as "offline" — that is the state this criterion exists to forbid.
- **AC-64:** No connectivity message names the local network; the wording is true for a phone on a mobile network.

**Phase 5 — stores**

- **AC-65:** Both listings are live, installable on a clean device, and the app opens to a working sign-in.
- **AC-66:** Each app declares its data collection for health-adjacent data on both stores.
- **AC-67:** A reviewer can sign in to a seeded demo clinic on the hosted domain with the credentials in the review
  notes, and is **not** blocked by a forced password change.
- **AC-68:** Each app's version comes from **one source** in `mobile/`, and that is what the shell reports as its
  `version` and sends as `X-Client-Version` — the hardcoded version already duplicated across three packaging files
  does not become four. *(The **floor** is operator-owned configuration on the server, deliberately not derived from
  the shell — a client cannot be the authority on whether it is too old.)*
- **AC-69:** The blueprint's amendment to `mobile-tablet-responsive`'s "no native application" non-goal is recorded
  in that spec, so the two documents do not disagree.

**Cross-cutting**

- **AC-70:** No new occurrence of the retired mode check exists anywhere; the deployment question is asked as a
  named capability. ⚠️ `DeploymentProfileCoverageTests` is `multi-tenant-cloud` US-1's guard, so this criterion is
  only *enforced* from Phase 3 — Phase 2's middleware must be written to satisfy it in advance rather than be
  retrofitted.
- **AC-71:** Every new API action carries an explicit named policy; no bare authorize attribute is added.
- **AC-72:** Any new background job declares its cross-clinic read explicitly, so it cannot silently read nothing.
- **AC-73:** Schema verification passes before and after the migration, and the money reconciliation diff is
  **empty** — nothing here touches money.

**Edge cases that would otherwise go unverified**

*Each of these was an Edge Case with no criterion behind it — narrative that nothing had to satisfy. They are
grouped rather than filed under a phase because each belongs to the phase its edge case does.*

- **AC-74 (EC-1, Phase 1):** A shell built against a newer API than the server degrades like a browser: an absent
  route is an ordinary French error on that one screen, never a crash, a blank app or a forced update. No feature
  negotiation.
- **AC-75 (EC-6, Phase 3):** With notification permission declined at first launch, or revoked in OS settings later,
  the app works fully and the in-app feed is unaffected; the state is visible with a route to OS settings, there is
  no repeated prompting, and nothing claims push is active.
- **AC-76 (EC-8, Phase 1):** An account that must change its password can complete the change **inside the shell**:
  the 403 carrying `code: "must_change_password"` is routed to the change-password screen, not surfaced as bad
  credentials — the normal onboarding path on the target topology, where accounts are admin-provisioned.
- **AC-77 (EC-9, Phase 4):** An upload interrupted by backgrounding either completes or fails with a French message,
  with the file still selectable for a retry. A truncated attachment on a patient's record fails this criterion.

---

## API Endpoints

### Client requirements *(new)*
```
GET /api/meta/client-requirements
(anonymous — a too-old client must be able to ask before it can authenticate)
(and EXEMPT from the version floor itself — read by the shell natively at launch, before any webview exists)

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
Absent where the deployment does not support push at all (404)

Request:
{ "token": "…", "platform": "ios" | "android", "shellVersion": "1.4.1" }

Response 200: { "id": "…", "registeredAt": "…" }
  — token already bound to this caller     ⇒ refreshed, 200
  — token bound to ANOTHER user            ⇒ rebound to the caller, prior binding
                                             deactivated, 200 (never 409 — see FR-11)
Response 400: { "error": "…" }   // incl. a platform this deployment cannot push to,
                                 //      refused with a French reason, not queued
Response 404: { "error": "…" }
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
⚠️ The two fields already exist on the response type, so the wire contract does not change and a client that
ignores them keeps working — which is what makes it safe. **But "the BFF already reads them" is only true of
`login`.** The refresh handler (`bff/auth/token/route.ts`) returns `{ accessToken, expiresAt }` and re-sets no
cookie, so the client half is real work in `web/` (FR-9), not a free consequence of the server change.

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
  - **OS push on a platform this deployment has no credentials for.** Stated **per platform** on the settings
    surface, and refused at registration — an iPhone on an FCM-only install is told, not silently queued.
  - **A navigation the platform refuses to serve in a webview** (Google's OAuth consent screen). Handed to the
    system browser rather than dead-ended (FR-5).
  - **Biometrics on a device with none enrolled.** Falls back to the password.
  - **A file above the bridge size limit.** Refused with the limit named, and offered another route.
- **Orientation:** the app declares `orientation: "any"`; crossing the 768 px hinge by rotation or Split View must
  not remount (AC-23).

---

## Data / Schema Changes

Two new tables, one migration, **nothing financial**.

| Table | Shape | Notes |
|---|---|---|
| Device registrations | Clinic, user, platform, token, shell version, last-seen, active flag | Clinic-owned aggregate root, so it takes the standard clinic query filter. Unique on the token — which is what makes FR-11's **rebind** a single deterministic write rather than a merge — and indexed for the per-user batched read. ⚠️ Must **survive** a restore that bumps every token version |
| Push delivery outbox | Recipient device, payload reference, status, attempts, failure reason, **send-not-before** | Four-state status including a **non-terminal blocked** state, so an unsendable row leaves the dispatch scan instead of accumulating at its front (the starvation defect already fixed once for reminders). The send-not-before instant is what carries FR-10's quiet-hours floor — deferring by *rescheduling* rather than by re-testing the clock on every scan keeps the deferral visible and the scan predicate unchanged |

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
- **Per-token (per-device) session revocation.** FR-9 slides the expiry of a **stateless** credential, so "sign this
  one phone out" would need a refresh-session table — a third table, a migration, and a DB round trip on a path that
  already makes one per authenticated request. `User.TokenVersion` gives *global* revocation today and covers the
  cases that actually arise (deactivation, admin reset, role change, a restored backup). Worth doing the day a
  clinic runs enough devices for one lost phone to matter; not worth carrying into this feature's migration.
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
- **Expected:** A notification for the previous user must never reach the new one — the failure here is a
  patient-relevant leak on a lock screen. Sign-out deregisters the device, but the guarantee does **not** rest on
  that: the arriving user's registration **rebinds** the token and deactivates the prior binding (FR-11), because a
  killed app, a lapsed session or a restored backup all end a session without a sign-out ever running. Anything
  already queued for the departing user is suppressed by EC-2's dispatch-time re-check.

### EC-4: A refresh response is lost in flight after rotation
- **Scenario:** The server issues a fresh credential; the response never arrives.
- **Expected:** **Nothing.** The client still holds its previous credential, which remains valid until its own
  expiry (FR-9 — the credential is stateless, so a superseded one is not revoked), so the next attempt simply
  succeeds. No sign-out, and no single-flight guarantee is load-bearing here. ⚠️ Stated because the obvious
  assumption is the opposite: with revoking rotation this *would* sign the user out, and a plan written against
  that assumption would add a mitigation for a failure mode this design does not have.

### EC-5: A very large file crosses the bridge
- **Scenario:** A 40 MB radiograph is downloaded in the shell.
- **Expected:** Refused above the configured limit (**25 MB** by default), with a French message **naming** the limit
  and offering an alternative route. Never an out-of-memory crash — bytes cross the bridge as a base64 string, so
  25 MB of file is ~33 MB of string. The refusal happens **before** the blob is read, not after.

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
  guard, not a security control**, since any client can omit a header — say so rather than relying on it. **And
  FR-9 is sliding expiry, not revoking rotation**: a superseded refresh credential stays valid until its own
  expiry, so the ceiling on a stolen credential is its remaining lifetime plus one slide, with `TokenVersion` as
  the only revocation lever. That is a weaker property than "rotation" usually implies and is written down here so
  no later document claims the stronger one.
- **Accessibility:** every shell control has a real accessible name; the biometric sheet always offers a visible
  password escape; state messages are text, not colour — an unreachable state must be legible to a screen reader
  and to someone reading it out over the phone; `prefers-reduced-motion` is honoured by the web app already and the
  shell adds no animation of its own.
- **Scalability:** push dispatch is a bounded batch with retention, per the existing outbox pattern; a per-clinic
  partition on the rate limiter matters once a fleet of phones sits behind one clinic address.
- **Privacy/compliance:** the hosted topology holds patient data, so the store declarations must be answered
  honestly, and the INPDP declaration named in `multi-tenant-cloud`'s owed decision #5 is a real prerequisite for a
  public launch — not for this spec's implementation, but for AC-65.

---

## Dependencies

The dependency is **not one thing**, and conflating the two is how a planner reaches AC-13 with nothing to open.
`HostedMultiTenant` is the topology this feature is *designed for*; a **deployed hosted origin** is what most of it
actually needs.

| Phase | Needs |
|---|---|
| **0** | nothing |
| **1, 2, and session continuity** | a **deployed** hosted origin on a real certificate — `cloud-deployment`'s `Caddyfile` + `docker-compose.prod.yml`, *actually running*, not merely committed. A hosted single-tenant install is sufficient: the shells load an origin, they do not read a profile |
| **3** | **plus** `multi-tenant-cloud` **US-2** (the tenant scope, without which a new background job reads **zero rows and logs success**). **US-1 has landed** — see the note below |
| **4** | Phase 1 |
| **5** | **plus** a public domain a reviewer can reach, and the seeded demo tenant (AC-67) |

- **`features/multi-tenant-cloud/plan.md` US-2** — hard, **Phase 3 only**. ⚠️ **Updated 2026-08-05: US-1 has
  landed.** `Infrastructure/Deployment/DeploymentProfile.cs` exists with 13 capabilities, and both
  `DeploymentProfileCoverageTests` and `DeploymentProfileTests` are live — so the named capability the push surface
  gates on is available, `SupportsOsPush` is a **14th capability on a real file**, and AC-70's guard is **enforced
  today** rather than pending. **US-2 (`ITenantScope`) has not landed** (no `ITenantScope`, no
  `SystemWideCallerCoverageTests`), and without it a new background job reads **zero rows and logs success** — so
  Phase 3, and therefore Phase 5 which needs push for Apple 4.2.2, remains gated. Phases 0–2 and session continuity
  are not.
  ⚠️ **One consequence lands outside Phase 3.** US-1 re-gated the connectivity probe onto `ExposesTrustEndpoints`,
  which is **✗ for `HostedMultiTenant`** — so `GET /api/connectivity` now **404s** on the very topology this spec
  targets, while the web provider still polls whenever `AUTH_MODE` is local (which that topology is). FR-13's defect
  is therefore **live in code**, not prospective, and its remaining fix is web-side only.
- **`features/cloud-deployment/`** (shipped) — the hosted front door and single origin the shells point at. ⚠️
  Shipped means *the artifacts exist*; a **running instance on a real domain** is an operational prerequisite of
  Phase 1, not something this repo can assert.
- **Apple Developer Program and a macOS build environment.** ⚠️ The hardest practical dependency and the only one
  not solvable in this repo: it is win32 with no CI. Either a Mac or a hosted build service.
- **A Google Play developer account**, and FCM/APNs credentials for the target deployment.
- **A seeded demo clinic** on the hosted domain, for review (AC-67).

---

## Open Questions

**Resolved**

- [x] **The bridge file-size limit** (FR-1 / EC-5 / AC-20) — **25 MB, configurable.** Revisit once the largest
      radiographs in practice are measured; the limit is config, not a rebuild.
- [x] **Deep-link target list** — **the appointment, for all five time-critical categories.** Created, cancelled and
      rescheduled, the ~24 h reminder and the post-visit review all *refer to one visit*, and
      `GET /api/appointments/{id}` already exists and is already documented as the notification deep-link target
      (other-clinic id → 404). So it is one route, not a table per category — and the post-visit review, which looked
      like the awkward case, is the clearest: it exists to record what happened at that appointment.

**Deferred — each is a business or operations decision, with what it blocks named so none can silently gate work**

- [ ] **Which hosted domain** the shells default to, and whether staging is a build variant or reached only through
      the address-entry screen. → **Blocks Phase 1 (AC-17).** Not Phase 0. Note FR-5 already requires the address to
      be *persisted rather than compiled in*, so a late answer costs configuration, not a rebuild.
- [ ] **Bundle identifiers and display name** — `tn.clinicmanagement.*` matches the existing Apple-profile
      identifier; the home-screen product name is unsettled. → **Blocks Phase 5 (AC-65).** ⚠️ A bundle id cannot be
      changed after first submission, so it must be settled *before* the first upload, not before the first build.
- [ ] **Does the demo tenant hold synthetic data only?** It must, if a reviewer signs into it — which means somebody
      maintains a seed. → **Blocks Phase 5 (AC-67).**
- [ ] **Who owns the store accounts** (the clinic or the vendor) — it decides who can ship an update, including a
      fix. → **Blocks Phase 5 (AC-65).**

⚠️ **Nothing above blocks Phase 0, and only the domain blocks Phase 1**, which is what allows the web fixes to ship
while these are settled.

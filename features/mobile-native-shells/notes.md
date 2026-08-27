# mobile-native-shells — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A backgrounded phone still knows, and a lock screen learns nothing (`mobile-native-shells` P6)

OS push, from
the registry to the dispatcher. `DeviceRegistration` is **unique on its token**, which is what makes **rebinding**
one deterministic write rather than a 409 — a shared reception tablet hands the app the *same* token to whoever
signs in, and a second row would mean the colleague who left keeps receiving notifications on a device somebody
else is holding. `PushDelivery` is the outbox, drained by the minutely `PushDispatchJob` on `NotificationJob`'s
template (connectivity-gated, bounded per tick **and per clinic**, with the non-terminal **`Blocked`** status L3
had to invent after the reminder queue starved — here from the start).
⚠️ **A push carries no message.** The payload is a category, a *fixed* French phrase for it (« Nouveau
rendez-vous ») and opaque routing ids — no patient name, act, tooth, amount or free text; the rendered body stays
in `StaffNotification` behind the app's own authentication, and the push is the doorbell for it. « The label equals
the feed row's title » is held by comparing the two rows one call produces, not by a constant.
⚠️ **Five of the nine categories reach a locked phone**, and the line is *time-critical to a person*, not
importance: booking, cancelling, rescheduling, the ~24 h reminder and the post-visit review. Low stock, expiring
stock, a stale backup and a failed reminder stay in-app — waking a dentist at home for a box of gloves is how the
OS permission gets revoked, and revoking it costs the five that matter. `StaffNotificationRules` **throws** on an
unclassified category rather than defaulting either way.
⚠️ **The fan-out is a decorator over `INotificationGenerator`**, so one hook reaches every category the feed has
or will have — editing twelve call sites is the `fixes-dont-propagate` shape. The feed is always written first and
the push queued inside a swallow-and-log: the whole chain is a post-commit side effect of an operation that has
already committed (AC-55).
⚠️ **Eligibility is re-checked at dispatch**, because a banner bypasses every request-time guard: the device may
have been deregistered, its token rebound to a colleague, or the appointment cancelled since. And **the capability
question is split on purpose** — `DeploymentProfile.PermitsOsPush` answers the deployment *kind* (so
`SelfHostedLan` is ✗ whatever an operator configures) while `IOsPushAvailability` ANDs in the per-install FCM/APNs
credentials, keeping `DeploymentProfile`'s « no operator setting can flip a capability » invariant intact.

## The clinic runs on a phone, and the phone is a shell not a second frontend (`mobile-native-shells` Part 4)

`mobile/android/` is a thin Kotlin `WebView` shell rendering the hosted server's **own** web bundle — five French
states (`WebPage` · `Connecting` · `ServerAddress` · `Unreachable` · `UpdateRequired`), a runtime-configurable
address, and `window.__clinicShell`. **`mobile/shared/bridge.md` is THE contract** (not `web/types/clinic-shell.d.ts`,
which describes only what the bundle consumes today), and a change to its method set edits that file **and** bumps
the shell's version. Phase 1's set is `saveFile` · `print` · `onPushToken`; every web-side read is a feature
detection, so with the object absent — every browser — behaviour is byte-identical to the pre-bridge app, which
AC-26 verifies by **deleting** it at runtime.
⚠️ **The address is never compiled in**, because one build serves a clinic's own PC on a LAN *and* a hosted
backend on the internet; `ServerConfig.parseAddress` is a faithful port of `desktop/ServerConfig.cs`'s, so the two
clients cannot disagree about what a typed address means. `network_security_config.xml` trusts **user-installed
CAs** — without it the self-signed `SelfHostedLan` certificate makes that whole topology unreachable — while
`onReceivedSslError` **is** overridden — and only to *report* the refusal: it calls `handler.cancel()` and shows
« certificat non approuvé », because leaving it out produced a **blank white screen** on a physical Galaxy S9
(when the SSL handler cancels, `onReceivedError` is not raised for the main frame, so the shell switched to an
empty WebView). `proceed()` appears nowhere in this project, so a bad certificate still fails loudly.
⚠️ **Three omissions are load-bearing.** `onReceivedHttpError` is deliberately unhandled: a status means the
server *answered*, and what it answered with is the app's own French error page — replacing that with a shell
state is the blank app AC-74 forbids (same reason the launch probe reads a 404 on
`/api/meta/client-requirements` as « no floor »). `android:configChanges` must list every configuration the
activity handles or rotation destroys the WebView and AC-23 is unachievable from inside the web app. And insets
are consumed as **padding on the root** rather than drawn under: targetSdk 35 forces edge-to-edge on Android 15,
and whether a given WebView reports the navigation bar through `env(safe-area-inset-bottom)` is version-dependent.
⚠️ **A WebView cannot see its page's `fetch` responses**, so anything needing a response body is `web/` work, not
shell work: the in-session 426 (Part 3) and the `must_change_password` 403 (AC-76) both live in
`web/lib/api/client.ts`. The latter's login path was never broken — the `local_must_change_password` cookie plus
`middleware.ts` cover it — but an **admin resetting the password of somebody already signed in** writes no cookie,
so every call 403'd and surfaced the middleware's **English** sentence verbatim. `onMustChangePassword` now routes
to `/change-password`, and it is the one place `client.ts` replaces a server-sent message.
⚠️ Not CI-runnable, operator-verified (as `desktop/` is) and in **neither** the `.sln` **nor** `web/`. Android Lint
runs with `warningsAsErrors` as the module's only static gate. The **hardware walk is owed**, and `applicationId`
is **provisional** — the bundle id is one of Part 8's deferred decisions and cannot change after first submission.

## A phone that has been in a pocket is unlocked, not signed out (`mobile-native-shells` P7 step 2)

the Local
session's 30-minute inactivity limit used to clear the cookie and drop the user on `/login`, which on a phone —
where the OS lock is already the barrier that matters — costs a dentist the fiche they had open several times a
day. In a shell it now **pauses**: `window.__clinicShell.confirmIdentity()` (bridge version **1.1.0**, Phase 4's
one new method) asks the OS to confirm the device owner, and `web/components/session-lock-gate.tsx` covers the
app while it does. On success the timer re-arms and **the cookie is never cleared** — AC-57 says so explicitly,
because a passing banner and a destroyed session look identical from outside.
⚠️ **The gate is opaque and the app stays mounted behind it**, and both halves are load-bearing: unmounting
`children` would reload the page the resume exists to preserve, while a translucent one would leave a patient's
record readable to whoever dismisses the OS prompt — which is the entire thing the limit is for.
⚠️ **Three attempts, and a dismissal counts as one.** Not tidiness: the cookie deliberately stays valid, so the
counter is the only bound on how long a live session can sit behind a client-side overlay. `unavailable` (no
enrolled biometric, no device credential, Android < 28) falls through **immediately** to the ordinary password
screen — no error and no dead control (AC-60) — and **nothing is stored on the device** (AC-59): the shell asks
the OS a yes/no question, and the session resumed is the one already in the WebView's cookie store.
⚠️ **`@JavascriptInterface` is synchronous**, so the result comes back through a separate global
(`__clinicShellDeliverIdentityResult`) resolving a pending request by id — `onPushToken`'s shape, and outside
`__clinicShell` for the same reason: deleting the bridge must not leave a live resolver.

## A stale app says so, once, instead of failing screen by screen (`mobile-native-shells` P3)

a native shell sends
**`X-Client-Version`**; `ClientVersionMiddleware` refuses a build below the operator's `Clients:MinimumShellVersion`
with **426** and `code: "client_too_old"`, and `<ClientVersionGate>` turns that into one full-screen « Mise à jour
requise » with the store link. `GET /api/meta/client-requirements` publishes the floor, the current release and both
store URLs — anonymous, and **the one `/api` route exempt from the floor**, because otherwise the single endpoint
that says where to update is the single endpoint a refused client cannot read. `Models/ClientRequirements` is both
the DTO that route returns *and* the object the middleware measures against, so the floor a client is told about is
the floor it was refused by.
⚠️ Three things it must **not** do, and each is why the code looks as it does. (a) It runs **before**
`UseAuthentication` — a stale shell's *login* has to 426, not 401, because 401 reads as « signed out » and a login
screen the app can never get past is worse than the refusal. (b) It is scoped to **`/api`**: the front door also
serves the web app, and 426-ing the page would replace the French update state with raw JSON. (c) **Anything
unreadable passes** — no header (every browser, every server-side BFF hop), a malformed version, an unset or
typo'd floor. A mistyped setting must refuse nothing, never everything.
⚠️ The client half was **not** just adding a header: fourteen raw-`fetch` sites across eight modules hand-wrote
their own `Authorization` object, so every PDF, CSV export and patient-file upload would have carried the token
and silently omitted the version — the floor covering part of the app only. They all now call the one exported
**`apiHeaders(token, contentType)`** in `client.ts`, and the `api-headers` check fails on a `Bearer` literal
anywhere else. This is `fixes-dont-propagate` caught at the moment the helper gained a second job.

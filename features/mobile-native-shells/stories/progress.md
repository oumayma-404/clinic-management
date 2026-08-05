# Implementation Progress — The clinic on a phone

**Story:** [`story-1-full-clinic-on-a-phone.md`](./story-1-full-clinic-on-a-phone.md) (`Layer: Full`, one story by
explicit choice — plan **R-1**)
**Plan:** [`../plan.md`](../plan.md) (APPROVED)
**Branch:** `feature/audit-sections-3-to-10`

## Part status (authoritative, live)

The unit of progress is the **part**, not the story. Each part boundary is a commit point (R-1).

| Part | Slice | Name | Status | Commit |
|------|-------|------|--------|--------|
| 1 | Phase 0 | The web fixes a webview makes load-bearing | **implemented** — gate green; on-device verification owed | see § Session 1 |
| 2 | Phase 2b | The session lasts the working day | **implemented** — gate green; the felt behaviour (AC-37) owed | see § Session 2 |
| 3 | Phase 2 | A stale app says so | not-started | — |
| 4 | Phase 1 | The Android shell | not-started (R-12 tooling check owed) | — |
| 5 | Phase 1 | The iOS shell | **blocked** — macOS + Xcode + Apple Developer Program | — |
| 6 | Phase 3 | A backgrounded phone still knows | **UNBLOCKED as of session 2** — `multi-tenant-cloud` US-2 has landed (`Application/Common/Interfaces/ITenantScope.cs` + `UnitTests/Common/SystemWideCallerCoverageTests.cs` both exist, suite green). Read `ITenantScope` before executing (plan R-3) | — |
| 7 | Phase 4 | The phone becomes an instrument | not-started (web + Android halves only) | — |
| 8 | Phase 5 | Two store listings | **blocked** — store accounts + 4 deferred business decisions | — |

## Session log

### Session 1 — 2026-08-05 · Part 1

**Scope chosen by the user:** Part 1 only. Physical-device verification is **not** available this session and is
recorded as owed (see *Owed verification*).

#### Working tree note (start of session)

The tree carried only this feature's own planning documents — `blueprint.md`, `exploration.md`, `spec.md` (modified)
and `plan.md`, `stories/` (untracked). Nothing unrelated was in flight, so the story's entry criterion about
"25+ modified files from other work" did not apply this session. `git diff HEAD --numstat` was run before any
staging regardless, and files are staged **explicitly by path**.

#### Branch deviation

The story's entry criteria require a branch off **`main`**, "not off `feature/audit-sections-3-to-10`". The user
directed the work to run **in the current branch** (`feature/audit-sections-3-to-10`) explicitly in the invocation.
Recorded rather than silently followed; no rebase was attempted.

#### Pre-change baseline (AC-12 compares against this)

Recorded before the first edit:

| Gate | Result |
|------|--------|
| `npm run check:responsive` | **11 checks, all pass**, 0 pending. ⚠️ `PENDING_PARTS` held `{P7, P8}` and **no check declares either part**, so the set was already inert — its removal (AC-10) changes no check's enforcement. |
| `npx tsc --noEmit` | **0 errors**, no output |
| `npm run build` | **exit 0, 1 warning** |

⚠️ **The baseline warning count is 1, not 0** — an earlier note here said 0 and that was wrong, read off a build log
that had died before the warning was emitted. The warning is
`@auth0/nextjs-auth0/dist/utils/dpopUtils.js: A Node.js module is loaded ('crypto' … ) which is not supported in the
Edge Runtime`, traced through `lib/auth0.ts`. It is **inside `node_modules`**, pre-existing, and untouched by this
part.

⚠️ **Two build failures during this session were environmental, not code.** Recorded so a future session does not
diagnose them as defects:
1. The first baseline build printed `unhandledRejection … Cannot find module '../chunks/ssr/[turbopack]_runtime.js'`
   at *Collecting page data* after `✓ Compiled successfully` — the stale-`.next`-cache symptom. `rm -rf .next` fixed
   it.
2. A later build failed with `ENOENT … copyfile '.next/routes-manifest.json' -> '.next/standalone/…'` because **two
   builds were running concurrently** (mine — a second `npm run build` was started before the first finished) and the
   second's cleanup deleted the manifest the first was copying. Compilation, typecheck and all 29 static pages had
   succeeded. Never run two `next build`s against one `.next`.

#### Post-change gate (AC-12)

| Gate | Result | vs. baseline |
|------|--------|--------------|
| `npm run check:responsive` | **All 13 checks passed** (11 + `blob-delivery` + `pdf-viewer-params`) | +2 checks, 0 failures |
| `npx tsc --noEmit` | **0 errors** | identical |
| `npm run build` | **exit 0, 1 warning** — the same `@auth0/nextjs-auth0` Edge-Runtime warning, in `node_modules` | **identical count, identical text**; the only textual difference is one frame *inside the Auth0 SDK's own* import trace (`server/client.js` → `server/auth-client.js`), which no file in `web/` touches |

⚠️ The plan predicted "13 checks after Parts 1 **and** 3". The real count after Part 1 alone is **13**; Part 3's
`api-headers` check will make it **14**. The plan's arithmetic was one low — noted so Part 3 does not go looking for a
check that is missing.

**Both new checks were proved to fail before being trusted.** A throwaway `components/__probe-delete-me.tsx`
containing `createElement("a")`, `.download =`, `saveAs(` and `#toolbar=0&navpanes=0` produced a red run (exit 1,
`2 of 13 check(s) failed`) naming all five hits on the right lines; the probe was deleted in the same command and the
run went green again. A green check that has never been shown to go red is indistinguishable from a check that
matches nothing.

#### Device verification — what was actually done, and what was not

⚠️ **No eye pass on the running application was performed, and none is claimed.** There is no `agent-browser` on this
machine, the app was not running (Docker's `clinic-postgres`/`clinic-minio` are up; the API and `web` are not), and
headless Chrome **clamps its window to ~489 px**, so viewport-keyed breakpoints cannot be exercised at 320/390 at all
by that route. What follows is what *was* measured, with the method, in place of a claim.

**The print rules (AC-9) — verified by rendering, not by reading.** The compiled stylesheet
(`.next/static/css/*.css`) was extracted and both `@media print` blocks confirmed present, then a harness
reproducing the real shell (`aside` rail · `header` · `main#contenu-principal` · bottom `nav` · a `fixed` launcher ·
a `[data-sonner-toaster]`) was rendered twice at 1440 px with `<html class="dark">`: once normally, once with
`@media print` rewritten to `@media all`. Result — rail, header, bottom bar, launcher and toast **all gone**;
document content present as **dark ink on white despite the dark class**, which is the `.dark` token override
working; and `<main>`'s inner scrollbar gone with a 3000 px block flowing, i.e. the `dvh`/`overflow-hidden` cage
released. Printing the same harness to PDF gave **4 pages**, confirming the cage release in genuine print media.

**The new PDF preview (AC-6, AC-7) — measured at a true 320 px.** Rendered inside an explicit 320 px container
(which reproduces a phone's *available width* faithfully, and at which `md:` is off exactly as on a phone), in both
pointer modes via the same media-rewrite technique:

| Measured | Value | Reads as |
|---|---|---|
| `body.scrollWidth` vs viewport | equal | **no horizontal overflow** at 320 px |
| card | w=284, h=402 | `aspect-[210/297]` honoured (284 × 297/210 = 402) |
| heading / description | w=236, h=48 / h=100 | both **wrap** and fit; `max-w-[42ch]` correctly capped by the 236 px available |
| « Ouvrir le document » | **h=44** | the `coarse:h-11` touch floor is real, not asserted |
| panel vs frame | `display:flex` under coarse; the frame renders under fine | the two-tree swap works — and confirms the inline-`display:block` trap was correctly avoided |

Content height 264 px inside a 402 px card, so no vertical overflow either.

Still owed on a real device: everything in *Owed verification* below.

## Findings that changed the work

### F-1 · AC-11 was already satisfied — no change made

The spec (`spec.md:106-109`) and plan step 9 both state that `ai-chat.tsx`'s microphone "uses
`webkitSpeechRecognition`, which does not exist on iOS — so today every iPhone shows a button that does nothing".
**That is stale.** `components/ai-chat.tsx:844` already renders the mic button only inside
`{isSpeechSupported && ( … )}`, and `isSpeechSupported` is set from
`window.SpeechRecognition || window.webkitSpeechRecognition` in the init effect (`:184-187`). A prior part of
`mobile-tablet-responsive` closed it.

The control is therefore already **absent, not inert**, which is exactly what AC-11 asks. No edit was made — a
redundant re-gating would have been churn asserting work that was already done. Verified there is no second
voice-**input** control in the file (one `Mic`/`MicOff` button, one `handleToggleListening`).

⚠️ Out of scope but noted for a later part: the speech-**output** toggle (`Volume2`/`VolumeX`, `:747-757`) is *not*
gated on `"speechSynthesis" in window`, so it is inert where synthesis is missing. AC-11 says "voice-**input**", so
this is not that criterion — but it is the same rule one control over.

## Deviations

### DEV-1: The icon generator is Node + `sharp`, not Python + PIL

**Date:** 2026-08-05 · **Story:** 1, Part 1 · **Category:** Technical
**Original plan:** `web/scripts/generate-icons.py` using PIL, justified in `plan.md` as "PIL 12.3, already present.
Emits all seven assets. Deterministic resampling so a re-run produces no diff (R-9)" — alongside
`web/branding/icon.svg` as an editable master "a designer replaces".
**Actual implementation:** `web/scripts/generate-icons.mjs`, rasterising `web/branding/icon.svg` through **`sharp`
0.34.5**, which is already in `web/node_modules` as Next's own dependency.
**Justification:** **PIL has no SVG reader** (verified: `cairosvg`, `svglib`, `resvg_py`, `skia` all absent, and
`PIL.Image.open` cannot decode SVG at all). The plan's two halves are therefore mutually exclusive as written — a
PIL script cannot consume the SVG master the same plan requires. The only PIL-compatible options were to hand-write
the mark **twice** (once as vector, once as PIL drawing calls), which is two authorities for one logo and the
`fixes-dont-propagate` shape `MEMORY.md` records as this repo's dominant defect, or to drop the SVG master and lose
"a designer replaces this one file". `sharp` keeps the SVG as the single authority, adds **no** dependency, and
matches `web/scripts/`'s existing `.mjs` convention (`check-responsive.mjs`).
**Impact:** None on later parts. R-9 (diff churn) is still addressed: the script pins the rasteriser's density and
PNG options, and the SVG master is committed so a regeneration is verifiable rather than trusted.
**Approved:** Yes — user chose this option over both PIL variants.

### DEV-2: Print suppression is `print:hidden` at the element, not four selectors in one CSS block

**Date:** 2026-08-05 · **Story:** 1, Part 1 · **Category:** Technical
**Original plan:** "Add a `@media print` block to `globals.css`: suppress the rail, the bottom bar, the AI launcher
and the toaster; drop `dvh` constraints; force a light surface."
**Actual implementation:** Tailwind's built-in `print:` variant on the shell elements themselves (the rail
`<aside>`, the `<header>`, the bottom `<nav>`, and the AI launcher + panel roots), plus a `@media print` block in
`globals.css` for the three things a utility cannot reach: sonner's own portal, releasing the
`h-dvh`/`overflow-hidden` cage so content flows across pages, and forcing the light document surface.
**Justification:** The AI launcher is an unnamed `fixed` div with no stable hook, so a pure-CSS block needed either a
new `data-*` attribute anyway or a brittle class-fragment selector — and `aside` / `header` /
`nav[aria-label="Navigation rapide"]` are all selectors that stop matching **silently** under an ordinary markup
refactor. A hide colocated with the element it hides cannot drift from it. The `@media print` block the plan asked
for still exists and still owns the policy that is genuinely global.
**Impact:** None on later parts. AC-9's outcome is unchanged.
**Approved:** Yes — user chose this option.

### DEV-3: The two PDF previews became one shared component

**Date:** 2026-08-05 · **Story:** 1, Part 1 · **Category:** Technical
**Original plan:** "Replace the two viewer-fragment iframes with coarse-pointer delivery through `downloadBlob`:
`patients/[id]/page.tsx:2714-2718`, `patient-files-manager.tsx:741-745`" — two inline edits; Part 1's file
inventory lists 9 created files, none of them a component.
**Actual implementation:** One new `web/components/patient-file-pdf-preview.tsx` holding both trees, rendered by
both call sites.
**Justification:** The two blocks are near-identical and their own comments already say "Kept in sync with the same
dialog in `patient-files-manager.tsx`" — i.e. the duplication was already known and already being maintained by
hand. **Part 7 routes this exact surface through the native PDF viewer** (AC-61), so leaving two copies schedules
that fix to land in one of them.
**Impact:** Positive for Part 7 — one call site to change. No behaviour change.
**Approved:** Yes — user chose this option.

### DEV-4: The `<header>` is hidden in print too

**Date:** 2026-08-05 · **Story:** 1, Part 1 · **Category:** Scope (minor)
**Original plan:** names "the rail, the bottom bar, the AI launcher and the toaster".
**Actual implementation:** `dashboard-header.tsx`'s `<header>` also carries `print:hidden`.
**Justification:** AC-9 is "no sidebar, **no navigation**, no assistant launcher". The header *is* navigation
chrome — patient search, the notification bell, the user menu — and printing a patient record with that row on the
page is not "document content only". Hiding the rail while keeping the header would satisfy the plan's list and
fail its own acceptance criterion.
**Impact:** None. Not approved separately — reported here as an in-scope reading of AC-9.

### Auto-approved deviations (trivial)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `manifest.ts`'s `theme_color`/`background_color` corrected `#fdfdfe` → `#f1f8fa` | Trivial | The existing comment justified `#fdfdfe` as "the light `--background` (oklch(0.995 0.002 225))", but `globals.css:79` now declares `oklch(0.975 0.008 215)` ≈ `#f1f8fa`. The value was stale against the live token; the comment stating the rule is rewritten in the same edit. No API, no behaviour beyond the colour it already intended to be. |

## Owed verification (not done this session)

Recorded rather than claimed. The user confirmed no phone/tablet hardware is reachable this session.

- [ ] **AC-4's on-device half** — on a **physical iPhone in Safari**: patient file, invoice PDF, e-invoice XML,
      document PDF and the Word export each *deliver a file*. This is the criterion that fails today and it cannot
      be verified in a desktop browser.
- [ ] **AC-2's install half** — installing from Chrome/Android and Safari/iOS gives a correct, uncropped
      home-screen tile.
- [ ] **AC-9 on the real screens** — printing `/factures`, a patient record and a document. The print *rules* were
      verified by rendering (above), but not against those three surfaces' own markup, and not on a printer. R-6
      also asks for one dialog and one card list to be checked.
- [ ] **The eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard** on the running app. Blocked on
      tooling this session, not deferred by choice — see *Device verification*.

### Second finding, out of Part 1's scope

`web/package.json` still lists **`file-saver`** and **`@types/file-saver`** as dependencies, and after this part
nothing imports either (`document-editor-content.tsx` was the sole caller). Left in place deliberately: removing
them touches the lockfile, which is a wider blast radius than this part's inventory, and an unused dependency is not
dead code in the repo. Worth dropping in a later part that already touches `package.json`.

---

### Session 2 — 2026-08-05 · Part 2 (Phase 2b, AC-35…AC-39)

**Scope chosen by the user:** Part 2 only. Same branch as Part 1 (`feature/audit-sections-3-to-10`) — the branch
deviation recorded in Session 1 still stands and was not re-opened.

#### Working tree note (start of session)

⚠️ **The tree was clean when this session began and did not stay clean.** A parallel `multi-tenant-cloud` **US-3**
change (self-registration retired in favour of operator-provisioned clinics + admin-created users) appeared in the
working tree *during* the session — 10 modified files plus 8 new ones (`AuthController`, `UsersController`,
`Program.cs`, `CreateClinicCommand`, `DeploymentProfile`, `DeploymentProfileTests`, `join/page.tsx`,
`join-wizard.tsx`, `user-management.tsx`, `lib/api/users.ts`, and the new `ProvisionClinicCommand`,
`CreateClinicUserCommand`, `LocalClinicProvisioning`, `CreatedClinicUserDto`, `CreateClinicUserRequest`,
`join-unavailable.tsx`, `lib/api/auth.ts`). **None of it is Part 2's and none of it was staged** — files were staged
explicitly by path after `git diff HEAD --numstat`, per the repo's standing rule.

⚠️ **One consequence worth recording, because it nearly became a wrong fix.** A mid-session full-suite run showed
`DeploymentProfileTests.Every_capability_is_covered_by_the_matrix` **failing**: US-3 had added a 13th capability,
`AllowsSelfRegistration`, with no row in the test's matrix — the derived drift guard doing exactly its job. It read
as an inherited red to clear under the 0-failures policy, and the edit was already drafted when `Edit` refused with
*"File has been modified since read"*: **its own author had fixed it in parallel, in the same minute.** A red seen in
a shared working tree is not necessarily a red on `HEAD`, and it is not necessarily yours to fix. The file was left
untouched and the suite is green.

#### What changed

| File | Change |
|---|---|
| `Application/Features/Auth/Commands/RefreshTokenCommand.cs` | Mints a fresh refresh credential per exchange (`GenerateRefreshToken`) and returns `RefreshToken`/`RefreshExpiresAt`; the doc comment states the **sliding-expiry, not revoking-rotation** property (AC-39, LEARNINGS `:61` at the call site) |
| `Application/DTOs/LoginResultDto.cs` | Doc only — the two fields said "empty on a refresh" / "the refresh path mints an access token only", which this part makes false |
| `web/lib/auth/session-cookie.ts` | **New.** The single writer/clearer of both session cookies: `writeSessionCookies`, `clearSessionCookies`, `clearMustChangeCookie` |
| `web/app/bff/auth/token/route.ts` | Re-sets the cookie through that helper on a successful exchange. The 401-clears and 429/503-leave-alone paths are unchanged, and the JSON body still carries **only** the access token |
| `web/app/bff/auth/local-login/route.ts` | Cookie write replaced by the helper — no attribute changed |
| `web/app/bff/auth/local-logout/route.ts` · `bff/auth/change-password/route.ts` | Their raw `cookies.set` deletions now go through the helper, so no route touches a cookie name directly |
| `UnitTests/Features/Auth/RefreshTokenCommandHandlerTests.cs` | **New**, 7 tests |

**No UI file was touched**, so there is no eye pass to record for this part — the four web files are BFF route
handlers plus one server-only module. `check:responsive` was run anyway: it is the repo's gate, not a UI-diff trigger.

#### Post-change gate

| Gate | Result |
|------|--------|
| `dotnet build` (UnitTests, `--no-incremental`) | **0 errors**, 57 warnings — the pre-existing baseline; **0 of them in any file this part touched** (the full warning list was grepped for the three `.cs` filenames: no hits) |
| `dotnet vstest` — **whole suite**, not just the new class | **1921 passed, 0 failed, 0 skipped** |
| `npm run check:responsive` | **13/13 passed** — unchanged from Part 1. Re-run last, on the committed tree |
| `npx tsc --noEmit` | **0 errors**. Re-run last, on the committed tree |
| `npm run build` | **exit 0, 1 warning** — the same pre-existing `@auth0/nextjs-auth0` Edge-Runtime warning as Part 1's baseline, same text, same import trace. ⚠️ **That green run pre-dates the final comment-only trims** — read *Build gate* below before relying on it |

**The new test class was proved to fail.** Removing the two new lines from the handler and re-running gave **3 of 7
red** — the AC-35 credential/expiry test, the AC-39 double-exchange test *and* the `mustChangePassword` test all
caught it; the probe was then reverted and the restoration verified. A green test never shown red proves nothing.

#### Build gate — the dev-server collision, diagnosed rather than guessed

`npm run build` could not simply be run: the parallel session had **`next dev --turbopack` live against the shared
`web/.next`**, the same collision that produced Session 1's spurious `routes-manifest.json` failure. The user was
asked and chose to stop the dev server; `.next` was removed and **build #1 ran green — exit 0, one warning, matching
Part 1's baseline exactly.**

⚠️ **Then three further builds failed, with three *different* errors, and the diagnosis is worth keeping** because
each one individually looks like a code defect or an environment problem and neither is true:

| # | Failure, all *after* `✓ Compiled successfully` + `Checking validity of types` |
|---|---|
| 2 | `ENOENT … rename '.next/export/500.html' -> '.next/server/pages/500.html'` (29/29 static pages had generated) |
| 3 | `PageNotFoundError: Cannot find module for page: /appointments` at *Collecting page data* |
| 4 | `Cannot find module './5611.js'` from `.next/server/webpack-runtime.js` |

Three plausible wrong conclusions were available: a code defect (no — compilation and typecheck passed every time),
disk exhaustion (no — 318 GB free), and **Smart App Control holding freshly-written files**, which the repo already
documents for the .NET test runner and which fitted the symptom well enough to be believable. The actual cause was
found by noticing that `Remove-Item -Recurse -Force .next` **left 103 files behind**, including
`cache/webpack/client-development/` — *dev* artifacts. `Get-CimInstance Win32_Process` then showed a `next dev`
**created at 21:17:27**, i.e. after build #1 and before builds #2–#4: the parallel session had restarted it. Every
one of the three failures was the same collision, re-created.

**What this means for the gate.** The green build ran against a tree identical to the committed one **except for
three comment-only trims** made afterwards (`local-login`, `token`, `session-cookie` — the comment-budget rule);
`npx tsc --noEmit` and `check:responsive` were both re-run **after** those trims and are green, and a malformed
comment is precisely what `tsc` would have caught. A build re-run on the exact committed tree is therefore **owed but
low-value**, and it needs the dev server stopped a second time — the user's call, not to be assumed from the first.
⚠️ Two operational notes: the `Remove-Item` above ran while that dev server was live and partly cleared its `.next`,
so **the parallel session's `npm run dev` may need restarting**; and never diagnose a `.next` error on this repo
without first checking for a live `next dev`, whatever the error text says.

#### Findings that changed the work

##### F-2 · `Deactivate()` bumps `TokenVersion`, so the deactivation test had to be arranged backwards

The obvious arrangement — set the exchange up, then `user.Deactivate()` — makes the handler refuse at the **version**
check, one branch *above* the `IsActive` check the test exists for. It would have passed while asserting nothing
about deactivation. Every arrangement in `RefreshTokenCommandHandlerTests` therefore mutates the account **before**
reading the version it presents, and the class docstring says so, because the next person adding a case will hit it.

##### F-3 · The sliding session would have outlived the forced-password-change cookie

`local_must_change_password` was set **only at login**, with the session's own expiry. Once the session slides the
flag lapses first — so a user who owes a password change would find the middleware had stopped redirecting them,
while `LocalAuthEnforcementMiddleware` still 403s every non-change-password call: an app that looks usable and is
dead. Both cookies are now written from one server answer on every exchange (DEV-6). Incidentally this also
**clears** a stale flag at login, which the old code never did — a leftover cookie used to strand the next user of
that browser on `/change-password`.

## Deviations (Part 2)

### DEV-5: The shared helper keeps the request-scheme fallback for `Secure`

**Date:** 2026-08-05 · **Story:** 1, Part 2 · **Category:** Technical
**Original plan:** step 2 — "`Secure` from the explicit config flag, **never** re-derived from `NODE_ENV` or the
internal request scheme" (R-8, LEARNINGS `:67`).
**Actual implementation:** `AUTH_COOKIE_SECURE` wins when set; **otherwise** `request.nextUrl.protocol === 'https:'`
— i.e. `local-login`'s existing rule, extracted verbatim into `session-cookie.ts`.
**Justification:** Following the letter would have **removed** `Secure` from the session cookie on any deployment
served over HTTPS that has not set the env var — a silent security downgrade to a route that works today, in order
to fix a divergence that does not exist. LEARNINGS `:67`'s own recommendation is « an explicit config flag **(or the
request's actual scheme)**, never `NODE_ENV` ». And R-8's real concern is that the two writers must not disagree,
which one helper makes structurally impossible whichever rule it contains.
**Impact:** None on later parts. `local-login`'s emitted cookie is byte-identical to before.
**Approved:** Yes — the user chose this over the literal wording.

### DEV-6: The must-change cookie is written by the same helper, on every exchange

**Date:** 2026-08-05 · **Story:** 1, Part 2 · **Category:** Scope (small, additive)
**Original plan:** step 3 names only `SESSION_COOKIE` — "re-set `SESSION_COOKIE` through that helper on success".
**Actual implementation:** `writeSessionCookies` sets **both** cookies from one server answer: it re-sets
`local_must_change_password` with the session's new expiry when the API reports `mustChangePassword`, and clears it
when the API reports false.
**Justification:** F-3 above — the plan's scope leaves the client-side forced-change gate expiring *before* the
session that now slides past it. Deriving the flag from the server's answer rather than from login-time state is
also what makes it self-correcting.
**Impact:** None on later parts. The API's own 403 gate was and remains the authority; this only keeps the redirect
honest. Part 7's biometric-resume work touches the same cookie and now has one writer to go through.
**Approved:** Yes — the user chose this over the plan's literal scope.

### Auto-approved deviations (trivial, Part 2)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `local-logout` and `change-password` also moved onto the helper | Trivial | Their deletions were byte-identical copies of the one in `token/route.ts`. Internal, no attribute changed, no API touched — and it makes the helper's "the single place these cookies are written" claim true rather than aspirational |
| Three multi-line `//` blocks in `local-login`/`token` compressed | Trivial | Comment budget (1–2 lines). No reasoning was dropped: the AC-5.12 "the cookie value is a decodable JWT" note moved into `SessionCookieState.credential`'s doc |

## Owed verification (Part 2)

- [ ] **AC-37, the felt behaviour** — the criterion AC-35 exists to serve, and it needs a running stack: a user
      **active all day** is never prompted by time alone (the cookie's expiry moves forward on each exchange, roughly
      every 30 min), while a user **idle past the refresh window** still is. Not observable in a unit test — the
      exchange is driven by `client.ts`'s token cache and the sliding half lives in a `Set-Cookie` header.
- [ ] **AC-35's cookie half in a real browser** — inspect `local_session` in devtools after ~30 min of activity and
      confirm both the **value** and the **expiry** changed. The handler and the route are tested; the browser's
      acceptance of that `Set-Cookie` is not.
- [ ] **AC-38** — the **desktop WebView2 shell** still signs in and stays signed in. It renders the same bundle
      through the same BFF, so nothing shell-specific is expected, but it is a named criterion and was not run.

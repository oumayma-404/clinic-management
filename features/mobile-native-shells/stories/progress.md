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
| 3 | Phase 2 | A stale app says so | **implemented** — gate green; the on-device half (a real shell below the floor) is owed | `8f42b5d` (⚠️ the `Program.cs` registration travelled in `65a72e6` — § Session 3) |
| 4 | Phase 1 | The Android shell | **implemented** (2026-08-06, session 6) — the shell builds, `lint` is clean with `warningsAsErrors`, both the debug APK and the **minified** release APK are produced. Steps 2–6 and step 9 are landed; **step 7's hardware walk is owed** (no physical Android phone), as is the bundle-identifier decision, which is Part 8's. See § Session 6 | `<pending>` |
| 5 | Phase 1 | The iOS shell | **blocked** — macOS + Xcode + Apple Developer Program | — |
| 6 | Phase 3 | A backgrounded phone still knows | **implemented** (2026-08-05/06, session 4) — backend + availability endpoint + settings statement. Web gate green; the **backend suite and both console verbs could not be re-run** (Smart App Control turned mid-session — § Session 4) | `999b877` |
| 7 | Phase 4 | The phone becomes an instrument | **partly implemented** (2026-08-06, session 5) — the three **shell-free** halves are landed and gated: reachability (AC-62…AC-64), the official forms in a shell (AC-8), the upload retry (AC-77). **Steps 1, 2 and 4 are deliberately not started**, each with its reason recorded in § Session 5 | `8a28846` |
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

---

### Session 3 — 2026-08-05 · Part 3 (Phase 2, AC-28…AC-34, AC-70, AC-71)

**Scope chosen by the user:** Part 3 only. Same branch (`feature/audit-sections-3-to-10`); the branch deviation
recorded in Session 1 still stands and was not re-opened.

#### Working tree note (start of session)

The parallel `multi-tenant-cloud` **US-3** work that arrived mid-session-2 is still in the tree and **grew during
this session** — its author was editing `api/ClinicManagement.API/CLAUDE.md`, `Application/CLAUDE.md`,
`web/CLAUDE.md` and `web/lib/CLAUDE.md` while this part ran. **None of it is Part 3's and none of it was staged.**

⚠️ **`Program.cs` ended up in *their* commit, and the lesson is that the hazard runs both ways.** US-3 added the
`provision-clinic` verb dispatch near the top of the file; Part 3 registers `ClientVersionMiddleware` in the
pipeline ~500 lines below — two disjoint hunks in one file. The plan here was to stage only the middleware hunk with
`git apply --cached` (`git add -p` is unavailable in this environment). Before that could happen the parallel session
**committed** as `65a72e6`, staging `Program.cs` wholesale and carrying my registration in with it: the commit's
diff for that file shows both hunks.

Nothing is broken — the line is in the file, the middleware is registered, and the suite is green — so their commit
was **not** rewritten to unpick it; that would be a worse trade than a misattributed five-line hunk. But the
standing rule (`check-file-is-clean-before-staging`) has only ever been written from the perspective of *not
swallowing someone else's work*. The mirror case is real: **on a shared tree, a file you have edited can be
committed out from under you at any moment.** The practical consequence is that « stage only my hunk » is not a plan
you can hold across a long session — either land the shared-file edit early and small, or accept that it may travel
in someone else's commit.

⚠️ **A second wave of parallel work arrived during this session** and is *also* excluded: `deploy/Caddyfile`,
`deploy/.env.hosted.example`, `deploy/docker-compose.hosted.yml`, the four `API/Maintenance/*Command.cs` files,
the new `API/Maintenance/MaintenanceDatabase.cs` and `Infrastructure/Security/LocalDataProtection.cs`. Every file
in this part's commit was staged **explicitly by path** after `git diff --numstat`, and the four `CLAUDE.md`s both
authors touch were diffed individually first to confirm they carried only this part's lines.

#### Pre-change baseline

| Gate | Result |
|------|--------|
| `npm run check:responsive` | **13/13 pass** |
| `npx tsc --noEmit` | **0 errors** |
| `dotnet build` (UnitTests, `--no-incremental`, scratch `OutDir`) | **0 errors, 57 warnings** — identical to Session 2's recorded baseline |

#### What changed

| File | Change |
|---|---|
| `API/Models/ClientRequirements.cs` | **New.** The DTO `GET /api/meta/client-requirements` returns **and** the object the middleware measures against (`IsBelowFloor`). One type for both halves so the floor a client is *told* about is the floor it was refused by |
| `API/Controllers/MetaController.cs` | **New.** `[AllowAnonymous]` action on a class-policy controller (ConnectivityController's shape). Owns `ClientRequirementsPath`, the const the middleware exempts |
| `API/Middleware/ClientVersionMiddleware.cs` | **New.** 426 + `{ error, code: "client_too_old" }` below the floor; `/api`-scoped; meta route exempt; unreadable ⇒ pass |
| `API/Program.cs` | Registered after `ExceptionMiddleware`, **before** `UseAuthentication` (plan R-11) |
| `API/appsettings.json` | `Clients:{MinimumShellVersion,CurrentShellVersion,StoreUrls:{Android,Ios}}`, all empty = no floor |
| `packaging/server/clinic-server.iss` | The same block in the **operator-owned** `appsettings.Production.json` template (AC-34), ASCII-only to match the file |
| `UnitTests/Api/ClientVersionMiddlewareTests.cs` | **New**, 9 tests |
| `UnitTests/Api/ControllerAuthorizationCoverageTests.cs` | `Meta.ClientRequirements` added to `ExpectedAnonymous` (equal in both directions, so mandatory) |
| `web/lib/api/client.ts` | `createHeaders`/`formDataHeaders` → one exported **`apiHeaders(token, contentType)`** adding `X-Client-Version`; `ApiErrorCode.ClientTooOld`; a French 426 fallback; the `onClientTooOld`/`isClientRefusedAsTooOld` hook |
| 8 modules, **14 sites** (`billing`, `clinics`, `doctors`, `export`, `invoices`, `medical-documents`, `patient-files` ×4, `treatment-plans` ×2) | Hand-written headers → `apiHeaders(...)`. **Headers only** — no response path touched (R-5) |
| `web/lib/realtime/clinic-hub.ts` | The version on the hub's HTTP legs (AC-31), with its reach stated honestly |
| `web/lib/api/meta.ts` · `web/components/client-version-gate.tsx` · `web/app/layout.tsx` | **New** + mounted outside the session provider |
| `web/scripts/check-responsive.mjs` | The `api-headers` derived check — **14 checks** now |

#### Post-change gate

| Gate | Result | vs. baseline |
|------|--------|--------------|
| `dotnet build` (UnitTests, `--no-incremental`) | **0 errors**; the full warning list was grepped for all four new/changed `.cs` filenames — **0 hits**, so 0 new warnings | pre-existing baseline only |
| `dotnet vstest` — **whole suite** | **1987 passed, 0 failed, 0 skipped** | +66 vs. session 2 (9 mine, the rest US-3's) |
| `npm run check:responsive` | **14/14 passed** | +1 check (`api-headers`), 0 failures |
| `npx tsc --noEmit` | **0 errors** | identical |
| `npm run build` | **exit 0, 1 warning** — the same `@auth0/nextjs-auth0` Edge-Runtime warning in `node_modules`, same text, same import trace. Run on a **cleared `.next`**, with **no `next dev` alive** (checked: no process, no listener on 3000–3020 — the parallel session's server had already exited, so nothing had to be stopped this time) | identical |

**Everything new was proved to fail.** Three separate probes on the backend, each reverted and re-verified:
`reported < floor` → `<=` reddened `A_client_at_or_above_the_floor_passes("1.2.0")`; dropping the meta exemption
reddened `The_meta_route_is_exempt_from_the_floor_it_publishes`; making `Applies` return `false` reddened both 426
tests. On the frontend, three throwaway probe files (`components/`, `lib/api/`, and — the one that matters —
`app/__probe/nested/route.ts`) confirmed `api-headers` catches the two browser-side ones and **correctly ignores the
route handler**, i.e. the exclusion is a boundary and not a hole. All probes deleted in the same command.

#### Device verification (eye pass)

⚠️ No `agent-browser` on this machine and the app was not running, so the Session-1 method was used instead: the
**compiled stylesheet** (`.next/static/css/*.css`) driving a harness that reproduces the gate's real markup, in
headless Chrome, with a coarse-pointer twin produced by rewriting `(pointer: coarse)` → `(min-width:1px)`.
Measured at **320 (fine + coarse) · 390 · 844×390 landscape · 820 · 1180 · 1440**, plus two deliberately short
viewports:

| Measured | Result |
|---|---|
| `scrollWidth` vs `clientWidth`, every width | equal — **no horizontal overflow at 320 px** |
| « Mettre à jour sur Google Play » | **h = 44** at every width — the `min-h-11` floor is real |
| « Version requise » line | 12 px — above the 11 px floor |
| card at 320 / 390 / 844×390 / 1440 | 338 / 318 / 298 / 298 px tall, centred, fits |

**F-4 · A real defect the eye pass found — see below.** The mechanical checks, `tsc` and the build were all green
across it.

#### Findings that changed the work

##### F-4 · `items-center` in an `overflow-y-auto` box is § 11's clipping trap, on the vertical axis

The gate was first written `flex min-h-dvh items-center justify-center overflow-y-auto`. Measured at **320×260**
that gives a **354 px card in a 260 px box with `scrollHeight` 323** — about 63 px of card, including the title and
the icon, **unreachable by any means**. It is exactly the failure `.claude/rules/frontend-web.md` § 11 documents for
`justify-center` horizontally (« the inline-start overflow is not in the scrollable region »), rotated 90°:
`align-items: center` pushes overflow to *both* ends and the top end is outside the scroll range.

Fixed by centring with **`my-auto` on the card** and leaving the scroller `items-start` — an auto margin resolves to
0 when there is no free space, so it centres when it can and degrades to top-aligned when it cannot. Re-measured:
`scrollHeight` **386** = 354 card + 32 padding, the whole card reachable, and centring unchanged at 320×720 and
1440×900. `h-dvh` replaced `min-h-dvh` in the same edit: on a `fixed inset-0` box a *minimum* height can grow past
the viewport, which would defeat the internal scroll it exists for. The rule file gained the vertical case.

##### F-5 · AC-31's « and on the hub connection » cannot be fully honoured in a browser, and is not faked

A browser cannot set headers on a **WebSocket upgrade**; SignalR's `headers` option reaches the negotiate request
and the fallback transports only. The header is attached (it costs one option and is real on those legs) and the
limit is stated in the code rather than papered over. It changes nothing operationally: `ClientVersionMiddleware`
guards `/api`, and `/hub/*` is deliberately outside it — realtime is additive (`useClinicRealtime` treats every
failure as invisible), so refusing a hub connection would cost a stale shell its live refresh without ever telling
anyone why. The message the user must see comes from `/api`, which is refused on every route.

## Deviations (Part 3)

### DEV-7: `ClientVersionMiddleware` is scoped to `/api`, not to every path

**Date:** 2026-08-05 · **Story:** 1, Part 3 · **Category:** Technical
**Original plan:** step 2 — « below the floor ⇒ **426** … ; **exempt the meta route itself** ». No other exemption
named; AC-30 says « on every API route but AC-29's ».
**Actual implementation:** the middleware returns early for any path not under `/api`, so the meta route is one of
**two** exemptions rather than the only one.
**Justification:** in a self-hosted install Kestrel is the single browser-facing endpoint and YARP proxies the whole
web app through it, so an unscoped middleware would 426 **the page itself** — and the page is what renders
`<ClientVersionGate>`. A stale shell would see raw JSON where the French update state was supposed to be, i.e. the
fix would destroy its own delivery mechanism. `/bff/auth/*` and `/hub/*` fall outside the prefix too, which is what
AC-32 asks for independently. AC-30's own wording is « every **API** route ».
**Impact:** none on later parts. Pinned by `Nothing_outside_the_api_prefix_is_refused` over `/`, `/login`,
`/_next/*`, `/bff/auth/token` and `/hub/clinic`.
**Approved:** reported here as an in-scope reading of AC-30 + AC-32, not a scope change.

### DEV-8: An unset or unparseable floor refuses nothing — fail-open, stated as a decision

**Date:** 2026-08-05 · **Story:** 1, Part 3 · **Category:** Technical
**Original plan:** silent on what an absent or malformed `Clients:MinimumShellVersion` should do.
**Actual implementation:** `IsBelowFloor` returns false unless **both** the floor and the reported version parse, so
an empty, absent or typo'd floor refuses nothing — and that is the committed default in `appsettings.json` and in
the installer template.
**Justification:** this middleware runs in front of authentication in **every** profile, so the blast radius of a
wrong « below » verdict is the whole API for every client. An operator-owned string that can take the product off
the air on a typo is not an acceptable failure mode, and the symptom (426 on every route) reads as the server being
down rather than as a config error. The opposite direction costs only that a floor nobody set enforces nothing —
which is the correct behaviour for a product whose shells do not exist yet.
**Impact:** Part 4 must set `Clients:MinimumShellVersion` deliberately; an unset floor is not a bug to find later.
Pinned by `An_unset_or_unparseable_floor_refuses_nothing`.
**Approved:** reported as an in-scope reading of AC-34 (« operator-owned configuration »).

### Auto-approved deviations (trivial, Part 3)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| The `kind` parameter is `'json' \| 'none'`, not `'json' \| 'multipart'` | Trivial | Internal naming. Ten of the fourteen sites are **GET blob downloads**, which have no body at all — `'multipart'` would have described none of them, and a parameter whose name is wrong at most call sites is how the next person passes the other one |
| `patient-files.ts`'s four sites stop sending `Authorization: ''` when there is no token | Trivial | The old inline object wrote an **empty** header (`token ? … : ''`); `apiHeaders` omits it. Equivalent for this API, headers-only, no response path touched |
| `web/lib/api/meta.ts` created rather than folding the call into an existing module | Trivial | New endpoint, new module — the per-resource convention. No API changed |
| The stale anonymous-endpoint lists in `API/CLAUDE.md` and `UnitTests/CLAUDE.md` now point at the test | Trivial | Doc-only. Both re-listed four endpoints and had been wrong since the Trust routes and `Auth.Refresh` landed; one fact, one home |

## Owed verification (Part 3)

- [ ] **AC-33's launch half** — the shell reading `/api/meta/client-requirements` **natively** before loading the
      webview. That is Part 4's code and cannot exist before it; only the mid-session half is implemented here.
- [ ] **AC-30/AC-33 end-to-end against a real client** — set a floor, send a below-floor `X-Client-Version`, and see
      « Mise à jour requise » with a working store link. The middleware, the exemption and the header are unit-tested
      and the gate is measured, but nothing has yet sent that header from a real client, because no shell exists.
- [ ] **The 426 → gate round trip in a running browser** — the hook and the component are exercised only by
      construction. A `curl` with the header proves the server half; the client half needs the stack up.
- [ ] **The eye pass on the *running* app.** What is recorded above is a measured harness over the real compiled
      CSS, which is stronger than a claim and weaker than the app. Same tooling gap as Sessions 1 and 2.
- [ ] **`packaging/server/clinic-server.iss` does not compile here** (no ISCC on this machine, R-1). The added block
      is ASCII-only and uses `//` comments with no `{…}` path constant, so neither the encoding trap nor the
      brace-comment trap applies — but the installer is operator-verified, not CI-verified, as it always was.

---

### Session 4 — 2026-08-05/06 · Part 6 (Phase 3, AC-40…AC-55, AC-70…AC-73, AC-75)

**Scope chosen by the user:** Part 6 only. Same branch (`feature/audit-sections-3-to-10`); the Session-1 branch
deviation still stands and was not re-opened.

#### Working tree note (start of session)

The parallel `multi-tenant-cloud` **Part F** work is still in the tree and **grew again** during this session
(`OutboxController`, `Startup/HealthChecks.cs`, `Startup/MigrationLock.cs`, `Startup/RateLimiting.cs`,
`Application/Features/Outbox/`, `DTOs/OutboxDepthDto.cs`, the four `Maintenance/*Command.cs`, three repo
interfaces + impls, `IFileStorage`, both storage backends, `LocalDataProtection`, `deploy/*`, and edits to five
`CLAUDE.md` files). **None of it is Part 6's and none of it was staged** — every file in this part's commit was
staged explicitly by path after `git diff HEAD --numstat`.

⚠️ **The tree did not compile when the session started, and the fix arrived from the other author mid-question.**
`Startup/MigrationLock.cs` had `internal const string AcquireSql = $"SELECT pg_advisory_lock({LockKey})"` — a
constant interpolated string requires every hole to be a constant **string**, and `LockKey` is a `long`, so it was
two `CS0133`s. The user was asked how to handle it (fix-and-leave-unstaged / fix-and-stage / stop) and chose
fix-and-leave-unstaged; by the time the answer came back **its own author had already changed it to
`static readonly`**. Nothing of mine touched the file. This is the Session-2 collision a third time, and the
practical lesson is now well evidenced: *on a shared tree, a red you did not cause is often not yours to fix and
may be fixed under you within the minute.*

#### Pre-change baseline

| Gate | Result |
|------|--------|
| `dotnet build` (solution, `--no-incremental`, scratch `BaseOutputPath`) | **0 errors, 57 warnings** — identical to Sessions 2 and 3 |
| `npm run check:responsive` | **14/14 pass** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run build` | **exit 0, 1 warning** (the `@auth0/nextjs-auth0` Edge-Runtime warning, in `node_modules`) |

#### What changed

**Domain** — `Enums/DevicePlatform.cs`, `Enums/PushDeliveryStatus.cs` (four-state, non-terminal `Blocked`),
`Entities/DeviceRegistration.cs`, `Entities/PushDelivery.cs`, `Repositories/IDeviceRegistrationRepository.cs`,
`Repositories/IPushDeliveryRepository.cs`.

**Application** — `Common/Interfaces/IOsPushAvailability.cs` (the one « can this install push to this platform? »),
`Common/Services/StaffNotificationRules.cs` (the reminder lead time, the doctor→user resolution, the five/four
category split, the fixed labels), `Features/PushDevices/{Commands,Queries}` (register/rebind · deregister ·
availability), `DTOs/PushDeviceDtos.cs`. `Common/Services/NotificationGenerator.cs` now *delegates* its lead time
and its target resolution to the shared rules instead of holding private copies.

**Infrastructure** — `Services/PushConfig.cs` (+ `ResolvedPushCredentials`), `Services/OsPushAvailability.cs`,
`Services/IPushSender.cs`, `Services/HttpPushSender.cs`, `Services/FcmPushSender.cs`, `Services/ApnsPushSender.cs`,
`Services/PushNotificationGeneratorDecorator.cs`, the two EF configurations, the two repositories,
`Deployment/DeploymentProfile.PermitsOsPush`, `Services/ReminderSchedule.DeferPastQuietHours`, the `DbSet`s and the
two query filters, and the DI block in `Extensions.cs` (including `AddSingleton(profile)`, which did not exist).

**API** — `Controllers/PushDevicesController.cs`, `BackgroundJobs/PushDispatchJob.cs`, the conditional recurring
registration in `Program.cs`, the `Push:*` block in `appsettings.json`.

**verify-schema** — one new check, `push-delivery-clinic-matches-device`
(`ISchemaVerificationReader.PushDeliveriesWithMismatchedClinic` + the reader's JOIN + the service's finding).

**Migration** — `20260805214704_AddOsPushDeviceRegistrations` (scaffolded, see below).

**Web** — `lib/api/push-devices.ts`, `components/push-availability-card.tsx`, mounted in `clinic-settings.tsx`.

**Tests** — `Features/Notifications/PushFanOutTests.cs` (14), `Api/PushDispatchJobTests.cs` (15),
`Features/PushDevices/DeviceRegistrationTenantIsolationTests.cs` (9), plus `DeploymentProfileTests` (+2),
`SchemaVerificationServiceTests` (+2 and a 13th positional argument at 13 sites).

#### The migration — scaffolded, and reviewed for the two silent hazards

`dotnet ef migrations add` needs an in-tree build, which failed on `MSB3021`/`MSB3027`: the user's
`ClinicManagement.API` (**PID 9364**) held the DLLs. That is a **file lock, not a compile error** — the error names
the process. The user chose « stop the API, scaffold, restarting is yours », so it was killed and the scaffold ran.
⚠️ **It has not been restarted, and on the current binaries it cannot be** — see the SAC note below.

The generated migration was reviewed against both hazards and is clean on both: **two `CreateTable`s and nothing
else** (no `DropColumn`, no narrowing `AlterColumn`, no backfill, so no destructive statement can precede one), and
**no scaffolded `defaultValue:`** anywhere (every column is in a new table, so there are no pre-existing rows for a
`0` to mean the wrong thing to — the trap `backup-schedule-backfill` exists for). `DeviceRegistrations` is created
before the `PushDeliveries` that references it, and `Down` drops in reverse. The differ also emitted **nothing
unrelated**, which confirms the committed model snapshot was current.

#### Post-change gate

| Gate | Result | vs. baseline |
|------|--------|--------------|
| `dotnet build` (solution, `--no-incremental`) | **0 errors, 57 warnings**; the full warning list was grepped for all thirteen new type names — **0 hits**, so 0 new warnings | identical |
| `npm run check:responsive` | **14/14 passed** | identical |
| `npx tsc --noEmit` | **0 errors** | identical |
| `npm run build` | **exit 0, 1 warning** — same `@auth0/nextjs-auth0` text, on a cleared `.next`, with **no `next dev` alive** (checked via `Win32_Process`) | identical |
| New/changed test classes (filtered) | **99/99 passed** | — |
| Whole backend suite | **2116 passed, 4 failed** — then 3 of the 4 were identified as **stale probe artifacts** and the 4th was fixed; the confirming re-run is **owed** (SAC — below) | see below |

⚠️ **`verify-schema` and `reconcile-money` did NOT run**, and the migration is **not applied** to any database.

#### The gate that could not be closed: Smart App Control turned mid-session

`dotnet vstest` and every `dotnet run -- <verb>` now fail with
`Could not load … ClinicManagement.Application.dll. An Application Control policy has blocked this file.
(0x800711C7)` — **Smart App Control**, which this repo already documents as time-varying. It is not a code defect:
`dotnet build` is green with 0 errors, and the same binaries ran fine **earlier in this same session**.

Everything documented was tried: `dotnet build-server shutdown`, deleting `bin`/`obj`, four different output
directories (`api/.testrun`, `.testrun2`, `.testrun3`, in-tree `bin/Debug`), and three repeat attempts. The block
follows the freshly-compiled `ClinicManagement.Application.dll` to every path, so the in-repo-`OutDir` rule the
`UnitTests` guide records is **no longer sufficient on its own**.

**What that leaves as evidence, stated exactly:**
- The **99/99** filtered run covers every class this part adds or changes, on a clean pre-probe build.
- The **whole-suite** run (2116/4) was against this part's source with **one line different**: the
  `RealtimeResourceResolver` exclusion below. Of its 4 failures, **3 were stale probe artifacts** — the probe DLL
  had not been rebuilt after the probes were reverted, which is why `SystemWideCallerCoverageTests` (which reads
  *source*, not the DLL) passed in the same run while the three DLL-driven ones failed. The 4th was real and is
  fixed.
- **Not directly observed green:** `RealtimeResourceResolverTests.Every_Emitted_Key_Is_Declared_By_The_Frontend`
  after its one-line fix. Its failure text named exactly `pushdevices`, the fix removes that key from the emitted
  set, and `clinic-hub.ts` was not touched — high confidence, but a run is owed and is not being claimed.

⚠️ **Operationally the more important consequence: the API cannot be started from the current build on this
machine until SAC clears the new binaries.** The instance that was running (PID 9364) was one SAC had already
cleared, and stopping it — at the user's direction, to scaffold the migration — is not reversible from here.

#### Everything new was proved able to fail

Three probes applied together, all three reverted and re-verified:
1. `StaffNotificationRules.PushLabel` for `AppointmentCreated` → `"PROBE Nouveau rendez-vous"` reddened
   `The_Push_Label_Is_The_Feed_Rows_Own_Title`.
2. Dropping the actor filter in `AudienceAsync` reddened `The_Actor_Gets_No_Push_For_Their_Own_Action`.
3. Replacing the job's `UseSystemWide(...)` reddened `The_Job_Declares_Its_Cross_Clinic_Read` **and**
   `SystemWideCallerCoverageTests.Every_Path_Without_An_Http_Context_Declares_Its_Tenant_Scope` — the best result of
   the three: US-2's derived guard covered a job written after it, with no edit to the guard.

#### Device verification (eye pass)

⚠️ No `agent-browser` on this machine and the app is not running (see SAC), so **no eye pass on the running
application was performed and none is claimed**. One web file was added (`push-availability-card.tsx`) and it is
built to the contract rather than measured: no fixed widths, `flex-wrap` on both the platform row and the retry
row, `min-w-0 flex-1` on the text column, `size-*` icons with `shrink-0`, `coarse:h-11` on the only button,
`text-2xs` as the smallest type (the 11 px floor), tokens throughout (`bg-success-wash`/`text-success`,
`bg-muted/40`, `border-border`) and no `dark:` twin, `role="status"` on the loading line, and the three empty
kinds kept apart — loading, **failed-to-load with « Réessayer »**, and content — so a failed read never renders as
« push is off ». `check:responsive` (14/14) is the mechanical half and it passed. **The widths owed are
320 / 390 / 820 / 1180 / 1440 + landscape + keyboard.**

## Findings that changed the work (Part 6)

##### F-6 · `DeploymentProfile`'s own invariant would have been the first casualty of the plan's wording

The plan asked for `SupportsOsPush(DevicePlatform)` on `DeploymentProfile`, resolved as « Kind permits **and**
credentials present ». But that file's own doc comment states that *every* capability is derived from `Kind` and
nothing else — and `DeploymentProfileTests` enforces it by reflecting over every `bool` property and asserting it
equals the old `IsLocalMode` truth table. A config-derived capability there is precisely the `httpsConfigured`
shape LEARNINGS `:45` records, which the plan itself flags as **R-4 (Med/High)**.

Split instead, with the user's approval (DEV-9): `DeploymentProfile.PermitsOsPush(platform)` answers the **Kind**
half only, and `IOsPushAvailability` ANDs in the credentials. Two consequences worth keeping: `SelfHostedLan` is ✗
*whatever* an operator configures — asserted directly by
`A_self_hosted_lan_install_permits_no_push_however_it_is_configured`, which resolves a profile with all four keys
**present** — and the four things that need the answer (registration, fan-out, dispatcher, settings) ask **one**
seam rather than three reaching for the profile.

##### F-7 · The push and the feed row legitimately disagree about quiet hours, and the obvious test hid it

`The_Reminder_Push_Waits_For_The_Same_Moment_The_Feed_Row_Does` failed on its first run — asserting
`SendNotBefore == EffectiveFeedTime` for an appointment 24 h + ε out, where the due moment landed at 22:58
clinic-local and the floor correctly deferred it to 08:00. **The production code was right and the test's
assumption was too strong:** an in-app row appearing at 02:00 wakes nobody, so the feed has no quiet-hours floor at
all, while a banner at 02:00 is the entire point of AC-46. Split into two tests — one pinned to 13:00 UTC so the
due moment is inside working hours (a `UtcNow.AddDays(5)` fixture would have passed or failed depending on the hour
the suite ran), and one that asserts the deferral *and* that the feed row stayed put.

##### F-8 · The realtime contract test caught the new feature area, which is what it is for

Adding `Features/PushDevices/Commands` made `RealtimeBroadcastBehavior` emit a `pushdevices` key that
`clinic-hub.ts` does not declare, and `RealtimeResourceResolverTests` failed. The fix is the **exclusion**, not a
new frontend key: a device registration records which *phone one user* is signed in on, so a colleague registering
a handset changes nothing on anybody's screen — broadcasting it would make every browser in the clinic refetch over
a fact none of them render, and would announce clinic-wide that somebody just signed in on a device. Same reasoning
as the `Dashboard` exclusion beside it (per-user state on a clinic-wide bus).

##### F-9 · AC-51/AC-52 had no owner in the plan, and no endpoint to read

Both criteria require the **settings surface** to state push availability *per platform*, and the plan's Part 6
inventory is backend-only (~18 created, 4 modified, zero `web/`) — while no later part covers it (Part 7 is native
capability, Part 8 is stores). It also had no endpoint: nothing published the answer, so neither the settings screen
nor a shell could ask. The user chose the full option (DEV-10): `GET /api/push-devices/availability` plus a card in
« Paramètres ». The endpoint is needed by the shell regardless — a shell that prompts for notification permission
on an installation with no credentials burns the single dialog the OS gives it (AC-75).

## Deviations (Part 6)

### DEV-9: `PermitsOsPush` is the Kind half only; the credentials half lives in `IOsPushAvailability`

**Date:** 2026-08-05 · **Story:** 1, Part 6 · **Category:** Technical
**Original plan:** step 1 — a 14th capability `SupportsOsPush(DevicePlatform)` on `DeploymentProfile`, « resolved
as `Kind`-permits **and** credentials-present ».
**Actual implementation:** `DeploymentProfile.PermitsOsPush(platform)` answers `Kind` alone; a new Application seam
`IOsPushAvailability` (implemented in Infrastructure over the profile + `PushConfig`) is the AND.
**Justification:** F-6 above. The plan's own R-4 is the risk that this capability touches configuration, and its
mitigation (« keep the `Kind` half in the matrix ») is fully achieved by not putting the other half there at all.
It also keeps `DeploymentProfileTests`' reflective bool-property matrix intact — a `bool` property would have been
`false` for `SelfHostedLan` where `IsLocalMode` was `true`, breaking the R-2 truth-table test on arrival.
**Impact:** Positive for Parts 7/8 — one seam to ask. `PermitsOsPush` is a **method**, so it is deliberately outside
the reflective matrix and carries its own theory instead.
**Approved:** Yes — user chose this over the literal wording.

### DEV-10: an availability endpoint and a settings card, which the plan's Part 6 did not contain

**Date:** 2026-08-05 · **Story:** 1, Part 6 · **Category:** Scope
**Original plan:** Part 6 is backend-only; AC-51/AC-52's « the settings surface says so per platform » has no step
and no file.
**Actual implementation:** `GET /api/push-devices/availability` (+ `GetPushAvailabilityQuery`, two DTOs) and
`web/components/push-availability-card.tsx` mounted in « Paramètres » for an admin.
**Justification:** F-9 above.
**Impact:** +1 endpoint, +2 web files, and the web gate now applies to Part 6. Part 4's shell reads the same route
to decide whether asking for OS permission is meaningful.
**Approved:** Yes — user chose this over « backend + endpoint only » and « backend exactly as planned ».

### DEV-11: push credentials come from `PushConfig` over configuration, not through `IReminderSecretProtector`

**Date:** 2026-08-05 · **Story:** 1, Part 6 · **Category:** Technical
**Original plan:** step 4 — « Credentials via `IReminderSecretProtector`, never `IConfiguration` ».
**Actual implementation:** `Infrastructure/Services/PushConfig.cs`, static accessors over a `Push:` section on the
`RemindersConfig`/`TtnConfig` pattern; committed config holds empty strings + `// SECRET`, real values arrive as
env vars.
**Justification:** `IReminderSecretProtector` decrypts **per-clinic** secrets stored on `ClinicReminderSettings` —
`ReminderSettingsProvider:146` is its only read in the solution. FCM/APNs credentials are **per install**: there is
one mobile app, so one Firebase project and one Apple team, and there is no per-clinic row to decrypt. Every other
per-install channel secret in this product already comes from configuration/env (`Reminders:Sms:ApiKey`,
`Reminders:WhatsApp:AccessToken`, `Meta:AppSecret`, `TTN_API_SECRET`). The lesson the plan is reaching for — *a
sender must not read `IConfiguration` itself* — is kept: the senders take a resolved `ResolvedPushCredentials`,
whose single `IsConfigured` predicate is also what the availability seam and the dispatcher's block reason read.
**Impact:** None on later parts. Part 8 sets `Push:Apns:BundleId`, the same value the bundle-identifier decision
fixes.
**Approved:** Yes — user chose this over the per-clinic and dual-key options.

### DEV-12: the decorator lives in Infrastructure, not Application

**Date:** 2026-08-05 · **Story:** 1, Part 6 · **Category:** Technical (structural)
**Original plan:** `Application/Common/Services/PushNotificationGeneratorDecorator.cs`.
**Actual implementation:** `Infrastructure/Services/PushNotificationGeneratorDecorator.cs`, registered by
`AddInfrastructure` (which runs after `AddApplication`, so the wrap is straightforward).
**Justification:** it reads the operator's **quiet-hours window** from configuration, which is Infrastructure's job
in this codebase — and `ReminderScheduler`, the other post-commit best-effort writer implementing an Application
interface, is already there for exactly that reason. Application would have needed `IConfiguration` plus a second
copy of the wrapping-window arithmetic (`ReminderSchedule.DeferPastQuietHours` shares it instead).
**Impact:** None on behaviour, no API change, nothing for later parts. **Not separately approved** — reported here
as a project-convention reading of the plan's file path, on the same basis as DEV-9 and DEV-11, both of which the
user resolved in favour of the convention.

### Auto-approved deviations (trivial, Part 6)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `IPushSender` lives in `Infrastructure/Services`, not `Application/Common/Interfaces` as the plan's file table says | Trivial | Its sibling `IReminderChannelSender` is in `Infrastructure/Services`, and nothing in Application calls a sender — the decorator writes rows, the job (API) sends. Internal placement, no API change |
| `AddSingleton(profile)` added to `AddInfrastructure` | Trivial | `DeploymentProfile` was resolved into a local and never registered, so nothing could inject it. Immutable, startup-derived value; the alternative (each consumer calling `Resolve` again) would re-parse the key and could throw from inside a request |
| `NotificationGenerator`'s private `ReminderLeadTime` and `ResolveTargetUserIdAsync` now delegate to `StaffNotificationRules` | Trivial | A pure extraction with no behaviour change, made because the fan-out needs the same two answers — a second copy would mean a banner arriving at a different hour from the feed row it announces |
| `SchemaVerificationServiceTests`' 13 positional `DataMigrationCounts` sites each gained one argument | Trivial | Forced by the record's new parameter. Applied as 13 individually-anchored `Edit`s, never a find/replace — the trailing `, 0)` fragment appears in unrelated code, and a scripted pass is what corrupted six files in an earlier feature |

## Owed verification (Part 6)

- [ ] **The backend suite re-run**, once Smart App Control clears the binaries. Everything except one
      `RealtimeResourceResolverTests` assertion has been observed green; that one is reasoned, not run.
- [ ] **`verify-schema` before/after the migration, diffed, and `reconcile-money` empty** (AC-73). Neither verb can
      execute (SAC), and **the migration has not been applied to any database**. `reconcile-money` must be empty by
      construction — Part 6 touches no money table — and that emptiness *is* the assertion.
- [ ] **The eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard** on the new settings card. Blocked on
      tooling *and* on the app being startable, not deferred by choice.
- [ ] **AC-40, AC-43, AC-48, AC-54, AC-75 end to end** — every one needs a shell to register a device token, which
      is Part 4 (blocked on Android tooling). The backend half is complete and unit-tested; a real banner, a real
      tap, a real deep link and a real permission refusal are what remain.
- [ ] **AC-46 on a real clock** — the quiet-hours deferral is pinned in a unit test against a fixed instant; that a
      08:00 send genuinely arrives at 08:00 Tunis is not.

---

### Session 5 — 2026-08-06 · Part 7 (Phase 4), the three shell-free halves + Part 4 unblocked

**Scope chosen by the user:** Part 7's halves that need **no** `mobile/` shell — steps 5, 3's web half and 6.
Same branch (`feature/audit-sections-3-to-10`); the Session-1 branch deviation still stands and was not re-opened.

#### Part 4's blocker was removed this session, at the user's direction

The session opened with « which part is now unblocked fully then ? » → « what should i do to unblock part 4 » →
« yes install ». R-12 was re-run first (still failing, identical to session 3), then the toolchain was installed
and **R-12 re-run green** — exact versions in the part table above. Two things it does **not** cover, recorded
rather than glossed: story step 7 wants a **physical Android phone**, and **Smart App Control is `Enforced`**
(`VerifiedAndReputablePolicyState = 1`), the same mechanism that blocked `dotnet vstest` in session 4.

⚠️ **Two install traps worth keeping.** `Invoke-WebRequest` reported **success** on a `cmdline-tools` download
that had **truncated at 64.2 MB of 148.4** — it only failed later, at `Expand-Archive` (« End of Central Directory
record could not be found »). `curl.exe -L --retry 10 --retry-all-errors -C -` fetched the full 148.44 MB in 67 s,
i.e. the connection was never the problem; Android Studio had been saturating it. And `sdkmanager --licenses`
**exits 0 having installed nothing** when its prompt gets no stdin (this shell is non-interactive), printing
« Skipping following packages as the license is not accepted » mid-output — a false green. The fix is the
documented CI route: write the SHA1 digests into `Sdk/licenses/`.

#### Working tree note (start of session)

The tree carried the parallel `multi-tenant-cloud` TTN/e-invoicing work (19 modified + 7 untracked). **None of it
was staged**; by the time this part committed, its author had landed it themselves as Parts D and E (`832ee58`,
`18f8a6c`) and the tree held only this part's six files. `git diff HEAD --numstat` was run before any `git add`
and files are staged **explicitly by path**, per the standing rule.

#### Pre-change baseline

| Gate | Result |
|------|--------|
| `npm run check:responsive` | **14/14 pass** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run build` | exit 0, **1 warning** — the `@auth0/nextjs-auth0` Edge-Runtime warning inside `node_modules`, identical to sessions 1–4 |

#### What changed

| File | Change |
|---|---|
| `web/lib/connectivity/connectivity.tsx` | The poll no longer asks `AUTH_MODE`; it runs on **every** deployment and derives the two axes from the response. `ConnectivityState.isLocal` → **`egressSignalAvailable`**. The outcome→state truth table is in the provider's doc comment |
| `web/components/connectivity-indicator.tsx` | Gates on what the deployment can *say*, not on its auth mode: an unreachable server surfaces **everywhere**, the two egress states only where a reading exists, and a healthy server with no probe renders **nothing** (so the Cloud header stays unchanged in the happy path) |
| `web/lib/api/client.ts` | `NETWORK_ERROR_MESSAGE` — the string **every** failed call surfaces — stopped naming the local network (F-12) |
| `web/components/document-editor-content.tsx` | AC-8: `bs1BlobRef` keeps the preview's bytes; `deliverOfficialFormPdf` hands them to the OS through the **existing** `saveFile` bridge; the preview is two trees behind `coarse:`; « Imprimer » delivers the file when the frame is not rendered. Plus `buildPdfFileName` (one filename for both routes) and a type-aware iframe title |
| `web/components/patient-files-manager.tsx` | AC-77: the input is copied-then-cleared before the upload runs, and `handleFileUpload` takes `File[]`. Plus `accept` matching the server allow-list (DEV-13) |
| `web/scripts/check-responsive.mjs` | New derived check **`local-network-wording`** — **15 checks** now (DEV-14) |

#### Post-change gate

| Gate | Result | vs. baseline |
|------|--------|--------------|
| `npm run check:responsive` | **All 15 checks passed** | +1 check (`local-network-wording`), 0 failures |
| `npx tsc --noEmit` | **0 errors** | identical |
| `npm run build` | exit 0, **1 warning** — the same `@auth0/nextjs-auth0` Edge-Runtime warning, same text, same import trace, on a cleared `.next` with no `next dev` alive | **identical count, identical text** |
| Backend | **not applicable and not run** — this part changes no `.cs` file. `verify-schema` (which **does** exist, `API/Maintenance/VerifySchemaCommand.cs` — checked, not assumed) is not applicable either: no migration | — |

**The new check was proved to fail.** A throwaway `components/__probe-delete-me.tsx` carrying the phrase in a
string **and** in a comment produced a red run (`1 of 15 check(s) failed`) naming **only the string's line** — so
`commentMask` is doing its job and the check reads real wording, not annotations about it. Probe deleted in the
same command; the run went green at 15/15.

⚠️ **One build was burned by editing source while it ran.** A mid-edit read caught « Cannot redeclare
block-scoped variable `deliverOfficialFormPdf` » between an insert and its matching delete. Not a defect — the
`tsc --noEmit` run *after* both edits was clean and the re-run build is green — but the rule is simply **do not
touch `web/` while `next build` is running**, a third variant of the collision sessions 1 and 2 recorded.

#### Device verification (eye pass)

⚠️ **No eye pass on the running application was performed, and none is claimed.** There is no `agent-browser` on
this machine and the stack is not up. The one new UI surface is the official-form coarse-pointer panel, which is
built to the contract rather than measured: it reuses `patient-file-pdf-preview.tsx`'s proven shape verbatim (two
trees behind `coarse:`, `max-w-[42ch]` on the prose, `coarse:h-11` on the single button, no fixed widths, tokens
throughout, no `dark:` twin). `check:responsive` (15/15) is the mechanical half and it passed.
**The widths owed are 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard.**

## Findings that changed the work (Part 7)

##### F-10 · Step 4's premise is stale — the deep-link destination already exists

The plan and story both say « there is **no `/appointments/[id]`** route and the notification panel navigates
nowhere », and prescribe adding `?focus=<id>` to `/appointments`. **Both halves are already built:**
`dashboard-header.tsx:189-193` pushes `/appointments?appointmentId=…` *and* dispatches a `clinic:deeplink` event
for the already-on-that-page case, and `app/appointments/page.tsx:176-198` reads the query param on mount while
`:274` listens for the event. `PushDelivery.AppointmentId` (P6) is the routing id that feeds it.
**Nothing was built**, and `?focus=` was deliberately *not* added: a second parameter meaning what
`?appointmentId=` already means is the `fixes-dont-propagate` shape pointing forwards. What genuinely remains of
step 4 is **App Links / Universal Links registration**, which is shell code (Parts 4/5).

##### F-11 · The camera claim was stale in the other direction — that input had no `accept` at all

Plan and story both state `patient-files-manager.tsx:477` « already renders `accept="image/*"` » and so « gets the
camera free ». It renders **no `accept` attribute**, and it is the **only one of the app's six file inputs**
without one (`clinic-settings`, `mon-profil`, `setup-wizard` → `image/*`; `doctor-document-identity` →
`image/png,image/jpeg`; `import-patients` → `.csv,text/csv`). See DEV-13.

##### F-12 · The third « réseau local » was the one that mattered, and it was not in a connectivity file

AC-64 reads as a two-string fix (the toast and the badge). The third instance was `NETWORK_ERROR_MESSAGE` in
`lib/api/client.ts` — the message surfaced by **every** failed call anywhere in the app, on every deployment,
whether or not the connectivity poll had ever run. Its own doc comment states it is kept in step with
`connectivity.tsx`'s banner « so the two ways the app can notice the same outage do not describe it differently »,
so leaving it would have broken an invariant the file declares about itself. It was found by grep, not by reading
the connectivity code — which is the argument for DEV-14.

## Deviations (Part 7)

### DEV-13: the patient-file input declares the server's allow-list, not the plan's `image/*`

**Date:** 2026-08-06 · **Story:** 1, Part 7 · **Category:** Technical
**Original plan:** step 1 — « `patient-files-manager.tsx:477` already renders `accept="image/*"` … Verify the photo
attaches with the same validation as an uploaded file. » i.e. **no change**.
**Actual implementation:** `accept="application/pdf,image/png,image/jpeg"`, mirroring
`FileContentValidation.PatientFileTypes`.
**Justification:** F-11 — the attribute the plan describes does not exist. Writing the plan's literal `image/*`
would have been worse than leaving it: a referral letter or lab report arrives as a **PDF**, the server accepts
one, and hiding it from the picker removes a working capability (§ 0). The server's own list is the only
non-arbitrary answer, it stops the picker offering the DICOM/TIFF files whose refusal the upload error handler was
written to explain, and naming the image types is what gives Part 4's `onShowFileChooser` something to key the
camera intent on.
**Impact:** Part 4 reads `acceptTypes` rather than assuming `image/*`. No server change.
**Approved:** Yes — user chose this over the plan's literal `image/*` and over leaving it alone.

### DEV-14: a `local-network-wording` check, which Part 7's plan does not ask for

**Date:** 2026-08-06 · **Story:** 1, Part 7 · **Category:** Scope (additive, gate-only)
**Original plan:** step 5 says « fix the wording ». No check.
**Actual implementation:** a 15th derived check in `check-responsive.mjs` failing on « réseau local » in any
`.ts`/`.tsx` under the scanned roots, with **no exemption list**.
**Justification:** F-12 — one of the three instances was in a file nobody would open while doing connectivity
work, and it was the most-surfaced string in the app. `web/` has no test runner, no ESLint and no CI, so the story
itself defines the gate *as* the test; a criterion of the form « no string may say X » has no other home, and § 14
of the frontend rule endorses exactly this shape. Proved able to fail before being trusted.
**Impact:** the gate is 15 checks. A future French string naming the local network fails the build.
**Approved:** not separately approved — reported here as the test deliverable for AC-64 under the skill's
« tests are part of the deliverables » rule, on the precedent of Part 1 (+2 checks) and Part 3 (+1).

### Auto-approved deviations (trivial, Part 7)

| Deviation | Classification | Reason |
|-----------|----------------|--------|
| `ConnectivityState.isLocal` → `egressSignalAvailable` | Trivial | Renaming the field *is* the fix the plan asks for (« stop deriving the poll from `AUTH_MODE` »). One consumer existed and it is edited in the same change |
| `buildPdfFileName()` extracted from `handleDownloadPdf` | Trivial | Pure extraction, no behaviour change, made so the shell delivery and « Télécharger » cannot name the same document differently |
| The official-form iframe's `title` is type-aware | Trivial | It said « bulletin de soins CNAM » on the arrêt de travail too — an accessible name that misnames the document. One line, no API |
| `handleFileUpload` takes `File[]` instead of `FileList \| null` | Trivial | Forced by AC-77: clearing the input empties the live `FileList`, so the copy must be taken first. Both callers updated in the same edit |
| Recovery toasts distinguish which axis recovered | Trivial | The single `else` announced « Connexion internet rétablie » when a LAN **server** came back. Same class of false statement AC-64 is about |

## Owed verification (Part 7)

- [ ] **The eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard** on the running app, for the
      official-form coarse panel. Blocked on tooling and on the stack being up, not deferred by choice.
- [ ] **AC-62 on a real network drop** — lose the cable or cellular and see « Serveur injoignable » within one 15 s
      poll, then automatic recovery. The truth table is pinned by construction; the timing is not.
- [ ] **AC-63 against a real `HostedMultiTenant` deployment** — the probe 404s, the AI chat and the Google controls
      stay **enabled**, and no « pas de connexion internet » warning appears. This is the defect the part exists to
      fix and it has not been observed fixed on a running hosted instance.
- [ ] **AC-8 on a real Android shell** — a BS1 opens in the platform viewer and prints. Needs Part 4.
- [ ] **AC-56 / AC-77 on a real phone** — the camera reaches the record through `onShowFileChooser`, and an upload
      killed by backgrounding leaves the file re-selectable. Needs Part 4.

## Not started, and why (Part 7)

Recorded rather than half-built, per the story's own « never partially implement a blocked part ».

- **Step 1 (camera, AC-56)** — the picker is Part 4's `onShowFileChooser`. The web half is DEV-13 and is done.
- **Step 2 (biometric resume, AC-57…AC-60)** — needs a **new** bridge method, and `mobile/shared/bridge.md` (the
  single contract the plan says to amend, and whose version it says to bump) is created by Part 4. Writing the web
  half now would define that contract unilaterally, untested against any shell. Nothing in `session.tsx` was
  touched, so AC-58's « absent bridge ⇒ unchanged » holds trivially.
- **Step 3's native half (AC-61)** — Android `PdfRenderer` / iOS `QLPreviewController` are shell code. AC-8's web
  half reaches them through the existing `saveFile` open path, which is what the plan's own step 3 prescribes.
- **Step 4 (deep links)** — see F-10: the destination exists; only App Links / Universal Links registration
  remains, which is shell code.

---

### Session 6 — 2026-08-06 · Part 4 (Phase 1, AC-13…AC-27, AC-74, AC-76)

**Scope chosen by the user:** Part 4 only. No physical Android phone this session, and an emulator was
**declined** in favour of the build gate — the same shape Parts 1, 2, 3 and 6 landed with.

#### Working tree note (start of session)

The tree was **clean** at the start. Another author's work on `multi-tenant-cloud` then arrived **mid-session** and
kept growing: three of its story documents, a new `reviews/feature-review.md`, and — by the time this part was
staged — `api/.../Startup/AuthAttemptAccount.cs`, `Startup/RateLimiting.cs`, `Infrastructure/ClientIp.cs` and a new
`Infrastructure/TrustedProxies.cs`. `git status` was re-read immediately before staging and **not one of those eight
paths is in this part's commit**; every file was staged explicitly by path, and nothing of theirs was reverted,
staged or touched.

#### R-12 re-verified, not taken from the previous session's note

| Tool | Found |
|---|---|
| JDK | Temurin **17.0.20+8**, `JAVA_HOME` persisted at User *and* Machine scope |
| SDK platform | **android-35** |
| Build tools | **34.0.0** (AGP 8.7's default, downloaded during the first build) + **35.0.0** |
| Platform tools | `adb` **1.0.41** |
| Gradle | **none on the machine** — see the note below |
| Smart App Control | `VerifiedAndReputablePolicyState = 1` (**Enforced**) — and it never interfered: Gradle runs through the signed Temurin `java.exe` and produces artifacts rather than executing them, which is not the shape SAC blocks (unlike `dotnet vstest` on freshly-built DLLs) |

⚠️ **The env vars are persisted but were absent from this session's shell**, which was started before they were
set. Every Gradle invocation sets `JAVA_HOME`/`ANDROID_HOME` explicitly; an `android/local.properties`
(git-ignored) carries `sdk.dir` with **forward slashes** — a Java properties file treats `\` as an escape, so the
backslash form silently resolves to a mangled path.

#### The Gradle wrapper had to be bootstrapped, and that is not a deviation

There is no standalone `gradle` and no `gradle-wrapper.jar` anywhere on the machine, and Studio's project wizard —
the route the previous session's note names — is a GUI step that cannot run here. So Gradle **8.9** was downloaded
to the scratch directory, its **SHA-256 verified against the published checksum** (`d725d707…cecab`, matched), and
`gradle wrapper` was run once to emit the committed `gradlew` / `gradlew.bat` / `gradle-wrapper.jar` /
`gradle-wrapper.properties`. That is the standard, committed entry point of every Android project, and from here on
nothing outside the repository is needed to build.

⚠️ The checksum step is not ceremony: `Invoke-WebRequest` reports success on a truncated download (LEARNINGS), and
a truncated distribution fails later in a way that reads as a build problem. It also caught a real slip — the first
attempt compared against a **byte array** rather than a string (`-UseBasicParsing` in PowerShell 5.1) and reported
a mismatch on a file that was in fact intact.

#### Versions, and why these

`AGP 8.7.3 · Gradle 8.9 · Kotlin 2.0.21 · compileSdk/targetSdk 35 · minSdk 26`. Pinned rather than latest, to the
SDK actually installed: the current AGP is 9.x and current AndroidX needs `compileSdk 36`, which needs an AGP newer
than any that pairs with the installed platform. `mobile/README.md` records the set as an operator instruction.

`minSdk 26` (Android 8.0) buys the adaptive-icon-only path and drops every pre-26 inset branch.

#### What was built

| File | Role |
|---|---|
| `mobile/CLAUDE.md` · `mobile/README.md` · `mobile/shared/bridge.md` | The map, the operator build/signing guide, **the** bridge contract (AC-27) |
| `MainActivity.kt` | The five states, the WebView configuration, insets, the back gesture, the launch version check |
| `ServerConfig.kt` | The address + `SharedPreferences`; a faithful port of `desktop/ServerConfig.cs`'s `ParseAddress` |
| `ClientRequirements.kt` | The native pre-launch read of `GET /api/meta/client-requirements` |
| `ShellBridge.kt` | `window.__clinicShell` — `saveFile` · `print` · `onPushToken`, plus the injected wrapper |
| `FileChooser.kt` | `onShowFileChooser` + the camera + the extension→MIME resolution |
| `ExternalNavigation.kt` | Off-origin top-level navigations → Custom Tabs |
| `network_security_config.xml` · `data_extraction_rules.xml` · `file_paths.xml` | TLS trust anchors, backup exclusions, the FileProvider path |
| layout · strings · colors · themes · adaptive icon | The five panels, every string in French, the mark copied from `web/branding/icon.svg` |

#### Post-change gate

| Gate | Result |
|---|---|
| `gradlew clean lint assembleDebug assembleRelease` | **exit 0.** Lint: **« No issues found »** — 0 errors, 0 warnings, with `warningsAsErrors = true` |
| debug APK | `app-debug.apk`, 2 343 970 bytes |
| **minified** release APK | `app-release-unsigned.apk`, 126 324 bytes — R8 + resource shrinking exercised |
| R8 keep rule verified | the release `classes.dex` still contains `saveFile` and the injected `__clinicShellNative` / `maxFileBytes` strings. The **class** was renamed, which is harmless: JS reaches the object by the name `addJavascriptInterface` gave it, never by class |
| `npm run check:responsive` | **All 15 checks passed** |
| `npx tsc --noEmit` | **0 errors** |
| `npm run build` | **exit 0**, warning count identical to baseline (the one pre-existing `@auth0/nextjs-auth0` Edge-Runtime warning, inside `node_modules`) |
| backend | **not run — nothing under `api/` changed.** Confirmed against `git status`, not assumed |

**The lint gate was proved red before being trusted.** It is not a check that has only ever passed: the first run
reported **16 warnings**, and after those were cleared `warningsAsErrors` surfaced **2 more** as errors
(`MergeRootFrame`, `LabelFor`). All 18 were fixed or deliberately suppressed with a stated reason, and only then
did the report read « No issues found ». Two of the 18 were real defects rather than style —
`android:allowBackup="false"` alone does not exclude an Android-12+ **device-to-device transfer**, which would have
handed a live clinical session to whatever restored it; and the `LabelFor` finding was the accessibility label
being **both** a `labelFor` and a `hint`, which is the one combination lint rejects.

Four suppressions, each with its reason in the file it sits in:

- `GradleDependency` (disabled in `lint {}`) — it reports « a newer AndroidX exists », not a defect, and bumping
  needs a `compileSdk` this project is not verified against.
- `SetJavaScriptEnabled` — JavaScript is what the shell exists to run.
- `AcceptsUserCertificates` — DEV-15's deliberate decision; the file states it.
- `MergeRootFrame` — the exception lint's own text names: the root **does** carry padding, applied at runtime by
  `applyWindowInsets`, and a `<merge>` would delete the view the inset listener pads.

#### Device pass

⚠️ **No pass on real hardware, and none is claimed.** No Android phone is available; an emulator was declined.
Everything in *Owed verification (Part 4)* below is untested on a device. What *is* established is that the project
builds, lints clean and produces both a debug and a minified release artifact — i.e. the code exists and compiles,
which is what a session with no phone can honestly deliver.

The device *contract* was nonetheless designed in rather than deferred, and the reasoning is in the files: every
panel is inside a `ScrollView` with `fillViewport` so a ~380 dp-high landscape phone reaches the buttons; every
button is ≥ 48 dp; every size is `sp`/`dp` and never a pixel; the two side-by-side buttons of the address panel were
**restacked full-width** because a button bar is cramped at 320 dp (which also cleared the `ButtonStyle` finding).

## Findings that changed the work (Part 4)

### F-11 · AC-23 is unachievable without `configChanges`, and the plan was right to say so

Android recreates an activity on rotation *and* on a Split View resize by default, which destroys the WebView and
reloads the app — losing whatever was typed in an open dialog. `android:configChanges` therefore lists
`orientation|screenSize|smallestScreenSize|screenLayout|density|fontScale|uiMode|keyboard|keyboardHidden|navigation|touchscreen|layoutDirection`.
No amount of web-side work can compensate from inside.

### F-12 · targetSdk 35 forces edge-to-edge, so AC-22 needed a different answer than expected

An app targeting SDK 35 draws under the system bars on Android 15 whether it asks to or not, and
`setDecorFitsSystemWindows(true)` no longer opts out. The plan's assumption — that `viewportFit=cover` plus the web
app's `--bottom-inset` suffices — holds only if this WebView build reports the navigation bar through
`env(safe-area-inset-bottom)`, which is **version-dependent and untestable from here**. So `applyWindowInsets`
consumes the system-bar + cutout insets as **padding on the root** and folds the IME inset into the same bottom
value: the viewport ends above the gesture bar on every version, and the keyboard shrinks it so the app's
`dvh`-sized sheets keep their sticky footers reachable.

### F-13 · `onReceivedHttpError` must NOT be handled — that omission *is* AC-74

An HTTP status means the server answered, and what it answered with is the app's own French error page. Replacing
that with a shell state would produce exactly the « blank app » AC-74 forbids. Only a transport failure
(`onReceivedError`, main frame only) becomes « Impossible de joindre le serveur ». The same reasoning covers the
native half of AC-74: the launch probe hitting a server too old to have `/api/meta/client-requirements` reads as
« no floor » and the app loads normally.

### F-14 · AC-76 cannot be implemented in a shell at all, and the login path was never the gap

A WebView does not observe its page's `fetch` responses — the spec says so itself under FR-8 — so the shell cannot
see the 403. Reading the code settled the rest: `/bff/auth/local-login` writes `local_must_change_password` and
`middleware.ts` already holds a user on `/change-password`, so the **login** path was fine. The real gap is an admin
resetting the password of somebody **already signed in**: no login, so no cookie, and every call from then on 403s —
surfacing `LocalAuthEnforcementMiddleware`'s **English** sentence verbatim through `lib/errors.ts`, in a product
whose own rule is that no English string reaches a user. Fixed in `client.ts` — see DEV-17.

### F-15 · An `accept` attribute may hold extensions, and the CSV import's does

`WebChromeClient.FileChooserParams.acceptTypes` passes `accept` through unchanged, so `.csv` arrives as a literal
extension. Handing that to the picker as a `type` matches nothing: the file list comes up **empty** and the import
looks broken rather than unsupported. `FileChooser.resolveMimeTypes` resolves extensions through `MimeTypeMap` and
widens anything still unresolvable to the wildcard type — a worse filter but a working one.

### F-16 · A wildcard MIME literal inside a KDoc comment terminates the comment

The three characters `*`, `/`, `*` in a doc block close it, and Kotlin then reports 17 « Expecting member
declaration » errors on the line *after*. Recorded because the error text points nowhere near the cause. Its XML
sibling cost a build in the same session: an XML comment may not contain a double hyphen, and a comment quoting the
CSS custom properties `primary` / `primary-foreground` by their `--` prefix failed `mergeDebugResources`.

## Deviations (Part 4)

### DEV-15: `network_security_config.xml` trusts user-installed CAs

**Date:** 2026-08-06 · **Story:** Part 4 · **Category:** Technical (security-adjacent)
**Original Plan:** silent — the plan specifies `mixedContentMode = NEVER_ALLOW` and nothing about trust anchors.
**Actual Implementation:** a `network_security_config.xml` declaring `system` **and** `user` trust anchors, with
`cleartextTrafficPermitted="false"`.
**Justification:** a `SelfHostedLan` install serves a certificate the API mints itself into `.local/` on first boot,
so the system store cannot validate it and **every** load fails — the shell would be unable to reach one of the
three shipped topologies, the one the desktop shell exists for. The alternative that "works" — overriding
`onReceivedSslError` to proceed — is blanket MITM acceptance and was rejected outright; `onReceivedSslError` is not
overridden anywhere in this app, so an untrusted certificate still fails loudly into « Impossible de joindre le
serveur de la clinique ». A user CA requires a deliberate per-device action with an OS warning, which is exactly
what the LAN device-trust page (`mobile-tablet-responsive` P8) exists to walk an operator through.
**Impact:** Android Lint's `AcceptsUserCertificates` is suppressed at that element with the reason in the file.
`mobile/README.md` documents the operator step and states that a hosted deployment with a publicly-trusted
certificate needs none of it.
**Approved:** Yes — asked and confirmed before any code was written.

### DEV-16: « Recharger » and « Changer de serveur… » hang off the back gesture, not a menu bar

**Date:** 2026-08-06 · **Story:** Part 4 · **Category:** Scope
**Original Plan:** « the five French states with « Réessayer », « Changer de serveur… », « Recharger » », mirroring
the desktop shell's `Serveur` menu.
**Actual Implementation:** « Réessayer » and « Changer de serveur » are buttons on the states that need them. The
back gesture at the **root** of the app opens a « Serveur » dialog carrying **Recharger · Changer de serveur… ·
Quitter**.
**Justification:** the desktop shell can afford a permanent menu bar; AC-13 requires « full-screen, no browser
chrome », so a persistent strip contradicts the criterion it would serve. Back-at-the-root was otherwise a free
gesture that closed the app outright — turning it into a deliberate « Quitter » is the standard Android answer and
satisfies AC-24's « navigates within the app rather than closing it » at the one place the criterion is otherwise
silent.
**Impact:** AC-15's « each reachable in a test » holds; the hardware walk must exercise the gesture, and the owed
list says so.
**Approved:** Auto — no capability is removed and no API changes; recorded because it departs from the plan's
literal wording.

### DEV-17: AC-76 is implemented in `web/lib/api/client.ts`, not in the shell

**Date:** 2026-08-06 · **Story:** Part 4 · **Category:** Scope
**Original Plan:** Part 4 step 9 assigns AC-76 to the shell, and the plan's file inventory says Part 4 modifies
**0** files.
**Actual Implementation:** `ApiErrorCode.MustChangePassword` + an `onMustChangePassword(listener)` hook in
`client.ts`, a French message replacing the backend's English one, and `LocalSessionProvider` navigating to
`/change-password`. `mobile/` implements nothing for AC-76.
**Justification:** F-14. A WebView cannot observe its page's `fetch` responses, so the shell **cannot** see the 403
— the plan's own spec says so under FR-8, which makes step 9 unimplementable as written and the « modifies 0 files »
row an error rather than a constraint. `client.ts` is the single point every response passes through, it already
carries the identical hook for the 426, and fixing it there closes the same gap for the browser.
**Impact:** two `web/` files changed, so the frontend gate was re-run in full (green). No caller's error handling
changes: the branch is keyed on the machine-readable `code`, and the message substitution is the only place this
module overrides a server-sent string — stated in its own doc comment.
**Approved:** Yes — asked, with the alternative (« shell only; record the gap ») spelled out, and confirmed.

### Auto-approved deviations (trivial, Part 4)

| Deviation | Classification | Reason |
|---|---|---|
| A sixth Kotlin file, `ClientRequirements.kt` | Trivial | The plan names five; the launch version read is a native HTTP call plus a JSON parse and does not belong inside `MainActivity`. No API surface, no dependency. |
| `lint { warningsAsErrors = true }` | Trivial | Makes the module's only static gate hold the repo's own 0-warnings policy. Proved red first. |
| `data_extraction_rules.xml` | Trivial | The Android-12+ half of `allowBackup="false"`, which lint named. Contained, no API. |
| Adaptive icon at `mipmap-anydpi/` rather than `-anydpi-v26/` | Trivial | `minSdk` is 26, so the qualifier is dead weight (`ObsoleteSdkInt`). |
| The address panel's two buttons restacked full-width | Trivial | Cleared `ButtonStyle` and is what 320 dp wants; matches the other three panels. |
| `config_hint` string removed | Trivial | Lint accepts a `labelFor` **or** a `hint`, not both; the example address is already in the help line. |

## Owed verification (Part 4)

Every one of these needs a **physical Android phone**. None was available.

- [ ] **AC-13** — the APK installs and opens the hosted app full-screen, no browser chrome.
- [ ] **AC-14** — sign in, kill the app, relaunch: **still signed in**. The cookie store and the `onPause` flush are
      in place; only hardware can show the session survived a real process death.
- [ ] **AC-15 / AC-16** — all five states reached, all French, « unreachable » naming the address *and* the reason,
      and « Réessayer » succeeding on an **unchanged** address once the server returns.
- [ ] **AC-17** — the address survives a relaunch and is changeable from the « Serveur » dialog (DEV-16's gesture).
- [ ] **AC-18** — all six file inputs open the picker; an image input offers the **camera**; the CSV import's `.csv`
      accept resolves to a picker that actually lists files (F-15).
- [ ] **AC-19 / AC-20** — every Part 1 delivery path lands a file through `saveFile`, and a **> 25 MB** file is
      refused with the limit named *before* the blob is read.
- [ ] **AC-21** — printing through the OS print service, and the output carrying document content only.
- [ ] **AC-22** — the bottom navigation clears the gesture bar (F-12's inset padding, on real hardware).
- [ ] **AC-23** — rotation and split-screen do **not** remount, and typed input in an open dialog survives.
- [ ] **AC-24** — the back gesture navigates within the app; at the root it opens the « Serveur » dialog.
- [ ] **AC-25** — « Connecter Google Agenda » leaves to a Custom Tab and the app shows the connected state on
      return. ⚠️ See the limitation below.
- [ ] **AC-26** — with `window.__clinicShell` **deleted at runtime**, every affected screen behaves as in a browser,
      including `window.print()` doing nothing rather than throwing.
- [ ] **AC-74** — a route the server does not have is an ordinary French error on that one screen.
- [ ] **AC-76** — the change completes **inside the shell** after an admin-forced reset (the web half is landed and
      gated; only the in-shell walk is owed).
- [ ] The **release** artifact on hardware: this session built a minified release APK but installed nothing, so R8's
      effect on the bridge is verified by inspecting the dex, not by using the app.

### Known limitation, not an omission — AC-25's automatic return

Nothing in the Custom Tabs API reports which URL a tab reached, so the OAuth callback landing back on the clinic's
origin **inside the tab** is not observable. Only an `intent-filter` with a **verified App Link** would close the tab
and hand the navigation back, and that needs a fixed publicly-resolvable domain — one of Part 8's four deferred
decisions. Until then the shell reloads the page the user left as soon as it is resumed, which reaches the
criterion's outcome (« shows the connected state ») by resume rather than by redirect. Recorded in
`ExternalNavigation.handOffInFlight`'s own doc comment so the App Links follow-up has one obvious home.

### Still owed to the story, and whose decision it is

- **The bundle identifier and the display name.** `applicationId` is `com.clinicmanagement.shell`, marked
  **provisional** in `build.gradle.kts` and in `mobile/README.md`. It **cannot be changed after the first store
  submission** — Part 8's decision, and nothing here should be read as having settled it.
- **Signing.** No keystore is committed and none is scripted; `mobile/README.md` carries the `keytool` and
  `bundleRelease` commands and the warning that losing the keystore ends the listing's ability to update.

### What Part 4 unblocks

- **Part 7 step 2 (biometric resume, AC-57…AC-60)** was recorded as « unstarted until `mobile/shared/bridge.md`
  exists ». It exists now, so the contract can be amended rather than invented.
- **Part 7 steps 1, 3 and 4** (camera on hardware, the native viewer, deep links) all have their Android half's
  home: `FileChooser`, `ShellBridge.saveFile`'s open path, and an App Links `intent-filter` respectively.
- **Part 6's device-token criteria** now have a shell to register from — `onPushToken` is wired and inert, and the
  delivery seam (`window.__clinicShellDeliverPushToken`) is the one line FCM has to call.

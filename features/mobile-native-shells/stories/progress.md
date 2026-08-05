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
| 2 | Phase 2b | The session lasts the working day | not-started | — |
| 3 | Phase 2 | A stale app says so | not-started | — |
| 4 | Phase 1 | The Android shell | not-started (R-12 tooling check owed) | — |
| 5 | Phase 1 | The iOS shell | **blocked** — macOS + Xcode + Apple Developer Program | — |
| 6 | Phase 3 | A backgrounded phone still knows | **blocked** — `multi-tenant-cloud` US-2 (`ITenantScope`) | — |
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

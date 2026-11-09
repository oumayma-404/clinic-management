# QA run 1 — Nouveautés par e-mail depuis la console

**Date:** 2026-11-06
**Cycle:** 1 of at most 3
**Plan:** features/console-news-broadcast/qa/plan.md (23 rows: A7 B7 C5 D4)
**Driver:** features/console-news-broadcast/qa/walk.mjs (+ api-rows.mjs, totp.mjs) — run from the session scratchpad
**Environment:** fully isolated — API from the worktree on :5094 (console listener :5494, started 13:14 by me),
console `.next/standalone` production build on :3094 (mine), new DB `clinic_news_qa` on `clinic-postgres`, own key
ring, MinIO bucket `news-qa`, shared Mailpit (:8025, QA addresses `@news-qa.test` only). Nothing shared was written.
**Peer sessions live during the run:** clinic-management-be (busy), clinic-management-77 (shell), clinic-management-29
(idle) — none use these ports or this database
**Widths looked at:** 320 · 390 (coarse pointer) · 820 · 1180 · 1440 · 1536×730
**Status:** RED (1 finding: 1 minor)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Envoyer à 2 cabinets » after the header link |
| A2 | ✅ | iframe: kicker, `<h1>` = subject, 🚀/📅 rows, closing line, logo decoded; shots/A2-preview-1440.png |
| A3 | ✅ | « E-mail de test envoyé à qa@news-qa.test. »; Mailpit: 1 message « [Test] QA nouveautés 122319 » to qa@ |
| A4 | ✅ | sheet « 2 cabinets · 3 adresses d'administrateur » + « 1 cabinet sans adresse… »; « Envoi en cours vers 3 adresses. »; form reset; shots/A4-confirm-1440.png |
| A5 | ✅ | Mailpit: exactly 3 — a@, b2@, B@News-QA.test (≡ b@); none to s@, c@, d@, f@ |
| A6 | ✅ | history row « 3 / 3 envoyés », no « en cours » (only this subject's row counted) |
| A7 | ✅ | HTML: kicker, `&#128640;`, closing, « administrateur du cabinet Cabinet Essai », `href="http://localhost:3093/login"`; text: « 🚀 Les rappels SMS partent la veille. » |
| B1 | ✅ | replayed body → 200 `alreadyQueued: true`; still 3 mails; 1 history row |
| B2 | ✅ | 5 refusals → 400 `news_invalid` with French sentences; history empty |
| B3 | ✅ | preview HTML has `&lt;b&gt;gras&lt;/b&gt;`; subject `<i>` escaped |
| B4 | ✅ | both send buttons disabled with no text |
| B5 | ✅ | move → r1,r3,r2; delete → r1,r2; « Ajouter » disabled at 12; last row undeletable |
| B6 | ⏭ | the field's `maxLength` caps a row at 300; the server refusal is B2 |
| B7 | ⏭ | SMTP-not-configured needs a second API without SMTP — unit test in `/test-small-feature` |
| C1 | ✅ | 320: no horizontal scroll, preview under the form, history as cards; shots/C1-nouveautes-320*.png |
| C2 | ✅ | coarse pointer (CDP touch emulation, `matchMedia` true): select 44 px, 9 buttons ≥ 44 px |
| C3 | ✅ | 1536×730: « Envoyer »/« Revenir » bottom at 449 px; shots/C3-confirm-1536x730.png |
| C4 | ✅ | no overflow at 390/820 (cards) and 1180/1440 (table) |
| C5 | ✅ | audience `{clinics:2, recipients:3, clinicsWithoutAdminEmail:1}` with a duplicate address seeded |
| D1 | ⏭ | byte-identical existing e-mails is a unit assertion — `/test-small-feature` |
| D2 | ✅ | `/cabinets` header wraps to two lines at 320, no overflow; shots/D2-cabinets-320.png |
| D3 | ✅ | `state=active` → Cabinet Essai, Cabinet Payant, Cabinet Sans Admin |
| D4 | ✅ | `/journal` « Journal des accès », `/retours` « Retours » |

Also: migration applied on the empty DB (182 migrations, 3 new tables, **no `xmin` column**); `verify-schema` shows
every new index/FK present — its one DRIFT is `key-ring-protection`, i.e. my throwaway unencrypted key ring.

## Findings

### F-1 · minor · the confirm sheet has no visible « X » and 60 px of empty space under its buttons

- **Repro:** `/nouveautes` → type a row → « Envoyer à 2 cabinets ».
- **Expected:** the close « X » at the panel's top-right; the panel ends under its footer.
- **Actual:** no « X » anywhere; the panel runs ~60 px past « Envoyer ». Measured: the close button's computed
  `position` is **`relative`** (class list says `absolute`), so it sits in the flow at y 582–598, below the
  content (318–550), and `overflow-hidden` clips it.
- **Evidence:** shots/A4-confirm-1440.png, shots/C3-confirm-1536x730.png; probe-sheet.mjs output.
- **Suspected owner:** `console/app/globals.css:120` — `.touch-target { position: relative; }` outranks the
  primitive's `absolute` on the same element (the CSS source-order trap the web app already fixed with
  `:where(.touch-target)`). Shared console primitive: every console sheet is affected.
- **Regression?** No — pre-existing in the console's `ui/sheet.tsx` + `globals.css`; visible on this new dialog.
  Escape and « Revenir » still close it, so no capability is lost.

## Observations (off-plan)

- In the e-mail, Windows renders 🦷 as a pale grey tooth, so the kicker's first emoji is faint. The emoji is the
  owner's choice; not a defect.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| C2 | 9 buttons « 36 px » | `Emulation.setEmulatedMedia` does not switch `pointer`; used `setDeviceMetricsOverride` + `setTouchEmulationEnabled` and asserted `matchMedia('(pointer: coarse)')` |
| A6/B1 | would count the first drive's history row on a re-drive | count only rows with this run's subject |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed — wider than reported | `.touch-target { position: relative }` sat at full specificity in `@layer utilities`, after `.absolute` in the built stylesheet, so it beat every `absolute` on the same element: the sheet's « X » **and** the login page's password « eye » (`ui/password-input.tsx:42`) | `console/app/globals.css` (`:where(.touch-target)`), `console/scripts/check-responsive.mjs` (new N9) | every console `<Button>` (no position utility → still `relative`, C2 re-checked), 2 `absolute` consumers fixed (C6, D5 added) |

**Guard added:** `check:responsive` N9 `touch-target-position` — derived from every `.css` file: a bare `.touch-target`
rule declaring `position` fails. Proven red on the unfixed `globals.css:120` before the fix.
**Gates after the last edit:** console `check:responsive` 16/16 ✅ · `tsc --noEmit` ✅ · `npm run build` ✅ (built CSS
holds only `:where(.touch-target){position:relative}`). Backend untouched by the fix.
**Re-run:** run-2.md

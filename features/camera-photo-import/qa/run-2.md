# QA run 2 — Camera photo import

**Date:** 2026-11-16
**Cycle:** 1 of at most 3 (this pass)
**Plan:** features/camera-photo-import/qa/plan.md (46 scenarios: A17 B14 C9 D11)
**Drivers:** `walk.mjs` (one headless launch), `scale.mjs` (C8), `console.mjs` (D8)
**Environment:** isolated — API :5099 (worktree `bin/Debug`, DB `clinic_camera_sd`, bucket `camera-demo-5e2c`; restarted
with `Console__Port=5196` for D8), web :3099 (production build), console `next dev` :3196. Shared stack, shared
database and prod untouched. Peers: none on this stack.
**Widths looked at:** 320 · 390 · 820 · 1180 · 1440 · 1536×730 · 740×380, coarse pointer at 820
**Status:** RED (4 product findings: 2 major, 2 minor, + 1 observation) — plus the code review's findings
(`reviews/feature-review.md`), fixed in the same pass

## Scenarios (second drive of the same walk, after probe fixes)

| ID | Outcome | Evidence |
|----|---------|----------|
| B1–B3 | ✅ | the three empty states, each with its own sentence; no sideways scroll at 320 |
| A1 | ✅ | review in 4 s; « 6 photos déjà rangées », « Afficher aussi 6 photos plus anciennes », « 2 RAW · 1 vidéo ignorés » |
| B7 | ✅ | « Ranger » disabled; « Choisissez le patient de 5 groupes » + « Voir » |
| C6 | ✅ | sticky bar on screen at 1536×730 and 390×844 (probe hit-tested the disabled button first) |
| C5 | ✅ | coarse pointer at 820 (touch emulation, `(pointer: coarse)` asserted): burst cards and the new-patient form ≥ 44 px; the viewer's « × » 43,x px — F-6 |
| D4, B5 | ✅ | older photos shown / hidden; the Ines choice survives |
| A2–A9, B8, B10, C9 | ✅ | every mistake case decided as planned; « Leila » finds « Leïla Trabelsi » |
| A3 | ✅ | viewer opens, photo painted 6000×4000, « Écarter » → « 4 photos gardées sur 5 » |
| B9 | ✅ | ←/→, Escape, focus back on the thumbnail (probe compared titles of two shots of the same minute) |
| C2, C3 | ✅ | viewer footer on screen at 320, 390, 1536×730; image shown at 740×380 |
| A10, A10b | ✅ | « Aucun patient » → « Nouveau patient « Sonia Mejri » » → created once, chosen |
| B11 | ❌ F-3 | « Annuler » empties the search box |
| B12 | ✅ | duplicate question shown (confirmed by a direct reproduction; the walk's miss was its own state) |
| A11, C1, C4 | ✅ | no-date photo; review and new-patient form with no sideways scroll at every width |
| A12–A14, B13 | ✅ | 40 filed after 2 cut uploads + « Réessayer »; no key twice; same card → « 46 déjà rangées »; a repeated key → 400 with its code |
| A15 | ❌ F-2 | new camera: groups « Aucun rendez-vous à cette heure-là » offering the importer's own patients |
| C7 | ❌ F-4 | reset clock: no hint that the camera's date is wrong |
| D7 | ✅ | quota stated; over it → refused in French, nothing stored |
| D9 | ✅ | manifest lists the hosted files and not the coffre file |
| C8 | ✅ | 399 full-size photos (1,87 Go): read in 3,5 s, 7 groups all sure, filed in 263 s, 0 failure, heap ≤ 13 Mo, Chrome ≤ 1,5 Go |
| D8 | ✅ | console: « Stockage » column, storage sort, quota needs a motif, stored, journaled |
| B14, A16, B6, B4, A17, D5, D6 | ⏭ | blocked by A15's stricter expectation in this drive — re-run in run 3 |
| D10, D11 | ⏭ | the Windows app connects over HTTPS to a trusted certificate only; owed with the real camera |

## Findings

### F-1 · major · « Autre carte » throws the review away with no question
- **Repro:** sort a few groups, press « Autre carte ».
- **Actual:** every choice, cut and set-aside photo is gone; no confirmation. (Also raised by the review.)
- **Owner:** `components/photo-import/import-screen.tsx` — `resetImport` on the header button.

### F-2 · major · a new camera's groups offer the guessed agenda's patients
- **Repro:** Nikon card (new camera, two practitioners).
- **Actual:** « Aucun rendez-vous à cette heure-là » under each group, offering Bilel Khemiri and Olfa Mansour (the
  importer's patients); the bar says « Choisissez le patient de 4 groupes » while one answer at the top was needed.
- **Owner:** `lib/photo-import/match.ts` `askUntilOwnerKnown` (only sure groups held), `import-screen.tsx` bar.

### F-3 · minor · « Annuler » in « Nouveau patient » loses the typed name
- **Actual:** back to an empty search box. **Owner:** `burst-card.tsx` (the search remounts).

### F-4 · minor · a camera clock reset gives no hint
- **Repro:** the paired Canon dated 2019-01-01.
- **Actual:** « Les photos de la carte sont plus anciennes que la dernière importation. » and nothing about the date.
- **Owner:** `import-screen.tsx` `emptyReview`.

### F-6 · observation · the dialog's own close « × » measures 43,x px on a coarse pointer
- Shared primitive (`ui/dialog.tsx`, `coarse:size-11`): a sub-pixel reading of a 44 px box, not this feature.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| C5, B9, C2, C3 | « strict mode violation » | two shots of the same minute share a label → `.first()`, then the rank was added to the label (product fix) |
| C5 | sizes unchanged under « coarse » | `Emulation.setEmulatedMedia` does not flip `pointer`; touch emulation + mobile metrics does |
| C6 | « Ranger » not on screen | a disabled button has `pointer-events: none`; hit-test its bar instead |
| A10b, A12…A16 | cascade | the search box was empty after « Annuler » (F-3) |
| A13 | « 6 incomplete » | the six photos filed by the setup script carry no preview |
| A17 | owner « » | the filed copy is named `IMG_0423.jpg` |
| D8 | « Mot de passe » matched two fields; one-time password | `exact: true`; change the password, then sign in again |

# QA run 3 — Camera photo import

**Date:** 2026-11-16
**Cycle:** 2 of at most 3 (re-run after the fixes of run 2 and of the code review)
**Plan:** features/camera-photo-import/qa/plan.md (46 scenarios + B15)
**Drivers:** `walk.mjs` (one headless launch, then a second drive after probe fixes), `scale.mjs` (C8), `console.mjs` (D8)
**Environment:** isolated — API :5099 rebuilt from the worktree (`bin/Debug`, DB `clinic_camera_sd` restored from its
base dump before each full drive, bucket `camera-demo-5e2c`, `Console__Port=5196`), web :3099 (fresh production build),
console `next dev` :3196. Shared stack, shared database and prod untouched. Peers: none on this stack.
**Widths looked at:** 320 · 390 · 820 · 1180 · 1440 · 1536×730 · 740×380, coarse pointer at 820
**Status:** GREEN — 115 checks pass, 0 product finding. The first drive's 4 reds were all the probe (table below).

## Scenarios (second drive)

| ID | Outcome | Evidence |
|----|---------|----------|
| B1–B3 | ✅ | the three empty states, each with its own sentence; no sideways scroll at 320 |
| A1, D4 | ✅ | review in 7,6 s; « 6 photos déjà rangées »; « plus anciennes » shown and hidden |
| B7, C6 | ✅ | « Ranger » blocked while groups ask, « Voir »; bar on screen and uncovered at 1536×730 and 390×844 |
| C5 | ✅ | coarse pointer at 820: burst cards, viewer and new-patient form ≥ 44 px |
| A2–A9, B8, B10, C9 | ✅ | every mistake case decided as planned; « Leila » finds « Leïla Trabelsi » |
| A3, B9, C2, C3 | ✅ | viewer: photo painted, « Écarter », ←/→/Escape, focus in and back; footer on screen at 320, 390, 1536×730; 740×380 |
| B15 | ✅ | « Autre carte » over decisions asks « Abandonner le tri de … ? »; « Continuer le tri » keeps every choice (run 2 F-1) |
| B5, A6 | ✅ | a choice and a cut survive a recount |
| A10, B11, C4 | ✅ | « Nouveau patient « Sonia Mejri » » created once and chosen; « Annuler » keeps the typed name (run 2 F-3); no sideways scroll |
| B12 | ✅ | « Créer quand même un dossier pour Rim Saidi ? » with the server's reason; « Choisir un patient existant » → back to the search, name kept; no second Rim Saidi |
| A11, C1 | ✅ | no-date photo; review with no sideways scroll at 320 · 390 · 820 · 1180 · 1440 |
| A12–A14, B13 | ✅ | 40 filed after 2 cut uploads + « Réessayer »; each with capture time, the camera's own model, a preview; no key twice; same card → « 46 déjà rangées »; a repeated key → 400 with its code |
| A15, B14 | ✅ | new camera: every group asks whose it is, no patient offered, the bar asks for the camera (run 2 F-2); « Oui, confirmer » lifts it everywhere |
| A16, B6 (D2) | ✅ | « 1 h de retard » after picking Dr Trabelsi; 12 filed; « Annuler la correction » / redo |
| B4, C7 | ✅ | all filed → its empty state; reset clock → the hint names the camera's date (run 2 F-4) |
| D1, A17 | ✅ | « Changer de patient » offers no « Nouveau patient »; IMG_0423 moved to Sarra Ferchichi, into her « Photographie » |
| D5 | ✅ | both cameras listed with their agenda; no sideways scroll at 390 |
| D6 | ✅ | camera photo « Prise le … à 12:14 »; DICOM keeps « Visionneuse », HEIC « Pleine résolution » (re-driven alone after the probe fix) |
| D7, D9 | ✅ | quota stated, over it refused in French with nothing stored; the mirror's manifest lists hosted files only |
| C8 | ✅ | 399 full-size photos: read in 2,9 s, 7 groups all sure, filed in 154 s (run 2: 263 s), 0 failure, heap ≤ 18 Mo, Chrome ≤ 1,5 Go |
| D8 | ✅ | console: « Stockage » column, storage sort, the quota waits for a motif, stored, journaled |
| D10, D11 | ⏭ | the Windows app (and its « N sur M » copy count) needs a real PC with a camera — owed with the pilot |

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| C5 | the viewer's close « × » 42×42 | measured during the dialog's zoom-in from 95 % (44 × 0,95); measured after the animation now |
| B12 | « no duplicate question » | the shared dialog reads « Créer quand même un dossier pour … ? » (screenshot), and its way out is « Choisir un patient existant », back to the search by design |
| D5 | « Nikon not listed » | the camera writes « NIKON D5600 »; the regex was case-sensitive |
| D6 | click timeouts, missing buttons | a file card is `role="button"`, not a `<button>`; the walk reloaded 3 s into the HEIC's 11 s preview build, aborting its upload; the buttons' names are « … visionneuse DICOM » / « … pleine résolution » in lower case |

`walk.mjs` also gained `QA_ONLY` (re-drive named rows after a probe fix) — never the gate.

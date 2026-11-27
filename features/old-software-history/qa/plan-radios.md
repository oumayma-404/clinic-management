# QA plan — the radios/photos pass, quick and unattended (branch `feature/legacy-import-radios`)

Asked 27/11/2026 after TDental's secretary analysed her radios folder and met « a lot of folders à vérifier ». Prod
showed she had only analysed: no image was sent, and « Importer » stays disabled while one folder is undecided.
Local only, the isolated stack of `plan.md` (API :5095, web :3095, DB `clinic_pr123_qa` fresh), fake data.

## What changed

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `match.ts` — a folder name is read for its name only (numbers, years, « radio »… dropped; « Med » = Mohamed); names prepared once, word comparisons cached | `plan.ts` only (`matchFolder`); `nameScore` has no other caller | must re-test — R1, and every plan row of `plan.md` / `plan-unattended.md` (A3-plan, E1-plan, G1-plan, G2) |
| 2 | `plan.ts` — matching counted in the progress and yields every 25 folders; `destinationLabels`; `rowIsComplete` | `import-screen.tsx` | must re-test — R1, R4, E4, A6 |
| 3 | `import-screen.tsx` — « Importer » no longer waits for undecided folders; end/start sentences name them; a done row with files added since runs again; radios-only progress counts cabinet patients; cabinet rows memoised | the import page only | must re-test — R2–R4, R6, C4, E1-count, E4 |
| 4 | `folder-choice-card.tsx` — stable `roots`/`labelOf` props instead of the plan | `import-screen.tsx` only | must re-test — R5, C4 |

Simulation before the browser (`scratchpad/sim`, 1 045-patient cabinet, one folder each): « NOM Prénom 2023 »
went from 12 sure / 1 033 to confirm → 749 / 296 (296 = the simulation's real homonyms); « Med » 709 → 749;
adversarial names: 0 wrong filings before and after; matching 12,4 s → ~2 s per 1 045 folders.

## Scenarios (`walk-radios.mjs`, run FIRST on the fresh database)

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| R0 | A | api | Arrangement: 415 patients through the API as the import account — 395 distinct names, 10 « Mohamed … », 5 homonym pairs (other birth date); notes « dossier n° N/2023 » | 415 created |
| R1-plan | A | browser | Radios folder only (417 folders, 2 JPEG each: 395 in 5 naming styles incl. « NOM Prénom 2023 », « N - Prénom Nom », « … radio »; 10 « Med … »; 10 homonym folders; 1 second folder of a patient, misspelt; 1 folder of nobody) → Analyser | done in < 30 s; every folder filed on its own is on its own patient; the 10 homonym folders and the misspelt one ask, and only one-letter neighbours (« Amel » / « Kamel ») may also ask; 1 not imported; page < 60 000 elements |
| R2 | A | browser | Before « Importer » | button enabled; footer « N dossiers à confirmer : ils attendront votre choix, le reste peut être importé. » (N from R1) |
| R3 | A | browser+sql | Importer | footer counted « Import en cours : N sur <filed> patients »; ends « Import terminé. N dossiers attendent votre choix : choisissez, puis relancez. »; 2 files per filed folder, on the right patients, nothing twice, none on the 11 waiting |
| R4 | B | browser+sql | Confirm the misspelt second folder (its patient already got 2 files) and one homonym folder (by its birth date) → « Relancer l'import » | only those 2 patients are asked (2 folder reads); the first now holds 4 files (2 new, 2 « déjà là »), the homonym 2; nothing twice; footer names N − 2 still waiting |
| R5 | C | browser | Click a candidate on a page of 417 folder cards | the card shows its new destination in < 500 ms |
| R6 | C | sql | Every imported file is on the patient its folder was made for | 0 misfiled |

Regression (tier D): `walk.mjs` → `walk-unattended.mjs` → `walk-b1-large.mjs` → `api-checks.mjs`, unchanged, after R.

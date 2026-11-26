# QA plan — the import runs on its own (branch `feature/legacy-import-unattended`)

Asked 26/11/2026 while TDental imported ~1 000 dossiers on prod: « I cannot stay here and monitor the migration, I need
the script to go well on its own, and make it speed up a lot … without damaging the other clinics ». Local only, the
same isolated stack as `plan.md` (API :5095, web :3095, DB `clinic_pr123_qa` fresh, bucket `pr123-qa`, fake data).

## What changed

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `run.ts` — retries on a lost call (3 s → 60 s), a row gives up only when the problem is its own, the run stops only after 30 min with no answer | the import page only | must re-test — E1, E4 + every row of `plan.md` |
| 2 | `run.ts` — 6 patients at once, 3 uploads at once, same-name records in one lane, no reads for a patient just created | the import page only | must re-test — E1, E3, A3/A5/A6, C3 |
| 3 | `run.ts` — a refused field → created again without it, the field in the notes | the import page only | must re-test — E2 |
| 4 | `run.ts` — file read once from disk, thumbnail capped at 15 s | the import page only | must re-test — E5, D3 |
| 5 | `import-screen.tsx` — painted twice a second, memoised cards, « N sur M » counter, « Connexion perdue » pause, wake lock, rows done skipped on « Relancer » | the import page only | must re-test — E1, E3, E4, C4 |
| 6 | `plan.ts` — 4 PDFs read at once | the plan step | must re-test — A3-plan (same figures), E1-plan |
| 7 | `ClientRequestFaultMiddleware` (API) — a request Kestrel cannot read keeps its 408/413/400 | every `/api` request | unit-tested (`ClientRequestFaultMiddlewareTests`); regression = every API row of `plan.md` still answers as before |

## Scenarios (new — `walk-unattended.mjs`)

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| E1-plan | A | browser | Import account picks batch E (40 dossiers + 80 radios) | plan: 40 à créer, 120 fichiers |
| E1 | A | browser+sql | Import batch E while the browser loses calls: every 3rd « create patient » arrives but its answer is lost, every 5th never leaves, every 4th upload arrives and its answer is lost, every 5th history POST never leaves | ends « Import terminé. », no failed row; footer showed « Connexion perdue : nouvel essai dans N s » and « Import en cours : N sur 40 » |
| E1-sql | A | sql | After E1 | exactly 40 patients of batch E, **no one twice**; exactly 120 files, **no file twice**; history lines **none twice** |
| E2 | B | browser+sql | Before E1, one card's birth date is set after its « Dossier créé le » (the server refuses that date) | that patient is created; its card says « Créé sans un champ refusé … »; its notes hold « Dossier créé le (ancien logiciel) » |
| E3 | A | browser | Speed: batch E's wall time, against run-2's 195 s for batch A (same size, no faults) | well under 195 s even with the faults |
| E4 | B | browser | « Relancer l'import » right after E1 | ends « Import terminé. » in seconds, with **no** patient/file/history call for the 40 done rows |
| E5 | B | browser+sql | Batch F (3 dossiers + radios); one radio is deleted from disk after the folder was picked, before « Importer » | that row: failed, naming the file (« n'a pas pu être lu sur ce PC »); the run goes on; the 2 others done; their files sent |

## Regression

`walk.mjs` (every row of `plan.md`: A1–A6, B1–B4, C1–C4, D1–D3) on the same fresh database, run before
`walk-unattended.mjs`. `api-checks.mjs` / `boost-dates.mjs` test the API limiter, which this change does not touch
(blast radius row 7 is a new middleware below it, unit-tested) — not re-run.

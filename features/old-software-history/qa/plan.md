# QA plan — PR #123: import account's raised rate limit + import page pacing

Branch `feature/legacy-import-v2` @ `ec242a50`. Local only, an isolated stack — never prod, never the shared DB.

## Environment

- API built from the worktree, run from the scratchpad on **:5095**, DB **`clinic_pr123_qa`** (fresh, migrated at
  startup), MinIO bucket **`pr123-qa`**, own key ring, `Console__Port=0`, `Diagnostics__SlowRequestMs=0`.
- Limits shrunk so the difference is measurable: `RateLimiting:Api:PermitLimit=120` (ordinary, per address, 60 s),
  boost `import@pr123.test` → **600/min**, `Until` = start + 3 days.
- Web: `next dev` on **:3095** from the worktree, `NEXT_PUBLIC_API_URL=http://localhost:5095/api`.
- Accounts: `admin@pr123.test` (cabinet admin) · `import@pr123.test` (secretary, boosted) · `collegue@pr123.test`
  (secretary, not boosted).
- Fake exports (no real patient data), in the old software's PDF form: batch **A** 40 dossiers + 40 radio folders
  (2 images each), batch **B** 15 + 15, batch **C** 6 + 6 with ~400 Ko images.

## Blast radius (the change)

| # | Touching | Other consumers | Verdict |
|---|---|---|---|
| 1 | `RateLimiting` global partitioner (new boost branch) | every `/api` request | must re-test — ordinary accounts still limited (D1), `/health` exempt (D2) |
| 2 | `GET /api/meta/rate-boost` (new) | the import page | must re-test — A1 |
| 3 | `run.ts` Pacer / allowDuplicate / storage full | the import page only | must re-test — A3–A6, B1, C1–C3 |
| 4 | `plan-patient-card.tsx`, `import-screen.tsx` | the import page only | must re-test — C4 |
| 5 | compose `RATE_BOOST_*` | hosted deploy only | unaffected — empty by default = off |

## Scenarios

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| A1 | A | api | `GET /meta/rate-boost` as import / colleague | import: `permitPerMinute 600` + `until`; colleague: both null |
| A2 | A | api | API startup log | « Rate-limit boost ON for import@pr123.test: 600 requests per 60s until … » |
| A3 | A | browser | Import account imports batch A (40 + 80 images) | plan reads 40 to create, 120 files; run ends « Import terminé. », 0 failed rows, never « Pause de » |
| A4 | A | api+log | While A3 runs, colleague calls the API ~1/s | every colleague call 200, no 429 at all in the API log during A3 |
| A5 | A | sql | After A3 | 40 patients, 120 files in « Ancien logiciel », history lines present, `CreatedAt` = old « Dossier créé le » |
| A6 | A | browser+sql | « Relancer l'import » on batch A | completes, counts unchanged (nothing twice) |
| B1 | B | browser+log | Colleague (no boost) imports batch B at 300/min over a 120/min limit | « Pause de N s … L'import reprend tout seul. » shown at least once; ends « Import terminé. », 0 failed rows; 429s in the log |
| B2 | B | api | Forged token (import e-mail, bad signature) fired 150× | ≤ 120 accepted, rest 429 — stays in the address bucket |
| B3 | B | api+log | A second API instance with `Until` 8 days away | log « Rate-limit boost NOT applied … more than 7 days away »; rate-boost answers null |
| B4 | B | api | A second API instance with `Until` = start + ~90 s | rate-boost 600 before, null after `Until`, no restart |
| C1 | C | browser | Batch C with the cabinet's quota set just above what is used (SQL arrangement on the throwaway DB) | plan warns « L'espace du cabinet ne suffit pas … »; run ends with « … les fichiers restants attendent … »; cards say « N en attente d'espace »; all 6 patients + history created |
| C2 | C | browser+sql | Raise the quota, « Relancer l'import » | only the waiting files are sent; no file or patient twice |
| C3 | C | sql | Batch A holds a mother and daughter on one phone | both created, neither refused as a duplicate |
| C4 | C | browser | Import page at 320 / 390 / 820 / 1180 / 1440 and 1536 × 730 (plan, running, done) | no page overflow, footer status readable, buttons reachable |
| D1 | D | api | Colleague fires 150 calls in a burst | ~120 accepted then 429 with « Trop de tentatives … » |
| D2 | D | api | `/health` during D1 | 200 |
| D3 | D | browser | An imported patient: « Ancien logiciel » tab and Fichiers | history lines listed; folder « Ancien logiciel » holds PDF + 2 images |

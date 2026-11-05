# QA run 3 — Pointage sur la borne du cabinet (after the review fixes)

**Date:** 2026-11-05
**Cycle:** re-run after `/apply-review-fixes` (54 of 57 findings applied)
**Plan:** features/pointage-borne/qa/plan.md (34 scenarios) + 13 review rows added to `walk.mjs` (R-keep, R-overlap,
R-month, R-mine, R-stray, R-lock, R-stale, R-rename, A12 motif) — no row removed
**Driver:** features/pointage-borne/qa/walk.mjs, full drive, own artefacts reset first
**Environment:** isolated — api :5096 (restarted on the fixed backend, mine) · web :3096 `next dev` (mine) · DB
`clinic_staffleave_qa` · Chrome 9333 (admin + borne tab) / 9334 (secretary)
**Peer sessions live during the run:** none on these ports/DB
**Status:** GREEN — three consecutive full drives (runs 8–10), see the table at the end

## What the drives found, and what each turned out to be

### 1 · Real defect, found by R-lock, fixed — the 5-try lock still counted one of N parallel guesses
- **Symptom:** three parallel wrong schémas → `[409, 409, 400 (4 left)]`, account at 1 failure.
- **Diagnosis (Postgres statement log on this DB):** all three took `SELECT … FOR UPDATE`, the winner saved, the two
  losers then sent `UPDATE "Users" … WHERE xmin = 2583318` — the version from *before* the winner — and matched 0 rows.
  They had read Ines before the lock: `IClinicRepository.GetByIdAsync` includes the clinic's users, and the clinic
  was read before the transaction, so EF kept her stale copy and the later read returned it.
- **Fix:** `PunchCommandHandler` takes the row lock before reading the clinic. Recorded in
  `api/ClinicManagement.Application/CLAUDE.md`.
- **Proof:** 3 parallel → tries left 4/3/2, account at 3; 5 parallel → 4/3/2/1 then 423 locked.

### 2 · Probe defect, A11 — « 08:30 » saved as 08:00 in two drives (runs 4, 5)
- **Symptom:** the line added through « Ajouter un pointage » was stored at 08:00, the day and motif correct.
- **Diagnosis:** a passive focus/input trace in the drive showed focus jumping back to the person picker
  (`focusin record-person`) ~70 ms after the option was chosen — Radix Select's normal focus return. The walk typed
  the time within that window; when the jump landed between Playwright focusing the time field and inserting the
  text, the keys went to the picker and the field kept 08:00.
- **Proof:** forcing that focus move before typing loses the time 2/2; without it 08:30 is saved 2/2. A drive with
  tiny pauses between fields passed 121/121. A person cannot type into another field within 70 ms of choosing an
  option, so no user path reaches it.
- **Fix (probe only):** `pickPerson()` waits for the picker to hand focus back before the next field. No product change.

## Review rows (all ✅ in runs 8–10)

| ID | Checks |
|----|--------|
| A12 | the motif « Parti à 17 h » reads on the line, no hover needed |
| R-keep | « Ajouter un pointage » keeps typed hours and motif through a « team » broadcast |
| R-overlap | an overlapping line is refused by name, dialog stays open, nothing written |
| R-month | a failed « Mois précédent » says so, hides November, disables « Exporter »; « Réessayer » loads October |
| R-mine | Mon profil shows « Réessayer » on a failed read instead of nothing; the card comes back |
| R-stray | one dot at the borne → « Reliez au moins 4 points. », no try spent |
| R-lock | three parallel wrong schémas each counted (4/3/2 left), account at 3 |
| R-stale | a stale « Départ » tile is refused (« Votre pointage a changé… »), nothing written, back to the tiles |
| R-rename | « Renommer cet appareil » retires the previous borne (one active), one « cet appareil », borne still works |
| B5 | tile reads « Bloqué · réessayez dans 15 min » (AC-7), disabled |

## Drives

| Run | Result | Note |
|-----|--------|------|
| 3 | 119 ✅ · 2 ❌ | R-lock — real defect 1 |
| 4, 5 | 118 ✅ · 3 ❌ | A11 + its two readers (A12, A16) — probe defect 2 |
| 6, 7 | 121 ✅ · 0 ❌ | instrumented copies (diagnosis) |
| 8 | 121 ✅ · 0 ❌ | fixed walk |
| 9 | 121 ✅ · 0 ❌ | fixed walk |
| 10 | 121 ✅ · 0 ❌ | fixed walk |

Skipped in every drive, answered elsewhere: B9 (curl: 401 `terminal_unknown`), D5 (unit suite 6605 / 0).

# QA run 2 — cabinet de groupe

**Date:** 2026-11-16 · **Cycle:** 2 of 3 (re-run after the cycle-1 fix)
**Environment:** the same isolated stack as run 1 — API :5099 (worktree build, Debug) on `cdg_qa`, MinIO bucket
`cdg-qa-throwaway`, main key ring, web `next dev` :3099 (worktree, hot-reloaded with the fix), playwright-core Chrome
(headless, own profile). Login: Salma Ben Youssef (admin + dentist). Peers live: anakin-6e (idle), anakin-22 (idle),
clinic-management-1c (idle), khaireddine-website-f3 (busy, another repo) — none on these ports or this database.
**Arrange (on the copy only, stamped `80988` so nothing meets run 1's records):** practitioner « Rania
Ferchichi80988 » inserted with no account (SQL); an admin « QA Admin Sans Fiche 80988 » through `POST /users`; a
block, an RDV for Salma and one for Nadia on 2026-11-17 through `POST /appointments`.
**Scripts:** `api-rows.mjs` (arrange + the api/sql rows) then `walk.mjs` (every browser row, no filter) — the same
files as run 1, with A9/A10 reading the stamped records from `arrange.json`.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | 400 `practitioner_required`, the French sentence |
| A2 | ✅ | **fixed** — « SB » ×2 and « NT » ×2 drawn on the 30-min blocks at 1440 — `shots/A2-week-1440.png` |
| A3 | ✅ | legend « Praticien » lists the dentists — `shots/A3-legend.png` |
| A4 | ✅ | first visit opens on « Salma Ben Youssef », chip present — `shots/A4-first-visit.png` |
| A5 | ✅ | « Tous les praticiens » kept after reload |
| A6 | ✅ | filtered on Nadia: the cabinet-wide block shown (4 blocks), no Salma RDV — `shots/A6-filtered-nadia.png` |
| A7 | ✅ | « Nouveau » while filtered on Nadia opens on « Nadia Trabelsi » |
| A8 | ✅ | admin invited with « C'est aussi un praticien »: record linked, row says so — `shots/A8-invite-admin-dentist.png` |
| A9 | ✅ | « Rania Ferchichi80988 — déjà dans Paramètres » pre-picked; sql: one record, linked — `shots/A9-invite-existing.png` |
| A10 | ✅ | « Lier à un praticien » → linked; row « Praticien : … » — `shots/A10-link-dialog.png` |
| A11 | ✅ | `#closure-doctor` present (« Tous les praticiens »), list re-read for Nadia — `shots/A11-a-cloturer.png` |
| A12 | ✅ | sql: review row TargetUserId = Nadia's account, AlsoTargetUserId = Salma's |
| B1 | ✅ | « Tout le cabinet » saved with DoctorId NULL (sql) |
| B2 | ✅ | no « Aucun praticien »; save refused with the sentence — `shots/B2-edit-unassigned.png` |
| B3 | ✅ | PUT time only → 200, doctorId still null |
| B4 | ✅ | « Envoyer l'invitation » disabled until the practitioner fields are filled |
| B5 | ✅ | 400 `practitioner_already_linked` |
| C1 | ✅ | 390 and 320: no sideways scroll of `<main>`; phone panel offers « Praticien » — `shots/C1-agenda-320.png`, `shots/C1-agenda-390.png`, `shots/C1-phone-panel-390.png` |
| C2 | ✅ | dialog at 1536×730: footer at y = 617 — `shots/C2-dialog-1536x730.png` |
| C3 | ✅ | 820: no sideways scroll, chips drawn — `shots/C3-agenda-820.png` |
| D1 | ✅ | `?patientId=` opens on « Salma Ben Youssef » |
| D2 | ✅ | the ordonnance names the signed-in dentist — `shots/D2-prescription.png` |
| D3 | ✅ | patient page renders, no failure notice |
| D4 | ✅ | strict filter: 2 rows, all Nadia's; `includeUnassigned`: the block + Nadia's, not Salma's |

Widths looked at: 1440×900 · 1536×730 · 820×1180 · 390×844 · 320×844.

## Findings

None.

## Observations (off-plan)

- The 2026-11-17 column shows each RDV twice (run 1's and run 2's arranged records, same times). Test data, not product.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F1 | confirmed | the chip lived only on the block's second line, which is withheld under 30 px — and a 30-min RDV (the default) is 24 px in the desktop week | `web/components/appointment-calendar.tsx` (the very-small block draws the chip on its name row; `renderDoctorChip` takes a class) | 1 consumer (the desktop week/day block); the phone line and the month chip already carried it |

**Guards added:** none — a layout question, held by A2 in this plan.
**Gates after the last edit:** check:responsive 88/88 ✅ · tsc ✅ · build ✅ (run after the dev server stopped) ·
dotnet test not re-run (the fix touched `web/` only; last full run 7 258 ✅, 0 ❌, 6 skipped).

**Status: GREEN (24/24, 0 ⏭)**

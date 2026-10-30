# QA run 1b — Retard et report (regression rows added after the blast-radius review)

**Date:** 2026-10-30 (09:15–09:45 Tunis)
**Cycle:** still 1 — no source changed since run 1; these are tier-D rows the first table missed (plan.md D5–D8)
**Driver:** walk.mjs with `ONLY=D5,D6,D7` · e2e `npx playwright test` (4 specs)
**Environment:** isolated — API :5098 (worktree `bin/Debug`) · web :3098 **standalone production build** · postgres `clinic_pvl_qa` (fresh copy of `clinic_conges_qa`) · rate limits raised for the e2e run
**Status:** GREEN

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| D5 | ✅ | coarse pointer — 1024: Venu · Absent · ⋯ end at 987 px inside a 999 px table box · 1180: 1143 inside 1155 · no sideways scroll · ⋯ is 44 px |
| D6 | ✅ | agenda ⋯ on an Absent block offers « Terminé »: free slot → `Completed`; slot given away → toast « Ce créneau est déjà réservé pour ce praticien (29/10 20:00–21:00). », statut stays `NoShow` |
| D7 | ✅ | user picks 20, colleague saves 30 → 409 → « Recharger » → 30 shown, « Modifié entre-temps par quelqu'un d'autre : « Statut » … » → save lands · shots/run1b-D7-recharger-merge.png |
| D8 | ✅ | e2e `appointment`, `concurrency`, `booking-browser`, `carried-draft-browser`: **18 passed, 1 skipped** — EDIT-05 skips by its own guard (it reads the visit right after creating it, before the minute job runs; `CreateAppointmentCommand` never advances a statut, here or on `main`). Its observable half — a form opened on a visit the job already advanced still saves — passed in run 1 on R2, R6 and R7 |

## Findings

None.

## Not covered, and why

| Area | Why not |
|------|---------|
| PC de secours copy (#18) | needs a paired PC; the cloud ships the PC the same build, and the row is a plain nullable column |

## Probe bugs fixed during the run

| Row | Symptom | Real cause |
|-----|---------|------------|
| setup | `slot_taken` booking R13 | a 09:07–10:07 live visit met the dev data's 10:00 booking; R13 now books with the overlap override |
| setup | « today 09:00 » in the future | the date rolled over; past visits are now booked yesterday so the walk holds at any hour |

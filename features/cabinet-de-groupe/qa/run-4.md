# QA run 4 — cabinet de groupe

**Date:** 2026-11-17 · **Why:** the owner asked to fix run 3's off-plan observation (the edit dialog read « Aucun
praticien » at a one-dentist cabinet while the save named the dentist). Whole plan re-run, O5 tightened, O6 added.
**Environment:** isolated, rebuilt — fresh worktree of `feature/cabinet-de-groupe` (a4d4f100 + the fix), API :5099
(Debug) on a new `cdg_qa` copy (migration on the copy only, checked), bucket `cdg-qa-throwaway`, web `next dev` :3099
(fresh), playwright-core Chrome headless, two fresh profiles (Salma, then the secretary Ines Gharbi). Peers: none on
these ports or this database.

## The fix

`web/components/edit-appointment-dialog.tsx` — a patient RDV with no dentist shows, and saves, the dentist the server
would name (`initialDoctorId(activeDoctors)`: the only active one, else none). **Derived on render, never written into
the form state**, so opening a visit does not make it dirty: the merge snapshot and the carried draft keep the raw
value. « Aucun praticien » is offered only for a « créneau occupé » and for a cabinet with no dentist. The récap pane
names the same dentist. Blast radius row 15.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| A1–A12 | ✅ | same as run 3 |
| B1–B5 | ✅ | B2: no « Aucun praticien » with two dentists, save refused with the sentence (unchanged by the fix) |
| C1–C3 | ✅ | 320 / 390 / 820 / 1536×730 |
| D1, D2, D4 | ✅ | |
| D3 | ✅ | patient page renders, no failure notice — **re-driven**: the first drive's `goto` hit its 30 s limit because a fresh `next dev` compiled `/patients/[id]` in **42 s** (dev log: `next.js: 33.4s`); second load 1.9 s. Environment, not product |
| S1–S6 | ✅ | secretary, prompt to the booker, promotion, month view, waiting list |
| O1–O4 | ✅ | one dentist: auto-named (API + screen), no initials, no filter |
| **O5** | ✅ | one dentist: the old RDV opens on « Salma Ben Youssef - Médecin dentiste », « Aucun praticien » not offered, saved with Salma — `shots/O5-one-dentist-old-rdv.png` (field and récap both name her) |
| **O6** | ✅ | one dentist: the cabinet-wide block still reads « Aucun praticien » and offers it — `shots/O6-one-dentist-blocked-slot.png` |

Widths looked at: 1440×900 · 1536×730 · 820×1180 · 390×844 · 320×844.

## Findings

None.

**Gates after the last edit:** check:responsive 88/88 ✅ · tsc ✅ · build ✅ · `dotnet test` not re-run (no `api/`
change this round; last full run 7 261 ✅).

**Status: GREEN (36/36, 0 ⏭)**

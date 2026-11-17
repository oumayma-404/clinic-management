# QA run 3 — cabinet de groupe

**Date:** 2026-11-17 · **Cycle:** 3 of 3 — the whole plan re-run plus 11 new rows (S1–S6, O1–O5) for the people the
change is for: the secretary, and a cabinet with one dentist.
**Environment:** isolated, rebuilt from scratch — a fresh worktree of `feature/cabinet-de-groupe` (a8e4bb2a + the
new tests), API :5099 (Debug) on a new `cdg_qa` copy of the dev DB (the migration landed on the copy only — checked:
`AlsoTargetUserId` exists in `cdg_qa`, not in `clinic_management`), MinIO bucket `cdg-qa-throwaway`, web `next dev`
:3099, playwright-core Chrome (headless, two fresh profiles: Salma, then the secretary). Peers live: anakin-6e,
anakin-22, clinic-management-1c (idle), khaireddine-website-f3 (busy, another repo) — none on these ports or this DB.
**Logins:** Salma Ben Youssef (admin + dentist) for A–D and S4–S6; **Ines Gharbi**, secretary
(`qa.secretary@ibnkhaldoun.test`, no dentist record) for S1–S3 and O1–O5.
**One-dentist phase:** after S2, every practitioner of the cabinet but Salma was set inactive on the copy (6 rows,
SQL) — the shape of every cabinet on prod today.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | 400 `practitioner_required` |
| A2 | ✅ | « SB » / « NT » on the 30-min blocks at 1440 |
| A3 | ✅ | legend « Praticien » lists the dentists |
| A4 | ✅ | first visit on « Salma Ben Youssef », chip present |
| A5 | ✅ | « Tous les praticiens » kept after reload |
| A6 | ✅ | filtered on Nadia: the cabinet-wide block shown, no Salma RDV |
| A7 | ✅ | « Nouveau » while filtered on Nadia opens on Nadia |
| A8 | ✅ | admin invited as a dentist: record linked, row says so |
| A9 | ✅ | existing roster record pre-picked; one record, linked |
| A10 | ✅ | « Lier à un praticien » → linked, row « Praticien : … » |
| A11 | ✅ | `#closure-doctor` present, list re-read for Nadia |
| A12 | ✅ | sql: review row target = Nadia's account, also = Salma's |
| B1 | ✅ | « Tout le cabinet » saved with DoctorId NULL |
| B2 | ✅ | no « Aucun praticien »; save refused with the sentence |
| B3 | ✅ | PUT time only → 200, doctorId still null |
| B4 | ✅ | « Envoyer l'invitation » disabled until the practitioner fields are filled |
| B5 | ✅ | 400 `practitioner_already_linked` |
| C1 | ✅ | 390 / 320: no sideways scroll; phone panel offers « Praticien » |
| C2 | ✅ | dialog at 1536×730: footer at y = 617 |
| C3 | ✅ | 820: no sideways scroll |
| D1 | ✅ | `?patientId=` opens on Salma |
| D2 | ✅ | the ordonnance names the signed-in dentist |
| D3 | ✅ | patient page renders, no failure notice |
| D4 | ✅ | strict filter: Nadia's only; `includeUnassigned`: + the block, not Salma's |
| **S1** | ✅ | the secretary's first visit opens on « Tous les praticiens » |
| **S2** | ✅ | secretary booking: field empty (« Choisir un médecin… »), « Créer » refused with « Choisissez le praticien… », saved for Nadia; sql: review row AlsoTargetUserId = the secretary — `shots/S2-secretary-no-dentist.png` |
| **S3** | ✅ | `pending-reviews`: the secretary sees the visit she booked for Salma and NOT Salma's own; Salma sees both |
| **S4** | ✅ | secretary promoted to « Médecin » → the roster record with her e-mail is linked, no second record; row « Praticien : Yosra … » — `shots/S4-promote-to-medecin.png` |
| **S5** | ✅ | month view: « SB » / « NT » on the entries — `shots/S5-month-1440.png` (re-driven after a probe fix: the view buttons carry a full and a short label, so the text match found nothing; now matched on `aria-label`) |
| **S6** | ✅ | « Promouvoir » on a waiting-list entry preferring Nadia opens the booking on Nadia — `shots/S6-waiting-list-booking.png` |
| **O1** | ✅ | one dentist, secretary, API booking with no `doctorId` → 201, doctorId = Salma |
| **O2** | ✅ | one dentist, secretary: dialog opens on « Salma Ben Youssef »; saved untouched → DoctorId = Salma — `shots/O2-one-dentist-booking.png` |
| **O3** | ✅ | one dentist: no initials on blocks, no « Praticien » section in the legend — `shots/O3-one-dentist-agenda.png` |
| **O4** | ✅ | one dentist: « À clôturer » has no « Praticien » select — `shots/O4-one-dentist-a-cloturer.png` |
| **O5** | ✅ | one dentist: the old RDV with no dentist saves without a refusal and gets Salma — `shots/O5-one-dentist-old-rdv.png` |

Widths looked at: 1440×900 · 1536×730 · 820×1180 · 390×844 · 320×844.

## Findings

None.

## Observations (off-plan)

- **O5 — the edit dialog says « Aucun praticien » where the save will name the only dentist.** At a one-dentist
  cabinet an old RDV with no dentist opens on « Aucun praticien » (the option is still offered there), and
  « Enregistrer » silently stores Salma. The result is right; the field shown before the save is not. The create
  dialog pre-picks the only dentist; the edit dialog does not. Not fixed here (cycle 3, off-plan) — offered to the owner.
- The cabinet header counts every practitioner record (« 8 praticiens » during the two-dentist phase): the copy holds
  the practitioners runs 1–3 created. Test data.

## Also added this cycle

- `ChangeUserRoleCommandHandlerTests`: 3 cases for the promotion — links the one unlinked record with the same
  e-mail; creates a new one when two share it; never takes a record linked to another account.
- **Gates:** `dotnet test` full suite 7 261 ✅, 0 ❌, 6 skipped. No `web/` source changed this cycle.

**Status: GREEN (35/35, 0 ⏭)**

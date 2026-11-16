# QA run 1 — cabinet de groupe

**Date:** 2026-11-16 · **Cycle:** 1 of 3
**Environment:** isolated — API :5099 (worktree build, Debug) on `cdg_qa` (copy of the dev DB; the shared
`clinic_management` was checked to have no `AlsoTargetUserId` column), MinIO bucket `cdg-qa-throwaway`, main key
ring, web `next dev` :3099 (worktree), playwright-core Chrome (headless, own profile). Login: Salma Ben Youssef
(admin + dentist). Peers live: anakin-6e (idle), anakin-22 (idle), clinic-management-1c (idle),
khaireddine-website-f3 (busy, another repo) — none on these ports or this database.
**Arrange (on the copy only):** practitioner « Karim Haddad » inserted with no account (SQL); an admin
« QA Admin Sans Fiche » created through `POST /users`; a block, an RDV for Salma and one for Nadia tomorrow through
`POST /appointments`.

## Scenarios

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | 400 `practitioner_required`, the French sentence |
| A2 | ❌ | labels « avec Salma Ben Youssef » / « avec Nadia Trabelsi » present, but **no chip drawn** on either 30-min block at 1440 — `shots/A2-week-1440.png` |
| A3 | ✅ | « Praticien » section lists SB · KH · NT (re-driven after a probe fix: heading is CSS-uppercased) |
| A4 | ✅ | first visit opens on « Salma Ben Youssef », chip « Praticien : Salma Ben Youssef » — `shots/A4-first-visit.png` |
| A5 | ✅ | « Tous les praticiens » kept after reload |
| A6 | ✅ | filtered on Nadia: the cabinet-wide « Occupé » block shown, no Salma RDV — `shots/A6-filtered-nadia.png` |
| A7 | ✅ | « Nouveau » while filtered on Nadia opens on « Nadia Trabelsi » |
| A8 | ✅ | admin invited with « C'est aussi un praticien »: one linked record (sql), row « Praticien : Hela Mansour… » — `shots/A8-invite-admin-dentist.png` |
| A9 | ✅ | « Karim Haddad — déjà dans Paramètres » pre-picked, name fields hidden; sql: 1 record, linked — `shots/A9-invite-existing.png` |
| A10 | ✅ | « Lier à un praticien » → toast « Fiche praticien liée. », row « Praticien : Sami Ben Ali… » — `shots/A10-link-dialog.png` |
| A11 | ✅ | `#closure-doctor` present; switching sent `to-close?…doctorId=<Nadia>` — `shots/A11-a-cloturer.png` |
| A12 | ✅ | sql: review row TargetUserId = Nadia's account, AlsoTargetUserId = Salma's |
| B1 | ✅ | « Tout le cabinet » offered; saved block has DoctorId NULL (sql) |
| B2 | ✅ | no « Aucun praticien »; « Enregistrer » shows « Choisissez le praticien de ce rendez-vous… »; DoctorId still NULL — `shots/B2-edit-unassigned.png` |
| B3 | ✅ | PUT time only → 200, doctorId still null |
| B4 | ✅ | « Envoyer l'invitation » disabled with empty practitioner fields |
| B5 | ✅ | 400 `practitioner_already_linked` |
| C1 | ✅ | 390 and 320: no sideways scroll of `<main>`; phone panel shows « Praticien » (re-driven after a probe fix: the first click hit « Aide ») — `shots/C1-agenda-320.png`, `shots/C1-phone-panel-390.png` |
| C2 | ✅ | dialog at 1536×730: « Créer le rendez-vous » at y = 617 — `shots/C2-dialog-1536x730.png` |
| C3 | ✅ | 820: no sideways scroll; chips « SB » / « NT » drawn — `shots/C3-agenda-820.png` |
| D1 | ✅ | `?patientId=` opens on « Salma Ben Youssef » |
| D2 | ✅ | the ordonnance names Salma (re-driven after a probe fix: there is no prescriber dropdown) — `shots/D2-prescription.png` |
| D3 | ✅ | patient page renders, no failure notice |
| D4 | ✅ | strict filter: 1 row, all Nadia's; `includeUnassigned`: the block + Nadia's, not Salma's (re-driven after a probe fix: day bounds) |

Widths looked at: 1440×900 · 1536×730 · 820×1180 · 390×844 · 320×844.

## Findings

### F1 — major — the dentist's initials are missing on 30-minute blocks in the desktop week
- **Repro:** 1440 px, Semaine, two dentists, an RDV of 30 min.
- **Expected:** « SB » / « NT » on the block.
- **Observed:** only the patient's name; the block is 24 px tall and the chip sits on the second line, which is
  withheld below 30 px. Only the accessible name carries the dentist. 30 minutes is the default booking.
- **Evidence:** `shots/A2-week-1440.png` (missing) vs `shots/C3-agenda-820.png` (taller rows, chips present).
- **Owner:** `web/components/appointment-calendar.tsx` — desktop block, the name row (~line 1990) has no chip;
  the chip is only in the `!isVerySmall` second line.

## Observations (off-plan)
- The re-run before the probe fix re-executed A8–A10 against already-used data (A9/B4/A10 timed out); probe noise,
  not product.

**Status: RED (1 finding)**

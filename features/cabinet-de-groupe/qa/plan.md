# QA plan — cabinet de groupe (run 1)

Stack: isolated — API :5099 on `cdg_qa` (a copy of the dev DB, so the new migration never reaches
`clinic_management`), MinIO bucket `cdg-qa-throwaway`, web `next dev` :3099, playwright-core Chrome.
Cabinet: « Ibn Khaldoun » (`f64a8a75…`) — 2 active dentists: **Salma Ben Youssef** (admin, the walk's login) and
**Nadia Trabelsi** (doctor); 676 old patient RDV with no dentist.
The post-visit popup is served empty (`pending-reviews` → `[]`), per `/clinic-browser`.

| ID | Tier | Layer | Steps | Expected (observable) |
|---|---|---|---|---|
| A1 | A | api | POST /appointments with a patient, no `doctorId` | 400, `code = practitioner_required` |
| A2 | A | browser | As Salma: agenda, Semaine, 1440 | blocks of Salma's/Nadia's RDV carry initials « SB » / « NT »; an RDV with no dentist carries none |
| A3 | A | browser | Agenda « Filtres » legend | a « Praticien » section listing both dentists with their chips |
| A4 | A | browser | First visit to /appointments with empty storage | the Praticien filter shows « Salma Ben Youssef » (own RDV); the chip row shows « Praticien : … » |
| A5 | A | browser | Change the filter to « Tous les praticiens », reload /appointments (no query) | still « Tous les praticiens » (remembered) |
| A6 | A | browser | Filter on Nadia; a cabinet-wide « créneau occupé » (created via API, no dentist) in the window | the block is visible while filtered on Nadia |
| A7 | A | browser | « Nouveau » from the agenda while filtered on Nadia | the dialog's Praticien reads « Nadia Trabelsi » |
| A8 | A | browser | Users → « Inviter un collègue » → Administrateur → tick « C'est aussi un praticien » | the practitioner block appears; after sending, the row reads « Praticien : <name> » |
| A9 | A | browser+sql | Add a practitioner « Karim Haddad » with no account (API roster), invite « Médecin » with that e-mail | « Fiche praticien » pre-picks « Karim Haddad — déjà dans Paramètres »; after sending, ONE Karim Haddad record, linked (sql) |
| A10 | A | browser | Row menu of an admin with no practitioner (created via API) → « Lier à un praticien » → new practitioner → Lier | toast « Fiche praticien liée. »; the row reads « Praticien : … » |
| A11 | A | browser | « À clôturer » with 2 dentists | a « Praticien » select beside « Période »; switching to Nadia changes the count |
| A12 | A | sql | A patient RDV created by Salma for Nadia | its post-visit review row: TargetUserId = Nadia's account, AlsoTargetUserId = Salma |
| B1 | B | browser | Nouveau → « Créneau occupé » → Praticien | « Tout le cabinet » offered; choosing it and saving stores DoctorId NULL (sql) |
| B2 | B | browser | Open an old patient RDV with no dentist (edit dialog) | « Aucun praticien » is NOT offered; « Enregistrer » with the field empty shows « Choisissez le praticien de ce rendez-vous… » |
| B3 | B | api | PUT the same RDV with only a new time | 200 — a drag still works |
| B4 | B | browser | Invite « Médecin » with no practitioner fields filled | « Envoyer l'invitation » stays disabled |
| B5 | B | api | POST /users/{id}/practitioner for an account that already has one | 400, `code = practitioner_already_linked` |
| C1 | C | browser | Agenda Semaine at 320 and 390 (phone) | no sideways scroll of `<main>`; phone panel offers « Praticien » |
| C2 | C | browser | Booking dialog at 1536×730 | « Créer » footer on screen; Praticien field reachable |
| C3 | C | browser | Agenda Jour at 820 | chips on blocks, no overflow |
| D1 | D | browser | Patient page → book from it (`/appointments?patientId=`) as Salma | Praticien pre-picked « Salma Ben Youssef » (row 7) |
| D2 | D | browser | /documents/prescription as Salma | the prescriber defaults to Salma (row 12) |
| D3 | D | browser | Patient page loads « Travail non facturé » / RDV tab with no error (row 6) | page renders the patient's name, no error banner |
| D4 | D | api | Export /appointments/export?doctorId=Nadia | contains no unassigned row (strict filter kept, row 5) |

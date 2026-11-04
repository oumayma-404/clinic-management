# QA plan — story 3 (nothing misbehaves around the read-only rule)

**Stack:** isolated — API :5099 `PublicDemo` on a fresh `clinic_demo_local`, web :3099. Fresh Chrome contexts via
`playwright-core`. The cabinet has no patients yet (story 4), so the rows that need one are ⏭ with that reason.

| ID | Tier | Layer | Scenario | Expected (observable) |
|----|------|-------|----------|------------------------|
| A1 | A | browser | Sécurité → « Mes appareils » with a second visitor in | one row, no « Déconnecter »; still one row after the second visitor enters |
| A2 | A | browser | Sécurité « Activer maintenant » | stays on `/securite`, one demo toast |
| A3 | A | browser | `/change-password`, fill, submit | form shown (not sent to `/demo`); in-form demo notice; typed values kept; no request |
| A4 | A | browser | Patients → « Exporter » (security-code export) | dialog shows the demo notice, **no** password field; no step-up request |
| A5 | A | browser | Paramètres → Modifier → logo file picked | one demo toast; no logo upload request |
| A6 | A | browser | Mon profil → cachet file picked | one demo toast; no upload request |
| A7 | A | browser | Patients → Importer → CSV picked | demo notice inside the dialog; no preview request |
| A8 | A | browser | Paramètres → Mode discret « Masquer » | neutral toast, still « Masquer » |
| A9 | A | browser | Tableau de bord → Personnaliser → toggle one block | neutral toast; the switch returns to its stored state |
| A10 | A | browser | Messages → a conversation → type + send | demo toast; text kept in the field; no new bubble |
| B1 | B | browser (network) | every write request across the walk | only `auth/demo-session` · `auth/refresh` · `auth/logout` · `csp-report`, plus the two presses that are meant to reach the server and be refused (Mode discret, dashboard layout). None to: notifications read, staff-chat read/send, push devices, default folders, uploads, step-up, change-password, import |
| B2 | B | browser (network) | post-visit popup withdrawn | no `GET /notifications/pending-reviews` |
| C1 | C | browser | the step-up notice at 390×844 and 1536×730; the password notice at 320×640 | notice and « Fermer » visible, no horizontal scroll |
| D1 | D | browser | story-1 walk (`walk.mjs`) | green |
| D2 | D | browser | story-2 walk (`walk-2.mjs`) | green |
| D3 | D | — | hub presence on the demo | ⏭ covered by `ClinicHubTests.On_The_Demo_A_Visitor_Arriving_Or_Leaving_Signals_Nobody` |
| D4 | D | — | Fichiers default folders, odontogram multi-tooth | ⏭ no patient until story 4 |

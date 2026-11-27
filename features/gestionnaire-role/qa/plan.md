# QA plan — rôle « Gestionnaire »

Stack: worktree API :5095 + web (standalone) :3095, DB `clinic_role_qa` (copy), bucket `role-qa`. Cabinet `pr123`.
Accounts: `admin@pr123.test` (admin, TOTP), `collegue@pr123.test` (secretary), plus a gestionnaire and a doctor
created by the walk itself.

## Owner's decisions (2026-11-27)

Gestionnaire = admin minus: Tableau de bord, Factures, Caisse, Chèques, Journal d'activité, mode discret,
Premiers pas; never creates an admin nor touches an admin's account. Dépenses, créances, corrections (annuler une
note, avoir, remise) allowed. Authenticator required like an admin.

## Scenarios

| ID | Tier | Layer | Steps | Expected |
|---|---|---|---|---|
| A1 | A | browser | admin → Utilisateurs → « Nouveau compte » → Rôle | the list offers « Gestionnaire » |
| A2 | A | api | admin creates `gestion@pr123.test` role `manager`; first sign-in | sign-in demands authenticator enrolment (`totp_enrolment_required`) |
| A3 | A | browser | gestionnaire signs in, 1440 px rail | has Utilisateurs, Médicaments, Paramètres, Abonnement, Sécurité, Stock; NOT Tableau de bord, Factures, Caisse, Chèques, Journal d'activité; rail shows « Gestionnaire » |
| A4 | A | browser | gestionnaire opens `/` | lands on the agenda / salle d'attente, not the dashboard |
| A5 | A | browser | gestionnaire opens `/factures`, `/caisse`, `/cheques`, `/journal` | each shows a refusal, no data |
| A6 | A | browser | gestionnaire opens `/settings` | « Modifier » on the cabinet info; no « Mode discret » card |
| A7 | A | browser | gestionnaire opens `/users` | role select on a secretary row has no « Administrateur »; the admin row has no select and no ⋯ menu |
| A8 | A | browser | gestionnaire opens `/medications`, `/procedure-types`, `/securite` | catalogue editable (« Nouveau » present), Sécurité opens |
| B1 | B | api | gestionnaire GET dashboard, caisse, cheques, audit, revenue, invoices export; POST money/hide | 403 each |
| B2 | B | api | gestionnaire GET users, receivables, expenses, team overview, backup history, outbox | 200 each |
| B3 | B | api | gestionnaire: create an admin; change the admin's role; reset the admin's password; switch the admin off; reset the admin's authenticator | refused each, admin unchanged in DB |
| B4 | B | api | gestionnaire creates a secretary, then changes her to gestionnaire | 200, role stored `manager` |
| B5 | B | api | gestionnaire GET /auth/totp | `isRequired: true` |
| C1 | C | browser | gestionnaire at 390 px: drawer | same rows as A3, no horizontal scroll |
| C2 | C | browser | gestionnaire `/users` at 390 px (cards) | role select options without « Administrateur » |
| D1 | D | browser | admin rail | still has Tableau de bord, Factures, Caisse, Chèques, Journal d'activité, Utilisateurs |
| D2 | D | browser | admin `/settings` | « Mode discret » card still there |
| D3 | D | browser | admin `/users` role select | four roles incl. « Administrateur » and « Gestionnaire » |
| D4 | D | browser | secretary rail | no Tableau de bord, Finances, Paramètres, Utilisateurs (unchanged) |
| D5 | D | api | secretary GET users / receivables; doctor GET caisse / users | 403 / 403; 200 / 403 (unchanged) |

# Rôle « Gestionnaire » (`manager`) — what shipped

Owner's call, 2026-11-27: « access to everything but the dashboard and the finance section, can add people,
less than admin, a lot more than the secretary ».

## The rule

| Gestionnaire gets | Gestionnaire does NOT get |
|---|---|
| everything the admin has: Utilisateurs, Paramètres, catalogues, équipe (congés, pointage), sauvegarde / restauration, PC de secours, Abonnement, Nouveautés, Sécurité, delete a patient | Tableau de bord, Factures, Caisse, Chèques (and marking a cheque banked), le chiffre d'affaires, the factures export |
| devis and every correction on them, annuler une note / un paiement, avoir, remise, write-off, dépenses, créances | Journal d'activité, « Mode discret » (on or off), « Premiers pas » |
| create / change / switch off doctors, secretaries, other gestionnaires | create an admin, or change / reset / switch off an admin's account |

- Authenticator **required**, like an admin (hosted). Never locked out by pointage, like an admin.
- Sees the admin-only bell rows (PC de secours, stockage, congés, pointage setup).
- Does not ask for congés himself (still secretary-only), and is not tracked by pointage by default.

## Where it lives

- Server: `User.RoleManager`, `User.RunsTheClinic()` (admin or gestionnaire — the handler twin of the policy).
  Two new policies: `AdminDoctorOrManager` (AdminOrDoctor's old scope minus the money screens) and
  `AdminOrManager` (AdminOnly's old scope minus journal / mode discret / Premiers pas). `AdminOrDoctor` and
  `AdminOnly` now hold only what the gestionnaire is refused.
- `Features/Users/StaffAccountAuthority` — the one rule « may this caller act on this account / grant this role »,
  asked by create, role change, password reset, on/off, invitation resend and authenticator reset.
- `StaffNotificationRepository.AddressedTo` — an `admin`-targeted row also reaches a gestionnaire.
- Web: `runsTheClinic()` in `lib/auth/can.ts`; `MANAGER_HIDDEN_HREFS` in `lib/nav.ts`; `user-management.tsx`
  offers no « Administrateur » and no actions on an admin row to a gestionnaire.

## Guard

`Api/ManagerRoleAccessTests.Endpoints_A_Manager_Does_Not_Reach` derives, from the compiled attributes, every
endpoint a gestionnaire cannot reach and compares it with the owner's list. A new endpoint under `AdminOnly` or
`AdminOrDoctor` fails it until somebody decides whether the gestionnaire gets it.

## Known gaps (not built)

- Dépenses live inside « Caisse », so a gestionnaire is allowed them by the server but has no screen for them.
- « Créances » has no menu entry for anyone (withdrawn screen); allowed by the server only.

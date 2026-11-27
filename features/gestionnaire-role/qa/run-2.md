# Run 2 — GREEN (77 checks, 0 failed, 0 not exercised)

Environment: branch `feature/role-gestionnaire` (worktree `legacy-import-v2`), API :5095 (Debug build), web :3095
(production standalone build), DB `clinic_role_qa` (copy of `clinic_pr123_qa`, migrated to main), bucket `role-qa`.
No peer session live on the shared stack (it was down; Docker started by this session). Widths: 1440×900, 390×844.

| Tier | Rows | Result |
|---|---|---|
| A — gestionnaire happy path | A1–A8 | ✅ rail without dashboard/Finances/Journal, « Gestionnaire » label, `/` → salle d'attente, the 4 money/journal pages refuse, Paramètres editable without Mode discret, Utilisateurs without « Administrateur » and admin row read-only, catalogues/Sécurité/Abonnement open, authenticator enrolment forced on first sign-in |
| B — server | B1–B5 | ✅ 7 refusals (dashboard, caisse, chèques, audit, revenue, factures export, mode discret); 7 reached; 5 admin-account actions refused **by the gestionnaire rule** with the admin row unchanged in the DB; creates a secretary and changes a role; authenticator required |
| C — 390 px | C1–C2 | ✅ no sideways scroll, bottom bar without dashboard/caisse, card role list without « Administrateur » |
| D — regression | D1–D5 | ✅ admin rail, Mode discret and four-role list unchanged; secretary rail unchanged; secretary/doctor server rules unchanged |

## Probe fixes (run 1, same launch — no source change)

| Row | Probe bug | Fix |
|---|---|---|
| B2 | `/team/overview` answered 400 « congés pas activés » — a business answer past authorization | « reached » = not 401/403/5xx |
| session | the TOTP field submits itself at six digits; « Vérifier » was gone | press it only if still on `/login` |
| B3 | the admin authenticator reset was refused by the step-up check, not by the new rule | confirm identity first, and assert the refusal sentence |

Screenshots: `shots/run-1/`, `shots/run-2/`.

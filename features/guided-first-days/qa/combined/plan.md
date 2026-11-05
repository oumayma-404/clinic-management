# QA plan — everything on one branch (`feature/guided-first-days-all`)

**Why:** R1 + R2 + R3 (stacked) + `fix/second-factor-rescue` merged into one branch so the owner tests once. The
merge had six conflicts; the only rendering one is `web/components/user-management.tsx`, where C2 (invite by e-mail)
and the rescue (second-factor reset + ONE shared row menu for both trees) met.

**Scope:** re-run, unchanged, the two walks that own the « Utilisateurs » screen, on an isolated stack.

| Walk | Copied from | Rows | Changed |
|---|---|---|---|
| `walk-c.mjs` | `qa/c/walk.mjs` (run-3 GREEN, 55 ✅) | A1–A8, B1–B9, C1–C4, D1–D5 | **A5 only**: « Renvoyer l'e-mail » is now a row-menu item at 1440 (the table shares the card's menu since the rescue), not a visible button. Expected and observable: menu offers « Renvoyer l'e-mail », no « Réinitialiser » |
| `walk-rescue.mjs` | `features/password-recovery-gaps/qa/walk.mjs` (run-2 GREEN, 59 ✅) | A1–A8, B1–B6, C1–C5, D1–D5 | none |

Rows from the other R1/R2/R3 plans are not re-run: the merge changed no file they own (`git diff 2a28d60a..HEAD`
touches users/auth/login/card-list only, all covered above).

**Stack:** API :5097 + web :3097 from this worktree, throwaway DB `clinic_gfd_all` (dropped after), shared mailpit
read only. The C walk and the rescue walk are driven ≥ 5 min apart (anonymous auth limit).

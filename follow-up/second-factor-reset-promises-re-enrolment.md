# A second-factor reset tells the person they will enrol again — false unless they are an admin

**Feature:** password-recovery-gaps (found by its QA run 1, 2026-11-03) · **Type:** bug (wording) · **Priority:** medium ·
**Created:** 2026-11-03

## What happens

After a reset, a new second factor is **asked for** only when the account must hold one:
`RequiresAdminSecondFactor && user.IsAdmin()` (`LoginCommand.cs:110`, `ManageTotpCommands.cs:147`,
`GetTotpStateQuery.cs:71`). A doctor who had turned one on voluntarily — and every account on a deployment that requires
none — signs in afterwards with the password alone (measured: `qa/run-1.md`, row A6).

Four places still promise the opposite, all on `main`, all older than the rescue branch:

| Where | Sentence |
|---|---|
| `api/ClinicManagement.Application/Common/SecondFactorResetNotice.cs` — e-mail (preheader, « À votre prochaine connexion » + 4 steps) and in-app row | « Vous devrez en enrôler un nouveau à votre prochaine connexion. » |
| `console/components/reset-second-factor-dialog.tsx:256` (vendor console) | « À sa prochaine connexion il devra en enrôler un nouveau. » |
| `api/ClinicManagement.API/Maintenance/ResetUserTotpConsoleCommand.cs:85` (LAN verb) | « Ce compte doit en enrôler un nouveau à sa prochaine connexion. » |

The clinic screen's own copy (step-up sentence + toast in `web/components/user-management.tsx`) was fixed on
`fix/second-factor-rescue` by stating only what happened.

## Why it was not fixed with the branch

`SecondFactorResetNotice`'s comment calls « a new one must be enrolled at the next sign-in » one of three load-bearing
statements. That is a decision on record, so it is the owner's call — and the fix touches the e-mail, the in-app
notification, the console and the LAN verb, which is a change of its own.

## Fix (when picked up)

- The server knows the answer per account: pass `mustEnrol = policy.RequiresAdminSecondFactor && user.IsAdmin()` into
  `SecondFactorResetNotice.Email/InApp`. Not required → « Votre compte n'est plus protégé que par son mot de passe.
  Vous pouvez activer un nouveau second facteur depuis « Sécurité ». » and no 4-step walk-through.
- Console dialog: the console already reads the user; phrase it from the role the same way. LAN verb: same test.
- Keep the third statement (« si vous n'êtes pas à l'origine… ») untouched — it is the reason the notice exists.
- Tests: `SecondFactorResetNotice` both branches; the two reset commands pass the right flag.

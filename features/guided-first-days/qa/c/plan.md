# QA plan — Guided first days, part C (colleague invited by e-mail)

**Written:** 2026-11-03
**Change under test:** `feature/guided-first-days-r2` — `27fad5da` (C1 server) · `8a001323` (C2 web)
**Rendering files in the diff:** `web/components/user-management.tsx`, `web/app/invitation/page.tsx`, `web/app/login/page.tsx`
**Blast-radius table:** `features/guided-first-days/blast-radius.md` — rows #12, #14, #27, #30, #31, #32
**Budget:** small feature with a public page and a password write → tiers A, B, C, D · 24 scenarios

**Environment:** isolated stack — API `:5097` (`dotnet run` from the worktree, `FrontendUrl=http://localhost:3097`, profile
`HostedMultiTenant` = dev default), web `next dev :3097`, throwaway database `clinic_gfd_r2` (gets
`AddStaffInvitations` on start), mail through the shared `clinic-mailpit` (read only). Own `playwright-core` Chrome, fresh
contexts: the invitee never shares the admin's context. A new cabinet + admin per drive.
**Fixtures:** none. Every account is created through the product. One rendering-only case (B7) rewrites a create response
with `page.route` — the server's own « e-mail not sent » path is unit-tested.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/users` | admin opens Utilisateurs | header button « Inviter un collègue »; dialog title « Inviter un collègue », description « APEXA lui envoie un e-mail… », submit « Envoyer l'invitation » | browser | 1440 |
| A2 | `/users` invite | name « Amel QA », secretary, e-mail, Envoyer | dialog « Invitation envoyée » naming the address; a link `http://localhost:3097/invitation#t=…`; « Copier le lien »; no temporary-password dialog | browser | 1440 |
| A3 | — | after A2 | `Users`: `MustChangePassword` false, `LastLoginAt` null; one `StaffInvitations` row, expires ≈ +7 days, not consumed | sql | — |
| A4 | mailpit | the invitee's inbox | subject « … vous invite sur APEXA »; « Choisir mon mot de passe »; the identifiant; « valable 7 jours »; the link equals the one shown in A2 | api | — |
| A5 | `/users` row | after A2 | « Invitation envoyée » chip; « Renvoyer l'e-mail » in place of « Réinitialiser le mot de passe »; « Jamais » | browser | 1440 |
| A6 | `/invitation` | fresh context, open the mailed link | « Bienvenue, Amel »; « Cabinet … vous a créé un compte APEXA »; identifiant field = the address, read-only; label « au moins 12 caractères »; the `#t=` is gone from the address bar | browser | 1440 |
| A7 | `/invitation` → `/login` | type and confirm a password, Enregistrer | lands on `/login`, e-mail pre-filled, « Mot de passe enregistré » banner; sign in with it → leaves `/login` | browser | 1440 |
| A8 | — / `/users` | after A7 | invitation consumed; admin's row: no chip, « Réinitialiser le mot de passe » back, last sign-in not « Jamais » | sql + browser | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | `/invitation` | open the A6 link again after A7 | « Lien expiré » + « n'est plus valable » + « se connecter » link to `/login` | browser | 1440 |
| B2 | `/invitation` | no fragment | « Lien incomplet » | browser | 1440 |
| B3 | `/invitation#t=garbage` | open | « Lien expiré » | browser | 1440 |
| B4 | `/invitation` form | mismatch, then too short | « Les deux mots de passe ne correspondent pas. », then « au moins 12 caractères »; values kept; link not consumed | browser + sql | 1440 |
| B5 | `/users` « Renvoyer l'e-mail » | doctor invite (practitioner fields), then Renvoyer | confirm « Renvoyer l'invitation ? » naming the address + « L'ancien lien ne fonctionnera plus. »; a new link; old link → « Lien expiré »; new link greets; two mails | browser + api | 1440 |
| B6 | — | after B5 | the doctor has a `Doctors` row | sql | — |
| B7 | `/users` invite, mail not sent | create response rewritten `invitationEmailed: false` | title « L'e-mail n'est pas parti », « existe », « WhatsApp », link + « Copier le lien » | browser | 1440 |
| B8 | `/users` mode probe fails | `/api/auth/mode` → 500 | button reads « Créer un compte », yet a create still opens the invitation dialog (the response decides) | browser | 1440 |
| B9 | `/users` invite duplicate | the A2 address again | error banner in the dialog, dialog open, fields kept | browser | 1440 |
| B11 | `/invitation` refused read (added run 2) | preview answered 429 with the server's sentence (route) | « L'invitation n'a pas pu être lue. » + the server's sentence + « Réessayer », never « Vérifiez votre connexion » | browser | 1440 |
| B10 | own-server profile / no `FrontendUrl` | — | temporary password unchanged | — | ⏭ needs a second API (LAN profile or no `FrontendUrl`); held by `StaffInvitationTests` (LAN → temp password; no public URL → temp password) |

## Tier C — edge cases

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| C1 | `/invitation` form | a fresh invite, open the link | no horizontal overflow; button on screen; read 320 shot | browser | 320, 390, 1536×730 |
| C2 | invitation dialog | the B5 resend at 390 | link wraps inside the box, « Copier le lien » inside the dialog, no page overflow | browser | 390 |
| C3 | `/users` cards | 390 | chip in the card; « Renvoyer l'e-mail » in its « ⋯ » menu | browser | 390 |

## Tier D — regression sweep

| ID | Blast row | Steps | Expected (observable) | Layer | Widths |
|----|-----------|-------|-----------------------|-------|--------|
| D1 | #31 | `/login` with no query | e-mail field empty; no banner | browser | 1440 |
| D2 | #31 | `/login?email=x@y.tn` | e-mail field = `x@y.tn`, password mode | browser | 1440 |
| D3 | #32 | admin « Réinitialiser le mot de passe » on the signed-in secretary | « Mot de passe temporaire » dialog with a password (reset path unchanged) | browser | 1440 |
| D4 | #27 | `/reinitialiser-mot-de-passe` signed out | stays on the page (no redirect to `/login`) | browser | 1440 |
| D5 | #12 | `/users?invite=1` | dialog opens in invite wording with « Secrétaire » selected; `?invite` gone from the address | browser | 1440 |

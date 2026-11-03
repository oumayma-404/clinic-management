# QA plan — Guided first days, release 1 (parts A + B)

**Written:** 2026-11-03
**Change under test:** `feature/guided-first-days` — `58209c06` (A1) · `91fc06dd` (A2) · `cde9fc35` (B1) · `dba3bb8d` (B2)
**Rendering files in the diff:** 9 — `app/login/page.tsx`, `app/securite/page.tsx`, `app/signup/verifier/page.tsx`,
`components/security/totp-enrolment-step.tsx`, `components/onboarding/video-link.tsx`,
`components/dashboard/{dashboard-page,dashboard-customizer,getting-started-card,phone-app-dialog}.tsx`,
`components/user-management.tsx`, `components/letterhead/letterhead-card.tsx`
**Blast-radius table:** `features/guided-first-days/blast-radius.md` — R1 rows marked `must re-test`: #1, #4, #7, #10, #11, #13
(#19–#23 belong to R2/R3)
**Budget:** large (auth + a new dashboard surface) → tiers A, B, C, D · 34 scenarios

**Environment:** isolated stack — API `:5097`, web `next dev :3097`, throwaway database `clinic_gfd_qa` in the shared
`clinic-postgres` container (the branch carries a migration the shared `clinic_management` lacks), own DataProtection
ring in the scratchpad, `Clients__StoreUrls__Android` set so the phone QR has something to encode, mail through the
shared `clinic-mailpit`. Profile: `HostedMultiTenant` (dev default) — signup open, admin second factor required,
30-day trial. Own `playwright-core` Chrome, fresh contexts; no `route()`.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/signup/verifier#token=…` | sign up (API), open the mailed link | « … est créé », « Prochaine étape : votre code de sécurité », 2 numbered lines, « Google Play » + « App Store » links (`target=_blank`), « Se connecter », « Voir la vidéo · 0:53 » → `youtube.com/watch?v=wQ-WNx39U50` | browser | 390, 1440 |
| A2 | mailpit | read the welcome e-mail | contains « Vidéos : vos premiers pas » and `https://apexa.tn/tutoriels.html` | api | — |
| A3 | `/login` | sign in with the new admin's password | enrolment screen with a QR + key, **no** « Même QR » / « Nouveau QR » notice | browser | 1440 |
| A4 | `/login` | « Revenir à la connexion », sign in again | « Même QR code que tout à l'heure. Déjà scanné ? … », the key text identical to A3 | browser | 1440 |
| A5 | `/login` | reload the page, sign in again | the same key again, « Même QR » notice | browser | 1440 |
| A6 | — | `Users` row after A3–A5 | `ProtectedTotpSecret` unchanged across A3→A5, `TotpEnrolmentStartedAt` set, `TokenVersion` unchanged A3→A5 | sql | — |
| A7 | `/login` | type the current code from the (replaced, see B3) key, confirm, « J'ai noté mes codes », sign in with a fresh code | the eight recovery codes, then the dashboard | browser | 1440 |
| A8 | `/` | dashboard as the admin | « Premiers pas » card under the greeting: « Votre cabinet en 7 étapes », « 1 sur 7 faite », « Essai gratuit · N jours », « Code de sécurité » + « Fait », « Votre secrétaire » carries the FILLED « Commencer », the others outlined | browser | 1440 |
| A9 | `/` card | video links | each ▶ is an `<a target=_blank>` to its YouTube id (6 steps), named « Vidéo : … » ; « Votre téléphone » has no ▶ | browser | 1440 |
| A10 | « Votre téléphone » → « Installer » | open the dialog | « L'application sur votre téléphone », a QR `<img>` on a white plate, the 3 iPhone steps naming `localhost:3097`, « Déjà fait » → « Vous · pas encore », « Terminé » closes | browser | 1440 |
| A11 | API | sign in with `clientSurface: "android"` + a fresh code | `PhoneAppPlatform = android` in SQL; after a reload the card's « Votre téléphone » reads « Fait » and the dialog reads « Vous · Android · 3 nov. 2026 » | sql + browser | 1440 |
| A12 | card → « Votre secrétaire » « Commencer » | follow the link | `/users`, « Créer un compte » open, role « Secrétaire », no `invite` left in the address bar | browser | 1440 |
| A13 | `/users` dialog | create « Amel QA » (secrétaire), back to `/` | « Votre secrétaire » « Fait », « 3 sur 7 faites » | browser | 1440 |
| A14 | card → « Vos patients » « Commencer » | follow the link | `/patients` with the new-patient dialog open | browser | 1440 |
| A15 | card → « Papier à en-tête » « Commencer » | follow the link | `/settings#en-tete`, « En-tête des documents » expanded (`aria-expanded=true`) and inside the viewport | browser | 1440 |
| A16 | card « Masquer » | press it, then « Annuler » in the toast | the card leaves, toast « Premiers pas masqués. » with « Annuler »; « Annuler » brings the card back | browser | 1440 |
| A17 | « Masquer » then « Personnaliser » | turn « Premiers pas » back on | the switch is listed under « La journée » with « Carte, sous le bonjour »; switching it on brings the card back | browser | 1440 |
| A18 | card while open | the clinic leaves its first 30 days (SQL fixture on the throwaway DB: `CreatedAt` − 31 d), tab hidden → visible | the card leaves and « Votre cabinet est prêt. » shows (the toast path; the all-done server rule is unit-tested) | browser | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | `/login?enrol=1` (signed in) | open it | « Activer le second facteur » form, **no** « Plus tard » | browser | 1440 |
| B2 | `/login?enrol=1&from=securite` | open it | the same form **with** « Plus tard » | browser | 1440 |
| B3 | `/login` | before A7: SQL ages `TotpEnrolmentStartedAt` by 25 h, sign in | « Nouveau QR code : il remplace le précédent. Dans votre application, supprimez la ligne « <cabinet> » … », and a different key | browser + sql | 1440 |
| B4 | `/login` in a browser | first paint | « Rester connecté sur cet appareil » unticked | browser | 1440 |
| B5 | `/login` with `window.__clinicShell = { platform: "windows" }` | first paint | the box ticked | browser | 1440 |
| B6 | — | after the browser sign-in of A7 | `PhoneAppFirstSignInAt` NULL (a browser records nothing) | sql | — |
| B7 | `/` as a doctor | create a doctor account (UI), sign in, change the temp password | no « Premiers pas » card; « Personnaliser » lists no « Premiers pas » row | browser | 1440 |
| B8 | phone dialog with no Android link | (covered by the dev default without `Clients__StoreUrls__Android`) | — | — | ⏭ planned: the stack runs WITH the link; the no-link sentence is one branch of JSX |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | verifier page at the floor | A1 at 320 | no horizontal scroll; store buttons wrap inside the card | browser | 320 |
| C2 | enrolment notice at the floor | B3 screen at 320 | the amber notice wraps inside the card; no horizontal scroll | browser | 320 |
| C3 | the card at every width | dashboard as admin | no horizontal scroll at 320/390/820/1180/1440; every row's buttons inside the card | browser | 320, 390, 820, 1180, 1440 |
| C4 | owner's laptop | dashboard + phone dialog at 1536 × 730 | the card fully visible under the greeting; the dialog's « Terminé » reachable | browser | 1536×730 |
| C5 | phone dialog at the floor | open at 320 | QR centred, no horizontal scroll, « Terminé » reachable | browser | 320 |

## Tier D — regression sweep (from the blast-radius table)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #1 replacement after a recovery code | `/login` « Utiliser un code de récupération » | sign-in with one recovery code → « Remplacer votre second facteur » → « Remplacer maintenant » → QR with « Nouveau QR code : il remplace le précédent » (run LAST) | browser | 1440 |
| D2 | #1 first enrolment | A3–A7 | the first enrolment still completes and signs in | browser | — |
| D3 | #1 `e2e/bootstrap.mjs` | — | ⏭ planned: the e2e stack is not run here; it reads `secret`, which is unchanged | — | — |
| D4 | #4 migration | `verify-schema` | identical before/after (already run 2026-11-03 on a throwaway DB, A1 commit) | sql | — |
| D5 | #7 « Rester connecté » | B4/B5 + A7 still signs in with the box unticked | — | browser | — |
| D6 | #10 customiser | as admin with the card, « Tout afficher » then « Par défaut » | the card stays (not hidden by default); badge total as admin = doctor's + 1 | browser | 1440 |
| D7 | #11 dashboard | admin and doctor layouts | greeting, « Personnaliser », the day board still render; doctor's page has no card | browser | 1440 |
| D8 | #13 letterhead card | `/settings` with no hash | « En-tête des documents » collapsed (`aria-expanded=false`) | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| « Premier rendez-vous », « Première fiche de soins », « Papier à en-tête » ticking | an appointment and a fiche are clinical writes and the letterhead uploads into the shared MinIO bucket; each tick is one fact covered by `GetGettingStartedQueryTests` |
| the native shells (Windows, Android) | not runnable from here; B5 emulates the bridge's `platform` for the one thing the web reads from it |
| the site's `tutoriels.html` | not part of this branch's web app; published separately with the owner's OK |

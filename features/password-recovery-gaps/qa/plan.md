# QA plan — lost second factor: a visible way back (branch `fix/second-factor-rescue`, commit f341e051)

**Diff:** `AuthController` (`vendorResetsSecondFactor`, `supportPhone` on `GET auth/mode`) · `ClinicUserDto.HasSecondFactor` ·
login « Pas de code de récupération ? » line (`NoRecoveryCodeLine`) · `/users` row menu shared by cards and table, with
« Réinitialiser le second facteur » through `StepUpDialog` · cards/table hinge moved from `lg` (1024) to `xl` (1280).

**Stack:** isolated — API `:5097` from this worktree (Hosted profile, `Subscription__ContactPhone="+216 20 123 456"`,
`RateLimiting__Auth__PermitLimit=200` so one drive is not throttled), web `next dev :3097`, throwaway DB `clinic_sfr_qa`,
shared mailpit (read only), own headless Chrome. Fresh cabinet per drive.

**Blast radius (must re-test):** the row menu replaced the table's visible buttons → reset password, deactivate (self
disabled), reactivate/activate · `CARDS_ONLY_XL` only used by `/users` (other `CardList` users unchanged) · `GET auth/mode`
read by login, signup, change-password hooks (fields added only) · `ClinicUserDto` read by `/users` only.

| Id | Tier | Scenario | Expected (observable) | Layer |
|----|------|----------|-----------------------|-------|
| A1 | A | `GET /api/auth/mode` on hosted | `vendorResetsSecondFactor: true`, `supportPhone: "+216 20 123 456"` | api |
| A2 | A | Login → code step → « Je n'ai plus accès à mon application » | recovery step shows « Pas de code de récupération ? Un administrateur de votre cabinet peut réinitialiser votre second facteur. Sinon, appelez le support : +216 20 123 456. » with `href="tel:+21620123456"` | browser |
| A3 | A | `/users` as admin, a doctor who turned a second factor on (a secretary cannot hold one: her « Sécurité » is closed) | his row menu offers « Réinitialiser le second facteur »; the admin's own row does not | browser |
| A4 | A | Pick it → step-up | dialog « Confirmer votre identité », sentence names his e-mail and « codes de récupération seront supprimés », and makes no claim about his next sign-in | browser |
| A5 | A | Enter the admin's code → Confirmer | toast « Second facteur réinitialisé. Ses sessions ont été fermées. »; DB: doctor no longer enrolled, no recovery codes; his menu no longer offers the item | browser + sql |
| A6 | A | The doctor signs in again | his factor is optional on this profile: he lands in the app with his password alone (the admin case is A8) | browser |
| A7 | A | His browser, open before the reset, goes to another page | lands on `/login` (sessions closed, as the sentence says) | browser |
| A8 | A | Same rescue for a second administrator (factor required here) | toast; DB not enrolled; she signs in again → enrolment | browser + sql |
| B1 | B | A colleague who never enrolled (just created) | no « Réinitialiser le second facteur » in his menu | browser |
| B2 | B | Step-up → Annuler | nothing changes (DB: still enrolled) | browser + sql |
| B3 | B | Step-up with a wrong code | refusal shown in the dialog, DB still enrolled | browser + sql |
| B4 | B | `auth/mode` says `vendorResetsSecondFactor: false` (LAN, by route) | line ends « … la personne qui a installé le logiciel peut le faire depuis le PC serveur du cabinet. », no phone | browser |
| B5 | B | `auth/mode` without the field (older API, by route) | colleague-administrator sentence only | browser |
| B6 | B | `auth/mode` fails (by route) | no line at all; « Revenir au code de vérification » still there | browser |
| C1 | C | `/users` at 1024, 1279 | cards (no table), no horizontal page scroll, menu button inside the viewport | browser |
| C2 | C | `/users` at 1280, 1440 | table, « Actions » column fully inside the viewport, no horizontal scroll | browser |
| C3 | C | `/users` at 320, 390 | cards, no horizontal scroll, menu reachable | browser |
| C4 | C | Recovery step at 320 × 640 | line wraps, phone number on one line, no horizontal scroll | browser |
| C5 | C | Step-up at 1536 × 730 | « Confirmer » visible without scrolling | browser |
| D1 | D | Menu → « Réinitialiser le mot de passe » on the colleague | confirm → « Mot de passe temporaire » with a password | browser |
| D2 | D | Own row → « Désactiver » | disabled | browser |
| D3 | D | Colleague → « Désactiver » then « Réactiver » | status chip changes both ways | browser |
| D4 | D | Search the list by name | only the matching row in the visible tree | browser |
| D5 | D | Ordinary sign-in with a code (admin, secretary) | lands in the app | browser (setup) |

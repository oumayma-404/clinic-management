# QA plan — demo-cabinet, story 1 (enter the demo)

**Stack:** isolated — API `:5099` (`PublicDemo`, DB `clinic_demo_local`, `Demo__SessionMinutes=2` so expiry is
testable in one run), web `:3099` (`next dev`). Own Chrome via `playwright-core`, fresh contexts. Not the shared
stack, not the MCP profile.
**Out of scope:** the band, refusals and adapted surfaces (stories 2–3); the full scenario (story 4).

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| A1 | A | browser | Fresh visitor opens `/` | lands on `/demo`; heading « Cabinet de démonstration »; button « Entrer dans la démo »; link « Créer mon cabinet » → `https://app.apexa.tn/signup`, new tab |
| A2 | A | browser | Press « Entrer dans la démo » | URL leaves `/demo`; dashboard renders (header « Mon compte » button present); no password / code field ever shown |
| A3 | A | browser | Second visitor (new context) enters while the first is in | both reach the dashboard; first still signed in after the second enters (reload keeps it off `/demo`) |
| A4 | A | api | Each entry is its own session, User row untouched | SQL: N new `SessionFamilies`, distinct, untrusted; `Users.LastLoginAt` NULL — answered before launch |
| B1 | B | browser | Signed-in visitor opens `/demo` | redirected to `/` |
| B2 | B | browser | Visitor opens `/login`, `/signup`, `/mot-de-passe-oublie`, `/join` (no session) | each ends on `/demo` |
| B3 | B | browser | « Mon compte » → « Se déconnecter » | ends on `/demo` with **no** « La démo a expiré » line |
| B4 | B | browser | Session past its 2-min cap, then reload | ends on `/demo?expired=1` showing « La démo a expiré. Entrez à nouveau pour continuer. »; « Session expirée / Reconnectez-vous » toast **not** shown |
| B5 | B | api | `POST /api/auth/demo-session` on a non-demo deployment | 404 — covered by unit test `DemoSession_404s_anywhere_but_the_public_demo` (no second API kind running) |
| C1 | C | browser | `/demo` at 320 / 390 / 820 / 1180 / 1440 wide, and 1536×730 | no horizontal page scroll; button and link fully visible without scrolling at 730 tall |
| C2 | C | browser | `/demo` at 320 on a coarse pointer | « Entrer dans la démo » ≥ 44 px tall |
| C3 | C | browser | Keyboard only: Tab to the button, Enter | enters the demo |
| C4 | C | browser | Link preview metadata | `<title>` « APEXA Démo »; meta description present |
| D1 | D | browser | Tier D — a non-demo login still works | ⏭ unless a `HostedMultiTenant` API is available; covered by `GetMode_reports_whether_this_is_the_public_demo` + unchanged login path |

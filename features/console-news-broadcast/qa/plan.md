# QA plan — Nouveautés par e-mail depuis la console

**Written:** 2026-11-06
**Change under test:** `feature/console-news-broadcast` (worktree, uncommitted, from `origin/main` 7000b0e0)
**Rendering files in the diff:** 7 — `console/app/nouveautes/page.tsx`, `components/news-composer.tsx`,
`components/news-history.tsx`, `components/auto-refresh.tsx`, `app/cabinets/page.tsx`, plus the server-rendered
e-mail (`EmailLayout`) shown in the preview iframe
**Blast-radius table:** `progress.md` § Blast Radius (1 row `must re-test`: #2 EmailLayout)
**Budget:** small → tiers A, B, C (reachable), D · 21 scenarios

## Isolated stack (nothing shared is written)

- DB `clinic_news_qa` (new, empty) on the shared Postgres server; API from the worktree on **:5094**, console
  listener **:5494**; console `next start` on **:3094**; own key ring + throwaway MinIO bucket `news-qa`.
- Mail lands in the shared Mailpit (:8025); every QA address is `@news-qa.test`, so filtering is exact.
- Seed: A « Cabinet Essai » (trial, admin `a@`), B « Cabinet Payant » (paid 12 mois, admins `b@` + `b2@`,
  secretary `s@`, plus a hash-less admin row `B@…` duplicating `b@`), C « Cabinet Expiré » (end date in the past,
  admin `c@`), D « Cabinet Suspendu » (admin `d@`), F « Cabinet Sans Admin » (trial, no admin).
- Expected audience: **2 cabinets · 3 adresses · 1 sans adresse** (A, B ; F has none ; C, D excluded).

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/cabinets` header | click « Nouveautés » | `/nouveautes` opens; button reads « Envoyer à 2 cabinets » | browser | 1440 |
| A2 | `/nouveautes` preview | type subject « QA nouveautés <run> », 2 rows (🚀 + 📅) | iframe shows « Happy Apexing ! 🦷✨ », the subject as heading, both rows with their emoji, « Apexa, toujours à vos côtés 💙 », logo `<img>` decoded (naturalWidth > 0) | browser | 1440 |
| A3 | « M'envoyer un test » | press it | status « E-mail de test envoyé à qa@news-qa.test. »; Mailpit has 1 message to `qa@` with subject « [Test] QA nouveautés <run> »; history unchanged | browser + api | 1440 |
| A4 | « Envoyer » | press → confirm | sheet reads « 2 cabinets · 3 adresses d'administrateur » and « 1 cabinet sans adresse… »; « Envoyer » → status « Envoi en cours vers 3 adresses. »; subject back to default, one empty row | browser | 1440 |
| A5 | Mailpit | list messages with the run subject | exactly 3, to `a@`, `B@News-QA.test` (Essai's hash-less admin, same mailbox as `b@`), `b2@`; none to `s@`, `c@`, `d@`, `f@`, and no second copy to `b@` | api | — |
| A6 | history | wait for refresh | one new row: subject, « 3 / 3 envoyés », no « en cours » | browser | 1440 |
| A7 | Mailpit message body | read `a@`'s message | HTML has the kicker, both emoji rows, closing line, « …administrateur du cabinet Cabinet Essai. », a button to `<FrontendUrl>/login`; text part has `🚀 <row 1>` lines | api | — |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | `POST /api/platform/news` | repeat A4's requestId | 202, `alreadyQueued: true`; Mailpit count for the subject still 3; history still one row | api | — |
| B2 | `POST /api/platform/news` | empty subject · no text row · subject 121 · row 301 · 13 rows | each 400 `news_invalid` with a French sentence; no row written | api | — |
| B3 | `POST …/news/preview` | a row `<b>gras</b>` | HTML contains `&lt;b&gt;gras&lt;/b&gt;`, never `<b>gras` | api | — |
| B4 | form | clear the only row's text | « M'envoyer un test » and « Envoyer » disabled | browser | 1440 |
| B5 | row controls | add 2 rows, move row 3 up, delete row 2; add to 12 | order follows the moves; « Ajouter » disabled at 12; delete disabled at 1 row | browser | 1440 |
| B6 | refusal shown | send a test with a 301-char row via devtools-free path: type 300 (maxLength) — then API refusal not reachable from the UI | ⏭ the UI caps at 300 (maxLength); the server refusal is B2 | — | — |
| B7 | SMTP not configured | — | ⏭ needs a second API without SMTP; covered by unit tests in `/test-small-feature` | — | — |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | open `/nouveautes` | `scrollWidth ≤ clientWidth` on document; preview under the form; history as cards | browser | 320 |
| C2 | coarse pointer | emulate `pointer: coarse` at 390 | emoji select ≥ 44 px tall; each icon button's `::after` ≥ 44×44 | browser | 390 |
| C3 | owner's laptop | confirm sheet at 1536×730 | « Envoyer » and « Revenir » fully inside the viewport | browser | 1536×730 |
| C4 | widths | screenshot `/nouveautes` | no overflow at 390 / 820 / 1180 / 1440; table at ≥ 1024, cards below | browser | 390, 820, 1180, 1440 |
| C6 | sheet geometry (added cycle 1, F-1) | confirm sheet at 1536×730 | the « X » is `absolute`, inside the panel's top-right; the panel ends at its footer (≤ 2 px) | browser | 1536×730 |
| C5 | one address, two accounts | seed `B@news-qa.test` (hash-less admin of A) | audience still 3 addresses (dedupe, case-insensitive) | api | — |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #2 EmailLayout | every other transactional e-mail | ⏭ byte-identical render is a unit assertion — `/test-small-feature` | — | — |
| D2 | #13 header | `/cabinets` at 320 | 4 links + « Se déconnecter » wrap, no horizontal scroll | browser | 320 |
| D3 | #3 « Actif » predicate | `GET /api/platform/clinics?state=active` | lists A, B, F (3) — the portfolio filter unchanged | api | — |
| D4 | console pages | `/journal`, `/retours` | both render their heading (no 500) | browser | 1440 |
| D5 | #16 `:where(.touch-target)` (added cycle 1) | `/login` password field | « Afficher le mot de passe » sits inside the field's box | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| SMTP not configured (503) | needs a second API process with no SMTP; a unit test is decisive |
| Byte-identical existing e-mails | a renderer equality is a unit assertion, not a browser fact |
| Real clinic app | no clinic screen changed |

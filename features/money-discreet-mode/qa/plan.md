# QA plan — Mode discret

**Written:** 2026-10-03
**Change under test:** feature/security-remediation, uncommitted working tree (money-discreet-mode files only)
**Rendering files in the diff:** 12 — `money-discreet-card.tsx`, `money-hidden-card.tsx`, `money-visibility-context.tsx`,
`layout.tsx`, `clinic-settings.tsx`, `dashboard-sidebar.tsx`, `bottom-nav.tsx`, `nav.ts`, `caisse/page.tsx`,
`factures/page.tsx`, `cheques/page.tsx`, `dashboard-page.tsx`
**Blast-radius table:** progress.md (5 rows `must re-test`: #4, #9, #10, #13, #15)
**Budget:** small feature → tiers A, B, C (reachable), D · 24 scenarios

Account: the QA admin (`salma.benyoussef@…`, TOTP enrolled). Bearer for `api` rows = the one the app itself sends,
captured from a request — never a second `/bff/auth/token` fetch.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A0 | `/settings` | open as admin, shown | « Mode discret » card is the last card, button « Masquer l'argent » enabled | browser | 1440 |
| A1 | `/settings` → rail | press « Masquer l'argent » | toast « L'argent est masqué… »; rail has no Factures/Caisse/Chèques, without reload; card reads « Afficher l'argent » | browser | 1440 |
| A1b | `/` | open dashboard while hidden | no « L'argent » section; « L'activité » present | browser | 1440 |
| A2 | — | `GET billing/caisse`, `billing/caisse/ledger`, `billing/cheques`, `billing/receivables`, `expenses`, `expenses/recurring`, `invoices`, `invoices/revenue`, `invoices/export` | each 403 with `code: money_hidden` | api | — |
| A2b | — | `GET invoices?patientId=<id>` | 200 | api | — |
| A3 | — | `GET dashboard` | `money: null`, `receivables: null`, `trend: []`, `activity` and `alerts` present | api | — |
| A4 | `/settings` | « Afficher l'argent » → code from the authenticator | toast « de nouveau affiché »; rail has Factures/Caisse/Chèques again; after reload still shown | browser | 1440 |
| A8 | `/caisse` (fresh load while hidden) | navigate, sample the DOM every 50 ms until settled | « Section masquée » card; no `… DT` amount painted at any sample; no request to `billing/` or `expenses` left the browser | browser | 1440 |
| A10 | — | read the journal | two `Clinic` audit rows naming the account: `IsMoneyHidden` false→true then true→false | sql | — |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | — | `POST auth/step-up` `{action:"show-money", password}` (correct password) | refused (400), no confirmation token | api | — |
| B2 | — | `POST clinics/money/show` with no token | 403 « confirmation récente », still hidden | api | — |
| B3 | `/settings` | « Afficher » → wrong code | dialog stays open, banner « Code de vérification incorrect. », still hidden | browser | 1440 |
| B4 | `/settings` (session role intercepted as `doctor`) | open | no « Mode discret » card; read-only note shown | browser | 1440 |
| B5 | `/settings` (`GET auth/totp` intercepted as not enrolled) | open | button « Masquer l'argent » disabled; link « Sécurité » → `/securite` | browser | 1440 |
| B6 | `/factures`, `/cheques` (hidden) | navigate | « Section masquée » card on each | browser | 1440 |
| B7 | second page on `/caisse` while page 1 hides | hide from page 1, wait | page 2 swaps to « Section masquée » within 10 s, no reload | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | `/settings` | no horizontal scroll; card button full width and inside the card | browser | 320 |
| C2 | 320 px sheet | « Afficher l'argent » at 320×640 | dialog is a bottom sheet; code field and « Confirmer » inside the viewport | browser | 320 |
| C3 | 730 tall | the code dialog at 1536×730 | « Confirmer » visible without scrolling | browser | 1536×730 |
| C4 | phone bar | 390 while hidden | bottom bar has no Factures/Caisse/Chèques entry; drawer neither | browser | 390 |
| C5 | double hide | `POST clinics/money/hide` twice | both 200 `isMoneyHidden:true`; only ONE audit row for the pair | api+sql | — |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #9 | `/caisse` shown | stat strip with « Net » and a DT figure renders, no error toast | browser | 1440 |
| D2 | #9 | `/factures` shown | « Total encaissé » figure renders, table has rows | browser | 1440 |
| D3 | #9 | `/cheques` shown | page header « Chèques » + table or empty state, no « Section masquée » | browser | 1440 |
| D4 | #10 | patient file « Factures » tab while hidden | tab loads, no 403 toast, no « Section masquée » | browser | 1440 |
| D5 | #13 | `/login` signed out | form renders; no `clinics/user-status` request | browser | 1440 |
| D6 | #15 | `/settings` other cards | « Informations du cabinet » still renders under the new card | browser | 1440 |
| D7 | #4 | step-up for another action with a password | 200 with a token (password still accepted elsewhere) | api | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| A real second PC | one browser profile; B7 uses a second page in the same context, which exercises the same broadcast |
| Window-focus re-read | covered by the broadcast path; focus events are unreliable in headless Chrome |
| Lost-phone recovery | a vendor-console flow, not this change |

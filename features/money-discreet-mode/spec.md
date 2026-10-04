# Feature Specification: Mode discret

**Status:** APPROVED
**Type:** Small
**Created:** 2026-10-03
**Scope:** Full
**Feature:** One click hides every clinic-wide money screen; showing it again needs an authenticator code.

## Overview
A cabinet sometimes has a visitor at the desk who must not see the practice's figures. An admin presses one
button in Paramètres and the Finances section and the dashboard's « L'argent » block disappear on every PC of
the cabinet. Showing them again takes the same button plus a 6-digit authenticator code, and they then stay
shown. It changes what is displayed only: no record is altered or deleted.

## What Changes
- Paramètres gets a « Mode discret » card at the top, admin only, with one button: « Masquer l'argent » / « Afficher l'argent ».
- Hiding is one click, with no confirmation and no code.
- Showing asks for an authenticator code only (no password), with the existing limit of 3 tries per 15 min.
- An admin with no authenticator cannot hide: the button is disabled and links to Sécurité.
- While hidden, on every device of the clinic: Factures, Caisse and Chèques leave the menu and the phone bar; typing their URL shows a « Section masquée » card; the dashboard has no « L'argent » block.
- While hidden, the server refuses the clinic-wide money reads behind those screens and sends no money figures in the dashboard read.
- Other open PCs follow within seconds (live refresh, plus a re-read when the window regains focus).
- Every hide and show is in the journal with who did it, and the before/after value.

## Acceptance Criteria
- **AC-1:** Admin with an authenticator presses « Masquer l'argent » → the menu loses Factures/Caisse/Chèques and the dashboard loses « L'argent », with no reload.
- **AC-2:** While hidden, `GET /api/billing/caisse` (and every endpoint listed under API Contract) answers 403 `money_hidden`; `GET /api/invoices?patientId=…` still answers 200.
- **AC-3:** While hidden, `GET /api/dashboard` returns `money: null`, `receivables: null`, `trend: []`; activity and alerts unchanged.
- **AC-4:** « Afficher l'argent » with a correct code → everything is back, and stays back after reload and on other PCs.
- **AC-5:** « Afficher l'argent » with a correct **password** and no code is refused.
- **AC-6:** A doctor or secretary account sees no « Mode discret » card; `POST /api/clinics/money/hide` answers 403 for them.
- **AC-7:** An admin with no authenticator sees « Masquer l'argent » disabled with a link to Sécurité; the endpoint answers 400 `totp_not_enrolled`.
- **AC-8:** On first load of `/caisse` while hidden, no amount is painted at any moment, and no money request leaves the browser.
- **AC-9:** At 320 px the « Mode discret » card fits with no horizontal scroll, and the code dialog opens as a sheet with the code field visible above the keyboard.
- **AC-10:** The journal shows two rows for one hide and one show, naming the account and `IsMoneyHidden` false → true → false.

## API Contract
### POST /api/clinics/money/hide
Request: none · Response 200: `{ isMoneyHidden: true }`
Errors: `403` non-admin · `400 totp_not_enrolled` — « Activez l'authentificateur dans Sécurité pour masquer l'argent. »

### POST /api/clinics/money/show
Request: `{ stepUpConfirmation: string }` (minted by `POST /api/auth/step-up` with `action: "show-money"` and a `totpCode`)
Response 200: `{ isMoneyHidden: false }`
Errors: `403` non-admin or invalid/expired confirmation

### POST /api/auth/step-up (modified)
For `action: "show-money"` only, a password is not accepted as proof. Every other action is unchanged.

### Refused while hidden: `403 money_hidden`
`GET invoices` (without `patientId`) · `invoices/revenue` · `invoices/export` · `billing/caisse` ·
`billing/caisse/ledger` (+ export) · `billing/receivables` (+ export) · `billing/cheques` (+ export) · every
`GET expenses*`.

Both POSTs work on an expired subscription (a read-only cabinet must still be able to hide). Neither checks the
clinic's version: they set a value, so another admin's edit of the clinic info never refuses them.

## Data / Schema Changes
- `Clinic.IsMoneyHidden` — bool, not null, default false. Migration adds the column only.
- `ClinicDto.IsMoneyHidden` — returned by `GET /api/clinics/user-status` and by the clinic update.
- `DashboardDto.Money` / `Receivables` — become nullable (null only when hidden).

## Device Behaviour
- **Leading device:** desk (reception PC).
- **Narrow width (< 640):** the card is one column, the button full width; the code dialog is the existing step-up dialog in its sheet form.
- **Touch:** one button, 44 px on a coarse pointer; nothing is hover-revealed.
Floor: `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope
- Money inside one patient's screens (patient file balance, factures tab, devis, fiche totals, appointment prices) — stays visible.
- Every write (recording a payment, issuing a note) — unchanged.
- Exports from patients, lab orders, stock; the clinic archive download.
- A per-PC or per-user hide; an automatic re-hide.

## Edge Cases (Critical only)
- Lost phone while hidden: another admin or the vendor console resets the authenticator, the admin sets up a new one, then shows.
- Authenticator removed while hidden: « Afficher » answers `totp_not_enrolled`, which links to Sécurité.
- Secretary role: already hides these screens; the two rules combine, neither re-shows anything.
- A screen still open when the cabinet is hidden from another PC: its next money request gets `money_hidden`, which re-reads the setting and swaps the page for the card.

# Feature Specification: Reste à payer worklist

**Status:** APPROVED
**Type:** Small
**Created:** 2026-10-03
**Scope:** Full
**Feature:** A 4th tab « Reste à payer » in « À clôturer » that lists the patients who owe money, split into « À relancer » (money due now) and « En cours » (not due yet).

Mockup (approved direction): https://claude.ai/artifact/CoZv3jxwDxC5JLtUmGAi1m

## Overview
The doctor wants to see only the patients who owe money and to tell apart the normal case (a treatment still
running, paid as it goes) from the one to chase (work finished or a promised date passed, and money missing).
Today the only clinic-wide list is « Créances »: one total per patient, removed from the menu, doctor and admin
only. This tab gives every role two separate lists, the acts behind each amount, and a way to call, message
and collect without leaving the screen.

## What Changes
- « À clôturer » gets a 4th tab « Reste à payer ». Its badge is the number of patients in « À relancer ».
- Two cards at the top show the total and the patient count of each list. Clicking a card switches the list.
- Each amount the patient owes is sorted by the server, using the rules already in place:
  - an unpaid **note d'honoraires** is due now (it is payable on issue);
  - for a **devis**, only the échéances that `InstallmentLateness` already calls late are due now. On a finished or
    stopped treatment that includes the lump-sum échéance the system created. An échéance agreed for a later
    date is not due yet;
  - everything else owed on a devis goes in « En cours ».
- A devis with one late échéance and one future échéance shows up in both lists, each with its own part.
- Each row shows the name, phone, a reason (« Séance du 14/08 non soldée », « Échéance du 15/09 dépassée »,
  « 2 sur 3 actes faits »), the date and days elapsed (À relancer) or the next step (En cours), the amount,
  and « + X DT » from the other list when there is any.
- Every row, card and the panel get **Appeler** (`tel:` link) and **WhatsApp** (`wa.me`, opens an empty chat)
  buttons.
- Clicking a patient opens a side panel with:
  - a bar splitting the two amounts and the « Solde dû »;
  - the documents, « À relancer » first, then « En cours »;
  - each document's acts sorted by date and marked done or planned;
  - Total / Payé / Reste for each document, and an « Encaisser » button.
- « Encaisser » opens the payment window that already exists for that document (note or échéance). The amount
  starts filled with what can be paid. The user can lower it for a partial payment. More than what is left is
  refused by the existing window.

## Acceptance Criteria
- **AC-1:** Admin, doctor and secretary all see the tab. The badge equals the number of « À relancer » patients.
- **AC-2:** An unpaid note goes entirely in « À relancer ». For a devis, only the outstanding of the late échéances
  goes there (capped at the devis' reste); the rest goes in « En cours ». A Draft (followed, un-numbered)
  treatment appears in neither list, the same as in « Solde dû ».
- **AC-3:** For every patient, « À relancer » + « En cours » = the « Solde dû » shown on their file. This is held
  by a test over every fixture of `MoneyReadConsistencyTests`.
- **AC-4:** The cards' totals and counts cover every matching patient, not one page. Search by patient name
  narrows the list and the cards together.
- **AC-5:** « À relancer » is sorted oldest debt first by default. « En cours » is sorted by the next séance or
  échéance date. Both can be sorted by amount, and both are paged.
- **AC-6:** Appeler and WhatsApp use the patient's stored dialable number. When the number cannot be dialled, both
  buttons are absent (never disabled, never a broken link). WhatsApp opens in a new tab.
- **AC-7:** In the panel, the acts under each document are in date order. Each act shows its date, name,
  teeth, price and done/planned. The panel's two amounts match the row it was opened from.
- **AC-8:** Paying 200 on a 450 note through « Encaisser » leaves 250 on the row, the panel and the card, with no
  reload. When the document is fully paid, it leaves the list, and so does the patient if nothing else is
  owed.
- **AC-9:** At 320 px the list is cards with no horizontal scroll. Appeler, WhatsApp and the detail are each
  reachable from the card. The panel takes the full screen. Targets are 44 px on a coarse pointer.
- **AC-10:** A list with nobody in it shows an empty state that names the list (« Personne à relancer »).

## API Contract
### GET /api/billing/reste-a-payer — `AnyClinicRole`
Request: `?list=due|running&search=&sort=age|amount&page=&pageSize=`
Response 200: `{ items: [{ patientId, patientName, phoneNumber, phoneE164, dueAmount, runningAmount, reason,
since, nextDate }], dueTotal, dueCount, runningTotal, runningCount, page, pageSize, totalCount, totalPages }`
### GET /api/patients/{id}/billing-summary — unchanged route and policy, `lines[]` grows (additive)
Each line gains `dueNow: decimal` (its « À relancer » part), `reason: string`, and
`acts: [{ date, designation, teeth, amount, done }]`.

## Data / Schema Changes
- No migration. `PatientDebtLineDto` gains the three fields above. One shared rule decides `dueNow` per
  document, and both reads go through it.

## Device Behaviour
- **Leading device:** desk (reception PC). The doctor also uses it on a phone.
- **Narrow width (< 640):** the table becomes cards with full-width Appeler / WhatsApp buttons, the panel becomes a
  full-screen sheet, and the payment window becomes a bottom sheet. Inherits `~/.claude/skills/DEVICE-CONTRACT.md`.
- **Touch:** nothing is hover-revealed. Every action is a visible button, 44 px on a coarse pointer.

## Out of Scope
- The « Créances » page and its CSV export: unchanged, still doctor/admin only, still out of the menu.
- A pre-written WhatsApp or SMS message, and automatic payment reminders.
- Paying per act: a payment still goes on a note or a devis.
- The dashboard and the patient file's « Reste à payer » block. They only gain the new line fields.

## Edge Cases (Critical only)
- A devis billed on a note (bridged) counts once, on the note, exactly as « Solde dû » does.
- An échéancier that no longer sums to its devis: `dueNow` is capped at the devis' reste, so AC-3 still holds.
- A patient with several phone numbers: the buttons use the primary one.

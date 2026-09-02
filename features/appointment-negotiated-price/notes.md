# appointment-negotiated-price — shipped notes

What the code actually does, and the decisions that are easy to undo by accident.

## A price agreed on the telephone is the price billed (`appointment-negotiated-price`)

A patient telephones for an appointment and haggles over the price of an act. The product already had an
answer — the **devis** — and it is the wrong size for the question: a treatment plan is a numbered, validated,
revisable document with an échéancier, and this is one act settled in one sentence at the desk. So the desk
did not use it, the negotiated figure lived in somebody's memory or in the appointment's free-text notes, and
the fiche de soins later priced the act at the catalogue tarif.

**« Actes du rendez-vous » now carries a price per act.** Each row in the booking dialogs shows
« Prix pour ce rendez-vous », prefilled with the act's catalogue tarif, editable, and carried into the fiche
de soins for that visit — so the amount quoted is the amount charged.

### Four decisions, each of which is a defect if reversed

⚠️ **`AgreedCost` is nullable and null means « nobody negotiated ».** It is *not* 0 (an act offered — a real
negotiation, and one a cabinet does make), and it is *not* the catalogue tarif copied in. Both substitutions
are tempting and both are wrong in the same way: writing the tarif onto the row would turn every visit ever
booked into a negotiation, freeze a snapshot of today's catalogue onto the future, and make a later price rise
invisible to a booking nobody had negotiated. Which is why the migration ships **no default and no backfill**,
and why `AppointmentProcedureSelection.ResolveAsync` passes the client's value through verbatim — including
its absence — rather than falling back to `procedureType.DefaultCost`.

⚠️ **The field is prefilled on screen but untouched in state.** `SelectedAct.agreedCost` is `undefined` until
the user types, and the input *displays* the tarif in the meantime. That is what lets the dentist see the
standard price (which is what was asked for) while the booking still says « rien de négocié » on the wire.
`undefined` and `""` are different: the second is a cleared field, which is the same statement as the first
(« leave it at the tarif ») and is why `agreedCostOf` reads both as null.

⚠️ **It is a forfait, never a per-tooth rate.** Teeth are not known when a visit is booked, so a unit price
cannot be turned back into the total the patient was quoted: told « 120 DT » for two extractions, a per-tooth
reading bills 240. `applyProcedure` therefore sets `perTooth: false` with `perToothLocked`, and the fiche
reopens such an act as a forfait at exactly that figure. It also arrives **`unitCostLocked: true`** — without
the lock, `applyProcedure` re-prices from the catalogue the moment the card is next touched, and the
negotiated figure vanishes at the one moment nobody is looking at the number.

⚠️ **The RDV price wins and the devis is untouched.** An act booked from a treatment plan seeds its price from
the plan step's `plannedCost` (so the visit is booked at what the patient was quoted, not at a tarif the devis
may have discounted away from), and editing it there changes **this visit only**. There is no write-back: a
discount agreed on the telephone must not rewrite a quote the patient may have signed. The consequence is
accepted deliberately — one act can carry two figures, the plan's and the visit's — because the alternative is
a booking dialog that edits an accepted document.

### The trap this feature is mostly made of

**The fiche does not read the appointment's act rows for pricing.** It takes each row's `procedureTypeId`,
resolves it back to the **catalogue**, and prices from `defaultCost`. So adding the column and the input would
have shown the negotiated price in the booking dialog and **silently reverted to the tarif in the fiche** —
worse than not shipping the feature, because the dentist has been handed a number to trust.

There are **two** prefill sites and they are reached by different code paths, which is exactly how one gets
fixed and the other does not (`fixes-dont-propagate`, the repo's dominant defect shape):

- `applyAppointment` — the lead act, proposed when the fiche opens. It now reads the agreed price from the
  appointment's own **row** (matched on `procedureTypeId` in sequence order), never from the catalogue entry.
- `addFromProcedure` — the « aussi prévu à ce rendez-vous » shortcuts. `otherBookedActs` used to resolve each
  booked row straight to a `ProcedureTypeDto` and **throw the row away**; it now carries `{ row, procedure }`
  so the price travels, and the chip states it (« Détartrage · 0,000 DT ») because the point is that this act
  is not at its tarif and the dentist should see that before tapping « Ajouter ».

`check:responsive`'s **`agreed-cost-reaches-the-fiche`** (N14) holds both, keyed on the *dispatch* rather than
on a file list so a third prefill site is caught the same way. Red-proofed: removing `agreedCost` from one of
the two dispatches fails the gate at the offending line.

### The other trap: a save that sends the acts sends the prices

`SetProcedures` **replaces** the whole list, so on the wire `procedures` is tri-state — omit it to leave the
acts alone, send it to mean « these acts at these prices ». Sending the list *without* the prices is therefore
how a reschedule, a changed note or any other unrelated edit would restore every act to its catalogue tarif
without saying so. That is why the edit dialog hydrates a stored price as **typed** (`agreedCost` set, not
`undefined`), and why both dialogs build `procedures` through the single shared `toProcedurePayloads` rather
than each assembling the array inline. Verified on the wire: a duration change from 60 to 90 minutes posts
`agreedCost: 45.5` / `agreedCost: 0` alongside it, and the row keeps both.

### What came free, and what came with it

The fiche's act card already had a tariff-difference line, so a negotiated act **announces itself** with no
new code: « Tarif catalogue 60,000 DT — geste de 14,500 DT · remettre au tarif ». That is the affordance the
feature would otherwise have needed, and it was already there for the hand-typed case.

Two things were added because the walk showed they were missing:

- **The picker's existing « Coût » field is now « Tarif au catalogue ».** That field creates a *catalogue* act
  at that price — permanent, seeding every future visit — and it sits a thumb's width from a field that
  changes one appointment. Two money inputs in one panel, one local and one permanent, is a mistake nobody
  would notice making.
- **The récapitulatif states « Prix convenu ».** The pane exists to be checked before committing, and money is
  now part of what is being committed. It appears **only when something was negotiated** (null otherwise) —
  a figure stated on every ordinary booking is a figure nobody reads by the second week — and it is in **both**
  renderings, the `rail` and the `bar`, because below `lg:` the strip *is* the récapitulatif and the phone is
  where the call that sets the price is taken.

### Adjacent defect fixed in the same change

`ProcedureTypeRefusals`' own docstring claims the money ceiling « was missing from **both** » create and
update and had been put right. It was on **update only**. Creating a procedure type at
999 999 999 999 999 999 was accepted by the handler, refused by PostgreSQL, and reached a French-speaking
dentist as an English EF sentence — the exact failure that class was written for. The guard this feature reuses
for `AgreedCost` is the same constant, so the hole was directly in the way.

### Verified

API build 0 · **3865/3865** unit tests · `tsc --noEmit` 0 · `check:responsive` **26/26** ·
`verify-schema` before and after the migration: the same four pre-existing dev-database drifts, none added.

Browser walk, signed in: a two-act séance booked at a negotiated 45,500 DT and an offered 0 DT stores
`45.500` / `0.000` while every pre-existing row stays **NULL**; an untouched act posts `agreedCost: null`
rather than its tarif; reopening the visit hydrates both prices as typed and a duration change re-sends them;
the fiche opens the lead act at **45,500 DT as a forfait** with the « geste de 14,500 DT » line, and the
« Détartrage · 0,000 DT » chip adds the second act at 0. Eye pass at 320 · 390 · 1440: the price line wraps
within its act card, no horizontal overflow, and the recap's meta line fits 320 px un-truncated (258/258 px).

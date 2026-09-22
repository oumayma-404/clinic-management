# Patient phone numbers — the country comes back, and a patient may have several

Two things shipped together because they are the same complaint, reported in one breath on 2026-09-21:

> « il a essayé de changer le numéro du patient, l'indicatif revient toujours au tunisien ; quand je
> réédite le patient, je trouve +216 — et le client veut pouvoir ajouter plusieurs numéros de patient »

---

## Part 1 — A stored number re-opens on the country its WRITER chose

### What was broken

Pick « France », type `06 12 34 56 78`, save. Reopen the patient: the selector reads **🇹🇳 +216**.

The write path was correct — `PhoneNumber.PersistedE164` held `+33612345678`, exactly as the
2026-09-11 fix intended. The **read-back** path was not. Both forms seeded the selector with

```ts
regionOf(patient.phoneNumber) ?? DEFAULT_REGION
```

and `regionOf` with no region re-derives against Tunisia. `0612345678` is not a valid Tunisian number, so
it answered `null`, so the fallback fired and the control said Tunisia. The country the writer chose was
sitting on the DTO the whole time, in `phoneE164`, and nothing read it.

⚠️ **The expensive half is what happens next, and nobody reported it because it looks like a different
bug.** With the selector back on Tunisia, the form's own pre-check reads the number against Tunisia too:

```ts
if (phone.trim() && !isDeliverablePhone(phone.trim(), phoneCountry))   // phoneCountry is now TN
```

So the **next ordinary save of that patient is refused** — « Numéro de téléphone invalide. Choisissez le
pays, ou saisissez le numéro au format international (+33…). » — about a field nobody touched, on an edit
that was about the address or the allergies. The only way out is to retype the number in `+33…` form,
which is precisely the workaround that made the original 2026-09-11 defect « look half-alive ».

### The fix

`storedPhoneCountry(e164, raw)` in `web/lib/phone.ts` is the one owner of the E.164-first order:

```ts
regionOf(e164) ?? regionOf(raw) ?? DEFAULT_REGION
```

`raw` is the legacy fallback and must stay — rows written before the column existed have no `phoneE164`,
and neither does a value no country can parse (« 71 555 (bureau) »). Both null ⇒ Tunisia, which is
exactly what those rows already showed, so no stored patient changes meaning.

Two call sites: `edit-patient-dialog.tsx` and `suppliers/supplier-form-dialog.tsx`. Both had it.

⚠️ **`ui/phone-field.tsx` is the one deliberate exception and must stay that way.** Its AC-11 effect reads
the **live** value so a pasted `+33…` moves the selector while you type. That is a different question —
« what is being typed? » rather than « what did the writer choose? » — and only the stored E.164 can answer
the second.

### Why the existing guards were all green

The same reason they were green in September. `PhoneRuleCorpusTests` and
`check:responsive`'s `phone-rule-matches-the-corpus` both drive `shared/phone-e164-corpus.json`, which
already contains `06 12 34 56 78` + `FR` ⇒ `+33612345678`. **A corpus pins a function.** Nothing pinned
that the callers hand it the right inputs — in September that was the callers not passing a region, and
here it was a caller passing the wrong value to read it back with.

`check:responsive`'s **N43** `stored-phone-country-has-one-owner` is the derived guard: it fails on
`regionOf(` in any `.tsx`, with `lib/phone.ts` and `ui/phone-field.tsx` as the two named exceptions, and
carries a non-vacuity tripwire on both ends (the owner existing, and at least one `.tsx` calling it).
Verified to go red by reverting the dialog to the old expression.

---

## Part 2 — A patient may carry several numbers

### Shape, and what was deliberately NOT done

`Patient.PhoneNumber` **stays the primary and is untouched.** It is what the reminder engine dispatches
to, what `PatientDuplicateIndex` folds, what the CSV import and export carry, and what ~200 files read.
Turning it into « the first row of a list » would have moved all of that at once for no clinical gain: a
practice still has one number it calls first, and « quel numéro appelle-t-on ? » must keep one answer.

So the extras are an **owned collection** on the aggregate:

| | |
|---|---|
| Table | `PatientPhoneNumbers` (`PhoneNumber`, `PhoneNumberE164`, `SortOrder`, `PatientId`) |
| Domain | `PatientPhone` value type + `Patient.AdditionalPhoneNumbers` / `SetAdditionalPhoneNumbers` |
| Cap | `Patient.MaxAdditionalPhoneNumbers` = 5 |
| Wire, read | `PatientDto.AdditionalPhones` → `{ value, e164 }` |
| Wire, write | `AdditionalPhones` → `{ value, region }`, **tri-state** on update |

⚠️ **Owned, not an entity, and that is the load-bearing choice.** EF loads an owned collection with its
owner on every read, so there is no `Include` to forget. A plain entity collection would have needed one
on each of `PatientRepository`'s reads, and the forgotten one would have saved the patient with an *empty*
list — an unloaded navigation is empty, not stale, which is the silent-and-confident failure
`RecoveryCodeLoadingCoverageTests` exists for.

⚠️ **`PatientPhone.E164` is non-nullable**, unlike `PhoneNumber.E164`. That one is nullable because it was
added to rows that already existed and re-derives for them; this table is new, every row goes through the
constructor, and the constructor refuses a number no country can parse. So every reader can dial a row of
this table with no fallback — and nothing here restates `PersistedE164 ?? ToE164(Value)`.

⚠️ **Each row carries its OWN region.** A patient's mobile may be Tunisian and their son's French; one
`phoneRegion` for the whole record would be wrong in exactly the case this feature exists for.

### The trap this feature IS

`SetAdditionalPhoneNumbers` **replaces the whole list** — the `DentalRecord.SetActs` shape that has cost
this codebase three fields (`PonticToothNumbers`, `ImplantPilierToothNumbers`, `IsUnfinished`). The only
thing between it and « reopening a fiche to fix a typo silently deleted three numbers » is the command's
tri-state: an omitted key never reaches the setter, so the calendar-import review save, a partial PATCH
and every future surface leave the numbers alone. `[]` is how the last one is deleted.

Both halves are wired in the dialog and both are tested:
`An_Update_That_Does_Not_Mention_Them_Leaves_Them_Alone` is the most important test in
`PatientAdditionalPhonesTests`, not the least, and the browser walk's **B6** exercises the same thing
through the product (edit the motif de consultation, save, reopen, both numbers still there).

### Decisions worth knowing

- **Search finds them.** `PatientRepository.ApplySearch` gained an `EXISTS` over the collection, matching
  both the typed form and the E.164 — the number on the caller ID is as often the second one as the
  first, and a search that only knows the primary sends reception to « aucun résultat » for a patient
  whose file holds the very number they typed. Still one SQL statement, still narrowing before the page
  is cut.
- **A blank row is dropped, never refused.** « Ajouter un numéro » appends one and a user who changes
  their mind leaves it there; refusing the save over it would make the button a trap. A row with
  *something* in it must be a real number — the primary's rule, per row.
- **Duplicates are dropped**, compared on E.164 so « 20 123 456 » and « +216 20 123 456 » are one number,
  and compared against the **new** primary when a request changes both (which is why the additional-phones
  block sits *after* the contact block in the update handler).
- **There is no « libellé » field, and it was built and then withdrawn before it shipped.** The first cut gave
  each row a free-text label (Mobile / Domicile / Époux), with a `datalist` of six suggestions, a 40-character
  column and its own tests. The owner's answer on seeing it was « just add second number that's ittt » — the ask
  is a second *number*, and a second box beside every number is a second thing to fill in on the commonest form
  in the product. ⚠️ **The column went with it**, in a re-scaffolded migration rather than left in place: a field
  the API serves and no screen can reach is the shape `features/cnam-ui-withdrawal` exists to warn about, and
  leaving it would have invited the next author to wire a UI back onto a decision that had been made.
- **Reminders still go to the primary only.** Fanning them out changes the consent story and the
  WhatsApp quota, which is a different decision from « let the practice record a second number ».
- **CSV import/export is unchanged.** It is an operator-facing published contract; widening it is its own
  decision. An imported patient simply has no extra numbers.
- **Nothing is rendered until the practice asks for it.** Almost every patient has one number, so the
  form shows a button and a help line, and rows only after a press.

### Where they are read

- The patient page's **header strip**, right behind the first number — that strip is what somebody reads
  when they are about to telephone, and a second number filed three cards down is a number nobody finds
  at the moment it is needed. `tel:` uses `extra.e164`, never the stored value (spaces, no country code).
- The **record card**, as « Autres numéros », `omitWhenEmpty` like « E-mail » and « Adresse » beside it.

### Where the control sits, and why it moved

Directly under « Téléphone », left-aligned, as the row after the phone/sexe/naissance grid.

⚠️ **`justify-between` was the first cut and was reported on sight.** It pushed « Ajouter un numéro » to the
right edge of a full-width row — diagonally opposite the « Téléphone » field it belongs to, with « Sexe » and
« Naissance » sitting between them — and the owner's reaction was « why is the autres numéros button so far
from the original number field ». Left-aligned, the label and the button land under the phone field's own
column; `-mt-2` closes the grid's `gap-4` so the two read as one block.

⚠️ It is still **below** the grid rather than inside the « Téléphone » cell: a variable-length list in one cell
of a three-column row grows that column and drags « Sexe » and « Naissance » down with it, and at 320 px the
cell is the whole width anyway.

⚠️ The help line is **always on its own row**, never appended to the label inline. Measured at 320 and 390 px:
beside the button the label had ~110 px left, so an inline caption broke « Autres / numéros » across two lines
and wrapped itself over two more.

---

## Verified

| Gate | Result |
|---|---|
| `dotnet test` (unfiltered) | 4743 passed, 0 failed, 6 skipped — including the new `PatientAdditionalPhonesTests` |
| `verify-schema` before/after the migration | one drift resolved (the new index reported MISSING → applied), **zero new** |
| `tsc --noEmit` · `check:responsive` (71) · `npm run build` | all green |
| Browser walk, one pass | 20 checks: the reported bug, the round-trip, the tri-state, delete, search, the patient page, the fournisseur round-trip, 320/390/820/1440 |

⚠️ **Three of the walk's « failures » were the probe**, and each is worth knowing before writing another:

| Symptom | Actual cause |
|---|---|
| « no Modifier button on the patient page » | Below `sm:` it is `hidden` by design; the action is in « Actions ▾ » |
| « no edit control on a fournisseur row » | The table row's label is an `sr-only` span; a bare `:has-text("Modifier")` finds the hidden phone-card one |
| « Ajouter un numéro is 32 px on a coarse pointer » | `.touch-target` is an `::after` overlay — the painted box is *meant* to stay 32 px. Hit-tested: 5/5 corners of the required 44×44, `::after` min 44×44 |

The two **real** eye-pass findings were both about placement and both are recorded above: the help caption
crowding the label at 320 px, and the button sitting at the far right of its row instead of under the phone
field. The walk was re-run in full after each.

⚠️ **The migration was re-scaffolded twice**, and the first one was a mistake worth recording: `--output-dir
Persistence/Migrations` put it in a **new folder** while every other migration lives in
`Infrastructure/Migrations`. EF found it anyway (it scans the assembly, not the folder) and it applied
cleanly — but `MigrationSqlAggregateTests` scans that directory by path, so the guard never saw it. Moved,
then re-scaffolded from scratch when the `Label` column was withdrawn.

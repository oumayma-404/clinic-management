# unfinished-act-continuation — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
`spec.md` is what was asked for; this is what shipped — including the two places it **departs** from the spec
and why.

## Un acte peut être noté « non terminé », et ce qui reste apparaît quelque part

A dentist ticks « Acte non terminé » on an act of the fiche de soins. That act then appears in
**« Suites à planifier »**, a third tab on `/a-cloturer`, until a devis picks it up — and one press from that
row opens the ordinary booking dialog with the continuation already chosen.

**`DentalRecordAct.IsUnfinished`** (`bool NOT NULL DEFAULT false`) is the whole of the new data. It is the one
fact nothing in this product can derive: a fiche records what was *carried out* and says nothing whatever
about what remains, which is why `GetContinuableActsQuery` has always had to offer **every** act of the last
120 days and make the dentist recognise the right one. This is that missing half, and it is only ever set by a
human.

⚠️ **It states money and moves none.** An act billed 1 000 with 800 collected still owes 200 *on its note*,
where la caisse, « Créances » and « Solde patient » already carry it. Every surface here says which document
collects; not one writes a figure. `ContinueRecordedActCommand` is untouched by this feature.

## The two departures from `spec.md`

- **The checkbox is on the armed card body, not inside the « Détails » fold.** The spec's Device Behaviour put
  it in the fold. That fold is shut by default and summarised in one truncated line, and the entire value of
  this feature is that somebody actually ticks the box — a control the dentist has to go looking for is one the
  worklist behind it never hears from. It sits after the teeth, which is the end of the act's clinical entry
  and the moment « est-ce fini ? » is answerable.
- **There is no booking-dialog notice (spec AC-4…AC-7), and a clinic-wide worklist instead.** The spec raised a
  dismissible notice in the booking dialog naming the patient's most recent unfinished act. That answers the
  question only for a patient somebody is *already* booking; the case the client described is the séance
  nobody has come back for. The worklist answers both — the row is a door into the same booking dialog with
  the same `ContinuationChoice` — and it does not add a fourth notice to a dialog that already arbitrates
  `PlanStepSuggestionNotice` against the continuation link (spec AC-5 exists precisely because notices there
  are already contended). The in-dialog door (« C'est la suite d'une séance précédente ? ») is unchanged and
  now sorts ticked acts to the top, which is what the notice was really for.

## ⚠️ « Suites à planifier » is NOT a fourth question in « À clôturer », and that was the main design call

The client's own phrasing was « a clôturer list becomes: came? fiche added? act completed? paid? ». It is a tab
on that page instead, and the three reasons are worth keeping:

1. **The cascade cannot ask a question whose default is already an answer.** Every step in `VisitClosureRules`
   is open because a record is *absent*. « L'acte est-il terminé ? » defaults to `false` = finished, so an
   untouched fiche raises nothing. Making it a real gate needs a tri-state, which means every fiche ever saved
   and every one from now on acquires a new mandatory answer — the « alarm that is always on » that feature's
   own notes warn against.
2. **It confuses the visit with the treatment.** A séance holding an unfinished act is *completely closed as a
   visit*: the patient came, the fiche is recorded, the money is settled. What is open is the treatment. In the
   same cascade the row could never be cleared by answering anything about the visit, and the page's count —
   and the dashboard chip behind it, which shares `VisitClosureReader` — would start counting a different
   thing.
3. **The client's ordering inverts a money rule.** « act completed? » *before* « paid? » would hold a séance
   open for billing until the next séance happens, while billing is settled per séance and completion is per
   act across séances.

`VisitClosureReader`, `VisitClosureRules` and the dashboard chip are **untouched**.

## The rule that is easy to get backwards

> The tick says what the dentist **saw**. « Is this act already being continued? » is a different question with
> a different owner.

`IsUnfinished` is **never cleared automatically**, not even once a continuation devis picks the fiche up: it
records a clinical observation, and un-ticking it behind the dentist's back would rewrite the record to match a
booking. So every list that offers a continuation asks **`ContinuationTracking`**, never the flag — otherwise
the worklist keeps chasing work already on a devis and its own button mints a second devis over the same act,
the state `ContinueRecordedActCommand` refuses on the press.

⚠️ **And the mirror rule, which is the opposite on the other reader.** In
`GetContinuableActsQuery` (the booking dialog's list) the flag is a **sort** and must never become a filter:
it is one checkbox at the end of a séance and it is the easiest thing in the product to forget, so filtering
on it would turn the one gesture that helps into the one you cannot recover from having skipped. Ticked acts
rise to the top; every other recent act stays exactly where it was. Held by `ContinuableActOrderingTests`.

## ⚠️ The trap this feature walked into, and the guard that now holds it

`DentalRecord.SetActs` replaces the **whole** act list on every save, so the fiche's editor is the only thing
keeping a stored act field alive. Read it back and forget to send it — or send it and forget to read it back —
and an ordinary re-save silently rewrites the act. This has now cost three fields: `ponticToothNumbers`
(flattens a bridge into three abutments and no pontic, a mouth that cannot exist), `implantPilierToothNumbers`
(charts rooted abutments over implants), and this one — which marks an act **finished**, a clinical claim
nobody made, whose only symptom is the act quietly leaving « Suites à planifier » so the séance nobody booked
is chased by nothing.

`check:responsive`'s **N36 `act-field-survives-a-re-save`** derives the required set from
`DentalRecordActDto`'s own declaration — so a fourth field is covered the day it is declared — and asserts it
in **both** directions across `actFromDto` and the modal's payload. `id` is the only exemption and it is
computed, not listed. Proven red in both directions before being trusted.

Verified end to end in the browser, not only by the guard: ticked → saved → present in Postgres → reopened
**still ticked** → re-saved untouched → still `true`.

## Where things live

| | |
|---|---|
| The flag | `DentalRecordAct.IsUnfinished` · `DentalRecordActInput.IsUnfinished` (optional, **last**) |
| Migration | `20260911161617_AddDentalRecordActIsUnfinished` — one `AddColumn<bool>`, **no backfill** |
| Wire | `DentalRecordActDto.IsUnfinished` (read back) · `DentalActInput.IsUnfinished` (sent) · `ContinuableActDto.IsUnfinished` |
| The worklist read | `GetUnfinishedActsQuery` + `UnfinishedActDto`, under `Features/TreatmentPlans` |
| Endpoint | `GET /api/treatment-plans/unfinished-acts` (paged, `AnyClinicRole`) |
| Repository | `IDentalRecordRepository.GetWithUnfinishedActsAsync` · `ITreatmentPlanRepository.GetByLinkedDentalRecordsAsync` |
| The shared window | `ContinuationTracking.LookbackDays` (120) — both readers, so the worklist cannot chase an act the dialog would no longer offer |
| UI | `record/act-card.tsx` (checkbox + card-face chip) · `visits/unfinished-acts-list.tsx` · `app/a-cloturer/page.tsx` (third tab) · `create-appointment-dialog.tsx` (`presetContinuation`) |
| Guards | `UnfinishedActWorklistTests` · `UnfinishedActRoundTripTests` · `ContinuableActOrderingTests` · `check:responsive` N36 |

⚠️ **`GetUnfinishedActsQuery` lives under `Features/TreatmentPlans`**, like « Traitements en cours » and for
the same mechanical reason: `RealtimeResourceResolver` derives the broadcast key from the namespace, so a
folder of its own would emit a key `clinic-hub.ts` does not declare and `RealtimeResourceResolverTests` would
fail the build in both directions.

⚠️ **`presetContinuation` is a pre-FILLER, never a second writer.** It is the same `ContinuationChoice` the
in-dialog `ContinueSessionDialog` hands back and goes through the same seeding, so the devis is still minted by
`materialiseTreatments` when the booking is **saved**. One materialiser, not two — a door that created the plan
itself would re-open the defect the in-dialog door already paid for, where pressing « Annuler » on the booking
left a numbered, accepted devis for a séance nobody booked.

⚠️ **The row says « un rendez-vous est déjà prévu », never « cet acte est planifié ».** Nothing links a booking
to an act with no treatment behind it — which is exactly what these acts are — so the booking line is **per
patient**. Claiming the stronger of the two is how a worklist starts lying.

## ⚠️ Empty on the day it ships, and that is correct

Nothing is backfilled, because the flag is not derivable: any backfill would be a guess written into the
clinical record, and a wrong guess reads as a dentist's own observation for ever. « Suites à planifier » fills
as séances are charted. **Do not repair it by inference.**

## A device regression this feature caused, and fixed

Adding a **third** tab to `/a-cloturer` made the two that already shipped clip. `TabsTrigger` is
`whitespace-nowrap` and `flex-1` is `flex: 1 1 0%` — a zero basis, so the triggers can never trigger a wrap and
simply divide whatever is there. Measured at 320 px: three triggers took **86 px** each out of a 273 px strip
while « Séances 281 » needed 94 and « À compléter 0 » needed 98, so both were cut mid-word. `flex-wrap` plus a
real `basis-28` is the fix: two tabs on the first row, the third below, nothing clipped — and it survives a
fourth tab and a longer label without being re-measured.

## Known, pre-existing, not from this work

At 320 px the fiche modal's **« Prescription »** `RecordSection` overflows its box (330 px in a 255 px card):
its summary line is a `truncate` span whose min-content is the whole string
(« Amlor Gélule 5 mg · doliprane 1000 mg · Radiographie panoramique dentaire », 382 px). That is the
`RecordSection` grid min-content trap the root `CLAUDE.md` already documents, on the *summary* rather than on
the add-buttons row that `404f5b42` fixed. Measured with the act card at 255/255 with **and without** the new
checkbox, so it is not caused by this feature.

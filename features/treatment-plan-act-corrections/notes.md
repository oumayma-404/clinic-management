# Un acte d'un devis se corrige et se retire, et le rendez-vous suit

What shipped, and the decisions that are easy to undo by accident. Reported from use: a dentist planned three
acts, recorded the fiche of the first, booked the second, changed his mind — and could change the act but not
remove it, while changing it left the old price behind.

---

## La seule chose qui empêche de retirer un acte, c'est une fiche de soins

`TreatmentPlan.RemoveItem` refused two things: a réalisé act, **and an act the patient was still booked for**.
The second refusal is gone.

It was not wrong about the consequence — removing a booked act leaves an appointment row pointing at an id
that no longer exists, with the patient still expected and the reminders already sent, and there is no FK to
catch it. It was wrong about the **remedy**: it sent the dentist out of the devis, into the agenda, to cancel
a visit, and back again, for the ordinary case of changing one's mind about work that has not started.
« Whether to un-book the patient or repurpose the slot is a phone call, not a cascade » — that sentence is
now false, and deliberately so, because the cascade is the caller's and the caller can do it properly.

So `TreatmentPlanItem.HasDeliveredWork` is the **single** refusal, in two shapes: a step-less `Done` act has no
séance count to quote, so « a déjà 0 séance(s) réalisée(s) » would be the sentence. Both messages now point at
**« Détacher la fiche »**, which is the control that clears the refusal.

⚠️ **`EnsureItemRemovable` exists so every refusal is raised BEFORE the first booking is touched.** A
rendez-vous cancelled for an act that then turns out to be un-removable is a phone call nobody can un-make.
`RemoveItem` is that method plus the removal.

## Le rendez-vous est réglé par le handler, et par MediatR

`AmendTreatmentPlanCommandHandler.ReleaseBookingsAsync`, per act being removed:

| The visit | What happens |
|---|---|
| carries other procedure rows too | `SetProcedures(kept)` — the séance stands, it just no longer covers this act |
| carries nothing else | cancelled |
| is `Completed` | **untouched** — it happened; only its link to a removed act is untrue, and the derivation runs act → appointment, never the reverse |
| is already cancelled / a no-show | untouched; it books nothing |

⚠️ **Decided per PROCEDURE ROW, never per plan link.** A séance may legitimately carry a devis act *and* a
walk-in détartrage, and that visit still has a reason to happen once the devis act goes.

⚠️ **`AgreedCost` rides along on every kept row.** `SetProcedures` replaces the whole list, so a row re-sent
without its price silently reverts to the catalogue tarif — the repo's own documented trap, on a new caller.

⚠️ **The cancellation goes through `UpdateAppointmentCommand` over MediatR, not `appointment.Cancel()`.**
Cancelling a visit is five things — the staff notification, the post-visit review row, the unsent SMS/WhatsApp
reminders, the Google Calendar event and the realtime broadcast — and all five live in that handler. A
hand-rolled cancel here would be a sixth writer that forgets every one of them.

⚠️ **It is sent AFTER the plan's own `SaveChangesAsync`**, and the order is forced: that handler calls
`SaveChangesAsync` on the same scoped `DbContext`, so sending it earlier would commit the plan's half-applied
edits *without* the expected-version check. The consequence is that the two are not atomic — a failed cancel
leaves the act removed and the slot booked, which is the recoverable direction and is near-impossible because
every refusal was raised first.

## « Cet acte est-il retirable ? » n'est PAS `planItemState`

`actRemovalPlan` (`plan-next-action.ts`) is the browser mirror of `EnsureItemRemovable`, term for term, and
it also reports the booking so the confirmation can name it.

The modal used to derive removability from `planItemState`, which answers for the act's **next step** —
deliberately, so the badge says what to do next. Reading it as « may this be removed? » was wrong in three
directions at once, none of which errors anywhere:

- a rendez-vous that had already **passed** disabled the bin with « un rendez-vous est prévu », on a removal
  the server allows;
- a bridge with **one séance delivered** and the next unbooked **enabled** the bin, on one the server refuses;
- a séance booked **out of protocol order** did the same.

`plan-next-action.ts` already carried that exact warning for `hasDeliveredWork`, one export above. The lesson
was never carried to the modal. `check:responsive`'s **N38** is what stops a second surface re-deriving it.

## Changer d'acte n'est pas corriger son nom

A line loaded from an existing devis arrives `costTouched` **and** `stepsTouched`, and rightly: a stored fee is
the number agreed with the patient and a stored protocol is somebody's decision, so re-picking the *same* act
to fix a typo must leave both alone.

But swapping the act for a **different** one makes that fee and that protocol the *previous* act's.
`selectProcedureType` now clears both when `pt.id !== line.procedureTypeId` — and only then. Same rule on the
« Traitements proposés » chips, which swapped the act and never touched the protocol at all.

⚠️ **The séances follow only when no séance of that act is done.** `SetSteps` refuses dropping a séance
already carried out, so re-proposing the catalogue's list over a part-done act would make the save fail
outright.

## « Modifier » est sur la ligne de l'acte

The amend dialog had exactly one door: a « Modifier les actes et les prix » item inside the header's « ⋯ »
menu. A dentist looking at the act they wanted to change did not find it.

`PlanActEditAction` is on every act row and every card, and it carries `focusItemId` so the modal opens with
**that** line scrolled to and ringed — without it, « Modifier » on the third act opens a dialog showing the
first, which on a six-act devis is the same « where is it? ».

⚠️ Its `aria-label` ends « — désignation, honoraires, dents », and that suffix is load-bearing for anything
driving the page: `PlanActStepsAction` sits immediately before it with an `aria-label` **starting** « Modifier
les N séances de … », so a prefix match opens the wrong dialog.

## Le sélecteur d'acte du devis est celui du rendez-vous

Grouped by discipline (the shared `groupProceduresByCategory`), a colour dot, the price, and an
**« N séances » pill** — which is the half that matters: the flat list gave a dentist no way to know that
picking « Implant dentaire » proposes six visits and opens a devis, and they learned it *after* the pick,
which is the wrong order on a document a patient signs.

⚠️ The price is shown here and the duration in the booking picker, deliberately: a devis is about money and a
booking is about the diary. The two are **not** one component — they also differ on the « Actes du devis »
group and on multi-select — but the grouping is shared, which is the part that could drift.

## Ce qui a été mesuré

`qa/run-1.md` — 26 browser scenarios in one launch, GREEN, at 320 / 820 / 1440 / 1536×730. Two observations
recorded there are **pre-existing and not from this work**: a reopen+save of the amend modal bumps
`revisionNumber` (the échéancier is always resent, so the « Aucune modification demandée. » guard cannot
fire), and an `AgreedCost` sent over the API came back 0.

⚠️ Worth knowing before the next browser pass, and now in the skill: a `next dev` left up for days loses its
compile workers and serves **every dynamic route as an empty document**, while the static ones are fine — one
here had been up three days and failed 11 scenarios against a healthy product.

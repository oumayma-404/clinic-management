# Multi-séance treatments — the feature, from the ask to now

> An act that cannot be done in one sitting is now plannable, bookable, recordable and correctable — without
> charging the patient twice. This file is the **story**; the traps live in the code's own comments and in the
> guides listed at the end.

## The ask

A dentist told the owner two things:

1. An implant or a bridge is **never one session**. The product had one act called « Implant dentaire » and no way
   to say that it happens over six visits.
2. And the blocker he actually hit: *he booked a bridge at 1 000 DT, the patient paid 800, the work was not
   finished, and he had no idea how to plan the next session without charging again.*

Everything below follows from those two sentences.

---

## 1 — The model

A devis act (`TreatmentPlanItem`) gained **steps** (`TreatmentPlanItemStep`): a label, an estimated duration, a
done-date, and **its own link to the fiche de soins that evidences it**. The act's status is derived from its
steps, and `nextStepId` says which one it is waiting on.

The load-bearing detail: an appointment's act row carries **`treatmentPlanItemStepId` beside
`treatmentPlanItemId`**, and the server keys its duplicate rules on the **pair**. That is the whole reason
« préparation + empreinte dans la même séance » is expressible — without it the two steps arrive as the same act
twice and the server refuses the booking the feature exists for.

⚠️ An act with **no** steps renders and behaves exactly as it always did. That is most acts, and it is the property
the whole migration's safety rests on.

## 2 — The screens

| Surface | What it does |
|---|---|
| `PlanStepStrip` | « ✓ Préparation — ✓ Empreinte — ○ Scellement · 2 / 3 » under the act's own name on the devis |
| `plan-item-steps-dialog` | Cut an act into séances. A **done** step is read-only with « réalisée le … » and points at « Détacher » to reopen it — it holds the link to the fiche that attests it |
| « Traitements en cours » | Every act the cabinet has **started and not finished**, with the étape that remains and whether a séance is booked. Derived per request, stored nowhere, so it cannot drift from reality |

## 3 — The money, which was the real blocker

The fee belongs to the **act**, never to the séance.

- A séance of a stepped act adds **no honoraires**: the price field is read-only 0 with « facturé sur le devis »,
  and a notice names the devis, **this act's** fee and what is left on the devis as a whole — two figures, two
  scopes, each labelled with its own.
- The 800 collected and the 200 owed live on the **échéancier** and are collected at whichever séance.
- So the séance that finishes a 1 000 DT bridge charges nothing, and nobody has to remember why.

## 4 — Protocols in « Types de procédures »

`ProcedureType.DefaultSteps` was already in the schema **with no consumer at all** — a dentist had to type the
sub-steps of every bridge as free text, which is what prompted « this is completely unprofessional ».

- **14 protocols researched from clinical sources** (HAS 2018, ITI, Cochrane 2022, SFSCMFCO, Constantine 3, a
  Lille thesis, Cahiers de Prothèse). The research **corrected** an invented crown protocol and the
  « endo = 2 visits » reflex.
- **19 of the 33 starter acts correctly get none.** That is what keeps the feature invisible on an ordinary
  détartrage, and it is a finding, not an omission.
- A hand-written, guarded data migration filled **50 rows**; `verify-schema` gained
  `procedure-step-protocol-backfill` so a half-applied backfill is visible.
- **`TreatmentPlanStepProtocol`** applies a protocol when a devis is **accepted** — the missing consumer.

## 5 — Wiring it so nobody types anything

- **Devis form** — « Étapes proposées »: the protocol shown, ticked and editable *before* the devis is accepted.
  Tri-state, and all three states are real: absent means « use the catalogue », `[]` is an explicit « une seule
  séance », a list is the confirmed sequence. Unticking every step must not be helpfully re-applied.
- **Booking dialog** — picking an act with a protocol offers « **Créer le devis et planifier la 1re séance** » in
  one press. Nothing is created silently: a devis consumes a number and is a document the patient may be shown.
- **Edit dialog** — « Actes du devis » attaches a séance to a devis **afterwards**, for the visit booked from the
  agenda before anyone thought about a plan.

## 6 — Grouped séances

Two steps carried out in one visit are **two rows on the wire and one card** in the picker (`groupActs`) — rendered
straight from the wire list, one bridge became two identical cards each claiming to be both steps.

And **one fiche closes both**: the fiche derives every step of that séance from the appointment rather than closing
only the step it was opened with. Without that the second step of a grouped séance was unrecordable — a dead end.

## 7 — The later passes

- **One page.** « Traitements en cours » and « Plans de traitement » were two rail entries over the same acts,
  with nothing linking them. They are one screen now — the worklist leads, the devis list follows —
  and `/traitements-en-cours` redirects.
- **The app suggests the next step.** Booking for a patient with a live devis offers its next étape, with the fee
  stated as *already covered*. Withdrawn once any devis act is on the séance, and dismissible.
- **Retroactive continuation.** An act done as a one-off that turns out unfinished becomes a treatment:
  « C'est la suite d'une séance précédente ? ». One rule governs the money — **the devis owns only what has not
  been billed yet** — so an already-invoiced act keeps its money on the note and the devis never re-charges it.
- **The protocol became visible.** On « Types de procédures » the steps were a grey run-on sentence under the act's
  name; they are a count, the chair time and numbered chips now, and the cell is the button that opens the editor
  (which gained reordering). « Description » and « Consommables » stopped being columns.
- **Nothing is locked.** Fees, acts and the échéancier are correctable on **billed and completed** plans; the
  divergence with a note is *stated* and points at an avoir instead of refusing the edit. And
  « **Arrêter le traitement** » handles the patient who stops halfway: drop what has not started, keep what was
  done, re-spread the échéancier, close the plan.

---

## What the verification actually found

Roughly **25 real defects**, nearly all caught by the eye pass or by driving the real API — not by a mechanical
gate. The expensive ones were money:

- « Déjà facturé » quoted the **devis total** as a single act's fee
- « Prix convenu 0,000 DT » on a séance inside a four-figure treatment
- « Reste à encaisser sur le devis : 120,000 DT » on a séance whose real balance was **15,50 DT**
- a booking the server would have **refused outright**, because the act carried a devis link with no
  `treatmentPlanId` beside it
- a « stop the treatment » schedule that kept a paid échéance at face value, so a 350 DT row holding a 50 DT
  deposit produced a 350 DT schedule against a 120 DT total — refused, on the most likely case of all

Plus, incidentally: **three tests that went red for one hour every night**, because they asked UTC what day it was
while production correctly asked `ClinicClock`.

## Guards

Five derived checks hold the parts that fail silently:

| Guard | Holds |
|---|---|
| `plan-step-travels-with-the-act` (N16) | a booked step reaches the wire and survives a re-save |
| `devis-act-carries-its-plan-id` (N17) | a surface offering devis acts resolves the appointment's own `treatmentPlanId` |
| `StepProtocolCoverageTests` | the catalogue's protocols stay consistent with the seed |
| `GroupedStepFicheTests` | one fiche closes every step of its séance (red-proofed) |
| `procedure-step-protocol-backfill` (`verify-schema`) | the migration reached every seeded act |

## Still open

- The published design canvas for the « Types de procédures » redesign still shows the **separate-column**
  version; what shipped keeps the steps in the name column (the table was already overflowing).
- An earlier design artboard says an act's status is *derived*; it shipped **stored**.

## Where the detail lives

- `web/CLAUDE.md` — the routes, and why each one is shaped as it is
- `web/components/CLAUDE.md` — every component named above
- `api/ClinicManagement.Application/CLAUDE.md` — the commands, the queries and the amendment window
- `api/ClinicManagement.Domain/CLAUDE.md` — the entities and `Invoice.AttachToTreatmentPlan`
- The code's own comments — every ⚠️ in the files above records a decision that is easy to undo by accident

# Multi-séance treatments — functional review

**Status:** COMPLETE for the money, correction, discoverability, catalogue and device lanes.
Two items still outstanding, both named in *Still open* at the foot of this file.
**Date:** 2026-09-04
**Scope:** functional / UX / money. **Not** a code review — every finding below is something a dentist can see,
do, or be misled by.
**Judged against:** intuitive · easy · a burden day to day · understandable · set up in a hurry · fluid ·
correctable · money.

---

## Verdict

**Do not ship. One defect (A0) charges the patient the act's full fee again at every séance, on the default
path, with one click and no user error — and it was executed end to end during this review: a patient has now
paid 300,000 DT for a 150,000 DT act, marked settled in cash.** A six-visit implant at 1 500 DT would bill six
times. The duplicate note carries no plan link, so none of the de-duplication machinery that protects every
aggregate figure can see it.

That is not a reason to reconsider the design. The **model** is right, and it is the hard part: the fee belongs
to the act, a step carries no money at all (`TreatmentPlanItemStep` has no money field, deliberately), and one
fiche closes every step of its visit. The driving complaint — *"the patient paid 800, the work isn't finished,
how do I plan the next session without charging again"* — is genuinely solved, and the booking dialog states
the money better than anything else in the app.

What is unfinished is everything **around** that model. Four patterns account for almost every finding:

1. **The invariant lives in one client function.** *"A devis act is 0 for this séance"* is enforced only by
   `agreedCostOf` in the booking picker. The fiche (A0) and the edit dialog (A3) each rebuild the act without
   asking it, and the server has **no rule at all** — `AppointmentProcedureSelection` accepts any
   `AgreedCost ≥ 0` on a plan-linked row. Two of the three surfaces that price a devis act get it wrong.
2. **A step-aware helper wired to one call site.** `planItemState` was changed *by this feature* to answer for
   an act's **next step** rather than the act. Two callers were never re-read in that light, and one of them
   deletes delivered work (A1).
3. **The money *display* was fixed in one place out of seven.** The booking dialog is exemplary and even knows
   to withhold the devis balance when a note exists. The six surfaces that print « Reste » were not touched,
   and they are the ones a dentist reads first (A2).
4. **The retroactive path throws away everything the feature built.** The dentist who discovers mid-treatment
   that an act is unfinished — the original complaint — gets no protocol, a generic label, and no way to price
   the work that remains (A5).

Every one of the four is the same shape, and it is the shape this repo has recorded thirteen times before: a
correct, well-documented rule wired to one consumer. The durable fix is not six patches — it is to make
« a devis act carries no honoraires » an invariant in the domain, and to add derived checks for the display
readers.

---

## Evidence base

| Lane | What it did |
|---|---|
| Flow map | Every entry point, dialog and click path, from source, with file:line |
| Money trace | The fee end to end: creation → acceptance → booking → fiche → invoice → échéancier → stop |
| Correction matrix | 15 state-changing actions × reversible / how / refused-when / role / audit trace |
| Browser walks | 6 agents on separate logins, each on its own patients; plus my own read-only passes |

Findings are marked **[screen]** (observed in the running app), **[code]** (traced in source), or
**[screen+code]**. Every figure quoted was cross-checked against the database.

---

## Part A — Money

### A0 · CRITICAL · The fiche de soins charges the act's full fee **again at every séance** — executed, collected, no user error **[screen+SQL — money actually moved]**

**This is the finding. Everything else in this review is secondary to it.** The feature's central promise — *"a
séance of a stepped act adds no honoraires"* — is honoured by the booking dialog and thrown away by the screen
that actually creates money.

Sonia Trabelsi, devis **2026-0011**, « Traitement de canal », planned at 150,000. Note **2026-0061 already bills
that act at 150,000 and is fully Paid** — she owed **0,000 DT**. Her appointment carries `AgreedCost = 0.000`,
and the appointment was never re-opened or edited. Recording the 2nd séance's fiche, the form pre-filled itself:

- « Acte planifié → **2026-0011 · Traitement de canal (dévitalisation)** » — it *knows* this is a devis act
- « Actes de la séance — **1 acte · 150,000 DT** », ACTE 1 forfait **150,000 DT**
- « **Payé 150,000** · Mode **Espèces** · Total **150,000** · Reste à payer : 0,000 DT »
- Primary button: « **Confirmer la séance — 150,000 DT** »
- « Déjà facturé » / « facturé sur le devis »: **absent everywhere on the screen**

The mechanism is visible in the form itself. At the bottom sits « Prévu à ce rendez-vous, retiré de la fiche —
remettre : **+ Traitement de canal (dévitalisation) · 0,000 DT** ». **The act was loaded twice**: the correct
0,000 line from the appointment, demoted to a "you removed this, restore it?" chip — and a second 150,000 copy
priced from the catalogue as the *active, billed* line.

One click on the default button:

```
  Number  |Status| TotalTtc |AmountCollected|      TreatmentPlanId       |     Designation
2026-0061 |  3   | 150.000  |    150.000    | 92e7cc56-…5ed3193ee9ff     | Traitement de canal…
2026-0094 |  3   | 150.000  |    150.000    | NULL                       | Traitement de canal…
```

Sonia went from 8 notes / 2 545,000 collected to **9 notes / 2 695,000**. La caisse for 2026-09-04 went
**80,000 → 230,000**. **She has now paid 300,000 DT for a 150,000 DT act**, marked settled in cash. The devis
auto-completed in the same write.

Two things make it worse than a simple over-charge:

1. **The duplicate note carries `TreatmentPlanId = NULL`**, so none of the de-duplication machinery that
   protects every *aggregate* read can see it. The money is not double-counted by a stale figure — it is a
   second real, collected note, invisible as a duplicate to every downstream check.
2. **It is the default.** No mis-click, no re-opened appointment, no unusual state. A dentist recording any
   séance of a stepped act and pressing the primary button over-charges the patient once per séance. A
   six-visit implant at 1 500 DT would be charged **six times**.

**It fires on the FIRST séance too — independently observed on a second plan.** Séance 1 of 3 of a 1 000 DT
« Couronne / bridge »: the booking dialog said « Cette séance n'ajoute pas d'honoraires » and carried the act at
0,000 DT. The fiche opened from that same appointment showed **ACTE 1 — 1 000,000 DT**, « **Payé 1 000,000** »,
« Total 1 000,000 », « Reste à payer : 0,000 DT », and a submit button reading « **Confirmer la séance —
1 000 000 DT** » — with the appointment's real 0,000 DT act demoted to the same "restore this?" chip. A dentist
who confirms the first séance as presented takes the **whole crown fee on day one** while the échéancier still
claims the full 1 060 DT. (That reviewer cleared the field rather than confirming, so the prefill is what was
measured there; the execution above is what proves the outcome.)

**Fix:** the fiche must price an act carrying a `treatmentPlanItemStepId` from the séance's own `AgreedCost`,
not from the catalogue `defaultCost` — the 0,000 line should be the active line and the 150,000 copy should not
be constructed at all. Then show the booking dialog's own « Déjà facturé … Cette séance n'ajoute pas
d'honoraires » notice here, and default « Payé » to 0 with the note named. The correct text already exists one
screen away; it simply never reached this one.

> **Test data left behind, deliberately, as the proof:** Sonia Trabelsi now holds duplicate note **2026-0094**
> (150,000, collected) with no plan link, and devis 2026-0011 is Completed. This needs reversing with an avoir
> before the dev database is reused.

### A1 · CRITICAL · « Arrêter le traitement » offers to delete work the patient has already received **[screen+code]**

Live plan **2026-0008** (Karim Hamdi, « Extraction simple », 1st séance performed and evidenced by a fiche, 2nd
step unbooked). The workspace itself says « Actes réalisés **0/1** · 1 en cours · **1/2 étapes** ». Opening
« Arrêter le traitement » shows, verbatim:

> **Arrêter le traitement de Karim Hamdi ?**
> Le patient ne poursuit pas. Les actes qui n'ont pas commencé sont retirés du devis, **ce qui a déjà été fait
> est conservé**, et le devis est clôturé.
>
> **RETIRÉS DU DEVIS**
> Extraction simple — 120,000 DT
>
> L'échéancier est ramené au total conservé (**0,000 DT**). Ce qui a déjà été encaissé est conservé. La note
> 2026-0087 n'est pas modifiée : corrigez-la par un avoir si elle ne correspond plus.

On this plan nothing survives, so there is no « CONSERVÉS » section at all — the only act, half delivered, is
listed for removal and the devis is reduced to zero.

**And it was then executed, on a purpose-built plan.** Devis 2026-0013: « Couronne / bridge (par élément) » at
1 000 DT with the catalogue's 3 séances, plus a « Détartrage » at 60 DT. Séances 1 (Préparation) and 2
(Empreinte) booked and **recorded with real fiches**; séance 3 (Scellement) left unbooked. The dialog offered:

> **RETIRÉS DU DEVIS** — Couronne / bridge (par élément) 1 000,000 DT
> **CONSERVÉS** — Détartrage 60,000 DT

Through the dialog you can read the very row it is about to delete: « ✓ Préparation · ✓ Empreinte · ○ Scellement
· **2 / 3** ». Pressing the red button gave a single toast — « **Tous les actes doivent être réalisés avant de
clôturer le plan.** », a *failure* — and committed the destruction anyway:

| | before | after |
|---|---|---|
| `TreatmentPlans.TotalPlanned` | 1060.000 | **60.000** |
| `TreatmentPlans.Status` | 2 (En cours) | **2 — never closed** |
| `TreatmentPlanItems` | couronne (1000, Status 2) + détartrage | **couronne row gone** |
| `TreatmentPlanItemSteps` | 3 rows, 2 with `DoneDate` + `LinkedDentalRecordId` | **0 rows** |
| `Installments` | 1060.000 | **60.000** |

What is left pointing at nothing: both `DentalRecords` survive, orphaned — the clinical record of two real
séances attached to no plan — and `AppointmentProcedures` still reference the deleted ids
(`item_exists 0`, `step_exists 0`).

What the dentist then sees: « Total 60,000 DT · Actes réalisés 0/1 »; **« Arrêter le traitement » is gone from
the screen** (the surviving act is « À enregistrer », not « À planifier »), so the half-run operation cannot be
retried or completed; the plan's own « **Parcours** » records **no trace of it** — two delivered séances and the
deletion of a 1 000 DT act leave no history entry; and the patient header says « Solde dû 60,000 DT » while the
patient's « Actes dentaires » table lists the two crown séances each showing « Reste 1 000,000 DT ». **Three
numbers, one patient.** Only SQL puts this back.

Cause: `stoppableItems = plan.items.filter(i => planItemState(i) === "to-schedule")`, whose own comment claims
these are the acts "with **no work and no booking against them**". But `planItemState` answers for the act's
**next step** — its own comment says so: *"A bridge with two of three séances done … Keying on the next step
makes the badge say what to do about the part that is still open."* So a two-thirds-delivered bridge whose last
séance is unbooked returns `"to-schedule"`. Server-side `RemoveItem` refuses only `Status == Done`; a partly-done
act is `InProgress` and passes. And because « Arrêter » is **two calls** — `POST /amend` then `POST /complete` —
the refusal of the second cannot undo the first.

Note the contrast that makes this an oversight rather than a policy: at *step* level the model guards this
carefully — « L'étape « X » est déjà réalisée et ne peut pas être retirée. » The act-level delete that
« Arrêter le traitement » uses bypasses that guard entirely.

**Fix, three parts:** (1) `stoppableItems` must exclude any act with *any* done step —
`item.steps.some(s => s.doneDate)`; the dialog's own promise is the correct spec. (2) Mirror it in the domain:
`RemoveItem` should refuse an act carrying a done step, because one client function is not the right home for
that rule. (3) Make « Arrêter le traitement » **one server-side command**, so a refused clôture cannot leave the
removal behind.

### A1b · CRITICAL · Stopping a treatment where nothing survives is a dead end, and the refusal is a .NET parameter dump **[screen+SQL]**

The same plan one step earlier, when both acts were droppable (kept total 0 DT). The dialog offered both acts
under « RETIRÉS DU DEVIS » and stated « L'échéancier est ramené au total conservé (**0,000 DT**) ». Pressing the
red button gives:

> **« Le montant de l'échéance doit être supérieur à 0. (Parameter 'amount') »**

The dialog stays open, unchanged, with the same figures and the same live red button. Pressing again repeats it.
SQL diff: identical — atomic, at least. **There is no way out of that screen**: the plan cannot be stopped at
all, and « Annuler » voids the devis rather than stopping the treatment, which is the wrong record for a patient
who did receive séances.

This is the *ordinary* shape: a patient accepts a devis, comes twice, and stops before paying anything. And the
message is a developer's parameter name in a product whose refusals are otherwise written in French for a
dentist. (The client code that builds the schedule comments that a zero row is "the degenerate schedule, which
the aggregate accepts". The aggregate does not.)

**Fix:** accept a zero-amount schedule — or drop the schedule — when the kept total is 0; and when nothing
survives at all, say so: « Ce devis n'a plus d'acte à conserver : annulez-le (motif requis) plutôt que de
l'arrêter », with a route to « Annuler ».

### A2 · CRITICAL · The largest number on a devis page is wrong on every bridged plan **[screen+code]**

4 of 4 bridged plans in the database, never right on any:

| Devis | Plan « Reste » | Note | Actually left on the note |
|---|---|---|---|
| 2026-0007 Nadia Jelassi | 4 500,000 | 2026-0063 | **0,000** (fully paid) |
| 2026-0008 Karim Hamdi | 120,000 | 2026-0087 | 15,500 |
| 2026-0009 Leila Gharbi | 180,000 | 2026-0076 | 30,000 |
| 2026-0011 Sonia Trabelsi | 150,000 | 2026-0061 | **0,000** (fully paid) |

The clearest illustration: **Karim Hamdi's patient file shows « Solde dû 31,000 DT » in the header and
« Reste 120,000 DT » in the plan strip on the same page** — two figures for what one patient owes, 89 000 DT
apart. Two of the four are patients who owe nothing, shown a « Reste » plus an « En retard » badge.

`isPlanBilled()` exists. It is called in exactly **three** places — the plans table's « Facturé — … » badge
(card and table form) and the workspace's button-gating variable — and in **none** of the six places that print
the balance:

| # | Site | What it shows |
|---|---|---|
| 1 | `plan-workspace.tsx:790` | the header « Reste » figure, guarded on `!isDraft` **only** |
| 2 | `treatment-plans-table.tsx:347` | « Reste » on the card form |
| 3 | `treatment-plans-table.tsx:461` | « Reste » in the table cell |
| 4 | `patient-plans-strip.tsx:168-169` | « Reste » on the patient file — **and `tone="alert"` (red) once every act is done**, so a finished, fully-paid treatment shows a red balance |
| 5 | `plan-next-action.ts:73` | `planNextAction` → `{ kind: "collect" }`, which points the dentist at an échéancier the server then refuses to collect on |
| 6 | `plan-next-action.ts:97` | `planHeadline` → « Reste à encaisser » |

Site 1 is the proof it is an oversight: the guard there is `!isDraft`, added with the stated reason that a Draft
contributes 0 to « Solde patient » so showing a « Reste » *"would contradict the balance the rest of the app
reports"*. That is verbatim the argument for a billed plan, and it was not applied.

That it is an oversight rather than a decision is provable: the same code suppresses « Reste » for a **Draft**
devis with the stated reason that showing it *"would contradict the balance the rest of the app reports"*. The
identical argument applies to a billed plan and was not applied.

**Not** a collectable double-charge — the collection path is properly closed (no « Encaisser » action on a
billed plan's rows, and an explicit notice). It is a wrong figure in the largest type on the page, with the
explanation ~880 px below it, under the fold at 1440×900.

**Fix:** one derived reader — `displayedOutstanding(plan)`, returning null when `isPlanBilled(plan)` — consumed
by all six sites, with the note's own number shown instead (which means adding the note's total to
`TreatmentPlanDto`; today it carries only the note's id, number and status, which is also why A6's "state the
divergence" fix is unimplemented). Then a derived check in `check:responsive` that fails on any new
`.outstanding` read outside that reader — this is the same non-propagation shape the repo has recorded 13 times,
and a guard is the only thing that stops the fourteenth.

### A3 · CRITICAL · Re-opening a booked séance unlocks the price and deletes the guard — executed **[screen+SQL]**

The edit-appointment dialog hydrates each stored act with `procedureTypeId / treatmentPlanItemId / planLabel /
fallbackName / agreedCost / treatmentPlanItemStepId` — and **no `billedOnPlan`**, while `presetToSelectedAct`
(used on the two *add* paths) sets it.

Proven on devis 2026-0008 (billed on note 2026-0087), minutes apart on the same act. **On create**, the dialog
is exemplary: `value="0,000" readOnly=true`, caption « facturé sur le devis », notice « Déjà facturé. Cet acte
est porté par le devis 2026-0008 à 120,000 DT. Cette séance n'ajoute pas d'honoraires. Encaissement sur la note
2026-0087. » Saved with `AgreedCost 0.000`. **Re-opening that same appointment**:

- « Prix pour ce rendez-vous » → **`readOnly=false`** — editable;
- the « Déjà facturé » notice → **absent**; « facturé sur le devis » → **absent**;
- a new link actively *invites* a price: « **remettre au tarif (60,000 DT)** » — and 60,000 is the **catalogue
  tarif**, not even the devis's own 120,000 for this act;
- the step « Pose de la prothèse » disappears from the act row (a display loss — the link survives the payload);
- a « **FACTURATION · Non facturé** » block appears with « **Facturer cette consultation** », on a séance whose
  act is already on note 2026-0087.

Typing 120 and saving returned **200** with `"agreedCost":120`, the récapitulatif then asserted « Prix convenu
120,000 DT », and SQL confirms `AppointmentProcedures.AgreedCost` went **0.000 → 120.000**.

Re-opening a visit to move its time is routine. Doing so removes the only guard against pricing an act twice
and replaces it with an invitation to price it — and A0 then bills whatever it finds.

`web/components/CLAUDE.md` states the rule exactly — *"the one that forgot would silently re-price a bridge"* —
and names two paths. Hydration is the third.

**Fix:** hydrate `billedOnPlan` (and `stepOptions`, see D3) through the same mapper the add paths use; keep the
step chip; suppress « Facturer cette consultation » for a séance whose act is carried by a devis; and add a
derived check pairing `billedOnPlan` with `treatmentPlanItemId`.

### A4 · CRITICAL · A finished devis can never be invoiced **[code — confirmed]**

Two lines, in two files, that cannot both be right:

- `TreatmentPlan.MarkItemStepDone` ends with « if every act is Done → `Complete()` » — so recording the **last**
  step sets the plan to `Completed`, automatically and unavoidably.
- `plan-workspace.tsx` renders « Facturer le devis » under `isActive && !billed`, where
  `isActive = status === "Accepted" || status === "InProgress"`.

So the moment a treatment is finished — the moment it should be billed — the only button that can raise the note
disappears. The server would allow it (`CreateInvoiceFromTreatmentPlanCommand` refuses only `Draft` and
`Cancelled`), and there is exactly one caller of that endpoint in the whole frontend: this button. `/factures`,
the plans table and the honoraires launcher have no equivalent.

An unbilled devis whose séances were all correctly recorded at 0 DT — the whole point of the feature — reaches
`Completed` with the full amount outstanding and no route to a note d'honoraires. The continuation dialog
promises precisely this and cannot deliver it: « Non facturée — le devis portera 1 000,000 DT et **sera facturé
une fois le traitement terminé.** »

The workaround, which nobody will find: add a step to reopen the plan to `InProgress`, bill it, then remove the
step.

**Fix:** allow « Facturer le devis » on a `Completed` unbilled plan. It is one gate — change `isActive && !billed`
to `!isDraft && plan.status !== "Cancelled" && !billed`, matching what the server already permits.

Worth separating from a *related and correct* behaviour: the button **is** properly withdrawn once a note
exists (verified on two billed plans, present on an unbridged one). The bug is the `Completed` gate, not the
`billed` one.

### A5 · MAJOR · The retroactive path never asks for the money the remaining work is worth **[screen+code]**

The worklist shows, today: **Karim Hamdi · « Extraction simple » · prochaine étape « Pose de la prothèse »**,
whole treatment priced at the extraction's **120 DT**. A prosthesis is not part of an extraction's fee.

« C'est la suite d'une séance précédente ? » creates the devis carrying only the *original* act's cost, and its
dialog has no money field at all — just « Nom de cette séance ». So every retroactive continuation systematically
under-prices by the value of the work that remains. The dentist must amend the devis afterwards to add the fee —
and on a plan already bridged to a note, added money is uncollectable and invisible to every money read (A6).

**Fix:** let the continuation dialog state and edit the total, or add the continuation as a *second* act with
its own fee rather than extending the first act's.

### A6 · MAJOR · An act added to a billed devis is permanently unbillable and invisible **[code]**

Both billed-plan guards were deliberately removed, on the reasoning that the divergence is documentary and
stating it is the whole fix. Sound for a *changed fee*; false for an *added act*. Adding 500 DT to a devis
already bridged to a note raises `TotalPlanned` and the échéancier, but every money read drops the plan, the
échéancier refuses to collect, and « Facturer le devis » never returns. 500 DT of delivered work reaches no
balance, no receivable and no caisse.

The notice names the wrong remedy: « … si le montant change, corrigez-la par un avoir. » An avoir does not make
the plan billable again.

Related: two source comments justify removing the guards by claiming the DTO carries the note's total so the
workspace can state the gap. **It does not** — only id, number and status. So the stated fix is not implemented.

### A7 · MAJOR · Cash handed over at the chair is refused, and the message names two wrong remedies **[code]**

The séance is recorded at 0. The patient hands over 200 DT. Typing it into « Montant payé » on the fiche is
refused pre-commit:

> Le montant payé (200,000 DT) dépasse le total de la séance (0,000 DT). **Corrigez le montant, ou ajoutez
> l'acte qui manque.**

There is no missing act, and 0 is not the right amount. The correct action — open the devis, échéancier,
« Encaisser » — is named nowhere: not in the refusal, not on the fiche, not in the booking notice. A dentist
following the message literally will "add the missing act", which prices the treatment a second time.

This also contradicts the feature's own claim that the balance is *"collected at whichever séance"*. It is
collectable only from the devis workspace. And the suggestion notice reinforces the wrong reading:
« … déjà facturé sur le devis, **rien à encaisser pour cette séance** » — true of honoraires, false of cash
whenever an échéance is due.

**Fix:** make the refusal name the échéancier, and give the fiche a « Encaisser sur le devis » action.

### A8 · MAJOR · « Créer le devis et planifier la 1re séance » discards a typed price, and can mint a 0 DT devis **[code]**

The one-press path writes the devis act at the **catalogue** `defaultCost`, ignoring whatever was typed into
« Prix pour ce rendez-vous », then replaces the act row — so a price agreed on the telephone is silently lost,
and the notice then confidently states the catalogue figure as the devis amount. If the act has no default cost
the devis total is **0**, which skips the automatic échéance entirely: an implant devis with no total, no
schedule, and every séance priced 0.

It also fires **immediately**, inside the still-open booking dialog. Abandon the booking afterwards and the
devis exists anyway — numbered, accepted, with a money claim and no appointment.

### A8b · MAJOR · « Modifier l'échéancier » stays enabled on a billed devis, with a false subtitle **[screen]**

Its subtitle reads « Re-répartissez **ce que le patient doit** sans toucher aux actes. » On a billed devis
« ce que le patient doit » is the false figure from A2. Unlike « Modifier le devis », which warns properly, this
dialog says nothing about the note — so a dentist hunting for *where do I take the money* lands here and
re-splits échéances that collect nothing.

### A8c · MAJOR · « Planifier l'étape » pre-fills 09:00, so most of the working day it books into the past **[screen — two independent runs]**

Measured at 11:43 Africa/Tunis: the booking sheet opened from « Planifier l'étape » pre-fills
`date=04/09/2026 time=**09:00**`. The ordinary agenda path (« Nouveau ») pre-fills the **current** time instead
— recorded at 11:25, 11:30, 11:35 and 11:50 across four runs. **So the feature's own booking entry point is the
one place in the app where the time default is wrong for most of the working day.**

Consequence, reproduced twice: « Créer le rendez-vous » raises « Heure dans le passé », then « Créneau déjà
occupé (04/09 09:30–10:30) » — because 09:00 is both past *and* taken. Setting a real time by hand then raised
« En dehors des horaires d'ouverture ». Three modals and three POSTs to book one séance, on the action a dentist
repeats most.

The sharper risk is habituation: a dentist who taps « Continuer » past « Heure dans le passé » out of habit has
just booked the next séance of an implant into a morning that has already gone.

**Fix:** use the same next-free-slot logic the agenda's « Nouveau » already has. Better still for this feature:
default the **date** forward by the step's clinical interval (I1).

### A9 · MINOR · `roundMillimes` is bypassed in this feature's own arithmetic **[code]**

The repo's client-side money-rounding authority is documented as applying to *any* client-side money arithmetic.
`stopTreatment` and `negotiatedTotalOf` both do raw float sums, and `stopTreatment` carries a hand-rolled copy
of the rounding plus hand-rolled tolerances. No reachable break was constructed (every input already sits at
millime precision), but the server checks the échéancier total with **exact equality**, so this is one odd price
away from a refusal nobody will be able to explain.

### A10 · MINOR · The re-spread échéance is dated with the browser clock **[code]**

`stopTreatment` builds the new due date from `new Date().toISOString().slice(0,10)` instead of the repo's
`todayLocalIso()`. For the first hour of every Tunisian day that dates the new échéance to **yesterday**, so it
is born « En retard ».

---

## Part B — Can a dentist undo a mistake?

The honest answer: **yes for steps, mostly no for documents, and never for the two irreversible creations.**

### B1 · CRITICAL · A wrong retroactive continuation is a permanent dead end **[code]**

Pick the wrong previous séance and press « Créer le traitement » — no confirmation — and:

- the devis is created **and accepted** in one save, so it cannot be deleted, only cancelled;
- the note is attached to it by `Invoice.AttachToTreatmentPlan`, for which **no inverse exists anywhere** in
  the codebase. The note points at the cancelled devis for ever;
- re-running the continuation on that fiche is refused for ever (the "already tracked" query has no status
  filter, so a cancelled plan still matches), and the fiche vanishes permanently from
  « Suite d'une séance précédente »;
- **the fiche can no longer be deleted either** — deleting it tries to un-mark the step on a cancelled plan,
  which the aggregate refuses, and the dentist sees only « Erreur lors de la suppression de l'acte dentaire.
  Veuillez réessayer. » Retrying never helps.

Order of operations matters and is documented nowhere: detach the step *before* cancelling, or you never can.

### B2 · CRITICAL · A stopped treatment cannot be reopened, resumed, or even cancelled **[code]**

« Arrêter le traitement » is not a server command — it is two sequential client calls (`amend`, then
`complete`). Afterwards the plan is `Completed`, so « Arrêter », « Terminer », « Facturer » and « Annuler » are
all gone, and there is no reopen route. The dropped acts and their steps are deleted; « Modifier le devis » can
only re-type them as **new** acts with new ids, leaving the old fiches orphaned. When the patient comes back —
which is what patients do — there is nothing to resume.

And because the two calls are not atomic, if any surviving act is still booked, `complete` throws *after* the
removals have committed: the acts are gone, the échéancier is rewritten, the plan is still `InProgress`, and the
dialog is left open over stale data.

### B2b · MAJOR · The devis offers to book several acts into one séance, and the fiche can only close one **[code — confirmed]**

The workspace invites the grouping explicitly: tick two « À planifier » acts and the bar offers
« **2 actes sélectionnés — Planifier ensemble — 1 RDV** ». Afterwards the row reads « séance de 2 actes » and the
card tree gets a « Séance de 2 actes » header. So the feature actively encourages it.

But recording it cannot complete it. `CreateDentalRecordCommand` passes a **single** `request.TreatmentPlanItemId`
to `DentalRecordLinker.LinkPlanItemAsync`, and `ResolveStepsOfTheSeanceAsync` is scoped to that one act. The
fiche's own « Acte planifié (facultatif) » is a single-choice `Select`. So one fiche closes the steps of **one**
devis act; the second act stays « À enregistrer » with its appointment already in the past, and the worklist
keeps listing it.

The recovery is to record a **second fiche for the same visit** — which is not refused (a different act, so no
conflict on `MarkDone`), but is the very thing the linker's own comment calls a dead end: *"the act row then
offered « Enregistrer la fiche » for a séance whose fiche exists, which opens a second fiche for one visit."*
That reasoning was applied to two **steps of one act** and fixed there; the same shape across **two acts** was
not.

Note this is the *grouping across acts*, not the grouping across steps. Steps of one act genuinely do close
together — the set is read from the appointment's own procedure rows, and there is a red-proofed guard behind it.
That half works.

**Fix:** make the fiche's plan link a list, or resolve every devis act on the appointment the way steps already
are — the appointment rows already hold the answer.

### B2c · MAJOR · Recovering a destroyed act re-quotes it 500 DT lower and walks the plan backwards **[screen+SQL]**

The only recovery on offer after A1 is « Modifier le devis » → « Ajouter un acte ». It works — 4 clicks, and the
amend dialog even shows « Étapes proposées 3/3 » for the *new* line. But:

- **new id**, so the two fiches, the appointments and the step links can never be reattached;
- **`PlannedCost` 500.000** — the catalogue default, **not** the 1 000 DT that was quoted and half delivered.
  Nothing on screen mentions the old fee;
- three brand-new steps, **all undone** — the two delivered séances are gone from the treatment;
- `SequenceNumber = 2`, so it now sits *below* the détartrage it used to precede;
- plan `Status` 2 → **1 (Accepté)**, header « Actes réalisés 0/2 ». A treatment two séances in reads as never
  started.

### B1b · MAJOR · The orphaned fiches then offer a *third* devis at 1 000 DT, as two identical candidates **[screen]**

After A1, the two fiches whose step links were deleted reappear in « C'est la suite d'une séance précédente ? »
— listed **twice, character for character**:

> **Couronne / bridge (par élément)** · 4 sept. 2026
> Non facturée — le devis portera **1 000,000 DT** et sera facturé une fois le traitement terminé.

Two rows, same act, same date, same sentence, no way to tell them apart. Picking either creates a new numbered
accepted devis at 1 000 DT — while the original plan is still live and already carries a re-added crown at
500 DT. The app's own continuation feature treats the wreckage as un-quoted work.

The guard against exactly this exists — « Cette séance fait déjà partie d'un traitement. Ouvrez le devis pour
planifier la suite. » — but it keys on `LinkedDentalRecordId` on the plan's items and steps, and **A1 deleted
precisely those rows.** So the destruction defeats the duplicate-quote guard.

### B3 · MAJOR · Steps are silently not editable in « Modifier le devis » **[screen+code]**

Three separate ways the same edit is lost, all silent:

1. **Existing acts show no step controls at all.** The amend form never hydrates `steps` onto a line, so the
   « Étapes proposées » panel is empty — confirmed in the browser on plan 2026-0010, a 6-step act with zero
   step controls on screen. The dialog's own description asserts « Seul le patient n'est pas modifiable », which
   is false: the steps are not modifiable there either.
2. **A steps-only edit is filtered out of the payload.** The client's changed-line test compares designation,
   cost, procedureTypeId and teeth — not `steps` — so the line is dropped from `updateItems`, and if nothing
   else changed the form answers « **Aucune modification demandée.** » for a change the dentist did make.
3. **An act *added* by amendment ignores what was ticked.** `AmendTreatmentPlanCommand` calls
   `TreatmentPlanStepProtocol.ApplyAsync(plan, clinicId, repo, ct)` with **no `confirmedByPosition`** argument,
   while the creation path passes the dentist's confirmed list. So unticking every séance on a newly-added act
   sends `steps: []`, the server discards it, and the full catalogue protocol is applied instead — under a
   « Devis modifié » success toast.

The tri-state that the modal, the API client and `TreatmentPlanStepProtocol` all document — absent means "use
the catalogue", `[]` means "one séance", a list means "this sequence" — is honoured **on creation only**.

**Fix:** hydrate `steps`, include them in the changed-line test, and pass `confirmedByPosition` on the amend
path exactly as the creation path does.

### B4 · MAJOR · « Arrêter le traitement » is hidden in the commonest abandon shape **[screen]**

On plan 2026-0010 the button is **absent**, because the act's next step has a future appointment, so nothing is
"stoppable". That is precisely the patient who cancels the next séance and never returns. The dentist must first
find and cancel the appointment on another screen, with nothing telling them so.

### B5 · MAJOR · « Terminer » promises the opposite of what it does — twice **[screen+code]**

The confirm says « Les N actes non réalisés resteront non réalisés — la clôture ne les valide pas. »
`Complete()` then throws « Tous les actes doivent être réalisés avant de clôturer le plan. » The button is
guaranteed to fail in exactly the case the dialog bothers to explain.

The same dialog makes a second false promise. On billed devis 2026-0008 it reads, verbatim:

> Le devis 2026-0008 passera à « Terminé ». Les 1 acte non réalisé resteront non réalisé — la clôture ne les
> valide pas. **Les échéances restantes resteront encaissables.**

On a billed devis that last sentence is false: the échéance has no « Encaisser » action, and the section
directly above it says an encaissement entered there would reach neither la caisse nor les recettes. Same
non-propagation as the « Reste » figure in A2.

Note also « **Les 1 acte non réalisé resteront non réalisé** » — a pluralisation bug on screen verbatim.

**And no case could be constructed from the UI in which « Terminer » succeeded.** With any act unrealised the
server refuses; when every act *is* realised the plan has already auto-completed and the button is not rendered.
Source suggests one narrow window — immediately after an amendment that removed the last non-done act, which is
exactly what « Arrêter le traitement » relies on. So in ordinary use « Terminer » is a button that confirms and
then always fails.

**Fix:** either make the clôture do what the dialog promises, or disable « Terminer » while any act is
unrealised and put the reason on the button.

### B6 · MAJOR · No confirmation on three irreversible creations **[code]**

Creating a devis, the one-press « Créer le devis et planifier la 1re séance », and « Créer le traitement » all
fire directly. Each consumes a devis number permanently and produces an accepted document with a money claim.
The only undo is a cancellation that stays on the books with a motif. Accepting a *legacy draft* — a far less
consequential act — does get a confirmation that spells out the consequences.

### B7 · MINOR · A standing footnote points at a control that is not on the screen **[screen]**

Tested and **largely refuted**: the two « Détacher » operations are not confusable in practice — in the steps
dialog a done step's icon is an unlink (« Détacher la fiche de soins de l'étape « 1re séance » ») and a pending
step's is a bin (« Supprimer l'étape « … » ») — different icons, different verbs — and the act-level « Détacher »
only renders once the whole act is Done, so the two are never on screen together.

What *is* wrong: the workspace's standing footnote says « Un acte coché par erreur se corrige avec « Détacher la
fiche » » on a plan where that button is **not rendered**, because the act is « en cours » rather than
« réalisé ». Text pointing at a control that is not there.

One real gap remains, documented in source and stated nowhere on screen: on a stepped act the act-level
« Détacher » undoes only the **last done step**, not the act.

### B8 · MINOR · Step reordering is disabled with no explanation, at 32×20 px **[screen]**

Corrected after testing — this is not a silent no-op but a silent **disable**. On a 2-step act with step 1 done:
the done step has no move buttons at all (a static grip), and the pending step has **both** « Monter » and
« Descendre » `disabled` with `title=null` — no tooltip, no message, nothing saying why. The buttons also measure
**32×20 px**, well under the touch floor, and unlike the row actions they are not wrapped in `.touch-target`.

**Fix:** state the reason on the control — « Une étape réalisée ne peut pas être déplacée » — and give the pair
a 44 px hit area.

### B9 · MINOR · Splitting a line copies the protocol to every part **[code]**

Splitting a 3-step act across 4 teeth quotes **12 séances**.

### B10 · MINOR · Correcting a step is admin/doctor only; creating the state is not **[code]**

A secretary can mark a step done by saving a fiche, and cannot detach it.

### B11 · MINOR · The journal cannot say which act or which étape changed **[code]**

Step entities are not auditable in their own right; a step edit is journalled as *TreatmentPlan · Update ·
UpdatedAt*. A rename and a deletion are indistinguishable.

---

## Part C — Is it intuitive?

### C1 · MAJOR · Two counters, the same shape, opposite meanings **[screen]**

On the worklist: « **étape 2 / 2** » — the *rank* of the next step. On the devis strip: « **1 / 2** » — *done
of total*. A dentist reading « étape 2 / 2 » on Nadia Jelassi's implant reasonably concludes it is finished; it
means the 2nd of 2 is next and nothing is done but the first. The pips beside it carry the truth, and are 8 px
across.

**In fairness, there is a defence, and it is worth recording**: the visible word « **étape** » is deliberate and
load-bearing — without it, a bridge with two of three done would read « 3 / 3 » on the worklist and « 2 / 3 » on
the workspace, which is worse. So the label is doing real work.

It still is not enough. Three reviewers read « étape 2 / 2 » cold on a treatment with one of two steps done, and
all three took it for complete. The word tells you *which* counter it is only if you already know there are two
kinds.

**Fix:** make the worklist chip say what it means — « étape 2 sur 2 **à faire** » — or drop the counter and keep
the pips plus the step name.

### C2 · MAJOR · Progress is invisible for exactly these treatments **[screen]**

« AVANCEMENT » counts **acts**, and a stepped act is only Done when every step is. So:
- 2026-0004, a bridge with 2 of 3 séances delivered → « **0/2 actes** »
- 2026-0008, 1 of 2 steps done → « **Actes réalisés 0/1** », with « 1 en cours · 1/2 étapes » in 11 px grey beside it.

A six-visit implant reads « 0/1 actes » from the first appointment to the last. On the list a dentist scans
daily, the feature's whole subject has no progress signal.

**Fix:** weight the column by steps (the workspace's progress *bar* already does this — the number beside it does not).

### C3 · MAJOR · « Planifier la séance » does not plan a séance **[screen+code]**

The worklist's primary action navigates to the devis workspace, where the dentist must find the act row and
press *its* « Planifier l'étape ». The most frequent action in the feature costs an extra screen and a hunt,
under a label that promises otherwise.

**Fix:** open the booking dialog directly, pre-filled with patient, act and next step — the workspace already
does exactly this.

### C4 · MAJOR · Vocabulary collides **[screen+code]**

- The catalogue editor is titled « **Étapes** » and counts « **6 séances** » in the same header.
- « séance » means both a sub-part of an act (catalogue) and the appointment that carries it (everywhere else).
- The rail says « Traitements et devis », the page says « Traitements », the patient tab says « Plan de
  traitement », the card inside it says « Plans de traitement », and the button says « Nouveau plan ».
- « Détacher » names two operations (B7).

**Fix:** one word per concept. Suggestion: **étape** = the sub-part; **séance** = the visit; **devis** = the
document; **traitement** = the whole course. Then « 6 séances » in the catalogue becomes « 6 étapes », and the
patient tab and its card agree with the rail.

### C5 · MINOR · A protocol-less act still offers a step control that reads as a state **[code]**

For an act with no steps the control's label becomes « **Définir les étapes de …** » — correct — but the
catalogue row reads « **Une seule séance** » to a non-admin, which is a claim about the act rather than about
its configuration.

### C6 · MINOR · « Parcours » prints « Date non disponible » **[screen]**

On plan 2026-0008: « Devis facturé · Note d'honoraires 2026-0087 · **Date non disponible** ». A missing value
rendered as a sentence in a timeline.

---

## Part D — The day-to-day burden

### D1 · MAJOR · The 14-day amber is already crying wolf, and no step can say « pas encore dû » **[screen+code]**

« DERNIÈRE SÉANCE » turns amber (`text-warning-ink`) after a flat **14 days**, from a constant
(`STALE_DAYS = 14`) with no reference to what the step is. On screen today: « Nadia Jelassi · Implant dentaire ·
Pose de la couronne » is amber at « il y a 14 jours » and « Sonia Trabelsi · Traitement de canal » likewise,
while Leila Gharbi (4 days) and Karim Hamdi (3 days) are grey.

The shipped implant protocol is *Bilan · Pose · Contrôle · Désenfouissement · Empreinte · Pose de la couronne*,
and osseointegration between « Pose de l'implant » and « Désenfouissement » is **8–12 weeks**. So a
correctly-progressing implant is amber for ten of its twelve weeks.

There is **no way to say « not due yet »**: every screen of the feature was searched for one.
`TreatmentInProgressDto` carries `lastStepDoneOn` and `nextStepAppointmentId` and **no due date**, and the only
thing that clears the amber is booking a séance.

A list that flags correct clinical waiting as overdue is a list a dentist stops reading — and then it also stops
catching the bridge that really *was* abandoned, which is the only reason the screen exists.

**Fix:** see I1. Cheapest interim: let a row be dismissed (« pas encore dû, rappeler le … ») so the amber means
something.

### D1b · MAJOR · The protocols researched the intervals, and the model cannot hold them **[code]**

This is the root of D1, and it is the most valuable single fix in the review. The seed's own research
established the elapsed time between séances, and quotes it:

- Parodontal, verbatim from HAS 2018: « les séances sont espacées d'**une semaine environ** », and « la
  réévaluation est à **8 semaines minimum** ». Four quadrants plus réévaluation is therefore ~11 weeks.
- Implant, from the ITI loading protocols: months between « Pose de l'implant » and « Désenfouissement ».
- Gingivectomie: « Dépose du pansement ou des sutures à **7–10 jours** ».

`ProcedureStepTemplate` carries **Label + DurationMinutes only**. So every one of those intervals was
discarded at the model boundary — the schema records chair time and nothing about the calendar. Consequences,
all visible:

1. The worklist's amber at 14 days contradicts the protocol the same codebase researched (D1).
2. There is no « pas encore due » state, so a treatment progressing exactly to plan is indistinguishable from
   one the practice has forgotten.
3. Booking the next séance offers no suggested date, though the protocol knows roughly when it should be.

**Fix:** add an interval to the step template (`MinDaysAfterPrevious`, or an explicit due-window), grade the
worklist's alarm against it instead of a flat 14 days, and pre-fill the booking date from it. The clinical
research is already done and already in the source comments; only the field is missing.

### D1c · MINOR · An act's own duration is stale for a stepped act, and still sizes the slot **[code+screen]**

For every stepped act the sum of the steps' minutes differs from the act's `DefaultDurationMinutes`, often
wildly: Implant 245 vs 60, Facette 405 vs 60, Prothèse amovible 225 vs 60, Parodontal 270 vs 45, Incision
d'abcès 45 vs 20. The code deliberately does not compare them, and for the booking path that carries a step
that is right — the slot takes the *step's* minutes. But any path that books the act **without** a step falls
back to the act's scalar, so a re-opened séance (D3, where the step chips do not render) can get a 60-minute
slot for a 90-minute « Pose de l'implant ».

### D1d · MAJOR · Splitting a line divides the money and multiplies the séances **[code]**

`splitLine` spreads one act over its teeth. It handles the money carefully — the cost is divided per tooth for
an act priced per tooth and kept whole otherwise, with a comment explaining why — and then copies the row with
`{...line}`, which carries `steps` along unchanged. So the fee is divided and the protocol is **duplicated**.

The catalogue names its unit where it matters — « Couronne / bridge (**par élément**) », « Application de fluor
(par arcade) » — so per-element pricing actively invites the split. Split that 3-step act across 4 teeth and the
devis quotes **12 séances for a bridge that takes 3**, each row claiming Préparation, Empreinte and Scellement.

The natural path (one line « Bridge 4 dents », four teeth) avoids it, and that is what live data shows dentists
doing — but the split button is right there beside a per-element act.

**Fix:** keep the steps on the first split row only, or ask. A protocol describes the *act*, not each tooth.

### D2 · MAJOR · A step cannot be booked from the screen that tells you to book it **[screen]**

See C3. Add the round trip back to `/treatment-plans` afterwards to confirm it left the list.

### D3 · MAJOR · You cannot change which step a booked séance is for **[code]**

The edit dialog omits `stepOptions`, so the « Étapes de cette séance » chips do not render on a re-opened
séance. The only route is remove-and-re-add, which lands on the plan's *next* step rather than the one that was
booked.

### D4 · MINOR · An appointment can be stranded on a devis **[code]**

Remove every devis act from a visit and `treatmentPlanId` is omitted — which means "unchanged" — so the
appointment keeps pointing at the devis with no control anywhere to clear it.

### D5 · MAJOR · The devis list defaults to « Cette semaine » and hides 370 000 DT of receivables **[screen+SQL]**

« DEVIS ET ÉCHÉANCIERS » defaults to the current week —
`GET /api/treatment-plans?…&from=2026-08-30T23:00:00Z&to=2026-09-06T22:59:59Z` — and the footer reads
« 1–12 sur 12 devis ». The clinic actually has **16**. Three fall outside the window and are hidden, two of them
unpaid:

| Devis | Patient | Date | Total | Reste |
|---|---|---|---|---|
| 2026-0001 | Amine Trabelsi | 17 Aug | 500,000 | 0,000 |
| 2026-0002 | Mehdi Bouazizi | 19 Aug | 300,000 | **300,000** |
| 2026-0003 | Fatma Zouari | 21 Aug | 120,000 | **70,000** |

**370 000 DT of receivables invisible by default**, on the one page whose columns are TOTAL / ENCAISSÉ / RESTE.
A devis for an implant is a multi-month object; defaulting its ledger to "this week" makes last month's unpaid
balances vanish.

Worse, the section directly above it — « TRAITEMENTS EN COURS » — has **no date filter at all**. So two tables
on one page cover different periods and neither says so.

**Fix:** default the devis section to « Tous » (or to "not fully settled"), and whenever the filter hides rows,
say so: « 3 devis masqués par le filtre ».

### D6 · MAJOR · The « facultatif » échéancier silently books the whole sum as due today **[screen+SQL]**

The section reads « Aucune échéance. Ajoutez un échéancier de paiement (**facultatif**). » Leaving it empty and
pressing « Créer le plan » creates **one** Installment: `DueDate` = the creation instant, `Amount` = the full
total, `AmountPaid` = 0.

So a 1 500 DT implant running six visits over months is recorded as payable **the day the devis is signed** —
and, being dated in the past by the time anyone looks, it carries an « **En retard** » badge from day one. Every
plan in the database has this shape, so it is the norm rather than an accident, and it is a large part of why
the « En retard » badges in A2 are meaningless.

**Fix:** say what will happen — « Sans échéancier, le total est dû à la signature » — or, better, default the
single échéance to the last étape's expected date (which I1 would make computable).

### D7 · MINOR · A séance that is one étape of a treatment looks identical to every other on the agenda **[screen+SQL]**

On 4 Sept, two of seven appointments are linked to plan steps (« Bilan pré-implantaire », « continuation ») and
five are not. **All seven cards render identically** — patient name + act name. No « 1/6 », no « devis » chip,
nothing.

The appointment *dialog* shows both the « devis » chip and the étape name. The agenda — the screen actually
being read at 8 a.m. — throws that away.

**Fix:** put « étape 1/6 » and the « devis » chip on the agenda card.

---

## Part D2 — On the devices it is actually used on

**Verdict: good, and this is not where the problems are.** Measured at 320 / 390 / 820, read-only:

- **No horizontal overflow at any width** on either the worklist or the devis workspace
  (`scrollWidth === clientWidth` at 320, 390 and 820).
- **The worklist has a real card form at 390.** Patient name, act, a labelled « PROCHAINE ÉTAPE » row carrying
  the step name + pips + counter, a labelled « DERNIÈRE SÉANCE » row, and a **full-width** « Planifier la
  séance » button. Not a reflowed table — a card.
- **The devis workspace stacks properly at 390**: the seven header actions wrap into a tidy pill stack, and the
  four figures become a 2×2 grid. The phone gets the bottom tab bar rather than the rail.
- **Touch targets are fine.** The 36 px icon buttons a naive measurement flags are wrapped in `.touch-target`,
  which raises the *tappable* area to 44 px on a coarse pointer via an overlaid pseudo-element without
  repainting — deliberately, so that the row actions of 22 tables don't inflate every row on a tablet. Anything
  reporting these as violations is measuring the painted box, not the hit area.

### D2z · CRITICAL · On a touch device, the step editor's « Monter » chevron moves the step **down** **[screen — tap-confirmed at 390 and 820]**

The up/down pair in « Étapes de l'acte » is two **32×20 px** buttons stacked with no gap, and every button in
this app carries `.touch-target`'s 44 px overlay. The later sibling paints last, so « **Descendre** »'s overlay
covers most of « Monter »'s painted box. Measured pixel row by pixel row on step 3 of a 6-step implant:

| Width | Painted up-chevron | « Monter » owns | « Descendre » owns |
|---|---|---|---|
| 820 | y = 410–430 | y = 398–**416** | y = **418**–460 |
| 390 | y = 439–459 | y = 427–**445** | y = **447**–489 |

So **14 of the up-chevron's 20 painted pixels fire the opposite action**, and the band that does work sits
mostly *above* the arrow you can see. A real coordinate tap at the painted centre of the up arrow, at both
widths, moved « Contrôle post-opératoire » **down** one place. At 1440 — fine pointer, no overlay emitted — the
same tap moves it up correctly.

**So it only breaks on the device this app is actually used on**, and each corrective tap makes it worse.

This matters beyond the annoyance: the step order is what the worklist reads as « prochaine étape » and what the
booking dialog pre-ticks, so a dentist who saves a wrongly-ordered protocol gets the wrong séance proposed from
then on.

**Fix — and the correct version already exists one dialog over.** This is the documented
`.touch-target`-on-adjacent-siblings trap: grow the boxes rather than overlaying them.
`procedure-type-steps-dialog.tsx` uses `size-6 coarse:size-8` and was **tap-verified working** at 390, 820 and
1440. `plan-item-steps-dialog.tsx`'s `h-5 w-8` is the odd one out. `plan-act-row.tsx`'s act-reorder pair is the
same `h-6 w-6` stacked shape and needs the same check — it could not be exercised (a one-act plan disables both).

That the two step editors are near-identical forms which **behave differently on touch** is the finding behind
the finding: one shared component would have fixed this in both places.

### D2y · MAJOR · At 390 the suggestion notice is below the fold — 7 of its 184 px are on screen **[screen]**

At 390×844 the booking sheet's scrolling region is y = 126–619 (493 px; the récapitulatif and the sticky footer
take the rest). « Ce patient a un traitement en cours » renders at y = **612–796**. Only its dashed top border
is visible; the message and its « Planifier « Bilan pré-implantaire » » button need a scroll. At 820 and 1440 it
is fully visible.

This is the feature's **only proactive reminder**, and its own design note says the case it exists for is *"the
dentist books from the agenda, in a hurry"*. In a hurry, on a phone, nobody scrolls a form they have already
filled — so on the device where the reminder is most needed it is the one place it is not seen.

**Fix:** put it directly under the patient field (it is about the patient, not the acts), or mirror it into the
sticky récapitulatif at coarse/narrow widths.

### D2x · MINOR · Below 640 px the act row drops its « devis » badge **[screen]**

At 1440 and 820 the row reads « Implant dentaire · **[devis]** · 45 min »; at 390 the badge is gone
(`hidden … sm:inline-flex`). In the **create** dialog nothing is lost — the « Déjà facturé … » notice still
renders in full at 390. But the **edit** dialog sets `planLabel` *without* the `billedOnPlan` that gates that
notice (A3), so there the badge is the only devis signal — and at 390 there would then be **none at all**.
**Fix:** drop the `sm:` gate; the badge is five characters.

### D2w · MINOR · The catalogue steps dialog's « Monter » hit box is 31 px tall at 390 **[screen]**

Painted 32×32, hit-tested 37×31 for « Monter » and 37×43 for « Descendre », each clipped by the neighbouring
row's overlay. **Direction is correct here** — a real tap moved the step up at all three widths — so a miss is a
mis-order rather than a wrong action. Still 31 px for a gloved finger.

### D2v · IMPROVEMENT · At 820 the money figures break after the number **[screen]**

« Total » and « Reste » wrap with « DT » alone on the second line, because the 256 px rail is still shown at 820
by design and the figure card is ~490 px. Every digit is legible; nothing is truncated. `whitespace-nowrap`.

Two further findings about *what* is on the small screen rather than how it lays out:

### D2a · MAJOR · On a phone, the wrong number is the most prominent thing on the page **[screen]**

At 390 px the devis workspace's money block fills roughly a third of the first screen, and « **Reste
120,000 DT** » is set in the same large bold type as « Total ». On plan 2026-0008 that figure is wrong (A2) —
and the notice that explains it is not merely below the fold, it is several screens down, past the whole
« Actes » card. The desktop at least has the two within one scroll of each other.

### D2b · MINOR · The act you came to act on is below the fold on a phone **[screen]**

At 390 px the « Actes » card is cut off at the fold — « Extraction simple » is half-visible — so the act row and
its « Planifier l'étape » sit behind four figures, one of which is wrong. On the device a dentist uses standing
up, the primary work is the last thing reachable.

### And it makes C1 and D1 worse, not better **[screen]**

On the phone card, « **étape 2 / 2** » sits alone on its own line under the step name, with the two 8 px pips
that carry the real meaning beside it. And the amber « il y a 14 jours » is a full-width row of its own on both
the implant and the endodontic treatment — so the alarm that shouldn't be firing (D1) is given a labelled row
to itself.

*B6's fuller sweep — the step editor dialog, « Étapes proposées », the booking picker's chips and the catalogue
steps cell — is still reporting.*

## Part E — Can a doctor in a hurry set it up?

**Once you know where it is: yes, and it is the feature's best work. Cold, a reviewer took four wrong turns and
never found the fast path at all.**

### E0 · MAJOR · The instinct path fails, and « étape » does not exist on screen until you are already inside the plan dialog **[screen]**

A cold walk, no source reading, looking for a way to plan a six-visit implant:

| # | Where | What was there | Verdict |
|---|---|---|---|
| 1 | `/` | Sidebar « Traitements et devis » + chips « 5 traitements en cours », « 0 devis en attente » | the concept **is** advertised — credit |
| 2 | `/appointments` — the instinct | Week grid. Toolbar: Aujourd'hui · Jour/Semaine/Mois · praticiens · Filtres · Google · … · Nouveau. **No** étape / séance / devis / plan anywhere | wrong turn 1 |
| 3 | « Nouveau » → « Nouveau rendez-vous » | Patient · Date · Heure · Durée · « Actes du rendez-vous » · Praticien · RÉCAPITULATIF. Nothing multi-visit | wrong turn 2 |
| 4 | « Autres actions de l'agenda » (…) | Exactly one item: « Exporter » | wrong turn 3 |
| 5 | back in the form, after choosing a patient | « **C'est la suite d'une séance précédente ?** » → « Il devient un traitement en plusieurs séances, et ce rendez-vous en est la **deuxième**. » A list of **past** acts | first hint — but backwards-only. Wrong turn 4 |
| 6 | `/patients/<id>` — second instinct | « Plans de traitement », tab « Plan de traitement » | **worked** |
| 7 | « Nouveau plan » | Titre* · Actes · Échéancier. **Still no mention of étapes** | — |
| 8 | pick « Implant dentaire » from the catalogue | « **Étapes proposées** » / « 1 acte se fait en plusieurs séances » | the moment of understanding |

So the feature is discoverable only as a *side-effect* of picking a catalogue act inside a plan you had already
decided to create. The word « étape » does not appear on any screen until step 8. And the one étape-shaped
control on the agenda — the screen a dentist lives in — only works **backwards** and always assumes this visit
is "la deuxième".

**Fix:** on the empty booking form, beside « Actes du rendez-vous », one line: « Cet acte se fait en plusieurs
séances ? Créer un devis ». And in « Suite d'une séance précédente », offer the forward twin — « Planifier les
séances à venir » — with the act prefilled.

### E0b · MAJOR · The one-press path was not reachable from the toolbar « Nouveau » button **[screen — measured negative]**

The fastest path on paper is: empty-slot click → patient → act → « **Créer le devis et planifier la 1re
séance** » → save. **7 clicks, 2 typed searches, one screen and one dialog, and no acceptance step**, because a
created devis is numbered and accepted in the same request.

But a cold reviewer walked the toolbar « **+ Nouveau** » path **twice** — once with no devis for the patient and
once with one — chose a patient and an act each time, and **the CTA was « Créer le rendez-vous » on both
occasions**. They never saw « Créer le devis et planifier la 1re séance ». The empty-slot entry was not tested,
so the label's placement is unverified; the negative result on the toolbar path is measured.

Two candidate causes, both worth checking: the offer renders **below the act row** inside a scrollable dialog
column, so it can sit under the fold; and it is correctly suppressed once the act already carries a devis link,
which covers the second attempt but not the first.

Either way the functional truth stands: the button that makes this feature fast was not found by someone
looking for it, from the entry point a dentist reaches by pressing the big « + Nouveau ».

### E4 · MAJOR · Typing the act's name instead of picking it loses the whole feature, silently **[code — confirmed]**

The devis form's field is labelled « **Désignation de l'acte (ou choisir au catalogue)** », so free text is a
first-class option, and the magnifier is an icon-only button beside it. But a typed line carries no
`procedureTypeId`, and:

- `TreatmentPlanItemPricing` resolves **by `ProcedureTypeId` only** — its own summary says « free-text lines
  (no procedure) are untouched ». No name matching anywhere.
- `TreatmentPlanStepProtocol` needs a `ProcedureTypeId` to look a protocol up — stated in its own comment.
- `stepProposals` requires `steps.length > 0`, so no « Étapes proposées » panel renders.

So a dentist who types « Implant dentaire » rather than opening the magnifier gets a **one-line devis at the
price they typed, with no protocol, no étapes and no fee prefill** — and nothing tells them the six researched
séances existed. The two paths look equivalent on screen and one of them quietly opts out of the entire feature.

**Fix:** when a typed designation matches a catalogue act, offer it — « « Implant dentaire » existe au
catalogue (6 séances, 1 500,000 DT) — l'utiliser ? » — or make the catalogue pick the primary affordance and
free text the fallback.

### E5 · MAJOR · The one moment a dentist needs « Planifier la séance », the page has no button for it **[screen]**

« TRAITEMENTS EN COURS » gives every row a « Planifier la séance » action — the best control on the page. But a
freshly accepted devis whose treatment has **not started** is absent from that list (it holds acts *begun and
unfinished*), and appears only in « DEVIS ET ÉCHÉANCIERS » as « 0/1 actes » with no next-séance link and no
scheduling action at all.

So the action immediately after accepting a devis — booking visit 1 — is the one the page does not offer, and
the dentist must go back through the agenda.

**Fix:** give every accepted devis with an unstarted étape the same « Planifier la séance » action.

### E6 · MINOR · « Étapes de cette séance » heads a list of *all* the act's étapes **[screen]**

The heading is followed by all six étapes as checkboxes, one ticked. Read cold it claims this appointment covers
six étapes. A wrong reading here is a wrong duration and a wrong expectation of what today covers.
**Fix:** « Étapes de l'acte — cochez celles de cette séance ».

### E7 · MINOR · An unknown route falls back to English **[screen]**

`/dashboard` (a natural guess; the home is `/`) returns « 404 · **This page could not be found.** » in an
otherwise fully French app. **Fix:** a French not-found page linking back to « Tableau de bord ».

### What genuinely is good here

Nothing is typed: the protocol arrives pre-filled and pre-ticked, with per-étape toggles, a « 6 / 6 » counter
and « Modifier ». 14 of the 34 catalogue acts ship a researched protocol and 20 correctly ship none, so an
ordinary détartrage is untouched. And **booking the next séance makes every decision for you** — measured at
6 clicks, all of them "which patient / which act": the app pre-checks the first unfinished étape, sets the
duration to that étape's own 45 min, sets the price to 0 with « facturé sur le devis », and explains itself.
Verified in the database afterwards: linked to `SequenceNumber 0`, `AgreedCost = 0.000`, no double billing.

Three things spoil it:

- **A8** — the price typed one field above the button is discarded.
- **E1 · MINOR · An act with no catalogue protocol cannot be cut into séances at creation time.** The
  « Étapes proposées » panel is derived from `lines.filter(line => (line.steps?.length ?? 0) > 0 && …)`, so an
  act that does not *already* carry a protocol never renders a panel, and the panel is the only place the
  creation form can add a step. Its absence then reads as « this act has no steps » rather than « you cannot set
  them here ».
  **Downgraded from Major after testing:** the workspace act row carries « Définir les étapes de « Détartrage » »
  as its own control, **one click**, and a cold reviewer found it without help. So the cost is one click after
  creation, not a dead end.
  **Fix:** still worth rendering the panel for every act with a designation, offering « + Découper en séances »
  when it has none — the same affordance `/procedure-types` already gives a protocol-less act.
- **E2 · IMPROVEMENT · « Titre » is the only thing you must invent, and the app already knows the answer.**
  It is the sole required field: pressing « Créer le plan » empty shows « Le titre est obligatoire. » and fires
  no request. Everything else came from the single act pick — designation, 1 500,000 DT, and six étapes with
  their durations. Twenty of these a week, and the only keystrokes demanded are a label derivable from the act
  just inserted. **Fix:** prefill from the first act, or drop the requirement.
- **E3 · IMPROVEMENT · The act picker does not say which acts are multi-séance.** 35 rows, name + price only.
  « Implant dentaire 1 500,000 DT » sits between « Greffe osseuse 700,000 DT » and « Contention
  post-orthodontique 300,000 DT » with nothing to distinguish the one that will propose six visits.
  **Fix:** a « 6 séances » chip on rows that ship a protocol.

---

## Part F0 — The one claim that was tested hardest and held: the feature does not intrude on ordinary work

The design bet is that a stepped act is invisible for the 20 of 34 acts that carry no protocol. Verified end to
end, on a patient with no plans:

| Test | Result |
|---|---|
| Book an ordinary « Détartrage » | Every visible line scanned for `étape\|séance\|devis\|protocol`: **one** hit — the « C'est la suite d'une séance précédente ? » link. No step chips, no devis badge, no « Déjà facturé ». Price **editable at 60,000** (the catalogue tarif, correct) |
| Record its fiche | **Zero** devis wording. The only « séance » hits are the fiche's own pre-existing vocabulary. Billed normally: invoice 2026-0095, 60,000 DT, `TreatmentPlanId` NULL |
| A devis of single-visit acts only | `étape\|séance\|protocol` matched **0 lines**. « Étapes proposées » does not render; no step strips |
| Does the one-off reach « TRAITEMENTS EN COURS »? | **No** — correct |

Two places where a dentist doing ordinary work now meets a new word, both costing a moment rather than a click:

1. « **C'est la suite d'une séance précédente ?** » appears on the booking dialog for **every** patient and act,
   including patients with no plan at all. It is genuinely useful, but it is a question on the most-used dialog
   in the app.
2. `/procedure-types` now shows « **Découper en étapes** » on **20 of 34** rows, so the catalogue no longer
   looks like a price list.

**That is the honest answer to "will it be a burden day to day": for single-visit work, no.** The burden is all
in Part D — the actions that *are* the feature.

## Part F — What works well (so signal is separable from noise)

1. **A step carries no money, by design.** No per-step fee split, so no remainder, no rounding question, and
   no act spread across invoice lines. The cleanest decision in the feature.
2. **The booking dialog's money statement is exemplary.** Read-only 0, the caption « facturé sur le devis », and
   a notice that names *this act's* fee and *the devis'* balance as two separately-labelled figures — and
   deliberately withholds the devis balance and names the note instead when the plan is bridged. This is the
   surface where the two previously-found money bugs lived, and it is now right.
3. **One fiche closes every step of its séance**, derived from the appointment rather than from the step the
   fiche was opened with — with a red-proofed guard behind it.
4. **Grouped séances are two wire rows and one card**, keyed server-side on the (act, step) pair, which is what
   makes « préparation + empreinte in one visit » expressible at all.
5. **An act with no steps behaves exactly as before, everywhere** — verified surface by surface. That is what
   makes the migration safe.
6. **A step is marked done only by saving the fiche that evidences it.** There is deliberately no manual toggle,
   and the workspace says so in prose.
7. **The échéancier guards are strict and correct**: exact-sum, never below what was collected, a paid row can
   never be dropped, and payments are attributed by their own date so re-dating a paid row moves no month's takings.
8. **Aggregate money is de-duplicated through one authority** across 12 reads, so the bridged-plan case never
   double-counts in a balance, a receivable, the caisse or the dashboard.
9. **« Facturer le devis » is correctly withdrawn once a note exists** — verified on two billed plans and
   present on an unbridged one. The "two notes for one devis" risk is closed.
10. **The protocols are real clinical work, sourced and traceable.** HAS 2018 for the parodontal quadrant
   split (verbatim), ITI for implant loading, Cochrane 2022 for endodontics, SFSCMFCO for the gouttière,
   Constantine 3 and a Lille thesis for the rest — each recorded beside the array it justifies, including
   *why* an act gets none. 20 of the 34 acts correctly ship no protocol, and the seed states the rule for
   adding one: a second séance needs a physical cause — a laboratory in the loop or a biological interval —
   not a habit of splitting work into stages. That rule is the reason this catalogue is trustworthy.
11. **« Modifier le devis » warns correctly on a billed plan**: « Ce devis est facturé sur la note 2026-0087.
   La note ne suivra pas cette correction : si le montant change, corrigez-la par un avoir. » Right warning,
   right place.
12. **« Contrôle post-opératoire » is modelled as a step inside the priced act**, not as a separate billable
   act — so the follow-up visit is free, which is both correct practice and correctly expressed.
13. **The screens are genuinely device-ready** (Part D2): no horizontal overflow at 320/390/820, a real card
   form for the worklist rather than a reflowed table, a full-width primary action on the card, and touch
   targets raised by an overlay so tablet row density is preserved. Whatever else is wrong here, a dentist can
   work this feature from a phone.
14. **The money refusal on stopping a treatment is the standard the others should meet.** Collect a 350 DT
   deposit, then stop with 60 DT of acts surviving: « **350,000 DT ont déjà été encaissés sur ce devis, pour
   60,000 DT d'actes conservés. Remboursez la différence par un avoir avant d'arrêter le traitement.** » Both
   figures, the remedy, and checked *before* anything is written — SQL diff shows no change.
15. **Removal blockers arrive pre-emptively, already disabled.** « Supprimer l'acte » in « Modifier le devis »
   is disabled with the reason beside it — « Un rendez-vous est prévu pour cet acte — annulez ou déplacez-le
   avant de le retirer. » The refusal is on screen before the dentist can trip it.
16. **The steps dialog answers the first question a dentist has, unasked**: « Rien ici ne touche à l'argent. Le
   prix de l'acte, le total du devis et l'échéancier sont inchangés, et le numéro de révision ne bouge pas. »
17. **« Détacher la fiche » is a real, well-confirmed undo** for a step recorded wrongly: « L'étape redevient
   « à faire » et son lien vers la fiche de soins est retiré. La fiche elle-même n'est pas supprimée, et aucun
   montant ne bouge. » And it correctly refuses when that fiche sits on a live note, naming the note.
18. **The booking clash guards each name the specific conflict** rather than asking « êtes-vous sûr ? » — a
   past time, an occupied slot quoting the conflicting appointment, and hours outside opening.
19. **`+ Découper en étapes` on every stepless catalogue row is the best discoverability decision in the
   feature** — it is on 20 of 34 rows, names the action in plain French, and is where a dentist actually learns
   the capability exists.
20. **The chair-time totals are arithmetically right every time** — « 3 séances / 1 h 50 de fauteuil au total »
   (30+60+20), Implant « 4 h 05 » (245), Facette « 6 h 45 » (405), Parodontal « 4 h 30 » (270) — and they
   recompute correctly after a middle step is deleted.
21. **All five edit operations on a protocol work, and reordering is a real reorder** — rename, ⌃/⌄ reorder,
   delete-middle, append, save; SQL after saving shows the durations travelled with their labels, so the
   sequence genuinely changed rather than being re-labelled. Blank rows are dropped silently on save.
22. **Per-step durations work end to end**: booking « Pose provisoire » (90 min) gives « 1 acte · 90 min »,
   « 09:00 → 10:30 », the « 1.5h » preset active — not the act's own 45 min — and two steps in one séance sum
   to 120 min. The created appointment persists 90.
23. **The card hinge is at `lg:`, not `md:`, on both new tables.** At 820 — the width this app is used on most —
   the worklist and the « Actes » table are card lists, so the primary action is a full-width button instead of
   a last column pushed out of the box. That is the exact habit-driven mistake the device contract warns about,
   and it was avoided.
24. **Every dialog reaches its own footer at every width.** Measured on all seven surfaces at 390 / 820 / 1440:
   no horizontal page-body scroll anywhere, no dialog unable to reach its primary button, and the 6-step strip
   stays legible in all four of its forms (6 pips + name + « 0 / 6 » on the workspace, 6 rows in the editors,
   6 chips in the booking dialog, « +4 » summary in the catalogue cell). The include checkboxes in « Étapes
   proposées » and the step chips in the booking dialog measure a true **44×44** on a coarse pointer.

---

## Part G — Incidental, outside this feature, but worth knowing

1. **A third concurrent device signs the other two out.** The refresh-token family keeps only current+previous,
   and every page load rotates. A third client is read as a replay and the whole device session is ended.
   A practice with a desk machine, a tablet and a phone on one account will hit this daily.
2. **A locked-out account stays locked while the owner keeps trying.** The 5-per-15-minute counter is held with
   a *sliding* expiry that is refreshed on every read, including the lockout check itself. So each further
   attempt — including a legitimate one — pushes the unlock out by another 15 minutes.
3. **A momentary API blip signs the dentist out permanently.** On a 503 or 401 from `/bff/auth/token` the web
   app fires `POST /bff/auth/local-logout` twice and deletes the cookie — while the token route's own source
   says a non-401 "is the server being unwell, not the session" and must leave the session alone.

---

## Part J — The catalogue, and the protocols themselves

### J1 · MAJOR · An existing protocol does not look editable; only an *absent* one does **[screen]**

`/procedure-types` has **no column for steps** — the header row is « COULEUR · NOM DE L'ACTE · CATÉGORIE ·
DURÉE · COÛT PAR DÉFAUT · ACTIONS », and the protocol renders *inside the act's name cell*, where a description
goes. For an act that has one, the cell reads « 2 séances · 45 min · ① Incision et drainage · ② Contrôle et
retrait du drain ». It **is** a 240×76 `<button>` with `aria-label` « Modifier les 2 étapes de … », but its
computed style is `border: solid 0px`, `background: rgba(0,0,0,0)`, **`cursor: default`**, no chevron, no
underline.

For an act with **no** protocol, the same cell is a dashed-border pill: « **+ Découper en étapes** ».

So the 20 acts that need nothing shout, and the 14 that a dentist actually needs to correct — an implant
protocol that doesn't match how they work — offer no visual invitation at all. Reviewed cold, the empty-act
route was found *by seeing it*; the filled-act route was found only *by reading the aria-label*.

**Fix:** give the filled cell the same bordered treatment as the empty one (or a pencil), and add « Séances » as
a real column header so the numbers under it are claimed by a name.

### J2 · MAJOR · Steps cannot be set while creating an act, and creation never mentions them **[screen]**

« Ajouter un type d'acte » asks for Nom · Durée · Coût · Catégorie · Description · État résultant · Couleur.
The words « étape » and « séance » appear **nowhere** in it. So the path is: create (7 fields), find the new row
in a 35-row paged table, click the cell, add the steps one at a time, save — two dialogs, two saves, and a
re-typed search.

Someone adding « Facette » on the day they start doing veneers has no reason to know the app can cut it into
séances at all.

**Fix:** keep one owner for the list (right call), but put one line in the create dialog — « Vous pourrez
découper cet acte en séances depuis sa ligne » — and offer « Découper en étapes » in the success toast.

### J3 · MAJOR · Saving a protocol clears the search and jumps back to page 1 of 35 **[screen]**

Search « B2-Test » (list: « 1 type »), edit the protocol, press « Enregistrer les étapes » → the search input is
back to its placeholder, the list reads « 1–25 sur 35 types d'actes », and the refetch goes out as
`GET /api/procedure-types?page=1&pageSize=25` with **no `search=`**. Reproduced twice.

Setting up the catalogue means doing this fourteen times. Each save costs a re-typed search and a re-scan of a
paged table to find where you were. **And there is no confirmation** — no toast at all (polled three separate
saves: `[]`), while creating a patient toasts « Patient créé » and creating a devis toasts « Devis 2026-0014
créé et validé ». So the only way to know the protocol saved is to re-find the row and read the cell.

**Fix:** preserve the search term and page across the refetch, and toast « Protocole enregistré — N séances ».

### J4 · MINOR · A drag handle is drawn on every step row and nothing can be dragged **[screen]**

Each row starts with a `grip-vertical` six-dot glyph, `aria-hidden`, computed `cursor: auto`. Pressing and
dragging it 60 px changed nothing — verified by comparing the order before and after. Reordering is done by the
⌃/⌄ chevrons beside it, which work correctly.

A six-dot handle means "drag me" to anyone who has used a phone. The first attempt to reorder reads as a broken
app. **Fix:** wire it up, or drop the icon — the chevrons are good.

### J5 · MINOR · Enter does not add the next step **[screen]**

With a label typed, Enter does nothing: no new row, no submit, focus unmoved. A 4-step protocol costs 4 separate
clicks on « Ajouter une étape ». A keyboard path exists but is six keystrokes of overhead per step
(label · Tab · minutes · Tab · Tab · Enter). **Fix:** Enter in the last row appends and focuses a row. Not
submitting on Enter is right and should stay.

### J6 · MINOR · Three notations for one duration, on one screen **[screen]**

For a single 90-minute step the booking dialog shows, all at once: the chip « Pose provisoire **90 min** », the
summary « 1 acte · **90 min** », the highlighted preset « **1.5h** », and the récapitulatif « Durée **1 h 30** ».
**Fix:** pick one — « 1 h 30 » reads best in French — below four hours.

### J7 · MINOR · The catalogue table overflows ~15 px at 1440, clipping « ACTIONS » **[screen]**

The scroll container measures `scrollWidth 1101 / clientWidth 1086`, so the header reads « ACTION » with the S
cut off and the « Supprimer » labels are shaved. The document itself does not scroll sideways. 15 px of column
padding.

### J8 · MAJOR · An improved protocol can never reach a live devis, and nothing says so where it matters **[screen+SQL]**

Verified end to end: a devis created with a 4-step protocol keeps its own snapshot; renaming a step and adding a
fifth in the catalogue changed **nothing** on the live devis (still « Étapes réalisées : 0 / 4 », still the old
label), while a *new* devis picked up all five. **That behaviour is right** — a signed quote must not move under
the patient — and the steps dialog says so: « les modifier ici ne change aucun devis en cours ».

But it is stated only inside the dialog you are already editing, and there is **no way to pull an improved
protocol into a live devis** — the only route is to hand-retype the steps. So fixing a typo is safe and
*improving* a protocol is a one-way door for every patient already quoted. The devis side never says « ce devis
suit un protocole plus ancien que le catalogue ».

The consequence is visible today: « TRAITEMENTS EN COURS » shows « Implant dentaire · Pose de la couronne ·
**étape 2 / 2** » while the catalogue row for the same act says « **6 séances** · 4 h 05 ». Same act, two
protocols, no screen acknowledges it — and a dentist reading « étape 2 / 2 » believes the implant is nearly done.

**Fix:** when an item's steps differ from its act's current `DefaultSteps` **and no step is yet done**, offer
« Le protocole du catalogue a changé (5 séances) — appliquer ? ». Stay silent once work is recorded.

### J9 · MAJOR · Two protocols are written for a *case* while their act is priced per *unit* **[judgement, evidence-backed]**

The catalogue names its unit where it matters — « Couronne / bridge (**par élément**) », « Application de fluor
(**par arcade**) ». Two acts break that discipline, and both are expensive:

1. **`Facette`, 700 DT, no unit suffix**, with « Préparation + provisoires **150 min** » and « Collage définitif
   **150 min** ». Two and a half hours each are 6–8-tooth appointments; one veneer is ~45–60 min to prepare and
   ~45 to bond. So a six-veneer smile is six lines × 700 DT and the devis proposes **24 séances / ~40 hours of
   chair time** (D1d then duplicates the protocol per split row). Either rename it « Facette (par élément) » and
   cut the minutes to per-tooth, or make it a case act and price it as one.
2. **`Implant dentaire`, 1 500 DT** — steps 4–6 (« Désenfouissement · Empreinte implantaire · Pose de la
   couronne ») **are the prosthetic phase, which the catalogue sells separately** as « Couronne / bridge (par
   élément) » at 500 DT with its own 3-step protocol. So a 1 500 DT implant line proposes six séances that
   include a 500 DT crown nobody put on the devis. The séance count is what a patient reads as *what I am
   paying for*. Split at « Contrôle post-opératoire » and let the crown be its own line.

### J10 · MINOR · Two more protocol shapes worth challenging **[judgement]**

- **`Réévaluation parodontale` is a billable visit and appears inside two acts** — step 1 of Gingivectomie
  (50 DT) and step 5 of Traitement parodontal (120 DT). Quote both, which is the normal sequence, and the same
  visit is proposed twice. It is also arguably not a *stage* of a gingivectomie: a réévaluation before the act is
  the decision to do it. The catalogue already sells « Consultation / examen bucco-dentaire » (40 DT) and
  « Contrôle / suivi » (0 DT).
- **Four 2-step protocols whose first step just restates the act** — Frénectomie → « Frénectomie · Contrôle
  post-opératoire »; Gingivectomie step 2 = « Gingivectomie »; Greffe osseuse step 1 = « Greffe osseuse »;
  Incision d'abcès step 1 = « Incision et drainage ». Each really encodes *remember the post-op check*, and the
  cost is that a one-visit act reads as « 2 séances » on the devis and « Étapes réalisées : 0 / 2 » on the plan —
  so a **finished frénectomie shows as half done** until a 15-minute control is recorded. It inflates every
  completion counter (and compounds C2).

### J11 · MAJOR · Five acts lack a protocol and clearly need one — and the gaps are inconsistent within pairs **[judgement, evidence-backed]**

| Act | Why | The pair that shows the inconsistency |
|---|---|---|
| **`Traitement de canal (dévitalisation)`, 150 DT** | A molar endo is routinely two sittings — **and this is the clearest gap in the 34** | The catalogue ships a 2-step **RE**-treatment (« Dépose et désinfection · Réobturation ») and nothing for the first treatment |
| **`Traitement orthodontique (multi-attaches)`, 3 500 DT** | The most expensive act in the catalogue; by definition 18–24 monthly visits | Its devis proposes **one séance** |
| **`Extraction chirurgicale (sagesse)`, 200 DT** | Suture removal at 7–10 days | « Incision d'abcès » at **40 DT** has a post-op step |
| **`Contention post-orthodontique`, 300 DT** | Empreinte → pose | Exactly the shape of « Mainteneur d'espace fixe », which **has** that 2-step protocol |
| **`Blanchiment dentaire`, 400 DT** | Empreintes → remise des gouttières → contrôle | Exactly the shape of « Gouttière occlusale », which **has** a 3-step protocol |

The `Traitement de canal` gap is confirmed by the live database, and it explains something I saw on day one:
« TRAITEMENTS EN COURS » shows « Sonia Trabelsi · Traitement de canal · **continuation** · étape 2 / 2 ».
Somebody hand-typed a two-step protocol on the devis because the catalogue could not supply one, and named the
second step « continuation ». That is the feature telling you exactly which protocol is missing.

The pattern across all five is that coverage is inconsistent *within pairs of near-identical acts*, which reads
less like a clinical decision than like the seed list running out of steam near the end.

### J12 · MINOR · Two labels are wrong for a document the patient reads **[judgement]**

The step labels are otherwise genuine francophone Tunisian dental vocabulary — « Surfaçage par quadrant »,
« Désenfouissement », « Rapports intermaxillaires », « Essai des dents en cire », « faux moignon » — not
translated filler. Two exceptions, and both land on a devis a patient is shown:

- « Validation du **mock-up** » — the only anglicism in the set. « Validation du projet esthétique » is the
  standard phrase.
- « Dépose du pansement » — clinically correct, opaque to a patient reading the devis PDF.

## Part H — The use-case scenarios, and how the feature answers each

These are the fifteen situations a Tunisian dental practice actually meets. The feature should name all of them;
today it names about six. ✅ handled · ⚠️ handled with friction · ❌ broken or absent.

| # | Scenario | Today | Where it stands |
|---|---|---|---|
| 1 | Implant over 6 visits, paid in instalments | ✅ | The path the feature was built for. 7 clicks to devis + 1st séance, nothing typed |
| 2 | Bridge quoted 1 000, patient paid 800, work unfinished — **the original complaint** | ✅ | Solved. The next séance charges nothing and says so, naming this act's fee and the devis balance separately |
| 3 | Two steps in one visit (préparation + empreinte) | ✅ | Two wire rows, one card, one fiche closes both |
| 4 | Ordinary single-visit act (détartrage) | ✅ | Completely untouched — no step wording anywhere. 20 of 34 acts correctly have no protocol |
| 5 | A step done, fiche recorded by mistake | ✅ | « Détacher la fiche » reopens it, warns correctly, moves no money, and refuses when the fiche is on a live note |
| 6 | Fee needs correcting after the note is issued | ⚠️ | Allowed and warned correctly — but the note is never reconciled, and an **added act** becomes permanently unbillable (A6) |
| 7 | An act the catalogue doesn't cover, needing a bespoke split | ⚠️ | Only after the devis exists (a number spent), then two screens deep behind an icon-only control. A dentist will not find it (Part E) |
| 8 | Reschedule an already-booked séance | ❌ | Its price becomes editable again and the « Déjà facturé » notice vanishes; a pre-feature booking re-prices from the catalogue (A3) |
| 9 | Booked the wrong step | ❌ | The step chips don't render on a re-opened séance. Remove-and-re-add, which lands on the plan's *next* step, not the one you wanted (D3) |
| 10 | Patient pays cash at the chair | ❌ | Refused, with a message naming two wrong remedies and never naming the échéancier (A7) |
| 11 | Implant waiting 3 months for osseointegration | ❌ | Amber « il y a 14 jours » on a treatment that is exactly on schedule. No « pas encore due » state (D1, D1b) |
| 12 | Patient stops halfway | ❌ | Either the button is **hidden** (if the next séance is booked — the commonest shape, B4), or it offers to **delete the work already delivered** (A1) |
| 13 | Patient who stopped comes back | ❌ | No resume, no reopen, no cancel. The dropped acts were deleted; re-adding them orphans the fiches (B2) |
| 14 | A one-off act turns out to be unfinished | ❌ | Creates a treatment priced at the *original* act's fee with no way to price the remaining work — today's live data shows « Extraction simple, 120 DT » whose next step is « Pose de la prothèse » (A5) |
| 15 | Improving a protocol in the catalogue | ⚠️ | Correctly does not disturb existing devis, and says so. But there is also no way to *apply* an improved protocol to a live devis — the dentist retypes it |

Scenarios 8–14 are the review. Six of the seven are about **the treatment not going to plan**, which is the
normal case in a dental practice, not the exception — and it is the half the feature has not yet been designed
for. Scenario 2, the one that prompted the work, is genuinely finished.

## What the app should do automatically and doesn't

The owner's goal was *"to have the app handle things automatically whenever it is possible."* Five places where
the information already exists and the dentist is still asked:

1. **The next séance's date.** The protocol researched the interval (a week between quadrants, 8 weeks to
   réévaluation, months for osseointegration) and the schema throws it away (D1b). Booking should pre-fill.
2. **The appointment's length on a re-opened séance.** The step knows its own minutes; the fallback uses the
   act's stale scalar (D1c).
3. **The devis balance that is actually owed.** `isPlanBilled()` exists; five surfaces don't call it (A2).
4. **Which acts a stop should keep.** The step data says which acts have work against them; `stoppableItems`
   asks the wrong question (A1).
5. **A note for a finished treatment.** Completing the last step is the signal to raise the invoice; instead it
   removes the only button that can (A4).

## Part I — Improvements beyond the defects

Ranked by leverage, not by size. The first three each fix several findings at once.

### I1 · Give a step an interval, not just a duration

`ProcedureStepTemplate` and `TreatmentPlanItemStep` carry chair time. Add elapsed time —
`MinDaysAfterPrevious` on the template, a derived `dueFrom` on the step. It is one field and it fixes four
things: the worklist alarm stops contradicting the protocol (D1), a step gains a « pas encore due » state, the
booking dialog can pre-fill a date instead of asking, and the « Dernière séance » column can say « à l'heure »
instead of amber. **The clinical data already exists in the seed's own comments** — a week between quadrants,
8 weeks to réévaluation, 7–10 days to a dépose, months for osseointegration. Only the field is missing.

### I2 · One reader for the displayed balance

`displayedOutstanding(plan)` → null when `isPlanBilled(plan)`, consumed by all six sites that print « Reste »,
with the note's own figure shown instead. Requires adding the linked note's total to `TreatmentPlanDto` — which
also implements A6's "state the divergence" promise that two source comments already claim is done. Fixes A2,
unblocks A6, and removes the red « Reste » on a settled treatment. Then a derived check on `.outstanding`, or
this recurs.

### I3 · Count progress in steps everywhere

The workspace's progress *bar* is already step-weighted. « Avancement » in the list, « Actes réalisés » in the
header and the « étape N / N » chip on the worklist are not. Make all four read from the same step-weighted
source and C1, C2 and half of D2 go away together — a six-visit implant stops reading « 0/1 actes » from the
first appointment to the last.

### I4 · Make the actions do what they say

- « Planifier la séance » on the worklist should open the booking dialog pre-filled, not navigate to the
  workspace (C3). The workspace's own row action already does exactly that; reuse it.
- « Terminer » should either close a plan with unrealised acts, as its dialog promises, or say it cannot (B5).
- « Arrêter le traitement » should appear whenever a treatment is live, and offer to cancel the booked séance
  as part of stopping — not hide itself because a séance is booked (B4).

### I5 · Give the treatment a way back

A stopped plan is `Completed` and unreachable. Add « **Reprendre le traitement** » that reopens it to
`InProgress` and restores the dropped acts — which means *parking* them (a `Withdrawn` status) rather than
deleting them (A1, B2). Parking also fixes A1's data loss for free: an act with any done step is kept and
marked withdrawn, so the fiches stay attached and the history survives. Patients come back; the model should
expect it.

### I6 · Let the continuation price the work it adds

« C'est la suite d'une séance précédente ? » should add the remaining work as its **own act with its own fee**,
rather than extending the original act's designation and price. Today's live data — « Extraction simple,
120 DT », next step « Pose de la prothèse » — is the whole argument (A5). Same dialog, one extra field, and the
under-charge disappears.

### I7 · Offer « Découper en séances » on the devis form

The creation form only shows the steps panel for an act that already has a protocol. `/procedure-types` already
has the right affordance for a protocol-less act — « + Découper en étapes ». Put the same thing on the devis
form and the bespoke case stops being two screens deep behind an icon (E1).

### I8 · Collect where the money is handed over

A « Encaisser sur le devis » action on the fiche de soins, and a refusal message that names the échéancier
instead of telling the dentist to "add the missing act" (A7). This is the difference between the feature's claim
and its behaviour.

### I9 · Say what the one-press path is about to do

« Créer le devis et planifier la 1re séance » spends a devis number, creates an accepted document with a money
claim, and fires before the appointment is saved. It needs the same one-line confirmation « Accepter le devis »
already has — naming the number and the amount — and it must carry the price the dentist typed (A8, B6).

### I10 · One word per concept

**étape** = the sub-part · **séance** = the visit · **devis** = the document · **traitement** = the course.
Then the catalogue dialog stops counting « 6 séances » under a heading that says « Étapes », the patient tab and
its card stop disagreeing with the rail, and « Détacher » stops naming two operations (C4, B7).

## Prioritised fix list

| # | Finding | Why in this order |
|---|---|---|
| **1** | **A0** the fiche re-charges the full fee at every séance | **Ships an over-charge on the default path.** One click, no user error, marked paid in cash, and the duplicate note has a NULL plan link so nothing downstream can spot it. A 6-visit implant bills 6×. Nothing else matters until this is fixed |
| 2 | **A3** re-opened séance unlocks the price and invites a fee | Feeds a wrong figure straight into A0, on the routine act of moving an appointment |
| 3 | **A1** stop-treatment deletes half-delivered work | Destroys clinical records and money, in the likeliest abandon case, behind a dialog promising the opposite |
| 4 | **A2** « Reste » wrong on every bridged plan | Wrong number in the largest type; « Solde dû 31,000 » beside « Reste 120,000 » on one page |
| 5 | **A4** a finished devis cannot be invoiced | The feature's own written promise is unreachable |
| 6 | **B1 / B2** two permanent dead ends | No way out of either, and no warning going in |
| 7 | **A7 / A8b** cash at the chair refused with wrong advice; the échéancier dialog repeats the false figure | Both send the dentist toward the action that double-prices |
| 8 | **C2 / C1 / C3** progress invisible, counter ambiguous, primary action mislabelled | The three things a dentist reads and presses every day |
| 9 | **D2z** « Monter » moves the step **down** on touch | Does the opposite of what it shows, only on the tablet the app is used on, and it corrupts the order that drives every later proposal. The correct sizing already exists one dialog over |
| 10 | **A8c** three refusals to book one séance | Pure friction on the most-repeated action, from one bad default — and « Continuer » out of habit books into the past |
| 11 | **B3 / B2b** steps silently uneditable when amending; one fiche closes only one act | Silent discard of work the dentist did |
| 12 | **E4 / E0b** typing the act name opts out of the feature; the fast path wasn't findable | Two silent ways to never meet the feature at all |
| 13 | **A5 / A6** retroactive path under-prices and cannot be corrected | Systematic revenue loss on the original use case |
| 14 | **D1 / D1b** 14-day alarm vs a 3-month protocol | Trains the dentist to ignore the worklist. One field (I1) fixes it |
| 15 | **D5 / D6** week filter hides 370 000 DT; the "optional" échéancier books the total as due today | Both make every balance read start wrong |

**A0 and A3 together are the ship blocker.** They are the same defect seen twice: the rule *"a devis act is 0
for this séance"* is enforced in exactly one place — `agreedCostOf` in the booking picker — and both the fiche
and the edit dialog reconstruct the act without asking it. There is **no server-side rule** that a plan-linked
act must price at 0; `AppointmentProcedureSelection` accepts any `AgreedCost ≥ 0` on a row carrying a
`TreatmentPlanItemId`. That is the durable fix: make it an invariant in the domain, not a convention in one
client function.

---

---

## Still open

Two things nobody exercised, both worth someone's hour:

1. **« Suite d'une séance précédente » on a fully-paid act.** One candidate offered during the review was
   « Implant dentaire · 21 août 2026 · Facturé 4 500,000 DT · **entièrement réglée** ». Turning a settled
   4 500 DT act into a multi-séance treatment is the money case most worth testing and the one nobody fired,
   deliberately, on shared data. Given A0, the expected outcome is bad.
2. **The grouped-séance walk across two acts** (B2b) is confirmed in source but not executed on screen — what
   the second act's row and the worklist look like after one fiche is saved, and whether a second fiche for the
   same visit is offered, refused, or silently allowed.

## Test data left on the dev database, deliberately

| What | Where | Why it matters |
|---|---|---|
| Duplicate note **2026-0094**, 150,000 collected, no plan link | Sonia Trabelsi | The proof of A0. **Needs an avoir before this database is reused.** |
| Plan **2026-0013** wrecked: révision 2, 560 DT, a re-added crown at 500 DT with fresh steps, two orphaned `DentalRecords`, three `AppointmentProcedures` rows with dangling ids, a 350 DT deposit against a 560 DT total | `B5-Test Alpha` | The proof of A1 / A1b / B2c / B1b |
| `AgreedCost` 0 → 120 on appointment `097e1e45` (no note raised) | Karim Hamdi | The proof of A3 |
| Procedure type `B2-Test-Protocole` (`DefaultSteps = []`), patient `B2-Test Patient`, devis 2026-0014 and 2026-0016, one appointment overlapping Hédi Chaabane's 18:00 | B2's objects | Safe to delete |
| Devis **2026-0015** (Amine Rekik, implant, 6 étapes) + one booked séance | B1's objects | Safe to keep |

`Gingivectomie` and `Frénectomie` were edited and **restored byte-exactly**, verified by diffing all 34
catalogue acts against a baseline taken first: no drift. None of the other protocols was touched.

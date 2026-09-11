"use client"

import { useMemo, useRef, useState } from "react"
import { toast } from "sonner"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
} from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Check, ChevronsUpDown, Clock, Plus, Stethoscope, X } from "lucide-react"
import { cn } from "@/lib/utils"
import { AppointmentProtocolEditor } from "@/components/appointment-protocol-editor"
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import { formatAmount, formatDT, parseAmountInput, quoteFr } from "@/lib/format"
import type { AppointmentProcedurePayload } from "@/lib/api/appointments"
import type { ContinuableActDto, ProcedureStepTemplateDto, ProcedureTypeDto } from "@/lib/api/types"

/**
 * One act chosen for a séance. `treatmentPlanItemId` is what makes grouping meaningful: a devis act booked
 * alongside others keeps its own link, so the plan reports each of them as planned rather than only the first.
 */
export interface SelectedAct {
  /**
   * The catalog act. **Null** for a link-only row: a hand-typed devis line has no `ProcedureType` behind it, and
   * it still belongs in the séance — dropping it would book the visit and leave that step reading « À planifier ».
   */
  procedureTypeId: string | null
  treatmentPlanItemId?: string | null
  /** Why this act is in the list, when it came from a devis — shown as a « devis » chip. */
  planLabel?: string
  /** Name for a link-only row, since there is no catalog entry to read one from. */
  fallbackName?: string
  /**
   * The price agreed for this act at this visit, **as typed** — a raw string, not a number, because « 90,500 »
   * is how this product prints money and `parseAmountInput` is what reads that back. A `type="number"` input
   * refuses the comma outright.
   *
   * <p>⚠️ `undefined` means **untouched**, and is not the same as `""`. Untouched shows the catalogue tarif in
   * the field and sends *nothing*, so the act stays at whatever its tarif is on the day the fiche is filled.
   * Prefilling the value into this field instead would freeze today's catalogue price onto every booking anyone
   * ever makes, and « personne n'a négocié » would become unsayable.</p>
   */
  agreedCost?: string
  /**
   * Which **step** of that devis act this séance carries out. Undefined for an act done in one sitting, which
   * is every booking made before steps existed.
   *
   * <p>⚠️ Only meaningful with `treatmentPlanItemId` — the server refuses a step without its act. Two rows may
   * share one plan act **only** when their steps differ, which is what lets « préparation » and « empreinte »
   * be booked into one séance.</p>
   */
  treatmentPlanItemStepId?: string | null
  /**
   * The steps of this act still to carry out, so the row can offer them. Empty or absent = a one-sitting act
   * and no step control at all.
   */
  stepOptions?: PlanStepOption[]
  /**
   * The devis this act is priced on, when there is one — « déjà facturé sur le devis DV-2026-0043 ». Present
   * only for an act that came from a plan, and it is what makes the price field read-only: the fee is on the
   * plan, so this séance adds no honoraires.
   */
  billedOnPlan?: BilledOnPlan
  /**
   * The séances this act will be split into when the booking is saved — the catalogue protocol as the dentist
   * has it **for this patient**, editable in place.
   *
   * <p>⚠️ **Tri-state, and all three states are real.** `undefined` is « nobody has decided », which the
   * picker's one seeding effect turns into one of the other two; `null` is an explicit « tout faire en une
   * seule séance »; a non-empty list is the confirmed protocol. `[]` is never stored — emptying the list *is*
   * choosing one séance, and that is `null`.</p>
   *
   * <p>⚠️ **The treatment is created on SAVE, not when the act is picked.** That is the whole design: the
   * decision is local form state until « Créer le rendez-vous », so it works before a patient has been chosen
   * (« Nouveau patient » has no id until the save) and abandoning the dialog leaves nothing behind. The button
   * it replaces created the plan on press and was rendered only when `selectedPatientId` was already set — so
   * a dentist booking a walk-in read « cet acte se fait en 3 séances » with no way to act on it, which is the
   * report this was built from.</p>
   *
   * <p>⚠️ Only ever set on an act **not** already on a devis: once `treatmentPlanItemId` is set the step chips
   * are the real control, and this would be a second, weaker route to the same thing.</p>
   */
  plannedProtocol?: ProcedureStepTemplateDto[] | null
  /**
   * The séance already carried out that this row continues — « c'est la suite d'une séance précédente ».
   *
   * <p>⚠️ <b>Present only while the treatment does not exist yet.</b> It is the continuation door's half of
   * `plannedProtocol`'s rule: the devis is minted by `materialiseTreatments` when the booking is SAVED, and
   * until then this is form state and « Annuler » leaves nothing behind. The dialog used to create it on
   * press, which minted a numbered, accepted devis with a lump-sum échéance — so abandoning the booking left
   * a live créance for a séance nobody booked, and the act it continued vanished from the picker for good
   * (`ContinuationTracking` counts it as tracked), recoverable only by cancelling the devis with a motif.</p>
   *
   * <p>⚠️ Mutually exclusive with `treatmentPlanItemId` by construction: once the plan exists the row is
   * rebuilt by `presetToSelectedAct` and this field is gone.</p>
   */
  pendingContinuation?: PendingContinuation
}

/**
 * A continuation the save will mint: what `continueRecordedAct` is keyed on, plus what the card states about
 * it before anything exists on the server.
 */
export interface PendingContinuation {
  /** The fiche and the act it recorded — the command's own two arguments. */
  dentalRecordId: string
  actId: string
  /** The act being finished, for the row's identity (« Traitement de canal — séance 2 sur 2 : … »). */
  ofLabel: string
  /** The name this séance carries, as typed in the dialog. */
  nextStepLabel: string
  /** What the remaining work is worth, or null for « rien de plus » — the field is optional. */
  remainingWorkCost: number | null
}

/**
 * The catalogue protocol of an act, or `[]` when it proposes none.
 *
 * ⚠️ A row with no catalogue act behind it (a hand-typed devis line) has none by construction, and neither
 * does an act already carried by a devis — that one's séances are the plan's, not the catalogue's.
 *
 * ⚠️ And neither does a **pending continuation**: it carries the original act's `procedureTypeId` (so the
 * fiche opened from the booking can prefill), which without this guard would make the card offer to split an
 * act that is already one séance in — and `materialiseTreatments` would then mint a second treatment for it.
 */
export function catalogueProtocolOf(
  act: SelectedAct,
  procedureTypes: ProcedureTypeDto[],
): ProcedureStepTemplateDto[] {
  if (act.treatmentPlanItemId || act.pendingContinuation || !act.procedureTypeId) return []
  return procedureTypes.find((p) => p.id === act.procedureTypeId)?.defaultSteps ?? []
}

/**
 * Fill in every `plannedProtocol` nobody has decided — **the one place the default lives**.
 *
 * <p>A multi-séance act is split <b>by default</b>: the treatment and its séances are prepared when the
 * booking is saved, and « Tout faire en une seule séance » is the way out. The opposite default was a button
 * somebody had to notice, and it was not even always rendered — which is the report this was built from.</p>
 *
 * <p>⚠️ <b>Derived, never seeded into state by an effect.</b> The only channel a picker has back to its host is
 * `onChange`, and the edit dialog's `onChange` also resets `durationTouched` — so an effect that seeded through
 * it would silently discard the saved duration of every appointment merely by opening it. Deriving costs one
 * `map` and has no such reach.</p>
 *
 * <p>⚠️ <b>One treatment per booking.</b> An appointment carries a single `TreatmentPlanId` and
 * `resolveAttachedPlanId` refuses two, so only the first undecided protocol act is followed; the rest resolve
 * to `null` and their card offers to take its place.</p>
 *
 * <p>⚠️ <b>A pending continuation occupies that one slot too</b>, and it had to be said here rather than left
 * to the save: both are minted by `materialiseTreatments`, so a séance carrying a continuation AND a split act
 * would create two devis and only then be refused by `resolveAttachedPlanId` — two orphans and a booking that
 * never happened, which is the whole defect this deferral removes, reached from the other side.</p>
 *
 * <p>⚠️ It returns `acts` unchanged — the same array reference — when there was nothing to decide, so it is
 * safe inside a `useMemo` feeding a list that compares by identity.</p>
 */
export function resolvePlannedProtocols(
  acts: readonly SelectedAct[],
  procedureTypes: ProcedureTypeDto[],
): SelectedAct[] {
  // Until the catalogue has arrived every act looks protocol-less, and resolving `null` on that would answer
  // « une seule séance » for an implant before anybody had been shown the question.
  if (procedureTypes.length === 0) return acts as SelectedAct[]

  let following =
    acts.some((a) => a.plannedProtocol && a.plannedProtocol.length > 0) ||
    acts.some((a) => a.pendingContinuation)
  let changed = false
  const next = acts.map((act) => {
    if (act.plannedProtocol !== undefined) return act
    changed = true
    const protocol = catalogueProtocolOf(act, procedureTypes)
    if (protocol.length === 0 || following) return { ...act, plannedProtocol: null }
    following = true
    // A copy, never the catalogue's own array — the editor rewrites this list and the catalogue must not move.
    return { ...act, plannedProtocol: protocol.map((s) => ({ ...s })) }
  })
  return changed ? next : (acts as SelectedAct[])
}

/** The acts this booking turns into treatments on save, with the séances each was given. */
export function followedProtocolActs(
  acts: readonly SelectedAct[],
): { act: SelectedAct; index: number; steps: ProcedureStepTemplateDto[] }[] {
  return acts.flatMap((act, index) =>
    act.plannedProtocol && act.plannedProtocol.length > 0
      ? [{ act, index, steps: act.plannedProtocol }]
      : [],
  )
}

/** The rows this booking turns into continuations on save — {@link SelectedAct.pendingContinuation}. */
export function pendingContinuationActs(
  acts: readonly SelectedAct[],
): { act: SelectedAct; index: number; continuation: PendingContinuation }[] {
  return acts.flatMap((act, index) =>
    act.pendingContinuation ? [{ act, index, continuation: act.pendingContinuation }] : [],
  )
}

/**
 * Why this booking cannot be saved yet, or null.
 *
 * ⚠️ Refused in the form rather than server-side, because the server would refuse the **treatment** with the
 * patient already created and the booking half-made: a blank séance name reaches `SetSteps` as an
 * `ArgumentException` long after the point of no return. Same reason `hasInvalidAgreedCost` is checked here.
 */
export function protocolError(acts: readonly SelectedAct[]): string | null {
  const blank = followedProtocolActs(acts).some((f) => f.steps.some((s) => s.label.trim().length === 0))
  if (blank) return "Nommez chaque séance du traitement, ou supprimez la ligne vide."

  /*
   * ⚠️ **Refused HERE because the alternative refuses it after the money is written.** An appointment carries
   * one `TreatmentPlanId` and `resolveAttachedPlanId` says so — but it runs *after* `materialiseTreatments`,
   * which by then has minted the continuation's devis, numbered and accepted. So a séance carrying a
   * continuation and a devis act (picked from « Actes du devis », or accepted from the suggestion) would leave
   * exactly the orphan the deferral exists to remove: a live créance for a booking that never happened.
   *
   * `resolvePlannedProtocols` handles the third combination on its own — a pending continuation already
   * occupies the one treatment slot, so no split act is followed beside it.
   */
  if (acts.some((a) => a.pendingContinuation) && acts.some((a) => a.treatmentPlanItemId)) {
    return (
      "Ce rendez-vous poursuit une séance précédente : il ne peut pas porter en plus l'acte d'un autre devis. " +
      "Retirez l'un des deux."
    )
  }
  return null
}

/**
 * A devis act offered to a booking dialog — « this séance carries out this act of that devis ».
 *
 * ⚠️ It lives here, beside `SelectedAct`, because **both** booking dialogs now consume it: the create dialog
 * seeds it from the devis workspace, and the edit dialog offers the patient's outstanding devis acts so a visit
 * booked from the agenda can be attached to a devis afterwards. It was declared in `create-appointment-dialog`,
 * which made the edit dialog's version of this feature unreachable without a circular import.
 */
export interface PresetPlanAct {
  planItemId: string
  /** The catalog act it stands for, when the workspace could resolve one. */
  procedureTypeId?: string
  /** Désignation, for the « devis » chip and the header summary. */
  label: string
  /**
   * The price the devis put on this step. It seeds « Prix pour ce rendez-vous » so the visit is booked at the
   * price the patient was quoted, not at the catalogue tarif the devis may well have discounted away from.
   *
   * <p>⚠️ Seeded as **typed**, not as an untouched field: a plan step's price is an agreed price, so it is sent
   * and carried into the fiche. Editing it here changes this visit only — the devis keeps its own figure, and a
   * price haggled on the telephone cannot rewrite a quote the patient may have signed.</p>
   */
  plannedCost?: number | null
  /**
   * The act's steps still to carry out, so the dialog can offer them as tick boxes. Absent or empty = an act
   * done in one sitting, which is almost every act, and the control is not rendered at all.
   */
  steps?: PlanStepOption[]
  /**
   * The step to arrive ticked — the one the workspace's own action named (« Planifier l'étape »). The user can
   * add or remove others in the dialog; this is the sensible default, not a constraint.
   */
  preselectedStepId?: string | null
  /**
   * The devis this act is priced on. Present whenever the act came from a plan, and it is what turns the price
   * field read-only with a sentence naming the devis, its total and what is left to collect.
   *
   * <p>⚠️ This is the answer to the question that stopped the dentist: 800 DT taken on a 1 000 DT bridge, and
   * no way to tell whether booking the next séance would charge again.</p>
   */
  billedOnPlan?: BilledOnPlan
}

/** One step a séance may be booked for. */
export interface PlanStepOption {
  id: string
  label: string
  /** Chair time, when the protocol estimates one — what the séance's duration is summed from. */
  estimatedDurationMinutes: number | null
  /**
   * Already carried out — **offered to no one, but still named.**
   *
   * <p>⚠️ `planItemToPreset` used to drop a réalisé step from this list entirely, and the reason was sound
   * (« offering it would invite a second fiche against a step already evidenced by one ») while the remedy
   * conflated two questions: what may be TICKED, and what this séance IS. Every label lookup in this file
   * resolves the appointment's booked step against this list — so reopening a séance after its fiche was
   * recorded made the step unresolvable, and the row read « Séance : étape ». Measured on the dev database:
   * appointment `bfd2b503` books « Empreinte primaire », done on 2026-09-06, and the card named none of it.</p>
   *
   * <p>So the list carries the whole protocol and the flag says which are spent: the chips filter on it, and
   * every label, ordering and duration lookup finally answers for the step actually booked.</p>
   */
  done: boolean
}

/**
 * Why this act's price is not typed here: it is carried by a devis.
 *
 * <p>⚠️ This is the answer to the question that stopped the dentist. He had taken 800 DT on a 1 000 DT bridge
 * and did not know whether booking the next séance would charge again — so the row states the devis, its total
 * and what is left to collect, and refuses the price field rather than prefilling a 0 that looks like a
 * mistake.</p>
 */
export interface BilledOnPlan {
  planNumber: string | null
  /**
   * ⚠️ **THIS act's planned fee on the devis — never the devis total.** It was the total, and the sentence
   * beside it read « cet acte est porté par le devis 2026-0004 à 1 080,000 DT » on a 1 000 DT bridge sitting
   * on a devis that also carried an 80 DT détartrage. A wrong figure in a sentence that names the right
   * document is worse than no figure: the dentist is reading it precisely because he does not trust his own
   * memory of what the act was quoted at.
   */
  actCost: number
  /** What is still to collect on the **whole devis**, which is why the sentence says « sur le devis ». */
  outstanding: number
  /**
   * The note d'honoraires that holds this devis' money, when one does — a devis created to continue an act that
   * had already been billed, or one that has since been facturé.
   *
   * ⚠️ **Its presence makes {@link outstanding} a figure that must NOT be shown.** A plan's `outstanding` is
   * `TotalPlanned − Σ its own installments' payments`, and a plan billed into a note has an auto-raised échéance
   * that will never see a payment — the money went to the note. So the devis reads « reste 120,000 » about an act
   * the patient has all but paid for: measured, a séance whose real balance was **15,50 DT on note 2026-0087**
   * announced « Reste à encaisser sur le devis : 120,000 DT ». « Solde patient » is right either way (it drops a
   * billed plan), which is exactly what makes this one silent — the wrong figure appears only here, on the screen
   * where somebody is deciding what to collect.
   */
  billedOnInvoiceNumber?: string | null
  /**
   * Present when this act **continues** one already carried out and billed — what « c'est la suite d'une séance
   * précédente » leaves behind. Absent on every ordinary devis act.
   *
   * <p>⚠️ Without it the booking modal renders the continuation as a standalone act: « continuation (dents 11) …
   * Reste à encaisser sur le devis : 20,000 DT ». Both halves mislead — it names neither the act being finished
   * nor which séance this is, and it quotes the devis' own balance while the note beside it still holds 50 the
   * same patient owes for the same work. Reported from use, on the screen where somebody decides what to
   * collect.</p>
   */
  continuation?: PlanActContinuation
}

/** What a continuation act is the continuation OF, and what the whole treatment is worth across both documents. */
export interface PlanActContinuation {
  /** The act being finished — « Traitement de canal (dévitalisation) ». */
  ofLabel: string
  /** Which séance this is, and of how many — counted over real step rows, never `done + 1`. */
  seanceNumber: number
  seanceTotal: number
  /** The note d'honoraires that already collected the earlier séance, and what is still owed on it. */
  noteNumber: string | null
  noteOutstanding: number
  /** The whole treatment across both documents — served, never derived here. */
  treatmentTotal: number
  treatmentOutstanding: number
}

/**
 * One act **as the dentist sees it in a séance**: the act, and which of its devis steps this visit covers.
 *
 * <p>⚠️ Two steps of one act are <b>two rows on the wire</b> — the server keys its duplicate rules on
 * (act, step) precisely so « préparation + empreinte dans la même séance » is expressible — but they are
 * <b>one act</b> to the person booking. Rendering the wire list directly produced two identical cards headed
 * « Couronne / bridge (par élément) », each with the same three chips ticked the same way and the same
 * « Déjà facturé » notice under it: the visual duplicate the feature exists to remove, on the one screen the
 * feature exists for. Grouping is display-only — <code>toProcedurePayloads</code> still sends every row.</p>
 */
export interface ActGroup {
  /** Every position in the wire list this one card stands for. Removing the card removes all of them. */
  indices: number[]
  /** The first row of the group — what the card's identity, price and devis notice are read from. */
  act: SelectedAct
  /** The steps booked into this séance, in the protocol's own order. */
  stepIds: string[]
}

/**
 * Collapses an act's step-rows into one group each, leaving everything else alone.
 *
 * <p>⚠️ The key is <b>the devis act</b>, never the procedure type: two obturations in one séance are two
 * genuinely separate acts of the same type and must stay two cards, while two rows sharing a
 * <code>treatmentPlanItemId</code> can only ever be two steps of one act. A row with no devis link is its own
 * group, always.</p>
 */
export function groupActs(acts: SelectedAct[]): ActGroup[] {
  const groups: ActGroup[] = []
  const byPlanItem = new Map<string, ActGroup>()

  acts.forEach((act, index) => {
    const key = act.treatmentPlanItemId
    if (!key) {
      groups.push({ indices: [index], act, stepIds: stepIdsOf(act) })
      return
    }

    const existing = byPlanItem.get(key)
    if (existing) {
      existing.indices.push(index)
      existing.stepIds.push(...stepIdsOf(act))
      return
    }

    const group: ActGroup = { indices: [index], act, stepIds: stepIdsOf(act) }
    byPlanItem.set(key, group)
    groups.push(group)
  })

  // Back into the protocol's order: the rows arrive in whatever order they were ticked, and a strip reading
  // « Empreinte, Préparation » invites exactly the wrong reading of a sequence.
  for (const group of groups) {
    const order = group.act.stepOptions?.map((o) => o.id) ?? []
    group.stepIds.sort((a, b) => order.indexOf(a) - order.indexOf(b))
  }

  return groups
}

function stepIdsOf(act: SelectedAct): string[] {
  return act.treatmentPlanItemStepId ? [act.treatmentPlanItemStepId] : []
}

/**
 * How many acts this séance covers, as a person would count them — a bridge booked for two of its steps is
 * <b>one</b>. The wire list's length is not that number.
 */
export function actCountOf(acts: SelectedAct[]): number {
  return groupActs(acts).length
}

/**
 * Each act's name for the récapitulatif, with the booked steps named on it.
 *
 * <p>Shared by both booking dialogs, which each had their own copy resolving the same three catalogue cases —
 * and both listed « Couronne / bridge (par élément) · Couronne / bridge (par élément) » for one bridge booked
 * across two steps, which reads as the same act booked twice.</p>
 */
export function actLabelsOf(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): string[] {
  const byId = new Map(procedureTypes.map((p) => [p.id, p]))

  return groupActs(acts).map((group) => {
    const { act } = group
    const base = actDisplayName(act, act.procedureTypeId ? byId.get(act.procedureTypeId) : undefined)

    if (group.stepIds.length === 0) return base

    const labels = group.stepIds
      .map((id) => act.stepOptions?.find((o) => o.id === id)?.label)
      .filter((l): l is string => !!l)

    return labels.length > 0 ? `${base} — ${labels.join(", ")}` : base
  })
}

/**
 * A devis act as a row of the séance being booked. **The single mapping**, shared by the create dialog's preset
 * seeding and the edit dialog's « Actes du devis » group — the two would otherwise each decide whether a
 * plan-billed act carries `billedOnPlan`, and the one that forgot would silently re-price a bridge.
 */
export function presetToSelectedAct(
  preset: PresetPlanAct,
  procedureTypes: ProcedureTypeDto[],
): SelectedAct {
  return {
    // Only a catalogue act this clinic still has: a devis line whose procedure was deleted becomes a
    // link-only row rather than a reference to nothing.
    procedureTypeId:
      preset.procedureTypeId && procedureTypes.some((p) => p.id === preset.procedureTypeId)
        ? preset.procedureTypeId
        : null,
    treatmentPlanItemId: preset.planItemId,
    planLabel: "devis",
    fallbackName: preset.label,
    // The devis' own figure, seeded as typed — see `PresetPlanAct.plannedCost`. A step priced at 0 is left
    // alone: the plan has not costed it, so the catalogue tarif is the better answer than a free act.
    //
    // ⚠️ An act carried by a devis overrides this entirely: `agreedCostOf` returns a hard 0 for it, so this
    // séance adds no honoraires whatever is in the field. The seeded figure is what a *pre-steps* booking
    // still needs, so it stays.
    agreedCost:
      preset.plannedCost != null && preset.plannedCost > 0
        ? formatAmount(preset.plannedCost)
        : undefined,
    stepOptions: preset.steps,
    treatmentPlanItemStepId: preset.preselectedStepId ?? null,
    billedOnPlan: preset.billedOnPlan,
  }
}

/**
 * A séance still to be continued, as a row of the booking being composed — the continuation door's
 * {@link presetToSelectedAct}, for the window in which no treatment exists yet.
 *
 * <p>⚠️ <b>Every figure here is the one the server will produce, and the four cases are not symmetric.</b>
 * `ContinueRecordedActCommand` prices the already-billed act 0 and leaves its note unattached <b>only</b> when
 * there is a note AND new money (<code>noteKeepsTheFirstAct</code>); with no new money the note is attached
 * instead, and with no note at all the devis carries the act's own fee. So which line this séance is booked
 * against — and therefore what the card says — flips on both inputs, and a card that guessed one rule would
 * re-price itself the moment the booking was saved.</p>
 *
 * <p>⚠️ <b>« séance 2 sur 2 » is stated, not counted.</b> The command always synthesises exactly two steps —
 * « 1re séance » marked done against the fiche, and this one — whether they sit on one act or two. Reading a
 * rank off a list that does not exist yet would be the `count + 1` mistake with no list behind it.</p>
 */
export function continuationToSelectedAct(
  previous: ContinuableActDto,
  nextStepLabel: string,
  remainingWorkCost: number | null,
  procedureTypes: ProcedureTypeDto[],
): SelectedAct {
  const remaining = remainingWorkCost != null && remainingWorkCost > 0 ? remainingWorkCost : 0
  // ⚠️ The id, never the number: `InvoiceLinkChoice.ByKey` keeps a DRAFT note, which bills nothing yet and has
  // no number — and the server's own fork is « is there a note », so keying on the number would take the
  // unbilled branch on exactly the séance the server treats as billed.
  const noteKeepsTheFirstAct = previous.invoiceId != null && remaining > 0
  const ofLabel = previous.procedureName.trim() || "Acte"
  // Which devis line this séance lands on: the priced « travail restant » when there is one, else the act
  // itself — `schedulablePlanItems` drops the finished line, and these are its two outcomes.
  const base = remaining > 0 ? nextStepLabel.trim() || ofLabel : ofLabel
  const teeth = previous.toothNumbers.length > 0 ? ` (dents ${previous.toothNumbers.join(", ")})` : ""
  const planTotal = noteKeepsTheFirstAct ? remaining : previous.cost + remaining

  return {
    // The original act's catalogue link travels with the continuation — the server copies it onto the line for
    // the same reason, so the fiche opened from this booking prefills instead of asking « Choisissez l'acte ».
    procedureTypeId:
      previous.procedureTypeId && procedureTypes.some((p) => p.id === previous.procedureTypeId)
        ? previous.procedureTypeId
        : null,
    planLabel: "devis",
    // The devis' own désignation, which `actDisplayName` prefers over the catalogue's — mirrors `planActLabel`.
    fallbackName: noteKeepsTheFirstAct
      ? `${ofLabel} — séance 2 sur 2 : ${base}${teeth}`
      : `${base}${teeth}`,
    // ⚠️ Both stated as null rather than left out (N16): there is no devis and therefore no act id and no step
    // id to carry — `materialiseTreatments` replaces this whole row with the real one before anything is sent.
    // An omitted step is how a booked séance silently forgets which step it was for, so « none » is said.
    treatmentPlanItemId: null,
    treatmentPlanItemStepId: null,
    pendingContinuation: {
      dentalRecordId: previous.dentalRecordId,
      actId: previous.actId,
      ofLabel,
      nextStepLabel,
      remainingWorkCost: remaining > 0 ? remaining : null,
    },
    billedOnPlan: {
      // No devis yet, and that is what the notice's « Suivi comme traitement » branch is for.
      planNumber: null,
      actCost: remaining > 0 ? remaining : previous.cost,
      outstanding: planTotal,
      // With no new money the note is ATTACHED and is what collects — the same statement the plan read makes
      // through `linkedInvoiceNumber` once the devis exists.
      billedOnInvoiceNumber: previous.invoiceId != null && remaining === 0 ? previous.invoiceNumber : null,
      continuation: noteKeepsTheFirstAct
        ? {
            ofLabel,
            seanceNumber: 2,
            seanceTotal: 2,
            noteNumber: previous.invoiceNumber,
            noteOutstanding: previous.invoiceOutstanding,
            // ⚠️ The note's WHOLE total, matching `TreatmentTotal = planShare + Σ note.TotalTtc`. The act's own
            // cost agrees on a single-act séance and is short by the rest on a mixed one.
            treatmentTotal: previous.invoiceTotal + remaining,
            treatmentOutstanding: previous.invoiceOutstanding + remaining,
          }
        : undefined,
    },
  }
}

/**
 * What one row is CALLED — **the treatment's own désignation when it belongs to one**, the catalogue's name
 * otherwise.
 *
 * <p>⚠️ <b>The order matters and it was the other way round.</b> `fallbackName` reads like a last resort, so
 * both readers took the catalogue name whenever one resolved — which was right while a devis line carrying a
 * catalogue link was an ordinary act, and stopped being right the day `ContinueRecordedActCommand` began
 * copying that link onto the continuation line. The row then announced « Traitement de canal (dévitalisation) »
 * for a séance the devis calls « Traitement de canal — séance 2 sur 2 : continuation (dents 11) »: the act
 * being finished, named as though this visit were the whole of it. Two fixes undoing each other, silently.</p>
 *
 * <p>A devis line's désignation is what the dentist wrote and what every treatment surface prints; the
 * catalogue name is what to fall back to when there is no devis behind the row.</p>
 */
export function actDisplayName(act: SelectedAct, procedureType: ProcedureTypeDto | undefined): string {
  if ((act.treatmentPlanItemId || act.pendingContinuation) && act.fallbackName) return act.fallbackName
  if (!act.procedureTypeId) return act.fallbackName ?? "Acte du devis"
  return procedureType?.name ?? act.fallbackName ?? "Acte indisponible"
}

/**
 * The agreed price of one act as a number, or null when none was negotiated (or the field was cleared, which is
 * the same statement: leave it at the tarif).
 */
export function agreedCostOf(act: SelectedAct): number | null {
  // ⚠️ An act carried by a devis is **0 for this séance**, not null. Null means « nobody negotiated », which
  // sends the fiche to the catalogue tarif — so on the second séance of a 1 000 DT bridge it would price the
  // bridge again. Zero is already a real answer in this model (« an act offered »), so this needs no fourth
  // money state; what makes it legible is the notice beside it naming the devis.
  if (act.billedOnPlan) return 0
  if (act.agreedCost === undefined || act.agreedCost.trim() === "") return null
  const parsed = parseAmountInput(act.agreedCost)
  return Number.isFinite(parsed) ? parsed : null
}

/** True when a typed price cannot be read as money, or is negative — the server refuses both. */
export function hasInvalidAgreedCost(act: SelectedAct): boolean {
  if (act.agreedCost === undefined || act.agreedCost.trim() === "") return false
  const parsed = parseAmountInput(act.agreedCost)
  return !Number.isFinite(parsed) || parsed < 0
}

/**
 * What the séance costs at the prices typed into it, or **null** when nothing was negotiated.
 *
 * <p>Null when no act carries a price of its own, so the récapitulatif states a figure only when there is one to
 * verify. An act left at its tarif inside a séance where another was negotiated still contributes its tarif —
 * the total the patient was quoted is the whole séance, not the discounted part of it, which is why the
 * catalogue is a parameter here rather than something this module goes looking for.</p>
 *
 * <p>⚠️ <b>An act carried by a devis is not a negotiation</b>, and reading it as one made the récapitulatif
 * announce « Prix convenu — 0,000 DT » on the second séance of a 1 000 DT bridge: <code>agreedCostOf</code>
 * returns a hard <code>0</code> for such an act (deliberately — see its own note), which is a real price and so
 * satisfied « did anyone negotiate? ». The pane then stated, in the one place that summarises what is about to
 * be committed, that a visit inside a four-figure treatment was free. A plan-billed act still contributes its
 * 0 to a mixed séance — that part is right, it genuinely adds no honoraires — it just does not make the row
 * appear on its own. The form's « Déjà facturé » notice is what explains the locked price, in full, beside it.</p>
 */
export function negotiatedTotalOf(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): number | null {
  if (!acts.some((a) => a.billedOnPlan == null && agreedCostOf(a) != null)) return null

  const byId = new Map(procedureTypes.map((p) => [p.id, p]))
  return acts.reduce((sum, act) => {
    const agreed = agreedCostOf(act)
    if (agreed != null) return sum + agreed
    const tariff = act.procedureTypeId ? byId.get(act.procedureTypeId)?.defaultCost : null
    return sum + (tariff ?? 0)
  }, 0)
}

/**
 * The acts as the API wants them. Exported and shared rather than built inline in each booking dialog, for the
 * reason `AppointmentProcedureMapping` is shared server-side: the two must agree. A dialog that assembled
 * `procedures` without `agreedCost` would silently restore every act of the visit to its catalogue tarif,
 * because the server replaces the whole list.
 */
export function toProcedurePayloads(acts: SelectedAct[]): AppointmentProcedurePayload[] {
  return acts.map((act) => ({
    procedureTypeId: act.procedureTypeId,
    treatmentPlanItemId: act.treatmentPlanItemId ?? null,
    // ⚠️ Must travel with the price. The server keys its duplicate rules on (act, step), so a payload that
    // dropped the step would refuse « préparation + empreinte » in one séance as the same act twice — the
    // feature refused by its own client.
    treatmentPlanItemStepId: act.treatmentPlanItemStepId ?? null,
    agreedCost: agreedCostOf(act),
  }))
}

/**
 * Seed colours rotated by catalog size for an act typed on the fly. A subset of the palette the backend
 * `ColorHex` value object accepts (`GET /procedure-types/colors` is the authority) — a rotation seed, not a
 * picker, so it does not fetch: the user is booking a visit, not designing a catalogue.
 */
const CUSTOM_PROCEDURE_COLORS = ["#4F83CC", "#2A9D8F", "#6BAA75", "#9B8EDC", "#E9A23B", "#E76F51"]

/** Mirrors the server's own cap (`AppointmentProcedureSelection.MaxProceduresPerAppointment`). */
const MAX_ACTS = 12

interface AppointmentActsPickerProps {
  /** Active catalog. The picker never fetches it — both dialogs already load it for other reasons. */
  procedureTypes: ProcedureTypeDto[]
  loading?: boolean
  /** Why the catalog is empty, if it failed to load — an empty list and a failed call are different facts. */
  error?: string | null
  onRetry?: () => void
  value: SelectedAct[]
  onChange: (acts: SelectedAct[]) => void
  disabled?: boolean
  /** Called when an act is created on the fly, so the parent can fold it into its catalog state. */
  onProcedureCreated?: (created: ProcedureTypeDto) => void
  /** Fallback duration for a created act with no typical duration of its own. */
  fallbackDurationMinutes?: number
  /** Acts that came from a devis and must stay in the séance (removing one is « ne pas le planifier »). */
  idPrefix?: string
  /**
   * The patient's outstanding devis acts, offered as their own group in the picker.
   *
   * ⚠️ Absent on the create dialog, which receives its plan acts pre-selected from the devis workspace. Present
   * on the **edit** dialog, and that is the whole of gap #2: a plan link used to be settable only at creation,
   * so a visit booked from the agenda could never be attached to a devis afterwards.
   */
  planActs?: PresetPlanAct[]

  /**
   * Save a new « Total convenu » for one treatment act. Resolve `true` once the server has it.
   *
   * <p>⚠️ Without this the total is read-only, which is the defect the locked « Prix pour ce rendez-vous » was
   * replaced to fix — an act's price must be changeable at any moment, from wherever it is on screen. Absent,
   * the figure still renders but cannot be edited, so a caller that forgets it degrades to the old behaviour
   * rather than crashing.</p>
   */
  onTotalChange?: (treatmentPlanItemId: string, total: number) => Promise<boolean>
}

/**
 * « Actes du rendez-vous » — the séance's act list.
 *
 * <p>Replaces the single « Type d'acte » Select that both booking dialogs carried. A visit is routinely several
 * acts, and with one dropdown the second and third could only be typed into the notes: invisible to the duration,
 * to the colour, to the fiche de soins proposal and to the devis.</p>
 *
 * <p>Shared by create and edit rather than duplicated, so the inline « acte personnalisé » path — which only the
 * create dialog had — now exists on both, and the total-duration rule has one implementation.</p>
 */
export function AppointmentActsPicker({
  procedureTypes,
  loading = false,
  error = null,
  onRetry,
  value,
  onChange,
  disabled = false,
  onProcedureCreated,
  fallbackDurationMinutes = 30,
  idPrefix = "appt-acts",
  planActs,
  onTotalChange,
}: AppointmentActsPickerProps) {
  const [pickerOpen, setPickerOpen] = useState(false)
  /**
   * Which act rows have their étape chooser open, keyed on the group's representative index.
   *
   * <p>Closed by default: the app has already selected the next étape, and a dentist booking a follow-up wants
   * that answer, not a checklist. Opening it is « modifier ».</p>
   */
  const [openStepEditors, setOpenStepEditors] = useState<Set<number>>(new Set())
  /** Uncommitted « Total convenu » keystrokes, keyed on the group's representative index. */
  const [totalDrafts, setTotalDrafts] = useState<Record<number, string>>({})
  /** The plan item whose total is being saved, so the row can say so instead of looking inert. */
  const [savingTotal, setSavingTotal] = useState<string | null>(null)
  /**
   * ⚠️ **The in-flight guard, and the double submit it stops is SELF-INFLICTED.**
   * `commitTotal` fires on Enter *and* on blur, and the row disables its own input while saving — so pressing
   * Enter set `savingTotal`, React disabled the field, the browser blurred it because a disabled element
   * cannot hold focus, and `onBlur` committed the same edit a second time. Measured end to end: two `POST
   * /amend` calls, the first **200** and the second **409** on the version the first had just superseded, so a
   * save that worked announced « Cet enregistrement a été modifié par quelqu'un d'autre » — the one sentence
   * guaranteed to be read as « it did not save ».
   * A ref rather than the state above, because both handlers run before React has re-rendered.
   */
  const savingTotalRef = useRef<string | null>(null)

  /**
   * Commit an edited « Total convenu » to the treatment.
   *
   * <p>Blur or Enter, never a Save button: it is one number in a row of a form the dentist is already filling.
   * A refusal leaves the typed value in place — losing what somebody typed to tell them it was refused is the
   * worst of both.</p>
   */
  const commitTotal = async (row: { group: ActGroup; act: SelectedAct }) => {
    const index = row.group.indices[0]
    const typed = totalDrafts[index]
    const planItemId = row.act.treatmentPlanItemId
    if (typed === undefined || !planItemId || !onTotalChange) return
    if (savingTotalRef.current === planItemId) return // see `savingTotalRef`
    const parsed = parseAmountInput(typed)
    // ⚠️ Not a silent return: this is a money field, and « nothing happened » is the one outcome a dentist
    // cannot tell from « saved ». Same reasoning as `saveActTotal`'s refusal toast.
    if (!Number.isFinite(parsed) || parsed < 0) {
      toast.error(`${quoteFr(typed)} n'est pas un montant — le total n'a pas été modifié.`)
      return
    }
    if (row.act.billedOnPlan && parsed === row.act.billedOnPlan.actCost) {
      setTotalDrafts((prev) => { const next = { ...prev }; delete next[index]; return next })
      return
    }
    savingTotalRef.current = planItemId
    setSavingTotal(planItemId)
    try {
      const saved = await onTotalChange(planItemId, parsed)
      // Only drop the draft once the server has the value, or the field would snap back to the old figure.
      if (saved) {
        setTotalDrafts((prev) => { const next = { ...prev }; delete next[index]; return next })
        /*
         * ⚠️ **The row's own copy of the devis figure, which nothing refreshed — so a SUCCESSFUL save looked
         * like a save that had not happened.** `billedOnPlan` is derived once, when the act is picked or
         * hydrated, and both dialogs' back-fill effects skip an act that already carries it
         * (`if (!act.treatmentPlanItemId || act.billedOnPlan) return act`). So dropping the draft above put
         * the field straight back to the OLD figure, and the notice under it kept quoting the old total too:
         * typed 700, green toast « Total mis à jour — 700,000 DT », field reads 600. Reported as « it did not
         * persist ». The write had landed.
         *
         * ⚠️ Every clone of the act, not `indices[0]`: a two-étape séance renders the same plan act twice, and
         * patching one card leaves the pair disagreeing about what the treatment costs.
         *
         * ⚠️ `outstanding` moves with it, by the delta. It is the DEVIS' balance, not this act's — raising a
         * total raises what is left to collect by exactly the same amount, because the payments are untouched
         * and the server re-spreads the schedule onto the new total (`RespreadScheduleToTotal`).
         */
        onChange(
          value.map((act) =>
            act.treatmentPlanItemId === planItemId && act.billedOnPlan
              ? {
                  ...act,
                  billedOnPlan: {
                    ...act.billedOnPlan,
                    actCost: parsed,
                    outstanding: Math.max(
                      0,
                      Math.round((act.billedOnPlan.outstanding + parsed - act.billedOnPlan.actCost) * 1000) / 1000,
                    ),
                  },
                }
              : act,
          ),
        )
      }
    } finally {
      savingTotalRef.current = null
      setSavingTotal(null)
    }
  }
  const toggleStepEditor = (index: number) =>
    setOpenStepEditors((prev) => {
      const next = new Set(prev)
      if (next.has(index)) next.delete(index)
      else next.add(index)
      return next
    })
  const [customMode, setCustomMode] = useState(false)
  const [customName, setCustomName] = useState("")
  const [customDuration, setCustomDuration] = useState("")
  const [customCost, setCustomCost] = useState("")
  const [creating, setCreating] = useState(false)
  const [customError, setCustomError] = useState<string | null>(null)

  const byId = useMemo(
    () => new Map(procedureTypes.map((p) => [p.id, p])),
    [procedureTypes],
  )

  /** The acts with every undecided `plannedProtocol` filled in — see {@link resolvePlannedProtocols}. */
  const resolved = useMemo(() => resolvePlannedProtocols(value, procedureTypes), [value, procedureTypes])

  /**
   * The chosen acts, resolved against the catalog. A row whose procedure is no longer in the active catalog is
   * **kept and marked**, never dropped: silently removing it would change what the user is about to save without
   * telling them, and on the edit dialog that means deleting an act from a booked visit.
   */
  const rows = useMemo(
    () =>
      groupActs(resolved).map((group) => {
        const act = group.act
        // ⚠️ The group's chair time, not the act's catalogue duration once per row. « 2 actes · 120 min » was
        // shown for a bridge booked across a 60-min préparation and a 30-min empreinte, while the duration
        // preset the same panel drives read 1.5 h — two figures for one séance, three lines apart, and the
        // wrong one is the one the eye lands on first.
        const stepMinutes = group.stepIds.length > 0
          ? group.stepIds.reduce(
              (sum, id) => sum + (act.stepOptions?.find((o) => o.id === id)?.estimatedDurationMinutes ?? 0),
              0,
            )
          : null
        // Link-only row: no catalogue entry, so no duration and no colour of its own. Not an error state — it is
        // a devis line the clinic never turned into a catalog act.
        if (!act.procedureTypeId) {
          return {
            group,
            act,
            // A hand-typed devis line has no catalogue act, so no protocol to propose.
            protocol: undefined as ProcedureStepTemplateDto[] | undefined,
            name: actDisplayName(act, undefined),
            durationMinutes: stepMinutes,
            colorHex: "#6C757D",
            missing: false,
            // A hand-typed devis line has no catalogue tarif to fall back on, so there is nothing to prefill and
            // nothing to « remettre au tarif » — its price line starts empty.
            tariff: null,
          }
        }
        const pt = byId.get(act.procedureTypeId)
        return {
          group,
          act,
          // The act's catalogue protocol, for the offer above. Read from the served catalogue, never a list
          // kept here — the practice edits these in « Types de procédures » and this must follow.
          protocol: pt?.defaultSteps,
          name: actDisplayName(act, pt),
          durationMinutes: stepMinutes ?? unbookedActMinutes(act, pt),
          colorHex: pt?.colorHex ?? "#6C757D",
          missing: !pt,
          tariff: pt?.defaultCost ?? null,
        }
      }),
    [resolved, byId],
  )

  /*
   * Writes back over `value`, deliberately, and NOT over `resolved`: an act left undecided must stay undecided
   * so it can be re-resolved. Freezing the resolved list here would mean that saying « une seule séance » on a
   * crown also silently froze the implant beside it at « une seule séance » — the slot it was only waiting for.
   */
  const setPlannedProtocol = (index: number, steps: ProcedureStepTemplateDto[] | null) =>
    onChange(value.map((act, i) => (i === index ? { ...act, plannedProtocol: steps } : act)))

  // Both figures come from the grouped model, so the badge cannot disagree with the duration this same panel
  // sets on the form: `rows.length` is acts-as-counted-by-a-person and each row's minutes are its steps'.
  const totalMinutes = rows.reduce((sum, r) => sum + (r.durationMinutes ?? 0), 0)
  const selectedIds = useMemo(
    () => new Set(value.map((a) => a.procedureTypeId).filter((id): id is string => id !== null)),
    [value],
  )
  const atCap = value.length >= MAX_ACTS
  // Shared with the fiche's catalogue picker so both agree on which discipline an act belongs to and in what
  // order the disciplines appear.
  const procedureGroups = useMemo(() => groupProceduresByCategory(procedureTypes), [procedureTypes])

  const addAct = (procedureTypeId: string) => {
    // The server refuses a duplicate by name; refusing it here too keeps the list honest without a round trip.
    if (selectedIds.has(procedureTypeId)) return
    onChange([...value, { procedureTypeId }])
  }

  /**
   * Attaches a devis act to this séance.
   *
   * ⚠️ This is what makes the flow two-directional. Before it, a plan link could be set **only at creation**,
   * from the devis workspace — so a visit booked from the agenda (which is how a dentist in a hurry books) could
   * never be attached to a devis afterwards, and an act that turned out to need a second séance had no route in
   * at all. Keyed on the plan act, not the procedure type: the same act may legitimately appear twice in a
   * séance once by step, and `groupActs` folds those back into one card.
   */
  const addPlanAct = (preset: PresetPlanAct) => {
    if (value.some((a) => a.treatmentPlanItemId === preset.planItemId)) return
    onChange([...value, presetToSelectedAct(preset, procedureTypes)])
  }

  /**
   * Removes every wire row the card stands for. A two-step bridge is two rows, so removing only the
   * representative left the other step booked with no card offering to take it off again.
   */
  const removeGroup = (group: ActGroup) => {
    const drop = new Set(group.indices)
    onChange(value.filter((_, i) => !drop.has(i)))
  }

  /** `undefined` puts the row back to « rien de négocié » — the field shows the tarif again and sends nothing. */
  const setAgreedCost = (index: number, next: string | undefined) =>
    onChange(value.map((act, i) => (i === index ? { ...act, agreedCost: next } : act)))

  /**
   * Tick or untick one step of a devis act.
   *
   * <p>⚠️ <b>Ticking a second step ADDS A ROW rather than replacing the first</b>, because « préparation +
   * empreinte dans la même séance » is two acts on the wire — the server keys its duplicate rules on
   * (act, step) precisely so that is expressible. Untickng the last step of an act leaves the act row with no
   * step, i.e. « the whole act in this séance », which is the ordinary pre-steps meaning.</p>
   */
  const toggleStep = (group: ActGroup, stepId: string) => {
    const index = group.indices[0]
    const row = value[index]
    const already = group.stepIds.includes(stepId)

    if (already) {
      const siblings = value.filter((a) => a.treatmentPlanItemId === row.treatmentPlanItemId)
      // The last remaining step of this act: keep the row and drop the step, rather than removing the act from
      // the séance — the user unticked a step, they did not remove the bridge.
      if (siblings.length === 1) {
        onChange(
          value.map((a, i) => (i === index ? { ...a, treatmentPlanItemStepId: null } : a)),
        )
        return
      }
      onChange(
        value.filter(
          (a) => !(a.treatmentPlanItemId === row.treatmentPlanItemId && a.treatmentPlanItemStepId === stepId),
        ),
      )
      return
    }

    // This row has no step yet: take it. Otherwise clone the row for the newly ticked step.
    if (!row.treatmentPlanItemStepId) {
      onChange(value.map((a, i) => (i === index ? { ...a, treatmentPlanItemStepId: stepId } : a)))
      return
    }

    const clone: SelectedAct = { ...row, treatmentPlanItemStepId: stepId, agreedCost: undefined }
    onChange([...value.slice(0, index + 1), clone, ...value.slice(index + 1)])
  }

  const handleCreateCustom = async () => {
    setCustomError(null)
    const name = customName.trim()
    if (!name) {
      setCustomError("Le nom de l'acte est requis")
      return
    }
    // Unique per clinic server-side; catching the common case here avoids a round-trip 400.
    const existing = procedureTypes.find((pt) => pt.name.trim().toLowerCase() === name.toLowerCase())
    if (existing) {
      setCustomError(
        `Un acte nommé ${quoteFr(existing.name)} existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`,
      )
      return
    }

    const typed = customDuration ? Number(customDuration) : NaN
    const inferred = Number.isFinite(typed) && typed > 0 ? Math.floor(typed) : fallbackDurationMinutes
    const durationMinutes = Math.min(479, Math.max(1, inferred || 30))
    const cost = customCost.trim() ? parseAmountInput(customCost) : null
    if (cost !== null && (Number.isNaN(cost) || cost < 0)) {
      setCustomError("Le montant est invalide")
      return
    }

    setCreating(true)
    try {
      const colorHex = CUSTOM_PROCEDURE_COLORS[procedureTypes.length % CUSTOM_PROCEDURE_COLORS.length]
      const created = await procedureTypesApi.create({
        name,
        defaultDurationMinutes: durationMinutes,
        defaultCost: cost,
        colorHex,
      })
      onProcedureCreated?.(created)
      onChange([...value, { procedureTypeId: created.id }])
      setCustomMode(false)
      setCustomName("")
      setCustomDuration("")
      setCustomCost("")
    } catch (err) {
      const message = err instanceof ApiError ? err.message : ""
      if (/already exists|existe déjà/i.test(message)) {
        setCustomError(`Un acte nommé ${quoteFr(name)} existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`)
      } else {
        setCustomError(message || "Échec de la création de l'acte")
      }
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Label htmlFor={`${idPrefix}-add`} className="text-sm">
          Actes du rendez-vous
        </Label>
        {/* The count and the summed duration together, because the summed duration is the reason the count
            matters: it is what the visit will be booked for. */}
        {rows.length > 0 && (
          <Badge variant="secondary" className="gap-1">
            <Clock className="h-3 w-3" />
            {rows.length} acte{rows.length > 1 ? "s" : ""}
            {totalMinutes > 0 ? ` · ${totalMinutes} min` : ""}
          </Badge>
        )}
      </div>

      {/*
        ⚠️ **The forward door onto the feature, and there was none.** A cold walk looking for a way to plan a
        six-visit implant took four wrong turns: the agenda's toolbar names no étape, séance, devis or plan; the
        booking form offers nothing multi-visit; « Autres actions » holds one item (« Exporter »); and the only
        étape-shaped control on the whole screen — « C'est la suite d'une séance précédente ? » — works
        **backwards** and always assumes this visit is the second. The word « étape » appeared on no screen until
        the reviewer had already decided to create a plan and picked a catalogue act inside it. So the feature was
        discoverable only as a side-effect of a decision it should have been informing.

        One line, on the empty form, beside the acts — which is where the question is actually asked. The pair is
        deliberate: this one goes forward, the existing link goes back, and neither is now the only one.
      */}
      {/* ⚠️ No longer gated on a « can this dialog start a treatment? » prop. It was, and that prop was absent
          in the edit dialog and on a create form with no patient chosen yet — so the sentence promising the
          feature was hidden in exactly the situations where somebody was looking for it. */}
      {/*
        ⚠️ **Shortened, deliberately NOT deleted — the design review proposed deleting it and the review was
        wrong.** The argument for deletion was that nobody reads a consigne before having a problem, and the
        card that appears after the choice says everything at the moment it matters. The argument against is
        the one written above, and it is backed by an observed walk rather than by a view about reading
        habits: with this line gone there is no *forward* door onto the feature at all, and a cold search for
        « how do I plan a six-visit implant » took four wrong turns. Two lines of grey prose above an empty
        field was the real complaint, so it is one short line now.
      */}
      {rows.length === 0 && (
        <p className="text-2xs leading-relaxed text-muted-foreground">
          Un acte en plusieurs séances&nbsp;? Choisissez-le&nbsp;: le traitement est préparé tout seul.
        </p>
      )}

      {rows.length > 0 && (
        <ul className="space-y-1.5">
          {rows.map((row) => (
            <li
              key={`${row.act.treatmentPlanItemId ?? row.act.procedureTypeId ?? "act"}-${row.group.indices[0]}`}
              className="rounded-md border bg-background px-3 py-2"
            >
              {/*
                ⚠️ **`flex-wrap`, and the name has a floor — at 320 px it was breaking mid-word.** The row's
                other four children are all `shrink-0` (the dot, the « devis » badge, « N min », the remove
                button), so the name got whatever was left: ~60 px at 320, and `[overflow-wrap:anywhere]` then
                did exactly what it is told to do — « Prothèse amovible (partielle / complèt / e) », one
                fragment per line, on the row whose whole job is to say which act is being booked. Measured on
                the eye pass; `check:responsive` and the overflow probe were both green, because nothing
                overflowed: it fitted, unreadably. The floor is 9rem so the badges wrap to their own line at
                320 and nothing changes at 390 and up.
              */}
              <div className="flex flex-wrap items-center gap-2">
              <span
                className="h-3 w-3 shrink-0 rounded-full"
                style={{ backgroundColor: row.colorHex }}
                aria-hidden
              />
              {/* Wraps, never truncates. The row is `flex items-center gap-2 px-3 py-2` with a 12px dot, a
                  `shrink-0` « N min » span and an `h-7 w-7` remove button, leaving ~170px at 390px — so
                  « Obturation composite deux faces » clipped to « Obturation composi… » and nothing else in
                  the row says which act is about to be booked. The act's name IS the row's identity. */}
              <span
                className={cn(
                  "min-w-32 flex-1 text-sm [overflow-wrap:anywhere]",
                  row.missing && "text-muted-foreground italic",
                )}
              >
                {row.name}
              </span>
              {/*
                ⚠️ **The three trailing controls wrap as ONE group.** Left as siblings of a wrapping row they
                broke apart: at 390 px the badge and « 30 min » stayed beside the name and the ✕ dropped alone
                onto the next line, orphaned at the left margin under the act it removes. They are one cluster
                — what this act is, how long it takes, and how to take it off — and a line break inside it says
                nothing.
              */}
              <div className="flex shrink-0 items-center gap-2">
              {/* ⚠️ No `sm:` gate. Below 640 px the « devis » chip disappeared, and on the EDIT dialog it is the
                  only devis signal a row carries — so at 390 px a plan-billed act read as an ordinary one.
                  Five characters; there is nothing to save by hiding them. */}
              {row.act.planLabel && (
                <Badge variant="outline" className="inline-flex shrink-0 gap-1 text-xs">
                  <Stethoscope className="h-3 w-3" />
                  {row.act.planLabel}
                </Badge>
              )}
              {/*
                ⚠️ **`> 0`, not `!= null` — an unknown chair time is not a zero-minute act.** A devis step
                carries `estimatedDurationMinutes` only when somebody typed one, and the two steps
                `ContinueRecordedActCommand` synthesises carry none at all (nothing knows how long the next
                séance of a retroactive continuation takes, which is the same reason their labels are generic).
                So the continuation's act rendered « Séance suivante · devis · 0 min » — a measurement nobody
                made, next to a récapitulatif correctly saying 30 min, on the one row a dentist reads to check
                what they are booking. Withholding the figure says « not stated »; printing 0 says « none »,
                and only one of those is true. `totalActsDuration` already refuses to sum it, and the summary
                badge three blocks up already hides its own total at 0 — this row was the one place left
                asserting it.
              */}
              {row.durationMinutes != null && row.durationMinutes > 0 && (
                <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
                  {row.durationMinutes} min
                </span>
              )}
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="h-7 w-7 shrink-0"
                aria-label={`Retirer ${quoteFr(row.name)} du rendez-vous`}
                disabled={disabled}
                onClick={() => removeGroup(row.group)}
              >
                <X className="h-4 w-4" />
              </Button>
              </div>
              </div>

              {/*
                ⚠️ Its own line, not another cell on the identity row. That row already wraps rather than
                truncates at 390 px — the act's name IS the row's identity — and squeezing a ~7rem money field
                beside it would take the name back below the width that made it readable.

                « Prix pour ce rendez-vous », never « Prix » alone: the panel below can also create a catalogue
                act with a price, and that one changes the tarif for every future visit. Two money fields a
                thumb's width apart, one local and one permanent, is a mistake nobody would notice making.
              */}
              {/*
                The steps of a devis act, as tick boxes — NOT a select. « Préparation » and « Empreinte » in one
                séance is the case this whole feature exists for, and a single-choice control cannot say it.
                Rendered only for an act that has steps left, which is a small minority of bookings.
              */}
              {/*
                ⚠️ **The app chooses the séance; the chooser is one press away.**
                This block used to render every remaining étape as a tick-chip, always open — six of them for
                the seeded implant, in a row that wrapped, on the densest surface in the product. A dentist
                booking the next visit of a bridge does not want to pick from a checklist: the answer is almost
                always « la suivante », which `planItemToPreset` has already selected.
                So the line states what will be recorded, and « modifier » reveals the chips for the case the
                whole feature exists for — two étapes in one séance. Nothing is removed, only folded.
              */}
              {row.act.stepOptions && row.act.stepOptions.length > 0 && (
                <div className="mt-2 border-t border-dashed pt-2">
                  {/*
                    ⚠️ **The étape is read at `text-sm`, like the act's own name, and it was `text-2xs`.**
                    « Couronne / bridge (par élément) » and « Essai de l'armature » answer two halves of one
                    question — *what* is being done and *which séance of it* — and the second half was set four
                    steps smaller than the first, in muted grey, under a dashed rule. Reported in as many words:
                    « users will definitely miss it ». A dentist who misreads which séance they are booking
                    books the wrong one, and the fiche then charts the wrong step as carried out.

                    ⚠️ The étapes are **named, never counted**. « 2 étapes dans cette séance » says how many and
                    not which, on the one line whose job is to say which — and a bare count beside a protocol is
                    read as progress (see `never-state-the-next-step-as-a-fact`).
                  */}
                  <div className="mb-1.5 flex flex-wrap items-baseline gap-x-2 gap-y-1">
                    <p className="min-w-0 flex-1 text-sm [overflow-wrap:anywhere]">
                      {row.group.stepIds.length === 0 ? (
                        <span className="text-muted-foreground">Aucune étape pour cette séance</span>
                      ) : (
                        <>
                          <span className="text-muted-foreground">
                            {row.group.stepIds.length === 1 ? "Séance : " : "Séances : "}
                          </span>
                          <span className="font-semibold">
                            {row.group.stepIds
                              .map((id) => row.act.stepOptions?.find((s) => s.id === id)?.label ?? "étape")
                              .join(" · ")}
                          </span>
                        </>
                      )}
                    </p>
                    {/* `coarse:` — a bare text link is a 15 px-tall target on a thumb (§ 9.2 of the device
                        contract); the act rows around it already carry the same treatment. */}
                    <button
                      type="button"
                      disabled={disabled}
                      onClick={() => toggleStepEditor(row.group.indices[0])}
                      className="-my-1 inline-flex shrink-0 items-center px-1 py-1 text-xs text-primary underline decoration-dotted coarse:min-h-11 coarse:px-2"
                      aria-expanded={openStepEditors.has(row.group.indices[0])}
                    >
                      {openStepEditors.has(row.group.indices[0]) ? "terminé" : "modifier"}
                    </button>
                  </div>
                  <div
                    className="flex flex-wrap gap-1.5"
                    hidden={!openStepEditors.has(row.group.indices[0])}
                  >
                    {/*
                      ⚠️ **A réalisé step is offered to nobody — but one this séance already books stays on
                      screen, disabled.** Hiding it outright would leave a ticked step the dentist can see in
                      the line above and cannot untick, and « a capability removed by a layout decision » is
                      exactly what this repo refuses. Untickable, because a second fiche against a step already
                      evidenced by one is the thing the whole flag exists to prevent.
                    */}
                    {row.act.stepOptions
                      .filter((step) => !step.done || row.group.stepIds.includes(step.id))
                      .map((step) => {
                      // The GROUP's own steps. Read off `value` for the whole act it was the same answer for
                      // every clone, so both cards of a two-step bridge showed both chips ticked — each card
                      // claiming to be both steps.
                      const ticked = row.group.stepIds.includes(step.id)
                      return (
                        <button
                          key={step.id}
                          type="button"
                          disabled={disabled || step.done}
                          title={step.done ? "Séance déjà réalisée" : undefined}
                          onClick={() => toggleStep(row.group, step.id)}
                          aria-pressed={ticked}
                          className={cn(
                            // Grown, not overlaid: these sit a few pixels apart in a row, so a 44 px
                            // `.touch-target` pseudo-element would overhang its neighbour and — the later
                            // sibling painting last — steal its taps (§ 2).
                            "inline-flex min-h-9 items-center gap-2 rounded-md border px-3 text-xs font-medium coarse:min-h-11",
                            ticked
                              ? "border-primary bg-primary/10 text-primary"
                              : "border-border bg-card text-muted-foreground",
                          )}
                        >
                          <span
                            aria-hidden="true"
                            className={cn(
                              "flex size-4 flex-none items-center justify-center rounded-[5px] border-[1.5px]",
                              ticked ? "border-primary bg-primary" : "border-border",
                            )}
                          >
                            {ticked && <Check className="size-2.5 text-primary-foreground" strokeWidth={4} />}
                          </span>
                          {step.label}
                          {step.estimatedDurationMinutes != null && (
                            <span className="font-mono text-2xs opacity-75">
                              {step.estimatedDurationMinutes} min
                            </span>
                          )}
                        </button>
                      )
                    })}
                  </div>
                </div>
              )}

              {/*
                ⚠️ **A séance of a treatment has no price, so it shows no price field.**
                It showed « Prix pour ce rendez-vous » read-only at 0 with « facturé sur le devis » beside it — a
                greyed box whose meaning nobody could act on. Worse, the field invited the wrong figure: typing
                600 on the first séance of a 2 000 DT implant reads as « cette séance vaut 600 », but the value is
                an `AgreedCost` on the ACT, so it flowed fiche → ligne de facture and charged 2 600.
                The act is priced ONCE, on the treatment, and what a séance carries is an *encaissement* — typed
                at the fiche, where the money changes hands. So this row states the total and offers to change it.
              */}
              {row.act.billedOnPlan != null ? (
                /*
                 * ⚠️ **EDITABLE.** This was a read-only figure, which is the same defect as the locked 0 it
                 * replaced: the price of an act must be changeable at any moment, from wherever the dentist is
                 * looking at it. What changed is *which* price the field means — the act's total for the whole
                 * treatment, not a price for this séance — so editing it saves to the treatment and the
                 * échéancier re-spreads itself server-side.
                 */
                <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                  <Label
                    htmlFor={`${idPrefix}-total-${row.group.indices[0]}`}
                    className="shrink-0 text-2xs font-normal text-muted-foreground"
                  >
                    Total convenu
                  </Label>
                  <div className="relative">
                    <span className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                      DT
                    </span>
                    <Input
                      id={`${idPrefix}-total-${row.group.indices[0]}`}
                      type="text"
                      inputMode="decimal"
                      className="h-8 w-28 ps-7 text-xs tabular-nums"
                      // Uncommitted keystrokes live here; the committed value is the treatment's own.
                      value={totalDrafts[row.group.indices[0]] ?? formatAmount(row.act.billedOnPlan.actCost)}
                      onChange={(e) =>
                        setTotalDrafts((prev) => ({ ...prev, [row.group.indices[0]]: e.target.value }))
                      }
                      onFocus={(e) => e.currentTarget.select()}
                      onBlur={() => commitTotal(row)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") { e.preventDefault(); commitTotal(row) }
                        else if (e.key === "Escape") {
                          setTotalDrafts((prev) => {
                            const next = { ...prev }
                            delete next[row.group.indices[0]]
                            return next
                          })
                        }
                      }}
                      disabled={disabled || savingTotal === row.act.treatmentPlanItemId}
                      aria-label={`Total convenu pour ${row.name}, tout le traitement`}
                    />
                  </div>
                  {/*
                    ⚠️ **`min-w-0`, never `shrink-0` — this caption is 257 px of unbreakable flex item and it
                    set the width of the whole card.** Measured at 320 px: the row allows 231, the span refused
                    to shrink below its max-content, and the overflow propagated up through the `<li>` (269 in
                    255) to the acts section (270 in 257) — the RecordSection trap in CLAUDE.md, one component
                    over. `flex-wrap` already gives it its own line; `min-w-0` is what lets it wrap inside it.
                  */}
                  <span className="min-w-0 text-2xs text-muted-foreground">
                    {savingTotal === row.act.treatmentPlanItemId
                      ? "enregistrement…"
                      : "pour tout le traitement — cette séance n'ajoute rien"}
                  </span>
                </div>
              ) : (
              <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                {/* `min-w-0`, not `shrink-0`: « Prix du traitement (6 séances) » beside a `w-28` field is the
                    same un-shrinkable-child arithmetic the sibling branch's caption was measured failing at
                    320 px. This label is dynamic, so its length is not something to eyeball once. */}
                <Label
                  htmlFor={`${idPrefix}-price-${row.group.indices[0]}`}
                  className="min-w-0 text-2xs font-normal text-muted-foreground"
                >
                  {/* ⚠️ A followed act is priced ONCE, for the whole treatment — `StartTreatmentCommand`
                      documents it (« a séance of it has none: what a séance carries is an encaissement »). The
                      label must say so *before* the figure is typed: « Prix pour ce rendez-vous » on a 2 000 DT
                      implant invites the dentist to type this visit's share, and the treatment is then created
                      at that share for all six visits. */}
                  {row.act.plannedProtocol && row.act.plannedProtocol.length > 0
                    ? `Prix du traitement (${row.act.plannedProtocol.length} séances)`
                    : "Prix pour ce rendez-vous"}
                </Label>
                <div className="relative">
                  <span className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                    DT
                  </span>
                  <Input
                    id={`${idPrefix}-price-${row.group.indices[0]}`}
                    readOnly={row.act.billedOnPlan != null}
                    // `text` + `inputMode="decimal"`, matching the fiche's own tarif field: `type="number"`
                    // refuses the comma this product prints money with, so « 90,500 » could not be typed at all.
                    type="text"
                    inputMode="decimal"
                    className={cn(
                      "h-8 w-28 ps-7 text-xs tabular-nums",
                      hasInvalidAgreedCost(row.act) && "border-destructive",
                      row.act.billedOnPlan != null && "bg-muted text-muted-foreground",
                    )}
                    // Untouched shows the tarif without claiming it was agreed — see `SelectedAct.agreedCost`.
                    // An act the devis prices reads a hard 0: the fee is on the plan, so this séance adds none.
                    value={
                      row.act.billedOnPlan != null
                        ? formatAmount(0)
                        : row.act.agreedCost ?? (row.tariff != null ? formatAmount(row.tariff) : "")
                    }
                    onChange={(e) => setAgreedCost(row.group.indices[0], e.target.value)}
                    disabled={disabled}
                    aria-invalid={hasInvalidAgreedCost(row.act)}
                    aria-label={`Prix convenu pour ${row.name} à ce rendez-vous`}
                    placeholder={row.tariff == null ? "Prix libre" : undefined}
                  />
                </div>
                {row.act.billedOnPlan != null && (
                  <span className="shrink-0 text-2xs text-muted-foreground">facturé sur le devis</span>
                )}
                {row.act.billedOnPlan == null && row.act.agreedCost !== undefined && row.tariff != null && (
                  <button
                    type="button"
                    onClick={() => setAgreedCost(row.group.indices[0], undefined)}
                    disabled={disabled}
                    className="shrink-0 text-2xs text-muted-foreground underline decoration-dotted hover:text-foreground"
                  >
                    remettre au tarif ({formatAmount(row.tariff)} DT)
                  </button>
                )}
                {hasInvalidAgreedCost(row.act) && (
                  <span className="basis-full text-2xs text-destructive">
                    Montant invalide — par exemple 120,000.
                  </span>
                )}
              </div>
              )}

              {/*
                The sentence that answers the question which stopped the dentist: he had taken 800 DT on a
                1 000 DT bridge and could not tell whether booking the next séance would charge again. It names
                the devis, THIS act's fee on it, and what is left to collect on the devis as a whole — a locked
                « 0,000 » with no explanation reads as a bug.

                ⚠️ Two figures, two scopes, and each is labelled with its own: the act's fee follows « cet
                acte » and the outstanding says « sur le devis ». Unlabelled, a reader attaches both to the act
                — which is exactly how this read « à 1 080,000 DT » for a 1 000 DT bridge.
              */}
              {/*
                A multi-séance act, on a visit that is not on a devis yet.

                ⚠️ **The split is the DEFAULT and the button is the way out**, which is the reverse of what
                shipped first. « Suivre ce traitement » was one press — but a press somebody had to notice, and
                `create-appointment-dialog` rendered it only when a patient was already selected, so booking a
                walk-in (« Nouveau patient » has no id until the save) or picking the act before the patient
                showed « cet acte se fait en 3 séances » with **no control at all** underneath it. That is the
                report this was rebuilt from: two people, the same act, one of them with the button.

                ⚠️ Shown only for an act **not** already linked to a devis: once it is, the step chips above are
                the real control and this would be a second, weaker route to the same thing.
              */}
              {!row.act.treatmentPlanItemId && row.protocol && row.protocol.length > 0 && (() => {
                const index = row.group.indices[0]
                const planned = row.act.plannedProtocol
                const followed = planned != null && planned.length > 0
                // Whose treatment this booking is already preparing, when it is not this act's — an appointment
                // carries one `TreatmentPlanId`, so following this act means giving that one up.
                const other = rows.find(
                  (r) => r !== row && r.act.plannedProtocol && r.act.plannedProtocol.length > 0,
                )

                return (
                  <div
                    className="mt-2 rounded-md border border-dashed border-primary/60 bg-primary/[0.05] p-2.5"
                    role="status"
                  >
                    {followed ? (
                      <>
                        <p className="text-2xs leading-relaxed">
                          <span className="inline-flex items-center gap-1 font-semibold text-primary">
                            <Stethoscope className="h-3.5 w-3.5 shrink-0" aria-hidden />
                            Traitement en {planned.length}&nbsp;
                            {planned.length > 1 ? "séances" : "séance"}.
                          </span>{" "}
                          {/* The one fact a dentist needs from this card: what is being done TODAY. */}
                          Ce rendez-vous est la 1re&nbsp;: {quoteFr(planned[0]?.label || "séance 1")}.
                        </p>
                        {planned.length > 1 && (
                          <p className="mt-0.5 text-2xs leading-relaxed text-muted-foreground">
                            Ensuite&nbsp;:{" "}
                            {planned.slice(1).map((s, i) => `${i + 2}. ${s.label}`).join(" · ")}
                          </p>
                        )}
                      </>
                    ) : (
                      <p className="text-2xs leading-relaxed">
                        <span className="font-semibold text-primary">
                          Cet acte se fait normalement en {row.protocol.length} séances.
                        </span>{" "}
                        {row.protocol.map((step) => step.label).join(" · ")}
                        <br />
                        <span className="text-muted-foreground">Prévu ici en une seule séance.</span>
                      </p>
                    )}

                    {/*
                      ⚠️ **Rendered unconditionally when the act is followed — there is no « Modifier les
                      séances » any more, and removing that toggle IS the fix.** The list was hidden behind a
                      disclosure whose open state was a form: 515 px at 1440 and 843 px at 390, for three
                      séances. Worse, the disclosure was the reported defect — open, the row read
                      « Terminer · Tout faire en une séance », so the exit looked like a validation of what had
                      just been typed and the control beside it looked like the confirm. Renaming it treated the
                      symptom; the list simply not being a mode treats the cause. It is a readable frise now, one
                      line per séance, and only a single ROW is ever a form. Nothing here needs validating:
                      « Créer le rendez-vous » is the one save on the screen.
                    */}
                    {followed && (
                      <AppointmentProtocolEditor
                        steps={planned}
                        onChange={(next) =>
                          // Emptying the list IS « une seule séance » — see `SelectedAct.plannedProtocol`.
                          setPlannedProtocol(index, next.length > 0 ? next : null)
                        }
                        onReset={() => setPlannedProtocol(index, row.protocol!.map((s) => ({ ...s })))}
                        canReset={
                          JSON.stringify(planned) !== JSON.stringify(row.protocol)
                        }
                        onSingleSeance={() => setPlannedProtocol(index, null)}
                        disabled={disabled}
                        idPrefix={`${idPrefix}-protocol-${index}`}
                        actName={row.name}
                      />
                    )}

                    <div className={cn("flex flex-wrap gap-2", !followed && "mt-2")}>
                      {followed ? null : (
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="h-8 w-full gap-1 text-2xs coarse:h-11"
                          disabled={disabled}
                          onClick={() => {
                            // One treatment per booking: taking the slot gives the other act its séance back.
                            const freed = other ? other.group.indices[0] : null
                            const steps = row.protocol!.map((s) => ({ ...s }))
                            onChange(
                              value.map((act, i) =>
                                i === index
                                  ? { ...act, plannedProtocol: steps }
                                  : i === freed
                                    ? { ...act, plannedProtocol: null }
                                    : act,
                              ),
                            )
                          }}
                        >
                          <Stethoscope className="h-3.5 w-3.5" />
                          Répartir en {row.protocol.length} séances
                        </Button>
                      )}
                    </div>

                    <p className="mt-1 text-2xs leading-relaxed text-muted-foreground">
                      {followed ? (
                        <>
                          Aucun devis, aucun numéro — le traitement est préparé à l&apos;enregistrement du
                          rendez-vous. Le prix saisi est celui de tout le traitement.
                        </>
                      ) : other ? (
                        // Said before the press, not after: an appointment carries one devis, and finding that
                        // out by having the other act quietly un-split is the worst possible way to learn it.
                        <>Ce rendez-vous prépare déjà le traitement de {quoteFr(other.name)} — un rendez-vous
                        n&apos;en prépare qu&apos;un.</>
                      ) : (
                        <>Les séances suivantes se planifient depuis la fiche du patient.</>
                      )}
                    </p>
                  </div>
                )
              })()}

              {row.act.billedOnPlan && (
                <p
                  className="mt-2 rounded-md border border-primary bg-primary/[0.07] p-2.5 text-2xs leading-relaxed"
                  role="status"
                >
                  {/*
                    ⚠️ With no number there is no devis yet — « Suivre ce traitement » makes an un-numbered
                    treatment on purpose — so « porté par le devis » names a document that does not exist. The
                    un-numbered case says « ce traitement » and claims nothing about a devis.

                    ⚠️ **It said « Déjà facturé. », and that was wrong twice over.** Nothing has been facturé:
                    a devis is a *quote*, and the document that bills in this product is the note d'honoraires
                    — which is a different row on this same notice (`billedOnInvoiceNumber`). And « déjà »
                    reads as « the money is settled », on a séance whose whole point may be to collect some.
                    Reported as misleading. « Chiffré sur le devis » says the true thing: the act carries ONE
                    price, for the whole treatment, and it lives over there.

                    ⚠️ The mirror of this sentence is in `patient-record-modal`'s fiche banner and the two must
                    be reworded together — this exact pair has already drifted once, over the un-numbered case.
                  */}
                  <span className="font-semibold text-primary">
                    {row.act.billedOnPlan.planNumber ? "Chiffré sur le devis." : "Suivi comme traitement."}
                  </span>{" "}
                  L&apos;acte entier est chiffré{" "}
                  <span className="font-mono tabular-nums">
                    {formatDT(row.act.billedOnPlan.actCost)}
                  </span>
                  {row.act.billedOnPlan.planNumber
                    ? ` sur le devis ${row.act.billedOnPlan.planNumber}`
                    : " pour tout le traitement"}
                  {" "}— cette séance n&apos;ajoute aucun honoraire.
                  {/*
                    ⚠️ **Where the money goes is stated HERE and not on the fiche's own banner**, and the two
                    are not the same call. On the fiche, « Encaissé sur le traitement » is the only money
                    control on the screen and pointing at it is noise. Here there is no money control at all —
                    the price field is withheld — so a dentist told « cette séance n&apos;ajoute aucun
                    honoraire » while the patient is handing over cash has been told what NOT to do and not
                    what to do.
                  */}
                  {" "}L&apos;argent encaissé pendant la séance se saisit ensuite sur la fiche de soins, dans
                  «&nbsp;Encaissé sur le traitement&nbsp;».
                  {/*
                    ⚠️ The devis' own « reste » is stated ONLY while the devis is what collects. Once a note
                    d'honoraires holds the money, that figure is the plan's untouched auto-échéance and is simply
                    false — see `BilledOnPlan.billedOnInvoiceNumber`. The note is named instead, so whoever is
                    about to take money knows which document to look at.
                  */}
                  {/*
                    ⚠️ **A CONTINUATION states BOTH documents, because the patient owes on both.** This row
                    printed « Reste à encaisser sur le devis : 20,000 DT » — true of the devis and silent about
                    the 50 still owed on the note that billed the first séance — on the one screen where
                    somebody is deciding what to collect. The two figures stay side by side rather than summed
                    into one: they are settled on two different documents, by two different actions.
                  */}
                  {row.act.billedOnPlan.continuation ? (
                    <>
                      {" "}Total des deux séances :{" "}
                      <span className="font-mono tabular-nums">
                        {formatDT(row.act.billedOnPlan.continuation.treatmentTotal)}
                      </span>
                      . Reste{" "}
                      <span className="font-mono tabular-nums">
                        {formatDT(row.act.billedOnPlan.continuation.noteOutstanding)}
                      </span>{" "}
                      sur{" "}
                      {row.act.billedOnPlan.continuation.noteNumber ? (
                        <>
                          la note{" "}
                          <span className="font-mono">{row.act.billedOnPlan.continuation.noteNumber}</span>
                        </>
                      ) : (
                        "un brouillon de note d'honoraires"
                      )}{" "}
                      et{" "}
                      <span className="font-mono tabular-nums">
                        {formatDT(row.act.billedOnPlan.outstanding)}
                      </span>{" "}
                      {/* ⚠️ « ce devis » names a document that does not exist yet on a pending continuation —
                          the devis is minted when the booking is saved, not when the séance was picked. */}
                      {row.act.billedOnPlan.planNumber ? "sur ce devis." : "sur le devis de ce traitement."}
                    </>
                  ) : row.act.billedOnPlan.billedOnInvoiceNumber ? (
                    <>
                      {" "}Encaissement sur la note{" "}
                      <span className="font-mono">{row.act.billedOnPlan.billedOnInvoiceNumber}</span>.
                    </>
                  ) : (
                    // ⚠️ And only a NUMBERED devis has an échéancier to owe anything on. An un-numbered
                    // treatment has no schedule at all, so « Reste à encaisser sur le devis » would quote a
                    // balance against a document that does not exist — the money is collected séance by
                    // séance on the fiche, which is where « Encaissé aujourd'hui » states the same figure.
                    row.act.billedOnPlan.planNumber != null &&
                    row.act.billedOnPlan.outstanding > 0 && (
                      <>
                        {" "}Reste à encaisser sur le devis :{" "}
                        <span className="font-mono tabular-nums">
                          {formatDT(row.act.billedOnPlan.outstanding)}
                        </span>
                        .
                      </>
                    )
                  )}
                  {/*
                    ⚠️ **When the devis exists, said here and once.** Everything above is in the present tense
                    about a document that has not been created yet — `materialiseTreatments` mints it on save,
                    exactly like a split protocol — and the sentence a dentist cannot recover from not having
                    read is that it is numbered and accepted, so it will never be deletable. Removing this act
                    from the séance is the whole of « annuler » while this line is on screen, and that stops
                    being true the moment the booking is saved.
                  */}
                  {row.act.pendingContinuation && (
                    <>
                      {" "}
                      <span className="font-medium">
                        Le devis sera créé à l&apos;enregistrement de ce rendez-vous, numéroté et accepté : il
                        ne se supprimera plus, il s&apos;annulera avec un motif. Retirez cet acte pour y
                        renoncer.
                      </span>
                    </>
                  )}
                </p>
              )}
            </li>
          ))}
        </ul>
      )}

      {/*
        Searchable, because a clinic's catalogue is long and a Select that lists all of it is unscannable.
        `modal` on the Popover: the parent Dialog disables pointer events outside its content, so a non-modal
        Popover portalled to <body> inherits pointer-events:none and its items can only be keyboard-selected.

        **The first act is the chooser itself.** With no acts yet this renders as an ordinary select box — same
        height, same placeholder, same chevron as every other field in the dialog — because choosing the act is
        the expected next step, not an extra one. An empty list behind an « Ajouter un acte » button made the
        common single-act booking cost one more click than it used to. Only from the second act on does it become
        a compact « Ajouter un autre acte », which is where an explicit add really is the intent.
      */}
      <Popover open={pickerOpen} onOpenChange={setPickerOpen} modal>
        <PopoverTrigger asChild>
          <Button
            id={`${idPrefix}-add`}
            type="button"
            variant="outline"
            size={rows.length === 0 ? "default" : "sm"}
            className={cn(
              "w-full font-normal",
              rows.length === 0 ? "h-10 justify-between" : "h-9 justify-start gap-2",
            )}
            disabled={disabled || loading || atCap}
            aria-expanded={pickerOpen}
          >
            {rows.length === 0 ? (
              <>
                <span className={cn("truncate", !loading && "text-muted-foreground")}>
                  {loading ? "Chargement des actes…" : "Sélectionner un type d'acte"}
                </span>
                <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
              </>
            ) : (
              <>
                <Plus className="h-4 w-4" />
                {loading
                  ? "Chargement des actes…"
                  : atCap
                    ? `Maximum ${MAX_ACTS} actes atteint`
                    : "Ajouter un autre acte…"}
              </>
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="p-0" align="start" style={{ width: "var(--radix-popover-trigger-width)" }}>
          <Command>
            <CommandInput placeholder="Rechercher un acte…" />
            <CommandList>
              <CommandEmpty>Aucun acte ne correspond.</CommandEmpty>

              {/*
                ⚠️ **The devis comes FIRST, above every discipline.** When a patient has work quoted and not
                finished, that is overwhelmingly what the visit is for — so the act the dentist wants is the
                first thing under the cursor, not something to be found among a hundred catalogue entries whose
                names it shares. Rendered only when there is something outstanding: an empty « Actes du devis »
                heading would teach a feature this patient does not have.

                Each row states the devis and the étape that comes next, because « Couronne » on its own cannot
                be told from the catalogue's « Couronne » one group down — and the number is what the dentist has
                in front of him on paper. All three go into `value` too, since `cmdk` matches on `value` alone:
                typing « 2026-0004 » or « scellement » has to find this row.
              */}
              {planActs && planActs.length > 0 && (
                <CommandGroup heading="Actes du devis">
                  {planActs.map((preset) => {
                    const already = value.some((a) => a.treatmentPlanItemId === preset.planItemId)
                    // ⚠️ `find(o => !o.done)`, never `steps[0]`: the list now carries the réalisé steps too
                    // (see `PlanStepOption.done`), so `[0]` on a bridge whose préparation is finished would
                    // announce — and search on — a séance that has already happened.
                    const nextStep = preset.steps?.find((o) => o.id === preset.preselectedStepId)
                      ?? preset.steps?.find((o) => !o.done)
                    const devis = preset.billedOnPlan?.planNumber
                    return (
                      <CommandItem
                        key={preset.planItemId}
                        // Everything a reader can see is searchable — see the note above.
                        value={`${preset.label} ${devis ?? ""} ${nextStep?.label ?? ""} devis`}
                        onSelect={() => {
                          // Already on the séance: keep it visible and ticked rather than removing it. Vanishing
                          // reads as « it failed », and a second tap must not silently detach a devis link.
                          if (!already) addPlanAct(preset)
                          setPickerOpen(false)
                        }}
                        className="coarse:py-3"
                      >
                        <Stethoscope className="me-2 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate">{preset.label}</span>
                          <span className="block truncate text-2xs text-muted-foreground">
                            {devis ? `devis ${devis}` : "devis"}
                            {nextStep ? ` · prochaine étape : ${nextStep.label}` : ""}
                          </span>
                        </span>
                        {already && (
                          <Check className="ms-2 h-4 w-4 shrink-0 text-success" aria-hidden="true" />
                        )}
                      </CommandItem>
                    )
                  })}
                </CommandGroup>
              )}

              {/*
                One CommandGroup per clinical discipline, in the order a course of treatment runs.
                A flat list of a clinic's whole catalogue is a wall of French with no landmarks; the headings turn
                it into something you scan rather than read. `cmdk` hides a group whose every item is filtered
                out, so typing collapses this back to a flat ranked list on its own — the same behaviour the
                fiche's picker gets by branching on `searching`, here for free.
              */}
              {procedureGroups.map(({ label, items }) => (
                <CommandGroup key={label} heading={label}>
                  {items.map((pt) => {
                    const already = selectedIds.has(pt.id)
                    return (
                      <CommandItem
                        key={pt.id}
                        // The discipline joins the searchable value, so « endo » finds « Traitement de canal ».
                        // cmdk matches on `value` alone, so leaving it out would make the group headings
                        // searchable to the eye but not to the keyboard.
                        value={pt.category ? `${pt.name} ${pt.category}` : pt.name}
                        // Kept visible but ticked rather than filtered out: an act vanishing from the list the
                        // moment it is picked reads as "it failed", and the tick is what says it is already in.
                        onSelect={() => {
                          if (!already) addAct(pt.id)
                          setPickerOpen(false)
                        }}
                      >
                        <Check className={cn("mr-2 h-4 w-4", already ? "opacity-100" : "opacity-0")} />
                        <span
                          className="mr-2 h-3 w-3 rounded-full"
                          style={{ backgroundColor: pt.colorHex }}
                          aria-hidden
                        />
                        <span className="flex-1 truncate">{pt.name}</span>
                        {/*
                          ⚠️ **Which acts are multi-séance, stated in the list where they are chosen.** The picker
                          was a name and a duration, so « Implant dentaire » sat between « Greffe osseuse » and
                          « Contention post-orthodontique » with nothing to distinguish the one that will propose
                          six visits and open a devis. A dentist learned the act was a treatment only after
                          picking it — which is exactly the wrong order, since the alternative (« Créer le devis
                          et planifier la 1re séance ») is a decision about a numbered document.
                        */}
                        {(pt.defaultSteps?.length ?? 0) > 1 && (
                          <span className="ml-2 shrink-0 rounded-full bg-primary/10 px-1.5 text-2xs font-medium text-primary">
                            {pt.defaultSteps!.length} séances
                          </span>
                        )}
                        <span className="ml-2 text-xs tabular-nums text-muted-foreground">
                          {pt.defaultDurationMinutes} min
                        </span>
                      </CommandItem>
                    )
                  })}
                </CommandGroup>
              ))}
              {/* Its own group, deliberately: creating an act is not a member of any discipline, and putting it
                  inside the last one would file it under whatever that happens to be. */}
              <CommandGroup>
                <CommandItem
                  value="__acte personnalisé nouveau__"
                  onSelect={() => {
                    setPickerOpen(false)
                    setCustomMode(true)
                    setCustomError(null)
                  }}
                >
                  <Plus className="mr-2 h-4 w-4" />
                  <span className="font-medium">Acte personnalisé…</span>
                </CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      {error && (
        <p role="status" className="flex flex-wrap items-center gap-2 text-xs text-destructive">
          <span>{error}</span>
          {onRetry && (
            <button
              type="button"
              onClick={onRetry}
              // A ~16px inline target that is the only recovery from a failed catalogue load in both booking
              // dialogs. `touch-target` plus real padding; the negative margin keeps the line height unchanged.
              className="touch-target -my-1 rounded px-1.5 py-1 underline underline-offset-2 hover:no-underline"
            >
              Réessayer
            </button>
          )}
        </p>
      )}

      {customMode && (
        <div className="space-y-3 rounded-md border bg-background p-3">
          <p className="text-sm font-medium">Nouvel acte personnalisé</p>
          {customError && <p className="text-xs text-red-600 dark:text-red-400">{customError}</p>}
          <div className="grid gap-3 sm:grid-cols-[1fr_120px_140px]">
            <div className="space-y-1">
              <Label htmlFor={`${idPrefix}-custom-name`} className="text-xs text-muted-foreground">
                Nom *
              </Label>
              <Input
                id={`${idPrefix}-custom-name`}
                value={customName}
                onChange={(e) => setCustomName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    void handleCreateCustom()
                  }
                }}
                placeholder="Nom de l'acte"
                className="h-9"
                disabled={creating}
                autoFocus
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor={`${idPrefix}-custom-duration`} className="text-xs text-muted-foreground">
                Durée (min)
              </Label>
              <Input
                id={`${idPrefix}-custom-duration`}
                type="number"
                min="1"
                max="479"
                value={customDuration}
                onChange={(e) => setCustomDuration(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    void handleCreateCustom()
                  }
                }}
                placeholder="auto"
                className="h-9"
                disabled={creating}
              />
            </div>
            <div className="space-y-1">
              {/* « Tarif au catalogue », not « Montant »: this one is permanent and seeds every future visit,
                  while each act row above carries a price for this rendez-vous only. */}
              <Label htmlFor={`${idPrefix}-custom-cost`} className="text-xs text-muted-foreground">
                Tarif au catalogue
              </Label>
              <div className="relative">
                <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">DT</span>
                {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). This « Montant » creates a
                    ProcedureType's `defaultCost` — the same field as the catalogue form's, reached from the
                    booking dialog — so its `step="0.01"` made the millime unreachable on the value that seeds
                    every invoice line, and it refused the comma the app prints with. */}
                <Input
                  id={`${idPrefix}-custom-cost`}
                  type="text"
                  inputMode="decimal"
                  value={customCost}
                  onChange={(e) => setCustomCost(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault()
                      void handleCreateCustom()
                    }
                  }}
                  placeholder="0,000"
                  className="h-9 pl-8"
                  disabled={creating}
                />
              </div>
            </div>
          </div>
          <div className="flex items-center justify-between gap-3">
            <p className="text-2xs text-muted-foreground">
              Durée et montant facultatifs. Sans durée, {fallbackDurationMinutes} min est utilisé.
            </p>
            <div className="flex shrink-0 gap-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8"
                onClick={() => {
                  setCustomMode(false)
                  setCustomError(null)
                }}
                disabled={creating}
              >
                Annuler
              </Button>
              <Button type="button" size="sm" className="h-8" onClick={handleCreateCustom} disabled={creating}>
                {creating ? "Ajout…" : "Ajouter"}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * The chair time an act books when no devis step is ticked — **the one place that rule lives**.
 *
 * <p>⚠️ A followed act books its <b>first séance only</b>; the other five are booked from the treatment, one at
 * a time, and each of those already contributes its own step's minutes. So the visit is as long as
 * « Bilan pré-implantaire · 45 min », never the 60 min the whole implant carries in the catalogue — and
 * `ProcedureStepTemplateDto.durationMinutes` says in as many words that the two are different quantities.
 * Falls back to the act's own duration when the séance states none: every act with no protocol, and every
 * protocol nobody has timed.</p>
 */
export function unbookedActMinutes(
  act: SelectedAct,
  procedureType: ProcedureTypeDto | undefined,
): number | null {
  // ⚠️ A continuation books ONE séance of an act already under way, and nothing states how long that séance
  // takes — the two steps the command synthesises carry no chair time, for the same reason their labels are
  // generic. The catalogue's figure is the whole act's, so falling through to it would book a 60-minute
  // dévitalisation for its second sitting, and the row would then shorten itself on save. Null says
  // « not stated », which is what the row and the total both already know how to print.
  if (act.pendingContinuation) return null
  return act.plannedProtocol?.[0]?.durationMinutes ?? procedureType?.defaultDurationMinutes ?? null
}

/**
 * Total booked duration of a séance, in minutes — the default length the dialogs pre-fill. Link-only acts
 * contribute nothing, because nothing anywhere knows how long a hand-typed devis line takes.
 */
export function totalActsDuration(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): number {
  const byId = new Map(procedureTypes.map((p) => [p.id, p]))
  // ⚠️ Resolved, because the split is the DEFAULT and nothing writes it back into the dialog's state: the card
  // already says « ce rendez-vous est la 1re : Bilan pré-implantaire », so the length beside it must agree.
  return resolvePlannedProtocols(acts, procedureTypes).reduce((sum, a) => {
    // ⚠️ A booked STEP contributes its own chair time, not the whole act's. « Empreinte, 30 min » inside a
    // « Bridge, 60 min » would otherwise book an hour for a half-hour sitting — and two steps of one act in
    // one séance would book two whole bridges.
    const step = a.treatmentPlanItemStepId
      ? a.stepOptions?.find((o) => o.id === a.treatmentPlanItemStepId)
      : undefined
    if (step) return sum + (step.estimatedDurationMinutes ?? 0)

    return sum + (unbookedActMinutes(a, a.procedureTypeId ? byId.get(a.procedureTypeId) : undefined) ?? 0)
  }, 0)
}

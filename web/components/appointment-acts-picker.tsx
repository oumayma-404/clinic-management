"use client"

import { useMemo, useState } from "react"
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
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import { formatAmount, formatDT, parseAmountInput, quoteFr } from "@/lib/format"
import type { AppointmentProcedurePayload } from "@/lib/api/appointments"
import type { ProcedureTypeDto } from "@/lib/api/types"

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
    const base = !act.procedureTypeId
      ? act.fallbackName ?? "Acte du devis"
      : byId.get(act.procedureTypeId)?.name ?? act.fallbackName ?? "Acte indisponible"

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
}: AppointmentActsPickerProps) {
  const [pickerOpen, setPickerOpen] = useState(false)
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

  /**
   * The chosen acts, resolved against the catalog. A row whose procedure is no longer in the active catalog is
   * **kept and marked**, never dropped: silently removing it would change what the user is about to save without
   * telling them, and on the edit dialog that means deleting an act from a booked visit.
   */
  const rows = useMemo(
    () =>
      groupActs(value).map((group) => {
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
            name: act.fallbackName ?? "Acte du devis",
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
          name: pt?.name ?? act.fallbackName ?? "Acte indisponible",
          durationMinutes: stepMinutes ?? pt?.defaultDurationMinutes ?? null,
          colorHex: pt?.colorHex ?? "#6C757D",
          missing: !pt,
          tariff: pt?.defaultCost ?? null,
        }
      }),
    [value, byId],
  )

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

      {rows.length > 0 && (
        <ul className="space-y-1.5">
          {rows.map((row) => (
            <li
              key={`${row.act.treatmentPlanItemId ?? row.act.procedureTypeId ?? "act"}-${row.group.indices[0]}`}
              className="rounded-md border bg-background px-3 py-2"
            >
              <div className="flex items-center gap-2">
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
                  "min-w-0 flex-1 text-sm [overflow-wrap:anywhere]",
                  row.missing && "text-muted-foreground italic",
                )}
              >
                {row.name}
              </span>
              {row.act.planLabel && (
                <Badge variant="outline" className="hidden shrink-0 gap-1 text-xs sm:inline-flex">
                  <Stethoscope className="h-3 w-3" />
                  {row.act.planLabel}
                </Badge>
              )}
              {row.durationMinutes != null && (
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
              {row.act.stepOptions && row.act.stepOptions.length > 0 && (
                <div className="mt-2 border-t border-dashed pt-2">
                  <p className="mb-1.5 text-2xs font-semibold text-muted-foreground">
                    Étapes de cette séance
                  </p>
                  <div className="flex flex-wrap gap-1.5">
                    {row.act.stepOptions.map((step) => {
                      // The GROUP's own steps. Read off `value` for the whole act it was the same answer for
                      // every clone, so both cards of a two-step bridge showed both chips ticked — each card
                      // claiming to be both steps.
                      const ticked = row.group.stepIds.includes(step.id)
                      return (
                        <button
                          key={step.id}
                          type="button"
                          disabled={disabled}
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

              <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                <Label
                  htmlFor={`${idPrefix}-price-${row.group.indices[0]}`}
                  className="shrink-0 text-2xs font-normal text-muted-foreground"
                >
                  Prix pour ce rendez-vous
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

              {/*
                The sentence that answers the question which stopped the dentist: he had taken 800 DT on a
                1 000 DT bridge and could not tell whether booking the next séance would charge again. It names
                the devis, THIS act's fee on it, and what is left to collect on the devis as a whole — a locked
                « 0,000 » with no explanation reads as a bug.

                ⚠️ Two figures, two scopes, and each is labelled with its own: the act's fee follows « cet
                acte » and the outstanding says « sur le devis ». Unlabelled, a reader attaches both to the act
                — which is exactly how this read « à 1 080,000 DT » for a 1 000 DT bridge.
              */}
              {row.act.billedOnPlan && (
                <p
                  className="mt-2 rounded-md border border-primary bg-primary/[0.07] p-2.5 text-2xs leading-relaxed"
                  role="status"
                >
                  <span className="font-semibold text-primary">Déjà facturé.</span>{" "}
                  {row.act.billedOnPlan.planNumber
                    ? `Cet acte est porté par le devis ${row.act.billedOnPlan.planNumber} à `
                    : "Cet acte est porté par le devis à "}
                  <span className="font-mono tabular-nums">
                    {formatDT(row.act.billedOnPlan.actCost)}
                  </span>
                  . Cette séance n&apos;ajoute pas d&apos;honoraires.
                  {row.act.billedOnPlan.outstanding > 0 && (
                    <>
                      {" "}Reste à encaisser sur le devis :{" "}
                      <span className="font-mono tabular-nums">
                        {formatDT(row.act.billedOnPlan.outstanding)}
                      </span>
                      .
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
                    const nextStep = preset.steps?.find((o) => o.id === preset.preselectedStepId)
                      ?? preset.steps?.[0]
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
 * Total booked duration of a séance, in minutes — the default length the dialogs pre-fill. Link-only acts
 * contribute nothing, because nothing anywhere knows how long a hand-typed devis line takes.
 */
export function totalActsDuration(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): number {
  const byId = new Map(procedureTypes.map((p) => [p.id, p]))
  return acts.reduce((sum, a) => {
    // ⚠️ A booked STEP contributes its own chair time, not the whole act's. « Empreinte, 30 min » inside a
    // « Bridge, 60 min » would otherwise book an hour for a half-hour sitting — and two steps of one act in
    // one séance would book two whole bridges.
    const step = a.treatmentPlanItemStepId
      ? a.stepOptions?.find((o) => o.id === a.treatmentPlanItemStepId)
      : undefined
    if (step) return sum + (step.estimatedDurationMinutes ?? 0)

    return sum + (a.procedureTypeId ? byId.get(a.procedureTypeId)?.defaultDurationMinutes ?? 0 : 0)
  }, 0)
}

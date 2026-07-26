"use client"

import { useMemo, useReducer } from "react"
import { roundMillimes } from "@/lib/format"
import { parseSurfaces } from "@/components/odontogram-conditions"
import type { DentalRecordDto, DentalRecordActDto, ProcedureTypeDto } from "@/lib/api/types"

/**
 * The act currently being composed. Its teeth are NOT stored here — an act always applies to the chart's
 * live selection, which is what makes the flow tooth-first (select teeth, then say what was done).
 */
export interface ActDraft {
  procedureTypeId: string | null
  procedureName: string
  /** The single editable price: per treated tooth when `perTooth`, otherwise the act's flat total. */
  unitCost: string
  perTooth: boolean
  /**
   * True once the dentist has used the `/dent ↔ forfait` switch on this draft. While false, `perTooth` is
   * re-derived every time the selection changes — necessary now that the procedure is chosen BEFORE the teeth
   * (the appointment proposes it), so the per-tooth default has to arm on the first tooth rather than at pick
   * time. Once locked, an explicit « forfait » is never silently flipped back.
   */
  perToothLocked: boolean
  resultingCondition: string | null
  surfaces: Set<string>
  note: string
}

/** A committed act in the open session. `key` is client-side only — server act ids are not stable. */
export interface SessionAct extends ActDraft {
  key: string
  toothNumbers: number[]
}

const emptyDraft = (): ActDraft => ({
  procedureTypeId: null,
  procedureName: "",
  unitCost: "",
  perTooth: false,
  perToothLocked: false,
  resultingCondition: null,
  surfaces: new Set<string>(),
  note: "",
})

/**
 * Whether the draft should be priced per tooth for a given selection size. An act that changes a tooth's state
 * is per-tooth; one that changes nothing (consultation, détartrage, orthodontie, prothèse) is a flat session
 * fee — and nothing is ever per-tooth with no tooth to multiply. A manual choice wins.
 */
function derivePerTooth(draft: ActDraft, selectionLength: number): boolean {
  if (selectionLength === 0) return false
  if (draft.perToothLocked) return draft.perTooth
  return draft.resultingCondition != null
}

/** A treatment-plan step's values carried into the composer when the step is linked. */
export interface PlanItemPrefill {
  designationFr?: string
  plannedCost?: number
  toothNumbers?: number[]
}

/**
 * The act's billed total: unit price × treated teeth when per-tooth, else the flat amount.
 * Rounded to the millime so float noise never reaches the UI or the API.
 */
export function resolveActCost(unitCost: string, perTooth: boolean, toothCount: number): number {
  const unit = Number.parseFloat(unitCost)
  if (!Number.isFinite(unit)) return 0
  return roundMillimes(unit * (perTooth && toothCount > 0 ? toothCount : 1))
}

/** True when the price field holds something unusable. Empty is allowed (a free act priced later). */
export function hasInvalidPrice(unitCost: string): boolean {
  const raw = unitCost.trim()
  if (raw === "") return false
  const unit = Number.parseFloat(raw)
  return !Number.isFinite(unit) || unit < 0
}

/** True when nothing has been typed into the composer yet (guards the plan-item prefill). */
function isDraftEmpty(draft: ActDraft): boolean {
  return draft.procedureName.trim() === "" && draft.unitCost.trim() === ""
}

interface SessionState {
  acts: SessionAct[]
  /** Teeth currently selected on the chart — the subject the composer applies to. */
  selection: number[]
  /** Key of the committed act being edited, or null when composing a new one. */
  editingKey: string | null
  draft: ActDraft
  nextKey: number
}

export type SessionAction =
  | { type: "reset"; record?: DentalRecordDto | null }
  | { type: "toggleTooth"; tooth: number }
  | { type: "selectMany"; teeth: number[]; additive: boolean }
  | { type: "clearSelection" }
  | { type: "patchDraft"; patch: Partial<ActDraft> }
  | { type: "pickProcedure"; procedure: ProcedureTypeDto }
  | { type: "useFreeText"; name: string }
  | { type: "applyAppointment"; procedure: ProcedureTypeDto }
  | { type: "detachProcedure" }
  | { type: "applyPlanItem"; item: PlanItemPrefill }
  | { type: "commitDraft" }
  | { type: "beginEditAct"; key: string }
  | { type: "cancelEdit" }
  | { type: "removeAct"; key: string }

const makeKey = (n: number) => `act-${n}`

/**
 * Load a persisted act back into the editor. The pricing intent is read from the stored provenance and is
 * NEVER inferred: an act with no captured unit price (or one saved as a flat fee) reopens as a forfait at its
 * stored total, so re-saving it is cost-neutral. Dividing `cost` by the tooth count to guess a unit price
 * would silently double the price of any act whose total happened to divide evenly.
 */
function actFromDto(a: DentalRecordActDto, key: string): SessionAct {
  const teeth = [...(a.toothNumbers ?? [])].sort((x, y) => x - y)
  const unit = a.unitCost
  const perTooth = a.isPerTooth && unit != null && teeth.length > 0
  return {
    key,
    procedureTypeId: a.procedureTypeId ?? null,
    procedureName: a.procedureName,
    toothNumbers: teeth,
    unitCost: String(perTooth && unit != null ? unit : a.cost),
    perTooth,
    // A saved act's pricing intent is authoritative and must never be re-derived from its selection.
    perToothLocked: true,
    resultingCondition: a.resultingCondition ?? null,
    surfaces: parseSurfaces(a.surfaces),
    note: a.note ?? "",
  }
}

function initialState(record?: DentalRecordDto | null): SessionState {
  const acts = (record?.acts ?? []).map((a, i) => actFromDto(a, makeKey(i)))
  return { acts, selection: [], editingKey: null, draft: emptyDraft(), nextKey: acts.length }
}

const sorted = (teeth: number[]) => Array.from(new Set(teeth)).sort((a, b) => a - b)

function reducer(state: SessionState, action: SessionAction): SessionState {
  switch (action.type) {
    case "reset":
      return initialState(action.record)

    case "toggleTooth": {
      const has = state.selection.includes(action.tooth)
      const selection = has
        ? state.selection.filter((t) => t !== action.tooth)
        : sorted([...state.selection, action.tooth])
      return {
        ...state,
        selection,
        draft: { ...state.draft, perTooth: derivePerTooth(state.draft, selection.length) },
      }
    }

    case "selectMany": {
      const selection = sorted(action.additive ? [...state.selection, ...action.teeth] : action.teeth)
      return {
        ...state,
        selection,
        draft: { ...state.draft, perTooth: derivePerTooth(state.draft, selection.length) },
      }
    }

    case "clearSelection":
      return { ...state, selection: [], draft: { ...state.draft, perTooth: false } }

    case "patchDraft": {
      const draft = { ...state.draft, ...action.patch }
      // Touching the switch itself locks the intent; changing the resulting condition re-derives it.
      if (action.patch.perTooth !== undefined) draft.perToothLocked = true
      else if (action.patch.resultingCondition !== undefined) {
        draft.perTooth = derivePerTooth(draft, state.selection.length)
      }
      return { ...state, draft }
    }

    case "pickProcedure": {
      const pt = action.procedure
      const draft: ActDraft = {
        ...state.draft,
        procedureTypeId: pt.id,
        procedureName: pt.name,
        // Only prefill an untouched price, so a typed amount is never overwritten.
        unitCost:
          state.draft.unitCost.trim() === "" && pt.defaultCost != null
            ? String(pt.defaultCost)
            : state.draft.unitCost,
        // A fresh pick re-opens the pricing question, so the switch un-locks.
        perToothLocked: false,
        resultingCondition: pt.resultingCondition ?? null,
      }
      return { ...state, draft: { ...draft, perTooth: derivePerTooth(draft, state.selection.length) } }
    }

    case "useFreeText": {
      // A procedure the catalogue does not carry: keep the typed name, drop the catalogue provenance, and
      // leave the price for the dentist (it commits at 0 with a warning rather than blocking).
      const draft: ActDraft = {
        ...emptyDraft(),
        procedureName: action.name.trim(),
      }
      return { ...state, draft }
    }

    case "applyAppointment": {
      // Option C: the booked procedure PROPOSES the act. Only ever fills an untouched session — reopening a
      // saved record, or a session the dentist has already started, is never overwritten. Nothing is
      // committed here: the proposal is a draft, so no act exists until the dentist confirms.
      if (state.acts.length > 0 || !isDraftEmpty(state.draft)) return state
      return reducer(state, { type: "pickProcedure", procedure: action.procedure })
    }

    case "detachProcedure":
      return { ...state, draft: { ...state.draft, procedureTypeId: null } }

    case "applyPlanItem": {
      // Carry the plan step's designation / cost / teeth into an untouched composer only.
      if (!isDraftEmpty(state.draft)) return state
      const item = action.item
      const teeth = item.toothNumbers && item.toothNumbers.length > 0 ? sorted(item.toothNumbers) : state.selection
      const draft: ActDraft = {
        ...state.draft,
        procedureName: item.designationFr ?? state.draft.procedureName,
        unitCost: item.plannedCost != null && item.plannedCost > 0 ? String(item.plannedCost) : state.draft.unitCost,
      }
      return { ...state, selection: teeth, draft: { ...draft, perTooth: derivePerTooth(draft, teeth.length) } }
    }

    case "commitDraft": {
      if (state.draft.procedureName.trim() === "") return state
      const committed = { ...state.draft, toothNumbers: [...state.selection] }

      if (state.editingKey) {
        const key = state.editingKey
        return {
          ...state,
          acts: state.acts.map((a) => (a.key === key ? { ...committed, key } : a)),
          editingKey: null,
          draft: emptyDraft(),
        }
      }

      return {
        ...state,
        acts: [...state.acts, { ...committed, key: makeKey(state.nextKey) }],
        nextKey: state.nextKey + 1,
        draft: emptyDraft(),
        // The selection is deliberately KEPT: recording a second procedure on the same tooth is the most
        // common next action, and re-clicking the tooth each time is exactly the friction being removed.
      }
    }

    case "beginEditAct": {
      const act = state.acts.find((a) => a.key === action.key)
      if (!act) return state
      const { key, toothNumbers, ...draft } = act
      return { ...state, editingKey: key, selection: [...toothNumbers], draft: { ...draft } }
    }

    case "cancelEdit":
      return { ...state, editingKey: null, draft: emptyDraft(), selection: [] }

    case "removeAct":
      return {
        ...state,
        acts: state.acts.filter((a) => a.key !== action.key),
        ...(state.editingKey === action.key ? { editingKey: null, draft: emptyDraft(), selection: [] } : {}),
      }

    default:
      return state
  }
}

/**
 * Owns the whole open session: the charted acts, the chart selection, the act being composed, and the act
 * being edited. A single reducer rather than a pile of `useState` + effects, so prefilling (edit mode, a
 * linked plan step, a catalog pick) is always an explicit dispatch and can never race user input.
 */
export function useSessionActs(record?: DentalRecordDto | null) {
  const [state, dispatch] = useReducer(reducer, record, initialState)

  /** Sum of the acts already confirmed into the session. */
  const total = useMemo(
    () =>
      roundMillimes(
        state.acts.reduce((sum, a) => sum + resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length), 0),
      ),
    [state.acts],
  )

  /** True when the draft names a procedure, i.e. confirming the session would save one more act. */
  const hasDraft = state.draft.procedureName.trim() !== ""

  /** What the draft would be billed at against the live selection. */
  const draftTotal = useMemo(
    () => (hasDraft ? resolveActCost(state.draft.unitCost, state.draft.perTooth, state.selection.length) : 0),
    [hasDraft, state.draft.unitCost, state.draft.perTooth, state.selection.length],
  )

  /**
   * What will actually be saved. The draft counts: the confirm-first flow expects the dentist to tap teeth and
   * press « Confirmer » without ever adding a second act, so a footer total that excluded the draft would read
   * 0,000 on the single most common path. While editing a committed act the draft REPLACES it rather than
   * adding to it, so that act is left out instead of being counted at its stale cost.
   */
  const grandTotal = useMemo(() => {
    const others = state.acts.reduce(
      (sum, a) =>
        a.key === state.editingKey ? sum : sum + resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length),
      0,
    )
    return roundMillimes(others + draftTotal)
  }, [state.acts, state.editingKey, draftTotal])

  const editingAct = useMemo(
    () => (state.editingKey ? (state.acts.find((a) => a.key === state.editingKey) ?? null) : null),
    [state.acts, state.editingKey],
  )

  return { ...state, total, hasDraft, draftTotal, grandTotal, editingAct, dispatch }
}

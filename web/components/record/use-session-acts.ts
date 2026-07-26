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
  resultingCondition: null,
  surfaces: new Set<string>(),
  note: "",
})

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
      // A mouth-level act has nothing to multiply, so clearing the last tooth drops per-tooth pricing.
      return { ...state, selection, draft: { ...state.draft, perTooth: state.draft.perTooth && selection.length > 0 } }
    }

    case "selectMany": {
      const selection = sorted(action.additive ? [...state.selection, ...action.teeth] : action.teeth)
      return { ...state, selection }
    }

    case "clearSelection":
      return { ...state, selection: [], draft: { ...state.draft, perTooth: false } }

    case "patchDraft":
      return { ...state, draft: { ...state.draft, ...action.patch } }

    case "pickProcedure": {
      const pt = action.procedure
      return {
        ...state,
        draft: {
          ...state.draft,
          procedureTypeId: pt.id,
          procedureName: pt.name,
          // Only prefill an untouched price, so a typed amount is never overwritten.
          unitCost:
            state.draft.unitCost.trim() === "" && pt.defaultCost != null
              ? String(pt.defaultCost)
              : state.draft.unitCost,
          // An act that changes a tooth's state is priced per tooth; one that changes nothing
          // (consultation, détartrage, orthodontie, prothèse) is a flat session fee.
          perTooth: pt.resultingCondition != null && state.selection.length > 0,
          resultingCondition: pt.resultingCondition ?? state.draft.resultingCondition,
        },
      }
    }

    case "detachProcedure":
      return { ...state, draft: { ...state.draft, procedureTypeId: null } }

    case "applyPlanItem": {
      // Carry the plan step's designation / cost / teeth into an untouched composer only.
      if (!isDraftEmpty(state.draft)) return state
      const item = action.item
      const teeth = item.toothNumbers && item.toothNumbers.length > 0 ? sorted(item.toothNumbers) : state.selection
      return {
        ...state,
        selection: teeth,
        draft: {
          ...state.draft,
          procedureName: item.designationFr ?? state.draft.procedureName,
          unitCost: item.plannedCost != null && item.plannedCost > 0 ? String(item.plannedCost) : state.draft.unitCost,
        },
      }
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

  const total = useMemo(
    () =>
      roundMillimes(
        state.acts.reduce((sum, a) => sum + resolveActCost(a.unitCost, a.perTooth, a.toothNumbers.length), 0),
      ),
    [state.acts],
  )

  const editingAct = useMemo(
    () => (state.editingKey ? (state.acts.find((a) => a.key === state.editingKey) ?? null) : null),
    [state.acts, state.editingKey],
  )

  return { ...state, total, editingAct, dispatch }
}

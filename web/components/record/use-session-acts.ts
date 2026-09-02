"use client"

import { useMemo, useReducer } from "react"
import { formatAmount, parseAmountInput, roundMillimes } from "@/lib/format"
import { parseSurfaces } from "@/components/odontogram-conditions"
import type { DentalRecordDto, DentalRecordActDto, ProcedureTypeDto } from "@/lib/api/types"

/**
 * One act of the séance, and the ONLY shape this store holds. Every card on screen is one of these: there is no
 * draft, no "act in progress" and no committed/uncommitted distinction.
 *
 * <p>⚠️ `toothNumbers` belongs to the ACT. It used to be a single `selection` shared by the whole séance, which
 * is what made « Ajouter un autre acte » mean *validate, clear the field and clear the chart* — the reported
 * bug. With teeth on the act, adding one appends a card and touches nothing else.</p>
 */
export interface SessionAct {
  /** Client-side only — server act ids are not stable across a save. */
  key: string
  procedureTypeId: string | null
  procedureName: string
  /** The single editable price: per treated tooth when `perTooth`, otherwise the act's flat total. */
  unitCost: string
  /**
   * True once the dentist has TYPED in the price field. Every other way `unitCost` gets filled — a catalogue
   * default, a saved act reopened, a plan step's quote — is a suggestion belonging to whichever act is currently
   * named, so choosing a different act must replace it. Without this flag the reducer only saw "the field is not
   * empty" and kept the previous act's tariff after « Changer d'acte ».
   */
  unitCostLocked: boolean
  perTooth: boolean
  /**
   * True once the dentist has used the `/dent ↔ forfait` switch on this act. While false, `perTooth` is
   * re-derived every time the act's teeth change — necessary because the procedure is often chosen BEFORE the
   * teeth (the appointment proposes it), so the per-tooth default has to arm on the first tooth rather than at
   * pick time. Once locked, an explicit « forfait » is never silently flipped back.
   */
  perToothLocked: boolean
  resultingCondition: string | null
  surfaces: Set<string>
  note: string
  /** The act's own teeth. Empty is legitimate — a détartrage or a panoramique is a séance-level act. */
  toothNumbers: number[]
  /** The card is showing the catalogue instead of its act. */
  picking: boolean
}

const makeKey = (n: number) => `act-${n}`

const emptyAct = (key: string): SessionAct => ({
  key,
  procedureTypeId: null,
  procedureName: "",
  unitCost: "",
  unitCostLocked: false,
  perTooth: false,
  perToothLocked: false,
  resultingCondition: null,
  surfaces: new Set<string>(),
  note: "",
  toothNumbers: [],
  picking: true,
})

/** An act the dentist has actually named — the only kind that is saved. */
export function isActNamed(act: SessionAct): boolean {
  return act.procedureName.trim() !== ""
}

/**
 * An unnamed act the dentist has nonetheless put something into. The trailing blank card is *not* touched, so it
 * is dropped silently on save; one carrying teeth or a price is refused instead, because dropping it would throw
 * away work that is visible on screen.
 */
export function isActTouched(act: SessionAct): boolean {
  return isActNamed(act) || act.toothNumbers.length > 0 || act.unitCost.trim() !== ""
}

/**
 * Whether an act should be priced per tooth for a given tooth count. An act that changes a tooth's state is
 * per-tooth; one that changes nothing (consultation, détartrage, orthodontie, prothèse) is a flat session fee —
 * and nothing is ever per-tooth with no tooth to multiply. A manual choice wins.
 */
function derivePerTooth(act: SessionAct, toothCount: number): boolean {
  if (toothCount === 0) return false
  if (act.perToothLocked) return act.perTooth
  return act.resultingCondition != null
}

/** A treatment-plan step's values carried into the first act when the step is linked. */
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
  const unit = parseAmountInput(unitCost)
  if (!Number.isFinite(unit)) return 0
  return roundMillimes(unit * (perTooth && toothCount > 0 ? toothCount : 1))
}

/** What one act will be billed at, from the act itself. */
export function actTotal(act: SessionAct): number {
  return resolveActCost(act.unitCost, act.perTooth, act.toothNumbers.length)
}

/**
 * True when the price field holds something unusable. Empty is allowed (a free act priced later).
 *
 * <p>⚠️ Both this and {@link resolveActCost} read the field through `parseAmountInput` (J8), because « Tarif » is
 * `type="text" inputMode="decimal"` — a `type="number"` input refused the comma this product prints with. Parsing
 * with bare `Number.parseFloat` here would read « 90,500 » as `90` and quietly under-bill the act by half a
 * dinar.</p>
 */
export function hasInvalidPrice(unitCost: string): boolean {
  const raw = unitCost.trim()
  if (raw === "") return false
  const unit = parseAmountInput(raw)
  return !Number.isFinite(unit) || unit < 0
}

/**
 * Rewrite the named acts so the séance bills **exactly** `target` — the « je fais ça à 1 000 » a cabinet
 * settles at the chair, entered as one figure instead of re-pricing each act by hand.
 *
 * <p><b>Why this rewrites the ACTS rather than storing a total.</b> The total on screen is
 * `Σ actTotal(namedActs)` and stays that way. A separate « overridden total » field would be a second source
 * of truth, and the one that loses is the one that bills: `DentalRecordInvoiceLines` prices the note from each
 * act's own `Cost`, so a total stored beside the acts would print on the fiche and never reach the invoice,
 * la caisse or the patient's balance. Pushing the figure down into the acts is what makes every downstream
 * read agree by construction — and it is also what makes « the user edits an act afterwards » need no conflict
 * logic at all: the total is derived, so it simply follows again. Whoever typed last wins, always.</p>
 *
 * <p><b>Proportional, not an equal subtraction.</b> Taking the difference off each act in equal parts is the
 * obvious reading and it breaks on the ordinary case: a 1 000 couronne beside a 200 détartrage, brought to
 * 1 000, would be 900 and 100 — a 10 % discount on one and half price on the other — and with a wider spread
 * it drives the smaller act negative, which is the edge case that has to be defended against rather than
 * produced. Scaling by share keeps each act's price recognisable, can never change a sign, and on the case
 * that prompted this — two equal acts, 1 200 → 1 000 — gives exactly the −100 each that was asked for.</p>
 *
 * <p>⚠️ <b>The arithmetic is in millimes, as integers.</b> Shares of a total do not divide evenly, and three
 * floats rounded independently do not add up to the figure the dentist typed — they land a millime out, which
 * on this screen reads as the app refusing the number. Largest-remainder over integers makes the parts sum to
 * the goal exactly.</p>
 *
 * <p>⚠️ <b>An act only stays « /dent » when the new amount divides evenly across its teeth.</b> Otherwise it
 * becomes a forfait at that amount. A unit price that does not multiply back — 250 over 3 teeth is 83,333, and
 * ×3 that is 249,999 — would quietly re-inflate or shrink the total the moment anything recomputed it, and the
 * negotiated-price feature learned the same lesson one surface over: a total cannot be turned back into a unit
 * price. Where it *does* divide, the per-tooth reading is kept, because the invoice renders it as « 3 × 80 »
 * and that is the line a patient and a caisse can check.</p>
 *
 * <p>⚠️ <b>Both locks are set.</b> Without `unitCostLocked` the catalogue re-prices the act the next time its
 * card is touched, and without `perToothLocked` the per-tooth default re-arms on the next tooth — either one
 * silently discards the figure that was just agreed.</p>
 *
 * <p>An act the dentist has not named is left alone: it is the trailing blank card, it is not saved, and
 * giving it a price would make it saveable.</p>
 */
export function distributeSessionTotal(acts: SessionAct[], target: number): SessionAct[] {
  const named = acts.filter(isActNamed)
  if (named.length === 0) return acts

  // Integer millimes throughout — see the note above on why floats cannot hit the typed figure.
  const goalM = Math.max(0, Math.round(roundMillimes(target) * 1000))
  const currentM = named.map((a) => Math.round(actTotal(a) * 1000))
  const currentTotalM = currentM.reduce((sum, c) => sum + c, 0)

  // Each act's exact share, kept as a numerator so the remainder can be handed out by size rather than by
  // position. With nothing priced yet there is no share to preserve, so the goal is split evenly — the only
  // honest reading of « these three acts come to 600 » when all three are blank.
  const exact = currentM.map((c) =>
    currentTotalM > 0 ? (c * goalM) / currentTotalM : goalM / named.length,
  )
  const partsM = exact.map((v) => Math.floor(v))
  let leftover = goalM - partsM.reduce((sum, p) => sum + p, 0)

  // Largest-remainder: the millimes that did not divide go to the acts closest to their next whole millime,
  // and ties go to the bigger act. Handing them all to the first act instead would visibly distort a small one.
  const byRemainder = exact
    .map((v, i) => ({ i, frac: v - Math.floor(v) }))
    .sort((a, b) => b.frac - a.frac || partsM[b.i] - partsM[a.i])
  for (let n = 0; n < byRemainder.length && leftover > 0; n++, leftover--) {
    partsM[byRemainder[n].i] += 1
  }

  /*
   * Nothing that was priced falls to zero while there is money left to spread — « gratuit » is a real thing a
   * cabinet does, but it should be typed, not arrived at by rounding. One millime is enough to keep the act
   * out of that reading, and it is taken from the largest act, which can always spare it.
   */
  if (goalM > 0) {
    for (let i = 0; i < partsM.length; i++) {
      if (partsM[i] !== 0 || currentM[i] === 0) continue
      let donor = -1
      for (let j = 0; j < partsM.length; j++) {
        if (partsM[j] > 1 && (donor === -1 || partsM[j] > partsM[donor])) donor = j
      }
      if (donor === -1) break
      partsM[donor] -= 1
      partsM[i] += 1
    }
  }

  const amountByKey = new Map<string, number>()
  named.forEach((a, i) => amountByKey.set(a.key, partsM[i]))

  return acts.map((act) => {
    const amountM = amountByKey.get(act.key)
    if (amountM === undefined) return act
    const teeth = act.toothNumbers.length
    const keepPerTooth = act.perTooth && teeth > 0 && amountM % teeth === 0
    return {
      ...act,
      unitCost: formatAmount((keepPerTooth ? amountM / teeth : amountM) / 1000),
      perTooth: keepPerTooth,
      unitCostLocked: true,
      perToothLocked: true,
    }
  })
}

interface SessionState {
  acts: SessionAct[]
  /**
   * The act the chart writes to, or null when nothing is armed. Exactly one act is armed at a time — that is what
   * lets every card stay editable without the chart having to guess which act a tapped tooth belongs to.
   */
  focusKey: string | null
  nextKey: number
}

export type SessionAction =
  | { type: "reset"; record?: DentalRecordDto | null }
  | { type: "focusAct"; key: string }
  | { type: "addAct" }
  | { type: "addFromProcedure"; procedure: ProcedureTypeDto; agreedCost?: number | null }
  | { type: "removeAct"; key: string }
  | { type: "patchAct"; key: string; patch: Partial<SessionAct> }
  | { type: "pickProcedure"; key: string; procedure: ProcedureTypeDto }
  | { type: "useFreeText"; key: string; name: string }
  | { type: "beginPicking"; key: string }
  | { type: "cancelPicking"; key: string }
  | { type: "resetUnitCostToTariff"; key: string; defaultCost: number | null }
  | { type: "toggleTooth"; tooth: number }
  | { type: "selectMany"; teeth: number[]; additive: boolean }
  | { type: "clearTeeth" }
  | { type: "applyAppointment"; procedure: ProcedureTypeDto; agreedCost?: number | null }
  | { type: "applyPlanItem"; item: PlanItemPrefill }
  /** The dentist typed the séance total; the acts follow. See {@link distributeSessionTotal}. */
  | { type: "setTotal"; total: number }

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
    // `formatAmount`, never `String(...)`: reopening a saved act must show its fee the way the rest of the
    // product prints it (« 90,500 », not « 90.5 »), and the field accepts that form back.
    unitCost: formatAmount(perTooth && unit != null ? unit : a.cost),
    // Whether the stored amount was typed or taken from a tariff is not recorded, so it is not treated as typed:
    // replacing the act re-prices from the act now chosen.
    unitCostLocked: false,
    perTooth,
    // A saved act's pricing intent is authoritative and must never be re-derived from its teeth.
    perToothLocked: true,
    resultingCondition: a.resultingCondition ?? null,
    surfaces: parseSurfaces(a.surfaces),
    note: a.note ?? "",
    picking: false,
  }
}

function initialState(record?: DentalRecordDto | null): SessionState {
  const acts = (record?.acts ?? []).map((a, i) => actFromDto(a, makeKey(i)))

  // A reopened fiche arms its act when there is exactly one, so « modifier » can edit its teeth on arrival.
  // With several, a tapped tooth has no unambiguous owner: nothing is armed and the chart asks for a card.
  if (acts.length === 1) return { acts, focusKey: acts[0].key, nextKey: 1 }
  if (acts.length > 0) return { acts, focusKey: null, nextKey: acts.length }

  const first = emptyAct(makeKey(0))
  return { acts: [first], focusKey: first.key, nextKey: 1 }
}

const sorted = (teeth: number[]) => Array.from(new Set(teeth)).sort((a, b) => a - b)

const mapAct = (state: SessionState, key: string, fn: (act: SessionAct) => SessionAct): SessionState => ({
  ...state,
  acts: state.acts.map((a) => (a.key === key ? fn(a) : a)),
})

/** Re-derives the pricing basis whenever an act's teeth change, unless the dentist has locked it. */
const withTeeth = (act: SessionAct, toothNumbers: number[]): SessionAct => ({
  ...act,
  toothNumbers,
  perTooth: derivePerTooth(act, toothNumbers.length),
})

/**
 * @param agreedCost
 *   The price negotiated for this act on the rendez-vous it was booked into, when there was one. It **wins over
 *   the catalogue tarif and arrives locked**: the whole point of typing a figure into the booking dialog is that
 *   the patient was quoted it, so re-pricing the act from the catalogue the next time the card is touched would
 *   undo the negotiation at the one moment nobody is looking at the number.
 *
 *   <p>⚠️ It is a **forfait**, hence `perTooth: false` with `perToothLocked` — see
 *   `AppointmentProcedure.AgreedCost`. Teeth are unknown at booking, so « 120 DT » cannot be a unit price
 *   without silently billing 240 for the two extractions it was agreed for.</p>
 */
function applyProcedure(act: SessionAct, pt: ProcedureTypeDto, agreedCost?: number | null): SessionAct {
  if (agreedCost != null) {
    return {
      ...act,
      procedureTypeId: pt.id,
      procedureName: pt.name,
      unitCost: formatAmount(agreedCost),
      unitCostLocked: true,
      perTooth: false,
      perToothLocked: true,
      resultingCondition: pt.resultingCondition ?? null,
      picking: false,
    }
  }

  const next: SessionAct = {
    ...act,
    procedureTypeId: pt.id,
    procedureName: pt.name,
    // The price follows the act unless the dentist typed one. Testing "is the field empty?" instead was the
    // « ce n'est pas cet acte » bug: the field still held the PREVIOUS act's tariff, so the new act was billed at
    // the old act's price. An act with no tariff clears the field rather than inheriting one that belongs to the
    // act just replaced.
    unitCost: act.unitCostLocked ? act.unitCost : pt.defaultCost != null ? formatAmount(pt.defaultCost) : "",
    // A fresh pick re-opens the pricing question, so the switch un-locks.
    perToothLocked: false,
    resultingCondition: pt.resultingCondition ?? null,
    picking: false,
  }
  return { ...next, perTooth: derivePerTooth(next, next.toothNumbers.length) }
}

function reducer(state: SessionState, action: SessionAction): SessionState {
  const focused = state.focusKey ? (state.acts.find((a) => a.key === state.focusKey) ?? null) : null

  switch (action.type) {
    case "reset":
      return initialState(action.record)

    case "focusAct":
      return { ...state, focusKey: action.key }

    case "addAct": {
      // A trailing card nobody has filled in IS the card being asked for. Appending a second blank below it is
      // how a double tap leaves two empty cards in the pile, and only one of them can ever be armed.
      const last = state.acts[state.acts.length - 1]
      if (last && !isActTouched(last)) {
        return { ...mapAct(state, last.key, (a) => ({ ...a, picking: true })), focusKey: last.key }
      }
      const act = emptyAct(makeKey(state.nextKey))
      return { acts: [...state.acts, act], focusKey: act.key, nextKey: state.nextKey + 1 }
    }

    case "addFromProcedure": {
      // The « aussi prévu à ce rendez-vous » shortcuts: fill the trailing blank if there is one, else append.
      const last = state.acts[state.acts.length - 1]
      if (last && !isActTouched(last)) {
        return {
          ...mapAct(state, last.key, (a) => applyProcedure(a, action.procedure, action.agreedCost)),
          focusKey: last.key,
        }
      }
      const act = applyProcedure(emptyAct(makeKey(state.nextKey)), action.procedure, action.agreedCost)
      return { acts: [...state.acts, act], focusKey: act.key, nextKey: state.nextKey + 1 }
    }

    case "removeAct": {
      const acts = state.acts.filter((a) => a.key !== action.key)
      // The pile is never empty: removing the last act leaves a blank card rather than a surface with no way to
      // start over.
      if (acts.length === 0) {
        const fresh = emptyAct(makeKey(state.nextKey))
        return { acts: [fresh], focusKey: fresh.key, nextKey: state.nextKey + 1 }
      }
      return { ...state, acts, focusKey: state.focusKey === action.key ? null : state.focusKey }
    }

    case "patchAct":
      return mapAct(state, action.key, (act) => {
        const next = { ...act, ...action.patch }
        // Typing a price is the one thing that makes it the dentist's own, so a later act change keeps it.
        if (action.patch.unitCost !== undefined) next.unitCostLocked = true
        // Touching the switch itself locks the intent; changing the resulting condition re-derives it.
        if (action.patch.perTooth !== undefined) next.perToothLocked = true
        else if (action.patch.resultingCondition !== undefined) {
          next.perTooth = derivePerTooth(next, next.toothNumbers.length)
        }
        return next
      })

    case "pickProcedure":
      return { ...mapAct(state, action.key, (a) => applyProcedure(a, action.procedure)), focusKey: action.key }

    case "useFreeText":
      // A procedure the catalogue does not carry: keep the typed name and the teeth already tapped, drop the
      // catalogue provenance, and leave the price to the dentist (it saves at 0 with a warning rather than
      // blocking).
      return {
        ...mapAct(state, action.key, (a) => ({
          ...a,
          procedureTypeId: null,
          procedureName: action.name.trim(),
          unitCost: "",
          unitCostLocked: false,
          perTooth: false,
          perToothLocked: false,
          resultingCondition: null,
          surfaces: new Set<string>(),
          picking: false,
        })),
        focusKey: action.key,
      }

    case "beginPicking":
      return { ...mapAct(state, action.key, (a) => ({ ...a, picking: true })), focusKey: action.key }

    case "cancelPicking":
      return mapAct(state, action.key, (a) => ({ ...a, picking: false }))

    case "resetUnitCostToTariff":
      // Deliberately NOT a `patchAct`: that path locks `unitCost`, and a tariff put back must be free to follow
      // the next act the dentist picks — which is the whole point of putting it back.
      return mapAct(state, action.key, (a) => ({
        ...a,
        unitCost: action.defaultCost != null ? formatAmount(action.defaultCost) : "",
        unitCostLocked: false,
      }))

    case "toggleTooth": {
      if (!focused) return state
      const has = focused.toothNumbers.includes(action.tooth)
      const teeth = has
        ? focused.toothNumbers.filter((t) => t !== action.tooth)
        : sorted([...focused.toothNumbers, action.tooth])
      return mapAct(state, focused.key, (a) => withTeeth(a, teeth))
    }

    case "selectMany": {
      if (!focused) return state
      const teeth = sorted(action.additive ? [...focused.toothNumbers, ...action.teeth] : action.teeth)
      return mapAct(state, focused.key, (a) => withTeeth(a, teeth))
    }

    case "clearTeeth":
      if (!focused) return state
      return mapAct(state, focused.key, (a) => withTeeth(a, []))

    case "applyAppointment": {
      // Option C: the booked procedure PROPOSES the act. Only ever fills an untouched session — reopening a saved
      // record, or a session the dentist has already started, is never overwritten. Nothing is committed: the
      // proposal is an ordinary card the dentist can change or delete.
      const first = state.acts[0]
      if (state.acts.length !== 1 || isActNamed(first)) return state
      return {
        ...mapAct(state, first.key, (a) => applyProcedure(a, action.procedure, action.agreedCost)),
        focusKey: first.key,
      }
    }

    case "applyPlanItem": {
      // Carry the plan step's designation / cost / teeth into an untouched session only.
      const first = state.acts[0]
      if (state.acts.length !== 1 || isActNamed(first) || first.unitCost.trim() !== "") return state
      const item = action.item
      const teeth = item.toothNumbers && item.toothNumbers.length > 0 ? sorted(item.toothNumbers) : first.toothNumbers
      const named = item.designationFr?.trim()
      const next: SessionAct = {
        ...first,
        procedureName: named || first.procedureName,
        unitCost: item.plannedCost != null && item.plannedCost > 0 ? formatAmount(item.plannedCost) : first.unitCost,
        // A step that names the act closes the catalogue; one that only carries teeth leaves it open.
        picking: named ? false : first.picking,
      }
      return { ...state, acts: [withTeeth(next, teeth)], focusKey: first.key }
    }

    case "setTotal":
      return { ...state, acts: distributeSessionTotal(state.acts, action.total) }

    default:
      return state
  }
}

/**
 * Owns the whole open session: the acts, and which one the chart writes to.
 *
 * <p>A single reducer rather than a pile of `useState` + effects, so prefilling (edit mode, a linked plan step, a
 * catalogue pick) is always an explicit dispatch and can never race user input.</p>
 */
export function useSessionActs(record?: DentalRecordDto | null) {
  const [state, dispatch] = useReducer(reducer, record, initialState)

  /** The acts that will actually be saved — a blank trailing card is not one of them. */
  const namedActs = useMemo(() => state.acts.filter(isActNamed), [state.acts])

  const grandTotal = useMemo(
    () => roundMillimes(namedActs.reduce((sum, a) => sum + actTotal(a), 0)),
    [namedActs],
  )

  const focusedAct = useMemo(
    () => (state.focusKey ? (state.acts.find((a) => a.key === state.focusKey) ?? null) : null),
    [state.acts, state.focusKey],
  )

  return { ...state, namedActs, grandTotal, focusedAct, dispatch }
}

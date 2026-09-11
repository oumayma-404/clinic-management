"use client"

import { useMemo, useReducer } from "react"
import { formatAmount, parseAmountInput, roundMillimes } from "@/lib/format"
import { isBridgeUnit, parseSurfaces } from "@/components/odontogram-conditions"
import type { DentalRecordDto, DentalRecordActDto, ProcedureTypeDto } from "@/lib/api/types"

/**
 * What one tooth of a bridge is. `pilier` is the **default** and is therefore not stored — the two lists on
 * `SessionAct` are the exceptions to it, which is exactly the shape the server's `ConditionFor` folds.
 *
 * ⚠️ A **fourth** role does not fit two subset lists. Do not add a third list; replace both with a role
 * map and migrate the two columns — `DentalRecordAct.ImplantPilierToothNumbers` carries the same warning.
 */
export type BridgeUnitRole = "pilier" | "pontique" | "pilierImplant"

/** The role a bridge act currently gives one tooth. */
export function bridgeRoleOf(act: SessionAct, tooth: number): BridgeUnitRole {
  if (act.ponticTeeth.includes(tooth)) return "pontique"
  if (act.implantPilierTeeth.includes(tooth)) return "pilierImplant"
  return "pilier"
}

/** The word shown on the tooth's chip, and read out by its control. */
export const BRIDGE_ROLE_LABEL: Record<BridgeUnitRole, string> = {
  pilier: "pilier",
  pontique: "pontique",
  pilierImplant: "pilier sur implant",
}

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
  /**
   * Which of `toothNumbers` are **pontiques** (suspended replacements) rather than **piliers** (crowned teeth
   * keeping their roots). Always a subset of `toothNumbers`; always empty unless `resultingCondition` is a
   * bridge unit.
   *
   * <p>⚠️ **This is the one thing an act could not previously say, and it made a bridge unrecordable.** An act
   * carries ONE `resultingCondition` for all of its teeth, so a three-unit bridge could only be entered as the
   * same procedure twice — which nothing in this form suggested — and entering it once charted three abutments
   * and no pontic: a bridge that cannot exist. Splitting it into two acts is still allowed and still prices
   * correctly; this makes the single-act path correct too.</p>
   *
   * <p>⚠️ **Never derived from position.** « The ends are piliers » is false of a pier abutment (a crowned tooth
   * in the middle of the span) and of a cantilever (a pontique hanging past the last abutment). The dentist says
   * which is which — `BridgeRolesStep` in `act-card.tsx` asks, and pre-marks nothing.</p>
   *
   * <p>⚠️ An earlier draft of this note named a `PONTIC_SEED` that was never written and does not exist
   * anywhere in the repo. Nothing seeds a role, deliberately; do not add it.</p>
   */
  ponticTeeth: number[]
  /**
   * Which of `toothNumbers` are piliers carried by an **implant** rather than by a prepared natural tooth
   * (« intermédiaire de bridge sur implant »). Always a subset of `toothNumbers` and always **disjoint from
   * `ponticTeeth`**; always empty unless `resultingCondition` is a bridge unit.
   *
   * <p>⚠️ Under the crown these are opposite things: a natural pilier keeps its root, an implant pilier has
   * a fixture in bone and none — and « does this abutment have a root? » is what a dentist reads off the chart
   * before touching it. Folded into `BridgePilier` the chart drew a rooted tooth over an implant.</p>
   */
  implantPilierTeeth: number[]
  /**
   * Has « quelles dents sont des pontiques ? » been **answered** for this act?
   *
   * <p>⚠️ **A third state, and it lives only here.** `ponticTeeth: []` means both « no pontique » and
   * « nobody was asked », and it must go on meaning both **on the wire**: `BridgeCharting.ConditionFor`'s
   * fourth branch keys on both lists being empty to leave every historical row charting exactly as it did, so
   * teaching the server a third value would put that guarantee at risk to drive a prompt. This is form state,
   * never sent, never stored.</p>
   *
   * <p>⚠️ Seeded **true** for an act read back from the server (`actFromDto`): a saved act has already been
   * through this form once, so reopening a fiche to fix a typo must not re-interrogate a bridge somebody
   * detailed last week. That one line is what makes a client-only tri-state sufficient.</p>
   */
  bridgeRolesAnswered: boolean
  /**
   * This act is carried by an accepted devis, so its fee lives once on the plan and this séance adds nothing.
   * ⚠️ **It is not the same fact as `unitCostLocked`**, which is merely « somebody has set this price » and is
   * true of a hand-typed figure too — so it cannot stand in for this.
   * <para>
   * It exists because the card derives its « geste » line from tariff-vs-typed alone: on a devis act at 0
   * against a 120 DT tariff it announced « Tarif catalogue 120,000 DT — **geste de 120,000 DT** » and offered
   * « remettre au tarif ». Both are wrong. The 0 is not a discount the dentist granted, it is « already
   * invoiced elsewhere » — and the restore link is one press from re-charging an act the devis already bills,
   * which is the double-charge this whole feature exists to prevent. Third surface of one rule, after the
   * picker's `agreedCostOf` and the edit dialog's `billedOnPlan`.
   * </para>
   */
  billedOnPlan: boolean
  /**
   * « Acte non terminé » — the dentist's own statement that this act needs another séance.
   *
   * <p>⚠️ **The one fact nothing in this product can derive.** A fiche records what was *carried out* and says
   * nothing about what remains, so no read can tell an unfinished bridge from a finished obturation — which is
   * why « C'est la suite d'une séance précédente ? » has to offer four months of history and make the dentist
   * recognise the right row. This is that missing half, and it is only ever set here, by a human.</p>
   *
   * <p>⚠️ **It states nothing about money and moves none.** An act billed 1 000 with 800 collected still owes
   * 200 on ITS NOTE, where la caisse, « Créances » and « Solde patient » already carry it. Ticking this box
   * changes no figure on this fiche or anywhere else.</p>
   *
   * <p>⚠️ **Read back from the server and sent back on every save.** `SetActs` rebuilds the whole act list, so
   * dropping it from the payload marks the act finished on an ordinary re-save — no gesture, no toast, and the
   * act silently leaves « Suites à planifier ». `actFromDto` and the modal's payload are the two halves.</p>
   */
  isUnfinished: boolean
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
  billedOnPlan: false,
  // Off by default, always: an act is unfinished only because somebody said so.
  isUnfinished: false,
  toothNumbers: [],
  ponticTeeth: [],
  implantPilierTeeth: [],
  // A brand-new act has been asked nothing. It only ever matters once the état is a bridge and it holds two
  // teeth, which is what the card gates the prompt on.
  bridgeRolesAnswered: false,
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

/**
 * One act booked into the rendez-vous, as the fiche proposes it.
 *
 * <p>⚠️ `agreedCost` is **required**, not optional — `null` has to be written out. The fiche prices a booked act
 * from the catalogue, so a caller that simply omits the price gets the tarif silently; making the field
 * mandatory turns that omission into a compile error instead of a wrong number on a patient's note.</p>
 */
export interface BookedActPrefill {
  procedure: ProcedureTypeDto
  agreedCost: number | null
  /**
   * The booked act is a devis act, so `agreedCost` is a rule (0) and not a negotiation. Required, like
   * `agreedCost`, so a caller cannot omit it and silently get the « geste » treatment on a plan act.
   */
  billedOnPlan: boolean
}

/** A treatment-plan step's values carried into the first act when the step is linked. */
export interface PlanItemPrefill {
  designationFr?: string
  plannedCost?: number
  toothNumbers?: number[]
  /**
   * True when the devis carries this act's fee — so the fiche prices it at **0** and locks the tarif, exactly as
   * a séance booked from the agenda already does.
   *
   * <p>⚠️ <b>Without it, linking a devis step by hand produced a fiche the server refuses.</b> `applyAppointment`
   * sets the flag from the appointment's own row, but a fiche opened from « Ajouter un acte dentaire » has no
   * appointment — so the act arrived at the devis' full price, unmarked, and `PlanCarriedActPricing` then imposed
   * 0 server-side. The button read « Créer la fiche — 500,000 DT » and the save came back
   * « Le montant payé (500,000 DT) dépasse le total de la séance (0,000 DT). … ajoutez l'acte qui manque » —
   * advice nobody can act on, since nothing is missing. « Encaissé sur le traitement », the field that actually
   * collects, was not offered at all, because it keys on this flag.</p>
   */
  billedOnPlan?: boolean
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
  /*
   * ⚠️ **An act the treatment prices takes no share, and leaving it in was a way THROUGH the read-only lock.**
   * `act-card` renders such an act's price `readOnly` and the server imposes 0 on it
   * (`PlanCarriedActPricing`) — but « Total » wrote straight past both: typing 150 on a séance carrying a
   * couronne moved the locked field to « 150,000 » on screen, and the save silently put it back to 0.
   * Measured on the running app, 2026-09-07.
   *
   * On a MIXED séance the same bug is worse and quieter: a couronne (carried) beside a détartrage (not), and
   * the typed total would be split between them — so the détartrage would be under-billed by whatever share
   * went to the act that cannot hold it. The total the dentist types is the honoraires of this séance, and a
   * carried act contributes none, so it must take none.
   *
   * When EVERY act is carried there is nothing to distribute and the list comes back untouched — which is
   * also the case where `patient-record-modal` withdraws the « Total » field altogether.
   */
  const named = acts.filter((a) => isActNamed(a) && !a.billedOnPlan)
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
  /**
   * **Set** — not toggle — whether one tooth is on the focused act. What the drag gesture dispatches.
   *
   * ⚠️ `useToothDragSelect` decides the direction ONCE, from the tooth the gesture began on, and then
   * *sets* every tooth in the range. Wiring it to `toggleTooth` would un-tick every tooth the pointer
   * re-crosses — which on a curve is most of them — and is the trap `paintTooth`'s own comment in
   * `odontogram.tsx` records.
   */
  | { type: "setTooth"; tooth: number; present: boolean }
  /**
   * Give one of an act's teeth its role in the bridge. Ignored for a non-bridge act, or a tooth not on it.
   *
   * ⚠️ Keyed by act `key`, **never by `focusKey`**: the answered role summary renders on a card at rest,
   * so « modifier » on the second bridge of a séance would otherwise write roles onto the first. Two bridges in
   * one visit is the case the owner reported.
   */
  | { type: "setBridgeRole"; key: string; tooth: number; role: BridgeUnitRole }
  /** The dentist answered the pontique question for this act — including by answering « aucun pontique ». */
  | { type: "answerBridgeRoles"; key: string }
  /** Put the pontique question back on screen — what « modifier » and a tooth's role word do. */
  | { type: "reopenBridgeRoles"; key: string }
  | { type: "selectMany"; teeth: number[]; additive: boolean }
  | { type: "clearTeeth" }
  | { type: "applyAppointment"; procedures: BookedActPrefill[] }
  | { type: "applyPlanItem"; item: PlanItemPrefill }
  /** The dentist typed the séance total; the acts follow. See {@link distributeSessionTotal}. */
  | { type: "setTotal"; total: number }
  /**
   * Mark the act(s) this fiche performs for a devis, once the modal has resolved which devis act that is.
   * Needed for a **reopened** fiche: `applyAppointment` carries the flag per act at creation, but a saved
   * record is built by `initialState` before any plan data has loaded, so without this the reopened card falls
   * back to reading its 0 as a « geste de 120,000 DT » with a « remettre au tarif » link — the same defect on
   * the hydration path, which is where this family of bug always reappears.
   */
  | { type: "markBilledOnPlan"; procedureTypeId: string | null }

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
    /*
     * ⚠️ Read back and intersected, not assumed empty. `SetActs` rebuilds every act from what this form sends,
     * so a fiche reopened to correct a typo would send an empty pontique list and silently flatten the bridge
     * charted last week — the same shape of loss as the `procedures`/`SetProcedures` trap. The intersection is
     * belt and braces against a row whose teeth were edited by an older client.
     */
    ponticTeeth: [...(a.ponticToothNumbers ?? [])].filter((t) => teeth.includes(t)).sort((x, y) => x - y),
    // Same read-back, same reason, second list — and made disjoint from the pontiques, as the aggregate does.
    implantPilierTeeth: [...(a.implantPilierToothNumbers ?? [])]
      .filter((t) => teeth.includes(t) && !(a.ponticToothNumbers ?? []).includes(t))
      .sort((x, y) => x - y),
    /*
     * ⚠️ **True, and that is what makes the client-only tri-state work.** A saved act has already passed
     * through this form, so the prompt must not re-ask about a bridge somebody detailed last week — including
     * one they detailed as « aucun pontique », which is indistinguishable on the wire from never asked.
     */
    bridgeRolesAnswered: true,
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
    // Not inferable from the stored row — a cost of 0 is also how a courtesy act is recorded, and this file's
    // rule is that pricing intent is read, never guessed. `markBilledOnPlan` back-fills it once the modal knows
    // which devis act the fiche is for; see the effect in `patient-record-modal.tsx`.
    billedOnPlan: false,
    // ⚠️ Read back, never defaulted to false here: this is the dentist's own statement, and the payload sends
    // whatever this holds. An editor blind to the tick erases it on the next « Enregistrer ».
    isUnfinished: a.isUnfinished === true,
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
  /*
   * ⚠️ Pruned here, at the ONE place an act's teeth change, rather than at each of the three actions that call
   * it — a tooth untapped from the act cannot remain a pontique of it, and the server intersects anyway, so a
   * stale entry would not corrupt the record but would silently reappear as a pontique if the tooth were tapped
   * back. Filtering keeps the screen and the save telling the same story.
   */
  ponticTeeth: act.ponticTeeth.filter((t) => toothNumbers.includes(t)),
  implantPilierTeeth: act.implantPilierTeeth.filter((t) => toothNumbers.includes(t)),
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
        /*
         * An état that is no longer a bridge cannot keep pontiques. The server clears them too — the aggregate
         * folds rather than refuses, precisely because a client legitimately holds a stale list — but leaving
         * them here would keep painting a travée on a chart whose act is now a couronne, until the next save
         * silently dropped it.
         */
        if (action.patch.resultingCondition !== undefined) {
          if (!isBridgeUnit(next.resultingCondition)) {
            next.ponticTeeth = []
            next.implantPilierTeeth = []
          } else if (!isBridgeUnit(act.resultingCondition)) {
            /*
             * ⚠️ It has just BECOME a bridge, so the question has not been asked about it. Without this the
             * prompt is skipped for every act picked as a couronne and corrected to a bridge afterwards — and
             * on a reopened fiche `bridgeRolesAnswered` is seeded true, so it would never appear at all.
             */
            next.bridgeRolesAnswered = false
          }
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

    case "setTooth": {
      if (!focused) return state
      const has = focused.toothNumbers.includes(action.tooth)
      if (has === action.present) return state
      const teeth = action.present
        ? sorted([...focused.toothNumbers, action.tooth])
        : focused.toothNumbers.filter((t) => t !== action.tooth)
      return mapAct(state, focused.key, (a) => withTeeth(a, teeth))
    }

    case "setBridgeRole": {
      const target = state.acts.find((a) => a.key === action.key)
      if (!target || !isBridgeUnit(target.resultingCondition)) return state
      if (!target.toothNumbers.includes(action.tooth)) return state
      /*
       * ⚠️ The two lists are kept **disjoint here**, not left to the fold: a tooth cannot be both a
       * suspended replacement and an abutment, and the aggregate makes pontique win — so a client that let
       * both hold the same tooth would show one role and save the other.
       */
      const pontiques = target.ponticTeeth.filter((t) => t !== action.tooth)
      const implants = target.implantPilierTeeth.filter((t) => t !== action.tooth)
      if (action.role === "pontique") pontiques.push(action.tooth)
      if (action.role === "pilierImplant") implants.push(action.tooth)
      return mapAct(state, action.key, (a) => ({
        ...a,
        ponticTeeth: sorted(pontiques),
        implantPilierTeeth: sorted(implants),
      }))
    }

    case "answerBridgeRoles":
      // « Aucun pontique » is a real answer and lands here with both lists empty — which is the record the
      // product already wrote, and now the one the dentist has confirmed.
      return mapAct(state, action.key, (a) => ({ ...a, bridgeRolesAnswered: true }))

    case "reopenBridgeRoles":
      return mapAct(state, action.key, (a) => ({ ...a, bridgeRolesAnswered: false }))

    case "selectMany": {
      if (!focused) return state
      const teeth = sorted(action.additive ? [...focused.toothNumbers, ...action.teeth] : action.teeth)
      return mapAct(state, focused.key, (a) => withTeeth(a, teeth))
    }

    case "clearTeeth":
      if (!focused) return state
      return mapAct(state, focused.key, (a) => withTeeth(a, []))

    case "applyAppointment": {
      // ⚠️ EVERY booked act is proposed, not just the first, and the séance's own total is why: with one card the
      // fiche showed « 1 acte » and one act's money for a visit booked for three, so the others were missed and
      // never billed. They are ordinary cards — deletable, and a deleted one is offered back — so an act that was
      // not performed is removed rather than never mentioned.
      const first = state.acts[0]
      if (state.acts.length !== 1 || isActNamed(first) || action.procedures.length === 0) return state
      const acts = action.procedures.map((p, i) =>
        // `billedOnPlan` travels with the price, because it is what the price MEANS: 0 on a devis act is a rule,
        // not a gesture. Applied after `applyProcedure` so it survives the branch that rebuilds the act.
        ({
          ...applyProcedure(i === 0 ? first : emptyAct(makeKey(state.nextKey + i - 1)), p.procedure, p.agreedCost),
          billedOnPlan: p.billedOnPlan,
        }),
      )
      return { acts, focusKey: acts[0].key, nextKey: state.nextKey + action.procedures.length - 1 }
    }

    case "applyPlanItem": {
      // Carry the plan step's designation / cost / teeth into an untouched session only.
      const first = state.acts[0]
      if (state.acts.length !== 1 || isActNamed(first) || first.unitCost.trim() !== "") return state
      const item = action.item
      const teeth = item.toothNumbers && item.toothNumbers.length > 0 ? sorted(item.toothNumbers) : first.toothNumbers
      const named = item.designationFr?.trim()
      /*
       * ⚠️ **A carried act is 0 on the fiche, and the price is NOT prefilled.** The devis prices the act once;
       * `PlanCarriedActPricing` imposes that 0 server-side whatever this sends, so putting `plannedCost` in the
       * fee field showed the dentist a total the save would refuse — « Créer la fiche — 500,000 DT » answered by
       * « le montant payé dépasse le total de la séance (0,000 DT) ». `billedOnPlan` is what makes the card read
       * « Chiffré sur le traitement » and what opens « Encaissé sur le traitement », the field that does collect.
       */
      const carried = item.billedOnPlan === true
      const next: SessionAct = {
        ...first,
        procedureName: named || first.procedureName,
        unitCost: carried
          ? "0"
          : item.plannedCost != null && item.plannedCost > 0
            ? formatAmount(item.plannedCost)
            : first.unitCost,
        billedOnPlan: carried || first.billedOnPlan,
        // A step that names the act closes the catalogue; one that only carries teeth leaves it open.
        picking: named ? false : first.picking,
      }
      return { ...state, acts: [withTeeth(next, teeth)], focusKey: first.key }
    }

    case "setTotal":
      return { ...state, acts: distributeSessionTotal(state.acts, action.total) }

    case "markBilledOnPlan": {
      // Matched on the procedure, so a mixed fiche — one devis act plus a filling done the same day — marks
      // only the act the devis actually carries. With no procedure to match on, a single-act fiche is
      // unambiguous and anything larger is left alone rather than marked on a guess.
      const target = action.procedureTypeId
      const match = (a: SessionAct) =>
        target != null ? a.procedureTypeId === target : state.acts.filter(isActNamed).length === 1
      if (!state.acts.some((a) => match(a) && !a.billedOnPlan)) return state
      return { ...state, acts: state.acts.map((a) => (match(a) ? { ...a, billedOnPlan: true } : a)) }
    }

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

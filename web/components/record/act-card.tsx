"use client"

import { useState } from "react"
import { ChevronDown, ChevronRight, Lock, RotateCcw, Trash2, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { Checkbox } from "@/components/ui/checkbox"
import { cn } from "@/lib/utils"
import { formatAmount, formatDT, parseAmountInput } from "@/lib/format"
import { conditionStyle, isBridgeUnit } from "@/components/odontogram-conditions"
import { ActCatalogPicker } from "@/components/record/act-catalog-picker"
import { ActDetailFields } from "@/components/record/act-detail-fields"
import {
  actTotal,
  hasInvalidPrice,
  isActNamed,
  BRIDGE_ROLE_LABEL,
  bridgeRoleOf,
  type BridgeUnitRole,
  type SessionAct,
  type SessionAction,
} from "@/components/record/use-session-acts"
import type { ProcedureTypeDto } from "@/lib/api/types"

/** The teeth the chart currently draws, split by arch — what « toute la bouche » is measured against. */
export interface ArchTeeth {
  all: number[]
  upper: number[]
  lower: number[]
}

const covers = (teeth: number[], set: number[]) => set.length > 0 && set.every((t) => teeth.includes(t))

/**
 * A card at rest states its teeth in one short phrase.
 *
 * <p>Naming all of them is unreadable the moment there are more than a handful: an act charted on the whole mouth
 * printed 32 numbers on a line meant to be scanned, which pushes the name out and says less than « toute la
 * bouche » does. The exact list is never lost — the chips are one click away on the armed card, the chart paints
 * them, and the full list is the line's own `title`.</p>
 */
function summariseTeeth(teeth: number[], arch: ArchTeeth): string {
  if (teeth.length === 0) return "sans dent"
  if (covers(teeth, arch.all)) return "toute la bouche"
  if (covers(teeth, arch.upper)) return "toute la mâchoire du haut"
  if (covers(teeth, arch.lower)) return "toute la mâchoire du bas"
  if (teeth.length > 5) return `${teeth.length} dents`
  return teeth.join(" · ")
}

/** Where a treatment-carried act's price is — shown where its price field would be. */
function PaidTag({ label, className }: { label?: string | null; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1 whitespace-nowrap rounded-full bg-primary/10 px-2 py-0.5 text-2xs font-semibold text-primary",
        className,
      )}
    >
      <Lock className="size-3" aria-hidden="true" />
      {label || "Inclus dans le traitement"}
    </span>
  )
}

/**
 * An act this séance puts onto the devis it is carrying out (owner's rule: no opt-out). No « + » glyph: with one
 * it read as a button (run3 A12), and there is nothing to press.
 */
function AddedTag({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center whitespace-nowrap rounded-full bg-success-wash px-2 py-0.5 text-2xs font-semibold text-success",
        className,
      )}
    >
      Ajouté au traitement
    </span>
  )
}

interface ActCardProps {
  act: SessionAct
  /** 1-based position in the séance — the card's name until an act is chosen. */
  index: number
  /** True when the chart writes to this act. Exactly one card is armed at a time. */
  focused: boolean
  procedureTypes: ProcedureTypeDto[]
  /**
   * This act's colour in the séance — its rail here, and its teeth on the chart. Resolved by the modal so the
   * two cannot disagree, and guaranteed distinct from every other act's: « quelles dents pour quel acte ? » is
   * answered by matching a card to the chart, and it stops being answerable the moment two acts share a hue.
   */
  color: string
  /** The teeth the chart draws, so a whole arch reads as « toute la bouche » rather than as 32 numbers. */
  arch: ArchTeeth
  /**
   * The tag a treatment-carried act wears where its price would be — « Inclus dans le traitement », or « Payé sur
   * la note n° N » when a note holds the money. Composed by the modal (it alone knows the note); absent → the
   * treatment wording.
   */
  paidOnLabel?: string | null
  /** The act this one finishes, on the second half of a continuation — « Suite de : … ». */
  continuationOf?: string | null
  /**
   * The devis this séance is carrying out — « 2026-0027 ». Absent when this séance names no devis, or when this
   * act is the one the devis already carries. The fiche's treatment band names that devis; this card only says
   * « Ajouté au traitement ».
   *
   * <p>⚠️ There is <b>no control</b> here. An act added to a séance a devis is carrying out goes onto that
   * devis, full stop (owner's decision, 2026-09-23) — this prop only decides whether the card can say so.</p>
   */
  addToPlanTarget?: string | null
  /** A save refusal this act caused, rendered where the offending field is. */
  error?: string | null
  /** Marked when another act in the séance names the same procedure on the same teeth. */
  duplicate?: boolean
  dispatch: (action: SessionAction) => void
  disabled?: boolean
}

/**
 * ONE act of the séance, as one card that is always both the record and the editor.
 *
 * <p>This replaced a split surface — a single-act « composeur » plus a read-only list grouped by tooth — whose
 * two defects had one cause. « Ajouter un autre acte » meant *validate, empty the field, empty the chart*, so
 * the act just entered appeared to vanish (it had gone into a fold that is shut by default); and the read-back
 * listed a three-tooth act under three « Dent NN » headings, two of them saying « inclus ». Both are unsayable
 * here: nothing is ever cleared, and an act is one card however many teeth it carries.</p>
 *
 * <p>The card has two states. <b>Armed</b> it is open — price, teeth, details — and the chart's taps land on it.
 * <b>At rest</b> it is one line carrying its name, its teeth and its money, so a séance of four acts is still
 * read at a glance and the chart is still on screen. Nothing is read-only either way: one click arms a card.</p>
 */
export function ActCard({
  act,
  index,
  focused,
  procedureTypes,
  color,
  arch,
  paidOnLabel,
  continuationOf,
  addToPlanTarget,
  error,
  duplicate,
  dispatch,
  disabled,
}: ActCardProps) {
  const [detailsOpen, setDetailsOpen] = useState(false)

  const procedure = act.procedureTypeId ? procedureTypes.find((p) => p.id === act.procedureTypeId) : undefined
  const named = isActNamed(act)
  const total = actTotal(act)
  const priceInvalid = hasInvalidPrice(act.unitCost)
  const condition = act.resultingCondition ? conditionStyle(act.resultingCondition) : null
  const toothCount = act.toothNumbers.length
  /* Whether « quelle dent porte le bridge ? » is a question worth asking of this act at all. Read from the
     shared vocabulary rather than compared here, so the chart's travée, this control and the server's own fold
     cannot come to disagree about what counts as a bridge. */
  const bridgeAct = isBridgeUnit(act.resultingCondition)

  /**
   * The gap between what is charged and what the catalogue asks. Shown because a dentist lowering a price for one
   * patient is making a *gesture*, and the product used to have nowhere to say so — the only reachable field was
   * « Payé », which means the patient still owes the difference.
   */
  const tariff = procedure?.defaultCost ?? null
  const typedUnit = parseAmountInput(act.unitCost)
  /*
   * ⚠️ **Never on an act the devis carries.** Its price is 0 by rule, so against a 120 DT tariff this offered
   * « ↺ tarif 120,000 » for a discount the dentist never granted — one press from re-charging an act the devis
   * already bills. The card's « Inclus dans le traitement » tag states the real reason.
   */
  const gesture =
    !act.billedOnPlan &&
    tariff != null && act.unitCost.trim() !== "" && Number.isFinite(typedUnit) && typedUnit !== tariff
      ? tariff - typedUnit
      : null

  const detailsSummary = [
    condition ? condition.label : "aucun état",
    act.surfaces.size > 0 ? `faces ${Array.from(act.surfaces).join(", ")}` : "aucune face",
    act.note.trim() ? "note" : null,
  ]
    .filter(Boolean)
    .join(" · ")

  /*
   * Negative block margins on the ARMED card: the button keeps its 32 / 44 px box, but the head is as tall as the
   * name. Laid out at full size it made that head 56 px on a finger — an empty band between the title and
   * « Dents » (gap2 C2-820). The margins stop at the card's top edge, so `overflow-hidden` clips none of it.
   */
  const remove = (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      className={cn(
        "size-8 shrink-0 text-muted-foreground hover:text-destructive coarse:size-11",
        focused && "-my-1 coarse:-mt-1.5 coarse:-mb-2.5",
      )}
      onClick={() => dispatch({ type: "removeAct", key: act.key })}
      disabled={disabled}
      aria-label={named ? `Supprimer ${act.procedureName}` : `Supprimer l'acte ${index}`}
      title="Supprimer cet acte"
    >
      <Trash2 className="h-4 w-4" />
    </Button>
  )

  return (
    <div
      className={cn(
        "relative overflow-hidden rounded-lg border transition-colors",
        focused ? "border-primary/70 bg-primary/[0.03]" : "hover:border-muted-foreground/40",
        priceInvalid && "border-destructive",
      )}
    >
      {/* The act's own colour, so a card and its teeth on the chart are the same thing at a glance. */}
      <span
        className="absolute inset-y-0 start-0 w-[3px]"
        style={{ backgroundColor: color }}
        aria-hidden="true"
      />

      {/* ── the head, in both states ─────────────────────────────────────────────────────────────────── */}
      <div className="flex items-start gap-1 ps-3 pe-1.5 py-1.5">
        {focused ? (
          /* `flex-wrap`: the name takes the line it needs and the tag drops under it at 320 px rather than
             squeezing the name into a column of single words. */
          <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1 py-0.5">
            {/* A free-text act has no catalogue name to fall back on, so its désignation is editable right here —
                it is what the card is identified by, and folding it away is how an unnamed act reached save and
                was silently dropped. */}
            {act.procedureTypeId === null && !act.picking ? (
              <Input
                value={act.procedureName}
                onChange={(e) => dispatch({ type: "patchAct", key: act.key, patch: { procedureName: e.target.value } })}
                className={cn("h-8 min-w-0 flex-1 basis-40 text-sm font-semibold", !named && "border-destructive")}
                placeholder="Désignation de l'acte"
                disabled={disabled}
                aria-label="Désignation de l'acte"
                aria-invalid={!named}
              />
            ) : (
              <p className="min-w-0 text-sm font-semibold leading-tight">
                {named ? (
                  act.procedureName
                ) : (
                  <span className="font-normal italic text-muted-foreground">Acte {index} — à compléter</span>
                )}
              </p>
            )}
            {duplicate && (
              <Badge variant="outline" className="border-amber-500 text-2xs text-amber-700 dark:text-amber-300">
                en double ?
              </Badge>
            )}
            {named && act.billedOnPlan && <PaidTag label={paidOnLabel} className="ms-auto" />}
            {named && !act.billedOnPlan && act.addToPlan && addToPlanTarget && (
              <AddedTag className="ms-auto" />
            )}
          </div>
        ) : (
          /* At rest the whole line arms the card — it is the most-used target on the screen, hence the painted
             44px floor on a coarse pointer rather than a `.touch-target` overlay, which would reach into the
             neighbouring card and steal its taps. */
          <button
            type="button"
            onClick={() => dispatch({ type: "focusAct", key: act.key })}
            disabled={disabled}
            className="flex min-h-9 min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-0.5 rounded-md px-1 py-1.5 text-start hover:bg-muted/60 coarse:min-h-11"
            aria-label={named ? `Modifier ${act.procedureName}` : `Compléter l'acte ${index}`}
          >
            <span className="min-w-0 basis-full truncate text-sm font-semibold sm:basis-auto sm:flex-1">
              {named ? (
                act.procedureName
              ) : (
                <span className="font-normal italic text-muted-foreground">Acte {index} — à compléter</span>
              )}
            </span>
            {/*
              ⚠️ **On the card's FACE, because the card is closed most of the time.** « À continuer » is the one
              thing about a recorded act that changes what happens next, and a fiche holding three acts shows
              three collapsed rows — so a state readable only on the armed card is a state nobody reads.

              ⚠️ **It WRAPS rather than truncates.** Every other item on this line is `truncate`, which is right
              for a name and wrong for a verdict: clipped, it says something else entirely. `basis-full` on the
              narrow layout gives it its own row instead of squeezing the name.
            */}
            {/*
              ⚠️ **Withheld on a treatment-carried act, on the SAME condition as the checkbox itself** — the
              treatment's séances already say what remains, and with the control hidden the chip would be a state
              the reader can see and cannot change. A stale tick is harmless — `ContinuationTracking`, not the
              flag, is what keeps the act off « Suites à planifier ».
            */}
            {act.isUnfinished && !act.billedOnPlan && (
              <span className="inline-flex min-w-0 basis-full items-center rounded border border-warning-ink/45 bg-warning-ink/10 px-1.5 py-px text-2xs font-medium leading-tight text-warning-ink [overflow-wrap:anywhere] sm:basis-auto">
                À continuer
              </span>
            )}
            {/* `flex-wrap` + `max-w-full`: at 320 px a long teeth phrase and the « Payé sur … » tag stack instead
                of pushing past the card. */}
            <span className="flex min-w-0 max-w-full flex-wrap items-center gap-x-2 gap-y-0.5 sm:ms-auto">
              <span
                className="max-w-[20ch] truncate text-2xs tabular-nums text-muted-foreground"
                title={toothCount > 0 ? `Dents ${act.toothNumbers.join(", ")}` : undefined}
              >
                {summariseTeeth(act.toothNumbers, arch)}
              </span>
              {/* ⚠️ No figure on an act the treatment prices: « 0,000 DT » beside its name reads as a free act.
                  The tag says where the price is instead. */}
              {act.billedOnPlan ? (
                <PaidTag label={paidOnLabel} className="bg-transparent px-0" />
              ) : (
                <span className="text-xs font-semibold tabular-nums">{formatDT(total)}</span>
              )}
            </span>
          </button>
        )}

        {/* An act going onto the devis wears its tag here instead; its figure is in the price row below. */}
        {focused && named && !act.billedOnPlan && !(act.addToPlan && addToPlanTarget) && (
          <span className="shrink-0 self-center whitespace-nowrap ps-1 text-sm font-semibold tabular-nums">
            {formatDT(total)}
          </span>
        )}
        {remove}
      </div>

      {/* ── the armed body ───────────────────────────────────────────────────────────────────────────── */}
      {focused && (
        <div className="flex flex-col gap-2.5 ps-3 pe-2.5 pb-2.5">
          {act.picking ? (
            <div className="overflow-hidden rounded-md border">
              <ActCatalogPicker
                procedureTypes={procedureTypes}
                onPick={(pt) => dispatch({ type: "pickProcedure", key: act.key, procedure: pt })}
                onFreeText={(name) => dispatch({ type: "useFreeText", key: act.key, name })}
                // Only offer a way out when there is an act to come back to; otherwise « Supprimer » is the exit.
                onCancel={named ? () => dispatch({ type: "cancelPicking", key: act.key }) : undefined}
                disabled={disabled}
                autoFocus
              />
            </div>
          ) : (
            <>
              {/* What this séance finishes, on a continuation — its own désignation is often just « continuation ». */}
              {continuationOf && (
                <span className="inline-flex w-fit max-w-full items-center gap-1 rounded-md border bg-muted/40 px-1.5 py-0.5 text-2xs text-muted-foreground">
                  Suite de&nbsp;:
                  <span className="min-w-0 truncate font-medium text-foreground">{continuationOf}</span>
                </span>
              )}
              {/*
                ⚠️ **An act the treatment prices shows NO money row at all** — only the head's « Payé sur le
                traitement » tag. A locked « 0,000 » and a live « par dent · pour tout » switch multiplying a
                figure imposed at zero were reported as « pourquoi ce 0 que je ne peux pas modifier ».

                ⚠️ It is withheld **per act**, never per fiche: a détartrage done in the same séance is real
                honoraires and keeps its price, its switch and its total. Hiding on « this séance touches a
                devis » would make that act unbillable.
              */}
              {!act.billedOnPlan && (
              <>
              {/* The price, on the card face. It used to be a read-only figure with the editable field two folds
                  down, so the dentist looked straight at the number they wanted to change and could not touch
                  it — and lowered « Payé » instead, recording a debt on a patient who owed nothing. */}
              <div className="flex flex-wrap items-center gap-2">
                <Input
                  type="text"
                  inputMode="decimal"
                  value={act.unitCost}
                  onChange={(e) => dispatch({ type: "patchAct", key: act.key, patch: { unitCost: e.target.value } })}
                  className={cn(
                    "h-9 w-28 text-right font-semibold tabular-nums",
                    priceInvalid && "border-destructive",
                  )}
                  placeholder="0,000"
                  disabled={disabled}
                  aria-label={act.perTooth ? "Prix par dent (DT)" : "Prix pour tout (DT)"}
                  aria-invalid={priceInvalid}
                />
                <span className="-ms-1 text-xs text-muted-foreground">DT</span>
                {/* ⚠️ `coarse:h-11` on both, not the inherited `.touch-target`. `buttonVariants` centres a 44 px
                    overlay on every Button, so two 32 px ones 4 px apart overhang each other and the later
                    sibling paints last — tapping the right of « par dent » would set « pour tout », i.e. silently
                    change what the act is billed at. Growing the painted box makes the overlay coincide. */}
                <div className="flex items-center gap-1 rounded-lg bg-muted p-0.5">
                  <Button
                    type="button"
                    variant={act.perTooth ? "default" : "ghost"}
                    size="sm"
                    className="h-8 px-2.5 text-2xs coarse:h-11"
                    onClick={() => dispatch({ type: "patchAct", key: act.key, patch: { perTooth: true } })}
                    disabled={disabled || toothCount === 0}
                    aria-pressed={act.perTooth}
                  >
                    par dent
                  </Button>
                  <Button
                    type="button"
                    variant={!act.perTooth ? "default" : "ghost"}
                    size="sm"
                    className="h-8 px-2.5 text-2xs coarse:h-11"
                    onClick={() => dispatch({ type: "patchAct", key: act.key, patch: { perTooth: false } })}
                    disabled={disabled}
                    aria-pressed={!act.perTooth}
                  >
                    pour tout
                  </Button>
                </div>
                {act.perTooth && toothCount > 0 && (
                  <span className="text-xs tabular-nums text-muted-foreground">
                    × {toothCount} dent{toothCount > 1 ? "s" : ""} ={" "}
                    <span className="text-sm font-semibold text-foreground">{formatDT(total)}</span>
                  </span>
                )}
                {priceInvalid && <span className="text-xs text-destructive">Montant invalide</span>}
                {!priceInvalid && act.unitCost.trim() === "" && (
                  <span className="text-xs text-warning-ink">Sans tarif</span>
                )}
                {/* The tariff, as one link and only when the price differs. The geste / majoration figure stays
                    in the name a screen reader hears. `coarse:min-h-11` grows the box: an overlay would reach
                    into the switch beside it. */}
                {gesture !== null && !priceInvalid && (
                  <button
                    type="button"
                    className="inline-flex min-h-8 items-center gap-1 rounded px-1 text-2xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline disabled:opacity-50 coarse:min-h-11"
                    onClick={() => dispatch({ type: "resetUnitCostToTariff", key: act.key, defaultCost: tariff })}
                    disabled={disabled}
                    aria-label={`Remettre au tarif catalogue ${formatDT(tariff!)} (${
                      gesture > 0 ? `geste de ${formatDT(gesture)}` : `majoration de ${formatDT(-gesture)}`
                    })`}
                  >
                    <RotateCcw className="size-3" aria-hidden="true" />
                    tarif {formatAmount(tariff!)}
                  </button>
                )}
              </div>
              </>
              )}

              {/* The act's own teeth. Chips rather than a list, so three teeth are three objects a finger can
                  remove one by one — and so the row wraps instead of pushing the card sideways at 390 px. */}
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5">
                <span className="shrink-0 text-2xs text-muted-foreground">Dents</span>
                {toothCount === 0 ? (
                  <span className="text-2xs italic text-muted-foreground">aucune</span>
                ) : (
                  act.toothNumbers.map((tooth) => (
                    /* The « × » grows its own box on a coarse pointer instead of taking a `.touch-target`
                       overlay: the chips sit a few pixels apart and the later sibling paints last, so an
                       overlay would remove the tooth next to the one aimed at. */
                    <span
                      key={tooth}
                      // Tinted with the act's own colour, the same one the chart paints these teeth in — the
                      // chip and the tooth are then visibly the same object.
                      style={{
                        backgroundColor: `color-mix(in oklab, ${color} 14%, transparent)`,
                        borderColor: `color-mix(in oklab, ${color} 42%, transparent)`,
                      }}
                      className="inline-flex min-h-7 items-center gap-0.5 rounded-md border ps-2 pe-0.5 text-xs tabular-nums coarse:min-h-11"
                    >
                      {tooth}
                      {/*
                        ⚠️ **The one control that makes a bridge recordable in a single act.** Without it an act
                        carried one état for all of its teeth, so « pilier · pontique · pilier » could only be
                        entered as the same procedure twice — which nothing here said to do — and entering it
                        once charted three abutments and no pontic: a bridge that cannot exist.

                        A word, not an icon, and on the chip rather than in the folded détails: it is a fact
                        about THIS tooth, and « P » would have to be learned. `whitespace-nowrap` because the
                        chip already wraps as a unit and « pontique » must not break inside it.
                      */}
                      {/*
                        ⚠️ **A word, and it REOPENS the question rather than cycling it.** It was a two-way
                        toggle, which said pilier/pontique and cannot express a third role: a three-state cycle
                        inside a chip hides its own options, so a thumb must tap past « pontique » to reach
                        « pilier sur implant » and can only discover that by trying. The roles are chosen in the
                        step below, where all three are visible at once; this states the answer and is the way
                        back to it.
                      */}
                      {bridgeAct && (
                        <button
                          type="button"
                          onClick={() => dispatch({ type: "reopenBridgeRoles", key: act.key })}
                          disabled={disabled}
                          aria-label={`La dent ${tooth} est un ${BRIDGE_ROLE_LABEL[bridgeRoleOf(act, tooth)]} — changer son rôle`}
                          className={cn(
                            "ms-0.5 inline-flex min-h-5 items-center whitespace-nowrap rounded px-1 font-sans text-2xs font-medium transition-colors coarse:min-h-11 coarse:px-2",
                            bridgeRoleOf(act, tooth) === "pilier"
                              ? "text-muted-foreground hover-hover:hover:text-foreground"
                              : "bg-foreground/10 text-foreground",
                          )}
                        >
                          {BRIDGE_ROLE_LABEL[bridgeRoleOf(act, tooth)]}
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => dispatch({ type: "toggleTooth", tooth })}
                        disabled={disabled}
                        className="inline-flex size-5 items-center justify-center rounded text-muted-foreground hover:bg-destructive/10 hover:text-destructive coarse:size-11"
                        aria-label={`Retirer la dent ${tooth} de ${act.procedureName || "cet acte"}`}
                      >
                        <X className="h-3 w-3" />
                      </button>
                    </span>
                  ))
                )}
              </div>

              {/*
                ⚠️ **Stated, never seeded.** « The ends are piliers and the middles are pontiques » was the
                obvious default and it is wrong in three arrangements a dentist meets: a **pier abutment** is a
                crowned tooth in the MIDDLE of the span, a **cantilever** hangs a pontique past the last
                abutment, and a bridge crossing the midline is not even in anatomical order when its FDI numbers
                are sorted (12 · 11 · 21 reads 11 · 12 · 21). A visible wrong default on a clinical form is worse
                than none, because it is accepted.

                So nothing is assumed and the consequence is said out loud instead: this is what will be charted
                if the dentist marks nothing, which is also exactly what the product did before this control
                existed. `role="status"`, and only while it is actually true.
              */}
              {bridgeAct && toothCount > 1 && (
                <BridgeRolesStep act={act} dispatch={dispatch} disabled={disabled} />
              )}

              {/*
                ⚠️ **« Acte non terminé » — the one fact nothing in this product can derive, so it has to be
                asked here or not at all.** A fiche records what was *carried out* and says nothing about what
                remains, which is why « C'est la suite d'une séance précédente ? » offers four months of history
                and makes the dentist recognise the right row out of it. One tick at the chair replaces that
                recognition — and feeds « Suites à planifier », the worklist that chases the séance nobody
                booked.

                ⚠️ **On the armed body, NOT inside the « Détails » fold**, which is where the spec first put it.
                That fold is collapsed by default and summarised in one truncated line, and the whole value of
                this feature is that somebody actually ticks the box: a control the dentist has to go looking
                for is one the worklist behind it never hears from. It sits after the teeth because that is the
                end of the act's clinical entry — the moment the question « est-ce fini ? » is answerable.

                ⚠️ **It states nothing about money and moves none.** An act billed 1 000 with 800 collected
                still owes 200 on its note, where la caisse, « Créances » and « Solde patient » already carry
                it. Ticking this changes no figure here or anywhere else. The label says what it means
                (« À continuer une autre séance ») and nothing more — the owner's rule, no helper under a control.

                The `<label>` wraps the control, so the text is part of the target: one ≥ 44 px hit area on a
                coarse pointer without a `.touch-target` overlay that would reach into the row below.
              */}
              {/*
                ⚠️ **Withheld on an act a TREATMENT already carries, and that is the whole condition.** Such an
                act has its séances behind it — the band at the top of the fiche draws them — so the devis is
                already the authority on what remains, and a second, free-text « à continuer » beside it is a
                rival answer to a question that is already answered. The flag exists for the case the devis
                cannot cover: a one-off act, no treatment, nothing tracking the remainder.

                ⚠️ **Per ACT, never per séance**: a mixed visit — a carried couronne beside an ordinary
                détartrage — must keep the box on the détartrage, which is exactly the act that might be left
                unfinished with nothing to chase it.

                ⚠️ **Hiding it cannot strand a tick.** The flag is never cleared automatically (it records what
                the dentist saw), but « Suites à planifier » excludes an act on a live devis through
                `ContinuationTracking` and not through the flag — so an act ticked before it joined a treatment
                leaves the worklist anyway, and there is no nag left behind for a control that is no longer on
                screen to answer.
              */}
              {/* ⚠️ `addToPlan` hides it for the reason the paragraph above already gives for a devis act: the
                  tick would be INERT. « Suites à planifier » excludes an act on a live devis through
                  `ContinuationTracking`, and this act is about to be on one — so the box would record a
                  statement nothing reads, on a card that also says the devis now owns the work. */}
              {!act.billedOnPlan && !act.addToPlan && (
              <label
                htmlFor={`${act.key}-unfinished`}
                className={cn(
                  "flex w-fit max-w-full cursor-pointer items-center gap-2 rounded-md px-1 py-1 transition-colors coarse:min-h-11",
                  act.isUnfinished ? "bg-warning-ink/[0.06]" : "hover-hover:hover:bg-muted/50",
                  disabled && "cursor-not-allowed opacity-60",
                )}
              >
                <Checkbox
                  id={`${act.key}-unfinished`}
                  checked={act.isUnfinished}
                  onCheckedChange={(checked) =>
                    dispatch({ type: "patchAct", key: act.key, patch: { isUnfinished: checked === true } })
                  }
                  disabled={disabled}
                  className="shrink-0"
                />
                <span className="min-w-0 text-xs font-medium leading-tight">À continuer une autre séance</span>
              </label>
              )}

              {/* ── the act's detail, folded but summarised, with « Changer d'acte » beside it ───────── */}
              {/* `flex-wrap` + `basis-48`: at 320 px « Changer d'acte » drops under the fold instead of cutting
                  its summary to « Couronn… » (run3 C1-fiche-320). */}
              <div className="flex flex-wrap items-start gap-x-2 gap-y-1">
                <div className="min-w-0 flex-1 basis-48 rounded-md border">
                  <button
                    type="button"
                    onClick={() => setDetailsOpen((v) => !v)}
                    aria-expanded={detailsOpen}
                    className="flex min-h-9 w-full items-center gap-2 rounded-md px-2.5 py-1.5 text-left hover:bg-muted coarse:min-h-11"
                  >
                    {detailsOpen ? (
                      <ChevronDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                    ) : (
                      <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                    )}
                    <span className="shrink-0 text-xs font-semibold">Détails</span>
                    <span className="min-w-0 flex-1 truncate text-2xs text-muted-foreground">{detailsSummary}</span>
                  </button>
                  {detailsOpen && (
                    <div className="grid gap-3 border-t px-2.5 pb-2.5 pt-2.5">
                      <ActDetailFields act={act} dispatch={dispatch} disabled={disabled} />
                    </div>
                  )}
                </div>
                <button
                  type="button"
                  className="ms-auto inline-flex min-h-9 shrink-0 items-center rounded px-1.5 text-xs font-medium text-primary underline-offset-2 hover:underline disabled:opacity-50 coarse:min-h-11"
                  onClick={() => dispatch({ type: "beginPicking", key: act.key })}
                  disabled={disabled}
                  aria-label={named ? `Changer d'acte — ${act.procedureName}` : undefined}
                >
                  Changer d&apos;acte
                </button>
              </div>
            </>
          )}
        </div>
      )}

      {error && (
        <p role="alert" className="border-t bg-destructive/5 px-3 py-1.5 text-xs font-medium text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}

/**
 * **« Quelles dents sont des pontiques ? »** — the follow-up a bridge act asks, and the only place the three
 * roles are chosen.
 *
 * ## Why this exists at all
 *
 * The roles were already recordable: each tooth chip carried a two-way « pilier »/« pontique » toggle, and it
 * charted correctly. What it never did was **ask**. It was a `text-2xs` word tucked inside a tooth chip, so a
 * dentist entering a three-unit bridge had no reason to think the shape was theirs to state — the reported
 * symptom was « you have to select the piliers and the pontiques separately », i.e. the flow was believed not to
 * exist. So the question is now put, once, in words, at the moment it can be answered.
 *
 * ## ⚠️ Always rendered; `bridgeRolesAnswered` decides EXPANDED vs COLLAPSED
 *
 * Not appear/disappear. Appearing on the 2nd tooth and vanishing on the answer leaves two bad options for a
 * tooth added afterwards: re-open (and nag once per tooth while the dentist taps four of them) or stay shut
 * (and silently default the new tooth to pilier, which is the wrong-default defect this whole feature exists to
 * remove). Collapsed to a **live summary** instead, so a tooth added later is visibly accounted for.
 *
 * ## ⚠️ « Aucun pontique » is a real answer, not a dismissal
 *
 * A bridge with no pontique is ordinary — nothing is missing, so nothing is suspended — and it is what the
 * product recorded before any of this existed. Answering it writes exactly that record, with the difference
 * that the dentist has now confirmed it rather than merely not been asked.
 *
 * ## ⚠️ Nothing is seeded, and that is not laziness
 *
 * « The ends are piliers and the middle is a pontique » is wrong for a **pier abutment** (crowned, in the
 * middle of the span), for a **cantilever** (a pontique hanging past the last abutment) and for a bridge
 * crossing the midline (12 · 11 · 21 sorts to 11 · 12 · 21, so « the middle one » is the wrong tooth). A visible
 * wrong default on a clinical form is worse than none, because it is accepted.
 */
function BridgeRolesStep({
  act,
  dispatch,
  disabled,
}: {
  act: SessionAct
  dispatch: React.Dispatch<SessionAction>
  disabled?: boolean
}) {
  if (act.bridgeRolesAnswered) {
    const byRole = new Map<BridgeUnitRole, number[]>()
    for (const tooth of act.toothNumbers) {
      const role = bridgeRoleOf(act, tooth)
      byRole.set(role, [...(byRole.get(role) ?? []), tooth])
    }
    // Piliers first, then what is suspended — the order the span is read in.
    const order: BridgeUnitRole[] = ["pilier", "pilierImplant", "pontique"]
    const summary = order
      .filter((r) => byRole.has(r))
      .map((r) => `${byRole.get(r)!.join(" · ")} ${BRIDGE_ROLE_LABEL[r]}${byRole.get(r)!.length > 1 ? "s" : ""}`)
      .join(" — ")

    return (
      <p className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-2xs text-muted-foreground">
        <span>{summary}</span>
        <button
          type="button"
          onClick={() => dispatch({ type: "reopenBridgeRoles", key: act.key })}
          disabled={disabled}
          className="min-h-5 rounded font-medium text-primary underline-offset-2 hover-hover:hover:underline coarse:min-h-11"
        >
          modifier
        </button>
      </p>
    )
  }

  return (
    <div role="group" aria-label="Rôle de chaque dent du bridge" className="space-y-2 rounded-md border bg-muted/30 p-2">
      <p className="text-2xs font-medium">Quelles dents sont des pontiques ?</p>
      <div className="space-y-1.5">
        {act.toothNumbers.map((tooth) => {
          const role = bridgeRoleOf(act, tooth)
          return (
            <div key={tooth} className="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span className="min-w-8 text-xs tabular-nums">{tooth}</span>
              {/* All three visible at once — the reason this is not a cycle on the chip. `flex-wrap` because
                  three French role names do not fit one line inside a card at 320 px. */}
              <div className="flex flex-wrap gap-1">
                {(["pilier", "pontique", "pilierImplant"] as BridgeUnitRole[]).map((r) => (
                  <button
                    key={r}
                    type="button"
                    onClick={() => dispatch({ type: "setBridgeRole", key: act.key, tooth, role: r })}
                    disabled={disabled}
                    aria-pressed={role === r}
                    aria-label={`Dent ${tooth} : ${BRIDGE_ROLE_LABEL[r]}`}
                    className={cn(
                      "inline-flex min-h-7 items-center whitespace-nowrap rounded border px-2 text-2xs font-medium transition-colors coarse:min-h-11",
                      role === r
                        ? "border-primary/40 bg-primary/10 text-primary"
                        : "border-border text-muted-foreground hover-hover:hover:text-foreground",
                    )}
                  >
                    {BRIDGE_ROLE_LABEL[r]}
                  </button>
                ))}
              </div>
            </div>
          )
        })}
      </div>
      <div className="flex flex-wrap gap-2">
        {/* Two ways out, and the first is an ANSWER: a bridge with nothing missing has no pontique, and saying
            so is a statement about the mouth rather than a way of closing the question. */}
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 text-xs coarse:h-11"
          disabled={disabled}
          onClick={() => {
            for (const tooth of act.toothNumbers) {
              dispatch({ type: "setBridgeRole", key: act.key, tooth, role: "pilier" })
            }
            dispatch({ type: "answerBridgeRoles", key: act.key })
          }}
        >
          Aucun pontique
        </Button>
        <Button
          type="button"
          size="sm"
          className="h-8 text-xs coarse:h-11"
          disabled={disabled}
          onClick={() => dispatch({ type: "answerBridgeRoles", key: act.key })}
        >
          Valider
        </Button>
      </div>
    </div>
  )
}

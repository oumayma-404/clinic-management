"use client"

import { useState } from "react"
import { ChevronDown, ChevronRight, Trash2, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { formatDT, parseAmountInput } from "@/lib/format"
import { conditionStyle } from "@/components/odontogram-conditions"
import { ActCatalogPicker } from "@/components/record/act-catalog-picker"
import { ActDetailFields } from "@/components/record/act-detail-fields"
import {
  actTotal,
  hasInvalidPrice,
  isActNamed,
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
  /** Set when this card holds the act the appointment booked and nothing has been changed. */
  proposedFromAppointment?: boolean
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
  proposedFromAppointment,
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

  /**
   * The gap between what is charged and what the catalogue asks. Shown because a dentist lowering a price for one
   * patient is making a *gesture*, and the product used to have nowhere to say so — the only reachable field was
   * « Payé », which means the patient still owes the difference.
   */
  const tariff = procedure?.defaultCost ?? null
  const typedUnit = parseAmountInput(act.unitCost)
  const gesture =
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

  const remove = (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      className="size-8 shrink-0 text-muted-foreground hover:text-destructive coarse:size-11"
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
          <div className="min-w-0 flex-1 py-0.5">
            <p className="flex flex-wrap items-center gap-1.5 font-mono text-2xs uppercase tracking-[0.09em] text-muted-foreground">
              {proposedFromAppointment ? "Acte prévu au rendez-vous" : `Acte ${index}`}
              {duplicate && (
                <Badge variant="outline" className="border-amber-500 text-2xs text-amber-700 dark:text-amber-300">
                  en double ?
                </Badge>
              )}
            </p>
            {/* A free-text act has no catalogue name to fall back on, so its désignation is editable right here —
                it is what the card is identified by, and folding it away is how an unnamed act reached save and
                was silently dropped. */}
            {act.procedureTypeId === null && !act.picking ? (
              <Input
                value={act.procedureName}
                onChange={(e) => dispatch({ type: "patchAct", key: act.key, patch: { procedureName: e.target.value } })}
                className={cn("mt-1 h-8 w-full text-sm font-semibold", !named && "border-destructive")}
                placeholder="Désignation de l'acte"
                disabled={disabled}
                aria-label="Désignation de l'acte"
                aria-invalid={!named}
              />
            ) : (
              <p className="mt-0.5 text-sm font-semibold leading-tight">
                {named ? (
                  act.procedureName
                ) : (
                  <span className="font-normal italic text-muted-foreground">Choisissez l&apos;acte réalisé…</span>
                )}
              </p>
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
            <span className="flex min-w-0 shrink-0 items-center gap-2 sm:ms-auto">
              <span
                className="max-w-[20ch] truncate font-mono text-2xs text-muted-foreground"
                title={toothCount > 0 ? `Dents ${act.toothNumbers.join(", ")}` : undefined}
              >
                {summariseTeeth(act.toothNumbers, arch)}
              </span>
              <span className="text-xs font-semibold tabular-nums">{formatDT(total)}</span>
            </span>
          </button>
        )}

        {focused && named && (
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
              {/* The price, on the card face. It used to be a read-only figure with the editable field two folds
                  down, so the dentist looked straight at the number they wanted to change and could not touch
                  it — and lowered « Payé » instead, recording a debt on a patient who owed nothing. */}
              <div className="flex flex-wrap items-center gap-2">
                <Input
                  type="text"
                  inputMode="decimal"
                  value={act.unitCost}
                  onChange={(e) => dispatch({ type: "patchAct", key: act.key, patch: { unitCost: e.target.value } })}
                  className={cn("h-9 w-28 text-right font-semibold tabular-nums", priceInvalid && "border-destructive")}
                  placeholder="0,000"
                  disabled={disabled}
                  aria-label={act.perTooth ? "Prix par dent (DT)" : "Montant forfaitaire (DT)"}
                  aria-invalid={priceInvalid}
                />
                {/* ⚠️ `coarse:h-11` on both, not the inherited `.touch-target`. `buttonVariants` centres a 44 px
                    overlay on every Button, so two 32 px ones 4 px apart overhang each other and the later
                    sibling paints last — tapping the right of « / dent » would set « forfait », i.e. silently
                    change what the act is billed at. Growing the painted box makes the overlay coincide. */}
                <div className="flex items-center gap-1 rounded-lg bg-muted p-0.5">
                  <Button
                    type="button"
                    variant={act.perTooth ? "default" : "ghost"}
                    size="sm"
                    className="h-8 px-2.5 text-2xs coarse:h-11"
                    onClick={() => dispatch({ type: "patchAct", key: act.key, patch: { perTooth: true } })}
                    disabled={disabled || toothCount === 0}
                    title={toothCount === 0 ? "Sélectionnez au moins une dent" : "Prix par dent"}
                    aria-pressed={act.perTooth}
                  >
                    / dent
                  </Button>
                  <Button
                    type="button"
                    variant={!act.perTooth ? "default" : "ghost"}
                    size="sm"
                    className="h-8 px-2.5 text-2xs coarse:h-11"
                    onClick={() => dispatch({ type: "patchAct", key: act.key, patch: { perTooth: false } })}
                    disabled={disabled}
                    title="Montant forfaitaire pour l'acte entier"
                    aria-pressed={!act.perTooth}
                  >
                    forfait
                  </Button>
                </div>
                <span className="text-xs tabular-nums text-muted-foreground">
                  {act.perTooth && toothCount > 0 && (
                    <>
                      × {toothCount} dent{toothCount > 1 ? "s" : ""} ={" "}
                    </>
                  )}
                  <span className="text-sm font-semibold text-foreground">{formatDT(total)}</span>
                </span>
                {priceInvalid && <span className="text-xs text-destructive">Montant invalide</span>}
                {!priceInvalid && act.unitCost.trim() === "" && (
                  <span className="text-xs text-warning-ink">Sans tarif — à compléter plus tard</span>
                )}
              </div>

              {gesture !== null && !priceInvalid && (
                <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-2xs">
                  <span className="text-warning-ink">
                    Tarif catalogue {formatDT(tariff!)} —{" "}
                    {gesture > 0 ? `geste de ${formatDT(gesture)}` : `majoration de ${formatDT(-gesture)}`}
                  </span>
                  <button
                    type="button"
                    className="touch-target underline underline-offset-2 text-muted-foreground hover:text-foreground disabled:opacity-50"
                    onClick={() => dispatch({ type: "resetUnitCostToTariff", key: act.key, defaultCost: tariff })}
                    disabled={disabled}
                  >
                    remettre au tarif
                  </button>
                </p>
              )}

              {/* The act's own teeth. Chips rather than a list, so three teeth are three objects a finger can
                  remove one by one — and so the row wraps instead of pushing the card sideways at 390 px. */}
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1.5">
                <span className="shrink-0 text-2xs text-muted-foreground">Dents</span>
                {toothCount === 0 ? (
                  <span className="text-2xs italic text-muted-foreground">
                    aucune — tapez sur le schéma, ou laissez vide pour un acte général (détartrage, panoramique…)
                  </span>
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
                      className="inline-flex min-h-7 items-center gap-0.5 rounded-md border ps-2 pe-0.5 font-mono text-xs tabular-nums coarse:min-h-11"
                    >
                      {tooth}
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

              {/* ── the act's detail, folded but summarised ───────────────────────────────────────── */}
              <div className="rounded-md border">
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

              <div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-8 text-xs"
                  onClick={() => dispatch({ type: "beginPicking", key: act.key })}
                  disabled={disabled}
                >
                  Changer d&apos;acte
                </Button>
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

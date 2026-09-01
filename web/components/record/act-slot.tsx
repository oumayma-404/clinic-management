"use client"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { formatDT, parseAmountInput } from "@/lib/format"
import { conditionStyle } from "@/components/odontogram-conditions"
import { ActCatalogPicker } from "@/components/record/act-catalog-picker"
import { hasInvalidPrice, type ActDraft, type SessionAction, type SessionAct } from "@/components/record/use-session-acts"
import type { ProcedureTypeDto } from "@/lib/api/types"

interface ActSlotProps {
  draft: ActDraft
  /** True when the draft names a procedure — i.e. there is something to show as a card. */
  hasDraft: boolean
  /** What the draft is billed at against the live selection. */
  draftTotal: number
  /** How many teeth the act applies to — drives « × N dents » and arms the per-tooth button. */
  toothCount: number
  procedureTypes: ProcedureTypeDto[]
  /** Set when the card's act came from the appointment and nothing has been confirmed yet. */
  proposedFromAppointment?: boolean
  /** The committed act being edited, or null while composing a new one. */
  editingAct: SessionAct | null
  /** How many acts the séance already holds. Decides whether an empty composer means « choose one » or « done ». */
  committedCount: number
  dispatch: (action: SessionAction) => void
  disabled?: boolean
}

/**
 * ONE slot with two states — the act as a card, or the catalogue as a list. Never both, and never a popover
 * over the dialog.
 *
 * The card state is Option C: when the appointment booked a procedure the app proposes it, priced, coloured
 * and with its état résultant already set, so the common visit needs no act entry at all. Crucially a proposal
 * is only a draft — no `DentalRecordAct` exists until the dentist confirms the session, which is why a generic
 * « Consultation » slot is harmless rather than a mis-billing risk.
 */
export function ActSlot({
  draft,
  hasDraft,
  draftTotal,
  toothCount,
  procedureTypes,
  proposedFromAppointment,
  editingAct,
  committedCount,
  dispatch,
  disabled,
}: ActSlotProps) {
  const [picking, setPicking] = useState(false)

  /*
   * Derived, not an effect: with nothing in hand and nothing recorded, the catalogue simply IS what this slot
   * renders — a first act has to be chosen from somewhere.
   *
   * ⚠️ `committedCount` is what keeps it from doing that on an EXISTING fiche. Reopening a saved séance leaves
   * the composer empty (its acts are committed, not drafted), so the slot used to greet « Modifier la fiche »
   * with an open act catalogue — which reads as « re-enter the act », and is exactly what was reported. A
   * séance that already holds acts gets a quiet resting state instead, and the catalogue on request.
   */
  const showPicker = picking || (!hasDraft && committedCount === 0)

  const procedure = draft.procedureTypeId ? procedureTypes.find((p) => p.id === draft.procedureTypeId) : undefined
  const stripe = procedure?.colorHex || "var(--border)"
  const condition = draft.resultingCondition ? conditionStyle(draft.resultingCondition) : null
  const priceInvalid = hasInvalidPrice(draft.unitCost)

  /**
   * The gap between what is charged and what the catalogue asks. Shown because the dentist lowering a price for
   * one patient is making a *gesture*, and the product used to have nowhere to say so — the only reachable field
   * was « Payé », which means the patient still owes the difference.
   */
  const tariff = procedure?.defaultCost ?? null
  const typedUnit = parseAmountInput(draft.unitCost)
  const gesture =
    tariff != null && draft.unitCost.trim() !== "" && Number.isFinite(typedUnit) && typedUnit !== tariff
      ? tariff - typedUnit
      : null

  if (showPicker) {
    return (
      <div className="overflow-hidden rounded-lg border">
        <ActCatalogPicker
          procedureTypes={procedureTypes}
          onPick={(pt) => {
            dispatch({ type: "pickProcedure", procedure: pt })
            setPicking(false)
          }}
          onFreeText={(name) => {
            dispatch({ type: "useFreeText", name })
            setPicking(false)
          }}
          // Only offer a way out when there is an act to come back to.
          onCancel={hasDraft ? () => setPicking(false) : undefined}
          disabled={disabled}
          autoFocus
        />
      </div>
    )
  }

  // Nothing in hand, but the séance is not empty: say so, and offer the catalogue rather than opening it.
  if (!hasDraft) {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-dashed p-3">
        <p className="min-w-0 flex-1 text-sm text-muted-foreground">
          {committedCount === 1 ? "1 acte enregistré" : `${committedCount} actes enregistrés`} pour cette séance.
          Modifiez-les dans « Actes de la séance », ou ajoutez-en un autre.
        </p>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-9 shrink-0 text-xs"
          onClick={() => setPicking(true)}
          disabled={disabled}
        >
          Ajouter un acte
        </Button>
      </div>
    )
  }

  return (
    <div
      className={cn(
        "rounded-lg border p-3",
        editingAct
          ? "border-amber-400 bg-amber-50/50 dark:bg-amber-950/20"
          : "border-primary/60 bg-primary/[0.03]",
      )}
    >
      <div className="flex flex-wrap items-center gap-3">
        <span
          className="min-h-[34px] w-1.5 shrink-0 self-stretch rounded-full"
          style={{ backgroundColor: stripe }}
          aria-hidden="true"
        />

        <div className="min-w-0 flex-1 basis-48">
          <p className="flex items-center gap-1.5 font-mono text-2xs uppercase tracking-[0.09em] text-muted-foreground">
            {editingAct ? (
              <Badge variant="outline" className="border-amber-500 text-2xs text-amber-700 dark:text-amber-300">
                Modification
              </Badge>
            ) : proposedFromAppointment ? (
              "Acte prévu au rendez-vous"
            ) : (
              "Acte réalisé"
            )}
          </p>
          <p className="mt-0.5 text-sm font-semibold leading-tight" title={draft.procedureName}>
            {draft.procedureName}
          </p>
          <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-2xs text-muted-foreground">
            {condition ? (
              <span className="flex items-center gap-1.5">
                <span className={cn("h-2 w-2 rounded-full", condition.swatch)} />
                {condition.label}
              </span>
            ) : (
              <span>aucun état résultant</span>
            )}
            {!procedure && (
              <>
                <span className="opacity-50">·</span>
                <span>texte libre</span>
              </>
            )}
          </p>
        </div>

        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 shrink-0 text-xs"
          onClick={() => setPicking(true)}
          disabled={disabled}
        >
          Ce n&apos;est pas cet acte
        </Button>
      </div>

      {/*
        The price, on the card. It used to be a read-only figure here and an editable field two folds down in
        « Détails de l'acte », so the dentist looked straight at the number they wanted to change and could not
        touch it — and lowered « Payé » instead, recording a debt on a patient who owed nothing.
        `pl-[26px]` lines the row up under the name, past the colour stripe and its gap.
      */}
      <div className="mt-2 flex flex-wrap items-center gap-2 sm:pl-[26px]">
        <Input
          type="text"
          inputMode="decimal"
          value={draft.unitCost}
          onChange={(e) => dispatch({ type: "patchDraft", patch: { unitCost: e.target.value } })}
          className={cn("h-9 w-28 text-right font-semibold tabular-nums", priceInvalid && "border-destructive")}
          placeholder="0,000"
          disabled={disabled}
          aria-label={draft.perTooth ? "Prix par dent (DT)" : "Montant forfaitaire (DT)"}
          aria-invalid={priceInvalid}
        />
        <div className="flex items-center gap-1 rounded-lg bg-muted p-0.5">
          <Button
            type="button"
            variant={draft.perTooth ? "default" : "ghost"}
            size="sm"
            className="h-8 px-2.5 text-2xs"
            onClick={() => dispatch({ type: "patchDraft", patch: { perTooth: true } })}
            disabled={disabled || toothCount === 0}
            title={toothCount === 0 ? "Sélectionnez au moins une dent" : "Prix par dent"}
          >
            / dent
          </Button>
          <Button
            type="button"
            variant={!draft.perTooth ? "default" : "ghost"}
            size="sm"
            className="h-8 px-2.5 text-2xs"
            onClick={() => dispatch({ type: "patchDraft", patch: { perTooth: false } })}
            disabled={disabled}
            title="Montant forfaitaire pour l'acte entier"
          >
            forfait
          </Button>
        </div>
        <span className="text-xs tabular-nums text-muted-foreground">
          {draft.perTooth && toothCount > 0 && (
            <>
              × {toothCount} dent{toothCount > 1 ? "s" : ""} ={" "}
            </>
          )}
          <span className="text-sm font-semibold text-foreground">{formatDT(draftTotal)}</span>
        </span>
        {priceInvalid && <span className="text-xs text-destructive">Montant invalide</span>}
        {!priceInvalid && draft.unitCost.trim() === "" && (
          <span className="text-xs text-warning-ink">Sans tarif — à compléter plus tard</span>
        )}
      </div>

      {gesture !== null && !priceInvalid && (
        <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-2xs sm:pl-[26px]">
          <span className="text-warning-ink">
            Tarif catalogue {formatDT(tariff!)} —{" "}
            {gesture > 0 ? `geste de ${formatDT(gesture)}` : `majoration de ${formatDT(-gesture)}`}
          </span>
          <button
            type="button"
            className="touch-target underline underline-offset-2 text-muted-foreground hover:text-foreground disabled:opacity-50"
            onClick={() => dispatch({ type: "resetUnitCostToTariff", defaultCost: tariff })}
            disabled={disabled}
          >
            remettre au tarif
          </button>
        </p>
      )}
    </div>
  )
}

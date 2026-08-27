"use client"

import { useState } from "react"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { formatDT } from "@/lib/format"
import { conditionStyle } from "@/components/odontogram-conditions"
import { ActCatalogPicker } from "@/components/record/act-catalog-picker"
import type { ActDraft, SessionAction, SessionAct } from "@/components/record/use-session-acts"
import type { ProcedureTypeDto } from "@/lib/api/types"

interface ActSlotProps {
  draft: ActDraft
  /** True when the draft names a procedure — i.e. there is something to show as a card. */
  hasDraft: boolean
  /** What the draft is billed at against the live selection. */
  draftTotal: number
  procedureTypes: ProcedureTypeDto[]
  /** Set when the card's act came from the appointment and nothing has been confirmed yet. */
  proposedFromAppointment?: boolean
  /** The committed act being edited, or null while composing a new one. */
  editingAct: SessionAct | null
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
  procedureTypes,
  proposedFromAppointment,
  editingAct,
  dispatch,
  disabled,
}: ActSlotProps) {
  const [picking, setPicking] = useState(false)

  // Derived, not an effect: with no act there is nothing to show a card for, so the list is simply what the
  // slot renders. Confirming an act empties the draft and the list comes back on its own.
  const showPicker = picking || !hasDraft

  const procedure = draft.procedureTypeId ? procedureTypes.find((p) => p.id === draft.procedureTypeId) : undefined
  const stripe = procedure?.colorHex || "var(--border)"
  const condition = draft.resultingCondition ? conditionStyle(draft.resultingCondition) : null

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
            <span className="opacity-50">·</span>
            <span>{draft.perTooth ? "tarif par dent" : "forfait"}</span>
            {!procedure && (
              <>
                <span className="opacity-50">·</span>
                <span>texte libre</span>
              </>
            )}
          </p>
        </div>

        <span className="shrink-0 text-right text-sm font-semibold tabular-nums">{formatDT(draftTotal)}</span>

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
    </div>
  )
}

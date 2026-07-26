"use client"

import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { formatDT } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, SURFACE_ORDER } from "@/components/odontogram-conditions"
import { hasInvalidPrice, resolveActCost, type ActDraft, type SessionAction } from "@/components/record/use-session-acts"

// Sentinel for "no resulting condition" (Radix Select forbids an empty-string item value).
const NO_CONDITION = "__none__"

interface ActDetailFieldsProps {
  draft: ActDraft
  /** How many teeth the act applies to — drives the per-tooth arithmetic. */
  toothCount: number
  dispatch: (action: SessionAction) => void
  disabled?: boolean
}

/**
 * Everything about the act beyond "which act, which teeth": tarif and the `/dent ↔ forfait` switch, the état
 * résultant that feeds the odontogram, the MODVL faces, and a free note. Folded behind « Détails de l'acte »
 * because the catalogue already answers all of it — but never removed, and always summarised in the header.
 */
export function ActDetailFields({ draft, toothCount, dispatch, disabled }: ActDetailFieldsProps) {
  const total = resolveActCost(draft.unitCost, draft.perTooth, toothCount)
  const priceInvalid = hasInvalidPrice(draft.unitCost)
  const conditionChip = conditionStyle(draft.resultingCondition ?? "Sain")

  const toggleSurface = (code: string) => {
    const next = new Set(draft.surfaces)
    if (next.has(code)) next.delete(code)
    else next.add(code)
    dispatch({ type: "patchDraft", patch: { surfaces: next } })
  }

  return (
    <>
      {/* A free-text act has no catalogue name to fall back on, so it stays editable here. */}
      {draft.procedureTypeId === null && draft.procedureName.trim() !== "" && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="shrink-0 text-[11px] text-muted-foreground">Désignation</span>
          <Input
            value={draft.procedureName}
            onChange={(e) => dispatch({ type: "patchDraft", patch: { procedureName: e.target.value } })}
            className="h-8 min-w-[10rem] flex-1 text-sm"
            disabled={disabled}
            aria-label="Désignation de l'acte"
          />
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-[11px] text-muted-foreground">Tarif</span>
        <Input
          type="number"
          min="0"
          step="0.001"
          value={draft.unitCost}
          onChange={(e) => dispatch({ type: "patchDraft", patch: { unitCost: e.target.value } })}
          className={cn("h-8 w-28 text-right tabular-nums", priceInvalid && "border-destructive")}
          placeholder="0.000"
          disabled={disabled}
          aria-label={draft.perTooth ? "Prix par dent (DT)" : "Montant forfaitaire (DT)"}
        />
        <div className="flex items-center gap-1 rounded-lg bg-muted p-0.5">
          <Button
            type="button"
            variant={draft.perTooth ? "default" : "ghost"}
            size="sm"
            className="h-7 px-2 text-[11px]"
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
            className="h-7 px-2 text-[11px]"
            onClick={() => dispatch({ type: "patchDraft", patch: { perTooth: false } })}
            disabled={disabled}
            title="Montant forfaitaire pour l'acte entier"
          >
            forfait
          </Button>
        </div>
        <span className="text-xs tabular-nums text-muted-foreground">
          {draft.perTooth && toothCount > 0 ? (
            <>
              × {toothCount} dent{toothCount > 1 ? "s" : ""} ={" "}
              <span className="font-semibold text-foreground">{formatDT(total)}</span>
            </>
          ) : (
            <span className="font-semibold text-foreground">{formatDT(total)}</span>
          )}
        </span>
        {priceInvalid && <span className="text-xs text-destructive">Montant invalide</span>}
        {!priceInvalid && draft.unitCost.trim() === "" && (
          <span className="text-xs text-amber-600 dark:text-amber-500">Sans tarif — à compléter plus tard</span>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-[11px] text-muted-foreground">État résultant</span>
        <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs">
          <span
            className={cn(
              "h-2.5 w-2.5 rounded-full border",
              draft.resultingCondition ? conditionChip.swatch : "bg-background",
            )}
          />
          {draft.resultingCondition ? conditionChip.label : "Aucun"}
        </span>
        <Select
          value={draft.resultingCondition ?? NO_CONDITION}
          onValueChange={(v) =>
            dispatch({ type: "patchDraft", patch: { resultingCondition: v === NO_CONDITION ? null : v } })
          }
          disabled={disabled}
        >
          <SelectTrigger className="h-8 w-44 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={NO_CONDITION}>Aucun</SelectItem>
            {CONDITION_ORDER.map((c) => (
              <SelectItem key={c} value={c}>
                {conditionStyle(c).label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <span className="ml-auto text-[11px] text-muted-foreground">alimente l&apos;odontogramme</span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-[11px] text-muted-foreground">Faces</span>
        <div className="flex items-center gap-1">
          {SURFACE_ORDER.map((s) => (
            <Button
              key={s}
              type="button"
              size="sm"
              variant={draft.surfaces.has(s) ? "default" : "outline"}
              className="h-8 w-8 p-0 text-xs"
              onClick={() => toggleSurface(s)}
              disabled={disabled}
              title={SURFACE_LABELS[s]}
              aria-pressed={draft.surfaces.has(s)}
            >
              {s}
            </Button>
          ))}
        </div>
        <Input
          value={draft.note}
          onChange={(e) => dispatch({ type: "patchDraft", patch: { note: e.target.value } })}
          placeholder="Note sur l'acte (optionnel)"
          className="h-8 min-w-[8rem] flex-1 text-xs"
          disabled={disabled}
        />
      </div>

      {toothCount > 1 && (draft.surfaces.size > 0 || draft.note.trim() !== "") && (
        <p className="text-[11px] text-muted-foreground">
          Les faces et la note s&apos;appliquent aux {toothCount} dents. Pour des faces différentes, enregistrez
          un acte par dent.
        </p>
      )}
    </>
  )
}

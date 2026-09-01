"use client"

import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, SURFACE_ORDER } from "@/components/odontogram-conditions"
import type { ActDraft, SessionAction } from "@/components/record/use-session-acts"

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
 * Everything about the act beyond "which act, which teeth, what price": the état résultant that feeds the
 * odontogram, the MODVL faces, and a free note. Folded behind « Détails de l'acte » because the catalogue
 * already answers all of it — but never removed, and always summarised in the header.
 *
 * <p>⚠️ The tarif and the `/dent ↔ forfait` switch used to live here and now sit on the act card itself
 * (`act-slot.tsx`). Folding the price away was the reported defect: the dentist saw the figure they wanted to
 * change and could not reach it, so they lowered « Payé » — the field that means the patient still owes.</p>
 */
export function ActDetailFields({ draft, toothCount, dispatch, disabled }: ActDetailFieldsProps) {
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
          <span className="shrink-0 text-2xs text-muted-foreground">Désignation</span>
          <Input
            value={draft.procedureName}
            onChange={(e) => dispatch({ type: "patchDraft", patch: { procedureName: e.target.value } })}
            className={cn(
              "h-8 min-w-[10rem] flex-1 text-sm",
              draft.procedureName.trim() === "" && "border-destructive",
            )}
            disabled={disabled}
            aria-label="Désignation de l'acte"
            // A free-text act with no name is silently DROPPED on save (`parsedActs` filters it out), so the
            // fiche saves fewer acts than the dentist charted. Marking the field is what makes that visible
            // before « Confirmer » rather than through a toast afterwards.
            aria-invalid={draft.procedureName.trim() === ""}
          />
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-2xs text-muted-foreground">État résultant</span>
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
        <span className="ml-auto text-2xs text-muted-foreground">alimente l&apos;odontogramme</span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-2xs text-muted-foreground">Faces</span>
        {/*
          ⚠️ `gap-2` + `coarse:size-11`, not `gap-1` + `h-8 w-8`.
          Five 32px buttons at `gap-1` sit on a 36px pitch, and `buttonVariants` already centres a 44px
          `touch-target` overlay on each — so every adjacent pair overlapped by 8px, and the later sibling wins.
          Tapping the right edge of « O » toggled « D »: the act is then recorded on the wrong tooth SURFACE,
          which is what the odontogram and the fiche both carry forward. Painting the 44px on a coarse pointer
          makes the hit area the button again.

          `size-8` rather than `h-8 w-8` so the base and the `coarse:` override are the same tailwind-merge
          group — with `h-8 w-8` the variant would be a different group and CSS source order, not intent, would
          decide which wins.
        */}
        <div className="flex items-center gap-2">
          {SURFACE_ORDER.map((s) => (
            <Button
              key={s}
              type="button"
              size="sm"
              variant={draft.surfaces.has(s) ? "default" : "outline"}
              className="size-8 p-0 text-xs coarse:size-11"
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
          placeholder="Note sur l'acte (facultative)"
          className="h-8 min-w-[8rem] flex-1 text-xs"
          disabled={disabled}
        />
      </div>

      {toothCount > 1 && (draft.surfaces.size > 0 || draft.note.trim() !== "") && (
        <p className="text-2xs text-muted-foreground">
          Les faces et la note s&apos;appliquent aux {toothCount} dents. Pour des faces différentes, enregistrez
          un acte par dent.
        </p>
      )}
    </>
  )
}

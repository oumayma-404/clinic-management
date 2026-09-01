"use client"

import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, SURFACE_ORDER } from "@/components/odontogram-conditions"
import type { SessionAct, SessionAction } from "@/components/record/use-session-acts"

// Sentinel for "no resulting condition" (Radix Select forbids an empty-string item value).
const NO_CONDITION = "__none__"

interface ActDetailFieldsProps {
  act: SessionAct
  dispatch: (action: SessionAction) => void
  disabled?: boolean
}

/**
 * Everything about ONE act beyond « quel acte, quelles dents, quel prix »: the état résultant that feeds the
 * odontogram, the MODVL faces, and a free note. Folded inside the act's own card, and summarised in the fold's
 * header so collapsing makes a value read-only rather than hidden.
 *
 * <p>⚠️ Two fields deliberately do NOT live here. The tarif and the `/dent ↔ forfait` switch sit on the card
 * face — folding the price away was a reported defect, because the dentist saw the figure they wanted to change
 * and could not reach it, so they lowered « Payé » instead, which means the patient still owes the difference.
 * The free-text désignation followed it out for the same reason: it is the act's *name*, the one thing the card
 * is identified by.</p>
 */
export function ActDetailFields({ act, dispatch, disabled }: ActDetailFieldsProps) {
  const conditionChip = conditionStyle(act.resultingCondition ?? "Sain")
  const toothCount = act.toothNumbers.length
  const patch = (p: Partial<SessionAct>) => dispatch({ type: "patchAct", key: act.key, patch: p })

  const toggleSurface = (code: string) => {
    const next = new Set(act.surfaces)
    if (next.has(code)) next.delete(code)
    else next.add(code)
    patch({ surfaces: next })
  }

  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <span className="shrink-0 text-2xs text-muted-foreground">État résultant</span>
        <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs">
          <span
            className={cn(
              "h-2.5 w-2.5 rounded-full border",
              act.resultingCondition ? conditionChip.swatch : "bg-background",
            )}
          />
          {act.resultingCondition ? conditionChip.label : "Aucun"}
        </span>
        <Select
          value={act.resultingCondition ?? NO_CONDITION}
          onValueChange={(v) => patch({ resultingCondition: v === NO_CONDITION ? null : v })}
          disabled={disabled}
        >
          <SelectTrigger className="h-8 w-44 text-xs" aria-label={`État résultant de ${act.procedureName}`}>
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
              variant={act.surfaces.has(s) ? "default" : "outline"}
              className="size-8 p-0 text-xs coarse:size-11"
              onClick={() => toggleSurface(s)}
              disabled={disabled}
              title={SURFACE_LABELS[s]}
              aria-pressed={act.surfaces.has(s)}
            >
              {s}
            </Button>
          ))}
        </div>
        <Input
          value={act.note}
          onChange={(e) => patch({ note: e.target.value })}
          placeholder="Note sur l'acte (facultative)"
          className="h-8 min-w-[8rem] flex-1 text-xs"
          disabled={disabled}
          aria-label={`Note sur ${act.procedureName}`}
        />
      </div>

      {toothCount > 1 && (act.surfaces.size > 0 || act.note.trim() !== "") && (
        <p className="text-2xs text-muted-foreground">
          Les faces et la note s&apos;appliquent aux {toothCount} dents. Pour des faces différentes, ajoutez
          un acte par dent.
        </p>
      )}
    </>
  )
}

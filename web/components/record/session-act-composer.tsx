"use client"

import { useMemo, useState } from "react"
import { Check, Plus, Search, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { formatDT } from "@/lib/format"
import { CONDITION_ORDER, conditionStyle, SURFACE_LABELS, SURFACE_ORDER } from "@/components/odontogram-conditions"
import type { ProcedureTypeDto } from "@/lib/api/types"
import {
  hasInvalidPrice,
  resolveActCost,
  type ActDraft,
  type SessionAction,
  type SessionAct,
} from "@/components/record/use-session-acts"

// Sentinel for "no resulting condition" (Radix Select forbids an empty-string item value).
const NO_CONDITION = "__none__"
// Bucket for procedures whose description isn't one of the seeded category names.
const OTHER_GROUP = "Autres"

interface SessionActComposerProps {
  draft: ActDraft
  selection: number[]
  procedureTypes: ProcedureTypeDto[]
  /** The committed act being edited, or null when composing a new one. */
  editingAct: SessionAct | null
  dispatch: (action: SessionAction) => void
  disabled?: boolean
}

/**
 * Records what was done to the currently selected teeth. The selection is the subject; this is the predicate.
 * Committing keeps the selection, so a second procedure on the same tooth is one more Entrée away.
 */
export function SessionActComposer({
  draft,
  selection,
  procedureTypes,
  editingAct,
  dispatch,
  disabled,
}: SessionActComposerProps) {
  const [pickerOpen, setPickerOpen] = useState(false)

  // `ProcedureType` has no Category column — the seed stores its category in the free-text `description`
  // (ProcedureTypeCatalogSeed passes it as the ctor's `description`). Group on it as a hint only, with an
  // "Autres" bucket for clinic-authored procedures that use the field as a real description.
  const grouped = useMemo(() => {
    const groups = new Map<string, ProcedureTypeDto[]>()
    for (const pt of procedureTypes) {
      const label = pt.description?.trim() || OTHER_GROUP
      const bucket = groups.get(label) ?? []
      bucket.push(pt)
      groups.set(label, bucket)
    }
    return Array.from(groups.entries()).sort((a, b) => {
      if (a[0] === OTHER_GROUP) return 1
      if (b[0] === OTHER_GROUP) return -1
      return a[0].localeCompare(b[0], "fr")
    })
  }, [procedureTypes])

  const toothCount = selection.length
  const total = resolveActCost(draft.unitCost, draft.perTooth, toothCount)
  const priceInvalid = hasInvalidPrice(draft.unitCost)
  const canCommit = !disabled && draft.procedureName.trim() !== "" && !priceInvalid
  const conditionChip = conditionStyle(draft.resultingCondition ?? "Sain")

  const commit = () => {
    if (!canCommit) return
    dispatch({ type: "commitDraft" })
  }

  const toggleSurface = (code: string) => {
    const next = new Set(draft.surfaces)
    if (next.has(code)) next.delete(code)
    else next.add(code)
    dispatch({ type: "patchDraft", patch: { surfaces: next } })
  }

  return (
    <div
      className={cn(
        "space-y-3 rounded-lg border p-3",
        editingAct ? "border-amber-400 bg-amber-50/50 dark:bg-amber-950/20" : "border-primary/60 bg-primary/[0.03]",
      )}
      // Enter commits, but never while the catalog list is open (it selects an item there instead).
      onKeyDown={(e) => {
        if (e.key === "Enter" && !pickerOpen) {
          e.preventDefault()
          commit()
        }
      }}
    >
      {/* Which teeth this act applies to */}
      <div className="flex flex-wrap items-center gap-1.5">
        {editingAct && (
          <Badge variant="outline" className="border-amber-500 text-[10px] text-amber-700 dark:text-amber-300">
            Modification
          </Badge>
        )}
        {toothCount > 0 ? (
          <>
            <span className="text-xs font-medium text-muted-foreground">Sélection :</span>
            {selection.map((t) => (
              <Badge key={t} variant="secondary" className="text-xs">
                {t}
              </Badge>
            ))}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-6 px-2 text-xs"
              onClick={() => dispatch({ type: "clearSelection" })}
              disabled={disabled}
            >
              Vider
            </Button>
          </>
        ) : (
          <span className="text-xs italic text-muted-foreground">
            Acte général (sans dent) — cliquez une dent sur le schéma pour le cibler
          </span>
        )}
      </div>

      {/* Procedure: free text + catalog picker */}
      <div className="space-y-1">
        <div className="flex items-center gap-2">
          <Input
            value={draft.procedureName}
            onChange={(e) => dispatch({ type: "patchDraft", patch: { procedureName: e.target.value } })}
            placeholder="Qu'avez-vous fait ? (ou choisir au catalogue)"
            disabled={disabled}
            autoFocus
          />
          <Popover open={pickerOpen} onOpenChange={setPickerOpen} modal>
            <PopoverTrigger asChild>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-9 shrink-0 px-3"
                disabled={disabled}
                title="Choisir un acte du catalogue"
              >
                <Search className="h-4 w-4" />
                <span className="sr-only">Choisir un acte du catalogue</span>
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-96 p-0" align="end">
              <Command>
                <CommandInput placeholder="Rechercher un acte…" />
                <CommandList>
                  <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
                  {grouped.map(([group, items]) => (
                    <CommandGroup key={group} heading={group}>
                      {items.map((pt) => (
                        <CommandItem
                          key={pt.id}
                          value={`${pt.name} ${group}`}
                          onSelect={() => {
                            dispatch({ type: "pickProcedure", procedure: pt })
                            setPickerOpen(false)
                          }}
                        >
                          <div className="flex w-full items-center justify-between gap-2">
                            <span className="text-sm">{pt.name}</span>
                            {pt.defaultCost != null && pt.defaultCost > 0 && (
                              <span className="shrink-0 text-xs text-muted-foreground">{formatDT(pt.defaultCost)}</span>
                            )}
                          </div>
                        </CommandItem>
                      ))}
                    </CommandGroup>
                  ))}
                </CommandList>
              </Command>
            </PopoverContent>
          </Popover>
        </div>
        {draft.procedureTypeId && (
          <Badge variant="secondary" className="gap-1 text-xs">
            Catalogue
            <button
              type="button"
              onClick={() => dispatch({ type: "detachProcedure" })}
              className="ml-1 rounded-full hover:text-destructive"
              title="Détacher du catalogue (texte libre)"
            >
              <X className="h-3 w-3" />
            </button>
          </Badge>
        )}
      </div>

      {/* Price: one editable number + the per-tooth / forfait switch, with the billed total always visible */}
      <div className="flex flex-wrap items-center gap-2">
        <Input
          type="number"
          min="0"
          step="0.001"
          value={draft.unitCost}
          onChange={(e) => dispatch({ type: "patchDraft", patch: { unitCost: e.target.value } })}
          className={cn("w-28", priceInvalid && "border-destructive")}
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
        <span className="text-xs text-muted-foreground">
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
        {!priceInvalid && draft.unitCost.trim() === "" && draft.procedureName.trim() !== "" && (
          <span className="text-xs text-amber-600 dark:text-amber-500">Sans tarif — à compléter plus tard</span>
        )}
      </div>

      {/* Resulting odontogram state */}
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs text-muted-foreground">État résultant</span>
        <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs">
          <span
            className={cn("h-2.5 w-2.5 rounded-full border", draft.resultingCondition ? conditionChip.swatch : "bg-background")}
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
      </div>

      {/* Surfaces + note */}
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs text-muted-foreground">Faces</span>
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
            >
              {s}
            </Button>
          ))}
        </div>
        <Input
          value={draft.note}
          onChange={(e) => dispatch({ type: "patchDraft", patch: { note: e.target.value } })}
          placeholder="Note (optionnel)"
          className="h-8 min-w-[8rem] flex-1 text-xs"
          disabled={disabled}
        />
      </div>
      {toothCount > 1 && (draft.surfaces.size > 0 || draft.note.trim() !== "") && (
        <p className="text-[11px] text-muted-foreground">
          Les faces et la note s'appliquent aux {toothCount} dents. Pour des faces différentes, enregistrez un acte
          par dent.
        </p>
      )}

      {/* Commit */}
      <div className="flex items-center gap-2">
        <Button type="button" size="sm" className="h-9 flex-1 gap-1.5" onClick={commit} disabled={!canCommit}>
          {editingAct ? <Check className="h-4 w-4" /> : <Plus className="h-4 w-4" />}
          {editingAct ? "Mettre à jour l'acte" : "Ajouter l'acte"}
          <kbd className="ml-1 rounded bg-primary-foreground/20 px-1 text-[10px] font-medium">Entrée</kbd>
        </Button>
        {editingAct && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-9"
            onClick={() => dispatch({ type: "cancelEdit" })}
            disabled={disabled}
          >
            Annuler la modification
          </Button>
        )}
      </div>
    </div>
  )
}

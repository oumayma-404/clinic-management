"use client"

import { useMemo } from "react"
import { Link2, Pencil, Trash2 } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { formatDT } from "@/lib/format"
import { conditionStyle } from "@/components/odontogram-conditions"
import { resolveActCost, type SessionAct } from "@/components/record/use-session-acts"

interface SessionActsListProps {
  acts: SessionAct[]
  editingKey: string | null
  onEdit: (key: string) => void
  onRemove: (key: string) => void
  disabled?: boolean
  /**
   * The act currently being composed, materialised with its selected teeth — **not yet confirmed**, but it *will*
   * be saved (the fiche folds the draft in, which is the whole point of the confirm-first flow).
   *
   * <p>It is shown because omitting it made this section say « Aucun acte pour cette séance » while the footer of
   * the same dialog showed its total — two statements about the same act that contradicted each other, with the
   * empty state instructing the dentist to do the thing they had just done.</p>
   */
  pendingAct?: SessionAct | null
}

/**
 * The session read back the way a dentist narrates it: grouped by tooth, then the mouth-level acts.
 * An act covering several teeth is listed under each of them — its fee is shown once (on its lowest tooth)
 * and marked « inclus » elsewhere, so a shared act never reads as billed twice.
 */
export function SessionActsList({
  acts,
  editingKey,
  onEdit,
  onRemove,
  disabled,
  pendingAct,
}: SessionActsListProps) {
  const toothGroups = useMemo(() => {
    const map = new Map<number, SessionAct[]>()
    for (const act of acts) {
      for (const tooth of act.toothNumbers) {
        const list = map.get(tooth) ?? []
        list.push(act)
        map.set(tooth, list)
      }
    }
    return Array.from(map.entries()).sort((a, b) => a[0] - b[0])
  }, [acts])

  const generalActs = useMemo(() => acts.filter((a) => a.toothNumbers.length === 0), [acts])

  // Soft duplicate hint: the same procedure charted on the same teeth more than once (a double Entrée).
  // Legitimate in principle, so it is flagged rather than blocked.
  const duplicateKeys = useMemo(() => {
    const seen = new Map<string, string>()
    const dupes = new Set<string>()
    for (const a of acts) {
      const signature = `${a.procedureName.trim().toLowerCase()}|${a.toothNumbers.join(",")}`
      const first = seen.get(signature)
      if (first) {
        dupes.add(first)
        dupes.add(a.key)
      } else {
        seen.set(signature, a.key)
      }
    }
    return dupes
  }, [acts])

  // Genuinely nothing yet — no confirmed act and nothing in hand.
  if (acts.length === 0 && !pendingAct) {
    return (
      <div className="rounded-lg border border-dashed p-6 text-center">
        <p className="text-sm font-medium text-foreground">Aucun acte pour cette séance</p>
        <p className="mt-1 text-xs text-muted-foreground">
          Cliquez une ou plusieurs dents sur le schéma, indiquez ce que vous avez fait, puis Entrée.
        </p>
      </div>
    )
  }

  const renderRow = (act: SessionAct, tooth: number | null) => {
    const isShared = act.toothNumbers.length > 1
    // The fee belongs to the act, not the tooth — print it on the lowest tooth only.
    const showCost = tooth === null || !isShared || tooth === act.toothNumbers[0]
    const cost = resolveActCost(act.unitCost, act.perTooth, act.toothNumbers.length)
    const style = conditionStyle(act.resultingCondition ?? "Sain")
    const isEditing = act.key === editingKey

    return (
      <li
        key={`${tooth ?? "general"}-${act.key}`}
        className={cn(
          "flex items-center gap-2 rounded-md border px-2 py-1.5 text-xs transition-colors",
          isEditing ? "border-amber-400 bg-amber-50/60 dark:bg-amber-950/20" : "hover:border-muted-foreground/40",
        )}
      >
        <span
          className={cn("h-2.5 w-2.5 shrink-0 rounded-full border", act.resultingCondition ? style.swatch : "bg-background")}
          title={act.resultingCondition ? style.label : "Aucun état résultant"}
        />
        <span className="min-w-0 flex-1 truncate font-medium text-foreground" title={act.procedureName}>
          {act.procedureName}
        </span>

        {isShared && (
          <span className="flex shrink-0 items-center gap-0.5 text-[10px] text-muted-foreground" title="Acte partagé entre plusieurs dents">
            <Link2 className="h-3 w-3" />
            {act.toothNumbers.join(", ")}
          </span>
        )}
        {act.surfaces.size > 0 && (
          <span className="shrink-0 text-[10px] text-muted-foreground">{Array.from(act.surfaces).join("")}</span>
        )}
        {duplicateKeys.has(act.key) && (
          <Badge variant="outline" className="shrink-0 border-amber-500 text-[9px] text-amber-700 dark:text-amber-300">
            en double ?
          </Badge>
        )}

        <span className="shrink-0 tabular-nums">
          {showCost ? (
            <>
              {formatDT(cost)}
              {act.perTooth && act.toothNumbers.length > 1 && (
                <span className="ml-1 text-[10px] text-muted-foreground">
                  ({formatDT(Number.parseFloat(act.unitCost) || 0)} / dent)
                </span>
              )}
            </>
          ) : (
            <span className="text-[10px] italic text-muted-foreground">inclus</span>
          )}
        </span>

        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="h-6 w-6 shrink-0"
          onClick={() => onEdit(act.key)}
          disabled={disabled}
          aria-label="Modifier l'acte"
          title="Modifier l'acte"
        >
          <Pencil className="h-3.5 w-3.5" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="h-6 w-6 shrink-0 hover:text-destructive"
          onClick={() => onRemove(act.key)}
          disabled={disabled}
          aria-label="Supprimer l'acte"
          title="Supprimer l'acte"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </Button>
      </li>
    )
  }

  return (
    <div className="space-y-3">
      {toothGroups.map(([tooth, toothActs]) => (
        <div key={tooth} className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-foreground">Dent {tooth}</span>
            <span className="h-px flex-1 bg-border" />
            <span className="text-[10px] text-muted-foreground">
              {toothActs.length} acte{toothActs.length > 1 ? "s" : ""}
            </span>
          </div>
          <ul className="space-y-1">{toothActs.map((act) => renderRow(act, tooth))}</ul>
        </div>
      ))}

      {generalActs.length > 0 && (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-foreground">Actes généraux</span>
            <span className="h-px flex-1 bg-border" />
            <span className="text-[10px] text-muted-foreground">sans dent</span>
          </div>
          <ul className="space-y-1">{generalActs.map((act) => renderRow(act, null))}</ul>
        </div>
      )}

      {/*
        The act in hand. Deliberately NOT rendered as one of the rows above: it carries no edit or remove control
        (it is already what the editor above is showing, and « supprimer » would mean clearing the composer, not
        deleting a saved act), and it says outright that it will be saved — because the reason this block exists is
        that its absence read as « Aucun acte » next to a non-zero total.
      */}
      {pendingAct && (
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-amber-700 dark:text-amber-300">En cours de saisie</span>
            <span className="h-px flex-1 bg-border" />
            <span className="text-[10px] text-muted-foreground">sera enregistré</span>
          </div>
          <div className="flex items-center gap-2 rounded-md border border-amber-400 bg-amber-50/60 px-2 py-1.5 text-xs dark:bg-amber-950/20">
            <span
              className={cn(
                "h-2.5 w-2.5 shrink-0 rounded-full border",
                pendingAct.resultingCondition
                  ? conditionStyle(pendingAct.resultingCondition).swatch
                  : "bg-background",
              )}
              title={
                pendingAct.resultingCondition
                  ? conditionStyle(pendingAct.resultingCondition).label
                  : "Aucun état résultant"
              }
            />
            <span className="min-w-0 flex-1 truncate font-medium text-foreground" title={pendingAct.procedureName}>
              {pendingAct.procedureName}
            </span>
            {pendingAct.toothNumbers.length > 0 ? (
              <span
                className="flex shrink-0 items-center gap-0.5 text-[10px] text-muted-foreground"
                title="Dents sélectionnées"
              >
                <Link2 className="h-3 w-3" />
                {pendingAct.toothNumbers.join(", ")}
              </span>
            ) : (
              <span className="shrink-0 text-[10px] italic text-muted-foreground">acte général</span>
            )}
            <span className="shrink-0 tabular-nums">
              {formatDT(
                resolveActCost(pendingAct.unitCost, pendingAct.perTooth, pendingAct.toothNumbers.length),
              )}
            </span>
          </div>
          <p className="text-[10px] text-muted-foreground">
            Cet acte est inclus dans le total et sera enregistré tel quel. « Ajouter un autre acte » le valide et
            libère la saisie pour le suivant.
          </p>
        </div>
      )}
    </div>
  )
}

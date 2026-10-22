"use client"

import { useEffect, useId, useRef, useState } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { ChevronDown, ChevronUp, Lock, Plus, RotateCcw, Trash2 } from "lucide-react"
import { toast } from "sonner"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  STAFF_QUICK_MESSAGE_LABEL_MAX,
  STAFF_QUICK_MESSAGES_MAX,
  staffChatApi,
  type StaffQuickMessageAudience,
  type StaffQuickMessageListDto,
} from "@/lib/api/staff-chat"
import { getErrorMessage } from "@/lib/errors"
import { quoteFr } from "@/lib/format"
import { useConflict } from "@/lib/hooks/use-conflict"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"

const AUDIENCE_LABEL: Record<StaffQuickMessageAudience, string> = {
  dentist: "Dentiste",
  desk: "Secrétariat",
  reply: "Réponse",
}

interface Row {
  /** Stable for React only; the message's own key is `key`, null until the server mints it. */
  id: string
  key: string | null
  label: string
  for: StaffQuickMessageAudience
  asksForPatient: boolean
  urgent: boolean
  builtIn: boolean
}

let rowSeq = 0
const nextRowId = () => `row-${++rowSeq}`

function rowsFrom(list: StaffQuickMessageListDto): Row[] {
  return [...list.items]
    .sort((a, b) => a.order - b.order)
    .map((item) => ({
      id: nextRowId(),
      key: item.key,
      label: item.label,
      for: item.for,
      asksForPatient: item.asksForPatient,
      urgent: item.urgent,
      builtIn: item.builtIn,
    }))
}

/** The first thing the server would refuse, worded as it would (FR-4) — so the banner appears before the round trip. */
function firstProblem(rows: Row[]): string | null {
  const seen = new Set<string>()
  for (const row of rows) {
    const label = row.label.trim()
    if (!label) return "Chaque message rapide doit avoir un texte."
    const folded = label.toLocaleLowerCase("fr")
    if (seen.has(folded)) return `${quoteFr(label)} figure deux fois dans la liste.`
    seen.add(folded)
  }
  return null
}

interface QuickMessagesEditorDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The list as the panel last read it; the editor opens on its cabinet order. */
  list: StaffQuickMessageListDto | undefined
}

/**
 * « Modifier les messages rapides » (US-6): anyone in the cabinet adds, renames, reorders and removes them. The
 * three built-ins stay, with their urgency (AC-6.2); a colleague's save in between is refused with « Recharger »
 * rather than overwritten (AC-6.5).
 */
export function QuickMessagesEditorDialog({ open, onOpenChange, list }: QuickMessagesEditorDialogProps) {
  const [rows, setRows] = useState<Row[]>([])
  const [version, setVersion] = useState(0)
  const [saving, setSaving] = useState(false)
  const [confirmReset, setConfirmReset] = useState(false)
  const [focusRowId, setFocusRowId] = useState<string | null>(null)
  const conflict = useConflict()
  const guard = useDirtyGuard(open, onOpenChange)
  const queryClient = useQueryClient()
  const wasOpen = useRef(false)
  const resetConflict = conflict.reset

  // Seeded on the closed → open edge only: a colleague's save arriving while the editor is open must not wipe the typing.
  useEffect(() => {
    if (open && !wasOpen.current && list) {
      setRows(rowsFrom(list))
      setVersion(list.version)
      resetConflict()
    }
    wasOpen.current = open && Boolean(list)
  }, [open, list, resetConflict])

  const update = (id: string, patch: Partial<Row>) =>
    setRows((previous) => previous.map((row) => (row.id === id ? { ...row, ...patch } : row)))

  const move = (index: number, delta: number) =>
    setRows((previous) => {
      const target = index + delta
      if (target < 0 || target >= previous.length) return previous
      const next = [...previous]
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })

  const add = () => {
    const id = nextRowId()
    setRows((previous) => [
      ...previous,
      { id, key: null, label: "", for: "desk", asksForPatient: false, urgent: false, builtIn: false },
    ])
    setFocusRowId(id)
  }

  const reload = async () => {
    conflict.clearMessage()
    try {
      const fresh = await queryClient.fetchQuery({
        queryKey: queryKeys.staffChatQuickMessages,
        queryFn: () => staffChatApi.quickMessages(),
        staleTime: 0,
        meta: realtimeMeta.staffChat,
      })
      setRows(rowsFrom(fresh))
      setVersion(fresh.version)
    } catch (err) {
      conflict.setError(getErrorMessage(err, "Les messages rapides n'ont pas pu être rechargés."))
    }
  }

  const save = async () => {
    const problem = firstProblem(rows)
    if (problem) {
      conflict.setError(problem)
      return
    }

    setSaving(true)
    conflict.clearMessage()
    try {
      const saved = await staffChatApi.saveQuickMessages(
        version,
        rows.map((row) => ({
          key: row.key,
          label: row.label.trim(),
          for: row.for,
          asksForPatient: row.asksForPatient,
          urgent: row.urgent,
        })),
      )
      queryClient.setQueryData(queryKeys.staffChatQuickMessages, saved)
      guard.markClean()
      onOpenChange(false)
      toast.success("Messages rapides enregistrés")
    } catch (err) {
      conflict.capture(err, "Les messages rapides n'ont pas pu être enregistrés.")
    } finally {
      setSaving(false)
    }
  }

  const reset = async () => {
    setSaving(true)
    try {
      const defaults = await staffChatApi.resetQuickMessages()
      queryClient.setQueryData(queryKeys.staffChatQuickMessages, defaults)
      setConfirmReset(false)
      guard.markClean()
      onOpenChange(false)
      toast.success("Messages par défaut rétablis")
    } catch (err) {
      setConfirmReset(false)
      conflict.setError(getErrorMessage(err, "Les messages par défaut n'ont pas pu être rétablis."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Messages rapides du cabinet</DialogTitle>
        </DialogHeader>

        <DialogBody className="space-y-3 px-1 py-1">
          <FormErrorBanner
            message={conflict.error}
            action={conflict.isConflict ? { label: "Recharger", onClick: () => void reload(), disabled: saving } : undefined}
          />

          <ol className="space-y-2" aria-label="Messages rapides">
            {rows.map((row, index) => (
              <QuickMessageRow
                key={row.id}
                row={row}
                index={index}
                last={index === rows.length - 1}
                autoFocus={row.id === focusRowId}
                onChange={(patch) => update(row.id, patch)}
                onMove={(delta) => move(index, delta)}
                onRemove={() => setRows((previous) => previous.filter((r) => r.id !== row.id))}
              />
            ))}
          </ol>

          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={add}
              disabled={rows.length >= STAFF_QUICK_MESSAGES_MAX || saving}
              className="coarse:min-h-11"
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              Ajouter un message
            </Button>
            <span className="text-2xs tabular-nums text-muted-foreground">
              {rows.length} / {STAFF_QUICK_MESSAGES_MAX}
            </span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setConfirmReset(true)}
              disabled={saving}
              className="ms-auto text-muted-foreground coarse:min-h-11"
            >
              <RotateCcw className="h-4 w-4" aria-hidden="true" />
              Rétablir les messages par défaut
            </Button>
          </div>
        </DialogBody>

        <DialogFooter>
          <Button variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={() => void save()} disabled={saving}>
            {saving ? "Enregistrement…" : "Enregistrer"}
          </Button>
        </DialogFooter>
      </DialogContent>
      <DiscardChangesDialog guard={guard} />

      <AlertDialog open={confirmReset} onOpenChange={(next) => !next && !saving && setConfirmReset(false)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Rétablir les 17 messages par défaut ?</AlertDialogTitle>
            <AlertDialogDescription>
              La liste du cabinet est remplacée pour tout le monde. Les messages déjà envoyés ne changent pas.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={saving}>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={saving}
              onClick={(event) => {
                event.preventDefault()
                void reset()
              }}
            >
              Rétablir
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Dialog>
  )
}

interface QuickMessageRowProps {
  row: Row
  index: number
  last: boolean
  autoFocus: boolean
  onChange: (patch: Partial<Row>) => void
  onMove: (delta: number) => void
  onRemove: () => void
}

function QuickMessageRow({ row, index, last, autoFocus, onChange, onMove, onRemove }: QuickMessageRowProps) {
  const id = useId()
  const name = row.label.trim() ? quoteFr(row.label.trim()) : `le message ${index + 1}`

  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-2">
      <div className="flex items-center gap-1">
        <Label htmlFor={`${id}-label`} className="sr-only">
          Texte du message {index + 1}
        </Label>
        <Input
          id={`${id}-label`}
          value={row.label}
          maxLength={STAFF_QUICK_MESSAGE_LABEL_MAX}
          autoFocus={autoFocus}
          onChange={(event) => onChange({ label: event.target.value })}
          className="h-9 min-w-0 flex-1"
        />
        <Button
          type="button"
          variant="ghost"
          size="icon"
          onClick={() => onMove(-1)}
          disabled={index === 0}
          aria-label={`Monter ${name}`}
          className="size-8 shrink-0 coarse:size-10"
        >
          <ChevronUp className="h-4 w-4" aria-hidden="true" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          onClick={() => onMove(1)}
          disabled={last}
          aria-label={`Descendre ${name}`}
          className="size-8 shrink-0 coarse:size-10"
        >
          <ChevronDown className="h-4 w-4" aria-hidden="true" />
        </Button>
        {row.builtIn ? (
          <span className="flex size-8 shrink-0 items-center justify-center text-muted-foreground coarse:size-10">
            <Lock className="h-4 w-4" aria-hidden="true" />
            <span className="sr-only">{name} ne peut pas être supprimé</span>
          </span>
        ) : (
          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={onRemove}
            aria-label={`Supprimer ${name}`}
            className="size-8 shrink-0 text-muted-foreground hover:text-destructive coarse:size-10"
          >
            <Trash2 className="h-4 w-4" aria-hidden="true" />
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="flex items-center gap-2">
          <Label htmlFor={`${id}-for`} className="text-xs text-muted-foreground">
            Pour
          </Label>
          <Select value={row.for} onValueChange={(value) => onChange({ for: value as StaffQuickMessageAudience })}>
            <SelectTrigger id={`${id}-for`} className="h-8 w-36 md:text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {(Object.keys(AUDIENCE_LABEL) as StaffQuickMessageAudience[]).map((audience) => (
                <SelectItem key={audience} value={audience}>
                  {AUDIENCE_LABEL[audience]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <label className="flex items-center gap-2 text-xs coarse:min-h-11">
          <Checkbox
            checked={row.asksForPatient}
            onCheckedChange={(checked) => onChange({ asksForPatient: checked === true })}
          />
          Demande un patient
        </label>
        <label className="flex items-center gap-2 text-xs coarse:min-h-11">
          <Checkbox
            checked={row.urgent}
            disabled={row.builtIn}
            onCheckedChange={(checked) => onChange({ urgent: checked === true })}
          />
          Urgent
        </label>
      </div>
    </li>
  )
}

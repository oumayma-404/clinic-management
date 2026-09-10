"use client"

import { useEffect, useMemo, useState } from "react"
import { ClipboardList, Loader2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { dentalRecordsApi, type BillableActLine } from "@/lib/api/dental-records"
import { formatDT, formatDateFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"

/**
 * « Reprendre des actes réalisés » — the patient's own recorded work, offered to the note d'honoraires editor.
 *
 * <h4>Why it exists</h4>
 * <p>The editor's per-line picker reads the <b>catalogue</b>: it answers « what does a détartrage cost? », not
 * « what did we do for this patient? ». So a fee note for work already carried out was retyped from the
 * patient's file in another tab, at whatever the tarif happens to be today rather than at what was charged.</p>
 *
 * <p>⚠️ <b>The figures are the SERVER's</b> (`GET …/dental-records/billable-lines`), not derived here.
 * `DentalRecordInvoiceLines` is the single authority on how recorded work becomes money — the per-tooth
 * provenance, the legacy fallback, the « (dents 16, 26) » designation — and every field it needs happens to be
 * on the wire already, which is exactly what would have made a copy of the rule in this file easy to write and
 * impossible to notice drifting. See the query's own summary.</p>
 *
 * <p>⚠️ <b>It appends; it never replaces.</b> Lines already typed are the user's, and a picker that clears them
 * would lose work visible on screen. Everything it adds stays editable, because a fee note legitimately departs
 * from what a séance recorded (a gesture commerciale, a rounding, a line merged).</p>
 *
 * <p>⚠️ <b>Nothing here writes anything.</b> This is a read plus a client-side selection; the fee note is a
 * printable `MedicalDocument` and the numbered fiscal note is still raised in Factures.</p>
 */
export interface BillableActsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Null closes the offer: with no patient chosen there is no record to read. */
  patientId: string | null
  /** The picked lines, in the order they are listed. */
  onConfirm: (lines: BillableActLine[]) => void
}

export function BillableActsDialog({ open, onOpenChange, patientId, onConfirm }: BillableActsDialogProps) {
  const [lines, setLines] = useState<BillableActLine[]>([])
  const [loading, setLoading] = useState(false)
  /** ⚠️ Distinct from `lines.length === 0`: « ce patient n'a aucun acte » and « je n'ai pas pu lire » are
   *  opposite facts, and on a money surface the reassuring one is the wrong default. */
  const [failed, setFailed] = useState(false)
  const [reload, setReload] = useState(0)
  const [picked, setPicked] = useState<Set<string>>(new Set())

  useEffect(() => {
    if (!open || !patientId) return

    let cancelled = false
    setLoading(true)
    setFailed(false)
    setPicked(new Set())
    ;(async () => {
      try {
        const read = await dentalRecordsApi.billableLines(patientId)
        if (cancelled) return
        setLines(read)
      } catch {
        if (cancelled) return
        setLines([])
        setFailed(true)
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [open, patientId, reload])

  const total = useMemo(
    () => lines.filter((line) => picked.has(line.key)).reduce((sum, line) => sum + line.quantity * line.unitPriceHt, 0),
    [lines, picked],
  )

  const toggle = (key: string, on: boolean) =>
    setPicked((prev) => {
      const next = new Set(prev)
      if (on) next.add(key)
      else next.delete(key)
      return next
    })

  const confirm = () => {
    onConfirm(lines.filter((line) => picked.has(line.key)))
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Reprendre des actes réalisés</DialogTitle>
          <DialogDescription>
            Les actes enregistrés dans les fiches de soins de ce patient, au montant porté sur la fiche. Ils
            restent modifiables une fois ajoutés.
          </DialogDescription>
        </DialogHeader>

        <DialogBody className="space-y-3">
          {loading ? (
            <div className="flex items-center justify-center gap-3 py-10 text-sm text-muted-foreground" role="status">
              <Loader2 className="h-5 w-5 animate-spin text-primary" aria-hidden="true" />
              Lecture des soins du patient…
            </div>
          ) : failed ? (
            <LoadFailureNotice
              message="Les soins enregistrés de ce patient n'ont pas pu être lus."
              detail="Aucun acte n'est proposé tant que la lecture échoue — ce patient en a peut-être."
              onRetry={() => setReload((n) => n + 1)}
            />
          ) : lines.length === 0 ? (
            <EmptyState
              size="compact"
              icon={ClipboardList}
              chipClassName={zoneChipClass(ZONES.clinical)}
              title="Aucun acte enregistré"
              description="Ce patient n'a encore aucune fiche de soins. Ajoutez les lignes à la main."
            />
          ) : (
            /* A list at every width, not a table: a row is a date, a name and a montant — nothing to compare
               down a column, so the two-tree hinge would buy nothing (frontend-web.md § 6). */
            <ul className="divide-y rounded-md border bg-card">
              {lines.map((line) => {
                const inputId = `billable-act-${line.key}`
                return (
                  <li key={line.key}>
                    {/* The whole row is the control: a 16 px box alone is under the touch floor, and the label
                        carries the text a thumb naturally aims at.
                        ⚠️ `items-center`, not `items-start`: `Checkbox` carries `.touch-target`, whose 44 px
                        pseudo-element is centred on the 16 px box — top-aligned in a 57 px two-line row it
                        measured 2 px into the row above (232 vs 230). Centred it sits inside its own row, which
                        is the § 2 rule for a control in a stack. */}
                    <label
                      htmlFor={inputId}
                      className="flex min-h-11 cursor-pointer items-center gap-3 px-3 py-2.5 hover:bg-accent/40"
                    >
                      <Checkbox
                        id={inputId}
                        className="shrink-0"
                        checked={picked.has(line.key)}
                        onCheckedChange={(state) => toggle(line.key, state === true)}
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">{line.designation}</span>
                        <span className="block text-xs text-muted-foreground">
                          {formatDateFr(line.interventionDate)}
                          {line.quantity > 1 ? ` · ${line.quantity} × ${formatDT(line.unitPriceHt)}` : ""}
                        </span>
                      </span>
                      <span className="shrink-0 text-sm tabular-nums">
                        {formatDT(line.quantity * line.unitPriceHt)}
                      </span>
                    </label>
                  </li>
                )
              })}
            </ul>
          )}
        </DialogBody>

        <DialogFooter className="sm:justify-between">
          {/* States what the button is about to add, so the total is checked before the lines land in the
              document rather than after. `me-auto` keeps it left of the actions when the footer is a row. */}
          <p className="me-auto text-sm text-muted-foreground" role="status">
            {picked.size === 0
              ? "Aucun acte sélectionné."
              : `${picked.size} acte${picked.size > 1 ? "s" : ""} · ${formatDT(total)}`}
          </p>
          <div className="flex gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} className="coarse:h-11">
              Annuler
            </Button>
            <Button type="button" onClick={confirm} disabled={picked.size === 0} className="coarse:h-11">
              Ajouter{picked.size > 0 ? ` (${picked.size})` : ""}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

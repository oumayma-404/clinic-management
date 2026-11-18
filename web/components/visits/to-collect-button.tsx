"use client"

import { useState } from "react"
import { format } from "date-fns"
import { Wallet } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import type { SeanceToCollectDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { useSeanceCollection } from "./use-seance-collection"

/** What the row says about the money, in the desk's words. */
function rowDetail(row: SeanceToCollectDto): string {
  switch (row.kind) {
    case "NoteBalance":
      return row.invoiceNumber ? `reste sur la note n° ${row.invoiceNumber}` : "reste sur la note"
    case "DraftNote":
      return "note en brouillon, à émettre"
    case "Treatment":
      return row.treatmentPlanNumber ? `échéance du devis n° ${row.treatmentPlanNumber}` : "échéance du traitement"
    default:
      return "à facturer"
  }
}

/**
 * **« À encaisser · N »** on the agenda — the secretary's way in (`seance-au-fauteuil`). The dentist saves the
 * fiche with « Payé » empty; the séance appears here with the amount the server computed (the fiche's total, or the
 * note's balance), and « Encaisser » takes it through the dialogs that already exist.
 *
 * <p>The rows come from `useSeancesToCollect` — the page's one live read, which the agenda blocks share. Renders
 * nothing while « Mode discret » hides money, nothing when nothing is owed, and nothing when the read failed (the
 * hook hands `null` for both).</p>
 */
export function ToCollectButton({ rows, onSettled }: { rows: SeanceToCollectDto[] | null; onSettled: () => void }) {
  const [open, setOpen] = useState(false)
  const collection = useSeanceCollection(onSettled)
  const count = rows?.length ?? 0

  return (
    <>
      {count > 0 && (
        // Its own row above the agenda, rendered only when something is owed: no phantom band on an ordinary day.
        <div className="mb-3 flex flex-shrink-0 items-center justify-end">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setOpen(true)}
          className="border-warning/40 bg-warning-wash text-warning-ink hover:bg-warning-wash/80 coarse:min-h-11"
        >
          <Wallet className="size-4" aria-hidden="true" />
          À encaisser · {count}
        </Button>
        </div>
      )}
      <Dialog open={open && count > 0} onOpenChange={setOpen}>
        <DialogContent mobile="sheet" className="md:max-w-lg">
          <DialogHeader>
            <DialogTitle>À encaisser aujourd&apos;hui</DialogTitle>
            <DialogDescription className="sr-only">
              Les séances dont la fiche est enregistrée et l&apos;argent pas encore pris.
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <ul className="divide-y">
              {(rows ?? []).map((row) => (
                <li key={row.appointmentId} className="flex flex-wrap items-center gap-x-3 gap-y-2 py-3">
                  <div className="min-w-0 flex-1 basis-48">
                    <p className="truncate font-medium">{row.patientName || "Patient"}</p>
                    <p className="text-xs text-muted-foreground">
                      {format(new Date(row.startUtc), "HH:mm")} · {rowDetail(row)}
                      {row.practitionerName ? ` · ${row.practitionerName}` : ""}
                    </p>
                  </div>
                  <span className="font-semibold tabular-nums">{formatDT(row.amountDue)}</span>
                  <Button
                    type="button"
                    size="sm"
                    variant={row.kind === "DraftNote" ? "outline" : "default"}
                    disabled={collection.opening !== null}
                    onClick={() => {
                      // The list closes first: a dialog opened over another dialog leaves two focus traps.
                      setOpen(false)
                      void collection.collect(row)
                    }}
                    className="coarse:min-h-11"
                    aria-label={`${row.kind === "DraftNote" ? "Ouvrir les factures de" : "Encaisser"} ${row.patientName}`}
                  >
                    {row.kind === "DraftNote" ? "Ouvrir les factures" : "Encaisser"}
                  </Button>
                </li>
              ))}
            </ul>
          </DialogBody>
        </DialogContent>
      </Dialog>
      {collection.dialogs}
    </>
  )
}

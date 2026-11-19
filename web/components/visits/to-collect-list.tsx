"use client"

import { format } from "date-fns"
import { Banknote } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import type { SeanceToCollectDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { useSeanceCollection } from "./use-seance-collection"

/** What the row says about the money, in the desk's words. */
function rowDetail(row: SeanceToCollectDto): string {
  switch (row.kind) {
    case "NoteBalance":
      return row.invoiceNumber ? `reste sur la note n° ${row.invoiceNumber}` : "reste sur la note"
    // The fiche is billed and its draft replaced — the dialog says so; for the desk it is simply « à facturer ».
    case "DraftNote":
      return "à facturer"
    case "Treatment":
      return row.treatmentPlanNumber ? `échéance du devis n° ${row.treatmentPlanNumber}` : "échéance du traitement"
    default:
      return "à facturer"
  }
}

/**
 * **« À encaisser »** — the tab of « À clôturer » where the desk takes the day's money (`seance-au-fauteuil`). The
 * dentist saves the fiche with « Payé » empty; the séance appears here with the amount the server computed (the
 * fiche's total, or the note's balance), and « Encaisser » takes it through the dialogs that already exist.
 *
 * <p>⚠️ Not on the agenda: the owner kept the agenda as it was (2026-11-19). The rows come from the page's one
 * `useSeancesToCollect` read, which also feeds the tab's count. `rows === null` is loading or a failed read — never
 * « rien à encaisser ».</p>
 */
export function ToCollectList({
  rows,
  failed,
  onSettled,
}: {
  rows: SeanceToCollectDto[] | null
  failed: boolean
  onSettled: () => void
}) {
  const collection = useSeanceCollection(onSettled)

  if (failed) {
    return (
      <LoadFailureNotice
        message="Impossible de lire les séances à encaisser."
        detail="Aucun paiement n'a été enregistré."
        onRetry={onSettled}
      />
    )
  }

  return (
    <>
      <div className="rounded-md border bg-card">
        {rows === null ? (
          <div className="space-y-2 p-3" role="status" aria-label="Chargement">
            {[0, 1, 2].map((i) => (
              <div key={i} className="h-14 animate-pulse rounded-md bg-muted" />
            ))}
          </div>
        ) : rows.length === 0 ? (
          <div className="p-4">
            <EmptyState size="compact" icon={Banknote} title="Rien à encaisser aujourd'hui" />
          </div>
        ) : (
          <ul className="divide-y" aria-label="Séances à encaisser aujourd'hui">
            {rows.map((row) => (
              <li key={row.appointmentId} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3">
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
                  disabled={collection.opening !== null}
                  onClick={() => void collection.collect(row)}
                  className="coarse:min-h-11"
                  aria-label={`Encaisser ${row.patientName}`}
                >
                  Encaisser
                </Button>
              </li>
            ))}
          </ul>
        )}
      </div>
      {collection.dialogs}
    </>
  )
}

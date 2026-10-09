import type { InvoiceDto } from "@/lib/api/types"

/** What a note d'honoraires has collected and still awaits — served by the invoice read, never summed here. */
export interface BilledNoteFigures {
  collected: number
  outstanding: number
}

/**
 * A billed fiche's « Payé » / « Reste », read from its NOTE rather than from the fiche.
 *
 * <p>⚠️ « Encaisser » (the « Reste à payer » section, `/factures`, a devis) pays the note and never touches the
 * fiche's own `amountPaid`, so every screen reading `cost − amountPaid` kept « reste 40,000 DT » on a note already
 * settled. The server's own top-up rule knows this (`DentalRecordBillingGuard.Check`); the history did not.</p>
 *
 * <p>Three answers per fiche, and a reader must keep them apart:</p>
 * <ul>
 *   <li><b>figures</b> — the note bills this fiche alone, so its collected / outstanding ARE this fiche's;</li>
 *   <li><b>null</b> — it bills more than this fiche (several fiches, or a devis), so no per-fiche reste exists:
 *   printing the note's figure on each of its fiches would make a reader add it up twice;</li>
 *   <li><b>absent</b> — not billed, or only by a draft (which takes no payment): the fiche's figures stand.</li>
 * </ul>
 */
export function noteFiguresByFiche(invoices: InvoiceDto[]): Map<string, BilledNoteFigures | null> {
  const figures = new Map<string, BilledNoteFigures | null>()
  for (const inv of invoices) {
    if (inv.status === "Cancelled" || inv.status === "Draft") continue
    const billed = new Set<string>()
    if (inv.dentalRecordId) billed.add(inv.dentalRecordId)
    for (const line of inv.lines ?? []) {
      if (line.dentalRecordId) billed.add(line.dentalRecordId)
    }
    const alone = billed.size === 1 && !inv.treatmentPlanId
    for (const recordId of billed) {
      if (figures.has(recordId)) continue
      figures.set(recordId, alone ? { collected: inv.amountCollected, outstanding: inv.outstanding } : null)
    }
  }
  return figures
}

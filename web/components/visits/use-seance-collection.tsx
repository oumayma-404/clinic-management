"use client"

import { useCallback, useState, type ReactNode } from "react"
import { toast } from "sonner"
import { BillDentalRecordDialog } from "@/components/factures/bill-dental-record-dialog"
import { PaymentModal } from "@/components/factures/payment-modal"
import { InstallmentPaymentModal } from "@/components/treatment-plans/installment-payment-modal"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { invoicesApi } from "@/lib/api/invoices"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { DentalRecordDto, InstallmentDto, InvoiceDto, SeanceToCollectDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"

/**
 * **The one « Encaisser » for a séance** (`seance-au-fauteuil`) — the dentist entered the acts, the desk takes the
 * money. A séance with no note opens « Facturer cette intervention » (issues the note and records the payment in one
 * step); a note with a balance opens the ordinary « Enregistrer un paiement » on it; a séance whose note is still a
 * DRAFT opens the same « Facturer cette intervention » on its fiche, which replaces the draft (the fiche makes the
 * note — owner's review, 2026-11-19). A devis séance
 * opens « Enregistrer un paiement » on the échéance due, tagged with the fiche so the dentist's re-save of that fiche
 * does not take the money a second time.
 *
 * <p>⚠️ It re-reads the fiche or the note before opening, never trusting the list row: the row is a figure of the
 * moment, and the dentist may have changed an act since.</p>
 */
export function useSeanceCollection(onSettled: () => void): {
  collect: (row: SeanceToCollectDto) => Promise<void>
  /** « Encaisser » on a séance's fiche, replacing its draft note when one is named; `key` feeds `opening`. */
  billFiche: (
    patientId: string,
    dentalRecordId: string | null | undefined,
    patientName: string,
    key: string,
    draftInvoiceId?: string | null,
  ) => Promise<void>
  opening: string | null
  dialogs: ReactNode
} {
  const [billing, setBilling] = useState<{
    record: DentalRecordDto
    patientName: string
    replacesDraft: { invoiceId: string; total: number } | null
  } | null>(null)
  const [paying, setPaying] = useState<InvoiceDto | null>(null)
  const [payingInstallment, setPayingInstallment] = useState<
    { planId: string; installment: InstallmentDto; dentalRecordId: string } | null
  >(null)
  /** The appointment whose dialog is being opened, so its button can say so. */
  const [opening, setOpening] = useState<string | null>(null)

  /*
   * « Encaisser » on a séance's FICHE — the one path for « à facturer » and for a séance whose note is still a draft.
   * The fiche makes the note: a named draft (no number, no money) is replaced by it, server-side, in the same
   * transaction — and only after the dialog has said so. Both documents are re-read first, never trusted from the row.
   */
  const billFiche = useCallback(
    async (
      patientId: string,
      dentalRecordId: string | null | undefined,
      patientName: string,
      key: string,
      draftInvoiceId?: string | null,
    ) => {
      setOpening(key)
      try {
        const record = dentalRecordId
          ? (await dentalRecordsApi.list(patientId)).find((r) => r.id === dentalRecordId)
          : undefined
        if (!record) {
          toast.error("Cette fiche de soins n'existe plus.")
          onSettled()
          return
        }
        let replacesDraft: { invoiceId: string; total: number } | null = null
        if (draftInvoiceId) {
          const draft = await invoicesApi.get(draftInvoiceId)
          if (draft.status !== "Draft") {
            // Issued in the meantime: the list shows what is left on it, and nothing is replaced.
            toast.info(draft.number ? `La note n° ${draft.number} a été émise entre-temps.` : "La note a changé entre-temps.")
            onSettled()
            return
          }
          replacesDraft = { invoiceId: draft.id, total: draft.totalTtc }
        }
        setBilling({ record, patientName, replacesDraft })
      } catch (err) {
        showErrorToast(err, "Impossible d'ouvrir l'encaissement.")
        onSettled()
      } finally {
        setOpening(null)
      }
    },
    [onSettled],
  )

  const collect = useCallback(
    async (row: SeanceToCollectDto) => {
      if (row.kind === "Unbilled" || row.kind === "DraftNote") {
        await billFiche(
          row.patientId,
          row.dentalRecordId,
          row.patientName,
          row.appointmentId,
          row.kind === "DraftNote" ? row.invoiceId : null,
        )
        return
      }
      setOpening(row.appointmentId)
      try {
        if (row.kind === "NoteBalance" && row.invoiceId) {
          setPaying(await invoicesApi.get(row.invoiceId))
          return
        }
        if (row.kind === "Treatment" && row.treatmentPlanId) {
          // Re-picked from the FRESH devis, in the server's order (due date, then id) — `useDebtLineCollection`'s rule.
          const plan = await treatmentPlansApi.get(row.treatmentPlanId)
          const target = [...plan.installments]
            .filter((i) => !i.isPaid && i.outstanding > 0)
            .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.id.localeCompare(b.id))[0]
          if (!target) {
            toast.info("Cet échéancier ne peut plus recevoir de paiement. Ouvrez le devis pour le compléter.")
            onSettled()
            return
          }
          setPayingInstallment({ planId: plan.id, installment: target, dentalRecordId: row.dentalRecordId })
          return
        }
      } catch (err) {
        showErrorToast(err, "Impossible d'ouvrir l'encaissement.")
      } finally {
        setOpening(null)
      }
    },
    [billFiche, onSettled],
  )

  const dialogs = (
    <>
      <BillDentalRecordDialog
        record={billing?.record ?? null}
        patientName={billing?.patientName ?? ""}
        replacesDraft={billing?.replacesDraft ?? null}
        onOpenChange={(open) => {
          if (!open) setBilling(null)
        }}
        onSuccess={() => {
          setBilling(null)
          onSettled()
        }}
      />
      <PaymentModal
        open={paying !== null}
        onOpenChange={(open) => {
          if (!open) setPaying(null)
        }}
        invoice={paying}
        onSuccess={() => {
          setPaying(null)
          onSettled()
        }}
      />
      <InstallmentPaymentModal
        open={payingInstallment !== null}
        onOpenChange={(open) => {
          if (!open) setPayingInstallment(null)
        }}
        planId={payingInstallment?.planId ?? null}
        installment={payingInstallment?.installment ?? null}
        dentalRecordId={payingInstallment?.dentalRecordId ?? null}
        onSuccess={() => {
          setPayingInstallment(null)
          onSettled()
        }}
      />
    </>
  )

  return { collect, billFiche, opening, dialogs }
}

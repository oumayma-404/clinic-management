"use client"

import { useCallback, useState, type ReactNode } from "react"
import { useRouter } from "next/navigation"
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
 * step); a note with a balance opens the ordinary « Enregistrer un paiement » on it; a DRAFT note sends the desk to
 * the patient's « Factures », where it is issued — billing the séance again would raise a second note. A devis séance
 * opens « Enregistrer un paiement » on the échéance due, tagged with the fiche so the dentist's re-save of that fiche
 * does not take the money a second time.
 *
 * <p>⚠️ It re-reads the fiche or the note before opening, never trusting the list row: the row is a figure of the
 * moment, and the dentist may have changed an act since.</p>
 */
export function useSeanceCollection(onSettled: () => void): {
  collect: (row: SeanceToCollectDto) => Promise<void>
  opening: string | null
  dialogs: ReactNode
} {
  const router = useRouter()
  const [billing, setBilling] = useState<{ record: DentalRecordDto; patientName: string } | null>(null)
  const [paying, setPaying] = useState<InvoiceDto | null>(null)
  const [payingInstallment, setPayingInstallment] = useState<
    { planId: string; installment: InstallmentDto; dentalRecordId: string } | null
  >(null)
  /** The appointment whose dialog is being opened, so its button can say so. */
  const [opening, setOpening] = useState<string | null>(null)

  const collect = useCallback(
    async (row: SeanceToCollectDto) => {
      if (row.kind === "DraftNote") {
        router.push(`/patients/${encodeURIComponent(row.patientId)}?tab=invoices`)
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
        const record = (await dentalRecordsApi.list(row.patientId)).find((r) => r.id === row.dentalRecordId)
        if (!record) {
          toast.error("Cette fiche de soins n'existe plus.")
          onSettled()
          return
        }
        setBilling({ record, patientName: row.patientName })
      } catch (err) {
        showErrorToast(err, "Impossible d'ouvrir l'encaissement.")
      } finally {
        setOpening(null)
      }
    },
    [router, onSettled],
  )

  const dialogs = (
    <>
      <BillDentalRecordDialog
        record={billing?.record ?? null}
        patientName={billing?.patientName ?? ""}
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

  return { collect, opening, dialogs }
}

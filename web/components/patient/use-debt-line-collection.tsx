"use client"

import { useState } from "react"
import { toast } from "sonner"
import { PaymentModal } from "@/components/factures/payment-modal"
import { InstallmentPaymentModal } from "@/components/treatment-plans/installment-payment-modal"
import { invoicesApi } from "@/lib/api/invoices"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { InstallmentDto, InvoiceDto, PatientDebtLineDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"

/**
 * « Encaisser » on a debt line, shared by the patient file and the worklist — the document is re-read before the
 * dialog opens (a stale « reste » is refused), and the dialogs are `/factures`' own. Mount `dialogs` at page level.
 */
export function useDebtLineCollection(onChanged: () => void) {
  const [paymentInvoice, setPaymentInvoice] = useState<InvoiceDto | null>(null)
  const [paymentInstallment, setPaymentInstallment] =
    useState<{ planId: string; installment: InstallmentDto } | null>(null)
  const [busyDocumentId, setBusyDocumentId] = useState<string | null>(null)

  const collectInvoice = async (line: PatientDebtLineDto) => {
    setBusyDocumentId(line.documentId)
    try {
      setPaymentInvoice(await invoicesApi.get(line.documentId))
    } catch (err) {
      showErrorToast(err, "La note d'honoraires n'a pas pu être ouverte.")
      onChanged()
    } finally {
      setBusyDocumentId(null)
    }
  }

  const collectInstallment = async (line: PatientDebtLineDto) => {
    setBusyDocumentId(line.documentId)
    try {
      const plan = await treatmentPlansApi.get(line.documentId)
      // Re-picked from the FRESH plan, ordered as the server's projection is (due date, then id).
      const target = [...plan.installments]
        .filter((i) => !i.isPaid && i.outstanding > 0)
        .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.id.localeCompare(b.id))[0]
      if (!target) {
        toast.info("Cet échéancier ne peut plus recevoir de paiement. Ouvrez le devis pour le compléter.")
        onChanged()
        return
      }
      setPaymentInstallment({ planId: plan.id, installment: target })
    } catch (err) {
      showErrorToast(err, "Le devis n'a pas pu être ouvert.")
      onChanged()
    } finally {
      setBusyDocumentId(null)
    }
  }

  const dialogs = (
    <>
      <PaymentModal
        open={paymentInvoice !== null}
        onOpenChange={(open) => { if (!open) setPaymentInvoice(null) }}
        invoice={paymentInvoice}
        onSuccess={onChanged}
      />
      <InstallmentPaymentModal
        open={paymentInstallment !== null}
        onOpenChange={(open) => { if (!open) setPaymentInstallment(null) }}
        planId={paymentInstallment?.planId ?? null}
        installment={paymentInstallment?.installment ?? null}
        onSuccess={onChanged}
      />
    </>
  )

  return { busyDocumentId, collectInvoice, collectInstallment, dialogs }
}

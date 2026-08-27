"use client"

import { useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { ApiError } from "@/lib/api/client"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { formatDT, formatDateFr } from "@/lib/format"
import type { InstallmentPaymentDto } from "@/lib/api/types"

/**
 * Annuler un encaissement d'échéance — the échéancier's half of a correction the invoice track has had all along.
 *
 * <p><b>The gap this closes.</b> `POST /treatment-plans/{id}/installments/{i}/payments/{p}/void` existed, was
 * tested, was reachable from `treatmentPlansApi`, and <b>no screen called it</b>. So a mis-keyed installment
 * payment — 4 000 typed for 400 — was permanent: it sat in la caisse, in the patient's solde and on the dashboard
 * with no way to take it back, while the identical mistake on an invoice payment was two clicks from being
 * corrected. Money you cannot correct is the money people stop trusting first.</p>
 *
 * <p><b>An in-place panel, not a nested dialog</b> — `invoice-detail-modal`'s idiom, followed deliberately:
 * nothing in this app nests Radix dialogs, and the plan workspace is itself frequently opened over one. It also
 * keeps the row being voided on screen beside its confirmation, which a dialog covering the table cannot.</p>
 *
 * <p><b>The motif is required</b>, exactly as on the invoice side: a void is a correction that survives in the
 * ledger for ever, and « pourquoi ? » is the whole of what makes it evidence rather than an erasure. The row is
 * kept and struck through, and a reprinted receipt carries « REÇU ANNULÉ ».</p>
 */
export interface VoidInstallmentPaymentProps {
  planId: string
  installmentId: string
  /** Due date of the échéance the payment sits on — the row's own identity on this screen. */
  installmentDueDate: string
  payment: InstallmentPaymentDto
  onCancel: () => void
  /** Called after the void committed; the parent refetches the plan. */
  onVoided: () => void
}

export function VoidInstallmentPayment({
  planId,
  installmentId,
  installmentDueDate,
  payment,
  onCancel,
  onVoided,
}: VoidInstallmentPaymentProps) {
  const [reason, setReason] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const confirm = async () => {
    if (!reason.trim()) {
      // Checked here as well as server-side: a required field whose only enforcement is a round trip reads as a
      // random failure.
      setError("Le motif est requis.")
      return
    }

    try {
      setBusy(true)
      setError(null)
      await treatmentPlansApi.voidInstallmentPayment(planId, installmentId, payment.id, reason.trim())
      toast.success("Encaissement annulé")
      onVoided()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de l'annulation de l'encaissement.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3 rounded-md border border-destructive/40 bg-destructive/5 p-3">
      <p className="text-sm">
        Annuler cet encaissement de <span className="font-semibold">{formatDT(payment.amount)}</span> du{" "}
        {formatDateFr(payment.paidOn)}, sur l&apos;échéance du {formatDateFr(installmentDueDate)} ?
        L&apos;encaissement sera retiré du devis et de la caisse, à sa date d&apos;origine. Cette action est
        définitive.
      </p>
      {/* The one thing the user cannot infer, and the reason a receipt already in the patient's hands matters. */}
      <p className="text-sm text-muted-foreground">
        Si un reçu a déjà été remis au patient, récupérez-le : sa réimpression portera la mention « REÇU ANNULÉ ».
      </p>

      <div className="space-y-1">
        {/* The id carries the payment's, so two panels can never share a `for` — the échéancier renders one row
            per payment and a stale label would focus the wrong textarea. */}
        <Label htmlFor={`void-installment-reason-${payment.id}`} className="text-sm">
          Motif <span className="text-destructive">*</span>
        </Label>
        <Textarea
          id={`void-installment-reason-${payment.id}`}
          rows={2}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="Ex. erreur de saisie du montant"
          disabled={busy}
        />
      </div>

      <FormErrorBanner message={error} />

      {/* `flex-col-reverse` below `sm:`: full-width targets stacked primary-first on a phone, the desktop reading
          order (retour → confirmer) preserved in the DOM. */}
      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button variant="outline" size="sm" disabled={busy} onClick={onCancel} className="w-full sm:w-auto">
          Retour
        </Button>
        <Button
          size="sm"
          className="w-full bg-destructive text-destructive-foreground hover:bg-destructive/90 sm:w-auto"
          disabled={busy}
          onClick={confirm}
        >
          {busy ? "Annulation…" : "Annuler l'encaissement"}
        </Button>
      </div>
    </div>
  )
}

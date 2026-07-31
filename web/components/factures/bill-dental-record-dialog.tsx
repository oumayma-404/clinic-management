"use client"

import { useEffect, useState } from "react"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { AlertTriangle, Loader2, Receipt } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { getErrorMessage } from "@/lib/errors"
import { formatDT, formatDate, todayLocalIso } from "@/lib/format"
import type { DentalRecordDto } from "@/lib/api/types"
import { PAYMENT_METHODS, paymentMethodLabel } from "./invoice-labels"

/**
 * « Facturer cette intervention » — raise the note d'honoraires for a fiche de soins and, optionally, record the
 * cash the patient handed over at the end of the session.
 *
 * <p>It replaces a prefilled `InvoiceFormModal`, and the replacement is the point. That flow produced a *draft*,
 * so the money the dentist had already been given still had to be collected in a second, separate action nobody
 * was prompted to take — which is how `DentalRecord.AmountPaid` came to be a field shaped like a receipt that no
 * money read has ever touched.</p>
 *
 * <p>It also deliberately shows **no editable line table**. The per-tooth pricing rule (quantity × unit price vs.
 * one flat fee) used to be computed here, in the browser, to seed that table — a second authority over how
 * recorded work becomes money. It now lives server-side in `DentalRecordInvoiceLines`, so this dialog states the
 * total and the acts and lets the server price them. Editing lines before billing is still possible: raise the
 * note from « Factures » instead.</p>
 */

interface BillDentalRecordDialogProps {
  /** The fiche to bill; null closes the dialog. */
  record: DentalRecordDto | null
  patientName: string
  onOpenChange: (open: boolean) => void
  onSuccess?: () => void
}

export function BillDentalRecordDialog({
  record,
  patientName,
  onOpenChange,
  onSuccess,
}: BillDentalRecordDialogProps) {
  const [collectNow, setCollectNow] = useState(true)
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  const [paidOn, setPaidOn] = useState(todayLocalIso())
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!record) return
    setCollectNow(true)
    // Pre-filled with the full fee — the common case is the patient settling the session in full. It stays
    // editable for a part-payment.
    setAmount(record.cost > 0 ? String(record.cost) : "")
    setMethod("Cash")
    // The session's own date, not today: a fiche recorded two days late was paid on the day it happened, and
    // booking that cash to today puts it in the wrong day's caisse.
    setPaidOn((record.interventionDate ?? "").slice(0, 10) || todayLocalIso())
    setError(null)
  }, [record])

  const handleSubmit = async () => {
    if (!record) return
    setError(null)

    let paidNow: { amount: number; method: string; paidOn: string } | null = null
    if (collectNow) {
      const parsed = Number(amount)
      if (!Number.isFinite(parsed) || parsed <= 0) {
        setError("Saisissez un montant encaissé supérieur à 0, ou décochez « Encaisser maintenant ».")
        return
      }
      paidNow = { amount: parsed, method, paidOn }
    }

    try {
      setSubmitting(true)
      const invoice = await invoicesApi.createFromDentalRecord(record.id, paidNow)
      toast.success(
        paidNow
          ? `Note n° ${invoice.number} émise — ${formatDT(paidNow.amount)} encaissé`
          : `Note n° ${invoice.number} émise`,
      )
      onOpenChange(false)
      onSuccess?.()
    } catch (err) {
      // The dialog stays open with the input intact — the amount is the one thing the user must not retype.
      setError(getErrorMessage(err, "Échec de la facturation de cette fiche de soins."))
    } finally {
      setSubmitting(false)
    }
  }

  const acts = record?.acts ?? []

  return (
    <Dialog open={!!record} onOpenChange={(open) => { if (!open && !submitting) onOpenChange(false) }}>
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Receipt className="h-5 w-5" />
            Facturer cette intervention
          </DialogTitle>
          <DialogDescription>
            {patientName}
            {record ? ` — séance du ${formatDate(record.interventionDate)}` : ""}
          </DialogDescription>
        </DialogHeader>

        {error && <FormErrorBanner message={error} />}

        <div className="space-y-4">
          {/* What is being billed. Read-only: the server prices the lines (per-tooth acts bill as quantity ×
              unit price), so showing an editable table here would be a second pricing authority. */}
          <div className="space-y-2 rounded-md border bg-muted/30 p-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium">Actes de la séance</span>
              <span className="text-base font-semibold">{formatDT(record?.cost ?? 0)}</span>
            </div>
            {acts.length > 0 ? (
              <ul className="space-y-1 text-sm text-muted-foreground">
                {acts.map((act) => (
                  <li key={act.id} className="flex items-center justify-between gap-2">
                    <span>
                      {act.procedureName}
                      {act.toothNumbers && act.toothNumbers.length > 0
                        ? ` (dents ${act.toothNumbers.join(", ")})`
                        : ""}
                    </span>
                    <span className="whitespace-nowrap">{formatDT(act.cost)}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">{record?.procedureType}</p>
            )}
          </div>

          {/* The irreversibility has to be stated before the click, not discovered after it. */}
          <div className="flex gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 dark:border-amber-800 dark:bg-amber-950/20">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
            <p className="text-xs text-amber-800 dark:text-amber-300">
              La note d&apos;honoraires est <strong>émise immédiatement</strong> et reçoit un numéro définitif.
              Une erreur de montant se corrige ensuite par un <strong>avoir</strong>, pas par une modification.
            </p>
          </div>

          <div className="flex items-center gap-2">
            <input
              id="collectNow"
              type="checkbox"
              className="h-4 w-4 rounded border-input"
              checked={collectNow}
              onChange={(e) => setCollectNow(e.target.checked)}
              disabled={submitting}
            />
            <Label htmlFor="collectNow" className="cursor-pointer text-sm font-medium">
              Encaisser maintenant
            </Label>
            {!collectNow && (
              <Badge variant="outline" className="ml-auto text-xs">
                Le patient paiera plus tard
              </Badge>
            )}
          </div>

          {collectNow && (
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="space-y-1.5">
                <Label htmlFor="billAmount" className="text-sm">Montant encaissé</Label>
                <Input
                  id="billAmount"
                  type="number"
                  step="0.001"
                  min="0"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  disabled={submitting}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="billMethod" className="text-sm">Mode</Label>
                <Select value={method} onValueChange={setMethod} disabled={submitting}>
                  <SelectTrigger id="billMethod">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PAYMENT_METHODS.map((m) => (
                      <SelectItem key={m} value={m}>{paymentMethodLabel(m)}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="billPaidOn" className="text-sm">Date</Label>
                <Input
                  id="billPaidOn"
                  type="date"
                  value={paidOn}
                  onChange={(e) => setPaidOn(e.target.value)}
                  disabled={submitting}
                />
              </div>
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={submitting}>
            Annuler
          </Button>
          <Button onClick={handleSubmit} disabled={submitting}>
            {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {collectNow ? "Facturer et encaisser" : "Facturer sans encaisser"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

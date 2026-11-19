"use client"

import { useEffect, useState } from "react"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Checkbox } from "@/components/ui/checkbox"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Loader2, Receipt } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { getErrorMessage } from "@/lib/errors"
import { formatAmount, formatDT, formatDate, parseAmountInput, todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import type { DentalRecordDto } from "@/lib/api/types"
import { PAYMENT_METHODS, paymentMethodLabel } from "./invoice-labels"
import { CHEQUE_METHOD, ChequeFields, EMPTY_CHEQUE_FIELDS, chequePaymentFields, type ChequeFieldsValue } from "./cheque-fields"

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
  /**
   * The séance's draft note this bill replaces (`seance-au-fauteuil`): the fiche makes the note, and the server
   * deletes that draft — no number, no money — in the same transaction. Stated in the dialog before the press.
   */
  replacesDraft?: { invoiceId: string; total: number } | null
  onOpenChange: (open: boolean) => void
  onSuccess?: () => void
}

export function BillDentalRecordDialog({
  record,
  patientName,
  replacesDraft = null,
  onOpenChange,
  onSuccess,
}: BillDentalRecordDialogProps) {
  const [collectNow, setCollectNow] = useState(true)
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<string>("Cash")
  // The desk takes cheques as often as cash; without these fields a cheque landed with no number, no bank and no
  // date — absent from « Chèques à encaisser » (`seance-au-fauteuil`).
  const [cheque, setCheque] = useState<ChequeFieldsValue>(EMPTY_CHEQUE_FIELDS)
  const [paidOn, setPaidOn] = useState(todayLocalIso())
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /*
   * Money being entered is not discarded by a stray tap (J9). This dialog is the sharpest case of the six: it
   * is the *last* step of a session, the amount has already been handed over in cash, and re-opening it means
   * re-reading the acts to work out what was owed.
   *
   * ⚠️ The root is open-driven by `record`, not by an `open` boolean, so the guard is fed `!!record`.
   */
  const guard = useDirtyGuard(!!record, (next) => { if (!next) onOpenChange(false) })

  useEffect(() => {
    if (!record) return
    setCollectNow(true)
    // Pre-filled with the full fee — the common case is the patient settling the session in full. It stays
    // editable for a part-payment.
    setAmount(record.cost > 0 ? formatAmount(record.cost) : "")
    setMethod("Cash")
    setCheque(EMPTY_CHEQUE_FIELDS)
    // The session's own date, not today: a fiche recorded two days late was paid on the day it happened, and
    // booking that cash to today puts it in the wrong day's caisse.
    setPaidOn((record.interventionDate ?? "").slice(0, 10) || todayLocalIso())
    setError(null)
  }, [record])

  const handleSubmit = async () => {
    if (!record) return
    setError(null)

    let paidNow: Parameters<typeof invoicesApi.createFromDentalRecord>[1] = null
    if (collectNow) {
      const parsed = parseAmountInput(amount)
      if (!Number.isFinite(parsed) || parsed <= 0) {
        setError("Saisissez un montant encaissé supérieur à 0, ou décochez « Encaisser maintenant ».")
        return
      }
      // The one cheque builder: it clears the three fields unless the method is a cheque.
      paidNow = { amount: parsed, method, paidOn, ...chequePaymentFields(method, cheque) }
    }

    try {
      setSubmitting(true)
      const invoice = await invoicesApi.createFromDentalRecord(record.id, paidNow, replacesDraft?.invoiceId)
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
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (§ 5). */}
    <Dialog open={!!record} onOpenChange={(open) => { if (!open && !submitting) guard.onOpenChange(false) }}>
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

          {/* No « émise immédiatement » warning here: removed on the owner's request (2026-11-19). */}

          {replacesDraft && (
            <p role="note" className="text-sm text-muted-foreground">
              Remplace le brouillon de note de {formatDT(replacesDraft.total)} préparé depuis le rendez-vous.
            </p>
          )}

          {/*
            The `Checkbox` primitive, not a raw `<input type="checkbox">`. `globals.css` deliberately EXCLUDES
            checkboxes from the 44 px coarse-pointer floor because `ui/checkbox.tsx` carries `touch-target`
            itself — so a hand-rolled one gets neither, and lands at 16 × 16 px under a gloved finger. This is
            the control that decides whether the cash the patient just handed over reaches la caisse at all.
          */}
          <div className="flex items-center gap-2">
            <Checkbox
              id="collectNow"
              checked={collectNow}
              onCheckedChange={(checked) => setCollectNow(checked === true)}
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
                {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma
                    this product prints with, and a rejected keystroke returns an EMPTY value — so the amount
                    looked typed and the submit sent nothing. The numeric keypad still appears. */}
                <Input
                  id="billAmount"
                  type="text"
                  inputMode="decimal"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  disabled={submitting}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="billMethod" className="text-sm">Mode</Label>
                <Select value={method} onValueChange={setMethod} disabled={submitting}>
                  {/* `w-full`: `ui/select.tsx` ships `w-fit`, so without this the trigger renders narrower than
                      the two `Input`s beside it in the same three-column grid. */}
                  <SelectTrigger id="billMethod" className="w-full">
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
          {collectNow && method === CHEQUE_METHOD && (
            <ChequeFields idPrefix="bill-record" value={cheque} onChange={setCheque} disabled={submitting} />
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => guard.onOpenChange(false)} disabled={submitting}>
            Annuler
          </Button>
          <Button onClick={handleSubmit} disabled={submitting}>
            {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {collectNow ? "Facturer et encaisser" : "Facturer sans encaisser"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}

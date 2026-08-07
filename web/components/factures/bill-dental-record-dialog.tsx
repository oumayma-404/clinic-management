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
import { AlertTriangle, Loader2, Receipt } from "lucide-react"
import { toast } from "sonner"
import { invoicesApi } from "@/lib/api/invoices"
import { getErrorMessage } from "@/lib/errors"
import { formatAmount, formatDT, formatDate, parseAmountInput, todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
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
      const parsed = parseAmountInput(amount)
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

          {/* The irreversibility has to be stated before the click, not discovered after it.
              On the theme's warning family — `--warning-wash` with `--warning-ink` — rather than an `amber-50 /
              dark:amber-950` pair maintained by hand. `text-warning-ink` and not `text-warning`: the plain step
              measures ~3.5:1 on its own wash, and this is the one paragraph in the flow that must be read. */}
          <div className="flex gap-2 rounded-md border border-warning/30 bg-warning-wash p-3">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning-ink" aria-hidden="true" />
            <p className="text-xs text-warning-ink">
              La note d&apos;honoraires est <strong>émise immédiatement</strong> et reçoit un numéro définitif.
              Une erreur de montant se corrige ensuite par un <strong>avoir</strong>, pas par une modification.
            </p>
          </div>

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

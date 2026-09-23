"use client"

import { useCallback, useRef, useState } from "react"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { PAYMENT_METHODS, type PaymentMethod } from "@/components/caisse/expense-fields"
import { ApiError, ApiErrorCode } from "@/lib/api/client"

/** How money is given back. Never a cheque — the server refuses one. */
export type RefundMethod = Exclude<PaymentMethod, "Cheque">
const REFUND_METHODS = PAYMENT_METHODS.filter((m) => m.value !== "Cheque")

/** The « Rendu en » field — shared by the confirm below and the stop dialog's own refund branch. */
export function RefundMethodField({
  id,
  value,
  onChange,
}: {
  id: string
  value: RefundMethod
  onChange: (method: RefundMethod) => void
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>Rendu en</Label>
      <Select value={value} onValueChange={(v) => onChange(v as RefundMethod)}>
        <SelectTrigger id={id} className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {REFUND_METHODS.map((m) => (
            <SelectItem key={m.value} value={m.value}>
              {m.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

/** Returned by `withRefund` when the dentist pressed « Retour »: nothing was saved, and nothing should be announced. */
export const REFUND_DECLINED = Symbol("refund-declined")

/**
 * « Rendre au patient » (G3). A devis lowered below what it already collected is refused with
 * `plan-total-below-collected`; this asks once, then re-sends with the chosen method. The server's sentence
 * names the amount, so it is shown as is.
 */
export function usePlanRefundConfirm() {
  const [message, setMessage] = useState<string | null>(null)
  const [method, setMethod] = useState<RefundMethod>("Cash")
  const resolver = useRef<((method: RefundMethod | null) => void) | null>(null)

  const close = (chosen: RefundMethod | null) => {
    resolver.current?.(chosen)
    resolver.current = null
    setMessage(null)
  }

  const withRefund = useCallback(
    async <T,>(attempt: (refundMethod?: RefundMethod) => Promise<T>): Promise<T | typeof REFUND_DECLINED> => {
      try {
        return await attempt()
      } catch (err) {
        if (!(err instanceof ApiError && err.code === ApiErrorCode.PlanTotalBelowCollected)) throw err
        setMethod("Cash")
        const chosen = await new Promise<RefundMethod | null>((resolve) => {
          resolver.current = resolve
          setMessage(err.message)
        })
        if (!chosen) return REFUND_DECLINED
        return attempt(chosen)
      }
    },
    [],
  )

  const refundDialog = (
    <AlertDialog open={message != null} onOpenChange={(open) => { if (!open) close(null) }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Rendre au patient ?</AlertDialogTitle>
          <AlertDialogDescription>{message} Le rendu est enregistré aujourd&apos;hui.</AlertDialogDescription>
        </AlertDialogHeader>
        <RefundMethodField id="plan-refund-method" value={method} onChange={setMethod} />
        <AlertDialogFooter>
          <AlertDialogCancel>Retour</AlertDialogCancel>
          <AlertDialogAction onClick={() => close(method)}>Rendre et enregistrer</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )

  return { withRefund, refundDialog }
}

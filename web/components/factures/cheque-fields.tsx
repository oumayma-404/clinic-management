"use client"

import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

/**
 * The three fields that identify a cheque — number, bank, due date (L8) — shared by every surface that records a
 * payment.
 *
 * <p><b>Why shared and not inlined twice.</b> An invoice payment and an échéance payment both take a cheque, and so
 * does the fiche de soins' « Facturer cette intervention ». Three copies of a conditional sub-form is how one of
 * them ends up without the « seulement pour un chèque » gate and sends a cheque number on a cash payment — which
 * the server then refuses, in a dialog whose user did nothing wrong. The gate lives in
 * {@link chequePaymentFields}, once.</p>
 *
 * <p><b>Why the fields are optional.</b> Reception could previously record a cheque with one field, and refusing
 * money that was genuinely received in order to enforce a form field is the wrong trade. The consequence is handled
 * where it belongs: a cheque with no due date cannot be sorted into a « à encaisser » date, so the cheques-due view
 * counts it as its own group rather than dropping it silently.</p>
 */

/** The stored `PaymentMethod` value that unlocks these fields. Never compared as a bare literal at a call site. */
export const CHEQUE_METHOD = "Cheque"

export interface ChequeFieldsValue {
  number: string
  bankName: string
  /** `YYYY-MM-DD`, as a native date input holds it. */
  dueDate: string
}

export const EMPTY_CHEQUE_FIELDS: ChequeFieldsValue = { number: "", bankName: "", dueDate: "" }

/**
 * The cheque part of a payment request body — or `undefined` for every field when the method is not a cheque.
 *
 * <p>⚠️ **The method check is here, not at the call sites.** The server refuses cheque details on a non-cheque
 * payment (one rule, in `ChequeDetails.For`), so a form that keeps a typed cheque number after the user switches
 * the method back to « Espèces » would submit a request the server rightly rejects. Clearing it at the one place
 * that builds the payload makes that unreachable, and leaves the typed values on screen in case they switch back.</p>
 */
export function chequePaymentFields(method: string, value: ChequeFieldsValue) {
  if (method !== CHEQUE_METHOD) {
    return { chequeNumber: undefined, chequeBankName: undefined, chequeDueDate: undefined }
  }

  return {
    chequeNumber: value.number.trim() || undefined,
    chequeBankName: value.bankName.trim() || undefined,
    // A date input gives a bare `YYYY-MM-DD`; sent as-is because the due date is a **calendar day** about a paper
    // document. ⚠️ Never `new Date(dueDate).toISOString()` — that converts to UTC first, so for the Tunisian
    // offset a cheque due on the 1st would be stored as the previous month's last day.
    chequeDueDate: value.dueDate || undefined,
  }
}

interface ChequeFieldsProps {
  /** Distinguishes the `id`/`htmlFor` pairs when two dialogs can be mounted at once. */
  idPrefix: string
  value: ChequeFieldsValue
  onChange: (value: ChequeFieldsValue) => void
  disabled?: boolean
}

/**
 * Renders the three inputs. The caller decides *whether* to render it (only for a cheque) — this component does not
 * self-hide, so a caller cannot mount it and silently show nothing.
 */
export function ChequeFields({ idPrefix, value, onChange, disabled }: ChequeFieldsProps) {
  return (
    <div className="space-y-3 rounded-lg border bg-muted/40 p-3">
      <p className="text-sm font-medium">Détails du chèque</p>

      {/* `sm:grid-cols-2` — never an ungated `grid-cols-2`, which is two columns at 320 px. */}
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor={`${idPrefix}-cheque-number`}>N° de chèque</Label>
          <Input
            id={`${idPrefix}-cheque-number`}
            value={value.number}
            onChange={(e) => onChange({ ...value, number: e.target.value })}
            disabled={disabled}
            placeholder="Ex. 4512873"
            // `inputMode` rather than `type="number"`: a cheque number is an identifier, not a quantity — it can
            // carry leading zeros a number input would eat, and spinner arrows on it are meaningless.
            inputMode="numeric"
            autoComplete="off"
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${idPrefix}-cheque-bank`}>Banque</Label>
          <Input
            id={`${idPrefix}-cheque-bank`}
            value={value.bankName}
            onChange={(e) => onChange({ ...value, bankName: e.target.value })}
            disabled={disabled}
            placeholder="Ex. BIAT"
            autoComplete="off"
          />
        </div>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor={`${idPrefix}-cheque-due`}>Encaissable le</Label>
        <Input
          id={`${idPrefix}-cheque-due`}
          type="date"
          value={value.dueDate}
          onChange={(e) => onChange({ ...value, dueDate: e.target.value })}
          disabled={disabled}
          className="w-auto"
        />
        {/* States the one thing a user cannot infer: the money is in the till today, the cheque clears later. That
            distinction is the entire reason the field exists. */}
        <p className="text-sm text-muted-foreground">
          Pour un chèque post-daté. Le paiement est enregistré à sa date ci-dessus ; cette date sert à suivre les
          chèques à encaisser.
        </p>
      </div>
    </div>
  )
}

"use client"

import type React from "react"
import { useState, useEffect } from "react"
import { Dialog, DialogBody, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Trash2, Plus, Lock } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi, type TreatmentPlanInstallmentInput } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { isPlanBilled } from "./plan-next-action"
import { formatAmount, formatDateFr, formatDT, parseAmountInput, todayLocalIso } from "@/lib/format"
import { installmentDueInputValue, installmentDueLabel } from "./treatment-plan-labels"

interface Row {
  /** The existing échéance this row revises; null for a row the user just added. */
  id: string | null
  dueDate: string
  amount: string
  /** Cash already collected. > 0 makes the row locked against deletion and against being lowered. */
  amountPaid: number
  /**
   * This row is the lump-sum ledger container `TreatmentPlan.Accept` raises when no schedule was given — not
   * an échéance anybody agreed.
   *
   * <p>⚠️ <b>Its `dueDate` was pre-filled into an editable `type="date"`, which is the half of M25 that
   * WRITES.</b> Every other surface stopped printing that fabricated instant; here re-saving the form turned
   * it into a date the dentist appears to have typed, and the row then reads as a real échéance for ever
   * afterwards. The field opens <b>empty</b>, labelled for what the row is; leaving it empty sends the stored
   * value back unchanged, and typing one is how a dentist deliberately turns the balance into a schedule.</p>
   */
  isAutoRaised: boolean
  /** What to send when an auto-raised row is left blank — see {@link Row.isAutoRaised}. */
  storedDueDate: string
  /** The last live payment's date, or null. An auto-raised row has no agreed date but a collected one has THIS. */
  lastPaidOn: string | null
}

interface ReviseInstallmentsModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  plan: TreatmentPlanDto
  onSuccess?: () => void
}

/**
 * Re-spread an accepted devis's échéancier without touching its acts — `PUT /treatment-plans/{id}/installments`
 * (AC-P2.5). The endpoint has been fully implemented and validated since `treatment-plan-workspace` and had
 * **no caller**: a patient who could no longer pay on the agreed dates had to have their devis cancelled and
 * retyped, losing its number.
 *
 * The server owns every rule; this dialog's job is to state them *before* submit (AC-P2.6) rather than let the
 * user discover them as a refusal — which rows are locked, why, and what the schedule must add up to.
 */
export function ReviseInstallmentsModal({ open, onOpenChange, plan, onSuccess }: ReviseInstallmentsModalProps) {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const guard = useDirtyGuard(open, onOpenChange)

  useEffect(() => {
    if (!open) return
    setRows(
      plan.installments.map((inst) => ({
        id: inst.id,
        dueDate: installmentDueInputValue(inst),
        amount: formatAmount(inst.amount),
        amountPaid: inst.amountPaid,
        isAutoRaised: inst.isAutoRaised === true,
        storedDueDate: inst.dueDate.slice(0, 10),
        lastPaidOn: inst.lastPaidOn,
      })),
    )
    setError(null)
  }, [open, plan])

  const updateRow = (index: number, patch: Partial<Row>) =>
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const addRow = () =>
    setRows((prev) => [
      ...prev,
      {
        id: null,
        dueDate: todayLocalIso(),
        amount: "",
        amountPaid: 0,
        isAutoRaised: false,
        storedDueDate: "",
        lastPaidOn: null,
      },
    ])

  const removeRow = (index: number) => setRows((prev) => prev.filter((_, i) => i !== index))

  const sum = rows.reduce((acc, r) => {
    const amt = parseAmountInput(r.amount)
    return Number.isFinite(amt) ? acc + amt : acc
  }, 0)

  // The server requires the schedule to equal the plan's planned total exactly (to the millime).
  const matchesTotal = Math.abs(sum - plan.totalPlanned) < 0.0005

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (rows.length === 0) {
      setError("L'échéancier ne peut pas être vide sur un devis accepté.")
      return
    }

    for (const row of rows) {
      // An auto-raised row is legitimately dateless — leaving it blank keeps it the balance it already is.
      if (!row.dueDate && !row.isAutoRaised) {
        setError("Chaque échéance doit avoir une date.")
        return
      }
      const amount = parseAmountInput(row.amount)
      if (!Number.isFinite(amount) || amount <= 0) {
        setError("Le montant de l'échéance doit être supérieur à 0.")
        return
      }
      if (row.amountPaid > 0 && amount < row.amountPaid - 0.0005) {
        setError(
          // The row named as the échéancier names it — it used to print a raw `2026-03-14`, and invented a
          // date for the auto-raised row.
          `${installmentDueLabel(row)} : déjà encaissé ${formatDT(row.amountPaid)} — son montant ne peut pas `
            + "être ramené en dessous.",
        )
        return
      }
    }

    // A paid échéance dropped from the list would erase that cash from the plan's balance; the server refuses
    // it, so say which row and why instead of forwarding a generic sentence.
    const droppedPaid = plan.installments.find(
      (inst) => inst.amountPaid > 0 && !rows.some((r) => r.id === inst.id),
    )
    if (droppedPaid) {
      setError(
        "Une échéance déjà encaissée ne peut pas être supprimée de l'échéancier. Conservez-la et ajustez les autres.",
      )
      return
    }

    if (!matchesTotal) {
      setError(
        `Le total des échéances (${formatDT(sum)}) doit être égal au coût total planifié du devis (${formatDT(plan.totalPlanned)}).`,
      )
      return
    }

    const payload: TreatmentPlanInstallmentInput[] = rows.map((r) => ({
      id: r.id,
      // A blank auto-raised row sends its stored instant back untouched: nothing moves, and the row stays the
      // ledger container it was. A typed date is a deliberate schedule and replaces it.
      dueDate: `${r.dueDate || r.storedDueDate}T00:00:00`,
      amount: parseAmountInput(r.amount),
    }))

    setLoading(true)
    try {
      // ⚠️ `plan.version` — this call rewrites the WHOLE échéancier and had no concurrency token at all.
      await treatmentPlansApi.reviseInstallments(plan.id, payload, plan.version)
      toast.success("Échéancier modifié")
      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Échec de la modification de l'échéancier.")
    } finally {
      setLoading(false)
    }
  }

  const lockedCount = rows.filter((r) => r.amountPaid > 0).length

  return (
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (AC-23). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-h-[90dvh] md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Modifier l&apos;échéancier</DialogTitle>
          {/*
            ⚠️ « ce que le patient doit » is false on a billed devis — the note collects, and an encaissement
            entered here reaches neither la caisse nor les recettes. « Modifier le devis » warns properly and
            this dialog said nothing, so a dentist hunting for *where do I take the money* landed here and
            re-split échéances that collect nothing.
          */}
          <DialogDescription>
            Re-répartissez l&apos;échéancier du devis sans toucher aux actes. Le devis garde son numéro
            {plan.number ? ` (${plan.number})` : ""} et passe en révision {plan.revisionNumber + 1}.
            {isPlanBilled(plan) && (
              <>
                {" "}⚠️ Ce devis est facturé sur la note{" "}
                {plan.linkedInvoiceNumber ?? "d'honoraires"} : l&apos;encaissement se fait sur cette note, et
                ces échéances n&apos;encaissent rien.
              </>
            )}
          </DialogDescription>
        </DialogHeader>

        {/* The form owns the remaining height so `DialogBody` scrolls and the footer stays on screen (AC-21). */}
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col gap-4">
          <DialogBody className="space-y-4">
          <FormErrorBanner message={error} />

          {lockedCount > 0 && (
            <p className="flex items-start gap-2 rounded-md border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
              <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <span>
                {lockedCount === 1
                  ? "Une échéance a déjà encaissé de l'argent : elle peut être re-datée et augmentée, mais ni supprimée ni ramenée en dessous du montant encaissé."
                  : `${lockedCount} échéances ont déjà encaissé de l'argent : elles peuvent être re-datées et augmentées, mais ni supprimées ni ramenées en dessous du montant encaissé.`}
              </span>
            </p>
          )}

          <div className="space-y-2">
            <Label>Échéances</Label>
            {rows.length === 0 && (
              <p className="text-sm text-muted-foreground">
                Aucune échéance. Un devis accepté doit en compter au moins une.
              </p>
            )}
            {rows.map((row, index) => {
              const collected = row.amountPaid > 0
              return (
                <div key={row.id ?? `new-${index}`} className="space-y-1">
                  {/*
                    ⚠️ **`flex-wrap` + a real `basis-*`, and both are load-bearing at 320 px.** The row was
                    `flex items-end gap-2` with no wrap: a `flex-1` `type="date"` (~120 px intrinsic minimum)
                    beside a `w-36` amount and a 40 px bin cannot reach its floor in the ~90 px left, so the
                    dialog scrolled sideways. `basis-40` lets the date take a line of its own and the amount
                    drop below it. `min-w-0` on both, since `Input` does not shrink past its intrinsic width
                    on its own.
                  */}
                  <div className="flex flex-wrap items-end gap-2">
                    <div className="min-w-0 flex-1 basis-40 space-y-1">
                      {index === 0 && <span className="text-xs text-muted-foreground">Échéance</span>}
                      <Input
                        type="date"
                        value={row.dueDate}
                        onChange={(e) => updateRow(index, { dueDate: e.target.value })}
                        disabled={loading}
                        aria-label={
                          row.isAutoRaised && !row.dueDate
                            ? "Solde à régler — aucune échéance convenue ; saisissez une date pour en fixer une"
                            : "Date de l'échéance"
                        }
                      />
                    </div>
                    <div className="min-w-0 flex-1 basis-28 space-y-1 sm:max-w-36">
                      {index === 0 && <span className="text-xs text-muted-foreground">Montant (DT)</span>}
                      {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the
                          comma this product prints with, and a rejected keystroke returns an EMPTY value. The
                          `min` it also drops was never the real guard — the server refuses an échéance below what
                          it has collected, and this modal states that rule in prose above. */}
                      <Input
                        type="text"
                        inputMode="decimal"
                        value={row.amount}
                        onChange={(e) => updateRow(index, { amount: e.target.value })}
                        disabled={loading}
                      />
                    </div>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      onClick={() => removeRow(index)}
                      disabled={loading || collected}
                      aria-label="Supprimer l'échéance"
                      title={
                        collected
                          ? "Échéance déjà encaissée — elle ne peut pas être supprimée."
                          : "Supprimer l'échéance"
                      }
                    >
                      {collected ? <Lock className="h-4 w-4" /> : <Trash2 className="h-4 w-4" />}
                    </Button>
                  </div>
                  {/* The hint lives here, not inside the date cell: a taller cell knocked the two columns
                      out of line under `items-end`. And an auto-raised row that has COLLECTED is not dateless —
                      the day is `lastPaidOn`, stated rather than pre-filled (pre-filling is the half of M25
                      that writes an agreed date nobody typed). */}
                  {collected ? (
                    <p className="text-xs text-muted-foreground">
                      Déjà encaissé : {formatDT(row.amountPaid)}
                      {row.lastPaidOn ? ` — encaissée le ${formatDateFr(row.lastPaidOn)}` : ""}
                      {row.isAutoRaised && !row.dueDate && " · aucune date convenue"}
                    </p>
                  ) : (
                    row.isAutoRaised &&
                    !row.dueDate && (
                      <p className="text-xs text-muted-foreground">
                        Solde à régler — aucune date convenue. Laissez vide pour le garder tel quel.
                      </p>
                    )
                  )}
                </div>
              )
            })}
            <Button type="button" variant="outline" size="sm" onClick={addRow} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter une échéance
            </Button>
          </div>

          <div className="flex justify-end text-sm">
            {/* `text-warning-ink`, not `amber-600` + a hand-written `dark:` twin. The token flips with the
                palette on its own, and `--warning-ink` is the step chosen for legibility as small text —
                `--warning` itself sits at L 0.62 and is under the contrast floor at this size. */}
            <span className={matchesTotal ? "text-muted-foreground" : "text-warning-ink"}>
              Total des échéances : {formatDT(sum)} / {formatDT(plan.totalPlanned)}
              {!matchesTotal && " — les deux doivent être égaux."}
            </span>
          </div>
          </DialogBody>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement…" : "Enregistrer la révision"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}

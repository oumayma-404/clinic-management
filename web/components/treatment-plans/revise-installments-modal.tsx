"use client"

import type React from "react"
import { useState, useEffect } from "react"
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Trash2, Plus, Lock } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi, type TreatmentPlanInstallmentInput } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatDT, todayLocalIso } from "@/lib/format"

interface Row {
  /** The existing échéance this row revises; null for a row the user just added. */
  id: string | null
  dueDate: string
  amount: string
  /** Cash already collected. > 0 makes the row locked against deletion and against being lowered. */
  amountPaid: number
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

  useEffect(() => {
    if (!open) return
    setRows(
      plan.installments.map((inst) => ({
        id: inst.id,
        dueDate: inst.dueDate.slice(0, 10),
        amount: String(inst.amount),
        amountPaid: inst.amountPaid,
      })),
    )
    setError(null)
  }, [open, plan])

  const updateRow = (index: number, patch: Partial<Row>) =>
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const addRow = () =>
    setRows((prev) => [
      ...prev,
      { id: null, dueDate: todayLocalIso(), amount: "", amountPaid: 0 },
    ])

  const removeRow = (index: number) => setRows((prev) => prev.filter((_, i) => i !== index))

  const sum = rows.reduce((acc, r) => {
    const amt = Number(r.amount)
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
      if (!row.dueDate) {
        setError("Chaque échéance doit avoir une date.")
        return
      }
      const amount = Number(row.amount)
      if (!Number.isFinite(amount) || amount <= 0) {
        setError("Le montant de l'échéance doit être supérieur à 0.")
        return
      }
      if (row.amountPaid > 0 && amount < row.amountPaid - 0.0005) {
        setError(
          `L'échéance du ${row.dueDate} a déjà encaissé ${formatDT(row.amountPaid)} — son montant ne peut pas être ramené en dessous.`,
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
      dueDate: `${r.dueDate}T00:00:00`,
      amount: Number(r.amount),
    }))

    setLoading(true)
    try {
      await treatmentPlansApi.reviseInstallments(plan.id, payload)
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
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Modifier l&apos;échéancier</DialogTitle>
          <DialogDescription>
            Re-répartissez ce que le patient doit sans toucher aux actes. Le devis garde son numéro
            {plan.number ? ` (${plan.number})` : ""} et passe en révision {plan.revisionNumber + 1}.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
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
                  <div className="flex items-end gap-2">
                    <div className="flex-1 space-y-1">
                      {index === 0 && <span className="text-xs text-muted-foreground">Échéance</span>}
                      <Input
                        type="date"
                        value={row.dueDate}
                        onChange={(e) => updateRow(index, { dueDate: e.target.value })}
                        disabled={loading}
                      />
                    </div>
                    <div className="w-36 space-y-1">
                      {index === 0 && <span className="text-xs text-muted-foreground">Montant (DT)</span>}
                      <Input
                        type="number"
                        min={collected ? row.amountPaid : 0}
                        step="0.001"
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
                  {collected && (
                    <p className="text-xs text-muted-foreground">
                      Déjà encaissé : {formatDT(row.amountPaid)}
                    </p>
                  )}
                </div>
              )
            })}
            <Button type="button" variant="outline" size="sm" onClick={addRow} disabled={loading} className="gap-2">
              <Plus className="h-4 w-4" /> Ajouter une échéance
            </Button>
          </div>

          <div className="flex justify-end text-sm">
            <span className={matchesTotal ? "text-muted-foreground" : "text-amber-600 dark:text-amber-400"}>
              Total des échéances : {formatDT(sum)} / {formatDT(plan.totalPlanned)}
              {!matchesTotal && " — les deux doivent être égaux."}
            </span>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? "Enregistrement..." : "Enregistrer la révision"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

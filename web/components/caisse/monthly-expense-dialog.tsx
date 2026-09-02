"use client"

import type React from "react"

import { useEffect, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { ApiError } from "@/lib/api/client"
import { expensesApi, type RecurringExpensePayload } from "@/lib/api/expenses"
import type { RecurringExpenseDto } from "@/lib/api/types"
import { formatAmount, parseAmountInput } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { EXPENSE_CATEGORIES, PAYMENT_METHODS, type PaymentMethod } from "./expense-fields"

/**
 * « Modifier » a monthly dépense — the loyer has gone from 800 to 850.
 *
 * <p>⚠️ **It has no date field, and that absence is the feature.** A series has a *day of the month*, not a day,
 * and offering a date here would invite « je corrige le loyer de septembre » — which is a different act, done on
 * the dépense row itself in the table below. The footer says so in one line, because a user who expects this
 * month to change and finds it unchanged has silently lost a correction.</p>
 */
export function MonthlyExpenseDialog({
  open,
  onOpenChange,
  series,
  onSaved,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  series: RecurringExpenseDto | null
  onSaved: () => void | Promise<void>
}) {
  const [category, setCategory] = useState("")
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<PaymentMethod>("Cash")
  const [dayOfMonth, setDayOfMonth] = useState("1")
  const [description, setDescription] = useState("")
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [banner, setBanner] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  const [saving, setSaving] = useState(false)

  // The VERSION only, re-read on open: the list read lands after the fields hydrate, so its values would replace
  // what was typed. Same reasoning as the one-off dépense form.
  const { source: fresh, resync } = useFreshVersion(
    open,
    series?.id,
    series,
    async () => (await expensesApi.listRecurring()).find((s) => s.id === series!.id) ?? null,
  )

  const guard = useDirtyGuard(open, onOpenChange)

  useEffect(() => {
    if (!series) return
    setCategory(series.category)
    // `formatAmount`, never `String(...)`: the raw number reopens « 800,5 » as « 800.5 » in a product that
    // prints « 800,500 ». The grouping space is stripped again on parse.
    setAmount(formatAmount(series.amount))
    setMethod(series.method as PaymentMethod)
    setDayOfMonth(String(series.dayOfMonth))
    setDescription(series.description ?? "")
    setErrors({})
    setBanner(null)
    setIsConflict(false)
  }, [series, open])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!category) next.category = "La catégorie est requise"
    const parsedAmount = parseAmountInput(amount)
    if (amount === "" || Number.isNaN(parsedAmount) || parsedAmount <= 0) {
      next.amount = "Saisissez un montant supérieur à 0"
    }
    const parsedDay = Number(dayOfMonth)
    if (!Number.isInteger(parsedDay) || parsedDay < 1 || parsedDay > 31) {
      next.dayOfMonth = "Un jour entre 1 et 31"
    }
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!series || !validate()) return

    const payload: RecurringExpensePayload = {
      category,
      amount: parseAmountInput(amount),
      method,
      dayOfMonth: Number(dayOfMonth),
      description: description.trim() || null,
      version: fresh?.version ?? series.version,
    }

    try {
      setSaving(true)
      setBanner(null)
      setIsConflict(false)
      await expensesApi.updateRecurring(series.id, payload)
      toast.success("Dépense mensuelle modifiée")
      onOpenChange(false)
      await onSaved()
    } catch (err) {
      // In the form, not a toast: a 409 on money is not transient, and the typed amount has to stay on screen.
      const conflict = err instanceof ApiError && err.status === 409
      setIsConflict(conflict)
      setBanner(err instanceof ApiError ? err.message : "Échec de l'enregistrement de la dépense mensuelle")
      if (!conflict) await resync()
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop. */}
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Modifier la dépense mensuelle</DialogTitle>
            <DialogDescription>
              Les mois déjà enregistrés ne changent pas&nbsp;: la modification s&apos;applique aux prochains.
            </DialogDescription>
          </DialogHeader>

          <form onSubmit={handleSubmit} className="space-y-4">
            <FormErrorBanner
              message={banner}
              action={isConflict ? { label: "Recharger", onClick: () => void onSaved(), disabled: saving } : undefined}
            />

            <div className="space-y-2">
              <Label htmlFor="recurring-category">
                Catégorie <span className="text-destructive">*</span>
              </Label>
              <Select value={category} onValueChange={setCategory}>
                <SelectTrigger id="recurring-category">
                  <SelectValue placeholder="Sélectionner une catégorie" />
                </SelectTrigger>
                <SelectContent>
                  {EXPENSE_CATEGORIES.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {errors.category && <p className="text-xs text-destructive">{errors.category}</p>}
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="recurring-amount">
                  Montant (DT) <span className="text-destructive">*</span>
                </Label>
                {/* `text` + `inputMode="decimal"`, never `type="number"`: a number input refuses the comma this
                    product prints with, and a rejected keystroke returns an EMPTY value. */}
                <Input
                  id="recurring-amount"
                  type="text"
                  inputMode="decimal"
                  placeholder="0,000"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                />
                {errors.amount && <p className="text-xs text-destructive">{errors.amount}</p>}
              </div>

              <div className="space-y-2">
                <Label htmlFor="recurring-day">
                  Jour du mois <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="recurring-day"
                  type="text"
                  inputMode="numeric"
                  placeholder="5"
                  value={dayOfMonth}
                  onChange={(e) => setDayOfMonth(e.target.value)}
                />
                {errors.dayOfMonth ? (
                  <p className="text-xs text-destructive">{errors.dayOfMonth}</p>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Un 29, 30 ou 31 tombe le dernier jour des mois plus courts.
                  </p>
                )}
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="recurring-method">
                Mode de paiement <span className="text-destructive">*</span>
              </Label>
              <Select value={method} onValueChange={(v) => setMethod(v as PaymentMethod)}>
                <SelectTrigger id="recurring-method">
                  <SelectValue placeholder="Sélectionner" />
                </SelectTrigger>
                <SelectContent>
                  {PAYMENT_METHODS.map((m) => (
                    <SelectItem key={m.value} value={m.value}>
                      {m.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-2">
              <Label htmlFor="recurring-description">Description</Label>
              <Textarea
                id="recurring-description"
                placeholder="Facultatif"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={2}
              />
            </div>

            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
                Annuler
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? "Enregistrement…" : "Mettre à jour"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
    </>
  )
}

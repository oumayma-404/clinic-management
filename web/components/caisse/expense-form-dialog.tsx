"use client"

import type React from "react"

import { useEffect, useMemo, useState } from "react"
import { format, parseISO } from "date-fns"
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
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { ApiError } from "@/lib/api/client"
import { expensesApi, type ExpensePayload } from "@/lib/api/expenses"
import type { ExpenseDto } from "@/lib/api/types"
import { formatAmount, parseAmountInput } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
import { EXPENSE_CATEGORIES, PAYMENT_METHODS, type PaymentMethod } from "./expense-fields"

/**
 * The one form a dépense is written in — « Nouvelle dépense » and « Modifier la dépense ».
 *
 * <p>⚠️ **It lives here rather than inside `app/caisse/page.tsx` because it now has two callers.** It was a
 * same-file helper while the dépenses table was the only way to edit a dépense; the « Corriger » column of
 * l'extrait opens it too now, and a second copy of a form that carries a date resolution rule, an amount parser,
 * a concurrency token and a dirty guard is four ways for the two to disagree about the same row.</p>
 *
 * <p>Every field is editable on an existing dépense — the date and the catégorie included — because a dépense is
 * a correctable record: somebody mistypes 800 for 80, or files the loyer under « Autre », and the fix is the same
 * form that wrote it.</p>
 */
interface ExpenseFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingExpense: ExpenseDto | null
  /** yyyy-MM-dd — today, clamped into the window on screen; new expenses default to it. */
  defaultDay: string
  onSaved: () => void | Promise<void>
}

export function ExpenseFormDialog({ open, onOpenChange, editingExpense, defaultDay, onSaved }: ExpenseFormDialogProps) {
  const [expenseDate, setExpenseDate] = useState("")
  const [category, setCategory] = useState("")
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<PaymentMethod>("Cash")
  const [description, setDescription] = useState("")
  // « Répéter chaque mois ». Only ever offered on a NEW dépense — see the switch's own note below.
  const [repeatMonthly, setRepeatMonthly] = useState(false)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [banner, setBanner] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  /*
   * Band B — the version this form saves with, re-read on open rather than taken from the row that was clicked.
   * ⚠️ The VERSION only: the read lands after the fields hydrate below, so its values would replace what was typed.
   */
  const { source: freshExpense, resync } = useFreshVersion(
    open,
    editingExpense?.id,
    editingExpense,
    async () => {
      // Read over the dépense's OWN day, not the window on screen: the row being edited is somewhere inside a
      // period that can be a whole month, and there is no get-by-id on this resource.
      const day = format(parseISO(editingExpense!.expenseDate), "yyyy-MM-dd")
      const page = await expensesApi.listPaged({ fromDay: day, toDay: day })
      return page.items.find((e) => e.id === editingExpense!.id) ?? null
    },
  )
  const [saving, setSaving] = useState(false)

  // A typed dépense is not discarded by a stray tap (J9). Below `md:` this is a bottom sheet, so the strip above
  // it is a live dismiss target sitting over the form.
  const guard = useDirtyGuard(open, onOpenChange)

  useEffect(() => {
    if (editingExpense) {
      setExpenseDate(format(parseISO(editingExpense.expenseDate), "yyyy-MM-dd"))
      setCategory(editingExpense.category)
      // `formatAmount`, never `String(...)`: the raw number reopens an edited dépense as « 45.5 » in a product
      // that prints « 45,500 ». The grouping space is stripped again on parse.
      setAmount(formatAmount(editingExpense.amount))
      setMethod(editingExpense.method as PaymentMethod)
      setDescription(editingExpense.description ?? "")
    } else {
      setExpenseDate(defaultDay)
      setCategory("")
      setAmount("")
      setMethod("Cash")
      setDescription("")
    }
    // Reset on BOTH branches: a switch left on from the previous dépense would silently create a second series.
    setRepeatMonthly(false)
    setErrors({})
    setBanner(null)
    setIsConflict(false)
  }, [editingExpense, defaultDay, open])

  /*
   * « le 5 de chaque mois » — the switch's own hint, read off the date field rather than from a second control.
   *
   * ⚠️ Guarded, for `dayLabel`'s reason one screen up: the date input can legitimately be empty mid-retype, and a
   * hint that read « le NaN de chaque mois » would be the only thing on screen saying the form is broken.
   */
  const monthlyDayPhrase = useMemo(() => {
    const day = Number(expenseDate.slice(8, 10))
    return Number.isInteger(day) && day >= 1 && day <= 31 ? `le ${day} de chaque mois` : "chaque mois"
  }, [expenseDate])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!expenseDate) next.expenseDate = "La date est requise"
    if (!category) next.category = "La catégorie est requise"
    const parsed = parseAmountInput(amount)
    if (amount === "" || Number.isNaN(parsed) || parsed <= 0) next.amount = "Saisissez un montant supérieur à 0"
    if (!method) next.method = "Le mode de paiement est requis"
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return

    const payload: ExpensePayload = {
      /*
       * ⚠️ The bare `yyyy-MM-dd` the user picked, NOT an instant. `new Date("…T00:00:00").toISOString()` was
       * midnight in the WORKSTATION's zone: Africa/Tunis sent 23:00Z and filed on the right day, Asia/Dubai sent
       * 20:00Z and filed on the day before. The read side of la caisse takes bare day keys for exactly this
       * reason — « no conversion, which is the whole point » — and the write side did not.
       */
      expenseDate,
      category,
      amount: parseAmountInput(amount),
      method,
      description: description.trim() || null,
    }

    try {
      setSaving(true)
      setBanner(null)
      setIsConflict(false)
      if (editingExpense) {
        await expensesApi.update(editingExpense.id, {
          ...payload,
          version: freshExpense?.version ?? editingExpense.version,
        })
        toast.success("Dépense mise à jour")
      } else {
        await expensesApi.create({ ...payload, repeatMonthly })
        toast.success(
          repeatMonthly
            ? "Dépense ajoutée et programmée chaque mois"
            : "Dépense ajoutée",
        )
      }
      onOpenChange(false)
      await onSaved()
    } catch (err) {
      // In the form, not a toast: a 409 on money is not transient, and the typed amount has to stay on screen.
      const conflict = err instanceof ApiError && err.status === 409
      setIsConflict(conflict)
      setBanner(err instanceof ApiError ? err.message : "Échec de l'enregistrement de la dépense")
      if (!conflict) await resync()
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
    {/* Only the ROOT and « Annuler » route through the guard — the save path calls the raw prop (§ 5). */}
    <Dialog open={open} onOpenChange={guard.onOpenChange}>
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <DialogTitle>{editingExpense ? "Modifier la dépense" : "Nouvelle dépense"}</DialogTitle>
          <DialogDescription>
            {editingExpense
              ? "Modifiez les détails de la dépense"
              : "Saisissez les détails de la nouvelle dépense"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="expenseDate">
              Date <span className="text-destructive">*</span>
            </Label>
            <Input
              id="expenseDate"
              type="date"
              value={expenseDate}
              onChange={(e) => setExpenseDate(e.target.value)}
            />
            {errors.expenseDate && <p className="text-xs text-destructive">{errors.expenseDate}</p>}
          </div>
          <div>
            <FormErrorBanner
              message={banner}
              action={isConflict ? { label: "Recharger", onClick: () => void onSaved(), disabled: saving } : undefined}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="category">
              Catégorie <span className="text-destructive">*</span>
            </Label>
            <Select value={category} onValueChange={setCategory}>
              <SelectTrigger id="category">
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
              <Label htmlFor="amount">
                Montant (DT) <span className="text-destructive">*</span>
              </Label>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8): a number input refuses the comma
                  this product prints with, and a rejected keystroke returns an EMPTY value — so the amount looked
                  typed and the submit sent nothing. The placeholder now shows the separator the field accepts. */}
              <Input
                id="amount"
                type="text"
                inputMode="decimal"
                placeholder="0,000"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
              />
              {errors.amount && <p className="text-xs text-destructive">{errors.amount}</p>}
            </div>

            <div className="space-y-2">
              <Label htmlFor="method">
                Mode de paiement <span className="text-destructive">*</span>
              </Label>
              <Select value={method} onValueChange={(v) => setMethod(v as PaymentMethod)}>
                <SelectTrigger id="method">
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
              {errors.method && <p className="text-xs text-destructive">{errors.method}</p>}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              placeholder="Facultatif"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          {/*
            « Répéter chaque mois » — the whole entry point for les dépenses mensuelles, and one tap.
            It derives its day from the date already typed rather than adding a « jour du mois » field: the
            dépense being recorded IS the series' first occurrence, so a second field would ask the same question
            twice and let the two answers disagree.

            ⚠️ **Offered on a new dépense only.** On an existing row it would be ambiguous in the one way that
            matters on a money screen — does ticking it repeat this row, or turn a past month into a template? —
            so the switch is absent when editing and the series is edited in « Dépenses mensuelles » instead.

            Bordered and last, directly above « Ajouter »: an unbordered switch among five fields reads as a
            sixth field, and the decision it carries is « this happens again every month », which belongs where
            the eye lands before saving.
          */}
          {!editingExpense && (
            <div className="flex items-start justify-between gap-3 rounded-lg border border-border bg-muted/30 p-3">
              <div className="min-w-0 space-y-0.5">
                <Label htmlFor="repeatMonthly" className="cursor-pointer">
                  Répéter chaque mois
                </Label>
                <p id="repeatMonthly-hint" className="text-xs text-muted-foreground">
                  {repeatMonthly
                    ? `Enregistrée automatiquement ${monthlyDayPhrase}, sans rien ressaisir.`
                    : "Loyer, salaire, crédit… à n'enregistrer qu'une fois."}
                </p>
              </div>
              {/* An ISOLATED control, so `Switch`'s own `.touch-target` overlay is the right 44 px fix — it has
                  no adjacent sibling whose taps it could steal (§ 2). */}
              <Switch
                id="repeatMonthly"
                checked={repeatMonthly}
                onCheckedChange={setRepeatMonthly}
                aria-describedby="repeatMonthly-hint"
              />
            </div>
          )}

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Enregistrement…" : editingExpense ? "Mettre à jour" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
    <DiscardChangesDialog guard={guard} />
    </>
  )
}


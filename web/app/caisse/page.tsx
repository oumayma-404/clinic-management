"use client"

import type React from "react"

import { useCallback, useEffect, useMemo, useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { ArrowDownCircle, ArrowUpCircle, Loader2, Pencil, Plus, Trash2, Wallet } from "lucide-react"
import { expensesApi, type ExpensePayload } from "@/lib/api/expenses"
import { ApiError } from "@/lib/api/client"
import { formatDT } from "@/lib/format"
import type { CaisseSummaryDto, ExpenseDto } from "@/lib/api/types"

// --- Domain constants -----------------------------------------------------------------------------

type PaymentMethod = "Cash" | "Cheque" | "Card" | "Transfer"

/** French label ↔ PaymentMethod enum value (the API stores the enum name). */
const PAYMENT_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: "Cash", label: "Espèces" },
  { value: "Cheque", label: "Chèque" },
  { value: "Card", label: "Carte" },
  { value: "Transfer", label: "Virement" },
]

const EXPENSE_CATEGORIES = [
  "Loyer",
  "Salaires",
  "Fournitures",
  "Laboratoire",
  "Électricité/Eau",
  "Équipement",
  "Maintenance",
  "Taxes",
  "Autre",
]

// --- Helpers --------------------------------------------------------------------------------------

const methodLabel = (value: string): string => PAYMENT_METHODS.find((m) => m.value === value)?.label ?? value

/** Local calendar day → UTC ISO boundaries [dayStart, nextDay) for a `from`/`to` query. */
const dayBounds = (day: string): { from: string; to: string } => {
  const start = new Date(`${day}T00:00:00`)
  const next = new Date(start)
  next.setDate(next.getDate() + 1)
  return { from: start.toISOString(), to: next.toISOString() }
}

// --- Page -----------------------------------------------------------------------------------------

export default function CaissePage() {
  const [selectedDay, setSelectedDay] = useState<string>(() => format(new Date(), "yyyy-MM-dd"))
  const [summary, setSummary] = useState<CaisseSummaryDto | null>(null)
  const [expenses, setExpenses] = useState<ExpenseDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [modalOpen, setModalOpen] = useState(false)
  const [editingExpense, setEditingExpense] = useState<ExpenseDto | null>(null)

  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [expenseToDelete, setExpenseToDelete] = useState<ExpenseDto | null>(null)
  const [deleting, setDeleting] = useState(false)

  const { from, to } = useMemo(() => dayBounds(selectedDay), [selectedDay])

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const [summaryData, expensesData] = await Promise.all([
        expensesApi.caisseSummary(from, to),
        expensesApi.list(from, to),
      ])
      setSummary(summaryData)
      setExpenses(expensesData)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement de la caisse"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [from, to])

  useEffect(() => {
    loadData()
  }, [loadData])

  const dayLabel = useMemo(
    () => format(new Date(`${selectedDay}T00:00:00`), "EEEE d MMMM yyyy", { locale: fr }),
    [selectedDay],
  )

  const cashIn = summary?.cashIn ?? 0
  const cashOut = summary?.cashOut ?? 0
  const net = summary?.net ?? 0

  const handleAddNew = () => {
    setEditingExpense(null)
    setModalOpen(true)
  }

  const handleEdit = (expense: ExpenseDto) => {
    setEditingExpense(expense)
    setModalOpen(true)
  }

  const handleDelete = (expense: ExpenseDto) => {
    setExpenseToDelete(expense)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (!expenseToDelete) return
    try {
      setDeleting(true)
      await expensesApi.delete(expenseToDelete.id)
      toast.success("Dépense supprimée")
      setDeleteDialogOpen(false)
      setExpenseToDelete(null)
      await loadData()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression de la dépense")
    } finally {
      setDeleting(false)
    }
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Caisse</h1>
                  <p className="mt-1 text-sm capitalize text-muted-foreground">{dayLabel}</p>
                </div>

                <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                  <div className="flex items-center gap-2">
                    <Label htmlFor="caisse-day" className="text-sm text-muted-foreground">
                      Jour
                    </Label>
                    <Input
                      id="caisse-day"
                      type="date"
                      value={selectedDay}
                      onChange={(e) => setSelectedDay(e.target.value)}
                      className="w-auto"
                    />
                  </div>
                  <Button onClick={handleAddNew} className="gap-2">
                    <Plus className="h-4 w-4" />
                    Nouvelle dépense
                  </Button>
                </div>
              </div>

              {/* Caisse summary */}
              <div className="grid gap-4 sm:grid-cols-3">
                <Card>
                  <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Encaissements</CardTitle>
                    <ArrowUpCircle className="h-4 w-4 text-emerald-600" />
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-semibold text-emerald-600">{formatDT(cashIn)}</div>
                  </CardContent>
                </Card>

                <Card>
                  <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Dépenses</CardTitle>
                    <ArrowDownCircle className="h-4 w-4 text-destructive" />
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-semibold text-destructive">{formatDT(cashOut)}</div>
                  </CardContent>
                </Card>

                <Card>
                  <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Net</CardTitle>
                    <Wallet className="h-4 w-4 text-muted-foreground" />
                  </CardHeader>
                  <CardContent>
                    <div
                      className={
                        net < 0 ? "text-2xl font-semibold text-destructive" : "text-2xl font-semibold text-foreground"
                      }
                    >
                      {formatDT(net)}
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Expenses table */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <Wallet className="h-5 w-5" />
                    Dépenses du jour
                    <Badge variant="secondary" className="ml-2">
                      {expenses.length}
                    </Badge>
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  {loading ? (
                    <div className="flex items-center justify-center py-12 text-muted-foreground">
                      <Loader2 className="h-5 w-5 animate-spin" />
                    </div>
                  ) : error ? (
                    <p className="py-12 text-center text-sm text-destructive">{error}</p>
                  ) : (
                    <div className="overflow-x-auto">
                      <Table>
                        <TableHeader>
                          <TableRow>
                            <TableHead>Date</TableHead>
                            <TableHead>Catégorie</TableHead>
                            <TableHead className="text-right">Montant</TableHead>
                            <TableHead>Mode</TableHead>
                            <TableHead>Description</TableHead>
                            <TableHead className="text-right">Actions</TableHead>
                          </TableRow>
                        </TableHeader>
                        <TableBody>
                          {expenses.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={6} className="h-24 text-center">
                                <p className="text-muted-foreground">Aucune dépense pour ce jour</p>
                              </TableCell>
                            </TableRow>
                          ) : (
                            expenses.map((expense) => (
                              <TableRow key={expense.id}>
                                <TableCell className="text-muted-foreground">
                                  {format(parseISO(expense.expenseDate), "dd/MM/yyyy", { locale: fr })}
                                </TableCell>
                                <TableCell>
                                  <Badge variant="outline">{expense.category}</Badge>
                                </TableCell>
                                <TableCell className="text-right font-medium text-foreground">
                                  {formatDT(expense.amount)}
                                </TableCell>
                                <TableCell className="text-muted-foreground">{methodLabel(expense.method)}</TableCell>
                                <TableCell className="max-w-xs truncate text-muted-foreground">
                                  {expense.description?.trim() ? expense.description : "—"}
                                </TableCell>
                                <TableCell className="text-right">
                                  <div className="flex justify-end gap-2">
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      onClick={() => handleEdit(expense)}
                                      className="h-8 gap-1"
                                    >
                                      <Pencil className="h-3 w-3" />
                                      Modifier
                                    </Button>
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      onClick={() => handleDelete(expense)}
                                      className="h-8 gap-1 text-destructive hover:text-destructive"
                                    >
                                      <Trash2 className="h-3 w-3" />
                                      Supprimer
                                    </Button>
                                  </div>
                                </TableCell>
                              </TableRow>
                            ))
                          )}
                        </TableBody>
                      </Table>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </main>
        </div>

        <ExpenseFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingExpense={editingExpense}
          defaultDay={selectedDay}
          onSaved={loadData}
        />

        <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Confirmer la suppression</AlertDialogTitle>
              <AlertDialogDescription>
                Cette dépense
                {expenseToDelete ? (
                  <>
                    {" "}
                    (<span className="font-semibold">{expenseToDelete.category}</span> —{" "}
                    {formatDT(expenseToDelete.amount)})
                  </>
                ) : null}{" "}
                sera définitivement supprimée. Cette action est irréversible.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
              <AlertDialogAction
                onClick={(e) => {
                  e.preventDefault()
                  confirmDelete()
                }}
                disabled={deleting}
                className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              >
                {deleting ? "Suppression..." : "Supprimer"}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>
    </ClinicGuard>
  )
}

// --- Expense form modal (same-file helper) --------------------------------------------------------

interface ExpenseFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingExpense: ExpenseDto | null
  /** yyyy-MM-dd — the currently viewed day; new expenses default to it. */
  defaultDay: string
  onSaved: () => void | Promise<void>
}

function ExpenseFormModal({ open, onOpenChange, editingExpense, defaultDay, onSaved }: ExpenseFormModalProps) {
  const [expenseDate, setExpenseDate] = useState("")
  const [category, setCategory] = useState("")
  const [amount, setAmount] = useState("")
  const [method, setMethod] = useState<PaymentMethod>("Cash")
  const [description, setDescription] = useState("")
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (editingExpense) {
      setExpenseDate(format(parseISO(editingExpense.expenseDate), "yyyy-MM-dd"))
      setCategory(editingExpense.category)
      setAmount(String(editingExpense.amount))
      setMethod(editingExpense.method as PaymentMethod)
      setDescription(editingExpense.description ?? "")
    } else {
      setExpenseDate(defaultDay)
      setCategory("")
      setAmount("")
      setMethod("Cash")
      setDescription("")
    }
    setErrors({})
  }, [editingExpense, defaultDay, open])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!expenseDate) next.expenseDate = "La date est requise"
    if (!category) next.category = "La catégorie est requise"
    const parsed = parseFloat(amount)
    if (amount === "" || Number.isNaN(parsed) || parsed <= 0) next.amount = "Saisissez un montant supérieur à 0"
    if (!method) next.method = "Le mode de paiement est requis"
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!validate()) return

    const payload: ExpensePayload = {
      expenseDate: new Date(`${expenseDate}T00:00:00`).toISOString(),
      category,
      amount: parseFloat(amount),
      method,
      description: description.trim() || null,
    }

    try {
      setSaving(true)
      if (editingExpense) {
        await expensesApi.update(editingExpense.id, payload)
        toast.success("Dépense mise à jour")
      } else {
        await expensesApi.create(payload)
        toast.success("Dépense ajoutée")
      }
      onOpenChange(false)
      await onSaved()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement de la dépense")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
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

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="amount">
                Montant (DT) <span className="text-destructive">*</span>
              </Label>
              <Input
                id="amount"
                type="number"
                min="0"
                step="0.001"
                placeholder="0.000"
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
              placeholder="Optionnel"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
            />
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" disabled={saving}>
              {saving ? "Enregistrement..." : editingExpense ? "Mettre à jour" : "Ajouter"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

"use client"

import type React from "react"

import { useCallback, useEffect, useMemo, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
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
import { ArrowLeftRight, Loader2, Pencil, Plus, Search, Trash2, Wallet, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { expensesApi, type ExpensePayload } from "@/lib/api/expenses"
import { CaisseLedgerTable } from "@/components/caisse/caisse-ledger-table"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"
import { ApiError } from "@/lib/api/client"
import { formatDT } from "@/lib/format"
import type { CaisseLedgerDto, CaisseSummaryDto, ExpenseDto } from "@/lib/api/types"

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

/**
 * Local calendar day(s) → UTC ISO boundaries `[startOfFirstDay, nextDayAfterLast)` for a `from`/`to` query.
 *
 * The upper bound is the midnight AFTER the last day, matching what this page has always sent: the caisse handler's
 * own default is `from.AddDays(1).AddTicks(-1)`, and both forms include the whole final day.
 *
 * `endDay` defaults to `startDay`, so the single-day case is unchanged — la caisse gained a range mode for the
 * dashboard's « Dépenses » / « Net » drill-through (a monthly KPI had nowhere truthful to land on a day-only screen),
 * and the daily till remains what it opens on.
 */
const rangeBounds = (startDay: string, endDay: string = startDay): { from: string; to: string } => {
  const start = new Date(`${startDay}T00:00:00`)
  const next = new Date(`${endDay}T00:00:00`)
  next.setDate(next.getDate() + 1)
  return { from: start.toISOString(), to: next.toISOString() }
}

// --- Page -----------------------------------------------------------------------------------------

export default function CaissePage() {
  const [selectedDay, setSelectedDay] = useState<string>(() => format(new Date(), "yyyy-MM-dd"))
  // The range's end. Empty means "one day" — the daily till, which is what this screen is for.
  const [endDay, setEndDay] = useState<string>("")
  const [summary, setSummary] = useState<CaisseSummaryDto | null>(null)
  // The « extrait »: every movement behind the three totals above it. Fetched alongside them from the same
  // window, so the lines and the figures can never describe different periods.
  const [ledger, setLedger] = useState<CaisseLedgerDto | null>(null)
  const [expensePage, setExpensePage] = useState<PagedResponse<ExpenseDto>>(() => emptyPage<ExpenseDto>())
  const expenses = expensePage.items

  // One search box drives BOTH tables below, because they describe the same money from two angles and searching
  // « loyer » should not mean one thing in the statement and another in the dépenses list. The term is sent to the
  // server for each, so it matches across the whole period rather than the rows currently rendered.
  const [search, setSearch] = useState("")
  const [ledgerPageNumber, setLedgerPageNumber] = useState(1)
  const [expensePageNumber, setExpensePageNumber] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [modalOpen, setModalOpen] = useState(false)
  const [editingExpense, setEditingExpense] = useState<ExpenseDto | null>(null)

  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [expenseToDelete, setExpenseToDelete] = useState<ExpenseDto | null>(null)
  const [deleting, setDeleting] = useState(false)

  const isRange = Boolean(endDay) && endDay !== selectedDay
  const { from, to } = useMemo(
    () => rangeBounds(selectedDay, isRange ? endDay : selectedDay),
    [selectedDay, endDay, isRange],
  )

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      // The summary stays unpaged and unsearched on purpose: the four figures above are the totals for the whole
      // period, and narrowing them to a page (or to a search) would make them contradict the header they sit under.
      const [summaryData, ledgerData, expensesData] = await Promise.all([
        expensesApi.caisseSummary(from, to),
        expensesApi.caisseLedger({
          from,
          to,
          page: ledgerPageNumber,
          pageSize,
          search: search.trim() || undefined,
        }),
        expensesApi.listPaged({
          from,
          to,
          page: expensePageNumber,
          pageSize,
          search: search.trim() || undefined,
        }),
      ])
      setSummary(summaryData)
      setLedger(ledgerData)
      setExpensePage(expensesData)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement de la caisse"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [from, to, ledgerPageNumber, expensePageNumber, pageSize, search])

  useEffect(() => {
    loadData()
  }, [loadData])

  // A new search must not leave either table on a page its result set no longer has.
  useEffect(() => {
    setLedgerPageNumber(1)
    setExpensePageNumber(1)
  }, [search, from, to])

  // Dashboard drill-through (« Dépenses » / « Net »): ?from=&to= opens the range the KPI was computed over, so la
  // caisse and the dashboard show the same three figures. window.location in an effect rather than useSearchParams —
  // the repo's idiom. A malformed date is ignored, leaving today's till.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlFrom = params.get("from")
    const urlTo = params.get("to")
    if (urlFrom && !Number.isNaN(Date.parse(urlFrom))) setSelectedDay(urlFrom)
    if (urlTo && !Number.isNaN(Date.parse(urlTo))) setEndDay(urlTo)
  }, [])

  // La caisse had no realtime subscription at all — the one screen whose whole job is "what is in the
  // drawer right now" sat stale while a colleague recorded payments next door. Every money source it sums
  // is watched, because any of them moves the total.
  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans, RealtimeResource.Expenses],
    loadData,
  )

  const dayLabel = useMemo(() => {
    const start = format(new Date(`${selectedDay}T00:00:00`), "EEEE d MMMM yyyy", { locale: fr })
    if (!isRange) return start
    const end = format(new Date(`${endDay}T00:00:00`), "EEEE d MMMM yyyy", { locale: fr })
    return `du ${start} au ${end}`
  }, [selectedDay, endDay, isRange])

  const cashIn = summary?.cashIn ?? 0
  const refunds = summary?.refunds ?? 0
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
      <AppShell contentClassName="space-y-6">
        {/* Page Header */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <PageHeader zone="Argent" title="Caisse" subtitle={<span className="capitalize">{dayLabel}</span>} />

          <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
            <div className="flex items-end gap-2">
              <div className="space-y-1.5">
                <Label htmlFor="caisse-day" className="text-sm text-muted-foreground">
                  {isRange ? "Du" : "Jour"}
                </Label>
                <Input
                  id="caisse-day"
                  type="date"
                  value={selectedDay}
                  onChange={(e) => setSelectedDay(e.target.value)}
                  className="w-auto"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="caisse-day-end" className="text-sm text-muted-foreground">
                  Au (optionnel)
                </Label>
                <Input
                  id="caisse-day-end"
                  type="date"
                  value={endDay}
                  min={selectedDay}
                  onChange={(e) => setEndDay(e.target.value)}
                  className="w-auto"
                />
              </div>
              {isRange && (
                <Button variant="outline" onClick={() => setEndDay("")}>
                  Journée
                </Button>
              )}
            </div>
            <Button onClick={handleAddNew} className="gap-2">
              <Plus className="h-4 w-4" />
              Nouvelle dépense
            </Button>
          </div>
        </div>

        {/* Caisse summary. Four figures, not three: « Encaissements » is now GROSS and avoirs have their own
            card. They used to be silently subtracted inside it, which stopped working the moment the
            statement below listed a refund as money leaving — the lines would not have summed to the total
            printed above them. Net = Encaissements − Avoirs − Dépenses. */}
        {/*
          The four figures share ONE surface (`KpiGrid`), the same treatment the dashboard uses for the same
          numbers — four separate `Card`s meant four borders, four shadows and four figures of equal weight,
          and the two screens reporting identical money looked like two different products.

          « Net » takes the accent: it is the *result* of the three beside it, which four identical cards had
          no way of saying. The other three keep their semantic colour (encaissé positive, avoirs warning,
          dépenses destructive) through the theme tokens rather than raw `emerald-600` / `amber-600`.
        */}
        <KpiGrid columns={4}>
          <CaisseFigure label="Encaissements" hint="brut, hors avoirs" value={formatDT(cashIn)} tone="text-success" />
          <CaisseFigure label="Avoirs remboursés" hint="rendus aux patients" value={formatDT(refunds)} tone="text-warning-ink" />
          <CaisseFigure label="Dépenses" hint="sorties de caisse" value={formatDT(cashOut)} tone="text-destructive" />
          <CaisseFigure
            label="Net"
            hint="encaissé − avoirs − dépenses"
            value={formatDT(net)}
            tone={net < 0 ? "text-destructive" : "text-primary"}
          />
        </KpiGrid>

        {/* The « extrait » — the statement behind the four figures above. It sits above the expenses table
            because the expenses are a subset of it; that table stays for its edit/delete actions, which
            belong to the expense aggregate and not to a read-only movement line. */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ArrowLeftRight className="h-5 w-5" />
              Extrait de caisse
              <Badge variant="secondary" className="ml-2">
                {ledger?.totalCount ?? 0}
              </Badge>
            </CardTitle>
            <CardDescription>
              Tous les mouvements de la période, du plus ancien au plus récent — paiements de factures,
              échéances de devis, avoirs remboursés et dépenses. Un mouvement annulé reste visible, barré,
              et ne compte pas dans le solde.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div className="mb-4">
              <Label htmlFor="caisse-search" className="sr-only">
                Rechercher un mouvement
              </Label>
              <div className="relative">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  id="caisse-search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Rechercher un mouvement ou une dépense (libellé, patient, référence)…"
                  className="pl-9"
                />
              </div>
            </div>
            {error && !loading ? (
              <p className="py-8 text-center text-sm text-destructive">{error}</p>
            ) : (
              <>
                <CaisseLedgerTable movements={ledger?.movements ?? []} loading={loading} />
                {ledger && (
                  <DataTablePagination
                    page={ledger}
                    onPageChange={setLedgerPageNumber}
                    onPageSizeChange={setPageSize}
                    loading={loading}
                    label={["mouvement", "mouvements"]}
                  />
                )}
              </>
            )}
          </CardContent>
        </Card>

        {/* Expenses table */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Wallet className="h-5 w-5" />
              Dépenses du jour
              <Badge variant="secondary" className="ml-2">
                {expensePage.totalCount}
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
                {/* Title is the catégorie, not the description: the description is nullable and truncated, so
                    it makes a poor identity, while every expense has a category. The amount leads the fields —
                    it is what the row is about. */}
                <CardList
                  className={CARDS_ONLY}
                  ariaLabel="Dépenses"
                  items={expenses}
                  getKey={(e) => e.id}
                  title={(e) => e.category}
                  subtitle={(e) => e.description?.trim()}
                  fields={(e) => [
                    { label: "Montant", value: <span className="font-medium">{formatDT(e.amount)}</span> },
                    { label: "Date", value: format(parseISO(e.expenseDate), "dd/MM/yyyy", { locale: fr }) },
                    { label: "Mode", value: methodLabel(e.method) },
                  ]}
                  actions={(e) => (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" aria-label={`Actions pour la dépense ${e.category}`}>
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onSelect={() => handleEdit(e)}>Modifier</DropdownMenuItem>
                        <DropdownMenuItem
                          className="text-destructive focus:text-destructive"
                          onSelect={() => handleDelete(e)}
                        >
                          Supprimer
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                  empty={
                    search.trim()
                      ? "Aucune dépense ne correspond à votre recherche"
                      : "Aucune dépense pour ce jour"
                  }
                />
                <Table containerClassName={TABLE_ONLY}>
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
                          <p className="text-muted-foreground">
                            {search.trim()
                              ? "Aucune dépense ne correspond à votre recherche"
                              : "Aucune dépense pour ce jour"}
                          </p>
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
                <DataTablePagination
                  page={expensePage}
                  onPageChange={setExpensePageNumber}
                  onPageSizeChange={setPageSize}
                  loading={loading}
                  label={["dépense", "dépenses"]}
                />
              </div>
            )}
          </CardContent>
        </Card>

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
      </AppShell>
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

/**
 * One figure inside la caisse's shared surface.
 *
 * <p>`bg-card` is load-bearing — `KpiGrid` is a `bg-border` container showing through `gap-px`, so a cell that does
 * not paint its own background renders as a solid border block. Deliberately **not** a `KpiCard`: these figures are
 * not links (there is nothing to drill into from la caisse — the statement below already *is* the detail) and they
 * carry no period comparison, so reusing that component would mean passing an `href` of `#` and lying about it.</p>
 */
function CaisseFigure({
  label,
  hint,
  value,
  tone,
}: {
  label: string
  hint: string
  value: string
  tone: string
}) {
  return (
    <div className="bg-card p-4">
      <p className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
        <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-primary/70" />
        {label}
      </p>
      <p className={`mt-1 text-2xl font-semibold tabular-nums tracking-tight ${tone}`}>{value}</p>
      <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p>
    </div>
  )
}

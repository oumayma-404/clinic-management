"use client"

import type React from "react"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { format, parseISO, startOfMonth } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"
import { PageHeader } from "@/components/ui/page-header"
import { ExportButton } from "@/components/ui/export-button"
import { Stat, StatStrip } from "@/components/ui/stat-strip"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
import Link from "next/link"
import { ArrowLeftRight, Loader2, Pencil, Plus, Search, SearchX, Trash2, Wallet, MoreHorizontal, X } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ZONES, zoneChipClass } from "@/lib/zones"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { expensesApi, type ExpensePayload } from "@/lib/api/expenses"
import { CaisseLedgerTable } from "@/components/caisse/caisse-ledger-table"
import { CashInByMethod } from "@/components/caisse/cash-in-by-method"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"
import { ApiError } from "@/lib/api/client"
import { formatAmount, formatDT, parseAmountInput, quoteFr, toLocalIso, todayLocalIso } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"
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

/** La caisse is the « Finances » zone, so an empty state here wears the hue the rail and the eyebrow already do. */
const MONEY_CHIP = zoneChipClass(ZONES.money)

/**
 * The same hue, as a card header's icon chip.
 *
 * <p>`app/documents/page.tsx`'s template-tile idiom sized down: the glyph goes inside a tinted `rounded-lg`
 * square instead of sitting loose in the heading's own ink, where it is not an icon but more text. The two cards
 * below — l'extrait and les dépenses — are the named sections of this page, and they take the <b>zone</b> hue
 * rather than `primary` because « où suis-je ? » is what a mark at the top of a section answers, and the rail
 * and the page eyebrow already answer it in amber.</p>
 */
const MONEY_HEADER_CHIP = `flex size-8 shrink-0 items-center justify-center rounded-lg ${MONEY_CHIP}`

const methodLabel = (value: string): string => PAYMENT_METHODS.find((m) => m.value === value)?.label ?? value

/*
 * ⚠️ `rangeBounds` used to live here, turning the two `<input type="date">` values into UTC instants with
 * `new Date(`${day}T00:00:00`).toISOString()` — midnight in the **workstation's** timezone. On a machine set to
 * anything but UTC+1 that made « la caisse du 3 août » a window offset by hours from the Tunisian day, so a
 * payment taken at 23:30 landed in the wrong day's till and, on the 1st, the wrong month's revenue (AC-6).
 *
 * The page now sends the day keys themselves and the server resolves them through `CaissePeriod`/`ClinicClock`.
 * The clinic's day is a fact about the clinic, not about whoever happens to be looking at it — and the browser is
 * the one participant that cannot know it.
 */

// --- Page -----------------------------------------------------------------------------------------

/**
 * I3 — the role gate, as a **wrapper around** the page rather than a branch inside it.
 *
 * <p>The split is not stylistic. Everything below opens its own `useState`/`useEffect` and fetches on mount, so a
 * branch inside the component would still fire every request for a secretary — three 403s and their French error
 * toasts, on top of the refusal card. Not mounting the body at all is what makes the refusal the only thing that
 * happens.</p>
 *
 * <p>The server is the gate: `GET /api/billing/caisse` and `/caisse/ledger` are both `AdminOrDoctor`, so every figure and every line on this page is refused. This is the polish on top of it.</p>
 */
export default function CaissePage() {
  const { user, isLoading } = useSession()

  if (isLoading) {
    return (
      <ClinicGuard>
        <AppShell width="none" gutter={false}>
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        </AppShell>
      </ClinicGuard>
    )
  }

  if (hidesClinicWideMoney(user?.role)) {
    return (
      <ClinicGuard>
        <AppShell width="none" gutter={false}>
          <AccessDeniedCard description="La caisse et son extrait sont réservés au praticien et à l'administrateur. Vous pouvez encaisser un paiement depuis la fiche du patient." />
        </AppShell>
      </ClinicGuard>
    )
  }

  return <CaisseContent />
}

function CaisseContent() {
  /**
   * `DELETE /api/expenses/{id}` is **AdminOnly** while reading la caisse and updating a dépense are
   * AdminOrDoctor — so « Supprimer » was offered to the praticien and answered 403.
   *
   * ⚠️ `hidesClinicWideMoney` was the only gate on this screen, and it answers a different question (the
   * secretary's). One missing role in the presentation layer, not a boundary in the wrong place: the doctor
   * genuinely may read the till and edit a dépense. Deleting one silently raises the reported Net, which is why
   * the server keeps it to an admin.
   */
  const { user: caisseUser } = useSession()
  const canDeleteExpense = caisseUser?.role === "admin"
  // The window opens on the month TO DATE, not on today: every figure here is read to answer « how is the month
  // going », and a one-day default made that question start with widening the range by hand.
  const [selectedDay, setSelectedDay] = useState<string>(() => toLocalIso(startOfMonth(new Date())))
  // The range's end. Empty means "one day" — the daily till, which « Aujourd'hui » comes back to.
  const [endDay, setEndDay] = useState<string>(() => todayLocalIso())
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
  // L8 slice B — « ne montre que les chèques ». It narrows the EXTRAIT only, never the four figures above it:
  // those are the totals for the period, and a filtered « Encaissements » would contradict the header it sits
  // under. The breakdown row is what states the per-method figure, and it is also this filter's control.
  const [methodFilter, setMethodFilter] = useState<string | null>(null)
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
  // Bare `YYYY-MM-DD`, exactly as the date inputs hold them — no conversion, which is the whole point.
  const fromDay = selectedDay
  const toDay = isRange ? endDay : selectedDay
  // A dépense is dated the day it happened, so its form opens on TODAY — clamped into the window on screen, since
  // `selectedDay` is now the 1st and pre-filling that would date every new dépense to the start of the month.
  const newExpenseDay = useMemo(() => {
    const today = todayLocalIso()
    return today < fromDay ? fromDay : today > toDay ? toDay : today
  }, [fromDay, toDay])

  // ⚠️ Every read is stamped with a generation and a late one is DISCARDED. `?from=&to=` (the dashboard's own
  // drill-through) arrives in an effect, so the default month's read is already in flight when the URL period's
  // read starts — and without this the slower of the two won, painting one period's figures under the other
  // period's header.
  const requestGeneration = useRef(0)

  const loadData = useCallback(async () => {
    const generation = ++requestGeneration.current
    try {
      setLoading(true)
      setError(null)
      // The summary stays unpaged and unsearched on purpose: the four figures above are the totals for the whole
      // period, and narrowing them to a page (or to a search) would make them contradict the header they sit under.
      const [summaryData, ledgerData, expensesData] = await Promise.all([
        expensesApi.caisseSummary(fromDay, toDay),
        expensesApi.caisseLedger({
          fromDay,
          toDay,
          page: ledgerPageNumber,
          pageSize,
          search: search.trim() || undefined,
          method: methodFilter ?? undefined,
        }),
        // The dépenses table shares the window with the totals and the extrait, so it takes the same day keys —
        // a second convention here would let the money-out list answer for a different day from the money-out
        // figure above it.
        expensesApi.listPaged({
          fromDay,
          toDay,
          page: expensePageNumber,
          pageSize,
          search: search.trim() || undefined,
        }),
      ])
      if (generation !== requestGeneration.current) return
      setSummary(summaryData)
      setLedger(ledgerData)
      setExpensePage(expensesData)
    } catch (err) {
      if (generation !== requestGeneration.current) return
      const message = err instanceof ApiError ? err.message : "Échec du chargement de la caisse"
      setError(message)
      toast.error(message)
    } finally {
      // A superseded read must not clear the live read's spinner either, or the page reads as settled while the
      // period it is about to show is still in flight.
      if (generation === requestGeneration.current) setLoading(false)
    }
  }, [fromDay, toDay, ledgerPageNumber, expensePageNumber, pageSize, search, methodFilter])

  useEffect(() => {
    loadData()
  }, [loadData])

  // A new search must not leave either table on a page its result set no longer has. The method filter narrows
  // only the extrait, so it resets only that pager — resetting the dépenses one too would move a table the
  // filter does not touch.
  useEffect(() => {
    setLedgerPageNumber(1)
    setExpensePageNumber(1)
  }, [search, fromDay, toDay])

  useEffect(() => {
    setLedgerPageNumber(1)
  }, [methodFilter])

  // Dashboard drill-through (« Dépenses » / « Net »): ?from=&to= opens the range the KPI was computed over, so la
  // caisse and the dashboard show the same three figures. window.location in an effect rather than useSearchParams —
  // the repo's idiom. A malformed date is ignored, leaving the month to date.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlFrom = params.get("from")
    const urlTo = params.get("to")
    if (urlFrom && !Number.isNaN(Date.parse(urlFrom))) {
      setSelectedDay(urlFrom)
      // `?from=` alone names ONE day; without this the month default's end would silently widen it to today.
      if (!urlTo) setEndDay("")
    }
    if (urlTo && !Number.isNaN(Date.parse(urlTo))) setEndDay(urlTo)
  }, [])

  // La caisse had no realtime subscription at all — the one screen whose whole job is "what is in the
  // drawer right now" sat stale while a colleague recorded payments next door. Every money source it sums
  // is watched, because any of them moves the total.
  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans, RealtimeResource.Expenses],
    loadData,
  )

  /**
   * The header's own sentence.
   *
   * ⚠️ **Guarded, because clearing « Du » destroyed the whole screen.** `new Date("T00:00:00")` is an Invalid Date,
   * `format` throws `RangeError: Invalid time value`, and the error boundary replaced la caisse with « Une erreur
   * inattendue est survenue » — leaving only « Réessayer » and « Retour au tableau de bord ». Ctrl+A + Suppr in a
   * date field is an ordinary way to retype a date, and clearing « Au » was already handled; only the start was
   * unguarded. A blank field now reads as « période incomplète » and the page stays up.
   */
  const dayLabel = useMemo(() => {
    const day = (value: string) => {
      const parsed = new Date(`${value}T00:00:00`)
      return Number.isNaN(parsed.getTime()) ? null : format(parsed, "EEEE d MMMM yyyy", { locale: fr })
    }
    const start = day(selectedDay)
    if (!start) return "période incomplète — choisissez une date de début"
    if (!isRange) return start
    const end = day(endDay)
    return end ? `du ${start} au ${end}` : start
  }, [selectedDay, endDay, isRange])

  const cashIn = summary?.cashIn ?? 0
  const refunds = summary?.refunds ?? 0
  const cashOut = summary?.cashOut ?? 0
  const net = summary?.net ?? 0
  // Only used to decide whether to offer the « chèques à encaisser » link — a cheque taken in this window is not
  // necessarily a cheque that can be banked, and the two questions belong on two screens.
  const chequeTotal = summary?.cashInByMethod?.find((m) => m.method === "Cheque")?.amount ?? 0
  // Through the page's existing `methodLabel` rather than the summary's `label` field: the chip must be able to
  // name the active filter before the summary has loaded (and after a failed load), and this page already owns one
  // French label per method for the expense form. Two lookups for one word is how they drift.
  const activeMethodLabel = methodFilter ? methodLabel(methodFilter) : null

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

  /*
   * One empty state, rendered by both halves of the responsive pair (the `CardList` below `md:` and the table
   * above it) so the two can never say different things.
   *
   * ⚠️ The filtered case never offers « Nouvelle dépense ». The row very likely exists and the term was simply
   * mistyped, and an « Ajouter » button on a no-match screen is an invitation to create the duplicate.
   */
  const searchTerm = search.trim()
  const expensesEmpty = searchTerm ? (
    <EmptyState
      size="compact"
      icon={SearchX}
      chipClassName={MONEY_CHIP}
      title={`Aucune dépense ne correspond à ${quoteFr(searchTerm)}`}
      description="La recherche porte sur la catégorie et la description, sur toute la période affichée."
      secondaryAction={
        <Button size="sm" variant="outline" onClick={() => setSearch("")}>
          Effacer les filtres
        </Button>
      }
    />
  ) : (
    <EmptyState
      size="compact"
      icon={Wallet}
      chipClassName={MONEY_CHIP}
      title={isRange ? "Aucune dépense sur cette période" : "Aucune dépense pour ce jour"}
      description="Loyer, fournitures, laboratoire, maintenance… tout ce qui sort de la caisse se saisit ici."
      action={
        <Button size="sm" onClick={handleAddNew} className="gap-2">
          <Plus className="h-4 w-4" />
          Nouvelle dépense
        </Button>
      }
    />
  )

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
        {/*
          Page header. The période controls and « Nouvelle dépense » go through `PageHeader`'s own `actions`
          slot: wrapped in a flex row beside it, the header shrank to the width of « Caisse » and its zone wash
          — which bleeds past its own box to meet the page gutter — was cut off with a hard vertical edge a
          third of the way across the page.
        */}
        <PageHeader
          title="Caisse"
          subtitle={<span className="capitalize">{dayLabel}</span>}
          actions={
            // `items-end` so the labelled date fields line up on their BOXES with the buttons beside them.
            <div className="flex flex-wrap items-end gap-2">
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
                  Au (facultatif)
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
                /* `coarse:h-11` — three action buttons in one `gap-2` row, so they grow their own boxes rather
                   than overlaying hit areas that would overlap (§ 2). Measured 107x36 before. */
                <Button
                  variant="outline"
                  className="coarse:h-11"
                  onClick={() => {
                    setSelectedDay(todayLocalIso())
                    setEndDay("")
                  }}
                >
                  Aujourd&apos;hui
                </Button>
              )}
              {/* L5 — the « extrait de caisse », over the window on screen. ⚠️ The free-text `search` is
                  deliberately NOT sent: « Solde de la période » is computed over the whole window before
                  filtering, so a text-filtered file would carry a running-balance column that sums to nothing.
                  The file is the statement for the period, which is what an accountant reconciles against a
                  bank statement. */}
              <ExportButton
                path="/billing/caisse/ledger/export"
                label="mouvements"
                params={{ fromDay, toDay }}
              />
              {/* The screen's primary action, measured 165x36. Same reasoning as « Aujourd'hui » beside it. */}
              <Button onClick={handleAddNew} className="gap-2 coarse:h-11">
                <Plus className="h-4 w-4" />
                Nouvelle dépense
              </Button>
            </div>
          }
        />

        {/* Caisse summary. Four figures, not three: « Encaissements » is now GROSS and avoirs have their own
            card. They used to be silently subtracted inside it, which stopped working the moment the
            statement below listed a refund as money leaving — the lines would not have summed to the total
            printed above them. Net = Encaissements − Avoirs − Dépenses. */}
        {/*
          The four figures share ONE surface (`StatStrip`) — the app's one summary strip, the same object
          « Factures », « Chèques » and « Rappels » now draw. Four separate `Card`s meant four borders, four
          shadows and four figures of equal weight; four *differently styled* strips meant the same money read
          two ways two clicks apart.

          « Net » takes the accent: it is the *result* of the three beside it, which four identical cards had
          no way of saying. The other three keep their semantic tone (encaissé positive, avoirs active,
          dépenses negative) through `ui/status-tone.ts` rather than a raw `emerald-600` / `amber-600` — and
          the tone now colours the figure alone, never the cell.
        */}
        {/*
          ⚠️ Band C, and this was the worst instance in the QA pass: on a failed read all four figures asserted
          « 0,000 DT ». On la caisse that is not a missing number, it is a statement that the practice took nothing
          and spent nothing — the one screen where a wrong zero is indistinguishable from a real day, and the one a
          dentist reads to decide whether the day balanced. A failed read renders the failure instead, with a retry.
        */}
        {error && !loading ? (
          <LoadFailureNotice
            message="Les totaux de la caisse n'ont pas pu être chargés."
            detail="Aucun montant n'est affiché : un « 0,000 DT » ici se lirait comme une journée sans mouvement."
            onRetry={loadData}
          />
        ) : (
          <StatStrip>
            <Stat label="Encaissements" hint="brut, hors avoirs" value={formatDT(cashIn)} tone="positive" loading={loading} />
            <Stat label="Avoirs remboursés" hint="rendus aux patients" value={formatDT(refunds)} tone="active" loading={loading} />
            <Stat label="Dépenses" hint="sorties de caisse" value={formatDT(cashOut)} tone="negative" loading={loading} />
            <Stat
              label="Net"
              hint="encaissé − avoirs − dépenses"
              value={formatDT(net)}
              tone={net < 0 ? "negative" : "accepted"}
              loading={loading}
            />
          </StatStrip>
        )}

        {/* « dont espèces / chèque / carte / virement » (L8 slice B). It sits under the grid rather than inside it
            because it is a decomposition of ONE of the four figures, not a fifth figure — and because each chip
            is also the control that narrows the extrait to the movements behind it. */}
        <CashInByMethod
          totals={summary?.cashInByMethod ?? []}
          selected={methodFilter}
          onSelect={setMethodFilter}
        />

        {/* « Chèques à encaisser » lives on its own screen, but this is where somebody notices the figure. The
            link is only offered when there IS cheque money in the window — an invitation to a screen that will be
            empty is worse than no invitation. */}
        {chequeTotal > 0 && (
          <p className="text-sm text-muted-foreground">
            Un chèque encaissé dans la période n'est pas forcément encaissable&nbsp;:{" "}
            <Link href="/cheques" className="font-medium text-primary underline-offset-4 hover:underline">
              voir les chèques à encaisser
            </Link>
            .
          </p>
        )}

        {/* The « extrait » — the statement behind the four figures above. It sits above the expenses table
            because the expenses are a subset of it; that table stays for its edit/delete actions, which
            belong to the expense aggregate and not to a read-only movement line. */}
        <Card>
          <CardHeader>
            {/* See MONEY_HEADER_CHIP. `flex-wrap` because the chip narrows the title column by ~40px and this
                one carries a count badge behind a five-word heading. */}
            <CardTitle className="flex min-w-0 flex-wrap items-center gap-2.5 leading-snug">
              <span aria-hidden="true" className={MONEY_HEADER_CHIP}>
                <ArrowLeftRight className="size-4" strokeWidth={1.75} />
              </span>
              Extrait de caisse
              <Badge variant="secondary">{ledger?.totalCount ?? 0}</Badge>
            </CardTitle>
            <CardDescription>
              {/* « du plus récent au plus ancien » — the statement is newest-first on purpose (the closing balance
                  is the top row, `GetCaisseLedgerQuery` reverses after computing it) and the sentence said the
                  opposite, so a reader reconciling it against a bank statement started from the wrong end. */}
              Tous les mouvements de la période, du plus récent au plus ancien — paiements de factures,
              échéances de devis, avoirs remboursés et dépenses. Un mouvement annulé reste visible, barré,
              et ne compte pas dans le solde.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div className="mb-4 space-y-2">
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
              {/* § 13 — an active filter is a REMOVABLE chip at every width. The breakdown chip above is already
                  pressed, but it is a screen away from the table it narrows once the page scrolls, and « il manque
                  des mouvements » is what an unlabelled filtered list reads as. */}
              {methodFilter && (
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary" className="gap-1.5 py-1 ps-2.5 pe-1.5">
                    Mode&nbsp;: {activeMethodLabel}
                    <button
                      type="button"
                      onClick={() => setMethodFilter(null)}
                      aria-label={`Retirer le filtre ${activeMethodLabel}`}
                      className="rounded-full p-0.5 hover:bg-background/60 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring coarse:p-1.5"
                    >
                      <X className="size-3" aria-hidden="true" />
                    </button>
                  </Badge>
                  <span className="text-xs text-muted-foreground">
                    Les totaux ci-dessus couvrent toute la période, tous modes confondus.
                  </span>
                </div>
              )}
            </div>
            {error && !loading ? (
              // Band C — the extrait gets the same treatment as the figures above it, and a way back.
              <LoadFailureNotice
                message="L'extrait de caisse n'a pas pu être chargé."
                onRetry={loadData}
              />
            ) : (
              <>
                <CaisseLedgerTable
                  movements={ledger?.movements ?? []}
                  closingBalance={ledger?.closingBalance}
                  loading={loading}
                  // The method filter counts as « filtered » too, or a period with no cheques would render the
                  // first-run « aucun mouvement » invite about a till that in fact took money all day.
                  isFiltered={Boolean(searchTerm) || Boolean(methodFilter)}
                  onClearSearch={() => {
                    setSearch("")
                    setMethodFilter(null)
                  }}
                />
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
            <div className="flex flex-wrap items-center justify-between gap-3">
              <CardTitle className="flex min-w-0 flex-wrap items-center gap-2.5 leading-snug">
                <span aria-hidden="true" className={MONEY_HEADER_CHIP}>
                  <Wallet className="size-4" strokeWidth={1.75} />
                </span>
                {isRange ? "Dépenses de la période" : "Dépenses du jour"}
                <Badge variant="secondary">{expensePage.totalCount}</Badge>
              </CardTitle>
              {/*
                L5 — a SECOND export on this page, and deliberately not a duplicate of the header's. That one is
                the « extrait de caisse » (every movement, both directions, with the running balance); this one is
                the dépenses list, which is what a clinic hands its accountant at the end of a month.

                ⚠️ Unlike the extrait, this one DOES send the search term: the expenses list is loaded narrowed by
                it (`expensesApi.listPaged`), so the file must be too. The extrait cannot, because its
                « Solde de la période » is computed over the whole window before filtering.
              */}
              <ExportButton
                path="/expenses/export"
                label="dépenses"
                compact
                params={{ fromDay, toDay, search: searchTerm || undefined }}
              />
            </div>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="flex items-center justify-center py-12 text-muted-foreground">
                <Loader2 className="h-5 w-5 animate-spin" />
              </div>
            ) : error ? (
              <LoadFailureNotice
                message="La liste des dépenses n'a pas pu être chargée."
                onRetry={loadData}
              />
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
                        {/* Admin only — the server's DELETE is AdminOnly while this screen and the PUT are not.
                            See `canDeleteExpense`. */}
                        {canDeleteExpense && (
                          <DropdownMenuItem
                            className="text-destructive focus:text-destructive"
                            onSelect={() => handleDelete(e)}
                          >
                            Supprimer
                          </DropdownMenuItem>
                        )}
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                  empty={expensesEmpty}
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
                    {/* `TableEmptyRow` drops the cell padding (the empty state owns its own vertical rhythm)
                        and pins the block to the visible left edge, so a wide, scrolled table cannot cut the
                        invite in half. */}
                    {expenses.length === 0 ? (
                      <TableEmptyRow colSpan={6}>{expensesEmpty}</TableEmptyRow>
                    ) : (
                      expenses.map((expense) => (
                        <TableRow key={expense.id}>
                          <TableCell className="text-muted-foreground">
                            {format(parseISO(expense.expenseDate), "dd/MM/yyyy", { locale: fr })}
                          </TableCell>
                          <TableCell>
                            <Badge variant="outline">{expense.category}</Badge>
                          </TableCell>
                          <TableCell numeric className="font-medium text-foreground">
                            {formatDT(expense.amount)}
                          </TableCell>
                          <TableCell className="text-muted-foreground">{methodLabel(expense.method)}</TableCell>
                          <TableCell clamp className="max-w-xs text-muted-foreground" title={expense.description?.trim() || undefined}>
                            {expense.description?.trim() ? expense.description : "—"}
                          </TableCell>
                          <TableCell className="text-right">
                            <div className="flex justify-end gap-2">
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => handleEdit(expense)}
                                className="h-8 w-8 p-0 coarse:size-11"
                                title="Modifier la dépense"
                                aria-label={`Modifier la dépense ${expense.category}`}
                              >
                                <Pencil className="h-4 w-4" />
                              </Button>
                              {canDeleteExpense && (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  onClick={() => handleDelete(expense)}
                                  className="h-8 w-8 p-0 text-destructive hover:text-destructive coarse:size-11"
                                  title="Supprimer la dépense"
                                  aria-label={`Supprimer la dépense ${expense.category}`}
                                >
                                  <Trash2 className="h-4 w-4" />
                                </Button>
                              )}
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
          defaultDay={newExpenseDay}
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
              {deleting ? "Suppression…" : "Supprimer"}
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
  /** yyyy-MM-dd — today, clamped into the window on screen; new expenses default to it. */
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
    setErrors({})
    setBanner(null)
    setIsConflict(false)
  }, [editingExpense, defaultDay, open])

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
        await expensesApi.create(payload)
        toast.success("Dépense ajoutée")
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


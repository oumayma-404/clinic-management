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
import { Table, TableBody, TableCell, TableEmptyRow, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
import { ArrowLeftRight, Loader2, Pencil, Plus, Repeat, Search, SearchX, Trash2, Wallet, MoreHorizontal, X } from "lucide-react"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ZONES, zoneChipClass } from "@/lib/zones"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { expensesApi } from "@/lib/api/expenses"
import { CaisseLedgerTable } from "@/components/caisse/caisse-ledger-table"
import { CashInByMethod } from "@/components/caisse/cash-in-by-method"
import { MonthlyExpenseList } from "@/components/caisse/monthly-expense-list"
import { ExpenseFormDialog } from "@/components/caisse/expense-form-dialog"
import { methodLabel } from "@/components/caisse/expense-fields"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"
import { ApiError } from "@/lib/api/client"
import { formatDT, quoteFr, toLocalIso, todayLocalIso } from "@/lib/format"
import type { CaisseLedgerDto, CaisseSummaryDto, ExpenseDto, RecurringExpenseDto } from "@/lib/api/types"

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
  // The standing monthly commitments. Not period data — see `MonthlyExpenseList` — so this is the whole list
  // whatever window is on screen, and it is deliberately outside `search` too: hiding a loyer because somebody
  // typed « chèque » in the movements box would answer a question about the extrait with a fact about the future.
  const [recurring, setRecurring] = useState<RecurringExpenseDto[]>([])

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
      const [summaryData, ledgerData, expensesData, recurringData] = await Promise.all([
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
        // No window, no page, no search — a standing commitment has none of those.
        expensesApi.listRecurring(),
      ])
      if (generation !== requestGeneration.current) return
      setSummary(summaryData)
      setLedger(ledgerData)
      setExpensePage(expensesData)
      setRecurring(recurringData)
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
  /*
   * « mensuelle » — the badge that answers « qui a saisi ça ? » about a dépense nobody typed.
   *
   * ⚠️ It is a label on the row's ORIGIN and changes nothing else about it: the row still edits and deletes like
   * any other, and it still counts in every total. Without the badge, an automatic posting is indistinguishable
   * from a colleague's entry — which on a money screen is the difference between « c'est le loyer » and « qui a
   * ajouté 800 dinars ? ».
   */
  const monthlyBadge = (expense: ExpenseDto) =>
    expense.recurringExpenseId ? (
      <Badge variant="secondary" className="gap-1">
        <Repeat className="size-3" aria-hidden="true" />
        mensuelle
      </Badge>
    ) : null

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
            because the expenses are a subset of it.
            ⚠️ It is no longer read-only for a dépense: its « Corriger » column opens the same form the table
            below does (`ExpenseMovementActions`). The old note here said edit/delete « belong to the expense
            aggregate and not to a read-only movement line » — but a dépense movement IS that aggregate, one
            projection away, so the distinction only ever cost the reader a scroll to a second table listing
            rows they were already looking at. A PAYMENT line still refuses, and for the real reason: it has a
            numbered note behind it. */}
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
                  // Correcting a line moves the running balance and every total above it, so the whole page is
                  // re-read rather than the row patched in place.
                  onChanged={() => void loadData()}
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

        {/* Les dépenses mensuelles — above the table because they EXPLAIN part of it: a « Loyer 800,000 » nobody
            remembers typing has its answer one card up, and each row it posted is badged « mensuelle » below.
            Renders nothing until the cabinet has one, and is hidden behind a failed read rather than asserting an
            empty list — « aucune dépense mensuelle » on this screen would read as « rien n'est programmé ». */}
        {!error && <MonthlyExpenseList series={recurring} onChanged={loadData} />}

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
                {/*
                  ⚠️ The **`lg:`** pair, not `md:`, and this is a bug fix rather than a preference.

                  Measured: these six columns come to 610 px at their min-content, while an `md:` table gets
                  399 px at 768 and 531 px at 900 — so from 768 px to 1023 px the column that fell outside the
                  scrollport was **Actions**, i.e. the pencil and the bin. A dépense entered by mistake was
                  therefore neither correctable nor removable on a tablet portrait or an unmaximised laptop
                  window: not because the capability was missing — every field edits fine — but because the only
                  two controls sat behind a sideways drag nobody thinks to try, which is exactly § 0's
                  « no capability is removed by a layout decision ».

                  On `lg:` that band gets the card list, whose one ⋯ menu carries « Modifier » and « Supprimer »
                  in view at every width. It also puts all three caisse tables on the same hinge — l'extrait was
                  already `lg:`, and this one was the odd one out.
                */}
                <CardList
                  className={CARDS_ONLY_LG}
                  ariaLabel="Dépenses"
                  items={expenses}
                  getKey={(e) => e.id}
                  title={(e) => e.category}
                  status={monthlyBadge}
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
                <Table containerClassName={TABLE_ONLY_LG}>
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
                            {/* `flex-wrap`, because « Loyer » + « mensuelle » is two badges in a cell that also
                                has to survive 820 px with five siblings — and a `Badge` is `shrink-0`. */}
                            <div className="flex flex-wrap items-center gap-1.5">
                              <Badge variant="outline">{expense.category}</Badge>
                              {monthlyBadge(expense)}
                            </div>
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

        <ExpenseFormDialog
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

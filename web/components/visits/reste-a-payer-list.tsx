"use client"

import { useCallback, useEffect, useState } from "react"
import { AlertCircle, ChevronRight, RefreshCw, Search, Wallet } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { ContactActions } from "@/components/visits/contact-actions"
import { PatientNameLink } from "@/components/patient-name-link"
import { ResteAPayerPatientSheet } from "@/components/visits/reste-a-payer-patient-sheet"
import { useDebtLineCollection } from "@/components/patient/use-debt-line-collection"
import { billingApi } from "@/lib/api/billing"
import type { PatientDebtLineDto, ResteAPayerPageDto, ResteAPayerRowDto } from "@/lib/api/types"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import { formatDT, formatDateFr, quoteFr } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { cn } from "@/lib/utils"

type List = "due" | "running"
type Sort = "age" | "amount"

const SORT_OPTIONS = [
  { value: "age" as const, label: "Plus ancien" },
  { value: "amount" as const, label: "Montant" },
]

/**
 * « Reste à payer » (4th tab of « À clôturer »): « À relancer » = due now, « En cours » = not due yet.
 * The server classifies every amount (`PatientDebtLines`) — nothing here sums or classifies money.
 */
export function ResteAPayerList({
  onDueCountChange,
}: {
  /** The server's « À relancer » patient count for the tab's badge; `null` after a failed read. */
  onDueCountChange?: (count: number | null) => void
}) {
  const [list, setList] = useState<List>("due")
  const [sort, setSort] = useState<Sort>("age")
  const [search, setSearch] = useState("")
  const [term, setTerm] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [data, setData] = useState<ResteAPayerPageDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<ResteAPayerRowDto | null>(null)
  const [sheetReloadKey, setSheetReloadKey] = useState(0)

  // Debounced: each keystroke is a read over every patient who owes.
  useEffect(() => {
    const id = setTimeout(() => {
      setTerm(search.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(id)
  }, [search])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await billingApi.getResteAPayer({ list, sort, search: term || undefined, page, pageSize })
      // Settling the last row of page 2 leaves it empty past the end; step back rather than claim « personne ».
      if (result.items.length === 0 && result.totalCount > 0 && page > 1) {
        setPage(Math.min(page - 1, Math.max(1, result.totalPages)))
        return
      }
      setData(result)
      setError(null)
      if (!term) onDueCountChange?.(result.dueCount)
    } catch (err) {
      setError(getErrorMessage(err))
      setData(null)
      onDueCountChange?.(null)
    } finally {
      setLoading(false)
    }
  }, [list, sort, term, page, pageSize, onDueCountChange])

  useEffect(() => {
    void load()
  }, [load])

  // Every write that moves a balance: a payment (invoices / treatmentplans), a fiche billed (patients), a visit.
  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans, RealtimeResource.Patients, RealtimeResource.Appointments],
    load,
  )

  const refreshAfterPayment = useCallback(() => {
    void load()
    setSheetReloadKey((k) => k + 1)
  }, [load])

  const collection = useDebtLineCollection(refreshAfterPayment)

  const collect = (line: PatientDebtLineDto) => {
    if (line.kind === "Invoice") void collection.collectInvoice(line)
    else void collection.collectInstallment(line)
  }

  const switchList = (next: List) => {
    setList(next)
    setPage(1)
  }

  const items = data?.items ?? []

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Type de reste à payer">
        <ListCard
          active={list === "due"}
          tone="due"
          icon={AlertCircle}
          label="À relancer"
          hint="Travail fini ou échéance passée, pas payé."
          total={data?.dueTotal}
          count={data?.dueCount}
          onSelect={() => switchList("due")}
        />
        <ListCard
          active={list === "running"}
          tone="running"
          icon={RefreshCw}
          label="En cours"
          hint="Traitement pas fini. C'est normal."
          total={data?.runningTotal}
          count={data?.runningCount}
          onSelect={() => switchList("running")}
        />
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative w-full min-w-0 sm:max-w-xs">
          <Label htmlFor="reste-a-payer-search" className="sr-only">
            Rechercher un patient dans les restes à payer
          </Label>
          <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            id="reste-a-payer-search"
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Rechercher un patient…"
            className="ps-9"
          />
        </div>
        <ModeSegmented
          value={sort}
          onChange={(value) => { setSort(value); setPage(1) }}
          options={SORT_OPTIONS}
          ariaLabel="Trier la liste"
          size="sm"
          className="sm:w-64 sm:shrink-0"
        />
      </div>

      {error ? (
        <LoadFailureNotice message={error} detail="Aucun paiement n'a été enregistré." onRetry={() => void load()} />
      ) : (
        <div className="rounded-md border bg-card">
          {loading && items.length === 0 ? (
            <ListSkeleton />
          ) : items.length === 0 ? (
            <div className="p-4">
              {term ? (
                <EmptyState
                  size="compact"
                  icon={Search}
                  title={`Aucun patient ne correspond à ${quoteFr(term)}`}
                  action={<Button variant="outline" size="sm" onClick={() => setSearch("")}>Effacer la recherche</Button>}
                />
              ) : (
                <EmptyState
                  size="compact"
                  icon={Wallet}
                  title={list === "due" ? "Personne à relancer" : "Aucun traitement en cours avec un reste à payer"}
                />
              )}
            </div>
          ) : (
            <>
              <div className={TABLE_ONLY_LG}>
                <Table>
                  <TableHeader className="sticky top-0 z-10 bg-card">
                    <TableRow>
                      <TableHead>Patient</TableHead>
                      <TableHead>{list === "due" ? "Ce qui n'est pas payé" : "Traitement"}</TableHead>
                      <TableHead className="whitespace-nowrap">{list === "due" ? "Depuis" : "Prochaine étape"}</TableHead>
                      <TableHead className="text-end">Reste à payer</TableHead>
                      <TableHead className="text-end">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {items.map((row) => (
                      <TableRow key={row.patientId}>
                        <TableCell>
                          <PatientNameLink patientId={row.patientId} name={row.patientName} />
                          {row.phoneNumber && (
                            <span className="mt-0.5 block font-mono text-2xs tabular-nums text-muted-foreground">{row.phoneNumber}</span>
                          )}
                        </TableCell>
                        <TableCell>
                          <ReasonCell row={row} list={list} />
                        </TableCell>
                        <TableCell className="whitespace-nowrap">
                          <WhenCell row={row} list={list} />
                        </TableCell>
                        <TableCell numeric>
                          <AmountCell row={row} list={list} />
                        </TableCell>
                        <TableCell className="text-end">
                          <div className="flex items-center justify-end gap-1.5">
                            <ContactActions phoneE164={row.phoneE164} name={row.patientName} />
                            <Button variant="outline" size="sm" className="coarse:h-11" onClick={() => setSelected(row)}>
                              Détail
                              <ChevronRight className="size-4" aria-hidden="true" />
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>

              <div className={cn(CARDS_ONLY_LG, "p-3")}>
                <CardList
                  items={items}
                  ariaLabel={list === "due" ? "Patients à relancer" : "Patients en cours de traitement"}
                  getKey={(row) => row.patientId}
                  title={(row) => row.patientName}
                  onSelect={setSelected}
                  accent={() => (list === "due" ? "var(--warning)" : "var(--primary)")}
                  status={(row) => <AmountCell row={row} list={list} />}
                  underTitle={(row) => <ReasonCell row={row} list={list} compact />}
                  fields={(row) => [
                    { label: list === "due" ? "Depuis" : "Prochaine étape", value: <WhenCell row={row} list={list} /> },
                    ...(row.phoneNumber
                      ? [{ label: "Téléphone", value: <span className="font-mono tabular-nums">{row.phoneNumber}</span> }]
                      : []),
                  ]}
                  primaryAction={(row) =>
                    row.phoneE164 ? (
                      <ContactActions phoneE164={row.phoneE164} name={row.patientName} variant="default" className="[&>*]:flex-1" />
                    ) : null
                  }
                />
              </div>
            </>
          )}

          {data && data.totalCount > 0 && (
            <DataTablePagination
              page={data}
              loading={loading}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size)
                setPage(1)
              }}
              label={["patient", "patients"]}
            />
          )}
        </div>
      )}

      <ResteAPayerPatientSheet
        row={selected}
        reloadKey={sheetReloadKey}
        busyDocumentId={collection.busyDocumentId}
        onClose={() => setSelected(null)}
        onCollect={collect}
      />
      {collection.dialogs}
    </div>
  )
}

/** One of the two cards that switch the list — a real radio, with the total and the patient count. */
function ListCard({
  active,
  tone,
  icon: Icon,
  label,
  hint,
  total,
  count,
  onSelect,
}: {
  active: boolean
  tone: "due" | "running"
  icon: typeof AlertCircle
  label: string
  hint: string
  total: number | undefined
  count: number | undefined
  onSelect: () => void
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      onClick={onSelect}
      className={cn(
        "@container min-h-11 rounded-xl border-2 bg-card px-4 py-3 text-start transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary",
        active && tone === "due" && "border-warning bg-warning-wash",
        active && tone === "running" && "border-primary bg-primary/10",
      )}
    >
      {/* Container query, not a viewport hinge: two cards side by side at 820 are narrower than one at 390. */}
      <span className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-3 gap-y-0.5 @md:grid-cols-[auto_minmax(0,1fr)_auto]">
        <span
          className={cn(
            "row-span-2 grid size-9 place-items-center self-start rounded-lg @md:self-center",
            tone === "due" ? "bg-warning-wash text-warning-ink" : "bg-primary/10 text-primary",
          )}
          aria-hidden="true"
        >
          <Icon className="size-4" />
        </span>
        <span className="whitespace-nowrap font-semibold">{label}</span>
        <span
          className={cn(
            "col-start-2 row-start-3 pt-1 text-lg font-bold tabular-nums @md:col-start-3 @md:row-span-2 @md:row-start-1 @md:pt-0 @md:text-end @md:text-xl",
            active && (tone === "due" ? "text-warning-ink" : "text-primary"),
          )}
        >
          {total === undefined ? "—" : formatDT(total)}
          <span className="block text-xs font-medium text-muted-foreground">
            {count === undefined ? "" : `${count.toLocaleString("fr-TN")} patient${count <= 1 ? "" : "s"}`}
          </span>
        </span>
        <span className="col-start-2 row-start-2 text-xs text-muted-foreground">{hint}</span>
      </span>
    </button>
  )
}

function ReasonCell({ row, list, compact = false }: { row: ResteAPayerRowDto; list: List; compact?: boolean }) {
  const reason = list === "due" ? row.dueReason : row.runningReason
  const document = list === "due" ? row.dueDocument : row.runningDocument
  const others = (list === "due" ? row.dueDocumentCount : row.runningDocumentCount) - 1
  const done = row.runningActsDone
  const total = row.runningActsTotal

  return (
    <div className="space-y-1">
      {reason && (
        <Badge
          variant="outline"
          className={cn(
            "whitespace-normal text-start",
            list === "due" ? "border-warning/40 bg-warning-wash text-warning-ink" : "bg-primary/10 text-primary",
          )}
        >
          {reason}
        </Badge>
      )}
      {list === "running" && done !== null && total !== null && total > 0 && !compact && (
        <div className="h-1.5 w-24 overflow-hidden rounded-full bg-muted" aria-hidden="true">
          <div className="h-full rounded-full bg-primary" style={{ width: `${(done / total) * 100}%` }} />
        </div>
      )}
      {document && (
        <p className="text-2xs text-muted-foreground">
          {document}
          {others > 0 && ` · + ${others} autre${others > 1 ? "s" : ""}`}
        </p>
      )}
    </div>
  )
}

function WhenCell({ row, list }: { row: ResteAPayerRowDto; list: List }) {
  if (list === "due") {
    if (!row.dueSince) return <span className="text-muted-foreground">—</span>
    const days = row.dueDays ?? 0
    return (
      <span className="text-xs">
        {formatDateFr(row.dueSince)}
        <span className={cn("ms-1.5 text-2xs", days >= 30 ? "font-medium text-warning-ink" : "text-muted-foreground")}>
          {days === 0 ? "aujourd'hui" : days === 1 ? "il y a 1 jour" : `il y a ${days} jours`}
        </span>
      </span>
    )
  }
  if (row.nextVisit) return <span className="text-xs">Séance le {formatDateFr(row.nextVisit)}</span>
  if (row.nextInstallmentDue) return <span className="text-xs">Échéance le {formatDateFr(row.nextInstallmentDue)}</span>
  return <span className="text-xs text-muted-foreground">Aucune séance prévue</span>
}

/** The list's own amount, then the other list's part in small — a patient may be in both. */
function AmountCell({ row, list }: { row: ResteAPayerRowDto; list: List }) {
  const main = list === "due" ? row.dueAmount : row.runningAmount
  const other = list === "due" ? row.runningAmount : row.dueAmount
  return (
    <span className="inline-block text-end">
      <span className={cn("block font-semibold tabular-nums", list === "due" ? "text-warning-ink" : "text-primary")}>
        {formatDT(main)}
      </span>
      {other > 0 && (
        <span className="block whitespace-nowrap text-2xs tabular-nums text-muted-foreground">
          + {formatDT(other)} {list === "due" ? "en cours" : "à relancer"}
        </span>
      )}
    </span>
  )
}

function ListSkeleton() {
  return (
    <div className="space-y-2 p-3" role="status" aria-label="Chargement">
      {[0, 1, 2].map((i) => (
        <div key={i} className="h-14 animate-pulse rounded-md bg-muted" />
      ))}
    </div>
  )
}

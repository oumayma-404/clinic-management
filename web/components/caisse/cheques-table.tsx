"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { AlertTriangle, CalendarClock, CalendarOff, CalendarRange, CheckCircle2, Loader2, ReceiptText, SearchX } from "lucide-react"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { ChequeBucket, ChequeDto, ChequesDueDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { ExportButton } from "@/components/ui/export-button"
import { Input } from "@/components/ui/input"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import { cn } from "@/lib/utils"

const MONEY_CHIP = zoneChipClass(ZONES.money)

/**
 * The four buckets, in urgency order. An exhaustive `Record` over the wire union, deliberately: a bucket added
 * server-side with no label here is a `tsc` error rather than a row that renders a bare English token.
 */
const BUCKET_LABELS: Record<ChequeBucket, string> = {
  Overdue: "En retard",
  DueSoon: "Bientôt",
  Later: "Plus tard",
  Undated: "Sans date",
}

/**
 * Tone per bucket. « Sans date » is a **warning**, not a neutral: it is the row nobody will chase, and painting it
 * grey is how it stays unread. « Plus tard » is genuinely neutral — a cheque dated for November is not a problem
 * in August.
 */
const BUCKET_BADGE: Record<ChequeBucket, string> = {
  Overdue: "bg-destructive-wash text-destructive",
  DueSoon: "bg-warning-wash text-warning-ink",
  Later: "bg-muted/60 text-muted-foreground",
  Undated: "bg-warning-wash text-warning-ink",
}

/**
 * « Chèques à encaisser » — every cheque the clinic holds, over both payment ledgers, soonest-due first.
 *
 * <p>The payoff of L8's cheque fields: before them a `PaymentMethod.Cheque` was a bare enum value, so
 * « quel chèque, de quelle banque, encaissable quand ? » had nowhere to live and a post-dated cheque forgotten in
 * a drawer was money simply lost.</p>
 *
 * <p>⚠️ **A cheque leaves this list only by being voided.** The product records the receipt of a cheque, not its
 * clearing at the bank — that is a column, a command and a write path this slice does not add — so the screen says
 * so out loud rather than implying that everything listed is outstanding. It is also why the four bucket figures
 * are the headline and the order is by due date: « En retard » is the actionable set.</p>
 */
export function ChequesTable() {
  const router = useRouter()
  const [data, setData] = useState<ChequesDueDto | null>(null)
  const rows = data?.items ?? []
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [reloadKey, setReloadKey] = useState(0)

  // Both ledgers feed this list, so both keys have to wake it: a cheque taken on a devis échéance next door is
  // exactly the row that must appear without a manual refresh.
  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans],
    useCallback(() => setReloadKey((k) => k + 1), []),
  )

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  useEffect(() => {
    let active = true
    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const result = await billingApi.getChequesDue({
          page,
          pageSize,
          search: debouncedSearch || undefined,
        })
        if (active) setData(result)
      } catch (e) {
        const msg = e instanceof ApiError ? e.message : "Erreur lors du chargement des chèques."
        if (active) {
          setError(msg)
          toast.error(msg)
        }
      } finally {
        if (active) setLoading(false)
      }
    }
    load()
    return () => {
      active = false
    }
  }, [reloadKey, page, pageSize, debouncedSearch])

  const groups = data?.groups
  // Read from the response, never summed from `rows`: those are one page, and these figures are the clinic's
  // whole uncashed exposure — the same rule « Créances » follows for its header total.
  const openRow = (row: ChequeDto) =>
    router.push(row.kind === "InvoicePayment" ? `/factures?invoiceId=${row.targetId}` : `/treatment-plans/${row.targetId}`)

  return (
    <>
      {/* The four buckets, on ONE surface — the same treatment la caisse and the dashboard give a related set of
          figures, so the two screens reporting cheque money do not look like two products. « En retard » leads:
          it is the only one of the four that is a problem today. */}
      <KpiGrid columns={4}>
        <ChequeBucketFigure
          label="En retard"
          hint="encaissables maintenant"
          bucket={groups?.overdue}
          tone="text-destructive"
        />
        <ChequeBucketFigure label="Bientôt" hint="sous 30 jours" bucket={groups?.dueSoon} tone="text-warning-ink" />
        <ChequeBucketFigure label="Plus tard" hint="au-delà de 30 jours" bucket={groups?.later} tone="text-foreground" />
        <ChequeBucketFigure
          label="Sans date"
          hint="aucune date d'encaissement saisie"
          bucket={groups?.undated}
          tone="text-warning-ink"
        />
      </KpiGrid>

      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center justify-between gap-3">
            <span className="flex min-w-0 items-center gap-2.5 leading-snug">
              <span aria-hidden="true" className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${MONEY_CHIP}`}>
                <ReceiptText className="size-4" strokeWidth={1.75} />
              </span>
              Chèques détenus{data && data.totalCount > 0 ? ` (${data.totalCount})` : ""}
            </span>
            {groups && groups.total.amount > 0 && (
              <span className="text-sm font-normal text-muted-foreground">
                Total&nbsp;: <span className="font-semibold text-foreground">{formatDT(groups.total.amount)}</span>
              </span>
            )}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {loading ? (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="me-2 size-5 animate-spin" /> Chargement…
            </div>
          ) : error ? (
            <div className="py-12 text-center text-sm text-destructive">{error}</div>
          ) : (
            <>
              {/* The filter it exports sits beside it, per L5's placement rule: this component owns the search
                  term (and debounces it), so a lifted copy at page level would be a second authority on what is
                  on screen. `debouncedSearch` is the value the request actually carried, not the keystroke. */}
              <div className="mb-4 flex flex-wrap items-center gap-2">
                <div className="w-full min-w-0 flex-1 sm:w-auto sm:max-w-sm">
                  <Label htmlFor="cheques-search" className="sr-only">
                    Rechercher un chèque
                  </Label>
                  <Input
                    id="cheques-search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="N° de chèque, banque, patient, référence…"
                  />
                </div>
                <ExportButton
                  path="/billing/cheques/export"
                  label="chèques"
                  compact
                  params={{ search: debouncedSearch || undefined }}
                />
              </div>

              {rows.length === 0 ? (
                debouncedSearch ? (
                  <EmptyState
                    icon={SearchX}
                    chipClassName={MONEY_CHIP}
                    title={`Aucun chèque ne correspond à « ${debouncedSearch} »`}
                    description="Le numéro et la banque restent optionnels : un chèque enregistré sans eux ne peut pas être retrouvé par ce champ."
                    secondaryAction={
                      <Button size="sm" variant="outline" onClick={() => setSearch("")}>
                        Effacer les filtres
                      </Button>
                    }
                  />
                ) : (
                  // Not an invite: there is nothing to create here, and a « Ajouter un chèque » would be an
                  // invitation to invent a payment. An empty list is simply good news.
                  <EmptyState
                    icon={CheckCircle2}
                    chipClassName={MONEY_CHIP}
                    title="Aucun chèque en attente"
                    description="Aucun règlement par chèque n'est enregistré. Les chèques apparaissent ici dès qu'un paiement de facture ou une échéance de devis est encaissé par chèque."
                  />
                )
              ) : (
                <>
                  {/* Eight columns, so the hinge is `lg:` and not `md:` — a tablet portrait is 820 px and would
                      otherwise get the desktop grid plus the 256 px rail. */}
                  <CardList
                    className={CARDS_ONLY_LG}
                    ariaLabel="Chèques à encaisser"
                    items={rows}
                    getKey={(r) => r.id}
                    title={(r) => r.chequeNumber ? `Chèque n° ${r.chequeNumber}` : "Chèque sans numéro"}
                    onSelect={openRow}
                    status={(r) => (
                      <Badge variant="secondary" className={cn("font-normal", BUCKET_BADGE[r.bucket])}>
                        {BUCKET_LABELS[r.bucket]}
                      </Badge>
                    )}
                    fields={(r) => [
                      { label: "Montant", value: <span className="font-semibold">{formatDT(r.amount)}</span> },
                      // Omitted rather than « — » (§ 6): all three cheque fields are genuinely optional.
                      r.dueDate ? { label: "Encaissable le", value: formatDateFr(r.dueDate) } : null,
                      r.bankName ? { label: "Banque", value: r.bankName } : null,
                      r.patientName ? { label: "Patient", value: r.patientName } : null,
                      r.reference ? { label: "Pièce", value: r.reference } : null,
                      { label: "Reçu le", value: formatDateFr(r.receivedOn) },
                    ]}
                  />

                  <Table containerClassName={TABLE_ONLY_LG}>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Encaissable le</TableHead>
                        <TableHead>Échéance</TableHead>
                        <TableHead>N° de chèque</TableHead>
                        <TableHead>Banque</TableHead>
                        <TableHead className="text-right">Montant</TableHead>
                        <TableHead>Patient</TableHead>
                        <TableHead>Pièce</TableHead>
                        <TableHead>Reçu le</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {rows.map((r) => (
                        <TableRow key={r.id} className="cursor-pointer" onClick={() => openRow(r)}>
                          <TableCell className="whitespace-nowrap font-medium">
                            {r.dueDate ? formatDateFr(r.dueDate) : <span className="text-muted-foreground">Non saisie</span>}
                          </TableCell>
                          <TableCell>
                            <Badge variant="secondary" className={cn("font-normal", BUCKET_BADGE[r.bucket])}>
                              {BUCKET_LABELS[r.bucket]}
                            </Badge>
                          </TableCell>
                          <TableCell className="whitespace-nowrap">
                            {r.chequeNumber ?? <span className="text-muted-foreground">—</span>}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">
                            {r.bankName ?? <span className="text-muted-foreground">—</span>}
                          </TableCell>
                          <TableCell className="text-right font-semibold tabular-nums">{formatDT(r.amount)}</TableCell>
                          <TableCell className="whitespace-nowrap">
                            {r.patientName ?? <span className="text-muted-foreground">—</span>}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">
                            {r.reference ?? <span className="text-muted-foreground">—</span>}
                          </TableCell>
                          <TableCell className="whitespace-nowrap">{formatDateFr(r.receivedOn)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </>
              )}

              {data && (
                <DataTablePagination
                  page={data}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  loading={loading}
                  label={["chèque", "chèques"]}
                />
              )}
            </>
          )}
        </CardContent>
      </Card>
    </>
  )
}

/**
 * One bucket figure. Mirrors `app/caisse/page.tsx`'s `CaisseFigure` rather than using `KpiCard`: these are not
 * dashboard KPIs (no delta, no drill-through of their own — the list below *is* the drill-through), and `bg-card`
 * is load-bearing because `KpiGrid` is a `bg-border` container showing through `gap-px`.
 */
function ChequeBucketFigure({
  label,
  hint,
  bucket,
  tone,
}: {
  label: string
  hint: string
  bucket?: { count: number; amount: number }
  tone: string
}) {
  const count = bucket?.count ?? 0
  const Icon = BUCKET_ICONS[label] ?? CalendarRange
  return (
    <div className="bg-card p-4">
      <div className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        <Icon className="size-3.5 shrink-0" strokeWidth={1.75} aria-hidden="true" />
        <span className="min-w-0 truncate">{label}</span>
      </div>
      <p className={cn("mt-1.5 text-xl font-semibold tabular-nums", count > 0 ? tone : "text-muted-foreground")}>
        {formatDT(bucket?.amount ?? 0)}
      </p>
      {/* The count is not decoration: « 1 200,000 DT en retard » is a very different problem depending on whether
          it is one cheque or eleven. */}
      <p className="mt-0.5 text-xs text-muted-foreground">
        {count === 0 ? "aucun chèque" : count === 1 ? "1 chèque" : `${count} chèques`} · {hint}
      </p>
    </div>
  )
}

const BUCKET_ICONS: Record<string, typeof CalendarRange> = {
  "En retard": AlertTriangle,
  Bientôt: CalendarClock,
  "Plus tard": CalendarRange,
  "Sans date": CalendarOff,
}

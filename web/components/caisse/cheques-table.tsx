"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { AlertTriangle, CalendarClock, CalendarOff, CalendarRange, CheckCircle2, Landmark, Loader2, ReceiptText, SearchX } from "lucide-react"
import { billingApi } from "@/lib/api/billing"
import { invoicesApi } from "@/lib/api/invoices"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { ChequeBucket, ChequeDto, ChequesDueDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
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
 * <p>⚠️ **This is a to-do list, so the default view is what the clinic still holds.** A cheque can now be marked
 * « encaissé en banque » and leaves the list; the « Encaissés » tab is where it goes, showing when and by whom.
 * Marking is **reversible** (a cheque returned unpaid is the ordinary case) and moves **no money**: la caisse
 * counts a cheque on the day it was received, not on the day it cleared, so no figure anywhere changes. The other
 * exit is a void, which removes the cheque from both views because the payment was never received at all.</p>
 *
 * <p>⚠️ **The four bucket figures always describe the outstanding set**, on both tabs — « combien me reste-t-il à
 * encaisser ? » is one question, and a header that changed meaning with the tab would be unreadable. The list
 * below them is what the tab filters.</p>
 */
export function ChequesTable() {
  const router = useRouter()
  const [data, setData] = useState<ChequesDueDto | null>(null)
  const rows = data?.items ?? []
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [banked, setBanked] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [reloadKey, setReloadKey] = useState(0)
  // The row awaiting confirmation, and whether its call is in flight. One at a time: the confirmation names the
  // cheque, so two pending rows would make « ce chèque » ambiguous.
  const [pending, setPending] = useState<ChequeDto | null>(null)
  const [marking, setMarking] = useState(false)

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

  // Both narrow the result set, so a stale page number would land on « aucun résultat » for a list that has rows.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch, banked])

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
          // Omitted rather than sent as `false`: the server treats null and false identically, and leaving it
          // off keeps the default view's URL the one a bookmark or a shared link produces.
          banked: banked || undefined,
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
  }, [reloadKey, page, pageSize, debouncedSearch, banked])

  const groups = data?.groups
  // Read from the response, never summed from `rows`: those are one page, and these figures are the clinic's
  // whole uncashed exposure — the same rule « Créances » follows for its header total.
  const openRow = (row: ChequeDto) =>
    router.push(row.kind === "InvoicePayment" ? `/factures?invoiceId=${row.targetId}` : `/treatment-plans/${row.targetId}`)

  /**
   * Which of the two routes a row is addressed by. An `InstallmentPayment` sits two levels inside the devis and
   * is only reachable as {plan, installment, payment}, which is why the DTO carries `installmentId` at all — an
   * invoice payment has none, and that absence *is* the discriminator.
   */
  const confirmMark = async () => {
    if (!pending) return
    const next = !pending.banked
    try {
      setMarking(true)
      if (pending.installmentId) {
        await treatmentPlansApi.setInstallmentPaymentBanked(
          pending.targetId,
          pending.installmentId,
          pending.id,
          next,
        )
      } else {
        await invoicesApi.setPaymentBanked(pending.targetId, pending.id, next)
      }
      toast.success(next ? "Chèque marqué encaissé en banque" : "Chèque remis dans les chèques à encaisser")
      setPending(null)
      setReloadKey((k) => k + 1)
    } catch (e) {
      // The dialog stays open with the row still named, per the UX floor: a refusal the user cannot re-read is
      // indistinguishable from nothing having happened.
      toast.error(e instanceof ApiError ? e.message : "Échec de la mise à jour du chèque.")
    } finally {
      setMarking(false)
    }
  }

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
              {/* The heading names the tab, so the figures above and the rows below cannot be read as one set. */}
              {banked ? "Chèques encaissés" : "Chèques détenus"}
              {data && data.totalCount > 0 ? ` (${data.totalCount})` : ""}
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
                {/* One segmented track, `period-selector`'s idiom: two positions of one control, not two
                    independent buttons. It is also the whole of § 13's « an active filter is visible » here —
                    the control *is* the statement of which set is on screen, at every width. */}
                <div
                  role="group"
                  aria-label="Chèques à afficher"
                  className="inline-flex w-full gap-0.5 rounded-full border bg-card p-0.5 shadow-sm sm:w-auto"
                >
                  {BANKED_TABS.map((tab) => (
                    <button
                      key={tab.label}
                      type="button"
                      aria-pressed={banked === tab.banked}
                      disabled={loading}
                      onClick={() => setBanked(tab.banked)}
                      className={cn(
                        // Plain `<button>`s, so they inherit no touch floor of their own — and they are
                        // adjacent, which is the case `.touch-target` steals taps in. Grow the row instead.
                        "flex-1 rounded-full px-3.5 py-1.5 text-sm transition-colors duration-150 coarse:min-h-11 coarse:px-4 sm:flex-none",
                        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:ring-offset-card",
                        "disabled:cursor-not-allowed disabled:opacity-60 motion-reduce:transition-none",
                        banked === tab.banked
                          ? "bg-primary font-semibold text-primary-foreground"
                          : "text-muted-foreground hover-hover:hover:text-foreground",
                      )}
                    >
                      {tab.label}
                    </button>
                  ))}
                </div>

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
                  // The export re-sends the screen's own query, so the tab travels with it: a file taken from
                  // « Encaissés » must hold those rows and not the to-do list the default would produce.
                  params={{ search: debouncedSearch || undefined, banked: banked || undefined }}
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
                ) : banked ? (
                  // A distinct sentence per tab: « aucun chèque en attente » on the Encaissés tab would read as
                  // « nothing left to bank », which is the opposite of what an empty banked list means.
                  <EmptyState
                    icon={Landmark}
                    chipClassName={MONEY_CHIP}
                    title="Aucun chèque marqué encaissé"
                    description="Aucun chèque n'a encore été porté en banque dans le logiciel. Marquez un chèque depuis « À encaisser » une fois qu'il est déposé."
                    secondaryAction={
                      <Button size="sm" variant="outline" onClick={() => setBanked(false)}>
                        Voir les chèques à encaisser
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
                    description="Aucun règlement par chèque n'est en attente d'encaissement. Les chèques apparaissent ici dès qu'un paiement de facture ou une échéance de devis est réglé par chèque."
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
                      r.bankedOn ? { label: "Porté en banque le", value: bankedSummary(r) } : null,
                    ]}
                    // `primaryAction`, not the actions menu: this screen exists to be worked through, and it is
                    // the one action a user opens it to perform. It also gives the verb its own 44 px row
                    // instead of crushing the cheque number at 320 px.
                    primaryAction={(r) => (
                      <Button
                        variant={r.banked ? "outline" : "default"}
                        className="w-full coarse:h-11"
                        onClick={() => setPending(r)}
                      >
                        <Landmark className="me-2 size-4" strokeWidth={1.75} aria-hidden="true" />
                        {r.banked ? "Annuler l'encaissement" : "Marquer encaissé"}
                      </Button>
                    )}
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
                        {/* Only on the tab where it has a value — an always-present column that is empty for
                            every row on the default tab is a column that teaches nothing. */}
                        {banked && <TableHead>Porté en banque</TableHead>}
                        <TableHead className="text-end">Action</TableHead>
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
                          {banked && (
                            <TableCell className="whitespace-nowrap text-muted-foreground">
                              {bankedSummary(r) ?? <span className="text-muted-foreground">—</span>}
                            </TableCell>
                          )}
                          <TableCell className="text-end">
                            {/* `stopPropagation`: the row itself opens the invoice or the devis, and a mark that
                                also navigated away would leave the user unable to work down the list. */}
                            <Button
                              size="sm"
                              variant={r.banked ? "outline" : "secondary"}
                              className="coarse:h-11"
                              onClick={(e) => {
                                e.stopPropagation()
                                setPending(r)
                              }}
                            >
                              {r.banked ? "Annuler" : "Marquer encaissé"}
                            </Button>
                          </TableCell>
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

      {/* `AlertDialogContent` is already a bottom sheet sized in `dvh` below `md:` — the presentation lives in
          the primitive, so nothing is overridden here. */}
      <AlertDialog open={pending !== null} onOpenChange={(open) => !open && setPending(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pending?.banked ? "Remettre ce chèque à encaisser ?" : "Marquer ce chèque encaissé ?"}
            </AlertDialogTitle>
            {/* The dialog names the cheque it is about — with three cheques of the same amount on screen, « ce
                chèque » cannot say which one. */}
            <AlertDialogDescription>
              {pending && (
                <>
                  {pending.chequeNumber ? `Chèque n° ${pending.chequeNumber}` : "Chèque sans numéro"}
                  {pending.bankName ? ` · ${pending.bankName}` : ""} · {formatDT(pending.amount)}
                  {pending.patientName ? ` · ${pending.patientName}` : ""}.{" "}
                  {pending.banked
                    ? "Il repassera dans « À encaisser ». À utiliser si la banque vous l'a retourné impayé."
                    : "Il quittera la liste des chèques à encaisser et sera classé dans « Encaissés »."}{" "}
                  {/* Stated every time, because it is the one thing the action does NOT do and the wording
                      « encaissé » invites the opposite assumption. */}
                  Aucun montant n&apos;est modifié : la caisse compte un chèque au jour où il a été reçu.
                </>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={marking}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={marking}
              onClick={(e) => {
                // The primitive closes on click; the dialog has to stay up until the call settles so a refusal
                // can be read against the row it names.
                e.preventDefault()
                void confirmMark()
              }}
            >
              {marking
                ? "Enregistrement…"
                : pending?.banked
                  ? "Remettre à encaisser"
                  : "Marquer encaissé"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}

/**
 * The two positions of the one filter. An exhaustive pair rather than a boolean at the call site, so the labels
 * and the value they select cannot drift apart.
 */
const BANKED_TABS = [
  { label: "À encaisser", banked: false },
  { label: "Encaissés", banked: true },
] as const

/**
 * « le 12/09/2026 par Dr Ben Salah », or just the date when no actor was resolved — a name is best-effort, since
 * a missing user must never have blocked the mark. Returns null when the cheque is still held, so the field is
 * omitted rather than printed as « — ».
 */
function bankedSummary(row: ChequeDto): string | null {
  if (!row.bankedOn) return null
  return row.bankedByName
    ? `${formatDateFr(row.bankedOn)} par ${row.bankedByName}`
    : formatDateFr(row.bankedOn)
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

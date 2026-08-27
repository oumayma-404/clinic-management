"use client"

import { Receipt } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Card, CardContent } from "@/components/ui/card"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { statusToneClass } from "@/components/ui/status-tone"
import { formatCalendarDay, formatDate, formatDT } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { SubscriptionHistoryPageDto, SubscriptionPeriodDto } from "@/lib/api/subscription"

/**
 * « Historique des paiements » — what the cabinet has paid its software vendor (AC-2.3).
 *
 * <p><b>Cards up to `lg:`, a table above it</b>, not the usual `md:` hinge: seven columns beside the 256 px rail
 * leaves ~532 px on the tablet portrait this product is used on most, and every cell in `ui/table.tsx` is
 * `whitespace-nowrap`, so the table cannot compress — it would simply scroll sideways with the amount off screen.</p>
 *
 * <p><b>A cancelled entry is marked in words as well as struck through</b> (AC-5.5). Strike-through alone is a
 * visual convention that disappears in greyscale, at 200 % zoom with a low-quality display, and for a screen
 * reader entirely — and the fact it carries is that this payment no longer counts toward the end date.</p>
 */
export function SubscriptionHistoryTable({
  data,
  loading,
  error,
  onRetry,
  onPageChange,
  onPageSizeChange,
}: {
  data: SubscriptionHistoryPageDto | null
  loading: boolean
  /** Set when the read failed. Rendered as a retry notice — **never** as « aucun paiement ». */
  error: string | null
  onRetry: () => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}) {
  if (error) {
    return (
      <LoadFailureNotice
        message="L'historique des paiements n'a pas pu être chargé."
        detail="Votre abonnement lui-même reste affiché ci-dessus."
        onRetry={onRetry}
      />
    )
  }

  // A skeleton distinct from empty: a card list has no header row, so « vide » and « en cours » would otherwise be
  // the same blank rectangle. ⚠️ The card half goes through `CardList`'s own `loading`, which carries `role="status"`,
  // an `aria-label` and `aria-busy` — a hand-rolled copy beside it gave a screen-reader user silence for the whole
  // fetch, and would not receive the next fix made to the shared one.
  if (loading && !data) {
    return (
      <div className="space-y-4">
        <div className={TABLE_ONLY_LG}>
          <Card>
            <CardContent className="space-y-3 py-6">
              {Array.from({ length: 3 }).map((_, i) => (
                <div key={i} className="h-12 animate-pulse rounded-md bg-muted/60" />
              ))}
            </CardContent>
          </Card>
        </div>
        <div className={CARDS_ONLY_LG}>
          <CardList
            items={[] as SubscriptionPeriodDto[]}
            loading
            skeletonRows={3}
            ariaLabel="Historique des paiements d'abonnement"
            getKey={(e) => e.id}
            title={(e) => e.id}
            fields={() => []}
          />
        </div>
      </div>
    )
  }

  const rows = data?.items ?? []

  if (rows.length === 0) {
    return (
      <EmptyState
        icon={Receipt}
        size="compact"
        title="Aucun paiement enregistré"
        description="Les paiements d'abonnement apparaissent ici dès qu'ils sont enregistrés."
      />
    )
  }

  return (
    <div className="space-y-4">
      {/* Two trees, not a `display:block` reflow: the reflow strips the implicit table roles, and a screen reader
          would read « Paiement 290,000 Virement 12/03 » with no idea which figure is the money. */}
      <div className={TABLE_ONLY_LG}>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Enregistré le</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>Période couverte</TableHead>
              {/* Right-aligned to match the cell: the amounts are the one column compared vertically. */}
              <TableHead className="text-right">Montant</TableHead>
              <TableHead>Mode</TableHead>
              <TableHead>Référence</TableHead>
              <TableHead>État</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((entry) => (
              <TableRow key={entry.id} className={entry.isCancelled ? "opacity-60" : undefined}>
                <TableCell className="text-muted-foreground">{formatDate(entry.recordedAt)}</TableCell>
                <TableCell>{entry.kindLabel}</TableCell>
                <TableCell className={cn(entry.isCancelled && "line-through")}>{coveredPeriod(entry)}</TableCell>
                {/* `numeric` rather than a hand-written `tabular-nums`: it also right-aligns, which is what makes
                    the decimal commas line up — the same rule /factures and la caisse follow. */}
                <TableCell numeric className={cn(entry.isCancelled && "line-through")}>
                  {entry.amount === null ? "—" : formatDT(entry.amount)}
                </TableCell>
                <TableCell>{entry.methodLabel ?? "—"}</TableCell>
                <TableCell className="text-muted-foreground">{entry.reference ?? "—"}</TableCell>
                <TableCell>
                  <StateBadge entry={entry} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <div className={CARDS_ONLY_LG}>
        <CardList
          items={rows}
          ariaLabel="Historique des paiements d'abonnement"
          getKey={(e) => e.id}
          // Identity first: the period covered is what an owner scans for — « ai-je payé jusqu'en septembre ? ».
          title={(e) => <span className={cn(e.isCancelled && "line-through")}>{coveredPeriod(e)}</span>}
          subtitle={(e) => e.kindLabel}
          status={(e) => <StateBadge entry={e} />}
          muted={(e) => e.isCancelled}
          fields={(e) => [
            { label: "Montant", value: e.amount === null ? null : formatDT(e.amount) },
            { label: "Mode", value: e.methodLabel },
            { label: "Référence", value: e.reference },
            { label: "Enregistré le", value: formatDate(e.recordedAt) },
            { label: "Motif d'annulation", value: e.cancelReason },
          ]}
        />
      </div>

      {data && (
        <DataTablePagination
          page={data}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
          loading={loading}
          label={["paiement", "paiements"]}
        />
      )}
    </div>
  )
}

/**
 * « Annulé » in words, or the entry's nature. A cancelled entry's reason is its own field in both trees, because the
 * end date may have moved into the past because of it (EC-4) and « pourquoi ? » has to be answerable on the screen.
 */
function StateBadge({ entry }: { entry: SubscriptionPeriodDto }) {
  if (entry.isCancelled) {
    return (
      <Badge className={statusToneClass("negative")} variant="secondary">
        Annulé
      </Badge>
    )
  }

  return (
    <Badge className={statusToneClass(entry.throughDay === null ? "accepted" : "positive")} variant="secondary">
      {entry.throughDay === null ? "Sans échéance" : "Pris en compte"}
    </Badge>
  )
}

/**
 * The stretch this entry covered, in French.
 *
 * Three distinct cases and none of them may be rendered as a date range with a hole in it: a **cancelled** entry
 * covers nothing at all, an **open-ended** one has a start and no end (AC-2.5's « in words » rule applies here too),
 * and a dated one has both.
 */
function coveredPeriod(entry: SubscriptionPeriodDto): string {
  if (entry.isCancelled || entry.fromDay === null) {
    return "Aucune période"
  }

  return entry.throughDay === null
    ? `Depuis le ${formatCalendarDay(entry.fromDay)} — sans échéance`
    : `${formatCalendarDay(entry.fromDay)} → ${formatCalendarDay(entry.throughDay)}`
}

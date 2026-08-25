"use client"

import { useCallback, useEffect, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { Loader2, HandCoins, SearchX } from "lucide-react"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { ReceivableDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { ExportButton } from "@/components/ui/export-button"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import type { ReceivablesPageDto } from "@/lib/api/types"

/** « Créances » is the Finances zone — the empty state's chip wears the hue the rail and the eyebrow already do. */
const MONEY_CHIP = zoneChipClass(ZONES.money)

export function ReceivablesTable() {
  const router = useRouter()
  const [data, setData] = useState<ReceivablesPageDto | null>(null)
  const rows = data?.items ?? []
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  // Bumped by a realtime event to re-run the load below. « Créances » is a debt list a colleague settles
  // from another screen; without this it kept showing balances that had already been paid.
  const [reloadKey, setReloadKey] = useState(0)

  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans],
    useCallback(() => setReloadKey((k) => k + 1), []),
  )

  // Debounced so a search does not fire a request per keystroke. Hand-rolled rather than via `usePagedList`
  // because this endpoint returns a wrapper (page + the clinic-wide `totalOutstanding`), not a bare page.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new term must not leave the table on a page the narrowed result set no longer has.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  useEffect(() => {
    let active = true
    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const data = await billingApi.getReceivablesPaged({
          page,
          pageSize,
          search: debouncedSearch || undefined,
        })
        if (active) setData(data)
      } catch (e) {
        const msg = e instanceof ApiError ? e.message : "Erreur lors du chargement des créances."
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

  // Read from the response, NOT summed from `rows`: the rows are one page, and « Total dû » is the clinic's
  // receivables. Summing what is on screen would report the page's total as the clinic's.
  const total = data?.totalOutstanding ?? 0

  return (
    <Card>
      <CardHeader>
        {/*
          The icon chip — `app/documents/page.tsx`'s template-tile idiom, sized for a header. The glyph was
          `text-muted-foreground` beside foreground text: a grey mark next to black text is not an icon, it is a
          faded word, and 43 headers were drawn that way. The hue is the `money` zone's rather than `primary`,
          because « Créances » is a Finances screen and the rail and the page eyebrow already paint it amber.

          ⚠️ `flex-wrap` + `gap-3` is not cosmetic: this row is `justify-between` with « Total dû : 12 345,000
          DT » on the right, and the chip takes ~40px out of the left column. At 390px the two halves already
          collided before the chip existed — without wrapping, the amount is what gets squeezed, and it is the
          number the page is for.
        */}
        <CardTitle className="flex flex-wrap items-center justify-between gap-3">
          <span className="flex min-w-0 items-center gap-2.5 leading-snug">
            <span
              aria-hidden="true"
              className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.money)}`}
            >
              <HandCoins className="size-4" strokeWidth={1.75} />
            </span>
            Créances{data && data.totalCount > 0 ? ` (${data.totalCount})` : ""}
          </span>
          {total > 0 && (
            <span className="text-sm font-normal text-muted-foreground">
              Total dû : <span className="font-semibold text-foreground">{formatDT(total)}</span>
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="flex items-center justify-center py-12 text-muted-foreground">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Chargement…
          </div>
        ) : error ? (
          <div className="py-12 text-center text-sm text-destructive">{error}</div>
        ) : (
          <>
            {/*
              L5 — « Exporter » sits BESIDE the filter it exports, not up in the page header, and that is the
              deliberate half of the decision. This component owns the search term (and debounces it), so a
              button in `PageHeader` would need a lifted copy of that state — a second authority on « what is on
              screen », and the one property the file must have is that it *is* the list. Reading
              `debouncedSearch` — the value the request actually carried, not the keystroke — is the same rule.

              At 320 px the input takes its own row and the button wraps beneath it (`flex-wrap` + `w-full`).
            */}
            <div className="mb-4 flex flex-wrap items-center gap-2">
              <div className="w-full min-w-0 flex-1 sm:w-auto sm:max-w-sm">
                <Label htmlFor="receivables-search" className="sr-only">
                  Rechercher un patient
                </Label>
                <Input
                  id="receivables-search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Rechercher un patient…"
                />
              </div>
              <ExportButton
                path="/billing/receivables/export"
                label="créances"
                compact
                params={{ search: debouncedSearch || undefined }}
              />
            </div>
            {/*
              Two emptinesses that must not share copy. « Aucune créance » is GOOD news and therefore has no
              action — there is nothing to create on a debt list, and offering one would be an invitation to
              invent a debt. The filtered case is recoverable and says how.
            */}
            {rows.length === 0 ? (
              debouncedSearch ? (
                <EmptyState
                  icon={SearchX}
                  chipClassName={MONEY_CHIP}
                  title={`Aucun patient ne correspond à « ${debouncedSearch} »`}
                  description="Le patient existe peut-être sans rien devoir : cette liste ne montre que les soldes dus."
                  secondaryAction={
                    <Button size="sm" variant="outline" onClick={() => setSearch("")}>
                      Effacer les filtres
                    </Button>
                  }
                />
              ) : (
                <EmptyState
                  icon={HandCoins}
                  chipClassName={MONEY_CHIP}
                  title="Aucune créance"
                  description="Tous les patients sont à jour : aucune facture ni échéance n'attend de règlement."
                />
              )
            ) : (
              <>
          {/*
            Below `md:` the table is replaced, not reflowed (AC-13/AC-14). This surface has **no action cell** —
            the row itself is the navigation — so the card is a link rather than a card with a menu.
          */}
          <CardList
            className={CARDS_ONLY}
            ariaLabel="Créances par patient"
            items={rows}
            getKey={(r) => r.patientId}
            title={(r) => r.patientName}
            href={(r) => `/patients/${r.patientId}`}
            status={(r) =>
              r.daysOverdue != null && r.daysOverdue > 0 ? (
                <Badge variant="destructive">En retard · {r.daysOverdue} j</Badge>
              ) : null
            }
            fields={(r) => [
              { label: "Solde dû", value: <span className="font-semibold">{formatDT(r.totalOutstanding)}</span> },
              // Omitted rather than « — » when the patient owes nothing overdue (AC-17).
              r.oldestOverdueDate ? { label: "Échéance la plus ancienne", value: formatDateFr(r.oldestOverdueDate) } : null,
            ]}
          />

          <Table containerClassName={TABLE_ONLY}>
            <TableHeader>
              <TableRow>
                <TableHead>Patient</TableHead>
                <TableHead className="text-right">Solde dû</TableHead>
                <TableHead>Échéance la plus ancienne</TableHead>
                <TableHead className="text-right">Retard</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow
                  key={r.patientId}
                  className="cursor-pointer"
                  onClick={() => router.push(`/patients/${r.patientId}`)}
                >
                  <TableCell className="font-medium">{r.patientName}</TableCell>
                  <TableCell className="text-right font-semibold">{formatDT(r.totalOutstanding)}</TableCell>
                  <TableCell className="whitespace-nowrap">{r.oldestOverdueDate ? formatDateFr(r.oldestOverdueDate) : "—"}</TableCell>
                  <TableCell className="text-right">
                    {r.daysOverdue != null && r.daysOverdue > 0 ? (
                      <Badge variant="destructive">En retard · {r.daysOverdue} j</Badge>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
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
                label={["créance", "créances"]}
              />
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}

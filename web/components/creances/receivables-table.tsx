"use client"

import { useCallback, useEffect, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { Loader2, HandCoins } from "lucide-react"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { ReceivableDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import type { ReceivablesPageDto } from "@/lib/api/types"

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
        <CardTitle className="flex items-center justify-between">
          <span className="flex items-center gap-2">
            <HandCoins className="h-5 w-5 text-muted-foreground" />
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
            <div className="mb-4">
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
            {rows.length === 0 ? (
              <div className="py-12 text-center text-sm text-muted-foreground">
                {debouncedSearch
                  ? "Aucun patient ne correspond à votre recherche."
                  : "Aucune créance — tous les patients sont à jour."}
              </div>
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
                  <TableCell>{r.oldestOverdueDate ? formatDateFr(r.oldestOverdueDate) : "—"}</TableCell>
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

"use client"

import { useState, useEffect, useCallback } from "react"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { invoicesApi } from "@/lib/api/invoices"
import type { InvoiceRevenueDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { AlertTriangle, Loader2 } from "lucide-react"
import { INVOICE_STATUS_LABELS } from "@/components/factures/invoice-labels"

const ALL_STATUSES = "all"

/**
 * One KPI figure, with the three states kept apart (AC-P3.28): still loading, failed to load, or a real
 * amount. « — » is reserved for a figure that genuinely has no value — a failed read says « indisponible »
 * so nobody reads a network error as "nothing was billed this month".
 */
function RevenueValue({ loading, failed, value }: { loading: boolean; failed: boolean; value?: number }) {
  if (loading) {
    return (
      <span className="inline-flex items-center gap-2 text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        <span className="sr-only">Chargement…</span>
      </span>
    )
  }
  if (failed) {
    return <span className="text-base font-medium text-muted-foreground">Indisponible</span>
  }
  return <>{value === undefined ? "—" : formatDT(value)}</>
}

export default function FacturesPage() {
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  // Dashboard drill-through (« Encaissé » / « Facturé »): ?from=&to=&status= pre-applies the filters so the table and
  // the revenue KPIs describe the same window the card counted. window.location in an effect rather than
  // useSearchParams — the repo's idiom, and it keeps this page out of a Suspense boundary.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlFrom = params.get("from")
    const urlTo = params.get("to")
    const urlStatus = params.get("status")
    // A malformed date or an unknown status is ignored, not refused — a stale link lands on the unfiltered list.
    if (urlFrom && !Number.isNaN(Date.parse(urlFrom))) setFrom(urlFrom)
    if (urlTo && !Number.isNaN(Date.parse(urlTo))) setTo(urlTo)
    if (urlStatus && urlStatus in INVOICE_STATUS_LABELS) setStatus(urlStatus)
  }, [])

  const [revenue, setRevenue] = useState<InvoiceRevenueDto | null>(null)
  const [revenueLoading, setRevenueLoading] = useState(true)
  // AC-P3.28 — the revenue read used to swallow its error without even a console.error, so a failed call and
  // a genuinely-empty period both rendered « — ». On a money screen those must not look alike.
  const [revenueError, setRevenueError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const fromIso = from ? `${from}T00:00:00` : undefined
  const toIso = to ? `${to}T23:59:59` : undefined
  const statusFilter = status === ALL_STATUSES ? undefined : status

  const loadRevenue = useCallback(async () => {
    try {
      setRevenueLoading(true)
      setRevenueError(null)
      const data = await invoicesApi.revenue({ from: fromIso, to: toIso })
      setRevenue(data)
    } catch (err) {
      setRevenue(null)
      setRevenueError(getErrorMessage(err, "Les recettes n'ont pas pu être chargées."))
    } finally {
      setRevenueLoading(false)
    }
  }, [fromIso, toIso])

  useEffect(() => {
    loadRevenue()
  }, [loadRevenue, reloadKey])

  const applyFilters = () => setReloadKey((k) => k + 1)
  const resetFilters = () => {
    setFrom("")
    setTo("")
    setStatus(ALL_STATUSES)
    setReloadKey((k) => k + 1)
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex-1 overflow-auto p-4 md:p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              <PageHeader
                zone="Argent"
                title="Factures &amp; recettes"
                subtitle="Notes d'honoraires, encaissements et suivi des recettes."
              />

              {/* Revenue summary. AC-P3.28 — three states, never conflated: loading, failed-to-load (with a
                  retry), and a real figure. */}
              {revenueError && (
                <div
                  role="status"
                  className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm"
                >
                  <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
                  <span className="flex-1 min-w-0">{revenueError}</span>
                  <Button size="sm" variant="outline" onClick={() => void loadRevenue()}>
                    Réessayer
                  </Button>
                </div>
              )}
              <div className="grid gap-4 sm:grid-cols-3">
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Total facturé</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold">
                      <RevenueValue
                        loading={revenueLoading}
                        failed={!!revenueError}
                        value={revenue?.totalInvoiced}
                      />
                    </div>
                  </CardContent>
                </Card>
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Total encaissé</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold text-green-700 dark:text-green-400">
                      <RevenueValue
                        loading={revenueLoading}
                        failed={!!revenueError}
                        value={revenue?.totalCollected}
                      />
                    </div>
                  </CardContent>
                </Card>
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Reste à recouvrer</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold text-amber-700 dark:text-amber-400">
                      <RevenueValue
                        loading={revenueLoading}
                        failed={!!revenueError}
                        value={revenue?.outstanding}
                      />
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Filters */}
              <Card>
                <CardContent className="flex flex-wrap items-end gap-4 pt-6">
                  <div className="space-y-1.5">
                    <Label htmlFor="from">Du</Label>
                    <Input id="from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="to">Au</Label>
                    <Input id="to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="status">Statut</Label>
                    <Select value={status} onValueChange={setStatus}>
                      <SelectTrigger id="status" className="w-48">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={ALL_STATUSES}>Tous</SelectItem>
                        {Object.entries(INVOICE_STATUS_LABELS).map(([value, label]) => (
                          <SelectItem key={value} value={value}>
                            {label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="flex gap-2">
                    <Button onClick={applyFilters}>Filtrer</Button>
                    <Button variant="outline" onClick={resetFilters}>Réinitialiser</Button>
                  </div>
                </CardContent>
              </Card>

              <InvoicesTable
                from={fromIso}
                to={toIso}
                status={statusFilter}
                reloadKey={reloadKey}
                onChanged={loadRevenue}
              />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

"use client"

import { useState, useEffect, useCallback } from "react"
import { ClinicGuard } from "@/components/clinic-guard"
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
import { INVOICE_STATUS_LABELS } from "@/components/factures/invoice-labels"

const ALL_STATUSES = "all"

export default function FacturesPage() {
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  const [revenue, setRevenue] = useState<InvoiceRevenueDto | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const fromIso = from ? `${from}T00:00:00` : undefined
  const toIso = to ? `${to}T23:59:59` : undefined
  const statusFilter = status === ALL_STATUSES ? undefined : status

  const loadRevenue = useCallback(async () => {
    try {
      const data = await invoicesApi.revenue({ from: fromIso, to: toIso })
      setRevenue(data)
    } catch {
      setRevenue(null)
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
          <main className="flex-1 overflow-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              <div>
                <h1 className="text-2xl font-bold">Factures &amp; Recettes</h1>
                <p className="text-muted-foreground">Notes d'honoraires, encaissements et suivi des recettes.</p>
              </div>

              {/* Revenue summary */}
              <div className="grid gap-4 sm:grid-cols-3">
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Total facturé</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold">{revenue ? formatDT(revenue.totalInvoiced) : "—"}</div>
                  </CardContent>
                </Card>
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Total encaissé</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold text-green-700 dark:text-green-400">
                      {revenue ? formatDT(revenue.totalCollected) : "—"}
                    </div>
                  </CardContent>
                </Card>
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm font-medium text-muted-foreground">Reste à recouvrer</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="text-2xl font-bold text-amber-700 dark:text-amber-400">
                      {revenue ? formatDT(revenue.outstanding) : "—"}
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

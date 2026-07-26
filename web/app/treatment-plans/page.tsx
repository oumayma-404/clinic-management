"use client"

import { useState } from "react"
import { ClinicGuard } from "@/components/clinic-guard"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PLAN_STATUS_LABELS } from "@/components/treatment-plans/treatment-plan-labels"

const ALL_STATUSES = "all"

export default function TreatmentPlansPage() {
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  const [reloadKey, setReloadKey] = useState(0)

  // Send UTC instants, not timezone-naive wall-clock strings: the backend compares these against a UTC
  // CreatedAt, so `${from}T00:00:00` silently shifted the range by the browser's offset and dropped (or
  // added) plans created near either edge of the window.
  const fromIso = from ? new Date(`${from}T00:00:00`).toISOString() : undefined
  const toIso = to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined
  const statusFilter = status === ALL_STATUSES ? undefined : status

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
                <h1 className="text-2xl font-bold">Plans de traitement &amp; Devis</h1>
                <p className="text-muted-foreground">Plans de soins, devis et échéanciers de paiement.</p>
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
                        {Object.entries(PLAN_STATUS_LABELS).map(([value, label]) => (
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

              <TreatmentPlansTable
                from={fromIso}
                to={toIso}
                status={statusFilter}
                reloadKey={reloadKey}
              />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

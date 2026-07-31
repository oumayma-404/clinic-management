"use client"

import { useEffect, useState } from "react"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { AppShell } from "@/components/app-shell"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PLAN_STATUS_LABELS } from "@/components/treatment-plans/treatment-plan-labels"
import { formatDateFr } from "@/lib/format"

const ALL_STATUSES = "all"

export default function TreatmentPlansPage() {
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  const [reloadKey, setReloadKey] = useState(0)
  // The ACCEPTANCE-date window, distinct from from/to above (which bound creation). The dashboard's « Devis acceptés »
  // counts by acceptedDate, so its drill-through has to filter by the same date or the list would not contain the
  // devis the card counted.
  const [acceptedFrom, setAcceptedFrom] = useState("")
  const [acceptedTo, setAcceptedTo] = useState("")

  // Dashboard drill-throughs: ?status= (« Devis en attente de réponse ») and ?acceptedFrom/?acceptedTo
  // (« Devis acceptés »). window.location in an effect rather than useSearchParams — the repo's idiom, and it keeps
  // this page out of a Suspense boundary. Unknown values are ignored, so a stale link lands on the full list.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlStatus = params.get("status")
    const urlAcceptedFrom = params.get("acceptedFrom")
    const urlAcceptedTo = params.get("acceptedTo")
    if (urlStatus && urlStatus in PLAN_STATUS_LABELS) setStatus(urlStatus)
    if (urlAcceptedFrom && !Number.isNaN(Date.parse(urlAcceptedFrom))) setAcceptedFrom(urlAcceptedFrom)
    if (urlAcceptedTo && !Number.isNaN(Date.parse(urlAcceptedTo))) setAcceptedTo(urlAcceptedTo)
  }, [])

  // Send UTC instants, not timezone-naive wall-clock strings: the backend compares these against a UTC
  // CreatedAt, so `${from}T00:00:00` silently shifted the range by the browser's offset and dropped (or
  // added) plans created near either edge of the window.
  const fromIso = from ? new Date(`${from}T00:00:00`).toISOString() : undefined
  const toIso = to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined
  const statusFilter = status === ALL_STATUSES ? undefined : status
  // Same UTC-instant treatment as from/to above, for the same reason: AcceptedDate is compared as UTC server-side.
  const acceptedFromIso = acceptedFrom ? new Date(`${acceptedFrom}T00:00:00`).toISOString() : undefined
  const acceptedToIso = acceptedTo ? new Date(`${acceptedTo}T23:59:59.999`).toISOString() : undefined
  const hasAcceptedWindow = Boolean(acceptedFrom || acceptedTo)

  const applyFilters = () => setReloadKey((k) => k + 1)
  const resetFilters = () => {
    setFrom("")
    setTo("")
    setStatus(ALL_STATUSES)
    setAcceptedFrom("")
    setAcceptedTo("")
    const url = new URL(window.location.href)
    url.search = ""
    window.history.replaceState({}, "", url)
    setReloadKey((k) => k + 1)
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          zone="Argent"
          title="Plans de traitement &amp; devis"
          subtitle="Plans de soins, devis et échéanciers de paiement."
        />

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

        {/* The acceptance window has no control of its own (it arrives by link), so it is stated explicitly —
            an invisible filter is how a user concludes their devis have disappeared. */}
        {hasAcceptedWindow && (
          <div role="status" className="flex flex-wrap items-center gap-3 rounded-lg border bg-muted/40 p-3 text-sm">
            <span className="min-w-0 flex-1">
              Devis acceptés {acceptedFrom ? `du ${formatDateFr(acceptedFrom)}` : ""}
              {acceptedTo ? ` au ${formatDateFr(acceptedTo)}` : ""}
            </span>
            <Button size="sm" variant="outline" onClick={resetFilters}>
              Afficher tous les devis
            </Button>
          </div>
        )}

        <TreatmentPlansTable
          from={fromIso}
          to={toIso}
          status={statusFilter}
          acceptedFrom={acceptedFromIso}
          acceptedTo={acceptedToIso}
          reloadKey={reloadKey}
        />
      </AppShell>
    </ClinicGuard>
  )
}

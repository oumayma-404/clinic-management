"use client"

import { useEffect, useState } from "react"
import { addDays, endOfMonth, startOfMonth, startOfWeek } from "date-fns"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ExportButton } from "@/components/ui/export-button"
import { AppShell } from "@/components/app-shell"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PLAN_STATUS_LABELS } from "@/components/treatment-plans/treatment-plan-labels"
import { formatDateFr, toLocalIso } from "@/lib/format"

const ALL_STATUSES = "all"

/**
 * The creation-date window, as a preset rather than two date inputs.
 *
 * <p>Two `<input type="date">` fields plus a statut Select plus two buttons wrapped to three or four stacked rows
 * below `sm:`, so on a phone the filter card was taller than the first two rows of the table it filters — and the
 * dates were the part nobody edits. A preset carries the common answers and only reveals the inputs for
 * « Personnalisé », which is also what lets the page open on **this week** without asking anyone to type it.</p>
 */
type PeriodKey = "week" | "month" | "all" | "custom"

const PERIOD_LABELS: Record<PeriodKey, string> = {
  week: "Cette semaine",
  month: "Ce mois",
  all: "Toutes les dates",
  custom: "Personnalisé",
}

const DEFAULT_PERIOD: PeriodKey = "week"

/**
 * The calendar days a preset covers, or `null` for « Toutes les dates » / « Personnalisé ».
 *
 * <p>The week is **Monday-based**, matching the agenda's `weekStartsOn: 1` and the backend's
 * `DashboardPeriod.ResolveWeek` — « cette semaine » must mean the same seven days wherever the product says it.
 * Built from the local-calendar accessors via `toLocalIso`, never `toISOString().slice(0, 10)`: the latter
 * converts to UTC first and so returns the *previous* day for the first hour of every Tunisian day.</p>
 */
function presetRange(period: PeriodKey): { from: string; to: string } | null {
  const now = new Date()
  if (period === "week") {
    const start = startOfWeek(now, { weekStartsOn: 1 })
    return { from: toLocalIso(start), to: toLocalIso(addDays(start, 6)) }
  }
  if (period === "month") {
    return { from: toLocalIso(startOfMonth(now)), to: toLocalIso(endOfMonth(now)) }
  }
  return null
}

export default function TreatmentPlansPage() {
  const [period, setPeriod] = useState<PeriodKey>(DEFAULT_PERIOD)
  // Only read when `period === "custom"`; kept across a preset switch so returning to « Personnalisé » does not
  // lose what was typed.
  const [customFrom, setCustomFrom] = useState("")
  const [customTo, setCustomTo] = useState("")
  const [status, setStatus] = useState<string>(ALL_STATUSES)
  // The ACCEPTANCE-date window, distinct from the creation window above. The dashboard's « Devis acceptés »
  // counts by acceptedDate, so its drill-through has to filter by the same date or the list would not contain the
  // devis the card counted.
  const [acceptedFrom, setAcceptedFrom] = useState("")
  const [acceptedTo, setAcceptedTo] = useState("")

  /*
   * Dashboard drill-throughs: ?status= (« Devis en attente de réponse », « Brouillons ») and
   * ?acceptedFrom/?acceptedTo (« Devis acceptés »). window.location in an effect rather than useSearchParams —
   * the repo's idiom, and it keeps this page out of a Suspense boundary. Unknown values are ignored, so a stale
   * link lands on the full list.
   *
   * ⚠️ A drill-through also switches the creation window **off**. The default « Cette semaine » bounds the date a
   * devis was *written*, so a plan accepted this month but written in June — or any draft older than Monday —
   * would be filtered out of the very list the card promised, and the destination would contradict the number
   * that sent the user there. Both those KPIs are counted by another date (or by no date at all).
   */
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const urlStatus = params.get("status")
    const urlAcceptedFrom = params.get("acceptedFrom")
    const urlAcceptedTo = params.get("acceptedTo")
    let drilledThrough = false
    if (urlStatus && urlStatus in PLAN_STATUS_LABELS) {
      setStatus(urlStatus)
      drilledThrough = true
    }
    if (urlAcceptedFrom && !Number.isNaN(Date.parse(urlAcceptedFrom))) {
      setAcceptedFrom(urlAcceptedFrom)
      drilledThrough = true
    }
    if (urlAcceptedTo && !Number.isNaN(Date.parse(urlAcceptedTo))) {
      setAcceptedTo(urlAcceptedTo)
      drilledThrough = true
    }
    if (drilledThrough) setPeriod("all")
  }, [])

  const range = period === "custom" ? { from: customFrom, to: customTo } : presetRange(period)
  const from = range?.from ?? ""
  const to = range?.to ?? ""

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

  /*
   * ⚠️ There is no « Filtrer » button, deliberately — the same reasoning `/factures` records. The controls flow
   * straight into `fromIso`/`toIso`/`statusFilter`, which are dependencies of the table's `fetchPage`, so the list
   * has *already* narrowed by the time a user could reach a submit button. A button that does nothing is worse
   * than a missing one: it reads as « le filtre n'est pas appliqué tant que vous n'avez pas cliqué ».
   */
  const isDefaultView =
    period === DEFAULT_PERIOD && status === ALL_STATUSES && !acceptedFrom && !acceptedTo

  const clearUrl = () => {
    const url = new URL(window.location.href)
    url.search = ""
    window.history.replaceState({}, "", url)
  }

  const changePeriod = (next: PeriodKey) => {
    // Seed « Personnalisé » with the window already on screen, so opening it starts from what the user was
    // looking at instead of silently widening the list to every devis the clinic has ever written.
    if (next === "custom" && !customFrom && !customTo) {
      const current = presetRange(period)
      if (current) {
        setCustomFrom(current.from)
        setCustomTo(current.to)
      }
    }
    setPeriod(next)
  }

  /** Back to how the page opens: this week, every statut. */
  const resetFilters = () => {
    setPeriod(DEFAULT_PERIOD)
    setCustomFrom("")
    setCustomTo("")
    setStatus(ALL_STATUSES)
    setAcceptedFrom("")
    setAcceptedTo("")
    clearUrl()
  }

  /**
   * Every devis, no window at all — what « Afficher tous les devis » and the filtered empty state promise.
   *
   * <p>Distinct from {@link resetFilters} on purpose: resetting to the default would still hold a
   * one-week creation window, so a button labelled « tous les devis » would hide most of them.</p>
   */
  const showAllPlans = () => {
    setPeriod("all")
    setCustomFrom("")
    setCustomTo("")
    setStatus(ALL_STATUSES)
    setAcceptedFrom("")
    setAcceptedTo("")
    clearUrl()
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Plans de traitement"
          subtitle="Plans de soins, devis et échéanciers de paiement."
          // L5 — every filter on screen, including the acceptance window. ⚠️ `acceptedFrom`/`acceptedTo` bound a
          // DIFFERENT date from `from`/`to` (acceptance vs. creation), so both pairs are sent: dropping either
          // would export a different set of devis from the one the table is showing.
          actions={
            <ExportButton
              path="/treatment-plans/export"
              label="devis"
              params={{
                status: statusFilter,
                from: fromIso,
                to: toIso,
                acceptedFrom: acceptedFromIso,
                acceptedTo: acceptedToIso,
              }}
            />
          }
        />

        {/* Filters. Two columns below `sm:` so the two Selects share one row on a phone — then a flex row from
            `sm:` up, where the wrap the old layout relied on has room to work. */}
        <Card>
          <CardContent className="grid grid-cols-2 gap-3 pt-6 sm:flex sm:flex-wrap sm:items-end sm:gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="period">Période</Label>
              <Select value={period} onValueChange={(value) => changePeriod(value as PeriodKey)}>
                <SelectTrigger id="period" className="w-full sm:w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(PERIOD_LABELS).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="status">Statut</Label>
              <Select value={status} onValueChange={setStatus}>
                <SelectTrigger id="status" className="w-full sm:w-48">
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
            {period === "custom" && (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor="from">Du</Label>
                  <Input
                    id="from"
                    type="date"
                    value={customFrom}
                    onChange={(e) => setCustomFrom(e.target.value)}
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="to">Au</Label>
                  <Input id="to" type="date" value={customTo} onChange={(e) => setCustomTo(e.target.value)} />
                </div>
              </>
            )}
            {/* Shown only when something differs from the default view — an always-present « Réinitialiser »
                invites a click that changes nothing, and on a phone it costs a whole row. */}
            {!isDefaultView && (
              <Button variant="outline" onClick={resetFilters} className="col-span-2 sm:col-auto">
                Réinitialiser
              </Button>
            )}
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
            <Button size="sm" variant="outline" onClick={showAllPlans}>
              Afficher tous les devis
            </Button>
          </div>
        )}

        {/* `filtered` is what keeps an empty week from rendering the first-run invite: the page now opens on a
            window, so « Aucun plan de traitement » would tell a clinic with three hundred devis that it has none. */}
        <TreatmentPlansTable
          from={fromIso}
          to={toIso}
          status={statusFilter}
          acceptedFrom={acceptedFromIso}
          acceptedTo={acceptedToIso}
          filtered={Boolean(fromIso || toIso || statusFilter || acceptedFromIso || acceptedToIso)}
          onClearFilters={showAllPlans}
        />
      </AppShell>
    </ClinicGuard>
  )
}

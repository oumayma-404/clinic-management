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
// ⚠️ The one section-heading primitive, imported across folders deliberately. Its own docstring records that a
// second component drawing the same band is how two bands drift — `app/page.tsx` already had a private
// `SectionBar` doing exactly that, and merging it back is why this component carries an `href`/`action` pair.
// The name is a legacy of where it was born, not a statement that only the dashboard may have sections.
import { DashboardSection } from "@/components/dashboard/dashboard-section"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { TreatmentsInProgressList } from "@/components/treatments/treatments-in-progress-list"
import { PLAN_STATUS_LABELS } from "@/components/treatment-plans/treatment-plan-labels"
import { useSession } from "@/lib/auth/session"
import { isAdminOrDoctor } from "@/lib/nav"
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

/**
 * ⚠️ **« Toutes les dates », and it used to be « Cette semaine ».**
 *
 * The window bounds the date a devis was *written*, and a devis for an implant is a multi-month object — so
 * defaulting its ledger to this week made last month's unpaid balances vanish from the one page whose columns
 * are TOTAL / ENCAISSÉ / RESTE. Measured on the live database: the footer read « 1–12 sur 12 devis » on a
 * clinic holding **16**, and the three outside the window included **370 000 DT of receivables**, two of them
 * unpaid outright. Worse, the section directly above — « TRAITEMENTS EN COURS » — has no date filter at all, so
 * two tables on one page covered different periods and neither said so.
 *
 * The presets are unchanged and one click away; what changes is which of them a dentist has to *notice* in
 * order not to be misled.
 */
const DEFAULT_PERIOD: PeriodKey = "all"

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

/**
 * **The** treatments screen — « Traitements en cours » on top, then the devis and their échéanciers.
 *
 * <p>These were two rail entries one group apart, over the same acts: `/traitements-en-cours` answered « où en
 * est le bridge de Mme X, et qu'est-ce qui reste ? » and `/treatment-plans` answered « qu'a-t-on convenu et
 * qu'a-t-elle payé ? ». Nothing linked them, and the first is a *worklist over the second's rows* — every line
 * of it opens a devis. One page, worklist first; `/traitements-en-cours` redirects here.</p>
 *
 * <p>⚠️ <b>The worklist leads, and that ordering is the whole point of the merge.</b> It is the question asked
 * every morning and the only one with an action attached; the devis list is a reference read. Putting the
 * filter card first would bury a four-row worklist under a control nobody touches most days.</p>
 *
 * <p>⚠️ <b>Both halves are open to a secretary, and only the export is not.</b> `GET /api/treatment-plans` and
 * `GET /api/treatment-plans/treatments-in-progress` are both `AnyClinicRole` — reception is who telephones the
 * patient who has not come back — while `GET /api/treatment-plans/export` is `AdminOrDoctor`, so that one
 * control is hidden rather than shown and refused. That gate is <i>new</i>: this page was already reachable by
 * reception and already offered them a button answering 403, and the merge is what made it their daily screen
 * rather than one they rarely opened.</p>
 */
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
  // The worklist's own server total, for its section heading. Reported by the list rather than counted here:
  // the read is paged, so the rows in hand are up to 25 of it.
  const [inProgressTotal, setInProgressTotal] = useState<number | null>(null)

  // L5 — « Exporter » is `AdminOrDoctor` server-side; hidden rather than shown-and-refused, the reasoning
  // `access-denied-card` records. Presentation only, the endpoint is authoritative.
  const { user } = useSession()
  const canExport = isAdminOrDoctor(user?.role)

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
   *
   * ⚠️ None of these narrow the **worklist**: it is not a devis list and has no date window at all. « Brouillons »
   * landing here still shows every treatment under way above the three drafts it was asking about, which is
   * correct — the section states its own subject and its own total.
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
          title="Traitements"
          // A fact, not a paraphrase: the two questions the page answers, in the order it answers them.
          subtitle="Les actes commencés et non terminés, puis les devis et leurs échéanciers."
          // L5 — every filter on screen, including the acceptance window. ⚠️ `acceptedFrom`/`acceptedTo` bound a
          // DIFFERENT date from `from`/`to` (acceptance vs. creation), so both pairs are sent: dropping either
          // would export a different set of devis from the one the table is showing.
          //
          // ⚠️ It exports the DEVIS, so it stays with them — it is labelled « devis » and lands beside the second
          // section's own controls in reading order. There is no export of the worklist: that list is derived per
          // request from steps and appointments, and a CSV of it would be a snapshot of a thing whose only value
          // is being current.
          actions={
            canExport ? (
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
            ) : undefined
          }
        />

        {/*
          The lead section. Its `hint` carries the SERVER's total, not the rows in hand — the read is paged, so
          « 25 » would be the page size masquerading as a fact about the clinic. Null until the first read
          answers, so the heading never claims « 0 acte » about a list still loading.
        */}
        <DashboardSection
          title="Traitements en cours"
          hint={
            inProgressTotal === null
              ? undefined
              : inProgressTotal === 0
                ? "rien en attente"
                : `${inProgressTotal} acte${inProgressTotal > 1 ? "s" : ""} à terminer`
          }
        >
          <TreatmentsInProgressList onTotalChange={setInProgressTotal} />
        </DashboardSection>

        {/*
          The devis half, under its own heading so the filter card below reads as scoping THIS list and not the
          worklist above it — the same reason the dashboard keeps its période selector inside the bilan band.
        */}
        <DashboardSection title="Devis et échéanciers" className="border-t pt-6">
          {/* Filters. Two columns below `sm:` so the two Selects share one row on a phone — then a flex row from
              `sm:` up, where the wrap the old layout relied on has room to work. */}
          <div className="space-y-6">
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
                      <Input
                        id="to"
                        type="date"
                        value={customTo}
                        onChange={(e) => setCustomTo(e.target.value)}
                      />
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
              <div
                role="status"
                className="flex flex-wrap items-center gap-3 rounded-lg border bg-muted/40 p-3 text-sm"
              >
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
                window, so « Aucun plan de traitement » would tell a clinic with three hundred devis that it has
                none. */}
            <TreatmentPlansTable
              from={fromIso}
              to={toIso}
              status={statusFilter}
              acceptedFrom={acceptedFromIso}
              acceptedTo={acceptedToIso}
              filtered={Boolean(fromIso || toIso || statusFilter || acceptedFromIso || acceptedToIso)}
              onClearFilters={showAllPlans}
            />
          </div>
        </DashboardSection>
      </AppShell>
    </ClinicGuard>
  )
}

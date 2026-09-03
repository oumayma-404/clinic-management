"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { useRouter } from "next/navigation"
/*
 * ⚠️ The page-level `toast.error(error)` effect is gone, deliberately.
 *
 * One failed dashboard read produced FIVE simultaneous reports of itself: a toast, plus the « Indisponible »
 * banner each `DashboardSection` already renders. The sections are the better surface and the only one with a
 * recovery action — they carry « Réessayer », they persist, and they say which block failed. A toast on top of
 * that is noise that also expires, which is why nothing here imports `sonner` any more.
 */
import {
  AlertCircle,
  BadgeCheck,
  CalendarCheck,
  PackageMinus,
  Receipt,
  Scale,
  Undo2,
  UserPlus,
  Wallet,
} from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { AppointmentList } from "@/components/appointment-list"
import { ClinicGuard } from "@/components/clinic-guard"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"
import { KpiCard } from "@/components/dashboard/kpi-card"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { DashboardSection } from "@/components/dashboard/dashboard-section"
import { DashboardCustomizer } from "@/components/dashboard/dashboard-customizer"
import { PeriodSelector } from "@/components/dashboard/period-selector"
import { CollectedTrendChart } from "@/components/dashboard/collected-trend-chart"
import {
  AppointmentStatusChart,
  type StatusWindowMode,
} from "@/components/dashboard/appointment-status-chart"
import { AppointmentTrendChart } from "@/components/dashboard/appointment-trend-chart"
import { ProcedureMixChart } from "@/components/dashboard/procedure-mix-chart"
import { DayGreeting } from "@/components/dashboard/day/day-greeting"
import { NowNextCards } from "@/components/dashboard/day/now-next-cards"
import { DayRibbon } from "@/components/dashboard/day/day-ribbon"
import { DayAlerts, type DayAlert } from "@/components/dashboard/day/day-alerts"
import {
  KPI_DESCRIPTIONS,
  KPI_LABELS,
  PREVIOUS_PERIOD_LABELS,
  comparedToLabel,
  periodWindowLabel,
  SECTION_LABELS,
} from "@/components/dashboard/dashboard-labels"
import { useDashboard } from "@/lib/hooks/use-dashboard"
import { useDashboardPreferences } from "@/lib/hooks/use-dashboard-preferences"
import { useAppointments } from "@/lib/hooks/use-appointments"
import { useAppointmentStatusMix } from "@/lib/hooks/use-appointment-status-mix"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { dashboardLink } from "@/lib/dashboard-links"
import { blocksInSection, type DashboardBlockKey } from "@/lib/dashboard-blocks"
import {
  buildDayPreview,
  buildDaySummary,
  minutesOfDay,
  resolveNextOpenDay,
} from "@/lib/dashboard/day-summary"
import { NextDayLine } from "@/components/dashboard/day/next-day-line"
import { buildDayPhrase } from "@/lib/dashboard/day-phrases"
import { formatDT, todayLocalIso } from "@/lib/format"
import type { DashboardPeriodKey, PeriodComparison } from "@/lib/api/types"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"

const PERIOD_PARAM = "period"
/** Radix cannot hold an empty Select value, so « tous » is an explicit sentinel. */
const ALL_DOCTORS = "all-doctors"
const PERIODS: DashboardPeriodKey[] = ["Today", "Week", "Month"]

/** How often the day zones re-read the clock. A minute is the finest unit anything here displays. */
const CLOCK_TICK_MS = 60_000

/** A count: French grouping, « — » when the figure is genuinely absent. */
const count = (value: number | null | undefined) =>
  value === null || value === undefined ? "—" : value.toLocaleString("fr-TN")

/** A percentage: « 8,3 % », or « — » when the rate is undefined (an empty period has no absence rate). */
const percent = (value: number | null | undefined) =>
  value === null || value === undefined
    ? "—"
    : `${value.toLocaleString("fr-TN", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %`

const money = (value: number | null | undefined) =>
  value === null || value === undefined ? "—" : formatDT(value)

/**
 * DEV-3 — where a secretary's morning starts.
 *
 * <p>I1 gates `GET /api/dashboard` to `AdminOrDoctor` (its Argent section *is* the clinic's revenue) and I3 hides
 * « Tableau de bord » from the rail. But `/` is where login lands, so without this reception would open the app
 * every morning onto the one screen they cannot read — a refusal card as the first thing the product says, every
 * day. `/appointments` is reception's actual first screen and is open to every role.</p>
 *
 * <p>⚠️ <b>This redirect is now the dashboard's weakest point, and it is a deliberate hold.</b> Since the day-first
 * rearrangement the top of this page carries no money at all — the greeting, the ribbon, the now/next pair and the
 * à-traiter chips are exactly what reception works from — so the person who most needs it is the one person locked
 * out. Opening the page and serving the money sections only to `AdminOrDoctor` is the right shape, but it is a
 * <b>security</b> change (the policy, `nav.ts`'s `SECRETARY_HIDDEN_HREFS`, and what the endpoint returns), so it is
 * asked rather than taken.</p>
 */
export function DashboardPage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const redirectToAgenda = hidesClinicWideMoney(user?.role)

  useEffect(() => {
    if (redirectToAgenda) router.replace("/appointments")
  }, [redirectToAgenda, router])

  // Nothing is rendered while the session resolves or while the redirect is in flight: a flash of the dashboard
  // shell — or of a refusal card that is about to be replaced — is worse than a blank moment.
  if (isLoading || redirectToAgenda) {
    return (
      <ClinicGuard>
        <AppShell width="none" gutter={false}>
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        </AppShell>
      </ClinicGuard>
    )
  }

  return <DashboardContent />
}

function DashboardContent() {
  const [period, setPeriod] = useState<DashboardPeriodKey>("Month")
  // L9 — the Argent section's practitioner filter. `ALL_DOCTORS` because Radix cannot hold an empty Select value.
  const [moneyDoctorId, setMoneyDoctorId] = useState<string>(ALL_DOCTORS)
  /*
   * « Rendez-vous par statut » carries its OWN window, which is why this state lives here and not in `period`.
   *
   * It is the one block on the page whose period is not the page's. `week` is the default and needs no bounds at
   * all — the server resolves the current clinic-local Monday-to-Sunday week, so the browser holds no copy of the
   * week rule. `month` sends explicit day keys because the month rule is trivial and unambiguous; `custom` sends
   * whatever the user applied.
   */
  const [statusMode, setStatusMode] = useState<StatusWindowMode>("week")
  const [customRange, setCustomRange] = useState<{ from: string; to: string } | null>(null)

  const statusWindow = useMemo(() => {
    if (statusMode === "week") return { from: undefined, to: undefined }
    if (statusMode === "custom" && customRange) return { from: customRange.from, to: customRange.to }
    if (statusMode === "custom") return { from: undefined, to: undefined }
    // `todayLocalIso()` and never `toISOString()`: for the first hour of every Tunisian day the latter names
    // yesterday, and on the 1st it names last month — which here would silently read the wrong month entirely.
    const today = todayLocalIso()
    const [year, month] = today.split("-")
    const lastDay = new Date(Number(year), Number(month), 0).getDate()
    return { from: `${year}-${month}-01`, to: `${year}-${month}-${String(lastDay).padStart(2, "0")}` }
  }, [statusMode, customRange])

  const {
    data: statusMix,
    loading: statusLoading,
    refetching: statusRefetching,
    error: statusError,
    refetch: refetchStatusMix,
  } = useAppointmentStatusMix(statusWindow.from, statusWindow.to)

  const { doctors, clinic } = useDoctors()
  const { data, loading, refetching, error, refetch } = useDashboard(
    period,
    moneyDoctorId === ALL_DOCTORS ? undefined : moneyDoctorId,
  )
  const { isVisible, hidden, toggle, resetToDefaults, showAll, loading: prefsLoading, saving } =
    useDashboardPreferences()

  /*
   * Today's appointments, fetched ONCE for the whole day board.
   *
   * The ribbon, the now/next pair and the list are three views of the same data, so the fetch lives here and the
   * derived summary is handed down. `AppointmentList` used to run its own identical `useAppointments(today, today)`;
   * leaving it there would have meant three components racing for the same rows.
   */
  const today = useMemo(() => new Date(), [])
  const {
    appointments,
    loading: dayLoading,
    error: dayError,
    refetch: refetchDay,
  } = useAppointments(today, today)

  /*
   * A ticking clock, resolved after mount.
   *
   * `new Date()` in a client component still runs during Next's prerender, and everything the day zones say about
   * *now* — « au fauteuil », « dans 1 h 50 », which blocks are past — would then be computed on the server and
   * re-computed differently in the browser, i.e. a hydration mismatch on the one screen that opens every morning.
   * Null until mounted, and the zones render skeletons meanwhile.
   *
   * It re-reads every minute so a dashboard left open does not keep announcing a patient who left an hour ago.
   */
  const [now, setNow] = useState<Date | null>(null)
  useEffect(() => {
    setNow(new Date())
    const id = window.setInterval(() => setNow(new Date()), CLOCK_TICK_MS)
    return () => window.clearInterval(id)
  }, [])

  const summary = useMemo(
    () => buildDaySummary(appointments, clinic?.workingHours, now ?? today),
    [appointments, clinic?.workingHours, now, today],
  )

  /*
   * The clinic's next OPEN day, and a SECOND fetch for it.
   *
   * The comment above argues against components racing for one window; this is a different window, and separating
   * them buys two things a widened `useAppointments(today, nextOpenDay)` cannot. Tomorrow's rows would otherwise
   * land in the same array `buildDaySummary` folds, so one missed partition silently inflates today's count,
   * ribbon and load percentage — a wrong figure that looks right. And a failed read here must not blank the day
   * board, which is the whole of today.
   *
   * `now`-derived, never a fresh `new Date()`: the day the board is having is settled once, after mount.
   */
  const nextOpenDay = useMemo(
    () => resolveNextOpenDay(clinic?.workingHours, now ?? today),
    [clinic?.workingHours, now, today],
  )
  /*
   * ⚠️ `?? today`, never `?? undefined`. `useAppointments` omits the date params entirely when a bound is
   * undefined, so the request becomes « every appointment this clinic has ever had » — on the home screen, on
   * every load. A clinic whose saved hours disable all seven days is the case that would have found it.
   * Falling back to today keeps the read bounded to one day; the line renders nothing there anyway.
   */
  const {
    appointments: nextDayAppointments,
    loading: nextDayLoading,
    error: nextDayError,
    refetch: refetchNextDay,
  } = useAppointments(nextOpenDay ?? today, nextOpenDay ?? today)

  const nextDayPreview = useMemo(
    () =>
      nextOpenDay
        ? buildDayPreview(nextDayAppointments, clinic?.workingHours, nextOpenDay, now ?? today)
        : null,
    [nextDayAppointments, clinic?.workingHours, nextOpenDay, now, today],
  )

  /*
   * The phrase is keyed on (clinic, clinic-local day, tier) and is therefore stable for the whole day — see
   * `day-phrases.ts`. `todayLocalIso()` and never `toISOString().slice(0, 10)`: the latter converts to UTC first,
   * so for the first hour of every Tunisian day it would name yesterday and re-roll the line at 01:00.
   */
  const phrase = useMemo(
    // `minutesOfDay(now)` is the same instant the day cards are measured against — the greeting's register must not
    // be able to say « bonne soirée » while « Ensuite » says the next patient is due in an hour.
    () => (now ? buildDayPhrase(summary, todayLocalIso(), minutesOfDay(now), clinic?.id ?? "") : null),
    [summary, clinic?.id, now],
  )

  // Restore the period from the URL on mount, so a refresh and a shared link both land on the same window.
  useEffect(() => {
    const fromUrl = new URLSearchParams(window.location.search).get(PERIOD_PARAM)
    if (fromUrl && (PERIODS as string[]).includes(fromUrl)) {
      setPeriod(fromUrl as DashboardPeriodKey)
    }
  }, [])

  const changePeriod = useCallback((next: DashboardPeriodKey) => {
    setPeriod(next)
    const url = new URL(window.location.href)
    url.searchParams.set(PERIOD_PARAM, next)
    window.history.replaceState({}, "", url)
  }, [])

  /**
   * One refetch for every read on the page — a peer's edit can move a figure in any, tomorrow's included.
   *
   * ⚠️ `refetchStatusMix` belongs here even though that card has its own window: an appointment booked or
   * cancelled by a colleague changes its columns exactly as it changes the figures above them, and leaving it out
   * would make the one card on the page that silently went stale the one with the most detail on it.
   */
  const refetchAll = useCallback(() => {
    refetch()
    refetchDay()
    refetchNextDay()
    refetchStatusMix()
  }, [refetch, refetchDay, refetchNextDay, refetchStatusMix])

  useClinicRealtime(
    [
      RealtimeResource.Appointments,
      RealtimeResource.Patients,
      RealtimeResource.Invoices,
      RealtimeResource.TreatmentPlans,
      RealtimeResource.Expenses,
      RealtimeResource.Stock,
      RealtimeResource.WaitingList,
      RealtimeResource.Recall,
      RealtimeResource.LabOrders,
    ],
    refetchAll,
  )

  /** « Jeudi 14 août ». Empty on the first paint for the hydration reason above; the eyebrow keeps its height. */
  const dayLabel = useMemo(() => {
    if (!now) return ""
    const label = new Intl.DateTimeFormat("fr-TN", { weekday: "long", day: "numeric", month: "long" }).format(now)
    return label.charAt(0).toUpperCase() + label.slice(1)
  }, [now])

  const previousLabel = PREVIOUS_PERIOD_LABELS[period]
  // The bounds the SERVER used. Links are built from these, never from a client-side recomputation — otherwise a
  // card could count one window and open another.
  const bounds = data?.period
  const link = (key: Parameters<typeof dashboardLink>[0]) => (bounds ? dashboardLink(key, bounds) : "#")

  const kpi = (
    key: Parameters<typeof dashboardLink>[0],
    value: string,
    icon: Parameters<typeof KpiCard>[0]["icon"],
    extras?: {
      comparison?: PeriodComparison
      sense?: Parameters<typeof KpiCard>[0]["sense"]
      variant?: "default" | "urgent"
      emphasis?: Parameters<typeof KpiCard>[0]["emphasis"]
      wide?: boolean
      sparkline?: number[]
    },
  ) =>
    isVisible(key) ? (
      <KpiCard
        key={key}
        label={KPI_LABELS[key]}
        description={KPI_DESCRIPTIONS[key]}
        value={value}
        icon={icon}
        href={link(key)}
        loading={loading}
        previousPeriodLabel={previousLabel}
        {...extras}
      />
    ) : null

  /**
   * True when at least one block of a customiser group, of a given shape, survives this user's choices.
   *
   * <p>The `form` argument is what stops a group whose only visible block is its chart from rendering an empty
   * hairline grid — « Facturé » and « Avoirs remboursés » are hidden by default, so a user can genuinely end up with
   * a chart and no figures.</p>
   */
  const hasVisible = (
    section: Parameters<typeof blocksInSection>[0],
    form?: Parameters<typeof blocksInSection>[1],
  ) => blocksInSection(section, form).some((key) => isVisible(key as DashboardBlockKey))

  /**
   * The « À traiter » chips.
   *
   * <p>Built from the alerts the dashboard read already carries, so this zone costs no extra request. Each entry
   * respects the user's own customiser choices — hiding « Stock bas » must hide it here too, not only in a section
   * that no longer exists.</p>
   */
  /*
   * « Traitements en cours » — the acts started and not finished.
   *
   * ⚠️ Its own small read, and it is the one chip on this zone that costs a request. It cannot ride
   * `GET /api/dashboard` like its neighbours because that endpoint is `AdminOrDoctor` while this list is open to
   * the whole team — folding the count into it would make the figure unavailable to precisely the person who
   * acts on it. `pageSize: 1` fetches one row for its `totalCount`, which is exact whatever page is asked for,
   * so the chip and the page it opens read the same number by construction.
   *
   * A failed read leaves the chip out entirely rather than showing 0: « aucun traitement en cours » and « je
   * n'ai pas pu lire » are opposite facts, and here the wrong one is the reassuring one (§ 13).
   */
  const [treatmentsInProgress, setTreatmentsInProgress] = useState<number | null>(null)

  const loadTreatmentsInProgress = useCallback(async () => {
    try {
      const page = await treatmentPlansApi.treatmentsInProgress({ page: 1, pageSize: 1 })
      setTreatmentsInProgress(page.totalCount)
    } catch {
      setTreatmentsInProgress(null)
    }
  }, [])

  useEffect(() => {
    void loadTreatmentsInProgress()
  }, [loadTreatmentsInProgress])

  useClinicRealtime(
    [RealtimeResource.TreatmentPlans, RealtimeResource.Appointments],
    loadTreatmentsInProgress,
  )

  const alerts: DayAlert[] = data
    ? (
        [
          // First: the only entry here about work already DONE. Everything else is something to start; this is
          // something to finish, and the practice loses money and record completeness while it waits.
          {
            key: "visitsToClose",
            count: data.alerts.visitsToClose,
            label: data.alerts.visitsToClose === 1 ? "séance à clôturer" : "séances à clôturer",
            tone: "hot" as const,
          },
          // Amber, like « séances à clôturer » above: the same register — work already started that is waiting
          // on a next step. Never red, which this zone reserves for money sitting still.
          ...(treatmentsInProgress !== null
            ? [
                {
                  key: "treatmentsInProgress",
                  count: treatmentsInProgress,
                  label:
                    treatmentsInProgress === 1 ? "traitement en cours" : "traitements en cours",
                  tone: "warm" as const,
                },
              ]
            : []),
          { key: "waitingList", count: data.alerts.waitingList, label: "en salle d’attente", tone: "live" as const },
          { key: "draftPlans", count: data.alerts.draftPlans, label: "devis en attente", tone: "calm" as const },
          {
            key: "overdueLabOrders",
            count: data.alerts.overdueLabOrders,
            label: data.alerts.overdueLabOrders === 1 ? "prothèse en retard" : "prothèses en retard",
            tone: "hot" as const,
          },
          {
            key: "lowStock",
            count: data.alerts.lowStock,
            label: data.alerts.lowStock === 1 ? "article en stock bas" : "articles en stock bas",
            tone: "warm" as const,
          },
          // Hidden entirely when the clinic switched the expiry alert off: « 0 » would claim nothing is
          // expiring, when in truth nothing was checked.
          ...(data.alerts.expiryAlertEnabled !== false
            ? [
                {
                  key: "expiringStock",
                  count: data.alerts.expiringStock,
                  label: data.alerts.expiringStock === 1 ? "lot bientôt périmé" : "lots bientôt périmés",
                  tone: "warm" as const,
                },
              ]
            : []),
        ] satisfies Array<Omit<DayAlert, "href" | "isZero">>
      )
        .filter((a) => isVisible(a.key as DashboardBlockKey))
        .map((a) => ({
          ...a,
          href: link(a.key as Parameters<typeof dashboardLink>[0]),
          isZero: a.count === 0,
        }))
    : []

  const dayReady = now !== null && !dayLoading

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-8">
        {/*
          ════════════════════════════════════════════════════════════════════════════════════════════════════
          THE DAY. Five zones, one question each: quelle humeur · qui maintenant · quelle forme · quoi d'urgent
          · le détail. Nothing here is period-bound, and nothing here is money.

          It leads because that is what a dentist opens the app at 08:00 to find out. « Argent » used to, with
          « Net » as a filled hero panel — a coherent choice while this was a money screen, and the wrong one for
          a screen somebody reads standing up between two patients.
          ════════════════════════════════════════════════════════════════════════════════════════════════════
        */}
        <div className="flex flex-wrap items-start justify-between gap-4">
          <DayGreeting phrase={phrase} dayLabel={dayLabel} loading={!dayReady} />
          <DashboardCustomizer
            hidden={hidden}
            onToggle={toggle}
            onResetToDefaults={resetToDefaults}
            onShowAll={showAll}
            saving={saving}
            disabled={prefsLoading}
          />
        </div>

        {/* A failed read of today never renders as an empty day — that is « aucun rendez-vous », a different and
            confidently wrong fact. The list below carries the retry. */}
        {dayReady && !dayError && (
          <>
            <NowNextCards summary={summary} nowMinutes={minutesOfDay(now)} />

            {/* An empty day has no shape worth drawing: the ribbon is absent rather than an empty rectangle,
                and the greeting's own tier already says what the day is. `slots`, not `count` — a day holding
                only « créneaux occupés » has no rendez-vous and still has a shape. */}
            {summary.slots.length > 0 && (
              <DashboardSection title={SECTION_LABELS.day} href="/appointments" action="Ouvrir l'agenda">
                <DayRibbon summary={summary} nowMinutes={minutesOfDay(now)} />
              </DashboardSection>
            )}
          </>
        )}

        {alerts.length > 0 && (
          <DashboardSection title={SECTION_LABELS.alerts}>
            <DayAlerts alerts={alerts} />
          </DashboardSection>
        )}

        {isVisible("todayAppointments") && (
          <DashboardSection
            title={SECTION_LABELS.appointments}
            href="/appointments"
            action="Ouvrir l'agenda"
          >
            <AppointmentList
              slots={summary.slots}
              loading={!dayReady}
              error={dayError}
              onRetry={refetchDay}
            />
          </DashboardSection>
        )}

        {/*
          ZONE 6 — la prochaine journée ouvrée.

          ⚠️ Deliberately OUTSIDE the `summary.count > 0` guard that wraps the ribbon, and outside
          `isVisible("todayAppointments")`. An empty today still has a next working day, and that is exactly when
          this line is worth most — gating it on today having work would hide it on the quietest afternoon.
        */}
        <NextDayLine
          preview={nextDayPreview}
          loading={now === null || nextDayLoading}
          error={nextDayError}
          onRetry={refetchNextDay}
        />

        {/*
          ════════════════════════════════════════════════════════════════════════════════════════════════════
          LE BILAN — everything period-bound, below the fold, recut by QUESTION rather than by kind of figure:
          l'argent and l'activité, each with its lead figure and the chart that explains it.

          ⚠️ The period selector stays DOWN HERE, and that remains the most important structural decision of the
          day-first rearrangement. At the top it would appear to govern the day zones, and choosing « Ce mois »
          would change nothing above it — a control whose implied scope its own page contradicts.

          ⚠️ There is no « Statistiques » heading any more, and no filled accent surface below the fold. The heading
          was a category name for figures rather than a question anybody asks, and the hero panel — correct while
          these sections *were* the page — had become the loudest thing on a screen nobody scrolls to.
          ════════════════════════════════════════════════════════════════════════════════════════════════════
        */}
        {/*
          The période band. It owns the selector and — for the first time — states the window in words, from the
          bounds the SERVER returned. « Ce mois » is a button, not a claim about dates, and nothing on the page used
          to say which days were being read.

          It carries the read's single error, and the two zones below render only when there is none: with `data`
          undefined every figure formats as « — », which reads as a real zero rather than as a failure.
        */}
        <DashboardSection
          title={SECTION_LABELS.period}
          hint={bounds ? periodWindowLabel(bounds) : undefined}
          control={<PeriodSelector value={period} onChange={changePeriod} disabled={loading} />}
          error={error}
          onRetry={refetch}
          // The one rule on the page: above it is today, below it is a period. `space-y-8` alone does not say that.
          className="border-t pt-8"
        />

        {!error &&
          (hasVisible("activity", "figure") ||
            isVisible("procedureMix") ||
            isVisible("appointmentStatusMix") ||
            isVisible("appointmentTrend")) && (
          <DashboardSection
            title={SECTION_LABELS.activity}
            hint={comparedToLabel(period)}
            refetching={refetching}
          >
            {/*
              ⚠️ **`items-stretch` is not enough on its own — the LEFT column has to be told to fill.** Both
              columns are grid items and so already stretch to the row's height, but the left one's own children
              (two `KpiGrid` surfaces) size to their content, so the stretched box was empty at the bottom and
              the two sides read as unequal cards. « Répartition des actes » holds a *dynamic* list (1–8 acts),
              so the row's height is whatever the chart needs — which means the fix is to make the figures fill
              that height rather than to guess it: `flex-col` + `flex-1` on the subordinate grid, and
              `auto-rows-fr` inside it so the extra height goes to the CELLS and not to the `bg-border`
              container showing through underneath them.
            */}
            {/* `DashboardSection` wraps children in one gapless div (its `space-y-3` is on the <section> above),
                so two sibling rows here would touch. */}
            <div className="space-y-4">
            {(hasVisible("activity", "figure") || isVisible("procedureMix")) && (
            <div className="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)]">
              <div className="flex flex-col gap-3">
                <KpiGrid columns={1}>
                  {kpi(
                    "completedAppointments",
                    count(data?.activity.completedAppointments.current),
                    CalendarCheck,
                    { comparison: data?.activity.completedAppointments, emphasis: "lead" },
                  )}
                </KpiGrid>
                <KpiGrid columns={2} className="auto-rows-fr xl:flex-1">
                  {kpi("absenceRate", percent(data?.activity.absenceRate.current), AlertCircle, {
                    comparison: data?.activity.absenceRate,
                    // A rising absence rate is bad news, so the arrow's colour must invert.
                    sense: "up-is-bad",
                  })}
                  {kpi("newPatients", count(data?.activity.newPatients.current), UserPlus, {
                    comparison: data?.activity.newPatients,
                  })}
                  {kpi("acceptedPlans", count(data?.activity.acceptedPlans.current), BadgeCheck, {
                    comparison: data?.activity.acceptedPlans,
                  })}
                </KpiGrid>
              </div>
              {/* « Répartition des actes » measures activity, not money — which is why it moved here from a row
                  where it was paired with the collected trend. */}
              {isVisible("procedureMix") && (
                <ProcedureMixChart points={data?.procedureMix ?? []} loading={loading} />
              )}
            </div>
            )}

            {/*
              The appointment row. `items-stretch` is the grid default and is what the two cards' internal
              `flex-1` plots need: the taller card sets the row's height and the other's plot grows into it,
              rather than one card trailing a block of empty surface — which is what a chart card looks like
              when it is shorter than its neighbour.

              ⚠️ `xl:` and not `lg:`, for the reason the rows above it use: a tablet portrait is 820 px and the
              256 px rail leaves ~532 px, which would give the status chart's columns nowhere to go.
            */}
            {(isVisible("appointmentStatusMix") || isVisible("appointmentTrend")) && (
              <div className="grid grid-cols-[minmax(0,1fr)] gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)]">
                {isVisible("appointmentStatusMix") && (
                  <AppointmentStatusChart
                    data={statusMix}
                    loading={statusLoading}
                    refetching={statusRefetching}
                    error={statusError}
                    mode={statusMode}
                    onModeChange={setStatusMode}
                    onCustomRange={(from, to) => setCustomRange({ from, to })}
                    customFrom={customRange?.from}
                    customTo={customRange?.to}
                    onRetry={refetchStatusMix}
                  />
                )}
                {isVisible("appointmentTrend") && (
                  <AppointmentTrendChart points={data?.appointmentTrend ?? []} loading={loading} />
                )}
              </div>
            )}
            </div>
          </DashboardSection>
        )}

        {!error && (hasVisible("money", "figure") || isVisible("trend")) && (
          <DashboardSection
            title={SECTION_LABELS.money}
            hint={comparedToLabel(period)}
            refetching={refetching}
            control={
              /*
               * L9 — the practitioner filter narrows this section only. « RDV honorés » and « Prothèses en retard »
               * are the practice's operational state, and a filter at page level would look like it applied to them.
               * In the header it is unmistakably this card's.
               */
              doctors.length > 1 ? (
                <div className="flex flex-wrap items-center gap-2">
                  <Label htmlFor="money-doctor" className="text-xs font-medium text-muted-foreground">
                    Praticien
                  </Label>
                  <Select value={moneyDoctorId} onValueChange={setMoneyDoctorId}>
                    <SelectTrigger id="money-doctor" className="h-9 w-full sm:w-56">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL_DOCTORS}>Tout le cabinet</SelectItem>
                      {doctors
                        .filter((d) => d.id)
                        .map((d) => (
                          <SelectItem key={d.id} value={d.id as string}>
                            {d.name}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                </div>
              ) : undefined
            }
          >
            <div className="space-y-3">
              {data?.money.clinicWideOutgoings && (
                <p role="note" className="rounded-md bg-warning-wash p-2.5 text-xs text-warning-ink">
                  Filtré par praticien&nbsp;: «&nbsp;Encaissé&nbsp;» ne compte que les paiements de factures
                  {data.money.collectedInvoicesOnly ? " (hors échéances de devis)" : ""}, et
                  «&nbsp;Dépenses&nbsp;» et «&nbsp;Net&nbsp;» restent ceux de tout le cabinet — une dépense
                  n&apos;appartient à aucun praticien.
                </p>
              )}
              {/*
                Figures on the left, the chart that explains them on the right. `xl:` and not `lg:`: a tablet
                portrait is 820 px and the 256 px rail leaves ~532 px, which would give the chart ~250 px.
              */}
              <div className="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(0,1fr)]">
                {/* Same fill as l'activité above — see the note there. The trend chart is the taller half here. */}
                <div className="flex flex-col gap-3">
                  <KpiGrid columns={1}>
                    {kpi("net", money(data?.money.net.current), Scale, {
                      comparison: data?.money.net,
                      emphasis: "lead",
                    })}
                  </KpiGrid>
                  <KpiGrid columns={2} className="auto-rows-fr xl:flex-1">
                    {kpi("collected", money(data?.money.collected.current), Wallet, {
                      comparison: data?.money.collected,
                    })}
                    {kpi("invoiced", money(data?.money.invoiced.current), Receipt, {
                      comparison: data?.money.invoiced,
                    })}
                    {kpi("expenses", money(data?.money.expenses.current), PackageMinus, {
                      comparison: data?.money.expenses,
                      sense: "up-is-bad",
                    })}
                    {kpi("refunds", money(data?.money.refunds.current), Undo2, {
                      comparison: data?.money.refunds,
                      sense: "up-is-bad",
                    })}
                  </KpiGrid>
                </div>
                {isVisible("trend") && (
                  <CollectedTrendChart points={data?.trend ?? []} loading={loading} />
                )}
              </div>
            </div>
          </DashboardSection>
        )}

      </AppShell>
    </ClinicGuard>
  )
}

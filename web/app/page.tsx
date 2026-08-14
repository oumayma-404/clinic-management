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
  FileText,
  FlaskConical,
  Hourglass,
  PackageMinus,
  Receipt,
  Scale,
  Undo2,
  UserPlus,
  Users,
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
import { ProcedureMixChart } from "@/components/dashboard/procedure-mix-chart"
import { DayGreeting } from "@/components/dashboard/day/day-greeting"
import { NowNextCards } from "@/components/dashboard/day/now-next-cards"
import { DayRibbon } from "@/components/dashboard/day/day-ribbon"
import { DayAlerts, type DayAlert } from "@/components/dashboard/day/day-alerts"
import {
  KPI_DESCRIPTIONS,
  KPI_LABELS,
  PREVIOUS_PERIOD_LABELS,
  SECTION_LABELS,
} from "@/components/dashboard/dashboard-labels"
import { useDashboard } from "@/lib/hooks/use-dashboard"
import { useDashboardPreferences } from "@/lib/hooks/use-dashboard-preferences"
import { useAppointments } from "@/lib/hooks/use-appointments"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { dashboardLink } from "@/lib/dashboard-links"
import { blocksInSection, type DashboardBlockKey } from "@/lib/dashboard-blocks"
import { buildDaySummary, minutesOfDay } from "@/lib/dashboard/day-summary"
import { buildDayPhrase } from "@/lib/dashboard/day-phrases"
import { formatDT, todayLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"
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
export default function DashboardPage() {
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
   * The phrase is keyed on (clinic, clinic-local day, tier) and is therefore stable for the whole day — see
   * `day-phrases.ts`. `todayLocalIso()` and never `toISOString().slice(0, 10)`: the latter converts to UTC first,
   * so for the first hour of every Tunisian day it would name yesterday and re-roll the line at 01:00.
   */
  const phrase = useMemo(
    () => (now ? buildDayPhrase(summary, todayLocalIso(), clinic?.id ?? "") : null),
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

  /** One refetch for both reads — a peer's edit can move a figure in either. */
  const refetchAll = useCallback(() => {
    refetch()
    refetchDay()
  }, [refetch, refetchDay])

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

  const netCard = kpi("net", money(data?.money.net.current), Scale, {
    comparison: data?.money.net,
    emphasis: "hero",
    sparkline: data?.trend.map((p) => p.collected),
  })

  /** True when at least one block of a customiser section survives this user's choices. */
  const sectionHasContent = (section: Parameters<typeof blocksInSection>[0]) =>
    blocksInSection(section).some((key) => isVisible(key as DashboardBlockKey))

  /**
   * The « À traiter » chips.
   *
   * <p>Built from the alerts the dashboard read already carries, so this zone costs no extra request. Each entry
   * respects the user's own customiser choices — hiding « Stock bas » must hide it here too, not only in a section
   * that no longer exists.</p>
   */
  const alerts: DayAlert[] = data
    ? (
        [
          { key: "waitingList", count: data.alerts.waitingList, label: "en salle d'attente", tone: "live" as const },
          { key: "draftPlans", count: data.alerts.draftPlans, label: "devis en attente", tone: "calm" as const },
          {
            key: "overdueLabOrders",
            count: data.alerts.overdueLabOrders,
            label: data.alerts.overdueLabOrders === 1 ? "prothèse en retard" : "prothèses en retard",
            tone: "hot" as const,
          },
          { key: "lowStock", count: data.alerts.lowStock, label: "en stock bas", tone: "warm" as const },
          // Hidden entirely when the clinic switched the expiry alert off: « 0 » would claim nothing is
          // expiring, when in truth nothing was checked.
          ...(data.alerts.expiryAlertEnabled !== false
            ? [
                {
                  key: "expiringStock",
                  count: data.alerts.expiringStock,
                  label: "périment bientôt",
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
                and the greeting's own tier already says what the day is. */}
            {summary.count > 0 && (
              <section aria-label="La journée" className="space-y-3">
                <SectionBar title="La journée" href="/appointments" action="Ouvrir l'agenda" />
                <DayRibbon summary={summary} nowMinutes={minutesOfDay(now)} />
              </section>
            )}
          </>
        )}

        {alerts.length > 0 && (
          <section aria-label={SECTION_LABELS.alerts} className="space-y-3">
            <SectionBar title={SECTION_LABELS.alerts} />
            <DayAlerts alerts={alerts} />
          </section>
        )}

        {isVisible("todayAppointments") && (
          <section aria-label="Les rendez-vous du jour" className="space-y-3">
            <SectionBar title="Les rendez-vous" href="/appointments" action="Ouvrir l'agenda" />
            <AppointmentList
              slots={summary.slots}
              loading={!dayReady}
              error={dayError}
              onRetry={refetchDay}
            />
          </section>
        )}

        {/*
          ════════════════════════════════════════════════════════════════════════════════════════════════════
          STATISTIQUES — everything period-bound, below the fold.

          ⚠️ The period selector moved DOWN HERE with them, and that is the single most important structural
          decision of this rearrangement. Left at the top it would appear to govern the day zones, and choosing
          « Ce mois » would change nothing above it — a control whose implied scope its own page contradicts.
          Sitting in this header it visibly governs only what is under it.
          ════════════════════════════════════════════════════════════════════════════════════════════════════
        */}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-8">
          <h2 className="font-mono text-2xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
            Statistiques
          </h2>
          <PeriodSelector value={period} onChange={changePeriod} disabled={loading} />
        </div>

        {sectionHasContent("activity") && (
          <DashboardSection
            title={SECTION_LABELS.activity}
            hint={`Comparé à ${previousLabel}`}
            refetching={refetching}
            error={error}
            onRetry={refetch}
          >
            <KpiGrid columns={4}>
              {kpi("completedAppointments", count(data?.activity.completedAppointments.current), CalendarCheck, {
                comparison: data?.activity.completedAppointments,
              })}
              {kpi("newPatients", count(data?.activity.newPatients.current), UserPlus, {
                comparison: data?.activity.newPatients,
              })}
              {kpi("absenceRate", percent(data?.activity.absenceRate.current), AlertCircle, {
                comparison: data?.activity.absenceRate,
                // A rising absence rate is bad news, so the arrow's colour must invert.
                sense: "up-is-bad",
              })}
              {kpi("acceptedPlans", count(data?.activity.acceptedPlans.current), BadgeCheck, {
                comparison: data?.activity.acceptedPlans,
              })}
            </KpiGrid>
          </DashboardSection>
        )}

        {sectionHasContent("money") && (
          <DashboardSection
            title={SECTION_LABELS.money}
            hint={`Comparé à ${previousLabel}`}
            refetching={refetching}
            error={error}
            onRetry={refetch}
          >
            {/*
              L9 — the practitioner filter narrows this section only. « RDV honorés » and « Prothèses en retard »
              are the practice's operational state, and a filter at page level would look like it applied to them.
            */}
            {doctors.length > 1 && (
              <div className="mb-3 flex flex-wrap items-center gap-2">
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
            )}
            {data?.money.clinicWideOutgoings && (
              <p role="note" className="mb-3 rounded-md bg-warning-wash p-2.5 text-xs text-warning-ink">
                Filtré par praticien&nbsp;: «&nbsp;Encaissé&nbsp;» ne compte que les paiements de factures
                {data.money.collectedInvoicesOnly ? " (hors échéances de devis)" : ""}, et
                «&nbsp;Dépenses&nbsp;» et «&nbsp;Net&nbsp;» restent ceux de tout le cabinet — une dépense
                n&apos;appartient à aucun praticien.
              </p>
            )}
            <div className={cn("grid gap-3", netCard && "lg:grid-cols-[minmax(0,1.05fr)_minmax(0,2fr)]")}>
              {netCard}
              <KpiGrid columns={3}>
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
          </DashboardSection>
        )}

        {/*
          The two charts side by side. « Répartition des actes » is the new one and the answer to a question no
          other screen asks; the collected trend keeps its place beside it.
        */}
        {(isVisible("procedureMix") || isVisible("trend")) && (
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,20rem)]">
            {isVisible("procedureMix") && (
              <ProcedureMixChart points={data?.procedureMix ?? []} loading={loading} />
            )}
            {isVisible("trend") && <CollectedTrendChart points={data?.trend ?? []} loading={loading} />}
          </div>
        )}

        {/*
          « À traiter » keeps a customiser section, but its blocks now render as the chips above rather than as a
          KpiGrid down here — the counts are operational state a dentist acts on in the morning, not statistics.
          The section header is intentionally absent: `DashboardSection` would draw an empty surface.
        */}
      </AppShell>
    </ClinicGuard>
  )
}

/**
 * A day-zone heading: the mono eyebrow the sections already use, plus an optional way through to the full screen.
 *
 * <p>Deliberately not `DashboardSection`, which owns loading/`Indisponible`/retry for a *period* read. The day
 * zones have their own single failure (the appointment fetch), reported once by the list.</p>
 */
function SectionBar({ title, href, action }: { title: string; href?: string; action?: string }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-2 border-b pb-2">
      <h2 className="flex items-center gap-2 font-mono text-2xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
        <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-primary" />
        {title}
      </h2>
      {href && action && (
        <a
          href={href}
          className="text-xs font-medium text-primary underline-offset-4 hover-hover:hover:underline"
        >
          {action} →
        </a>
      )}
    </div>
  )
}

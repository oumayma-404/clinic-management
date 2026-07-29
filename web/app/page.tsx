"use client"

import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import {
  AlertCircle,
  BadgeCheck,
  CalendarCheck,
  FileText,
  FlaskConical,
  HandCoins,
  Hourglass,
  PackageMinus,
  PhoneCall,
  Receipt,
  Scale,
  Undo2,
  UserPlus,
  Users,
  Wallet,
} from "lucide-react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { AppointmentList } from "@/components/appointment-list"
import { ClinicGuard } from "@/components/clinic-guard"
import { KpiCard } from "@/components/dashboard/kpi-card"
import { DashboardSection } from "@/components/dashboard/dashboard-section"
import { PeriodSelector } from "@/components/dashboard/period-selector"
import { CollectedTrendChart } from "@/components/dashboard/collected-trend-chart"
import {
  KPI_DESCRIPTIONS,
  KPI_LABELS,
  PERIOD_LABELS,
  PREVIOUS_PERIOD_LABELS,
  SECTION_LABELS,
} from "@/components/dashboard/dashboard-labels"
import { useDashboard } from "@/lib/hooks/use-dashboard"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { dashboardLink } from "@/lib/dashboard-links"
import { formatDT } from "@/lib/format"
import type { DashboardPeriodKey, PeriodComparison } from "@/lib/api/types"

const PERIOD_PARAM = "period"
const PERIODS: DashboardPeriodKey[] = ["Today", "Week", "Month"]

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

export default function DashboardPage() {
  const [period, setPeriod] = useState<DashboardPeriodKey>("Month")
  const { data, loading, refetching, error, refetch } = useDashboard(period)

  // Restore the period from the URL on mount, so a refresh and a shared link both land on the same window.
  // window.location + replaceState rather than useSearchParams — the repo's idiom, and it keeps this page out of a
  // Suspense boundary (see /patients, /appointments).
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

  // Every resource whose mutation can move a figure on this page. All nine keys already existed on both sides of the
  // realtime contract; the dashboard previously listened to only the first four, so the à-traiter counts and la
  // caisse would have gone stale under a peer's edit.
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
    refetch,
  )

  useEffect(() => {
    if (error) toast.error(error)
  }, [error])

  const previousLabel = PREVIOUS_PERIOD_LABELS[period]
  // The bounds the SERVER used. Links are built from these, never from a client-side recomputation — otherwise a card
  // could count one window and open another.
  const bounds = data?.period
  const link = (key: Parameters<typeof dashboardLink>[0]) => (bounds ? dashboardLink(key, bounds) : "#")

  const kpi = (
    key: Parameters<typeof dashboardLink>[0],
    value: string,
    icon: Parameters<typeof KpiCard>[0]["icon"],
    extras?: { comparison?: PeriodComparison; sense?: Parameters<typeof KpiCard>[0]["sense"]; variant?: "default" | "urgent" },
  ) => (
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
  )

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-7xl space-y-8">
              {/* Title + the one filter row, above everything it scopes. */}
              <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                  <h1 className="text-3xl font-semibold text-foreground">Tableau de bord</h1>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {PERIOD_LABELS[period]} — chaque chiffre ouvre le détail correspondant.
                  </p>
                </div>
                <PeriodSelector value={period} onChange={changePeriod} disabled={loading} />
              </div>

              <DashboardSection
                title={SECTION_LABELS.activity}
                hint={`Comparé à ${previousLabel}`}
                refetching={refetching}
                error={error}
                onRetry={refetch}
              >
                <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
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
                </div>
              </DashboardSection>

              <DashboardSection
                title={SECTION_LABELS.money}
                hint={`Comparé à ${previousLabel}`}
                refetching={refetching}
                error={error}
                onRetry={refetch}
              >
                <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                  {kpi("collected", money(data?.money.collected.current), Wallet, {
                    comparison: data?.money.collected,
                  })}
                  {kpi("invoiced", money(data?.money.invoiced.current), Receipt, {
                    comparison: data?.money.invoiced,
                  })}
                  {kpi("refunds", money(data?.money.refunds.current), Undo2, {
                    comparison: data?.money.refunds,
                    // Refunding more is not good news either. « Encaissé » is now gross, so this figure is no
                    // longer hidden inside it — a month with a large avoir used to just look like a weak month.
                    sense: "up-is-bad",
                  })}
                  {kpi("expenses", money(data?.money.expenses.current), PackageMinus, {
                    comparison: data?.money.expenses,
                    // More spending is not good news.
                    sense: "up-is-bad",
                  })}
                  {kpi("net", money(data?.money.net.current), Scale, { comparison: data?.money.net })}
                </div>

                {/* Créances sits with the money but carries NO comparison — it is a live balance, not a period total. */}
                <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                  {kpi("receivables", money(data?.receivables.total), HandCoins)}
                </div>
              </DashboardSection>

              <DashboardSection
                title={SECTION_LABELS.alerts}
                hint="État actuel — indépendant de la période"
                refetching={refetching}
                error={error}
                onRetry={refetch}
              >
                <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
                  {kpi("waitingList", count(data?.alerts.waitingList), Users)}
                  {kpi("draftPlans", count(data?.alerts.draftPlans), FileText)}
                  {kpi("patientsToRecall", count(data?.alerts.patientsToRecall), PhoneCall)}
                  {kpi("overdueLabOrders", count(data?.alerts.overdueLabOrders), FlaskConical, {
                    variant: (data?.alerts.overdueLabOrders ?? 0) > 0 ? "urgent" : "default",
                  })}
                  {kpi("lowStock", count(data?.alerts.lowStock), PackageMinus, {
                    variant: (data?.alerts.lowStock ?? 0) > 0 ? "urgent" : "default",
                  })}
                  {/* Hidden entirely when the clinic switched the expiry alert off: « 0 » would claim nothing is
                      expiring, when in truth nothing was checked. */}
                  {data?.alerts.expiryAlertEnabled !== false &&
                    kpi("expiringStock", count(data?.alerts.expiringStock), Hourglass, {
                      variant: (data?.alerts.expiringStock ?? 0) > 0 ? "urgent" : "default",
                    })}
                </div>
              </DashboardSection>

              <DashboardSection
                title={SECTION_LABELS.trend}
                refetching={refetching}
                error={error}
                onRetry={refetch}
              >
                <CollectedTrendChart points={data?.trend ?? []} loading={loading} />
              </DashboardSection>

              <AppointmentList />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

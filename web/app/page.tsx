"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
/*
 * ⚠️ The page-level `toast.error(error)` effect is gone, deliberately.
 *
 * One failed dashboard read produced FIVE simultaneous reports of itself: a toast, plus the « Indisponible »
 * banner each of the four `DashboardSection`s already renders. The sections are the better surface and the only
 * one with a recovery action — they carry « Réessayer », they persist, and they say which block failed. A toast
 * on top of that is noise that also expires, which is why nothing here imports `sonner` any more.
 */
import {
  AlertCircle,
  BadgeCheck,
  CalendarCheck,
  FileText,
  FlaskConical,
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
import { AppShell } from "@/components/app-shell"
import { AppointmentList } from "@/components/appointment-list"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"
import { KpiCard } from "@/components/dashboard/kpi-card"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
import { DashboardSection } from "@/components/dashboard/dashboard-section"
import { DashboardCustomizer } from "@/components/dashboard/dashboard-customizer"
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
import { useDashboardPreferences } from "@/lib/hooks/use-dashboard-preferences"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { dashboardLink } from "@/lib/dashboard-links"
import { blocksInSection, type DashboardBlockKey } from "@/lib/dashboard-blocks"
import { formatDT } from "@/lib/format"
import { cn } from "@/lib/utils"
import type { DashboardPeriodKey, PeriodComparison } from "@/lib/api/types"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"

const PERIOD_PARAM = "period"
/** Radix cannot hold an empty Select value, so « tous » is an explicit sentinel. */
const ALL_DOCTORS = "all-doctors"
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

/**
 * DEV-3 — where a secretary's morning starts.
 *
 * <p>I1 gates `GET /api/dashboard` to `AdminOrDoctor` (its Argent section *is* the clinic's revenue) and I3 hides
 * « Tableau de bord » from the rail. But `/` is where login lands, so without this reception would open the app
 * every morning onto the one screen they cannot read — a refusal card as the first thing the product says, every
 * day. `/appointments` is reception's actual first screen and is open to every role.</p>
 *
 * <p>A wrapper rather than a branch inside the page, for the same reason as « Caisse » and « Factures »: the body
 * below fires the dashboard read on mount, and a secretary must not spend a 403 to be told where to go. The
 * redirect is a `replace`, so Back does not bounce them straight back into it.</p>
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
  const { user } = useSession()
  const [period, setPeriod] = useState<DashboardPeriodKey>("Month")
  // L9 — the Argent section's practitioner filter. `ALL_DOCTORS` because Radix cannot hold an empty Select value.
  const [moneyDoctorId, setMoneyDoctorId] = useState<string>(ALL_DOCTORS)
  const { doctors } = useDoctors()
  const { data, loading, refetching, error, refetch } = useDashboard(
    period,
    moneyDoctorId === ALL_DOCTORS ? undefined : moneyDoctorId,
  )
  /*
   * Per-user layout, persisted server-side (`GET/PUT /api/dashboard/preferences`).
   *
   * Deliberately NOT gating the render: the hook starts from the default layout and corrects once the row arrives,
   * rather than holding the whole dashboard behind a preferences fetch. The correction lands during the same beat
   * the figures are still skeletons — the two requests go out together and this one is far smaller — so a card
   * settling then is invisible, whereas blocking would mean a clinic cannot see today's numbers because a
   * cosmetic preference is slow.
   */
  const { isVisible, hidden, toggle, resetToDefaults, showAll, loading: prefsLoading, saving } =
    useDashboardPreferences()

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

  /*
   * The date, resolved after mount.
   *
   * `new Date()` in a client component still runs during Next's prerender, where the server's calendar day and
   * the browser's can differ — so rendering it inline produces a hydration mismatch on exactly the boundary
   * (around midnight, and for any clinic whose server sits in another zone) where the date matters most. An
   * empty first paint is the honest version: the greeting is complete either way, and the line simply fills in.
   */
  const [todayLabel, setTodayLabel] = useState("")
  useEffect(() => {
    setTodayLabel(
      new Intl.DateTimeFormat("fr-TN", {
        weekday: "long",
        day: "numeric",
        month: "long",
      }).format(new Date()),
    )
  }, [])

  /**
   * « Bonjour, Dr Ben Salah » — the practitioner's own name when the session carries one.
   *
   * <p>Deliberately not a fallback to the email: « Bonjour, o.benkhalifa@… » is worse than no name at all.
   * Cloud sessions may carry only an email, and « Bonjour » alone reads perfectly well in French.</p>
   *
   * <p>« Dr » is prefixed only when the stored name does not already carry it, so a clinic that types
   * « Dr Ben Salah » into its roster does not get « Dr Dr Ben Salah ».</p>
   */
  const greeting = (() => {
    const name = user?.name?.trim()
    if (!name) return "Bonjour"
    return /^(dr|docteur|pr)\b/i.test(name) ? `Bonjour, ${name}` : `Bonjour, Dr ${name}`
  })()

  const previousLabel = PREVIOUS_PERIOD_LABELS[period]
  // The bounds the SERVER used. Links are built from these, never from a client-side recomputation — otherwise a card
  // could count one window and open another.
  const bounds = data?.period
  const link = (key: Parameters<typeof dashboardLink>[0]) => (bounds ? dashboardLink(key, bounds) : "#")

  /**
   * One figure, or `null` when this user has it switched off.
   *
   * Returning `null` rather than filtering a list upstream is what keeps the section bodies readable — each row
   * below still reads as the figures it contains, and React drops the nulls. `visibleIn` then answers "does this
   * section have anything left at all", so a section whose every figure is hidden renders nothing instead of an
   * empty bordered surface.
   */
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
   * « Net » — the hero, rendered outside its section's grid.
   *
   * <p>Built here rather than inline so the enclosing row can ask whether it exists: hidden by this user, the grid
   * must take the full width instead of leaving a dead column.</p>
   *
   * <p>It receives the collected trend as a sparkline. The trend's *shape* answers the same question the hero's
   * number does, and having it only 400 px lower meant the reader travelled for it.</p>
   */
  const netCard = kpi("net", money(data?.money.net.current), Scale, {
    comparison: data?.money.net,
    emphasis: "hero",
    sparkline: data?.trend.map((p) => p.collected),
  })

  /** True when at least one block of a customiser section survives this user's choices. */
  const sectionHasContent = (section: Parameters<typeof blocksInSection>[0]) =>
    blocksInSection(section).some((key) => isVisible(key as DashboardBlockKey))

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-8">
        {/*
          The dashboard now uses the app's own `PageHeader` (it was the one screen still hand-rolling its
          title at `text-2xl md:text-3xl` rather than the `text-title` token, and the only one with no zone
          eyebrow — so the first screen of every day was the one most off-system).

          The title is a **greeting**, not « Tableau de bord ». The rail already names this page and marks it
          `aria-current`; repeating the label as the largest text on screen tells the reader something they
          used to get here. A name and a date are what a person actually wants at 08:00, and for a user who is
          not comfortable with software it is the difference between opening a tool and being addressed by one.
        */}
        <div className="flex flex-wrap items-end justify-between gap-4">
          <PageHeader
            title={greeting}
            subtitle={
              <>
                {/* `capitalize` because `Intl` returns « vendredi », and a sentence opens with a capital. */}
                {todayLabel && <span className="capitalize">{todayLabel}</span>}
                {todayLabel && " · "}
                {PERIOD_LABELS[period]} — chaque chiffre ouvre le détail correspondant.
              </>
            }
          />
          <div className="flex flex-wrap items-center gap-2">
            <PeriodSelector value={period} onChange={changePeriod} disabled={loading} />
            {/* Beside the period selector, in the one row that scopes the whole page — the same reasoning
                that keeps the period selector out of the individual sections. */}
            <DashboardCustomizer
              hidden={hidden}
              onToggle={toggle}
              onResetToDefaults={resetToDefaults}
              onShowAll={showAll}
              saving={saving}
              disabled={prefsLoading}
            />
          </div>
        </div>

        {/*
          Argent leads now, and « Net » is the hero.

          The audience for this screen is the practitioner-owner, and the section order should say so: the
          money block used to sit second behind Activité, with its five figures — encaissé, facturé, avoirs,
          dépenses, net — rendered as five equal cards, so the one number that answers « comment va le
          cabinet ce mois-ci ? » had exactly the same weight as the one that says how many notes were
          printed. Net is now 4xl and spans two columns; the rest support it at normal weight.
        */}
        {sectionHasContent("money") && (
          <DashboardSection
            title={SECTION_LABELS.money}
            hint={`Comparé à ${previousLabel}`}
            refetching={refetching}
            error={error}
            onRetry={refetch}
          >
            {/*
              The hero stands OUTSIDE the grid, in its own column beside it.

              It paints a filled accent surface, and a filled cell inside a hairline grid reads as a rendering
              fault — the grid's shared border would also cut straight across the panel's own edge. Two
              columns instead of a `col-span-2` cell, and the grid drops to full width on its own when this
              user has « Net » switched off.
            */}
            {/*
              L9 — the practitioner filter, and it lives INSIDE the Argent section rather than beside the period
              selector at the top. That placement is the honest one: it narrows this section only. « RDV honorés » and
              « Prothèses en retard » are the practice's operational state, and a filter at page level would look
              like it applied to them too. Offered only when the practice has more than one practitioner.
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
            {/*
              ⚠️ The scope caveats, from the response rather than inferred here. Two of them, and both matter: an
              expense has no practitioner, so Dépenses and Net stay clinic-wide — presenting them as one dentist's
              would show their income minus everybody's costs. And a filtered « Encaissé » counts invoice payments
              only, because échéance collections are not attributable in this slice.
            */}
            {data?.money.clinicWideOutgoings && (
              <p role="note" className="mb-3 rounded-md bg-warning-wash p-2.5 text-xs text-warning-ink">
                Filtré par praticien&nbsp;: «&nbsp;Encaissé&nbsp;» ne compte que les paiements de factures
                {data.money.collectedInvoicesOnly ? " (hors échéances de devis)" : ""}, et
                «&nbsp;Dépenses&nbsp;» et «&nbsp;Net&nbsp;» restent ceux de tout le cabinet — une dépense
                n&apos;appartient à aucun praticien.
              </p>
            )}
            <div
              className={cn(
                "grid gap-3",
                netCard && "lg:grid-cols-[minmax(0,1.05fr)_minmax(0,2fr)]",
              )}
            >
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
                  // More spending is not good news.
                  sense: "up-is-bad",
                })}
                {kpi("refunds", money(data?.money.refunds.current), Undo2, {
                  comparison: data?.money.refunds,
                  // Refunding more is not good news either. « Encaissé » is gross, so this figure is not
                  // hidden inside it — a month with a large avoir used to just look like a weak month.
                  sense: "up-is-bad",
                })}
              </KpiGrid>
            </div>
          </DashboardSection>
        )}

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

        {sectionHasContent("alerts") && (
          <DashboardSection
            title={SECTION_LABELS.alerts}
            hint="État actuel — indépendant de la période"
            refetching={refetching}
            error={error}
            onRetry={refetch}
          >
            {/* Compact density: these are single-digit counts whose LABEL is what you scan. At normal
                density the six of them took as much vertical space as the money and activity blocks
                combined, which is not what they are worth. */}
            <KpiGrid columns={3}>
              {/*
                « Patients à rappeler » was here. The card is gone, not the data: `alerts.patientsToRecall`
                is still computed and still on the wire, and the whole recall backend is intact.
                What went is its DESTINATION — /recalls was removed, and this KPI counts patients *due for
                a recall*, which the new « Rappels » delivery log does not list. Pointing it there would be
                a card that counts one set and opens another, which is the single defect `dashboard-links.ts`
                exists to make impossible. It comes back the day the worklist gets a new home.
              */}
              {kpi("draftPlans", count(data?.alerts.draftPlans), FileText, { emphasis: "compact" })}
              {kpi("overdueLabOrders", count(data?.alerts.overdueLabOrders), FlaskConical, {
                emphasis: "compact",
                variant: (data?.alerts.overdueLabOrders ?? 0) > 0 ? "urgent" : "default",
              })}
              {kpi("lowStock", count(data?.alerts.lowStock), PackageMinus, {
                emphasis: "compact",
                variant: (data?.alerts.lowStock ?? 0) > 0 ? "urgent" : "default",
              })}
              {/* Hidden entirely when the clinic switched the expiry alert off: « 0 » would claim nothing is
                  expiring, when in truth nothing was checked. */}
              {data?.alerts.expiryAlertEnabled !== false &&
                kpi("expiringStock", count(data?.alerts.expiringStock), Hourglass, {
                  emphasis: "compact",
                  variant: (data?.alerts.expiringStock ?? 0) > 0 ? "urgent" : "default",
                })}
              {kpi("waitingList", count(data?.alerts.waitingList), Users, { emphasis: "compact" })}
            </KpiGrid>
          </DashboardSection>
        )}

        {isVisible("trend") && (
          <DashboardSection
            title={SECTION_LABELS.trend}
            refetching={refetching}
            error={error}
            onRetry={refetch}
          >
            <CollectedTrendChart points={data?.trend ?? []} loading={loading} />
          </DashboardSection>
        )}

        {isVisible("todayAppointments") && <AppointmentList />}
      </AppShell>
    </ClinicGuard>
  )
}

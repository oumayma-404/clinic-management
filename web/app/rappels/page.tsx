"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { SlidersHorizontal } from "lucide-react"

import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from "@/components/ui/sheet"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { ReminderLogTable } from "@/components/rappels/reminder-log-table"
import { ReminderSettings } from "@/components/reminder-settings"
import { reminderSettingsApi, type ReminderDeliveryStatus, type ReminderLogDto } from "@/lib/api/reminder-settings"
import { useSession } from "@/lib/auth/session"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { showErrorToast } from "@/lib/errors"
import { todayLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"

/** How far back the log looks by default. Wide enough that last week's failure is on the first screen. */
const DEFAULT_WINDOW_DAYS = 14

type StatusFilter = ReminderDeliveryStatus | "all"
type ChannelFilter = "all" | "SMS" | "WhatsApp"

/**
 * « Rappels » — the delivery log for outbound patient messages, with the channel configuration behind it.
 *
 * <p>The configuration used to be a card at the bottom of « Paramètres », with a twenty-row status list under it.
 * That got the priority backwards: credentials are set roughly once, whereas « est-ce que ce patient a bien reçu
 * son SMS ? » is asked every day. Here the log <b>is</b> the page and the configuration is a button.</p>
 *
 * <p>Reading the log is open to all staff — it is the secretary fielding « je n'ai rien reçu » who needs it, and a
 * row carries a patient name and a phone masked to two digits. <b>Writing</b> the channel settings stays admin.</p>
 */
export default function RappelsPage() {
  const { user } = useSession()
  const isAdmin = user?.role === "admin"

  const [status, setStatus] = useState<StatusFilter>("all")
  const [channel, setChannel] = useState<ChannelFilter>("all")
  const [from, setFrom] = useState(() => isoDaysAgo(DEFAULT_WINDOW_DAYS))
  const [to, setTo] = useState(() => todayLocalIso())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [configOpen, setConfigOpen] = useState(false)

  const [data, setData] = useState<ReminderLogDto | null>(null)
  // `loading` is first paint only; a filter change sets `refreshing` and keeps the rows on screen. Blanking the
  // table on every keystroke or chip tap is what makes a filtered list feel like it is reloading the page.
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const isFiltered = status !== "all" || channel !== "all"

  const load = useCallback(async () => {
    const first = data === null
    if (first) setLoading(true)
    else setRefreshing(true)
    try {
      const result = await reminderSettingsApi.log({
        status: status === "all" ? undefined : status,
        channel: channel === "all" ? undefined : channel,
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize,
      })
      setData(result)
    } catch (error) {
      showErrorToast(error, "Le journal des rappels n'a pas pu être chargé.")
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
    // `data` is read only to decide skeleton-vs-dim; depending on it would refetch after every load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, channel, from, to, page, pageSize])

  /*
   * Deep-link from the notification bell: a failed-reminder notification lands here with `?status=failed`.
   *
   * `window.location.search` + `replaceState` rather than `useSearchParams` — the repo's idiom, and it keeps this
   * page out of a Suspense boundary. The param is consumed and cleared so a refresh does not re-apply a filter the
   * user has since cleared. Tolerant, like every other deep-link here: an unknown value leaves the filter alone
   * rather than refusing.
   */
  useEffect(() => {
    const incoming = new URLSearchParams(window.location.search).get("status")
    if (incoming === "sent" || incoming === "pending" || incoming === "failed") {
      setStatus(incoming)
      window.history.replaceState({}, "", "/rappels")
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  // A dispatched or failed reminder changes this page under the viewer, and the job runs every minute.
  useClinicRealtime(RealtimeResource.Clinics, load)

  // Any filter change returns to page 1: keeping the page number lands the user on page 4 of a 2-page result —
  // an empty table over data that matched.
  const setFilter = <T,>(setter: (v: T) => void) => (value: T) => {
    setter(value)
    setPage(1)
  }

  const resetFilters = () => {
    setStatus("all")
    setChannel("all")
    setFrom(isoDaysAgo(DEFAULT_WINDOW_DAYS))
    setTo(todayLocalIso())
    setPage(1)
  }

  const rows = data?.page.items ?? []

  /*
   * "No channel is sendable" is inferred from the counters rather than fetched separately: an empty log with
   * nothing ever sent AND nothing pending is the shape a clinic with no configured channel has. It only ever
   * changes an empty state's wording, so a wrong guess costs a sentence, not correctness — and it avoids a
   * second request on every page load to answer a question that matters on day one only.
   */
  const looksUnconfigured =
    data !== null && data.sentToday === 0 && data.pending === 0 && data.page.totalCount === 0 && !isFiltered

  const counters = useMemo(
    () => [
      { key: "sent", label: "Envoyés aujourd'hui", value: data?.sentToday, tone: "success" as const },
      { key: "pending", label: "En attente", value: data?.pending, tone: "warning" as const },
      // Several days, not today: a send that failed at 23:00 must still be counted the next morning.
      { key: "failed", label: "Échecs (7 j)", value: data?.failedRecent, tone: "destructive" as const },
    ],
    [data],
  )

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto flex max-w-7xl flex-col gap-6">

              {/* PageHeader: mono zone crumb, one title size, a subtitle carrying a FACT, actions right. */}
              <div className="flex flex-wrap items-end justify-between gap-4">
                <div>
                  <p className="font-mono text-[10.5px] uppercase tracking-[0.1em] text-muted-foreground">
                    Opérations
                  </p>
                  <h1 className="mt-1 text-[26px] font-[650] leading-tight tracking-[-0.022em]">Rappels</h1>
                  <p className="mt-1 max-w-[56ch] text-sm text-muted-foreground">
                    {data
                      ? `${data.page.totalCount.toLocaleString("fr-TN")} message${data.page.totalCount === 1 ? "" : "s"} sur la période · SMS et WhatsApp`
                      : "Messages envoyés aux patients — SMS et WhatsApp."}
                  </p>
                </div>
                {isAdmin && (
                  <Button variant="outline" className="gap-2" onClick={() => setConfigOpen(true)}>
                    <SlidersHorizontal className="h-4 w-4" />
                    Configurer les canaux
                  </Button>
                )}
              </div>

              {/* Counters: one shared surface with hairlines, the KpiGrid idiom — not three bordered cards. */}
              <div className="grid grid-cols-1 gap-px overflow-hidden rounded-xl border bg-border sm:grid-cols-3">
                {counters.map((c) => (
                  <div key={c.key} className="flex flex-col gap-0.5 bg-card px-4 py-3">
                    <span className="flex items-center gap-2 text-xs text-muted-foreground">
                      <i aria-hidden="true" className={cn("size-1.5 shrink-0 rounded-full", TONE_DOT[c.tone])} />
                      {c.label}
                    </span>
                    {c.value === undefined ? (
                      <span className="h-7 w-12 animate-pulse rounded bg-muted" aria-label="Chargement" />
                    ) : (
                      <span
                        className={cn(
                          "text-[22px] font-[650] tabular-nums tracking-[-0.01em]",
                          // Only the failure counter can turn red, and only when non-zero: it is the one
                          // actionable figure here. A red zero would raise an alarm about nothing.
                          c.tone === "destructive" && c.value > 0 && "text-destructive",
                        )}
                      >
                        {c.value.toLocaleString("fr-TN")}
                      </span>
                    )}
                  </div>
                ))}
              </div>

              {/* ListToolbar: only what NARROWS the list. Counted chips, so an active filter is visible as a
                  state rather than having to be read out of a changing button label. */}
              <div className="flex flex-wrap items-center gap-2 border-b pb-3">
                {STATUS_CHIPS.map((chip) => (
                  <button
                    key={chip.value}
                    type="button"
                    aria-pressed={status === chip.value}
                    onClick={() => setFilter(setStatus)(chip.value)}
                    className={cn(
                      "inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[13px] transition-colors",
                      status === chip.value
                        ? "border-primary bg-accent font-semibold text-accent-foreground"
                        : "border-border text-muted-foreground hover:bg-accent/50",
                    )}
                  >
                    {chip.label}
                    {chip.count(data) !== undefined && (
                      <span className="font-mono text-[11px] opacity-75 tabular-nums">{chip.count(data)}</span>
                    )}
                  </button>
                ))}

                <select
                  className="rounded-lg border bg-transparent px-2 py-1.5 text-[13px]"
                  value={channel}
                  onChange={(e) => setFilter(setChannel)(e.target.value as ChannelFilter)}
                  aria-label="Canal"
                >
                  <option value="all">Tous les canaux</option>
                  <option value="SMS">SMS</option>
                  <option value="WhatsApp">WhatsApp</option>
                </select>

                <span className="flex items-center gap-1.5">
                  <input
                    type="date"
                    value={from}
                    onChange={(e) => setFilter(setFrom)(e.target.value)}
                    aria-label="Du"
                    className="rounded-lg border bg-transparent px-2 py-1.5 text-[13px] tabular-nums"
                  />
                  <span className="text-xs text-muted-foreground">au</span>
                  <input
                    type="date"
                    value={to}
                    onChange={(e) => setFilter(setTo)(e.target.value)}
                    aria-label="Au"
                    className="rounded-lg border bg-transparent px-2 py-1.5 text-[13px] tabular-nums"
                  />
                </span>
              </div>

              <ReminderLogTable
                rows={rows}
                loading={loading}
                refreshing={refreshing}
                isFiltered={isFiltered}
                onResetFilters={resetFilters}
                noChannelConfigured={looksUnconfigured}
                onConfigure={isAdmin ? () => setConfigOpen(true) : undefined}
              />

              {data && data.page.totalCount > 0 && (
                <DataTablePagination page={data.page} onPageChange={setPage} onPageSizeChange={setPageSize} />
              )}
            </div>
          </main>
        </div>

        {/*
          The configuration, unchanged in substance — the existing `ReminderSettings` component, moved rather than
          rewritten. It carries working logic for two channels, secret handling, lead times, the message template
          and the WhatsApp Embedded-Signup flow; reimplementing 768 lines to change where it lives would be a
          second implementation of all of it, which is how the two would drift.
        */}
        <Sheet open={configOpen} onOpenChange={setConfigOpen}>
          <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-xl">
            <SheetHeader>
              <SheetTitle>Canaux de rappel</SheetTitle>
              <SheetDescription>
                Réglages propres à cette clinique. Les champs laissés vides héritent de la configuration de
                l&apos;installation.
              </SheetDescription>
            </SheetHeader>
            <div className="px-4 pb-6">
              <ReminderSettings />
            </div>
          </SheetContent>
        </Sheet>
      </div>
    </ClinicGuard>
  )
}

const TONE_DOT = {
  success: "bg-success",
  warning: "bg-warning",
  destructive: "bg-destructive",
} as const

/**
 * The status chips carry their counts, so the cost of a filter is visible before it is applied.
 *
 * ⚠️ The counts come from the **clinic-wide** counters, not from the rows on screen — deriving them from
 * `page.items` would render « les échecs parmi ces 25 ». « Tous » deliberately shows none: the total is already in
 * the page subtitle, and repeating it on a chip that removes filters would read as a fourth category.
 */
const STATUS_CHIPS: {
  value: StatusFilter
  label: string
  count: (d: ReminderLogDto | null) => number | undefined
}[] = [
  { value: "all", label: "Tous", count: () => undefined },
  { value: "sent", label: "Envoyés", count: (d) => d?.sentToday },
  { value: "pending", label: "En attente", count: (d) => d?.pending },
  { value: "failed", label: "Échecs", count: (d) => d?.failedRecent },
]

/** `yyyy-MM-dd` n days back, through local date parts — never `toISOString`, which converts to UTC first. */
function isoDaysAgo(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days)
  const m = String(d.getMonth() + 1).padStart(2, "0")
  const day = String(d.getDate()).padStart(2, "0")
  return `${d.getFullYear()}-${m}-${day}`
}

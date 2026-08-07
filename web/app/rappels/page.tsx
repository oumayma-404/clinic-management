"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { SlidersHorizontal } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from "@/components/ui/sheet"
import { KpiGrid } from "@/components/dashboard/kpi-grid"
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
    if (incoming === "sent" || incoming === "pending" || incoming === "failed" || incoming === "blocked") {
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
      /*
       * L3a — « Bloqués » is the counter this page was missing, and the reason a whole install's queue could stop
       * sending with nothing on any screen to say so. A blocked row is not waiting its turn: it needs a setting
       * changed, and the reason is printed on the row itself.
       *
       * It is a counter rather than a banner because it is a figure of the same kind as the other three, and
       * because zero is the normal reading — a banner that is absent 99 % of the time is a banner nobody learns
       * to look for.
       */
      { key: "blocked", label: "Bloqués", value: data?.blocked, tone: "blocked" as const },
      // Several days, not today: a send that failed at 23:00 must still be counted the next morning.
      { key: "failed", label: "Échecs (7 j)", value: data?.failedRecent, tone: "destructive" as const },
    ],
    [data],
  )

  return (
    <ClinicGuard>
      <AppShell contentClassName="flex flex-col gap-6">

        {/* PageHeader: mono zone crumb, one title size, a subtitle carrying a FACT, actions right. */}
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <p className="font-mono text-2xs uppercase tracking-[0.1em] text-muted-foreground">
              Opérations
            </p>
            <h1 className="mt-1 text-title font-[650] leading-tight tracking-[-0.022em]">Rappels</h1>
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

        {/*
          Counters on the shared `KpiGrid` surface — the same object « Factures » and la caisse draw their
          figures on. This grid was hand-rolled from the identical `gap-px bg-border` idiom but WITHOUT
          `shadow-sm`, which is how the widest surface on the page ended up flatter than the cards below it;
          and its value was `font-[650] tracking-[-0.01em]` where la caisse's was `font-semibold
          tracking-tight` — two hand-tuned near-misses for the same treatment.
        */}
        {/* Four columns now, and `sm:grid-cols-2` before them: four figures at 320 px would be four 80 px
            columns, and « Envoyés aujourd'hui » does not fit in one. Two-up on a phone, four-up from `lg:`. */}
        <KpiGrid columns={4} className="sm:grid-cols-2 lg:grid-cols-4">
          {counters.map((c) => (
            <div key={c.key} className="flex flex-col gap-0.5 bg-card p-4">
              <span className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                <i aria-hidden="true" className={cn("size-1.5 shrink-0 rounded-full", TONE_DOT[c.tone])} />
                {c.label}
              </span>
              {c.value === undefined ? (
                <span className="h-7 w-12 animate-pulse rounded bg-muted" aria-label="Chargement" />
              ) : (
                <span
                  className={cn(
                    "text-2xl font-semibold tabular-nums tracking-tight",
                    // Only the two actionable counters can colour up, and only when non-zero: a red or amber
                    // zero would raise an alarm about nothing. « Bloqués » is amber, not red — nothing failed,
                    // a setting is missing — but it is emphasised, because a non-zero here means messages are
                    // not going out at all.
                    c.tone === "destructive" && c.value > 0 && "text-destructive",
                    c.tone === "blocked" && c.value > 0 && "text-warning-ink",
                  )}
                >
                  {c.value.toLocaleString("fr-TN")}
                </span>
              )}
            </div>
          ))}
        </KpiGrid>

        {/* ListToolbar: only what NARROWS the list. Counted chips, so an active filter is visible as a
            state rather than having to be read out of a changing button label. */}
        <div className="flex flex-wrap items-center gap-2 border-b pb-3">
          {/* `touch-target` gives each chip a 44px hit area on a finger without repainting it — these are ~28px
              tall, and this page is read on the tablet at the desk (AC-10). */}
          {STATUS_CHIPS.map((chip) => (
            <button
              key={chip.value}
              type="button"
              aria-pressed={status === chip.value}
              onClick={() => setFilter(setStatus)(chip.value)}
              className={cn(
                "touch-target inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm transition-colors",
                status === chip.value
                  ? "border-primary bg-accent font-semibold text-accent-foreground"
                  : "border-border text-muted-foreground hover:bg-accent/50",
              )}
            >
              {chip.label}
              {chip.count(data) !== undefined && (
                <span className="font-mono text-2xs opacity-75 tabular-nums">{chip.count(data)}</span>
              )}
            </button>
          ))}

          {/* The shared primitives, not a raw `<select>` and two raw `<input type="date">`. These were the only
              controls in the app rendering with browser-default chrome — a native dropdown arrow and a native
              date widget sitting beside shadcn fields — and they carried neither the focus ring nor the 44px
              coarse-pointer floor the primitives already own. */}
          <Select value={channel} onValueChange={(v) => setFilter(setChannel)(v as ChannelFilter)}>
            <SelectTrigger size="sm" className="w-44" aria-label="Canal">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tous les canaux</SelectItem>
              <SelectItem value="SMS">SMS</SelectItem>
              <SelectItem value="WhatsApp">WhatsApp</SelectItem>
            </SelectContent>
          </Select>

          <span className="flex items-center gap-1.5">
            <Input
              type="date"
              value={from}
              onChange={(e) => setFilter(setFrom)(e.target.value)}
              aria-label="Du"
              className="h-8 w-auto tabular-nums"
            />
            <span className="text-xs text-muted-foreground">au</span>
            <Input
              type="date"
              value={to}
              onChange={(e) => setFilter(setTo)(e.target.value)}
              aria-label="Au"
              className="h-8 w-auto tabular-nums"
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

        {/*
        The configuration, unchanged in substance — the existing `ReminderSettings` component, moved rather than
        rewritten. It carries working logic for two channels, secret handling, lead times, the message template
        and the WhatsApp Embedded-Signup flow; reimplementing 768 lines to change where it lives would be a
        second implementation of all of it, which is how the two would drift.
        */}
        <Sheet open={configOpen} onOpenChange={setConfigOpen}>
        {/*
          ⚠️ The scroll belongs to an INNER wrapper, never to `SheetContent` itself. `ui/sheet.tsx` pins its ✕ at
          `absolute top-4 right-4` against the content element, so scrolling the content scrolls the close button
          out of the viewport — and `ReminderSettings` is a 768-line form. On a phone, with no Escape key, that
          leaves an overlay tap as the only way out of a full-height sheet. `min-h-0` is what lets a flex child
          actually shrink and scroll instead of growing past its parent.
        */}
        <SheetContent side="right" className="w-full sm:max-w-xl">
          <SheetHeader>
            <SheetTitle>Canaux de rappel</SheetTitle>
            <SheetDescription>
              Réglages propres à cette clinique. Les champs laissés vides héritent de la configuration de
              l&apos;installation.
            </SheetDescription>
          </SheetHeader>
          <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-6">
            <ReminderSettings />
          </div>
        </SheetContent>
        </Sheet>
      </AppShell>
    </ClinicGuard>
  )
}

const TONE_DOT = {
  success: "bg-success",
  warning: "bg-warning",
  destructive: "bg-destructive",
  // Same amber as « En attente » — a blocked row has not failed, it is waiting on a setting. The label and the
  // reason on each row carry the distinction; a fourth hue would imply a fourth kind of severity.
  blocked: "bg-warning",
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
  // L3a — the filter that turns the counter into a worklist: « 12 bloqués » is only useful if one tap lists
  // which twelve, with the reason on each row.
  { value: "blocked", label: "Bloqués", count: (d) => d?.blocked },
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

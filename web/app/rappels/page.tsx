"use client"

import { useCallback, useEffect, useState } from "react"
import { SlidersHorizontal, X } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { PageHeader } from "@/components/ui/page-header"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription } from "@/components/ui/sheet"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import {
  DELIVERY_LABEL_PLURAL,
  DELIVERY_TONE,
  asStatusFilter,
  type StatusFilter,
} from "@/components/rappels/delivery-tone"
import { ReminderCounters } from "@/components/rappels/reminder-counters"
import { ReminderLogTable } from "@/components/rappels/reminder-log-table"
import { MessagingAllowanceCard } from "@/components/rappels/messaging-allowance-card"
import { WhatsAppConnectCard } from "@/components/rappels/whatsapp-connect-card"
import { MessagingAllowanceHistory } from "@/components/rappels/messaging-allowance-history"
import { ReminderSettings } from "@/components/reminder-settings"
import { reminderSettingsApi, type ReminderLogDto } from "@/lib/api/reminder-settings"
import {
  reminderAllowanceApi,
  type ReminderAllowanceDto,
  type ReminderAllowanceHistoryDto,
} from "@/lib/api/reminder-allowance"
import { ApiError } from "@/lib/api/client"
import { useSession } from "@/lib/auth/session"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { showErrorToast } from "@/lib/errors"
import { quoteFr, todayLocalIso } from "@/lib/format"
import { cn } from "@/lib/utils"

/** How far back the log looks by default. Wide enough that last week's failure is on the first screen. */
const DEFAULT_WINDOW_DAYS = 14

type ChannelFilter = "all" | "SMS" | "WhatsApp"

/**
 * « Rappels » — the delivery log for outbound patient messages, with the channel configuration behind it.
 *
 * <p>The configuration used to be a card at the bottom of « Paramètres », with a twenty-row status list under it.
 * That got the priority backwards: credentials are set roughly once, whereas « est-ce que ce patient a bien reçu
 * son SMS ? » is asked every day. Here the log <b>is</b> the page and the configuration is a button.</p>
 *
 * <p><b>The order of the page is the whole design</b>, and it was wrong twice over before this pass. Three cards —
 * the WhatsApp connection, the forfait figures and the monthly history — sat between the filters and the log, so
 * the thing the screen exists for started at the <i>seventh</i> block. And the four counters were flat white cells
 * restating the numbers that the five status chips directly beneath them <i>also</i> carried. Now:</p>
 * <ol>
 *   <li>the counters carry the colour and <b>are</b> the filter, so the chips are gone;</li>
 *   <li>the forfait keeps its place at the top as one line with a meter — « combien me reste-t-il ? » is the
 *       question a secretary arrives with — while the connection and the history move down;</li>
 *   <li>the log is the fifth block, above the fold on a desk machine;</li>
 *   <li>« Configuration » closes the page, on the quiet end of the palette, because it is read rarely.</li>
 * </ol>
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

  /*
   * The forfait section's own state, tri-state on availability (AC-1.6, EC-16).
   *
   * `available === null` is « we have not asked yet »; `false` is the deployment answering 404, i.e. it does not do
   * vendor-purchased messaging and the whole section is **absent**. Only an explicit 404 means that — every other
   * failure (a network drop is `ApiError(0)`) is a retryable « je n'ai pas pu lire », because « cette installation ne
   * fonctionne pas comme ça » and « la lecture a échoué » are opposite facts with the same blank picture. This is
   * `/abonnement`'s own rule, one screen over.
   */
  const [available, setAvailable] = useState<boolean | null>(null)
  const [allowance, setAllowance] = useState<ReminderAllowanceDto | null>(null)
  const [allowanceHistory, setAllowanceHistory] = useState<ReminderAllowanceHistoryDto | null>(null)
  const [allowanceLoading, setAllowanceLoading] = useState(true)
  const [allowanceError, setAllowanceError] = useState<string | null>(null)
  // `loading` is first paint only; a filter change sets `refreshing` and keeps the rows on screen. Blanking the
  // table on every keystroke or chip tap is what makes a filtered list feel like it is reloading the page.
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  /** Band C — the log read failed. Distinct from « no rows », which is a fact about the practice. */
  const [logFailed, setLogFailed] = useState(false)

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
      setLogFailed(false)
    } catch (error) {
      // Band C — recorded as a FAILURE, not only toasted. A toast expires and leaves « Aucun message pour le
      // moment » on screen, which is a claim about the practice's reminders made out of a network error.
      setLogFailed(true)
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
   * rather than refusing — which is what `asStatusFilter` returning `null` means.
   */
  useEffect(() => {
    const incoming = asStatusFilter(new URLSearchParams(window.location.search).get("status"))
    if (incoming) {
      setStatus(incoming)
      window.history.replaceState({}, "", "/rappels")
    }
  }, [])

  /**
   * The forfait section's read. Both endpoints together, because the strip and the history are one subject on
   * screen — even now that they sit at opposite ends of the page — and a half-loaded pair is worse than a retry.
   */
  const loadAllowance = useCallback(async () => {
    setAllowanceLoading(true)
    try {
      const [current, history] = await Promise.all([
        reminderAllowanceApi.current(),
        reminderAllowanceApi.history(),
      ])
      setAllowance(current)
      setAllowanceHistory(history)
      setAllowanceError(null)
      setAvailable(true)
    } catch (error) {
      // Only an explicit 404 means « this deployment does not do vendor messaging » — see the state's own note.
      if (error instanceof ApiError && error.status === 404) {
        setAvailable(false)
        setAllowanceError(null)
      } else {
        setAllowanceError(
          error instanceof Error ? error.message : "Le forfait de rappels n'a pas pu être lu.",
        )
        // Leave `available` as it was: a failed read says nothing about whether the feature exists here, and
        // flipping it to false would hide the section AND the retry that would fix it.
      }
    } finally {
      setAllowanceLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    void loadAllowance()
  }, [loadAllowance])

  // A dispatched or failed reminder changes this page under the viewer, and the job runs every minute.
  useClinicRealtime(RealtimeResource.Clinics, load)

  /*
   * The forfait refreshes on the same key as the log above, and deliberately gets **no key of its own**:
   * `Features.Messaging` contains queries only, so it emits nothing — and `RealtimeResourceResolverTests` asserts the
   * emitted and declared key sets are equal in both directions, so declaring one here would fail the build.
   *
   * ⚠️ What that means in practice: the figures follow a peer's *settings* edit, and the warning rows arrive on the
   * bell's own `notifications` key. A send incrementing the counter is a background job with no command behind it, so
   * nothing broadcasts for it — the numbers catch up on the next thing that does, or on a reload. That is a fact worth
   * knowing rather than a gap to paper over with a poll: the practice reads this screen to decide something, not to
   * watch it tick.
   */
  useClinicRealtime(RealtimeResource.Clinics, loadAllowance)

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

  /*
   * AC-4.9 — « en attente de forfait », counted apart from the undifferentiated « Bloqués » it is a subset of.
   *
   * It is a sentence under the counters rather than a fifth tile, and that is deliberate twice over: four figures at
   * 320 px are already two rows of two, and — more to the point — a fifth counter would present it as a fifth *state*
   * when the rows are `Blocked` like any other. What distinguishes them is the reason, which is why it reads as a
   * breakdown of the number above it. Absent entirely at zero, which is the normal reading.
   */
  const heldByAllowance = data?.heldByAllowance ?? 0
  /**
   * Blocked by the SENDER, not by the forfait — an unapproved template or a number Meta has stopped.
   *
   * ⚠️ Its own sentence, because its own remedy. These rows were inside `heldByAllowance`, so a reminder the log's
   * own badge reads « numéro » told the practice it was « en attente de forfait » — and « ils partiront dès que
   * nous augmentons votre forfait » is then a promise about the wrong thing entirely.
   */
  const heldBySender = data?.heldBySender ?? 0

  return (
    <ClinicGuard>
      <AppShell contentClassName="flex flex-col gap-6">
        {/*
          The shared `PageHeader`, which this page used to hand-roll — and lost three things by doing so: the
          zone's 44 px icon chip, the 128 px band that carries the hue behind the title, and the `font-semibold`
          every other page's title uses (this one had drifted to `font-[650]`). Its eyebrow is the zone's own
          label from `lib/zones.ts`, so it now reads « Gestion » — the rail's group heading for this route —
          rather than the « Opérations » that was typed here and matched nothing.
        */}
        <PageHeader
          title="Rappels"
          subtitle={
            data
              ? `${data.page.totalCount.toLocaleString("fr-TN")} message${data.page.totalCount === 1 ? "" : "s"} sur la période · SMS et WhatsApp`
              : "Messages envoyés aux patients — SMS et WhatsApp."
          }
          actions={
            isAdmin && (
              <Button variant="outline" className="gap-2" onClick={() => setConfigOpen(true)}>
                <SlidersHorizontal className="h-4 w-4" />
                Configurer les canaux
              </Button>
            )
          }
        />

        {/* The counters, which are also the filter — see `reminder-counters.tsx` for why the status chips that
            used to sit under them are gone. */}
        <ReminderCounters data={data} status={status} onPick={setFilter(setStatus)} />

        {heldByAllowance > 0 && (
          <p role="status" className="-mt-2 text-sm text-warning-ink">
            {heldByAllowance === 1
              ? "1 rappel est en attente de forfait"
              : `${heldByAllowance.toLocaleString("fr-TN")} rappels sont en attente de forfait`}{" "}
            — ils partiront dès que nous augmentons votre forfait WhatsApp.{" "}
            <button
              type="button"
              onClick={() => setFilter(setStatus)("blocked")}
              className="touch-target underline"
            >
              Voir lesquels
            </button>
          </p>
        )}

        {heldBySender > 0 && (
          <p role="status" className="-mt-2 text-sm text-warning-ink">
            {heldBySender === 1
              ? "1 rappel WhatsApp est bloqué par la connexion"
              : `${heldBySender.toLocaleString("fr-TN")} rappels WhatsApp sont bloqués par la connexion`}{" "}
            — le modèle de message n'est pas encore validé, ou le numéro a été suspendu. Ce n'est pas une question
            de forfait&nbsp;: nous nous en occupons.{" "}
            <button
              type="button"
              onClick={() => setFilter(setStatus)("blocked")}
              className="touch-target underline"
            >
              Voir lesquels
            </button>
          </p>
        )}

        {/*
          US-2 — the forfait, still above the log because « combien me reste-t-il ? » is the question a secretary
          arrives with. It is one line with a meter now instead of three big figures in a card; the connection
          (US-1) and the monthly history moved to « Configuration » at the foot of the page, which is what let the
          log come up.

          ⚠️ Rendered only where the deployment answered something other than 404 (AC-1.6, EC-16): on a clinic's own
          PC and on the Auth0 deployment there is no strip, no button and no message at all — absent, not
          present-and-refusing. `available === null` still renders, so it shows its own skeleton on first paint
          instead of appearing a beat late.
        */}
        {available !== false && (
          <MessagingAllowanceCard
            data={allowance}
            loading={allowanceLoading}
            error={allowanceError}
            onRetry={() => void loadAllowance()}
          />
        )}

        {/* ListToolbar: only what NARROWS the list, which is now the channel and the period. The status chips
            moved into the counters above; what remains of them here is the one pill that says a status filter is
            on and takes it off again. */}
        <div className="flex flex-wrap items-center gap-2 border-b pb-3">
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

          {/* ⚠️ `flex-wrap` on the pair, not just on the toolbar around it. A `type="date"` field has a native
              intrinsic width (~170 px in Chrome) it will not go below, so the two of them plus « au » measure
              ~366 px — wider than the 343 px content box of a 390 px phone. As one unwrappable group the span
              could neither shrink nor break, and the whole page scrolled sideways. Wrapping lets the second
              field drop to its own line; `max-w-full` is the floor for the narrowest case (320 px), where even
              one field is close to the full width. */}
          <span className="flex flex-wrap items-center gap-1.5">
            <Input
              type="date"
              value={from}
              onChange={(e) => setFilter(setFrom)(e.target.value)}
              aria-label="Du"
              className="h-8 w-auto max-w-full tabular-nums"
            />
            <span className="text-xs text-muted-foreground">au</span>
            <Input
              type="date"
              value={to}
              onChange={(e) => setFilter(setTo)(e.target.value)}
              aria-label="Au"
              className="h-8 w-auto max-w-full tabular-nums"
            />
          </span>

          {/*
            The active status filter, in its own tone, with the only way to clear it. The pressed tile above says
            the same thing — but a counter says « voici combien », and this says « et c'est ce que vous regardez
            en ce moment », which is the part that has to be undoable from where the list is.

            `aria-label` overrides the visible text on purpose: the label alone would announce « Bloqués » on a
            control whose job is to remove that filter.
          */}
          {status !== "all" && (
            <button
              type="button"
              aria-label={`Retirer le filtre ${quoteFr(DELIVERY_LABEL_PLURAL[status])}`}
              onClick={() => setFilter(setStatus)("all")}
              className={cn(
                "touch-target inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full px-3 text-sm font-medium",
                STATUS_TONE_CLASS[DELIVERY_TONE[status]],
              )}
            >
              {DELIVERY_LABEL_PLURAL[status]}
              <X aria-hidden="true" className="size-3.5" />
            </button>
          )}
        </div>

        <ReminderLogTable
          rows={rows}
          loading={loading}
          refreshing={refreshing}
          isFiltered={isFiltered}
          onResetFilters={resetFilters}
          noChannelConfigured={looksUnconfigured}
          onConfigure={isAdmin ? () => setConfigOpen(true) : undefined}
          loadFailed={logFailed}
          onRetry={load}
        />

        {data && data.page.totalCount > 0 && (
          <DataTablePagination page={data.page} onPageChange={setPage} onPageSizeChange={setPageSize} />
        )}

        {/*
          « Configuration » — the two surfaces that are set up once and then consulted rarely, which is exactly
          why they are last. They were above the log, where they pushed it off the first screen every day for the
          sake of a question asked twice a year.

          The eyebrow is deliberately `text-muted-foreground` and **not** a zone hue: `lib/zones.ts` lists the
          five places a zone colour may appear and a section heading is not one of them. Nothing is lost — the
          Configuration zone's own hue is near-neutral by design (chroma 0.02), so this is the colour it would
          have been anyway.

          Gated as a whole on `available !== false`: with no vendor messaging here there is nothing inside it, and
          a heading over an empty grid reads as a failure to load. The « Configurer les canaux » button lives in
          the page header at every width, so nothing is out of reach when this section is absent.
        */}
        {available !== false && (
          <section aria-labelledby="rappels-configuration" className="flex flex-col gap-4 border-t pt-6">
            <div>
              <p className="flex items-center gap-1.5 font-mono text-2xs font-medium uppercase tracking-[0.1em] text-muted-foreground">
                <span aria-hidden="true" className="size-1.5 shrink-0 rounded-full bg-muted-foreground/50" />
                Configuration
              </p>
              <h2 id="rappels-configuration" className="mt-1 text-base font-semibold">
                Canaux et forfait
              </h2>
              <p className="mt-0.5 max-w-[56ch] text-sm text-muted-foreground">
                Réglé une fois, consulté rarement — d&apos;où sa place sous le journal.
              </p>
            </div>

            {/*
              `items-start` matters: the connection card is three lines and the history is a twelve-row table, so
              stretching them to a common height would leave the short one with a lake of empty card under it.
            */}
            <div className="grid items-start gap-4 lg:grid-cols-2">
              {/*
                US-1 — the connection. It renders its own five states in words and offers the button only to an
                admin (AC-1.1/1.4), and returns nothing at all while `allowance` is still null, which is why it
                needs no skeleton of its own here.
              */}
              <WhatsAppConnectCard data={allowance} isAdmin={isAdmin} onConnected={() => void loadAllowance()} />
              <MessagingAllowanceHistory
                data={allowanceHistory}
                loading={allowanceLoading}
                error={allowanceError}
                onRetry={() => void loadAllowance()}
              />
            </div>
          </section>
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

/** `yyyy-MM-dd` n days back, through local date parts — never `toISOString`, which converts to UTC first. */
function isoDaysAgo(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days)
  const m = String(d.getMonth() + 1).padStart(2, "0")
  const day = String(d.getDate()).padStart(2, "0")
  return `${d.getFullYear()}-${m}-${day}`
}

"use client"

import { useCallback, useEffect, useState } from "react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { Label } from "@/components/ui/label"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"
import { Badge } from "@/components/ui/badge"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { VisitClosureList } from "@/components/visits/visit-closure-list"
import { UnfinishedActsList } from "@/components/visits/unfinished-acts-list"
import { ResteAPayerList } from "@/components/visits/reste-a-payer-list"
import { useMoneyVisibility } from "@/lib/money-visibility/money-visibility-context"
import { cn } from "@/lib/utils"
import { ClipboardCheck, CircleDashed, Wallet } from "lucide-react"
import { Button } from "@/components/ui/button"
import { CalendarImportUndoBanner } from "@/components/visits/calendar-import-undo-banner"
import { appointmentsApi, type VisitsToCloseResponse } from "@/lib/api/appointments"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import { getErrorMessage } from "@/lib/errors"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/**
 * « À clôturer » — the séances whose slot has passed and which still owe one of three answers.
 *
 * <p><b>Its own route, and open to every role.</b> The dashboard is `AdminOrDoctor` and `/` sends a secretary to
 * `/appointments`, so a worklist living only on the dashboard would be invisible to reception — the person who
 * knows whether the patient came and who takes the money. The dashboard chip and the agenda strip both land here.</p>
 *
 * <p><b>Nothing here is stored.</b> A visit is open because a record is *absent*, so this list cannot drift from
 * reality and needs no task table to maintain — see `VisitClosureRules` server-side.</p>
 */

/**
 * Windows offered. Mirrors `VisitClosureReader`'s clamp; the server is the authority and re-clamps anyway.
 *
 * ⚠️ « Toutes les dates » is the **default**, and `all` sends no `days` at all — an absent window means every date
 * server-side. A 7-day default was the wrong one for what this list is: a séance nobody closed is not *less* open
 * for being three weeks old, it is the one most likely to have been forgotten and to have money still on it, and
 * a window that hides it also subtracts it from the count at the top of the page. So the practice was told it had
 * nothing left to do while the oldest rows sat outside the window. Narrowing is now something you reach for.
 */
const ALL_DATES = "all"

const WINDOWS = [
  { value: ALL_DATES, label: "Toutes les dates" },
  { value: "7", label: "7 derniers jours" },
  { value: "14", label: "14 derniers jours" },
  { value: "30", label: "30 derniers jours" },
  { value: "90", label: "90 derniers jours" },
] as const

export default function VisitsToClosePage() {
  const [days, setDays] = useState<string>(ALL_DATES)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [data, setData] = useState<VisitsToCloseResponse | null>(null)
  /**
   * Showing the séances somebody has taken off the list, rather than the ones still asking a question.
   *
   * ⚠️ The way back is not optional. A removal nobody can see or undo is a black hole, and a worklist with one
   * in it is a worklist people stop trusting — which is the whole reason « Retirer de la liste » can be offered
   * in bulk at all.
   */
  const [showDisregarded, setShowDisregarded] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await appointmentsApi.visitsToClose({
        // Omitted, not zero: the server reads an absent window as « toutes les dates », while 0 would clamp to 1.
        days: days === ALL_DATES ? undefined : Number(days),
        disregarded: showDisregarded || undefined,
        page,
        pageSize,
      })

      // Closing the last séance of page 2 leaves that page empty while the list still has rows: `PageRequest`
      // clamps the page *size* and deliberately does not clamp a page past the end. Rendering it would print
      // « Rien à clôturer » — a false statement — under a pager reading « 26–26 sur 26 ». Step back instead.
      if (result.visits.items.length === 0 && result.visits.totalCount > 0 && page > 1) {
        setPage(Math.min(page - 1, Math.max(1, result.visits.totalPages)))
        return
      }

      setData(result)
      setError(null)
    } catch (err) {
      // § 13 — a failed read must NEVER render as an empty list. « Aucune séance à clôturer » and « je n'ai pas
      // pu lire » are the same picture and opposite facts, and here the wrong one is actively reassuring.
      setError(getErrorMessage(err))
      setData(null)
    } finally {
      setLoading(false)
    }
  }, [days, showDisregarded, page, pageSize])

  useEffect(() => {
    void load()
  }, [load])

  // Every key whose mutation can close a visit or reveal a new one: a peer marking presence, recording a fiche,
  // issuing a note d'honoraires, or accepting the devis that covers the séance.
  useClinicRealtime(
    [
      RealtimeResource.Appointments,
      RealtimeResource.Patients,
      RealtimeResource.Invoices,
      RealtimeResource.TreatmentPlans,
    ],
    load,
  )

  /**
   * « Suites à planifier »'s own total, reported up by the list so its trigger can carry a figure.
   *
   * ⚠️ `null` is « je n'ai pas pu lire » and renders as « — », never as 0 — a tab hides its half by definition,
   * so a zero on the door is a statement the page is not entitled to make after a failed read.
   */
  const [unfinishedCount, setUnfinishedCount] = useState<number | null>(0)

  // « Reste à payer »: every role sees it, and it hides with the rest of the money under « Mode discret ».
  const { moneyShown } = useMoneyVisibility()
  const [tab, setTab] = useState("visits")
  const [resteDueCount, setResteDueCount] = useState<number | null>(0)
  useEffect(() => {
    if (!moneyShown && tab === "reste") setTab("visits")
  }, [moneyShown, tab])

  /** The page of séances, whichever half is being shown. */
  const visits = data?.visits ?? null

  const toggleDisregarded = () => {
    setShowDisregarded((shown) => !shown)
    // A different list; keeping page 4 would land past its end and read as « rien à clôturer ».
    setPage(1)
  }

  const changeWindow = (value: string) => {
    setDays(value)
    // A wider window is a different list; keeping page 4 would land past its end and read as « rien à clôturer ».
    setPage(1)
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="À clôturer"
          /* The séances left to close, over every date — a badge whose figure depends on a filter can be quietly wrong. */
          titleBadge={
            visits && !showDisregarded ? (
              <Badge variant="secondary" className="tabular-nums">
                {visits.totalCount.toLocaleString("fr-TN")}
              </Badge>
            ) : undefined
          }
          subtitle={
            !visits
              ? undefined
              : showDisregarded
                // Said plainly, because this view looks exactly like the worklist and means the opposite: these
                // rows are not waiting for anybody.
                // ⚠️ The whole clause agrees, not just the noun: « 1 séance retirée … elles ne sont comptées »
                // is what pluralising in one place gives. French ZERO takes the singular too, which is why the
                // test is `<= 1` rather than `=== 1` — the same slip the day heading on this page already fixed.
                ? visits.totalCount <= 1
                  ? `${visits.totalCount.toLocaleString("fr-TN")} séance retirée de la liste — elle n’est comptée dans aucun chiffre`
                  : `${visits.totalCount.toLocaleString("fr-TN")} séances retirées de la liste — elles ne sont comptées dans aucun chiffre`
                // ⚠️ `<= 1`, not `=== 1`: in French ZERO takes the singular, so « 0 séances » was wrong — and the
                // day heading beside it on the same page already got this right (`group.visits.length > 1`).
                : `${visits.totalCount.toLocaleString("fr-TN")} séance${visits.totalCount <= 1 ? "" : "s"} en attente d’une présence, d’une fiche ou d’un encaissement`
          }
          actions={
            <div className="flex items-center gap-2">
              <Label htmlFor="closure-window" className="text-xs font-medium text-muted-foreground">
                Période
              </Label>
              <Select value={days} onValueChange={changeWindow}>
                <SelectTrigger id="closure-window" className="h-9 w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {WINDOWS.map((w) => (
                    <SelectItem key={w.value} value={w.value}>
                      {w.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          }
        />

        {/*
          The active trigger carries its zone's wash (entry 6 on `lib/zones.ts`' list), the ACTIVE mark only.
          ⚠️ Each trigger carries its count: a tab hides its half, and « 0 » is a statement worth making.
        */}
        {/*
          ⚠️ The undo is offered HERE, and that is the whole point of where it lives. A cabinet that regrets
          « Importer depuis Google » is looking at this page — that is where the damage shows — and it will not
          go hunting through the settings for the way out. The banner withdraws itself once the run is undone or
          its rows are gone, so it never becomes furniture.
        */}
        <CalendarImportUndoBanner onReverted={() => void load()} />

        <Tabs value={tab} onValueChange={setTab} className="space-y-4">
          {/*
            ⚠️ **`flex-wrap` + a real `basis`, because a THIRD tab made the two that shipped clip.**
            `TabsTrigger` is `whitespace-nowrap`, and `flex-1` is `flex: 1 1 0%` — a zero basis, so the
            triggers can never trigger the wrap and simply divide whatever is there. Measured at 320 px:
            three triggers took 86 px each out of a 273 px strip while « Séances 281 » needed 94 and
            « À compléter 0 » needed 98, so both were cut mid-word — a label clipped to « à compléter »
            names something else, and the count beside it is the figure the tab exists to show. A basis
            lets a row hold what fits and move the rest down, which also survives a fourth tab and a
            longer label without being re-measured.
          */}
          <TabsList className="flex h-auto w-full flex-wrap items-stretch gap-1 p-1 sm:justify-start">
            {/* ⚠️ The labels are SHORTENED below `sm:`, with the full phrase kept as the accessible name.
                `TabsTrigger` is `whitespace-nowrap`, so at 320 px the two labels plus their badges measured wider
                than the 288 px content box and « Séances » was clipped to « s 32 » — the strip overflowed and the
                active tab had scrolled it out. Same trick as `odontogram.tsx`'s « Créer un plan »: shorten what is
                drawn, never what is announced. */}
            <TabsTrigger
              value="visits"
              aria-label="Séances à clôturer"
              className={cn(
                "h-auto min-h-9 min-w-0 grow basis-28 gap-1.5 py-1.5 leading-tight coarse:min-h-11 sm:flex-none sm:basis-auto sm:gap-2",
                "data-[state=active]:bg-zone-daily/12 data-[state=active]:text-zone-daily",
              )}
            >
              <ClipboardCheck className="hidden h-4 w-4 shrink-0 sm:block" />
              Séances
              <Badge variant="secondary" className="ms-0.5 shrink-0 tabular-nums">
                {(visits?.totalCount ?? 0).toLocaleString("fr-TN")}
              </Badge>
            </TabsTrigger>
            {/*
              ⚠️ A THIRD tab rather than a fourth question in the cascade beside it — see
              `UnfinishedActsList`'s own note for the whole argument. The short form: a séance holding an
              unfinished act is completely closed as a *visit*, so folding it into « Séances » would make a row
              that answering the visit's own three questions can never clear, and would change what the page's
              count and the dashboard chip behind it are counting. What the practice asked for is one screen
              showing everything still owed, and a tab delivers that without moving the cascade.
            */}
            <TabsTrigger
              value="unfinished"
              aria-label="Suites à planifier"
              className={cn(
                "h-auto min-h-9 min-w-0 grow basis-28 gap-1.5 py-1.5 leading-tight coarse:min-h-11 sm:flex-none sm:basis-auto sm:gap-2",
                "data-[state=active]:bg-zone-clinical/12 data-[state=active]:text-zone-clinical",
              )}
            >
              <CircleDashed className="hidden h-4 w-4 shrink-0 sm:block" />
              {/* Shortened below `sm:` with the full phrase kept as the accessible name — the strip is
                  `whitespace-nowrap`, and three full labels plus their badges overflow a 320 px content box. */}
              <span className="sm:hidden">Suites</span>
              <span className="hidden sm:inline">Suites à planifier</span>
              <Badge variant="secondary" className="ms-0.5 shrink-0 tabular-nums">
                {unfinishedCount === null ? "—" : unfinishedCount.toLocaleString("fr-TN")}
              </Badge>
            </TabsTrigger>
            {moneyShown && (
              <TabsTrigger
                value="reste"
                aria-label="Reste à payer"
                className={cn(
                  "h-auto min-h-9 min-w-0 grow basis-28 gap-1.5 py-1.5 leading-tight coarse:min-h-11 sm:flex-none sm:basis-auto sm:gap-2",
                  "data-[state=active]:bg-zone-money/12 data-[state=active]:text-zone-money",
                )}
              >
                <Wallet className="hidden h-4 w-4 shrink-0 sm:block" />
                <span className="sm:hidden">À payer</span>
                <span className="hidden sm:inline">Reste à payer</span>
                <Badge variant="secondary" className="ms-0.5 shrink-0 tabular-nums">
                  {resteDueCount === null ? "—" : resteDueCount.toLocaleString("fr-TN")}
                </Badge>
              </TabsTrigger>
            )}
          </TabsList>

          {/* `forceMount`: Radix unmounts an inactive panel, so its read — and the figure on its trigger — would wait for a click. */}
          <TabsContent value="unfinished" forceMount className="data-[state=inactive]:hidden">
            <UnfinishedActsList onTotalChange={setUnfinishedCount} />
          </TabsContent>

          {moneyShown && (
            <TabsContent value="reste" forceMount className="data-[state=inactive]:hidden">
              <ResteAPayerList onDueCountChange={setResteDueCount} />
            </TabsContent>
          )}

          <TabsContent value="visits">
        {error ? (
          // The shared primitive, `role="alert"` in both variants — the reader is otherwise about to take an
          // absence for a fact, and here the wrong reading (« rien à clôturer ») is actively reassuring. It
          // replaced a hand-written banner that announced itself as a mere `role="status"`.
          <LoadFailureNotice
            message={error}
            detail="Aucune séance n'a été modifiée."
            onRetry={() => void load()}
          />
        ) : (
          <VisitClosureList
            visits={visits?.items ?? []}
            loading={loading}
            disregardedView={showDisregarded}
            onChanged={load}
            // Inside the list's own surface: the pager carries a `border-t` and no border, so as a page-level
            // sibling it rendered as a filet flottant on the page ground.
            // ⚠️ `totalCount > 0` too: the pager rendered under « Rien à clôturer », so an empty state carried
            // « 0 séance » and « Par page 25 » for a list that does not exist — a third of the card at 390 px.
            // `ui/data-table-pagination.tsx`'s own doc says an empty table should not carry a pager.
            footer={
              visits && !loading && visits.totalCount > 0 ? (
                <DataTablePagination
                  page={visits}
                  onPageChange={setPage}
                  onPageSizeChange={(size) => {
                    setPageSize(size)
                    setPage(1)
                  }}
                  loading={loading}
                  label={["séance", "séances"]}
                />
              ) : null
            }
          />
        )}
            {/*
              The way back. Rendered whenever there is something set aside — or whenever we are looking at it —
              so a removal is never a one-way door. ⚠️ `min-h-11`: 44 px on a coarse pointer, per the device
              contract, and a `button` rather than a link because it changes this page rather than navigating.
            */}
            {data && (showDisregarded || data.disregardedCount > 0) ? (
              <div className="mt-3">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="w-full text-muted-foreground coarse:h-11"
                  onClick={toggleDisregarded}
                >
                  {showDisregarded
                    ? "Revenir aux séances à clôturer"
                    // ⚠️ The ARTICLE changes with the count, not only the noun: « Voir les 1 séance retirée »
                    // is what pluralising the noun alone produces, and it reads as broken French on the one
                    // control that has to look deliberate. One takes « la » and no number at all.
                    : data.disregardedCount === 1
                      ? "Voir la séance retirée"
                      : `Voir les ${data.disregardedCount.toLocaleString("fr-TN")} séances retirées`}
                </Button>
              </div>
            ) : null}
          </TabsContent>
        </Tabs>
      </AppShell>
    </ClinicGuard>
  )
}

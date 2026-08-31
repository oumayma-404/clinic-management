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
import { PendingReviewBlock } from "@/components/patients/pending-review-block"
import { cn } from "@/lib/utils"
import { ClipboardCheck, UserPlus } from "lucide-react"
import { appointmentsApi } from "@/lib/api/appointments"
import { DEFAULT_PAGE_SIZE, type PagedResponse } from "@/lib/api/paging"
import type { VisitToCloseDto } from "@/lib/api/types"
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
  const [data, setData] = useState<PagedResponse<VisitToCloseDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await appointmentsApi.visitsToClose({
        // Omitted, not zero: the server reads an absent window as « toutes les dates », while 0 would clamp to 1.
        days: days === ALL_DATES ? undefined : Number(days),
        page,
        pageSize,
      })

      // Closing the last séance of page 2 leaves that page empty while the list still has rows: `PageRequest`
      // clamps the page *size* and deliberately does not clamp a page past the end. Rendering it would print
      // « Rien à clôturer » — a false statement — under a pager reading « 26–26 sur 26 ». Step back instead.
      if (result.items.length === 0 && result.totalCount > 0 && page > 1) {
        setPage(Math.min(page - 1, Math.max(1, result.totalPages)))
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
  }, [days, page, pageSize])

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

  // The count the « Patients à compléter » tab carries. Lifted out of the block because a tab with no figure on it
  // is a door with nothing to say how much is behind it — and this half is hidden by default.
  const [pendingCount, setPendingCount] = useState(0)

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
          /* ⚠️ ONE figure, and it is the sum of both tabs: a tab hides its own half by definition, so the two
             counts on the triggers answer « how much is behind this door » and neither answers « how much is
             left ». That total is the question the page is opened with — and it is why the window now defaults
             to every date, since a badge whose figure depends on a filter can be quietly wrong. */
          titleBadge={
            data ? (
              <Badge variant="secondary" className="tabular-nums">
                {(data.totalCount + pendingCount).toLocaleString("fr-TN")}
              </Badge>
            ) : undefined
          }
          subtitle={
            data
              // ⚠️ `<= 1`, not `=== 1`: in French ZERO takes the singular, so « 0 séances » was wrong — and the
              // day heading beside it on the same page already got this right (`group.visits.length > 1`).
              ? `${data.totalCount.toLocaleString("fr-TN")} séance${data.totalCount <= 1 ? "" : "s"} en attente d’une présence, d’une fiche ou d’un encaissement`
              : undefined
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
          Two tabs rather than two stacked blocks: an imported patient is not a séance (no visit date to group
          under, not counted in « N séances »), and stacking them pushed the séances — the page's reason for
          existing — below a card that is usually empty.

          The active trigger carries its zone's wash: azure for the séances, answered on the agenda; violet for the
          patients, answered in the clinical record. Entry 6 on `lib/zones.ts`' list, and the ACTIVE mark only, so
          the hue stays a mark instead of becoming the panel's colour scheme.

          ⚠️ Each trigger carries its count. A tab hides its half by definition, so without the figure the backlog
          this page exists to surface would be behind an unremarkable door — and « 0 » is a statement worth making.
        */}
        <Tabs defaultValue="visits" className="space-y-4">
          <TabsList className="flex h-auto w-full items-stretch gap-1 p-1 sm:w-auto sm:justify-start">
            {/* ⚠️ The labels are SHORTENED below `sm:`, with the full phrase kept as the accessible name.
                `TabsTrigger` is `whitespace-nowrap`, so at 320 px the two labels plus their badges measured wider
                than the 288 px content box and « Séances » was clipped to « s 32 » — the strip overflowed and the
                active tab had scrolled it out. Same trick as `odontogram.tsx`'s « Créer un plan »: shorten what is
                drawn, never what is announced. */}
            <TabsTrigger
              value="visits"
              aria-label="Séances à clôturer"
              className={cn(
                "h-auto min-h-9 min-w-0 flex-1 gap-1.5 py-1.5 leading-tight coarse:min-h-11 sm:flex-none sm:gap-2",
                "data-[state=active]:bg-zone-daily/12 data-[state=active]:text-zone-daily",
              )}
            >
              <ClipboardCheck className="hidden h-4 w-4 shrink-0 sm:block" />
              Séances
              <Badge variant="secondary" className="ms-0.5 shrink-0 tabular-nums">
                {(data?.totalCount ?? 0).toLocaleString("fr-TN")}
              </Badge>
            </TabsTrigger>
            <TabsTrigger
              value="patients"
              aria-label="Patients à compléter"
              className={cn(
                "h-auto min-h-9 min-w-0 flex-1 gap-1.5 py-1.5 leading-tight coarse:min-h-11 sm:flex-none sm:gap-2",
                "data-[state=active]:bg-zone-clinical/12 data-[state=active]:text-zone-clinical",
              )}
            >
              <UserPlus className="hidden h-4 w-4 shrink-0 sm:block" />
              <span className="sm:hidden">À compléter</span>
              <span className="hidden sm:inline">Patients à compléter</span>
              <Badge variant="secondary" className="ms-0.5 shrink-0 tabular-nums">
                {pendingCount.toLocaleString("fr-TN")}
              </Badge>
            </TabsTrigger>
          </TabsList>

          {/* ⚠️ `forceMount`: Radix unmounts an inactive panel, so the block's read — and therefore the count on
              its own trigger — would not run until somebody opened the tab, which is precisely the tab nobody
              opens without a figure on it. Radix applies `hidden` while inactive, so it costs a hidden table of at
              most 25 rows and no announcement. */}
          <TabsContent value="patients" forceMount className="data-[state=inactive]:hidden">
            <PendingReviewBlock onLoaded={setPendingCount} />
          </TabsContent>

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
            visits={data?.items ?? []}
            loading={loading}
            onChanged={load}
            // Inside the list's own surface: the pager carries a `border-t` and no border, so as a page-level
            // sibling it rendered as a filet flottant on the page ground.
            // ⚠️ `totalCount > 0` too: the pager rendered under « Rien à clôturer », so an empty state carried
            // « 0 séance » and « Par page 25 » for a list that does not exist — a third of the card at 390 px.
            // `ui/data-table-pagination.tsx`'s own doc says an empty table should not carry a pager.
            footer={
              data && !loading && data.totalCount > 0 ? (
                <DataTablePagination
                  page={data}
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
          </TabsContent>
        </Tabs>
      </AppShell>
    </ClinicGuard>
  )
}

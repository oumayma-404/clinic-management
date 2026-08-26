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
import { VisitClosureList } from "@/components/visits/visit-closure-list"
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

/** Windows offered. Mirrors `VisitClosureReader`'s clamp; the server is the authority and re-clamps anyway. */
const WINDOWS = [
  { value: "7", label: "7 derniers jours" },
  { value: "14", label: "14 derniers jours" },
  { value: "30", label: "30 derniers jours" },
  { value: "90", label: "90 derniers jours" },
] as const

export default function VisitsToClosePage() {
  const [days, setDays] = useState<string>("7")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [data, setData] = useState<PagedResponse<VisitToCloseDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await appointmentsApi.visitsToClose({ days: Number(days), page, pageSize })

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
      </AppShell>
    </ClinicGuard>
  )
}

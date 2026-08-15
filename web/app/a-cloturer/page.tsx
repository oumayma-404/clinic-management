"use client"

import { useCallback, useEffect, useState } from "react"
import { AlertCircle } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
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
              ? `${data.totalCount.toLocaleString("fr-TN")} séance${data.totalCount === 1 ? "" : "s"} en attente d’une présence, d’une fiche ou d’un encaissement`
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
          <div
            role="status"
            className="flex flex-wrap items-center gap-3 rounded-md border border-destructive/30 bg-destructive/5 p-4 text-sm"
          >
            <AlertCircle aria-hidden="true" className="size-4 text-destructive" />
            <span className="flex-1">{error}</span>
            <Button variant="outline" size="sm" onClick={() => void load()}>
              Réessayer
            </Button>
          </div>
        ) : (
          <>
            <VisitClosureList visits={data?.items ?? []} loading={loading} onChanged={load} />

            {data && (
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
            )}
          </>
        )}
      </AppShell>
    </ClinicGuard>
  )
}

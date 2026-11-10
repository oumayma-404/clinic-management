"use client"

import Link from "next/link"
import { useCallback, useEffect, useState } from "react"
import { DashboardSection } from "@/components/dashboard/dashboard-section"
import { TeamTodayList } from "@/components/team/team-today-list"
import { pointageApi, type TeamTodayDto } from "@/lib/api/pointage"
import { usePendingLeaveCount } from "@/lib/hooks/use-staff-leave"
import { USERS_ATTENDANCE_TAB, USERS_LEAVE_TAB } from "@/lib/staff-leave"
import { getErrorMessage } from "@/lib/errors"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/**
 * « Équipe aujourd'hui » (`pointage-borne` AC-12): who is here and since when, who is on congé, and the congés
 * waiting for a decision. Admin only, hideable like every block; a punch reaches it live (`team`).
 */
export function TeamTodayBlock() {
  const [data, setData] = useState<TeamTodayDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refetching, setRefetching] = useState(false)

  const load = useCallback(async () => {
    setRefetching(true)
    try {
      setData(await pointageApi.today())
      setError(null)
    } catch (err) {
      setError(getErrorMessage(err, "L'équipe du jour n'a pas pu être chargée."))
    } finally {
      setRefetching(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])
  useClinicRealtime(RealtimeResource.Team, () => void load())

  const pending = data?.pendingLeaveCount ?? 0

  return (
    <DashboardSection
      title="Équipe aujourd'hui"
      href={`/users?onglet=${USERS_ATTENDANCE_TAB}`}
      action="Ouvrir les présences"
      refetching={refetching && data !== null}
      error={data ? null : error}
      onRetry={() => void load()}
    >
      {data && (
        <div className="space-y-2">
          <TeamTodayList today={data} onChanged={() => void load()} />
          {pending > 0 && <PendingLeaveLink count={pending} />}
        </div>
      )}
    </DashboardSection>
  )
}

/** « 2 demandes de congé en attente », to the Congés tab. */
function PendingLeaveLink({ count }: { count: number }) {
  return (
    <Link
      href={`/users?onglet=${USERS_LEAVE_TAB}`}
      className="inline-flex items-center text-sm font-medium text-primary hover:underline coarse:min-h-11"
    >
      {count} demande{count > 1 ? "s" : ""} de congé en attente
    </Link>
  )
}

/** The same block with the congés on and the pointage off: only the requests waiting, and nothing while none is. */
export function PendingLeaveBlock() {
  const pending = usePendingLeaveCount(true)
  if (pending === 0) return null
  return (
    <DashboardSection title="Équipe aujourd'hui" href={`/users?onglet=${USERS_LEAVE_TAB}`} action="Ouvrir les congés">
      <PendingLeaveLink count={pending} />
    </DashboardSection>
  )
}

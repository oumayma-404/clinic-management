"use client"

import { useState } from "react"
import Link from "next/link"
import { CalendarCheck, CalendarOff, ChevronRight } from "lucide-react"
import { AppLoader } from "@/components/ui/app-loader"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import type { StaffLeaveListFilter, TeamLeaveMemberDto } from "@/lib/api/team"
import { useStaffLeaveRequests, useTeamLeaveOverview } from "@/lib/hooks/use-staff-leave"
import { formatStaffDays, staffDaysLabel, staffLeaveShortPeriod } from "@/lib/staff-leave"
import { StaffLeaveSectionTitle } from "./staff-leave-bits"
import { StaffLeaveDecisionCards, StaffLeaveHistory, useLeaveDialogs } from "./staff-leave-request-list"

const DEFAULT_PAGE_SIZE = 10

type HistoryFilter = Extract<StaffLeaveListFilter, "decided" | "approved" | "rejected" | "cancelled">

const HISTORY_FILTERS: Array<{ value: HistoryFilter; label: string }> = [
  { value: "decided", label: "Toutes" },
  { value: "approved", label: "Acceptées" },
  { value: "rejected", label: "Refusées" },
  { value: "cancelled", label: "Annulées" },
]

const EMPTY_HISTORY: Record<HistoryFilter, string> = {
  decided: "Aucun congé décidé pour le moment",
  approved: "Aucun congé accepté",
  rejected: "Aucune demande refusée",
  cancelled: "Aucun congé annulé",
}

/** One secretary on the team strip: what is left of her year, and her next accepted congé. */
function TeamMemberCard({ member }: { member: TeamLeaveMemberDto }) {
  const { allowance, taken, remaining } = member.balance
  const used = allowance && allowance > 0 ? Math.min(100, Math.max(0, (taken / allowance) * 100)) : null
  const next =
    member.nextStartDate && member.nextEndDate
      ? staffLeaveShortPeriod({ startDate: member.nextStartDate, endDate: member.nextEndDate, halfDay: member.nextHalfDay })
      : null
  return (
    <Link
      href={`/users/${encodeURIComponent(member.userId)}`}
      className="flex items-center gap-3.5 rounded-xl border bg-card p-4 transition-colors hover:bg-accent/40 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
    >
      <InitialsAvatar name={member.fullName} className="size-10 text-xs" />
      <span className="min-w-0 flex-1 space-y-1.5">
        <span className="flex items-baseline justify-between gap-2">
          <span className="font-medium [overflow-wrap:anywhere]">{member.fullName}</span>
          <span className="shrink-0 text-sm text-foreground/80 tabular-nums">
            {remaining !== null && allowance !== null ? (
              <>
                <strong className="font-semibold">{formatStaffDays(remaining)}</strong> / {formatStaffDays(allowance)} j
              </>
            ) : (
              `${staffDaysLabel(taken)} pris`
            )}
          </span>
        </span>
        {used !== null && (
          <span
            role="img"
            aria-label={`${staffDaysLabel(remaining ?? 0)} restants sur ${staffDaysLabel(allowance ?? 0)}`}
            className="block h-1.5 overflow-hidden rounded-full bg-muted"
          >
            <span className="block h-full rounded-full bg-primary" style={{ width: `${used}%` }} />
          </span>
        )}
        <span className="block text-xs text-muted-foreground">
          {next ? `Prochain congé : ${next}` : "Aucun congé prévu"}
        </span>
      </span>
      <ChevronRight className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
    </Link>
  )
}

/** « Utilisateurs → Congés »: the team, the requests to decide, then every other one. */
export function StaffLeaveAdminPanel() {
  const team = useTeamLeaveOverview(true)
  const pending = useStaffLeaveRequests(true, "pending")
  const [filter, setFilter] = useState<HistoryFilter>("decided")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const decided = useStaffLeaveRequests(true, filter, page, pageSize)

  const refetchAll = () => {
    team.refetch()
    pending.refetch()
    decided.refetch()
  }
  const { open, dialogs } = useLeaveDialogs(refetchAll, { showName: true })

  const decidedPage = decided.data
  const pendingItems = pending.data?.items ?? []
  const members = team.data?.members ?? []

  return (
    <div className="space-y-8">
      {team.failed ? (
        <section aria-labelledby="leave-team-heading" className="space-y-3">
          <StaffLeaveSectionTitle id="leave-team-heading">Équipe</StaffLeaveSectionTitle>
          <LoadFailureNotice message="Les soldes de l'équipe n'ont pas pu être chargés." onRetry={team.refetch} />
        </section>
      ) : team.loading && !team.data ? (
        <AppLoader label="Chargement de l'équipe…" className="py-6" />
      ) : (
        members.length > 0 && (
          <section aria-labelledby="leave-team-heading" className="@container space-y-3">
            <StaffLeaveSectionTitle id="leave-team-heading">Équipe · congé payé {team.data?.year}</StaffLeaveSectionTitle>
            <div className="grid gap-3 @xl:grid-cols-2 @4xl:grid-cols-3">
              {members.map((member) => (
                <TeamMemberCard key={member.userId} member={member} />
              ))}
            </div>
          </section>
        )
      )}

      <section aria-labelledby="leave-pending-heading" className="space-y-3">
        <StaffLeaveSectionTitle id="leave-pending-heading">
          À décider{pending.data ? ` (${pendingItems.length})` : ""}
        </StaffLeaveSectionTitle>
        {pending.loading && !pending.data ? (
          <AppLoader label="Chargement des demandes…" className="py-6" />
        ) : pending.failed ? (
          <LoadFailureNotice message="Les demandes en attente n'ont pas pu être chargées." onRetry={pending.refetch} />
        ) : pendingItems.length === 0 ? (
          <EmptyState size="compact" icon={CalendarCheck} title="Aucune demande en attente" />
        ) : (
          <StaffLeaveDecisionCards items={pendingItems} onOpen={open} />
        )}
      </section>

      <section aria-labelledby="leave-decided-heading" className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <StaffLeaveSectionTitle id="leave-decided-heading">Toutes les demandes</StaffLeaveSectionTitle>
          <ModeSegmented
            value={filter}
            onChange={(next) => {
              setFilter(next)
              setPage(1)
            }}
            options={HISTORY_FILTERS}
            ariaLabel="Filtrer les demandes par statut"
            size="sm"
            className="grid w-full grid-cols-2 gap-1 sm:flex sm:w-auto"
          />
        </div>
        {decided.loading && !decidedPage ? (
          <AppLoader label="Chargement des congés…" className="py-6" />
        ) : decided.failed ? (
          <LoadFailureNotice message="Les congés n'ont pas pu être chargés." onRetry={decided.refetch} />
        ) : !decidedPage || decidedPage.items.length === 0 ? (
          <EmptyState size="compact" icon={CalendarOff} title={EMPTY_HISTORY[filter]} />
        ) : (
          <div className="space-y-3">
            <StaffLeaveHistory
              items={decidedPage.items}
              showName
              onOpen={open}
              ariaLabel="Toutes les demandes de congé"
            />
            <DataTablePagination
              page={{
                page: decidedPage.page,
                pageSize: decidedPage.pageSize,
                totalCount: decidedPage.totalCount,
                totalPages: Math.max(1, Math.ceil(decidedPage.totalCount / Math.max(1, decidedPage.pageSize))),
              }}
              onPageChange={setPage}
              onPageSizeChange={(size) => {
                setPageSize(size)
                setPage(1)
              }}
              loading={decided.loading}
              label={["congé", "congés"]}
            />
          </div>
        )}
      </section>
      {dialogs}
    </div>
  )
}

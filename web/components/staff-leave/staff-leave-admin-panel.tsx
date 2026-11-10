"use client"

import { useState } from "react"
import Link from "next/link"
import { CalendarCheck, CalendarOff, ChevronRight, Plus } from "lucide-react"
import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { InitialsAvatar } from "@/components/ui/initials-avatar"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Progress } from "@/components/ui/progress"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import type { StaffLeaveListFilter, TeamLeaveMemberDto } from "@/lib/api/team"
import { useStaffLeaveRequests, useTeamLeaveOverview } from "@/lib/hooks/use-staff-leave"
import {
  formatStaffDays,
  isOverdrawn,
  staffBalanceShort,
  staffDaysLabel,
  staffDaysShort,
  staffLeaveShortPeriod,
} from "@/lib/staff-leave"
import { cn } from "@/lib/utils"
import { StaffLeaveSectionTitle } from "./staff-leave-bits"
import { StaffLeaveRequestDialog } from "./staff-leave-request-dialog"
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

/** With no « Droit à congé » set: what she took, and what is accepted and still to come — never a balance. */
function takenOrPlanned(taken: number, planned: number): string {
  if (taken > 0 && planned > 0) return `${staffDaysLabel(taken)} pris · ${staffDaysShort(planned)} prévus`
  if (planned > 0) return `${staffDaysLabel(planned)} prévus`
  return `${staffDaysLabel(taken)} pris`
}

/** « 18 / 24 j », or « 2 j de dépassement » in red below zero. */
function RemainingFigure({ remaining, acquired }: { remaining: number; acquired: number }) {
  if (isOverdrawn(remaining)) return <strong className="font-semibold text-destructive">{staffBalanceShort(remaining)}</strong>
  return (
    <>
      <strong className="font-semibold">{formatStaffDays(remaining)}</strong> / {formatStaffDays(acquired)} j
    </>
  )
}

/**
 * One secretary on the team strip: what is left of her year (or « Droit à définir »), her days waiting, and her next
 * accepted congé. The name and the figure wrap onto two lines rather than squeeze the name at 320 px.
 */
function TeamMemberCard({ member }: { member: TeamLeaveMemberDto }) {
  const { acquired, taken, planned, remaining, hasAccount } = member.balance
  const spent = taken + planned
  const used = remaining !== null && acquired > 0 ? Math.min(100, Math.max(0, (spent / acquired) * 100)) : null
  const next =
    member.nextStartDate && member.nextEndDate
      ? staffLeaveShortPeriod({
          startDate: member.nextStartDate,
          endDate: member.nextEndDate,
          halfDay: member.nextHalfDay,
          halfDayPart: member.nextHalfDayPart,
        })
      : null
  return (
    <Link
      href={`/users/${encodeURIComponent(member.userId)}`}
      className="flex items-center gap-3.5 rounded-xl border bg-card p-4 transition-colors hover:bg-accent/40 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
    >
      <InitialsAvatar name={member.fullName} className="size-10 text-xs" />
      <div className="min-w-0 flex-1 space-y-1.5">
        <div className="flex flex-wrap items-baseline justify-between gap-x-2 gap-y-0.5">
          <span className="min-w-0 flex-[1_1_8rem] font-medium [overflow-wrap:anywhere]">{member.fullName}</span>
          <span className="shrink-0 text-sm text-foreground/80 tabular-nums">
            {remaining !== null ? <RemainingFigure remaining={remaining} acquired={acquired} /> : takenOrPlanned(taken, planned)}
          </span>
        </div>
        {used !== null && remaining !== null && (
          <Progress
            value={used}
            aria-label={
              isOverdrawn(remaining)
                ? `${staffBalanceShort(remaining)} sur ${staffDaysLabel(acquired)} acquis`
                : `${staffDaysLabel(remaining)} restants sur ${staffDaysLabel(acquired)} acquis`
            }
            className={cn("h-1.5", isOverdrawn(remaining) && "[&>div]:bg-destructive")}
          />
        )}
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
          {!hasAccount && (
            <span className={cn("rounded-full px-2 py-0.5 font-medium", STATUS_TONE_CLASS.active)}>Droit à définir</span>
          )}
          {member.pendingDays > 0 && (
            <span className="font-medium text-warning-ink">{staffDaysShort(member.pendingDays)} en attente</span>
          )}
          <span>{next ? `Prochain congé : ${next}` : "Aucun congé prévu"}</span>
        </div>
      </div>
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
  const [absenceOpen, setAbsenceOpen] = useState(false)

  const refetchAll = () => {
    team.refetch()
    pending.refetch()
    decided.refetch()
  }
  const { open, dialogs } = useLeaveDialogs(refetchAll, { admin: true })

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
            <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
              <StaffLeaveSectionTitle id="leave-team-heading">Équipe · congé payé {team.data?.year}</StaffLeaveSectionTitle>
              {/* A secretary who calls in sick is recorded from here, picked in the form; her file keeps its own. */}
              <Button variant="outline" size="sm" className="gap-1.5 coarse:h-11" onClick={() => setAbsenceOpen(true)}>
                <Plus className="size-4" aria-hidden="true" />
                Ajouter une absence
              </Button>
            </div>
            <div className="grid gap-3 @xl:grid-cols-2 @4xl:grid-cols-3">
              {members.map((member) => (
                <TeamMemberCard key={member.userId} member={member} />
              ))}
            </div>
          </section>
        )
      )}

      <section aria-labelledby="leave-pending-heading" className="space-y-3" data-guide="conges-pending">
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
      <StaffLeaveRequestDialog
        open={absenceOpen}
        onOpenChange={setAbsenceOpen}
        target={{ kind: "admin" }}
        people={members}
        onSaved={refetchAll}
      />
    </div>
  )
}

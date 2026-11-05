"use client"

import { useState } from "react"
import { CalendarOff, Plus } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { StaffLeaveBalanceCard } from "@/components/staff-leave/staff-leave-balance-card"
import { StaffLeaveMonthCalendar, StaffLeaveNextCard } from "@/components/staff-leave/staff-leave-month-calendar"
import { StaffLeaveRequestDialog } from "@/components/staff-leave/staff-leave-request-dialog"
import { StaffLeaveSectionTitle } from "@/components/staff-leave/staff-leave-bits"
import { StaffLeaveRows, useLeaveDialogs } from "@/components/staff-leave/staff-leave-request-list"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { useSession } from "@/lib/auth/session"
import { todayLocalIso } from "@/lib/format"
import { useMyStaffLeave } from "@/lib/hooks/use-staff-leave"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { isUpcomingLeave } from "@/lib/staff-leave"
import { cn } from "@/lib/utils"

/** « Mes congés » — a secretary's balance, her requests ahead and behind, the month, and « Demander un congé ». */
export default function CongesPage() {
  const { user, isLoading: sessionLoading } = useSession()
  const { data: status, isLoading: statusLoading } = useUserStatus()
  const isSecretary = user?.role === "secretary"
  const enabled = status?.clinic?.leaveRequestsEnabled === true
  const { data, loading, failed, refetch } = useMyStaffLeave(isSecretary && enabled)
  const [dialogOpen, setDialogOpen] = useState(false)
  const todayKey = todayLocalIso()
  const { open, dialogs } = useLeaveDialogs(refetch)

  const canAsk = isSecretary && enabled
  const askButton = (className?: string) => (
    <Button className={cn("gap-2 coarse:h-11", className)} onClick={() => setDialogOpen(true)}>
      <Plus className="size-4" aria-hidden="true" />
      Demander un congé
    </Button>
  )

  const upcoming = (data?.items ?? [])
    .filter((r) => isUpcomingLeave(r, todayKey))
    .sort((a, b) => a.startDate.localeCompare(b.startDate))
  const history = (data?.items ?? []).filter((r) => !isUpcomingLeave(r, todayKey))

  const body = () => {
    if (sessionLoading || statusLoading) return <AppLoader />
    if (!isSecretary) {
      return (
        <AccessDeniedCard
          title="Réservé aux secrétaires"
          description="Les demandes de congé se font depuis un compte secrétaire. L'administrateur les décide depuis « Utilisateurs »."
        />
      )
    }
    if (!enabled) {
      return (
        <EmptyState
          icon={CalendarOff}
          title="Les demandes de congé ne sont pas activées"
          description="L'administrateur du cabinet peut les activer dans « Paramètres »."
        />
      )
    }
    if (loading && !data) return <AppLoader label="Chargement de vos congés…" />
    if (failed || !data) return <LoadFailureNotice message="Vos congés n'ont pas pu être chargés." onRetry={refetch} />
    return (
      <>
        <StaffLeaveBalanceCard balance={data.balance} />
        {data.items.length === 0 ? (
          <EmptyState icon={CalendarOff} title="Aucune demande pour le moment" action={askButton()} />
        ) : (
          <div className="flex flex-wrap items-start gap-6">
            <div className="min-w-0 flex-[999_1_32rem] space-y-6">
              <section aria-labelledby="leave-upcoming" className="space-y-3">
                <StaffLeaveSectionTitle id="leave-upcoming">En cours et à venir</StaffLeaveSectionTitle>
                {upcoming.length === 0 ? (
                  <EmptyState size="compact" icon={CalendarOff} title="Aucun congé à venir" />
                ) : (
                  <StaffLeaveRows items={upcoming} todayKey={todayKey} onOpen={open} />
                )}
              </section>
              {history.length > 0 && (
                <section aria-labelledby="leave-history" className="space-y-3">
                  <StaffLeaveSectionTitle id="leave-history">Historique</StaffLeaveSectionTitle>
                  <StaffLeaveRows items={history} todayKey={todayKey} onOpen={open} />
                </section>
              )}
            </div>
            <aside aria-label="Ce mois-ci" className="min-w-0 flex-[1_1_17.5rem] space-y-3 lg:max-w-sm">
              <StaffLeaveSectionTitle id="leave-month">Ce mois-ci</StaffLeaveSectionTitle>
              <StaffLeaveMonthCalendar items={data.items} todayKey={todayKey} />
              <StaffLeaveNextCard items={data.items} todayKey={todayKey} />
            </aside>
          </div>
        )}
      </>
    )
  }

  const showAsk = canAsk && Boolean(data) && (data?.items.length ?? 0) > 0

  return (
    <ClinicGuard>
      {/* The fixed phone button covers the last ~60 px of the page, so the content gets a runway to scroll past it. */}
      <AppShell contentClassName={cn("space-y-6", showAsk && "pb-16 sm:pb-0")}>
        <PageHeader title="Mes congés" actions={showAsk ? askButton("hidden sm:inline-flex") : undefined} />
        {body()}
      </AppShell>
      {showAsk && (
        // A phone asks from the bottom of the screen, where the thumb is; the header button takes over from `sm:`.
        <div className="fixed inset-x-4 bottom-[calc(var(--bottom-inset)+0.75rem)] z-30 sm:hidden print:hidden">
          {askButton("h-12 w-full text-base shadow-lg")}
        </div>
      )}
      {dialogs}
      {canAsk && (
        <StaffLeaveRequestDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          target={{ kind: "self" }}
          onSaved={refetch}
        />
      )}
    </ClinicGuard>
  )
}

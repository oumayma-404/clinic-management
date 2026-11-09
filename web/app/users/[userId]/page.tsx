"use client"

import { useCallback, useEffect, useState } from "react"
import Link from "next/link"
import { useParams } from "next/navigation"
import { ArrowLeft, CalendarOff, Plus } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { CancelEntryDialog, StaffLeaveAccountCard, type CancellableLine } from "@/components/staff-leave/staff-leave-account-card"
import { StaffLeaveBalanceCard } from "@/components/staff-leave/staff-leave-balance-card"
import { StaffLeaveStatement } from "@/components/staff-leave/staff-leave-statement"
import { StaffLeaveRequestDialog } from "@/components/staff-leave/staff-leave-request-dialog"
import { StaffLeaveSectionTitle } from "@/components/staff-leave/staff-leave-bits"
import { StaffLeaveHistory, useLeaveDialogs } from "@/components/staff-leave/staff-leave-request-list"
import { AttendancePanel } from "@/components/team/attendance-panel"
import { PointagePersonRow } from "@/components/team/attendance-settings-card"
import { TEAM_SETTINGS_HREF } from "@/components/team/team-today-list"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { pointageApi, type AttendancePersonSettingDto } from "@/lib/api/pointage"
import { USER_ROLE_LABELS_FR } from "@/lib/api/users"
import { useSession } from "@/lib/auth/session"
import { useLeaveStatementYear, useStaffFile } from "@/lib/hooks/use-staff-leave"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import type { StaffLeaveType } from "@/lib/api/team"
import { leaveMovementLabel, staffDaysShort } from "@/lib/staff-leave"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { cn } from "@/lib/utils"

const TYPE_SWATCH: Record<StaffLeaveType, string> = {
  PaidLeave: "bg-primary",
  Sick: "bg-warning",
  Unpaid: "bg-muted-foreground/60",
  Other: "bg-muted-foreground/30",
}

/** A route segment holding `local|…` may arrive still percent-encoded. */
function decodeSegment(value: string | string[] | undefined): string | null {
  const raw = Array.isArray(value) ? value[0] : value
  if (!raw) return null
  try {
    return decodeURIComponent(raw)
  } catch {
    return raw
  }
}

/** A secretary's file for the admin: her congé balance, her « Droit à congé » and its relevé, and every request. */
export default function StaffFilePage() {
  const params = useParams()
  const userId = decodeSegment(params?.userId)
  const { user, isLoading: sessionLoading } = useSession()
  const { data: status } = useUserStatus()
  const isAdmin = user?.role === "admin"
  const leaveEnabled = status?.clinic?.leaveRequestsEnabled === true
  const attendanceEnabled = status?.clinic?.attendanceEnabled === true
  const [attendanceName, setAttendanceName] = useState<string | null>(null)
  const { data: file, loading, failed, refetch } = useStaffFile(isAdmin && leaveEnabled ? userId : null)
  const [absenceOpen, setAbsenceOpen] = useState(false)
  const [cancelling, setCancelling] = useState<CancellableLine | null>(null)
  const { open, dialogs } = useLeaveDialogs(refetch, { admin: true })
  const statement = useLeaveStatementYear(userId && file ? { kind: "user", userId } : null, file)
  const refetchLeave = () => {
    refetch()
    statement.onRetry()
  }

  const canRecordAbsence = Boolean(file && leaveEnabled && file.role === "secretary")
  // A secretary's file, or anyone who still has congés or « Droit à congé » lines from when she was one.
  const showLeave = !file || file.role === "secretary" || file.items.length > 0 || file.entries.length > 0

  const body = () => {
    if (sessionLoading || !status) return <AppLoader />
    if (!isAdmin) {
      return (
        <AccessDeniedCard
          title="Administrateurs uniquement"
          description="La fiche d'un membre de l'équipe est réservée aux administrateurs du cabinet."
        />
      )
    }
    if (!leaveEnabled && !attendanceEnabled) {
      return (
        <EmptyState
          icon={CalendarOff}
          title="Ni les congés ni le pointage ne sont activés"
          action={
            <Button asChild className="coarse:h-11">
              <Link href={TEAM_SETTINGS_HREF}>Activer dans Paramètres</Link>
            </Button>
          }
        />
      )
    }
    // Présences stand on their own read: a failed congés file must not take them down with it.
    return (
      <>
        {leaveEnabled && showLeave && leaveBody()}
        {leaveEnabled && !showLeave && !attendanceEnabled && (
          <EmptyState size="compact" icon={CalendarOff} title="Aucun congé enregistré" />
        )}
        {attendanceEnabled && userId && (
          <section aria-labelledby="staff-presences" className="space-y-3">
            <StaffLeaveSectionTitle id="staff-presences">Présences</StaffLeaveSectionTitle>
            <StaffPointageHeader userId={userId} />
            <AttendancePanel scope={{ kind: "person", userId }} onPersonName={setAttendanceName} />
          </section>
        )}
      </>
    )
  }

  const leaveBody = () => {
    if (loading && !file) return <AppLoader label="Chargement de la fiche…" />
    if (failed || !file) return <LoadFailureNotice message="Les congés n'ont pas pu être chargés." onRetry={refetch} />
    return (
      <>
        <div className="flex flex-wrap items-stretch gap-4">
          <StaffLeaveBalanceCard className="min-w-0 flex-[999_1_28rem]" balance={file.balance} />
          <section
            aria-labelledby="staff-leave-by-type"
            className="min-w-0 flex-[1_1_16rem] space-y-2 rounded-xl border bg-card p-5 sm:p-6"
          >
            <StaffLeaveSectionTitle id="staff-leave-by-type">Absences acceptées en {file.balance.year}</StaffLeaveSectionTitle>
            <ul className="divide-y">
              {file.acceptedByType.map((total) => {
                const inOpening = total.type === "PaidLeave" ? (file.balance.takenInOpening ?? 0) : 0
                return (
                  <li key={total.type} className="py-2.5 text-sm">
                    <div className="flex items-center justify-between gap-3">
                      <span className="inline-flex items-center gap-2.5">
                        <span aria-hidden="true" className={cn("size-2 rounded-sm", TYPE_SWATCH[total.type])} />
                        {total.typeLabel}
                      </span>
                      <span className="font-semibold tabular-nums">{staffDaysShort(total.days)}</span>
                    </div>
                    {inOpening > 0 && (
                      <p className="ps-[1.125rem] text-xs text-muted-foreground">
                        dont {staffDaysShort(inOpening)}{" "}
                        {file.balance.opening !== null ? "inclus dans le solde de départ" : "avant le début du droit à congé"}
                      </p>
                    )}
                  </li>
                )
              })}
            </ul>
          </section>
        </div>
        <StaffLeaveAccountCard file={file} onChanged={refetchLeave} />
        <section aria-labelledby="staff-leave-statement" className="space-y-3">
          <StaffLeaveSectionTitle id="staff-leave-statement">Relevé</StaffLeaveSectionTitle>
          <StaffLeaveStatement
            {...statement}
            onCancel={(m) => m.entryId && setCancelling({ id: m.entryId, label: leaveMovementLabel(m) })}
          />
        </section>
        <section aria-labelledby="staff-leave-history" className="space-y-3">
          <StaffLeaveSectionTitle id="staff-leave-history">Historique</StaffLeaveSectionTitle>
          {file.items.length === 0 ? (
            <EmptyState size="compact" icon={CalendarOff} title="Aucun congé enregistré" />
          ) : (
            <StaffLeaveHistory
              items={file.items}
              showName={false}
              onOpen={open}
              ariaLabel={`Congés de ${file.fullName}`}
            />
          )}
        </section>
      </>
    )
  }

  const roleLabel = file ? (USER_ROLE_LABELS_FR[file.role] ?? file.role) : undefined

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <Button asChild variant="ghost" size="sm" className="-ms-2 w-fit gap-1.5 coarse:h-11">
          <Link
            href={leaveEnabled || !attendanceEnabled ? "/users?onglet=conges" : "/users?onglet=presences"}
            data-guide="conges-back"
          >
            <ArrowLeft className="size-4" aria-hidden="true" />
            Utilisateurs
          </Link>
        </Button>
        <PageHeader
          title={file?.fullName ?? attendanceName ?? "Fiche"}
          titleBadge={
            file ? (
              <span
                className={cn(
                  "rounded-full px-2.5 py-0.5 text-xs font-semibold",
                  STATUS_TONE_CLASS[file.isActive ? "positive" : "neutral"],
                )}
              >
                {file.isActive ? "Actif" : "Désactivé"}
              </span>
            ) : undefined
          }
          subtitle={file ? [roleLabel, file.email].filter(Boolean).join(" · ") : undefined}
          actions={
            canRecordAbsence ? (
              <Button className="gap-2 coarse:h-11" onClick={() => setAbsenceOpen(true)}>
                <Plus className="size-4" aria-hidden="true" />
                Ajouter une absence
              </Button>
            ) : undefined
          }
        />
        {body()}
      </AppShell>
      {dialogs}
      <CancelEntryDialog entry={cancelling} onClose={() => setCancelling(null)} onSaved={refetchLeave} />
      {file && canRecordAbsence && (
        <StaffLeaveRequestDialog
          open={absenceOpen}
          onOpenChange={setAbsenceOpen}
          target={{ kind: "admin", userId: file.userId, userName: file.fullName }}
          onSaved={refetchLeave}
        />
      )}
    </ClinicGuard>
  )
}

/** The file's « Présences » header: whether this person pointe, their schéma, and its two fixes. */
function StaffPointageHeader({ userId }: { userId: string }) {
  const { data: status } = useUserStatus()
  const lockOn = status?.clinic?.attendanceLockEnabled !== false
  const [person, setPerson] = useState<AttendancePersonSettingDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [loaded, setLoaded] = useState(false)

  const load = useCallback(async () => {
    try {
      const settings = await pointageApi.settings()
      setPerson(settings.people.find((p) => p.userId === userId) ?? null)
      setFailed(false)
    } catch {
      setFailed(true)
    } finally {
      setLoaded(true)
    }
  }, [userId])

  useEffect(() => {
    void load()
  }, [load])
  useClinicRealtime(RealtimeResource.Team, () => void load())

  if (failed && !person) {
    return (
      <LoadFailureNotice
        variant="inline"
        message="Le réglage du pointage n'a pas pu être chargé."
        onRetry={() => void load()}
      />
    )
  }
  if (!loaded) return <div className="h-12 animate-pulse rounded-md bg-muted/50" aria-hidden="true" />
  if (!person) return null
  return (
    <div className="rounded-md border bg-card px-3 py-2.5">
      <PointagePersonRow as="div" showName={false} person={person} lockOn={lockOn} onChanged={() => void load()} />
    </div>
  )
}

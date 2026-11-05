"use client"

import type React from "react"

import { useState } from "react"
import Link from "next/link"
import { useParams } from "next/navigation"
import { toast } from "sonner"
import { ArrowLeft, CalendarOff, Plus } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { StaffLeaveBalanceCard } from "@/components/staff-leave/staff-leave-balance-card"
import { StaffLeaveRequestDialog } from "@/components/staff-leave/staff-leave-request-dialog"
import { StaffLeaveSectionTitle } from "@/components/staff-leave/staff-leave-bits"
import { StaffLeaveHistory, useLeaveDialogs } from "@/components/staff-leave/staff-leave-request-list"
import { AttendancePanel } from "@/components/team/attendance-panel"
import { STATUS_TONE_CLASS } from "@/components/ui/status-tone"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { teamApi } from "@/lib/api/team"
import { USER_ROLE_LABELS_FR } from "@/lib/api/users"
import { useSession } from "@/lib/auth/session"
import { showErrorToast } from "@/lib/errors"
import { useStaffFile } from "@/lib/hooks/use-staff-leave"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import type { StaffLeaveType } from "@/lib/api/team"
import { MAX_STAFF_DAYS, formatStaffDays, parseStaffDays, staffDaysShort } from "@/lib/staff-leave"
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

/** A secretary's file for the admin: her congé balance, the yearly allowance, and every request. */
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
  const { open, dialogs } = useLeaveDialogs(refetch, { showName: true })

  const canRecordAbsence = Boolean(file && leaveEnabled && file.role === "secretary")

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
    const presences =
      attendanceEnabled && userId ? (
        <section aria-labelledby="staff-presences" className="space-y-3">
          <StaffLeaveSectionTitle id="staff-presences">Présences</StaffLeaveSectionTitle>
          <AttendancePanel scope={{ kind: "person", userId }} onPersonName={setAttendanceName} />
        </section>
      ) : null
    if (!leaveEnabled) {
      return (
        presences ?? (
          <EmptyState
            icon={CalendarOff}
            title="Les demandes de congé ne sont pas activées"
            description="Activez-les dans « Paramètres » pour retrouver le solde et l'historique de chacun."
          />
        )
      )
    }
    if (loading && !file) return <AppLoader label="Chargement de la fiche…" />
    if (failed || !file) return <LoadFailureNotice message="La fiche n'a pas pu être chargée." onRetry={refetch} />
    return (
      <>
        <div className="flex flex-wrap items-stretch gap-4">
          <StaffLeaveBalanceCard
            className="min-w-0 flex-[999_1_28rem]"
            balance={file.balance}
            action={
              <AllowanceEditor
                key={String(file.balance.allowance)}
                userId={file.userId}
                allowance={file.balance.allowance}
                onSaved={refetch}
              />
            }
          />
          <section
            aria-labelledby="staff-leave-by-type"
            className="min-w-0 flex-[1_1_16rem] space-y-2 rounded-xl border bg-card p-5 sm:p-6"
          >
            <StaffLeaveSectionTitle id="staff-leave-by-type">Absences acceptées en {file.balance.year}</StaffLeaveSectionTitle>
            <ul className="divide-y">
              {file.acceptedByType.map((total) => (
                <li key={total.type} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                  <span className="inline-flex items-center gap-2.5">
                    <span aria-hidden="true" className={cn("size-2 rounded-sm", TYPE_SWATCH[total.type])} />
                    {total.typeLabel}
                  </span>
                  <span className="font-semibold tabular-nums">{staffDaysShort(total.days)}</span>
                </li>
              ))}
            </ul>
          </section>
        </div>
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
        {presences}
      </>
    )
  }

  const roleLabel = file ? (USER_ROLE_LABELS_FR[file.role] ?? file.role) : undefined

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <Button asChild variant="ghost" size="sm" className="-ms-2 w-fit gap-1.5 coarse:h-11">
          <Link href="/users?onglet=conges">
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
      {file && canRecordAbsence && (
        <StaffLeaveRequestDialog
          open={absenceOpen}
          onOpenChange={setAbsenceOpen}
          target={{ kind: "admin", userId: file.userId, userName: file.fullName }}
          onSaved={refetch}
        />
      )}
    </ClinicGuard>
  )
}

/** « Droit annuel » — days of congé payé a year, in halves; empty clears it. Keyed on the saved value by its caller. */
function AllowanceEditor({
  userId,
  allowance,
  onSaved,
}: {
  userId: string
  allowance: number | null
  onSaved: () => void
}) {
  const [value, setValue] = useState(allowance === null ? "" : formatStaffDays(allowance))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (saving) return
    let days: number | null = null
    if (value.trim()) {
      days = parseStaffDays(value)
      if (days === null) {
        setError(`Par demi-journée, entre 0 et ${MAX_STAFF_DAYS}.`)
        return
      }
    }
    setSaving(true)
    setError(null)
    try {
      await teamApi.setAllowance(userId, days)
      toast.success(days === null ? "Droit annuel retiré" : "Droit annuel enregistré")
      onSaved()
    } catch (err) {
      showErrorToast(err, "Le droit annuel n'a pas pu être enregistré.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <form noValidate onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2">
      <div className="space-y-1">
        <Label htmlFor="staff-allowance">Droit annuel (jours)</Label>
        <Input
          id="staff-allowance"
          inputMode="decimal"
          value={value}
          onChange={(e) => {
            setValue(e.target.value)
            setError(null)
          }}
          aria-invalid={Boolean(error)}
          aria-describedby={error ? "staff-allowance-error" : undefined}
          className="w-28 md:text-sm"
        />
      </div>
      <Button type="submit" variant="outline" className="coarse:h-11" disabled={saving}>
        {saving ? "Enregistrement…" : "Enregistrer"}
      </Button>
      {error && (
        <p id="staff-allowance-error" className="basis-full text-xs text-destructive">
          {error}
        </p>
      )}
    </form>
  )
}

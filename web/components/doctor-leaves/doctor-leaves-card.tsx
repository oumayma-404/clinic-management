"use client"

import { useMemo, useState } from "react"
import { CalendarOff, MoreHorizontal, Plus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { AppLoader } from "@/components/ui/app-loader"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import type { DoctorLeaveDto } from "@/lib/api/doctors"
import { canEditAnyLeave } from "@/lib/auth/can"
import { useSession } from "@/lib/auth/session"
import { dayCountLabel, leavePeriodLabel } from "@/lib/doctor-leaves"
import { todayLocalIso } from "@/lib/format"
import { useDoctorLeaves } from "@/lib/hooks/use-doctor-leaves"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { DeleteLeaveConfirm, DoctorLeaveDialog } from "./doctor-leave-dialog"

interface DoctorLeavesCardProps {
  doctorId: string
  doctorName?: string
  /** Rendered as a plain block, for « Paramètres → Médecins »' per-practitioner disclosure. */
  embedded?: boolean
}

/** One practitioner's congés: current and upcoming first, past ones folded. Beside « Horaires de travail ». */
export function DoctorLeavesCard({ doctorId, doctorName, embedded = false }: DoctorLeavesCardProps) {
  const { user } = useSession()
  const { currentUserDoctor } = useDoctors()
  const { leaves, loading, failed, refetch } = useDoctorLeaves(doctorId)
  const canEdit = canEditAnyLeave(user?.role) || currentUserDoctor?.id === doctorId

  const [dialog, setDialog] = useState<{ open: boolean; leave: DoctorLeaveDto | null }>({ open: false, leave: null })
  const [deleting, setDeleting] = useState<DoctorLeaveDto | null>(null)

  const { upcoming, past } = useMemo(() => {
    const today = todayLocalIso()
    return {
      upcoming: leaves.filter((l) => l.endDate >= today),
      past: leaves.filter((l) => l.endDate < today).reverse(),
    }
  }, [leaves])

  const row = (leave: DoctorLeaveDto) => (
    <li key={leave.id} className="flex min-w-0 items-center gap-2 rounded-md border px-3 py-2">
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{leavePeriodLabel(leave)}</p>
        <p className="text-xs text-muted-foreground">
          {dayCountLabel(leave.dayCount)}
          {leave.note ? ` · ${leave.note}` : ""}
        </p>
      </div>
      {canEdit && (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              className="size-9 shrink-0 coarse:size-11"
              aria-label={`Modifier ou supprimer le congé ${leavePeriodLabel(leave)}`}
            >
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => setDialog({ open: true, leave })}>
              Modifier le congé
            </DropdownMenuItem>
            <DropdownMenuItem variant="destructive" onSelect={() => setDeleting(leave)}>
              Supprimer le congé
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      )}
    </li>
  )

  const addButton = canEdit && (
    <Button
      variant="outline"
      size="sm"
      className="shrink-0 gap-1.5 coarse:h-11"
      onClick={() => setDialog({ open: true, leave: null })}
    >
      <Plus className="size-4" aria-hidden="true" />
      Ajouter un congé
    </Button>
  )

  const body = (
    <div className="space-y-3">
      {embedded && <div className="flex justify-end">{addButton}</div>}
      {loading && leaves.length === 0 ? (
        <AppLoader label="Chargement des congés…" className="py-4" />
      ) : failed && leaves.length === 0 ? (
        <LoadFailureNotice message="Les congés n'ont pas pu être chargés." onRetry={refetch} />
      ) : (
        <>
          {upcoming.length === 0 ? (
            <EmptyState size="compact" icon={CalendarOff} title="Aucun congé prévu" />
          ) : (
            <ul className="space-y-2">{upcoming.map(row)}</ul>
          )}
          {past.length > 0 && (
            <details>
              <summary className="cursor-pointer py-2 text-xs font-medium text-muted-foreground hover:text-foreground coarse:py-3.5">
                Congés passés ({past.length})
              </summary>
              <ul className="mt-1 space-y-2">{past.map(row)}</ul>
            </details>
          )}
        </>
      )}
    </div>
  )

  return (
    <>
      {embedded ? (
        body
      ) : (
        <Card>
          <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2 pb-3">
            <CardTitle className="flex min-w-0 items-center gap-2.5 text-base leading-snug">
              <span
                aria-hidden="true"
                className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
              >
                <CalendarOff className="size-4" strokeWidth={1.75} />
              </span>
              Mes congés{doctorName ? ` — ${doctorName}` : ""}
            </CardTitle>
            {addButton}
          </CardHeader>
          <CardContent>{body}</CardContent>
        </Card>
      )}

      <DoctorLeaveDialog
        open={dialog.open}
        onOpenChange={(open) => setDialog((current) => ({ ...current, open }))}
        leave={dialog.leave}
        defaultDoctorId={doctorId}
        onChanged={refetch}
      />
      <DeleteLeaveConfirm
        leave={deleting}
        open={deleting !== null}
        onOpenChange={(open) => {
          if (!open) setDeleting(null)
        }}
        onDeleted={refetch}
      />
    </>
  )
}

/** The card behind a disclosure, mounted once opened — Paramètres lists one per practitioner. */
export function DoctorLeavesDisclosure({ doctorId }: { doctorId: string }) {
  const [open, setOpen] = useState(false)
  return (
    <details className="mt-1" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary className="cursor-pointer py-2 text-xs font-medium text-muted-foreground hover:text-foreground coarse:py-3.5">
        Congés de ce praticien
      </summary>
      {open && (
        <div className="mt-2">
          <DoctorLeavesCard doctorId={doctorId} embedded />
        </div>
      )}
    </details>
  )
}

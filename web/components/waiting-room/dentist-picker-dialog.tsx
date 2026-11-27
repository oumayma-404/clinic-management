"use client"

import { Stethoscope } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import type { WaitingRoomDentistDto } from "@/lib/api/types"

/**
 * « Quel praticien ? » — asked before a cabinet de groupe seats a patient whose RDV names no dentist (`salle-attente`
 * round 2, C7), and behind « Changer de praticien » for a patient whose dentist is away today (C10). One press per
 * dentist; dentists on congé are never offered. Nothing is written until a dentist is picked, so « Annuler » needs
 * no confirmation.
 */
export interface DentistPick {
  /** What the pick is for — it decides the title. */
  purpose: "seat" | "change"
  patientName: string
  /** The dentist the patient has now, left out of a « Changer de praticien ». */
  currentDoctorId?: string | null
}

export function DentistPickerDialog({
  pick,
  dentists,
  busy,
  onPick,
  onClose,
}: {
  pick: DentistPick | null
  dentists: WaitingRoomDentistDto[]
  busy: boolean
  onPick: (doctorId: string) => void
  onClose: () => void
}) {
  const choices = dentists.filter((dentist) => pick?.purpose !== "change" || dentist.doctorId !== pick.currentDoctorId)

  return (
    <Dialog open={pick !== null} onOpenChange={(open) => !open && !busy && onClose()}>
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <DialogTitle>{pick?.purpose === "change" ? "Changer de praticien" : "Quel praticien ?"}</DialogTitle>
          <DialogDescription>
            {pick?.purpose === "change"
              ? `Qui reçoit ${pick?.patientName ?? "ce patient"} aujourd'hui ?`
              : `Qui reçoit ${pick?.patientName ?? "ce patient"} au fauteuil ?`}
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          {choices.length === 0 ? (
            <p className="text-sm text-muted-foreground" role="status">
              Aucun autre praticien n&apos;est présent aujourd&apos;hui.
            </p>
          ) : (
            <div className="grid gap-2">
              {choices.map((dentist) => (
                <Button
                  key={dentist.doctorId}
                  type="button"
                  variant="outline"
                  className="h-auto min-h-11 justify-start gap-2 whitespace-normal py-2 text-start"
                  disabled={busy}
                  onClick={() => onPick(dentist.doctorId)}
                >
                  <Stethoscope className="size-4 shrink-0 text-primary" aria-hidden="true" />
                  <span className="min-w-0">Dr {dentist.name}</span>
                </Button>
              ))}
            </div>
          )}
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" className="coarse:min-h-11" disabled={busy} onClick={onClose}>
            Annuler
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

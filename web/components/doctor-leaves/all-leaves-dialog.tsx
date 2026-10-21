"use client"

import { Dialog, DialogBody, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { DoctorLeavesCard } from "./doctor-leaves-card"

interface AllDoctorLeavesDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

/** Every practitioner's congés in one place — reception's list, since Paramètres is hidden from a secretary. */
export function AllDoctorLeavesDialog({ open, onOpenChange }: AllDoctorLeavesDialogProps) {
  const { doctors } = useDoctors()
  const practitioners = doctors.filter((d): d is typeof d & { id: string } => Boolean(d.id))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent mobile="sheet" className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Congés des praticiens</DialogTitle>
          <DialogDescription className="sr-only">Les congés de chaque praticien du cabinet.</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="space-y-6">
            {practitioners.map((doctor) => (
              <section key={doctor.id} aria-label={`Congés de ${doctor.name}`} className="space-y-2">
                <h3 className="text-sm font-semibold">{doctor.name}</h3>
                {/* Mounted only while open: one read per practitioner, not on every agenda render. */}
                {open && <DoctorLeavesCard doctorId={doctor.id} embedded />}
              </section>
            ))}
          </div>
        </DialogBody>
      </DialogContent>
    </Dialog>
  )
}

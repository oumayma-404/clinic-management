"use client"

import { useEffect, useState } from "react"
import { toast } from "sonner"
import { ArrowRight, User } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { PatientSearchList, type PickedPatient } from "@/components/patients/patient-search-list"
import { ApiError } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
import type { PatientFileDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { quoteFr } from "@/lib/format"
import { useFreshVersion } from "@/lib/hooks/use-fresh-version"

/**
 * « Changer de patient » (`features/camera-photo-import`) — a file filed under the wrong person moves to the right one,
 * keeping its capture facts and its history. Open to every role: deleting is doctor/admin only and loses the capture
 * facts, so without this a secretary could not correct her own import.
 *
 * ⚠️ A file in a default folder (« Photographie ») lands in the other patient's folder of the same name; any other
 * lands at their root — a folder belongs to one patient. The server decides that, not this dialog.
 */
export function MoveToPatientDialog({
  patientId,
  file,
  onOpenChange,
  onMoved,
}: {
  patientId: string
  file: PatientFileDto | null
  onOpenChange: (open: boolean) => void
  onMoved: (target: PickedPatient) => void
}) {
  const [target, setTarget] = useState<PickedPatient | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (file) setTarget(null)
  }, [file])

  // The version this move is sent with, read on open rather than taken from the row that was clicked.
  const { source: freshFile, resync } = useFreshVersion(
    Boolean(file),
    file?.id,
    file,
    async () =>
      (await patientFilesApi.getFiles(patientId, file!.folderId ?? undefined)).find((f) => f.id === file!.id) ?? null,
  )

  const move = async () => {
    if (!file || !target || saving) return
    try {
      setSaving(true)
      await patientFilesApi.moveToPatient(patientId, file.id, target.patientId, freshFile?.version ?? file.version)
      toast.success("Fichier déplacé", {
        description: `${quoteFr(file.fileName)} est maintenant dans le dossier de ${target.patientName}.`,
      })
      onOpenChange(false)
      onMoved(target)
    } catch (error) {
      // The dialog stays open with the patient chosen; a refusal names its reason (archived patient, linked document…).
      showErrorToast(error, "Le fichier n'a pas pu être déplacé.")
      if (!(error instanceof ApiError && error.status === 409)) await resync()
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={!!file} onOpenChange={(open) => { if (!saving) onOpenChange(open) }}>
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Changer de patient</DialogTitle>
          <DialogDescription className="[overflow-wrap:anywhere]">
            {file ? `${quoteFr(file.fileName)} sera rangé chez un autre patient.` : ""}
          </DialogDescription>
        </DialogHeader>

        {target ? (
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-muted/40 p-3">
            <p className="flex min-w-0 items-center gap-2 text-sm">
              <ArrowRight className="size-4 shrink-0 text-primary" aria-hidden="true" />
              <User className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
              {/* A sentence, not a link: navigating away from a confirmation is worse than no link. */}
              <span className="min-w-0"><span className="font-medium">{target.patientName}</span> recevra ce fichier.</span>
            </p>
            <Button type="button" variant="ghost" size="sm" className="coarse:min-h-11" onClick={() => setTarget(null)} disabled={saving}>
              Choisir un autre
            </Button>
          </div>
        ) : (
          // No autoFocus: opened from a menu, focus must land on the title and come back to « ⋯ » on close — and on
          // a phone the keyboard would rise over the sheet as it opens.
          <PatientSearchList label="Rechercher le patient qui doit recevoir ce fichier" onPick={setTarget} excludeId={patientId} />
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          {/* « Ranger » alone on the button — the line above names the patient, and a long name overflowed at 320 px. */}
          <Button
            onClick={() => void move()}
            disabled={!target || saving}
            aria-label={target && !saving ? `Ranger chez ${target.patientName}` : undefined}
          >
            {saving ? "Déplacement…" : "Ranger"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

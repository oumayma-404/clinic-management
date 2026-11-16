"use client"

import { Clock } from "lucide-react"
import { Button } from "@/components/ui/button"
import { setOffsetAccepted, type ImportCamera } from "@/lib/photo-import/session"
import { formatOffset } from "@/lib/photo-import/wall-clock"

/**
 * « L'horloge de l'appareil semble avoir 1 h 02 de retard — corrigé » (`features/camera-photo-import`).
 *
 * A camera whose clock was reset by a battery change files every photo one patient early, so a wrong clock is
 * corrected as soon as the photos say so — and said out loud, with the way back one press away, because the
 * correction moves every group on the screen.
 */
export function ClockOffsetNotice({ camera }: { camera: ImportCamera }) {
  if (camera.detectedOffset === 0) return null
  const amount = formatOffset(camera.detectedOffset)
  // Minutes ADDED to the camera's time: a positive correction means its clock was behind.
  const direction = camera.detectedOffset > 0 ? "de retard" : "d'avance"

  return (
    <div
      role="status"
      className="flex flex-col gap-2 rounded-lg bg-warning-wash p-3 text-sm text-warning-ink sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="flex items-start gap-2">
        <Clock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          {camera.offsetAccepted
            ? `L'horloge de l'appareil ${camera.label} semble avoir ${amount} ${direction} : les heures sont corrigées.`
            : `L'heure de l'appareil ${camera.label} n'est pas corrigée.`}
        </span>
      </p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="shrink-0 bg-card coarse:min-h-11"
        onClick={() => setOffsetAccepted(camera.key, !camera.offsetAccepted)}
      >
        {camera.offsetAccepted ? "Annuler la correction" : `Corriger de ${amount}`}
      </Button>
    </div>
  )
}

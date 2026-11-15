"use client"

import { useId } from "react"
import { Camera, Check } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { PhotoImportDoctorDto } from "@/lib/api/photo-import"
import { setCameraDoctor, type ImportCamera } from "@/lib/photo-import/session"

const WHOLE_CABINET = "cabinet"

/**
 * « Nouvel appareil » — asked once per camera, the first time an import meets it (`features/camera-photo-import`):
 * whose agenda its photos belong to. Saved on the camera at once, so the next card from it asks nothing.
 *
 * Pre-selected on the signed-in practitioner — the usual answer — and offered only when the cabinet has more than one;
 * with one, there is nothing to choose.
 *
 * ⚠️ **Pre-selected is not answered.** Until somebody picks or confirms, the camera's groups ask (`needsOwner`): a
 * colleague's camera matched against the wrong day can look sure, and a wrong clock makes it look surer.
 */
export function CameraOwnerPrompt({ camera, doctors }: { camera: ImportCamera; doctors: PhotoImportDoctorDto[] }) {
  const id = useId()

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-card p-3 sm:flex-row sm:items-center sm:justify-between">
      <p className="flex min-w-0 items-start gap-2 text-sm">
        <Camera className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
        <span>
          Nouvel appareil : <span className="font-medium">{camera.label}</span>
          {camera.serialSuffix && <span className="text-muted-foreground"> (n° …{camera.serialSuffix})</span>}
        </span>
      </p>
      <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-2">
        <Label htmlFor={id} className="text-sm font-normal text-muted-foreground">
          Ses photos suivent l&apos;agenda de
        </Label>
        <Select
          value={camera.doctorId ?? WHOLE_CABINET}
          onValueChange={(value) => void setCameraDoctor(camera.key, value === WHOLE_CABINET ? null : value)}
        >
          <SelectTrigger id={id} className="w-full sm:w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {doctors.map((doctor) => (
              <SelectItem key={doctor.id} value={doctor.id}>
                {doctor.name}
              </SelectItem>
            ))}
            <SelectItem value={WHOLE_CABINET}>Tout le cabinet</SelectItem>
          </SelectContent>
        </Select>
        {!camera.doctorChosen && (
          <Button
            type="button"
            size="sm"
            className="gap-1.5 coarse:min-h-11"
            onClick={() => void setCameraDoctor(camera.key, camera.doctorId)}
          >
            <Check className="size-4" aria-hidden="true" />
            Confirmer
          </Button>
        )}
      </div>
    </div>
  )
}

"use client"

import { useCallback, useEffect, useId, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import { Camera, Clock, Pencil, Trash2 } from "lucide-react"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { EmptyState } from "@/components/ui/empty-state"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { clinicCamerasApi, type ClinicCameraDto, type UpdateCameraRequest } from "@/lib/api/clinic-cameras"
import { useSession } from "@/lib/auth/session"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDateTime, formatFileSize, quoteFr } from "@/lib/format"
import { useDoctors } from "@/lib/hooks/use-doctors"
import { isAdminOrDoctor } from "@/lib/nav"
import { useCameraShell } from "@/lib/photo-import/shell-camera"
import { formatOffset } from "@/lib/photo-import/wall-clock"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

const WHOLE_CABINET = "cabinet"

/**
 * « Appareils photo » (`features/camera-photo-import`) — the cameras this cabinet imports from, and the three things
 * the app remembers about each: whose agenda its photos follow, how wrong its clock is, and when it was last
 * imported. A camera appears here the first time an import meets it; there is nothing to add by hand.
 *
 * ⚠️ « Oublier » erases nothing a patient's record shows — each photo keeps the model it was taken with. It is
 * doctor/admin only, like every other removal.
 */
export function CamerasCard() {
  const { user } = useSession()
  const { allDoctors } = useDoctors()
  const [cameras, setCameras] = useState<ClinicCameraDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [forgetting, setForgetting] = useState<ClinicCameraDto | null>(null)
  const canForget = isAdminOrDoctor(user?.role)

  const load = useCallback(async () => {
    try {
      setCameras(await clinicCamerasApi.list())
      setError(null)
    } catch (err) {
      setError(getErrorMessage(err, "Les appareils photo n'ont pas pu être lus."))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])
  useClinicRealtime(RealtimeResource.Cameras, () => void load())

  const update = async (camera: ClinicCameraDto, change: Omit<UpdateCameraRequest, "version">) => {
    try {
      const saved = await clinicCamerasApi.update(camera.id, { ...change, version: camera.version })
      setCameras((current) => current?.map((item) => (item.id === saved.id ? saved : item)) ?? null)
      return true
    } catch (err) {
      showErrorToast(err, "L'appareil n'a pas pu être modifié.")
      // A colleague's change (409) or a vanished camera: show what the server holds now.
      await load()
      return false
    }
  }

  const forget = async () => {
    if (!forgetting) return
    const camera = forgetting
    setForgetting(null)
    try {
      await clinicCamerasApi.forget(camera.id)
      setCameras((current) => current?.filter((item) => item.id !== camera.id) ?? null)
      toast.success("Appareil oublié", { description: `${quoteFr(camera.label)} ne figure plus dans la liste.` })
    } catch (err) {
      showErrorToast(err, "L'appareil n'a pas pu être oublié.")
    }
  }

  const doctors = allDoctors.filter((doctor): doctor is typeof doctor & { id: string } => Boolean(doctor.id))

  return (
    <Card>
      <CardHeader className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div className="space-y-1">
          <CardTitle className="flex items-center gap-2 text-base">
            <Camera className="size-4 text-primary" aria-hidden="true" />
            Appareils photo
          </CardTitle>
          <CardDescription>Les photos de chaque appareil suivent l&apos;agenda de son praticien.</CardDescription>
        </div>
        <Button asChild variant="outline" size="sm" className="coarse:min-h-11">
          <Link href="/photos/import">Importer des photos</Link>
        </Button>
      </CardHeader>
      <CardContent>
        {error && !cameras ? (
          <LoadFailureNotice message={error} onRetry={() => void load()} />
        ) : !cameras ? (
          <p className="text-sm text-muted-foreground" role="status">
            Chargement…
          </p>
        ) : cameras.length === 0 ? (
          <EmptyState
            size="compact"
            icon={Camera}
            title="Aucun appareil pour l'instant"
            description="Un appareil apparaît ici à sa première importation."
          />
        ) : (
          <ul className="divide-y divide-border rounded-md border border-border">
            {cameras.map((camera) => (
              <CameraRow
                key={camera.id}
                camera={camera}
                doctors={doctors}
                canForget={canForget}
                onUpdate={(change) => update(camera, change)}
                onForget={() => setForgetting(camera)}
              />
            ))}
          </ul>
        )}
        <ThisPcSection />
      </CardContent>

      <AlertDialog open={!!forgetting} onOpenChange={(open) => { if (!open) setForgetting(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Oublier {forgetting ? quoteFr(forgetting.label) : "l'appareil"} ?</AlertDialogTitle>
            <AlertDialogDescription>
              Son praticien et sa correction d&apos;heure sont effacés. Les photos déjà rangées ne changent pas.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Annuler</AlertDialogCancel>
            <AlertDialogAction
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
              onClick={() => void forget()}
            >
              Oublier l&apos;appareil
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}

function CameraRow({
  camera,
  doctors,
  canForget,
  onUpdate,
  onForget,
}: {
  camera: ClinicCameraDto
  doctors: Array<{ id: string; name: string }>
  canForget: boolean
  onUpdate: (change: Omit<UpdateCameraRequest, "version">) => Promise<boolean>
  onForget: () => void
}) {
  const [renaming, setRenaming] = useState(false)
  const [label, setLabel] = useState(camera.label)
  const [saving, setSaving] = useState(false)
  const labelId = useId()
  const doctorId = useId()

  useEffect(() => {
    if (!renaming) setLabel(camera.label)
  }, [camera.label, renaming])

  const save = async (change: Omit<UpdateCameraRequest, "version">) => {
    setSaving(true)
    const ok = await onUpdate(change)
    setSaving(false)
    return ok
  }

  return (
    <li className="space-y-3 p-3">
      {renaming ? (
        <form
          className="flex flex-col gap-2 sm:flex-row sm:items-end"
          onSubmit={async (event) => {
            event.preventDefault()
            if (!label.trim()) return
            if (await save({ label: label.trim() })) setRenaming(false)
          }}
        >
          <div className="min-w-0 flex-1 space-y-1">
            <Label htmlFor={labelId}>Nom de l&apos;appareil</Label>
            <Input id={labelId} value={label} maxLength={60} onChange={(event) => setLabel(event.target.value)} autoFocus />
          </div>
          <div className="flex gap-2">
            <Button type="button" variant="outline" className="coarse:min-h-11" onClick={() => setRenaming(false)} disabled={saving}>
              Annuler
            </Button>
            <Button type="submit" className="coarse:min-h-11" disabled={saving || !label.trim()}>
              Enregistrer
            </Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="font-medium">{camera.label}</p>
            <p className="text-xs text-muted-foreground">
              {[
                camera.serialSuffix && `n° …${camera.serialSuffix}`,
                camera.lastImportedAt ? `dernière photo rangée le ${formatDateTime(camera.lastImportedAt).replace(" ", " à ")}` : "jamais importé",
              ]
                .filter(Boolean)
                .join(" · ")}
            </p>
          </div>
          <div className="flex gap-1">
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="size-9 coarse:size-11"
              aria-label={`Renommer ${camera.label}`}
              onClick={() => setRenaming(true)}
            >
              <Pencil className="size-4" aria-hidden="true" />
            </Button>
            {canForget && (
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="size-9 text-destructive coarse:size-11"
                aria-label={`Oublier ${camera.label}`}
                onClick={onForget}
              >
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            )}
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-4">
        <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:gap-2">
          <Label htmlFor={doctorId} className="text-sm font-normal text-muted-foreground">
            Agenda de
          </Label>
          <Select
            value={camera.doctorId ?? WHOLE_CABINET}
            disabled={saving}
            onValueChange={(value) => void save({ doctorId: value === WHOLE_CABINET ? null : value })}
          >
            <SelectTrigger id={doctorId} className="w-full sm:w-56">
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
        </div>

        {camera.clockOffsetMinutes !== 0 && (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span className="flex items-center gap-1.5 text-muted-foreground">
              <Clock className="size-4" aria-hidden="true" />
              Heure corrigée de {formatOffset(camera.clockOffsetMinutes)}
              {camera.clockOffsetMinutes > 0 ? " (en retard)" : " (en avance)"}
            </span>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="coarse:min-h-11"
              disabled={saving}
              onClick={() => void save({ clockOffsetMinutes: 0 })}
            >
              Ne plus corriger
            </Button>
          </div>
        )}
      </div>
    </li>
  )
}

/**
 * « Sur ce PC » — the Windows app's half of the camera (`features/camera-photo-import`, phase 2): whether this PC says
 * « Appareil photo détecté » when one is plugged in, and the full-size originals it keeps. Rendered only in the Windows
 * app 1.8; per PC and per Windows account, which is why it is here beside the cabinet's cameras and not a cabinet
 * setting.
 *
 * ⚠️ **The switch is off by default** and only raises a notice: nothing is copied before « Importer ». « Depuis
 * l'appareil branché » on the import screen works whatever it says.
 */
function ThisPcSection() {
  const shell = useCameraShell()
  const switchId = useId()
  const [status, setStatus] = useState<ShellCameraStatus | null>(null)
  const [unreadable, setUnreadable] = useState(false)
  const [saving, setSaving] = useState(false)
  const [copying, setCopying] = useState(false)

  const read = useCallback(async () => {
    if (!shell) return
    const next = await shell.cameraStatus()
    setStatus(next)
    setUnreadable(next === null)
  }, [shell])

  useEffect(() => {
    void read()
  }, [read])

  if (!shell) return null

  const toggle = async (on: boolean) => {
    setSaving(true)
    try {
      const next = await shell.setCameraAutoImport(on)
      if (next) setStatus(next)
      else toast.error("Le réglage n'a pas pu être enregistré sur ce PC.")
    } finally {
      setSaving(false)
    }
  }

  const copyNow = async () => {
    setCopying(true)
    try {
      const result = await shell.copyOriginalsNow()
      if (!result) toast.error("La copie n'a pas pu être lancée.")
      else if (result.complete) toast.success(result.sentence)
      else toast.warning(result.sentence)
      await read()
    } finally {
      setCopying(false)
    }
  }

  const originals = status?.originals
  return (
    <section className="mt-4 space-y-3 border-t border-border pt-4" aria-labelledby={`${switchId}-title`}>
      <h3 id={`${switchId}-title`} className="text-sm font-medium">
        Sur ce PC
      </h3>

      {unreadable ? (
        <LoadFailureNotice message="L'application Windows n'a pas répondu." onRetry={() => void read()} />
      ) : (
        <>
          <div className="flex items-center justify-between gap-3">
            <Label htmlFor={switchId} className="text-sm font-normal">
              Détecter l&apos;appareil photo branché
            </Label>
            <Switch
              id={switchId}
              checked={status?.autoImport ?? false}
              disabled={!status || saving}
              onCheckedChange={(on) => void toggle(on)}
            />
          </div>

          {originals && originals.files > 0 && (
            <div className="flex flex-col gap-2 text-sm sm:flex-row sm:items-center sm:justify-between">
              <p className="text-muted-foreground">
                Originaux gardés : {originals.files === 1 ? "1 photo" : `${originals.files} photos`} ·{" "}
                {formatFileSize(originals.bytes)}
                {originals.freeBytes !== null && ` · ${formatFileSize(originals.freeBytes)} libres`}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="coarse:min-h-11"
                disabled={copying}
                onClick={() => void copyNow()}
              >
                {copying ? "Copie en cours…" : "Copier sur le disque de copie"}
              </Button>
            </div>
          )}

          {status?.copy && (
            <p
              role="status"
              className={status.copy.complete ? "text-sm text-muted-foreground" : "text-sm text-warning-ink"}
            >
              {status.copy.sentence}
            </p>
          )}
        </>
      )}
    </section>
  )
}

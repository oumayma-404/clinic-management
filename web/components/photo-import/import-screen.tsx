"use client"

import { useEffect, useMemo } from "react"
import { useSearchParams } from "next/navigation"
import { Images, Loader2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { Progress } from "@/components/ui/progress"
import { StorageUsageLine } from "@/components/patients/files/storage-usage-line"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import { formatFileSize } from "@/lib/format"
import { replaceUrl } from "@/lib/history"
import { useUploadPolicy } from "@/lib/hooks/use-upload-policy"
import {
  UNKNOWN_CAMERA,
  filingPlan,
  patientOf,
  resetImport,
  setShowOlder,
  startFiling,
  startImport,
  unresolvedGroups,
  useImportSession,
  type ImportPhoto,
  type ImportState,
} from "@/lib/photo-import/session"
import { scanShellCamera, takeShellBatch, useCameraShell, useShellCamera } from "@/lib/photo-import/shell-camera"
import { skippedSummary } from "@/lib/photo-import/sources"
import { wallClockDay } from "@/lib/photo-import/wall-clock"
import { BurstCard } from "./burst-card"
import { CameraOwnerPrompt } from "./camera-owner-prompt"
import { ClockOffsetNotice } from "./clock-offset-notice"
import { FilingStep } from "./filing-step"
import { SourceStep } from "./source-step"

/** What a 2 560 px copy weighs, roughly — for the « assez de place ? » estimate only; the server measures the real one. */
const COPY_ESTIMATE_BYTES = 1.6 * 1024 * 1024

const plural = (count: number, one: string, many: string) => `${count} ${count > 1 ? many : one}`

/**
 * « Importer les photos » (`features/camera-photo-import`) — card → groups by visit → « Ranger ». The state lives in
 * `lib/photo-import/session.ts`, so leaving this screen neither cancels the uploads nor loses the review.
 */
export function ImportScreen() {
  const state = useImportSession()
  const policy = useUploadPolicy()
  const photos = useMemo(() => new Map(state.photos.map((photo) => [photo.id, photo])), [state.photos])
  const shell = useCameraShell()
  const { batch: shellBatch, busy: shellBusy } = useShellCamera()

  // The Windows app's photos start as soon as they arrive — they come only because somebody asked for them — except
  // over a batch still being read or sent, which they wait behind.
  useEffect(() => {
    if (!shellBatch || state.phase === "reading" || state.phase === "filing") return
    const batch = takeShellBatch()
    if (batch) void startImport(batch)
  }, [shellBatch, state.phase])

  // `?appareil=<device>` — « Importer » in a notice: copy that device's photos now, once, and drop the key.
  // Read through `useSearchParams` in the browser only (the `useUrlFilterSeed` pattern), so the page stays static.
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const routed = typeof window === "undefined" ? null : useSearchParams()
  const appareil = routed?.get("appareil") ?? null
  useEffect(() => {
    if (appareil === null) return
    replaceUrl(window.location.pathname)
    void scanShellCamera({ sourceKey: appareil || undefined })
  }, [appareil])

  const subtitle =
    state.phase === "review"
      ? `${plural(state.photos.length - state.olderCount, "nouvelle photo", "nouvelles photos")}`
      : shell
        ? "Depuis l'appareil photo ou sa carte"
        : "Depuis la carte de l'appareil photo"

  return (
    <div className="space-y-5">
      <PageHeader
        title="Importer les photos"
        subtitle={subtitle}
        actions={
          state.phase === "review" || state.phase === "failed" ? (
            <Button type="button" variant="outline" className="coarse:min-h-11" onClick={resetImport}>
              Autre carte
            </Button>
          ) : undefined
        }
      />

      <StorageUsageLine policy={policy} />

      {state.phase === "idle" && shellBusy && <ShellCopyingNotice />}
      {state.phase === "idle" && !shellBusy && <SourceStep />}

      {state.phase === "reading" && (
        <div className="space-y-3 rounded-lg border border-border bg-card p-6" role="status">
          <p className="flex items-center gap-2 font-medium">
            <Loader2 className="size-4 text-primary motion-safe:animate-spin" aria-hidden="true" />
            {state.read.done < state.read.total
              ? `Lecture des photos : ${state.read.done} sur ${state.read.total}`
              : "Lecture de l'agenda…"}
          </p>
          <Progress
            value={state.read.total > 0 ? Math.round((state.read.done / state.read.total) * 100) : 100}
            className="h-2"
            aria-label="Lecture des photos"
          />
        </div>
      )}

      {state.phase === "failed" && (
        <LoadFailureNotice
          message={state.error ?? "Les photos n'ont pas pu être lues."}
          detail="Rien n'a été rangé."
          onRetry={resetImport}
        />
      )}

      {state.phase === "review" && <ReviewStep state={state} policy={policy} photos={photos} />}

      {(state.phase === "filing" || state.phase === "done") && <FilingStep state={state} photos={photos} />}
    </div>
  )
}

/** « Importer » in the Windows app: the photos are being copied off the camera onto this PC. */
function ShellCopyingNotice() {
  return (
    <div className="flex items-center gap-2 rounded-lg border border-border bg-card p-6 font-medium" role="status">
      <Loader2 className="size-4 text-primary motion-safe:animate-spin" aria-hidden="true" />
      Copie des photos de l&apos;appareil sur ce PC…
    </div>
  )
}

function ReviewStep({
  state,
  policy,
  photos,
}: {
  state: ImportState
  policy: UploadPolicy | null
  photos: Map<string, ImportPhoto>
}) {
  const skipped = skippedSummary(state.skipped)
  const doctors = state.context?.doctors ?? []
  const doctorNames = doctors.length > 1 ? new Map(doctors.map((doctor) => [doctor.id, doctor.name])) : null
  const cameraLabels = state.cameras.length > 1 ? new Map(state.cameras.map((camera) => [camera.key, camera.label])) : null
  const days = new Set(state.groups.filter((group) => group.start !== null).map((group) => wallClockDay(group.start!)))
  const plan = filingPlan(state)
  const unresolved = unresolvedGroups(state)
  const patientCount = new Set(plan.map((item) => item.patientId)).size
  const newCameras = state.cameras.filter((camera) => camera.isNew && camera.key !== UNKNOWN_CAMERA)

  // « Assez de place ? » — an estimate before anything is sent; each refusal is still the server's own.
  let shortBy = 0
  if (policy && policy.storageQuotaBytes > 0 && plan.length > 0) {
    const perPhoto = policy.cameraCopy
      ? COPY_ESTIMATE_BYTES
      : plan.reduce((sum, item) => sum + (photos.get(item.photoId)?.file.size ?? 0), 0) / plan.length
    shortBy = Math.max(0, plan.length * perPhoto - (policy.storageQuotaBytes - policy.storageUsedBytes))
  }

  const shellOlder = state.shell?.older ?? 0
  const shellFailed = state.shell?.failed ?? 0

  const goToFirstUnresolved = () => {
    const first = unresolved[0]
    if (first) document.getElementById(`groupe-${first.key}`)?.scrollIntoView({ behavior: "smooth", block: "center" })
  }

  return (
    <div className="space-y-4">
      {shellFailed > 0 && (
        <p role="status" className="rounded-lg bg-warning-wash p-3 text-sm text-warning-ink">
          {plural(shellFailed, "photo n'a", "photos n'ont")} pas pu être copiée{shellFailed > 1 ? "s" : ""} depuis
          l&apos;appareil : elle{shellFailed > 1 ? "s seront proposées" : " sera proposée"} à la prochaine importation.
        </p>
      )}
      {shellOlder > 0 && (
        <Button
          type="button"
          variant="link"
          className="h-auto justify-start p-0 text-sm coarse:min-h-11"
          onClick={() => void scanShellCamera({ includeOlder: true })}
        >
          Ajouter {plural(shellOlder, "photo plus ancienne restée", "photos plus anciennes restées")} sur l&apos;appareil
        </Button>
      )}
      {(state.alreadyImported > 0 || skipped || state.olderCount > 0 || (state.showOlder && state.photos.length > 0)) && (
        <div className="flex flex-col gap-1 text-sm text-muted-foreground sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-4">
          {state.alreadyImported > 0 && <p>{plural(state.alreadyImported, "photo déjà rangée", "photos déjà rangées")}</p>}
          {skipped && (
            <p>
              {skipped}
              {state.skipped.raw > 0 && " — réglez l'appareil en RAW + JPEG"}
            </p>
          )}
          {(state.olderCount > 0 || state.showOlder) && (
            <Button
              type="button"
              variant="link"
              className="h-auto justify-start p-0 text-sm coarse:min-h-11"
              onClick={() => void setShowOlder(!state.showOlder)}
            >
              {state.showOlder
                ? "Masquer les photos plus anciennes"
                : `Afficher aussi ${plural(state.olderCount, "photo plus ancienne", "photos plus anciennes")}`}
            </Button>
          )}
        </div>
      )}

      {doctors.length > 1 && newCameras.map((camera) => (
        <CameraOwnerPrompt key={camera.key} camera={camera} doctors={doctors} />
      ))}
      {state.cameras.map((camera) => (
        <ClockOffsetNotice key={camera.key} camera={camera} />
      ))}
      {state.rangeTrimmed && (
        <p role="status" className="rounded-lg bg-warning-wash p-3 text-sm text-warning-ink">
          Les photos prises plus d&apos;un mois avant les autres ne sont comparées à aucun agenda : choisissez leur
          patient.
        </p>
      )}

      {state.groups.length === 0 ? (
        <EmptyState
          icon={Images}
          title="Aucune nouvelle photo à ranger"
          description={
            state.olderCount > 0
              ? "Les photos de la carte sont plus anciennes que la dernière importation."
              : "Toutes les photos de la carte sont déjà rangées."
          }
          action={
            <Button type="button" onClick={resetImport}>
              Choisir une autre carte
            </Button>
          }
        />
      ) : (
        <ol className="space-y-3" aria-label="Groupes de photos">
          {state.groups.map((group) => {
            const patient = patientOf(group, state)
            return (
              <BurstCard
                key={group.key}
                group={group}
                photos={photos}
                patient={patient}
                chosen={Boolean(state.choices[group.key])}
                ignored={Boolean(state.ignored[group.key])}
                excluded={state.excluded}
                showDay={days.size > 1}
                doctorNames={doctorNames}
                cameraLabel={cameraLabels?.get(group.cameraKey) ?? null}
              />
            )
          })}
        </ol>
      )}

      {state.groups.length > 0 && (
        // Sticky to the page scroller, so « Ranger » is on screen at 730 px tall however long the list is.
        <div className="sticky bottom-0 z-20 -mx-4 border-t border-border bg-card/95 px-4 py-3 backdrop-blur-sm md:-mx-6 md:px-6">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0 space-y-0.5 text-sm" role="status">
              {unresolved.length > 0 ? (
                <p className="text-warning-ink">
                  Choisissez le patient de {plural(unresolved.length, "groupe", "groupes")}, ou ignorez-
                  {unresolved.length > 1 ? "les" : "le"}.{" "}
                  <Button type="button" variant="link" className="h-auto p-0 text-sm text-warning-ink underline" onClick={goToFirstUnresolved}>
                    Voir
                  </Button>
                </p>
              ) : plan.length === 0 ? (
                <p className="text-muted-foreground">Aucune photo à ranger.</p>
              ) : (
                <p>
                  {plural(plan.length, "photo", "photos")} pour {plural(patientCount, "patient", "patients")}
                </p>
              )}
              {shortBy > 0 && (
                <p className="text-destructive">
                  Espace presque plein : il manque environ {formatFileSize(shortBy)}, les dernières photos seront
                  refusées.
                </p>
              )}
            </div>
            <Button
              type="button"
              className="w-full coarse:min-h-11 sm:w-auto"
              disabled={unresolved.length > 0 || plan.length === 0}
              onClick={() => void startFiling(policy)}
            >
              Ranger {plural(plan.length, "photo", "photos")}
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

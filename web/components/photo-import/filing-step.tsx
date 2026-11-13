"use client"

import Link from "next/link"
import { AlertTriangle, CheckCircle2, FolderOpen, Loader2, RotateCcw } from "lucide-react"
import { PatientNameLink } from "@/components/patient-name-link"
import { Button } from "@/components/ui/button"
import { Progress } from "@/components/ui/progress"
import { resetImport, retryFailed, type FilingItem, type ImportPhoto, type ImportState } from "@/lib/photo-import/session"

interface PatientTally {
  patientId: string
  patientName: string
  filed: number
  already: number
  failed: number
  waiting: number
}

function tallies(items: FilingItem[]): PatientTally[] {
  const byPatient = new Map<string, PatientTally>()
  for (const item of items) {
    const tally =
      byPatient.get(item.patientId) ??
      { patientId: item.patientId, patientName: item.patientName, filed: 0, already: 0, failed: 0, waiting: 0 }
    if (item.status === "filed") tally.filed++
    else if (item.status === "already") tally.already++
    else if (item.status === "failed" || item.status === "blocked") tally.failed++
    else tally.waiting++
    byPatient.set(item.patientId, tally)
  }
  return [...byPatient.values()].sort((a, b) => a.patientName.localeCompare(b.patientName, "fr"))
}

const plural = (count: number, one: string, many: string) => `${count} ${count > 1 ? many : one}`

/**
 * « Ranger » running, then done (`features/camera-photo-import`). One row per patient — that is how the dentist checks
 * the result — each opening the patient's files; a failure names the photo and the server's own reason.
 */
export function FilingStep({ state, photos }: { state: ImportState; photos: Map<string, ImportPhoto> }) {
  const items = state.filing
  const settled = items.filter((item) => item.status !== "pending" && item.status !== "sending").length
  const filed = items.filter((item) => item.status === "filed").length
  const failures = items.filter((item) => item.status === "failed" || item.status === "blocked")
  const running = state.phase === "filing"
  const percent = items.length > 0 ? Math.round((settled / items.length) * 100) : 100

  return (
    <div className="space-y-5">
      <section className="space-y-3 rounded-lg border border-border bg-card p-4" aria-label="Rangement des photos">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="flex items-center gap-2 font-medium" role="status">
            {running ? (
              <Loader2 className="size-4 text-primary motion-safe:animate-spin" aria-hidden="true" />
            ) : failures.length === 0 ? (
              <CheckCircle2 className="size-4 text-success" aria-hidden="true" />
            ) : (
              <AlertTriangle className="size-4 text-warning-ink" aria-hidden="true" />
            )}
            {running
              ? `Rangement : ${settled} sur ${items.length}`
              : failures.length === 0
                ? `${plural(filed, "photo rangée", "photos rangées")}`
                : `${plural(failures.length, "photo non rangée", "photos non rangées")} sur ${items.length}`}
          </p>
          {!running && (
            <div className="flex flex-wrap gap-2">
              {failures.length > 0 && !state.storageFullMessage && (
                <Button type="button" variant="outline" className="gap-2 coarse:min-h-11" onClick={() => void retryFailed()}>
                  <RotateCcw className="size-4" aria-hidden="true" />
                  Réessayer
                </Button>
              )}
              <Button type="button" className="coarse:min-h-11" onClick={resetImport}>
                Importer d&apos;autres photos
              </Button>
            </div>
          )}
        </div>
        <Progress value={percent} className="h-2" aria-label={`Photos traitées : ${percent} %`} />
        {running && (
          <p className="text-xs text-muted-foreground">Vous pouvez changer de page : l&apos;envoi continue.</p>
        )}
        {state.storageFullMessage && (
          <p role="alert" className="text-sm text-destructive">
            {state.storageFullMessage}
          </p>
        )}
      </section>

      <ul className="divide-y divide-border rounded-lg border border-border bg-card" aria-label="Photos par patient">
        {tallies(items).map((tally) => (
          <li key={tally.patientId} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
            <div className="min-w-0">
              <PatientNameLink patientId={tally.patientId} name={tally.patientName} />
              <p className="text-xs text-muted-foreground">
                {[
                  tally.filed > 0 && plural(tally.filed, "rangée", "rangées"),
                  tally.already > 0 && plural(tally.already, "déjà rangée", "déjà rangées"),
                  tally.failed > 0 && plural(tally.failed, "non rangée", "non rangées"),
                  tally.waiting > 0 && plural(tally.waiting, "en attente", "en attente"),
                ]
                  .filter(Boolean)
                  .join(" · ")}
              </p>
            </div>
            {tally.filed + tally.already > 0 && (
              <Button asChild variant="outline" size="sm" className="gap-1.5 coarse:min-h-11">
                <Link href={`/patients/${tally.patientId}/files`}>
                  <FolderOpen className="size-4" aria-hidden="true" />
                  Ouvrir les fichiers
                </Link>
              </Button>
            )}
          </li>
        ))}
      </ul>

      {!running && failures.length > 0 && (
        <section className="space-y-2" aria-label="Photos non rangées">
          <h2 className="text-sm font-medium">Photos non rangées</h2>
          <ul className="space-y-1 text-sm">
            {failures.slice(0, 20).map((item) => (
              <li key={item.photoId} className="flex flex-col gap-0.5 rounded-md bg-muted/50 px-3 py-2 sm:flex-row sm:gap-2">
                <span className="font-medium">{photos.get(item.photoId)?.file.name ?? "Photo"}</span>
                <span className="text-muted-foreground">{item.message ?? "Envoi refusé."}</span>
              </li>
            ))}
          </ul>
          {failures.length > 20 && (
            <p className="text-xs text-muted-foreground">Et {failures.length - 20} autres.</p>
          )}
        </section>
      )}
    </div>
  )
}

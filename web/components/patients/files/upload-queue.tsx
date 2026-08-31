"use client"

import { useCallback, useRef, useState } from "react"
import { AlertTriangle, CheckCircle2, Loader2, Ban } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Progress } from "@/components/ui/progress"
import { patientFilesApi } from "@/lib/api/patient-files"
import { getErrorMessage } from "@/lib/errors"
import { formatFileSize } from "@/lib/format"
import { destinationFor, refusalFor, type FileDestination, type UploadPolicy } from "@/lib/api/upload-policy"
import { ingestIntoVault } from "@/lib/vault/ingest"
import { cn } from "@/lib/utils"

/**
 * Per-file upload with a per-file outcome (AC-5.4), on `import-patients-dialog.tsx`'s `RowCard` shape.
 *
 * <p>It replaces a `Promise.all` over the whole selection, whose rejection reported **the batch** as failed:
 * pick ten radiographs, have one refused on its signature, and the toast said all ten failed while nine were
 * already stored. Here each file carries its own state and its own reason, and a refusal is visibly not a
 * network failure.</p>
 *
 * <p>Concurrency is bounded because the caps are now 150 MB: ten simultaneous CBCT studies saturate a clinic's
 * uplink and every one of them times out together.</p>
 */
const MAX_CONCURRENT_UPLOADS = 3

export type UploadItemState = "pending" | "uploading" | "done" | "refused" | "failed"

export interface UploadItem {
  id: string
  name: string
  size: number
  state: UploadItemState
  /** French, and the server's own words wherever the server produced them. */
  reason?: string
  /**
   * Which door this file goes through. `vault` means the bytes are copied inside the cabinet and never sent —
   * shown, because « conservé au cabinet » is a different promise from « envoyé » and a user filing a 400 Mo
   * study in four seconds would otherwise reasonably assume it failed.
   */
  destination: FileDestination
  /** 0 … 1 while a coffre copy runs. Absent for a hosted upload, which has no progress to report. */
  progress?: number
}

export function useUploadQueue({
  patientId,
  folderId,
  policy,
  vault,
  onFileUploaded,
}: {
  patientId: string
  folderId?: string
  policy?: UploadPolicy | null
  /** This machine's coffre, or null. Its absence is what makes a study-class file refusable (AC-6). */
  vault?: FileSystemDirectoryHandle | null
  onFileUploaded: () => void
}) {
  const [items, setItems] = useState<UploadItem[]>([])
  const [running, setRunning] = useState(false)
  const nextId = useRef(0)

  const update = useCallback((id: string, change: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...change } : item)))
  }, [])

  const enqueue = useCallback(
    async (files: File[]) => {
      if (files.length === 0) return

      const queued = files.map((file) => {
        // AC-5.1 / AC-6 — refused instantly, in French, before a byte moves. The server re-checks every one of
        // these; `vaultReachable` is the term that turns « this belongs at the cabinet » into a real refusal on a
        // machine with no coffre, rather than an upload that would be refused after the wait.
        const refusal = policy ? refusalFor(policy, file, { vaultReachable: !!vault }) : null

        return {
          file,
          item: {
            id: `u${nextId.current++}`,
            name: file.name,
            size: file.size,
            state: (refusal ? "refused" : "pending") as UploadItemState,
            reason: refusal ?? undefined,
            destination: policy ? destinationFor(policy, file) : ("hosted" as FileDestination),
          },
        }
      })

      setItems((current) => [...current, ...queued.map((entry) => entry.item)])

      const sendable = queued.filter((entry) => entry.item.state === "pending")
      if (sendable.length === 0) return

      setRunning(true)
      let cursor = 0
      const worker = async () => {
        while (cursor < sendable.length) {
          const entry = sendable[cursor++]
          update(entry.item.id, { state: "uploading" })
          try {
            if (entry.item.destination === "vault") {
              // `vault` is non-null here: a null one makes every study-class file `refused` above, so it never
              // reaches this queue.
              await ingestIntoVault(vault!, patientId, entry.file, {
                folderId,
                onProgress: ({ copied }) => update(entry.item.id, { progress: copied }),
              })
            } else {
              await patientFilesApi.uploadFile(patientId, entry.file, folderId)
            }

            update(entry.item.id, { state: "done", progress: undefined })
            onFileUploaded()
          } catch (error) {
            update(entry.item.id, {
              state: "failed",
              progress: undefined,
              reason: getErrorMessage(error, "L'envoi a échoué."),
            })
          }
        }
      }

      await Promise.all(
        Array.from({ length: Math.min(MAX_CONCURRENT_UPLOADS, sendable.length) }, worker),
      )
      setRunning(false)
    },
    [patientId, folderId, policy, vault, onFileUploaded, update],
  )

  const clear = useCallback(() => setItems([]), [])

  return { items, enqueue, clear, running }
}

const STATE_STYLE: Record<UploadItemState, { icon: typeof Loader2; label: string; tone: string }> = {
  pending: { icon: Loader2, label: "En attente", tone: "text-muted-foreground" },
  uploading: { icon: Loader2, label: "Envoi…", tone: "text-primary" },
  done: { icon: CheckCircle2, label: "Envoyé", tone: "text-success" },
  refused: { icon: Ban, label: "Refusé", tone: "text-destructive" },
  failed: { icon: AlertTriangle, label: "Échec", tone: "text-destructive" },
}

export function UploadQueue({
  items,
  running,
  onClear,
}: {
  items: UploadItem[]
  running: boolean
  onClear: () => void
}) {
  if (items.length === 0) return null

  const settled = items.filter((item) => item.state !== "pending" && item.state !== "uploading").length
  const sent = items.filter((item) => item.state === "done").length

  return (
    <section className="space-y-3" aria-label="Envois en cours">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-medium" role="status">
          {sent} / {items.length} {items.length === 1 ? "fichier envoyé" : "fichiers envoyés"}
        </p>
        {!running && (
          <Button variant="ghost" size="sm" onClick={onClear} className="coarse:h-11">
            Effacer la liste
          </Button>
        )}
      </div>

      <Progress value={Math.round((settled / items.length) * 100)} aria-label="Progression des envois" />

      <ul className="space-y-2">
        {items.map((item) => {
          const style = STATE_STYLE[item.state]
          const Icon = style.icon

          return (
            <li key={item.id} className="rounded-lg border p-3">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="min-w-0 break-words text-sm font-medium">{item.name}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatFileSize(item.size)}
                    {item.destination === "vault" && " · conservé au cabinet"}
                  </p>
                </div>
                <span className={cn("flex items-center gap-1.5 text-xs font-medium", style.tone)}>
                  <Icon className={cn("h-4 w-4", item.state === "uploading" && "animate-spin")} />
                  {/* A coffre file is never « envoyé » — nothing left the practice, and saying so would be a
                      different promise from the one that was kept. */}
                  {item.destination === "vault" && item.state === "done" ? "Enregistré" : style.label}
                </span>
              </div>
              {/* Only the coffre copy has progress worth showing: it moves hundreds of megabytes across a disk
                  while a hosted upload is a single request with no readable milestones. */}
              {item.state === "uploading" && item.progress !== undefined && (
                <Progress
                  className="mt-2 h-1.5"
                  value={Math.round(item.progress * 100)}
                  aria-label={`Copie de ${item.name}`}
                />
              )}
              {item.reason && <p className="mt-2 text-xs text-destructive">{item.reason}</p>}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

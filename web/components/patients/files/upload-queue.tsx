"use client"

import { useCallback, useRef, useState } from "react"
import { AlertTriangle, CheckCircle2, Loader2, Ban } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Progress } from "@/components/ui/progress"
import { patientFilesApi } from "@/lib/api/patient-files"
import { getErrorMessage } from "@/lib/errors"
import { formatFileSize } from "@/lib/format"
import { refusalFor, type UploadPolicy } from "@/lib/api/upload-policy"
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
}

export function useUploadQueue({
  patientId,
  folderId,
  policy,
  onFileUploaded,
}: {
  patientId: string
  folderId?: string
  policy?: UploadPolicy | null
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

      const queued = files.map((file) => ({
        file,
        item: {
          id: `u${nextId.current++}`,
          name: file.name,
          size: file.size,
          // AC-5.1 — refused instantly, in French, before a byte leaves the browser. The server re-checks.
          state: (policy && refusalFor(policy, file) ? "refused" : "pending") as UploadItemState,
          reason: policy ? refusalFor(policy, file) ?? undefined : undefined,
        },
      }))

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
            await patientFilesApi.uploadFile(patientId, entry.file, folderId)
            update(entry.item.id, { state: "done" })
            onFileUploaded()
          } catch (error) {
            update(entry.item.id, {
              state: "failed",
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
    [patientId, folderId, policy, onFileUploaded, update],
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
                  <p className="text-xs text-muted-foreground">{formatFileSize(item.size)}</p>
                </div>
                <span className={cn("flex items-center gap-1.5 text-xs font-medium", style.tone)}>
                  <Icon className={cn("h-4 w-4", item.state === "uploading" && "animate-spin")} />
                  {style.label}
                </span>
              </div>
              {item.reason && <p className="mt-2 text-xs text-destructive">{item.reason}</p>}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

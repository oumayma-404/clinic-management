"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { AlertTriangle, CheckCircle2, Loader2, Ban, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Progress } from "@/components/ui/progress"
import { patientFilesApi } from "@/lib/api/patient-files"
import { getErrorMessage } from "@/lib/errors"
import { formatFileSize } from "@/lib/format"
import {
  destinationFor,
  refusalFor,
  shouldUploadInParts,
  type FileDestination,
  type UploadPolicy,
} from "@/lib/api/upload-policy"
import { ingestIntoVault } from "@/lib/vault/ingest"
import { buildPreview } from "@/lib/files/preview"
import { uploadInParts, UploadCancelledError } from "@/lib/files/resumable-upload"
import { forgetUpload, rememberUpload, type InterruptedUpload } from "@/lib/files/upload-resume-store"
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

export type UploadItemState = "pending" | "uploading" | "done" | "refused" | "failed" | "cancelled"

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
  /**
   * 0 … 1 while a coffre copy or a **chunked** upload runs. Still absent for a single-POST upload, and that
   * absence is honest rather than a gap: `fetch` reports nothing about a request body it is sending, so the only
   * bar such an upload could draw is an animation. Sending in parts is what makes the number real — it is the
   * server's own byte count, coming back on every confirmed part.
   */
  progress?: number
  /** Set while a chunked upload is in flight, which is exactly when it can be interrupted and resumed. */
  cancellable?: boolean
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

  /**
   * One controller per in-flight chunked upload, so « Annuler » reaches the request that is actually moving.
   * A ref and not state: aborting must not re-render, and a stale closure here would cancel the wrong file.
   */
  const controllers = useRef(new Map<string, AbortController>())

  /**
   * The upload sessions this queue is working on right now.
   *
   * ⚠️ It exists because `rememberUpload` writes a record the moment a session opens, so without it
   * `ResumeUploadsNotice` offers « Reprendre » for a file the queue is uploading two centimetres below —
   * measured, at « 0 o sur 29,8 Mo déjà envoyés » beside a bar reading 54 %. Accepting that offer would open a
   * second client against one session and have its parts refused as out of order. The record must exist (a tab
   * closed mid-upload is exactly what it is for), so what has to change is who is *shown* it.
   */
  const [activeUploads, setActiveUploads] = useState<string[]>([])

  const update = useCallback((id: string, change: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...change } : item)))
  }, [])

  /**
   * Sends one file through whichever of the three doors it belongs to.
   *
   * ⚠️ The **chunked** branch is chosen on size alone, and only where the server published a chunk size — see
   * `shouldUploadInParts`. A file that fits in one part goes through the single POST it always did: the protocol
   * would cost it three extra round trips and buy nothing, since the smallest thing it could resume is the whole
   * file.
   */
  const sendOne = useCallback(
    async (item: UploadItem, file: File, resumeFrom?: string) => {
      if (item.destination === "vault") {
        // `vault` is non-null here: a null one makes every study-class file `refused` at enqueue, so it never
        // reaches this queue.
        await ingestIntoVault(vault!, patientId, file, {
          folderId,
          onProgress: ({ copied }) => update(item.id, { progress: copied }),
        })
        return
      }

      // ⚠️ Built here, not on the server: the browser already holds the bytes and the codecs, and the clinic's
      // own machine is idle while the upload waits. `buildPreview` returns null for anything it cannot paint,
      // which the upload carries as « no stand-in » rather than as a failure.
      const preview = await buildPreview(file)

      if (!shouldUploadInParts(policy, file)) {
        await patientFilesApi.uploadFile(patientId, file, folderId, undefined, preview)
        return
      }

      const controller = new AbortController()
      controllers.current.set(item.id, controller)
      update(item.id, { cancellable: true, progress: 0 })

      // An object and not a `let`: the id is assigned inside a callback, and TypeScript's flow analysis would
      // narrow a plain local to `null` for the whole of the catch — where it is exactly what we need.
      const opened: { id: string | null } = { id: null }

      // Every session this attempt touches. Usually one; two when a resumed session turned out to be gone and a
      // fresh one was opened in its place — and then BOTH have to stop being offered, or the dead one is left on
      // screen as something to continue.
      const touched = new Set<string>()
      const markActive = (id: string) => {
        if (touched.has(id)) return
        touched.add(id)
        setActiveUploads((current) => (current.includes(id) ? current : [...current, id]))
      }

      // A resumed session is known before the first request, so it stops being offered immediately rather than
      // one round trip later — that gap is the whole width of a double-click on « Reprendre ».
      if (resumeFrom) markActive(resumeFrom)

      try {
        await uploadInParts({
          patientId,
          file,
          folderId,
          preview,
          resumeFrom,
          signal: controller.signal,
          onProgress: ({ session, fraction }) => {
            opened.id = session.uploadId
            // Before `rememberUpload`, so the record is never readable as « interrupted » while it is live.
            markActive(session.uploadId)
            update(item.id, { progress: fraction })

            // ⚠️ **Only once a part has really landed.** `onProgress` fires as soon as the session opens, so
            // remembering unconditionally means every upload dropped in its first seconds leaves an offer to
            // « reprendre » nothing — « 0 o sur 29,8 Mo déjà envoyés », a second hunt through the file system to
            // achieve exactly what « Téléverser » does. There is no staging area to continue from either, so
            // the record would be describing something that does not exist.
            if (session.receivedParts > 0) {
              // Re-written on every confirmed part, so a tab closed mid-upload is offered back with a count the
              // server agrees with rather than one this page happened to reach.
              void rememberUpload({
                uploadId: session.uploadId,
                patientId,
                folderId,
                fileName: file.name,
                fileSize: file.size,
                lastModified: file.lastModified,
                receivedBytes: session.receivedBytes,
                expiresAtUtc: session.expiresAtUtc,
              })
            }
          },
        })
        if (opened.id) void forgetUpload(opened.id)
      } catch (error) {
        // A cancellation releases the staging area; every other failure leaves it, because the upload is still
        // resumable and the parts already sent are the whole point of having kept them.
        if (error instanceof UploadCancelledError && opened.id) {
          void patientFilesApi.abandonUpload(patientId, opened.id).catch(() => undefined)
          void forgetUpload(opened.id)
        }
        throw error
      } finally {
        controllers.current.delete(item.id)
        setActiveUploads((current) => current.filter((id) => !touched.has(id)))
      }
    },
    [patientId, folderId, policy, vault, update],
  )

  const run = useCallback(
    async (sendable: Array<{ file: File; item: UploadItem; resumeFrom?: string }>) => {
      if (sendable.length === 0) return

      setRunning(true)
      let cursor = 0
      const worker = async () => {
        while (cursor < sendable.length) {
          const entry = sendable[cursor++]
          update(entry.item.id, { state: "uploading" })
          try {
            await sendOne(entry.item, entry.file, entry.resumeFrom)
            update(entry.item.id, { state: "done", progress: undefined, cancellable: false })
            onFileUploaded()
          } catch (error) {
            // ⚠️ A cancellation is not a failure and must not be worded as one: an aborted `fetch` reaches us as
            // the same event a fired deadline does, and « Vérifiez votre connexion » after pressing « Annuler »
            // reads as the app having lost the file.
            const cancelled = error instanceof UploadCancelledError
            update(entry.item.id, {
              state: cancelled ? "cancelled" : "failed",
              progress: undefined,
              cancellable: false,
              reason: cancelled ? undefined : getErrorMessage(error, "L'envoi a échoué."),
            })
          }
        }
      }

      await Promise.all(
        Array.from({ length: Math.min(MAX_CONCURRENT_UPLOADS, sendable.length) }, worker),
      )
      setRunning(false)
    },
    [sendOne, onFileUploaded, update],
  )

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
      await run(queued.filter((entry) => entry.item.state === "pending"))
    },
    [policy, vault, run],
  )

  /**
   * Picks an interrupted upload back up with the file the user has just re-chosen.
   *
   * ⚠️ It goes through the same refusal pre-check as a new file rather than trusting the remembered session: the
   * deployment's policy may have changed since, and « it was accepted yesterday » is not a reason to send
   * something today's server will refuse at the end.
   */
  const resume = useCallback(
    async (record: InterruptedUpload, file: File) => {
      const refusal = policy ? refusalFor(policy, file, { vaultReachable: !!vault }) : null

      const item: UploadItem = {
        id: `u${nextId.current++}`,
        name: file.name,
        size: file.size,
        state: refusal ? "refused" : "pending",
        reason: refusal ?? undefined,
        destination: policy ? destinationFor(policy, file) : "hosted",
      }

      setItems((current) => [...current, item])
      if (refusal) return

      await run([{ file, item, resumeFrom: record.uploadId }])
    },
    [policy, vault, run],
  )

  const cancel = useCallback((id: string) => {
    controllers.current.get(id)?.abort()
  }, [])

  const clear = useCallback(
    () => setItems((current) => current.filter((item) => item.state === "uploading")),
    [],
  )

  // Aborting on unmount is what stops a queue left running by a navigation from writing progress into a store
  // nothing will read, and releases each staging area rather than leaving it to expire a day later.
  useEffect(() => {
    const inFlight = controllers.current
    return () => {
      inFlight.forEach((controller) => controller.abort())
    }
  }, [])

  /**
   * How many uploads have finished, one way or another.
   *
   * ⚠️ It exists so `ResumeUploadsNotice` knows when to re-read its store, and the obvious substitute —
   * `items.length` — is wrong in the way that matters: it changes when an upload *starts* and never again, so
   * a resumed file would finish while the offer to resume it stayed on screen. An offer to continue something
   * already stored is not stale decoration; it is the app contradicting itself about what it holds.
   */
  const settledCount = items.filter(
    (item) => item.state !== "pending" && item.state !== "uploading",
  ).length

  return { items, enqueue, resume, cancel, clear, running, settledCount, activeUploads }
}

const STATE_STYLE: Record<UploadItemState, { icon: typeof Loader2; label: string; tone: string }> = {
  pending: { icon: Loader2, label: "En attente", tone: "text-muted-foreground" },
  uploading: { icon: Loader2, label: "Envoi…", tone: "text-primary" },
  done: { icon: CheckCircle2, label: "Envoyé", tone: "text-success" },
  refused: { icon: Ban, label: "Refusé", tone: "text-destructive" },
  failed: { icon: AlertTriangle, label: "Échec", tone: "text-destructive" },
  cancelled: { icon: Ban, label: "Annulé", tone: "text-muted-foreground" },
}

export function UploadQueue({
  items,
  running,
  onCancel,
  onClear,
}: {
  items: UploadItem[]
  running: boolean
  onCancel: (id: string) => void
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
          const percent = item.progress === undefined ? null : Math.round(item.progress * 100)

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
                <div className="flex items-center gap-1">
                  <span className={cn("flex items-center gap-1.5 text-xs font-medium", style.tone)}>
                    <Icon className={cn("h-4 w-4", item.state === "uploading" && "animate-spin")} />
                    {/* A coffre file is never « envoyé » — nothing left the practice, and saying so would be a
                        different promise from the one that was kept. */}
                    {item.destination === "vault" && item.state === "done" ? "Enregistré" : style.label}
                  </span>
                  {/* Only a chunked upload can be stopped: it is the one that lasts long enough to be worth
                      stopping, and the one whose staging area there is something to release. */}
                  {item.state === "uploading" && item.cancellable && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-8 w-8 shrink-0 coarse:h-11 coarse:w-11"
                      aria-label={`Annuler l'envoi de ${item.name}`}
                      onClick={() => onCancel(item.id)}
                    >
                      <X className="h-4 w-4" />
                    </Button>
                  )}
                </div>
              </div>
              {/* A coffre copy moves hundreds of megabytes across a disk; a chunked upload reports the server's
                  own byte count on every part. A single POST still shows nothing, because there is nothing true
                  to show. */}
              {item.state === "uploading" && percent !== null && (
                <div className="mt-2 flex items-center gap-2">
                  <Progress className="h-1.5" value={percent} aria-label={`Envoi de ${item.name}`} />
                  <span className="shrink-0 text-xs tabular-nums text-muted-foreground">{percent} %</span>
                </div>
              )}
              {item.reason && <p className="mt-2 text-xs text-destructive">{item.reason}</p>}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

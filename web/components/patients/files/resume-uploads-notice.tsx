"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { RotateCcw } from "lucide-react"

import { Button } from "@/components/ui/button"
import { patientFilesApi } from "@/lib/api/patient-files"
import { formatFileSize, quoteFr } from "@/lib/format"
import {
  forgetUpload,
  interruptedUploadsFor,
  isSameFile,
  type InterruptedUpload,
} from "@/lib/files/upload-resume-store"

/**
 * « Un envoi a été interrompu » — the offer to carry on with a file whose upload did not finish.
 *
 * <p>The server keeps the parts it received for twenty-four hours, so after a closed tab or a reloaded page the
 * only thing missing is the bytes — and they are still on the user's disk. This asks for them back.</p>
 *
 * ⚠️ **The file has to be re-chosen, and that is a decision rather than a limitation.** A browser can be made to
 * remember the file itself, which would make this one click instead of two; it would also leave a copy of a
 * patient's imaging in a shared clinic PC's browser profile, surviving reboots. See `upload-resume-store.ts`.
 *
 * ⚠️ **It renders nothing at all when there is nothing to resume** — the ordinary case, every day. A permanent
 * strip explaining a feature nobody is currently using is the noise that makes a screen feel complicated.
 */
export function ResumeUploadsNotice({
  patientId,
  onResume,
  /**
   * The queue's settled count. Bumped when an upload **finishes**, which is when this list may have changed —
   * see `useUploadQueue`'s own note on why the number of queued items is the wrong signal.
   */
  reloadToken,
  /**
   * The upload sessions the queue is running right now.
   *
   * ⚠️ **They are records and must stay records** — a tab closed mid-upload is precisely what the store is for —
   * but they must not be *offered*: a file uploading two centimetres below is not something to resume, and
   * accepting would open a second client against one session and have its parts refused as out of order.
   */
  inFlight = [],
}: {
  patientId: string
  onResume: (record: InterruptedUpload, file: File) => void
  reloadToken?: number
  inFlight?: string[]
}) {
  const [records, setRecords] = useState<InterruptedUpload[]>([])
  const [mismatch, setMismatch] = useState<string | null>(null)
  const picker = useRef<HTMLInputElement>(null)
  const awaiting = useRef<InterruptedUpload | null>(null)

  const reload = useCallback(() => {
    void interruptedUploadsFor(patientId).then(setRecords)
  }, [patientId])

  useEffect(reload, [reload, reloadToken])

  const askForFile = (record: InterruptedUpload) => {
    setMismatch(null)
    awaiting.current = record
    picker.current?.click()
  }

  const abandon = async (record: InterruptedUpload) => {
    // Release the staging area as well as forgetting it here: an upload dropped on purpose should not sit on the
    // server for a day waiting to expire.
    await patientFilesApi.abandonUpload(patientId, record.uploadId).catch(() => undefined)
    await forgetUpload(record.uploadId)
    reload()
  }

  const offered = records.filter((record) => !inFlight.includes(record.uploadId))

  if (offered.length === 0) return null

  return (
    <section className="space-y-2" aria-label="Envois interrompus">
      <input
        ref={picker}
        type="file"
        className="hidden"
        onChange={(event) => {
          // ⚠️ Copy and clear before doing anything else — the element keeps the file it just handed over, so
          // re-picking the same one fires no `change` at all and a second attempt would silently do nothing.
          const chosen = event.target.files?.[0] ?? null
          event.target.value = ""

          const record = awaiting.current
          awaiting.current = null
          if (!record || !chosen) return

          if (!isSameFile(record, chosen)) {
            setMismatch(
              `Ce n'est pas le fichier ${quoteFr(record.fileName)} qui était en cours d'envoi. ` +
                "Choisissez le même fichier, ou abandonnez cet envoi pour recommencer.",
            )
            return
          }

          setMismatch(null)
          // No optimistic removal here: `useUploadQueue.resume` marks the session in flight before its first
          // request, so `inFlight` takes it out of the list on the very next render — one mechanism for both
          // « resumed from this notice » and « started fresh by the picker », rather than one each.
          onResume(record, chosen)
        }}
      />

      {offered.map((record) => (
        <div
          key={record.uploadId}
          role="status"
          className="flex flex-col gap-2 rounded-lg border border-dashed bg-muted/40 p-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <div className="min-w-0">
            <p className="min-w-0 break-words text-sm font-medium">
              L&apos;envoi de {quoteFr(record.fileName)} a été interrompu
            </p>
            <p className="text-xs text-muted-foreground">
              {formatFileSize(record.receivedBytes)} sur {formatFileSize(record.fileSize)} déjà envoyés.
              Choisissez à nouveau le fichier pour reprendre là où il s&apos;est arrêté.
            </p>
          </div>
          <div className="flex shrink-0 flex-wrap gap-2">
            <Button size="sm" className="coarse:h-11" onClick={() => askForFile(record)}>
              <RotateCcw className="mr-2 h-4 w-4" />
              Reprendre
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="coarse:h-11"
              onClick={() => void abandon(record)}
            >
              Abandonner
            </Button>
          </div>
        </div>
      ))}

      {mismatch && (
        <p className="text-xs text-destructive" role="alert">
          {mismatch}
        </p>
      )}
    </section>
  )
}

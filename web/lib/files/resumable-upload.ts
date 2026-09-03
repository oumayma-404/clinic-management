import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
import type { FileUploadSessionDto, PatientFileDto } from "@/lib/api/types"

/**
 * An upload sent in parts, and resumed where it stopped.
 *
 * <p>This is the browser half of `features/large-file-transfer` Part 2. The server half is five endpoints; this
 * is the only thing in the app that calls them, and the order they go in lives here rather than in the queue
 * component so that « what happens when part 7 fails » has one answer.</p>
 *
 * ⚠️ **The point is not speed — it is that an interruption stops costing the whole file.** A 150 Mo study over a
 * clinic's uplink is minutes of sending; a single POST that dies at 95 % starts again at zero, and nothing in the
 * product could even tell the user that was what happened. Here a dropped connection costs one part.
 */

/** How many times one part is re-attempted before the upload is reported as failed. */
const PART_ATTEMPTS = 4

/** 1 s, 2 s, 4 s — long enough for a cell hand-off, short enough that a dead link is not a two-minute wait. */
const RETRY_BACKOFF_MS = [1_000, 2_000, 4_000]

/**
 * A cancellation, which is **not a failure** and must not be worded as one.
 *
 * <p>An aborted `fetch` reaches us as the same `AbortError` a fired deadline produces, which `client.ts` maps to
 * « Vérifiez votre connexion » — true for a timeout and a lie for a user who just pressed « Annuler ». Its own
 * type is what lets the queue tell them apart.</p>
 */
export class UploadCancelledError extends Error {
  constructor() {
    super("Envoi annulé.")
    this.name = "UploadCancelledError"
  }
}

/**
 * Told to the caller each time the server confirms a part, so an interrupted upload can be offered back to the
 * user after a reload. Deliberately **not** the bytes: see `upload-resume-store.ts`.
 */
export interface ResumableUploadProgress {
  session: FileUploadSessionDto
  /** 0…1, from the server's own byte count rather than from what the browser believes it sent. */
  fraction: number
}

export interface ResumableUploadOptions {
  patientId: string
  file: File
  folderId?: string
  description?: string
  /** The small stand-in image, built while the parts travel. Never worth failing an upload for. */
  preview?: Blob | null
  /**
   * An upload already opened, from the resume store. Its session is re-read and continued; if it has expired or
   * been cleaned up, a fresh one is opened and the file starts again — which is the honest outcome, because its
   * staged parts no longer exist.
   */
  resumeFrom?: string
  signal?: AbortSignal
  /** Called once the session exists, and again after every confirmed part. */
  onProgress?: (progress: ResumableUploadProgress) => void
}

/**
 * Sends `file` in parts and returns the stored file.
 *
 * ⚠️ **Every count comes back from the server.** The loop is driven by `session.nextPart`, never by a local
 * counter: a part whose response was lost is stored on the server and unknown to us, and a browser trusting its
 * own tally would skip it and assemble a file with a hole in the middle — the right length in its row, and
 * wrong in a way no error reports.
 */
export async function uploadInParts(options: ResumableUploadOptions): Promise<PatientFileDto> {
  const { patientId, file, signal } = options

  let session = await openOrResume(options)
  options.onProgress?.({ session, fraction: fractionOf(session) })

  while (session.receivedParts < session.totalParts) {
    throwIfCancelled(signal)

    const part = session.nextPart
    const start = (part - 1) * session.chunkSize
    // `Math.min` and not `start + chunkSize`: the last part is short, and the server checks a part's length
    // against its own arithmetic — an over-long final slice is refused, not truncated.
    const end = Math.min(start + session.chunkSize, session.declaredLength)

    session = await sendPart(patientId, session, part, file.slice(start, end), signal)
    options.onProgress?.({ session, fraction: fractionOf(session) })
  }

  throwIfCancelled(signal)
  return patientFilesApi.completeUpload(patientId, session.uploadId, options.preview)
}

/**
 * One part, re-attempted on a transport failure.
 *
 * ⚠️ **A refusal is not retried.** The server's 4xx sentences — the wrong length, a part out of order, a
 * signature that does not match the extension — are all facts about the file or the protocol, and re-sending the
 * same bytes reproduces them exactly while costing the clinic's uplink another eight megabytes.
 *
 * ⚠️ **Between attempts the session is re-read rather than assumed.** The most likely reason a part failed is
 * that the *response* was lost, not the request: the part is stored, and blindly re-sending it would be eight
 * megabytes to be told « already have it ». Asking costs one small GET and is also the only way to notice that
 * the upload expired while we were backing off.
 */
async function sendPart(
  patientId: string,
  session: FileUploadSessionDto,
  part: number,
  chunk: Blob,
  signal: AbortSignal | undefined,
): Promise<FileUploadSessionDto> {
  let current = session

  for (let attempt = 0; ; attempt++) {
    try {
      return await patientFilesApi.uploadChunk(patientId, current.uploadId, part, chunk, signal)
    } catch (error) {
      throwIfCancelled(signal)

      const retryable = error instanceof ApiError && error.code === ApiErrorCode.Network
      if (!retryable || attempt >= PART_ATTEMPTS - 1) throw error

      await pause(RETRY_BACKOFF_MS[Math.min(attempt, RETRY_BACKOFF_MS.length - 1)], signal)

      current = await patientFilesApi.getUpload(patientId, current.uploadId)
      if (current.receivedParts >= part) {
        // It had arrived after all. Nothing more to send for this part.
        return current
      }
    }
  }
}

/**
 * The session to work against: a resumed one where the caller has an id and the server still holds it, a fresh
 * one otherwise.
 *
 * ⚠️ **A resumed session is checked against the length of the file in hand.** After a reload the user re-picks
 * the file and nothing guarantees it is the same one; a mismatch would assemble a study half from each. The
 * declared length is the server's own record of what was opened, so comparing it compares against the bytes
 * already staged.
 *
 * ⚠️ The **name** is deliberately not compared here, and that is a decision. What comes back on a session is the
 * name after `FileNameSanitizer` — which strips path segments and seven characters, collapses whitespace, trims
 * dots and bounds the length — so comparing it with a raw `file.name` needs a second copy of that sanitiser in
 * TypeScript, and a copy that drifts calls ordinary accented filenames « a different file » and silently
 * restarts uploads that were perfectly resumable. The identity check on the *name* belongs where the raw name was
 * recorded: `upload-resume-store.ts`, which never sees the server's version of it.
 */
async function openOrResume(options: ResumableUploadOptions): Promise<FileUploadSessionDto> {
  const { patientId, file, resumeFrom } = options

  if (resumeFrom) {
    try {
      const existing = await patientFilesApi.getUpload(patientId, resumeFrom)
      if (existing.declaredLength === file.size) {
        return existing
      }
      // A different file under a remembered upload. Release the staging area rather than leaving it to expire,
      // then fall through and open a new session for the file actually chosen.
      await patientFilesApi.abandonUpload(patientId, resumeFrom).catch(() => undefined)
    } catch {
      // Gone, expired, or unreadable — all of which mean the staged parts are not there to continue from.
    }
  }

  return patientFilesApi.startUpload(patientId, {
    fileName: file.name,
    fileSize: file.size,
    folderId: options.folderId,
    description: options.description,
  })
}

function fractionOf(session: FileUploadSessionDto): number {
  if (session.declaredLength <= 0) return 0
  return Math.min(1, session.receivedBytes / session.declaredLength)
}

function throwIfCancelled(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new UploadCancelledError()
}

function pause(ms: number, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort)
      resolve()
    }, ms)

    const onAbort = () => {
      clearTimeout(timer)
      reject(new UploadCancelledError())
    }

    signal?.addEventListener("abort", onAbort, { once: true })
  })
}

import { ApiError, ApiErrorCode, apiGet, apiPost } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
import { PhotoImportCode } from "@/lib/api/photo-import"
import { shouldUploadInParts, type UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { buildPreview } from "@/lib/files/preview"
import { uploadInParts } from "@/lib/files/resumable-upload"
import { toE164 } from "@/lib/phone"
import { CABINET_PREFIX, filesFor, findExisting, type ImportPlan, type PlanFile, type PlanPatient } from "./plan"
import { genderOf, maritalStatusOf, phoneOf, type LegacyHistoryEntry } from "./parse-record"

/**
 * Writes a reviewed plan into the cabinet through the ordinary API, as whoever is signed in (a secretary may: every
 * door used here is open to every clinic role).
 *
 * ⚠️ **Safe to run again.** Nothing is remembered in the browser; each step asks the cabinet what it already holds:
 * the patient (same name, same birth date), the « Ancien logiciel » folder, each file (same name and size), each
 * history line (same day, category, title and text). A closed tab, a lost connection or a second press of
 * « Importer » therefore continues where it stopped and creates nothing twice.
 *
 * ⚠️ **Paced.** The API allows each account 600 requests a minute, shared with that account's other tabs, and an
 * import is hundreds of requests. Every call here waits its turn (≤ 300 a minute), and a 429 pauses the whole run and
 * retries the same call — it is never counted as a failure.
 */

export const FOLDER_NAME = "Ancien logiciel"

export type RowState = "waiting" | "running" | "done" | "failed" | "skipped"

export interface RowResult {
  state: RowState
  patientId?: string
  created?: boolean
  sent: number
  already: number
  refused: number
  failedFiles: number
  /** Files not sent because the cabinet's storage is full — sent by a later run once there is room. */
  waitingForSpace: number
  historyAdded: number
  errors: string[]
  /** The cabinet seems to hold this person and refused a second fiche: the server's own sentence. */
  duplicate?: string
}

export const emptyResult = (): RowResult => ({
  state: "waiting",
  sent: 0,
  already: 0,
  refused: 0,
  failedFiles: 0,
  waitingForSpace: 0,
  historyAdded: 0,
  errors: [],
})

const RESUME = "Relancez l'import : il reprend où il s'est arrêté."

/** A refusal that will refuse every row the same way: stop rather than repeat it a hundred times. */
function isFatal(error: unknown): boolean {
  return error instanceof ApiError && [0, 401, 402, 403, 426, 429].includes(error.status)
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** One import's share of the account's request budget, and what it does when the server says « not now ». */
class Pacer {
  private nextSlot = 0
  private pausedUntil = 0

  constructor(
    private readonly signal: AbortSignal,
    private readonly onPause: (seconds: number) => void,
  ) {}

  /** ≤ 5 calls a second across the whole run (all three upload lanes included). */
  private async turn() {
    for (;;) {
      const now = Date.now()
      const wait = Math.max(this.pausedUntil, this.nextSlot) - now
      if (wait <= 0) {
        this.nextSlot = now + 200
        return
      }
      await sleep(Math.min(wait, 1000))
    }
  }

  /** Runs `fn` when its turn comes; on a 429, pauses everything and runs it again — up to ~10 minutes in all. */
  async call<T>(fn: () => Promise<T>): Promise<T> {
    for (let attempt = 0; ; attempt++) {
      await this.turn()
      try {
        return await fn()
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 429) || attempt >= 12 || this.signal.aborted) throw error
        const seconds = Math.min(60, 15 * (attempt + 1))
        this.pausedUntil = Math.max(this.pausedUntil, Date.now() + seconds * 1000)
        this.onPause(seconds)
      }
    }
  }
}

/** Set once the server answers « storage full »: no more files are sent this run, patients and history go on. */
interface Space {
  full: boolean
  message: string
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/**
 * ⚠️ `allowDuplicate` is true unless several patients of the cabinet carry this exact name. The page has already
 * looked for this person by name (and birth date) and found nobody, so what the server's guard can still catch is a
 * shared TELEPHONE — a mother and her daughter, a whole family on one number — which is not a duplicate here.
 */
function createBody(p: PlanPatient, allowDuplicate: boolean) {
  const r = p.record
  const notes = [`Importé de l'ancien logiciel (dossier ${p.numero ? `n° ${p.numero}` : p.key}).`]
  const keep = (label: string, value: string | undefined) => {
    if (value?.trim()) notes.push(`${label} : ${value.trim()}`)
  }

  const phone = toE164(p.phone, "TN") ? p.phone : null
  if (!phone) keep("Téléphone (ancien logiciel)", p.phone)
  const phone2 = r?.phone2 ? phoneOf(r.phone2) : ""
  const extraPhones = phone2 && toE164(phone2, "TN") ? [{ value: phone2, region: "TN" }] : []
  if (phone2 && !extraPhones.length) keep("Téléphone 2 (ancien logiciel)", phone2)
  const email = r?.email && EMAIL.test(r.email.trim()) ? r.email.trim() : null
  if (r?.email && !email) keep("E-mail (ancien logiciel)", r.email)
  const contactPhone = r?.otherContactPhone ? phoneOf(r.otherContactPhone) : ""
  const contactPhoneOk = !!contactPhone && !!toE164(contactPhone, "TN")
  if (contactPhone && !contactPhoneOk) keep("Téléphone de l'autre contact", contactPhone)
  keep("Assurance", r?.insurance)
  const address = r && (r.street || r.city || r.governorate)
    ? { street: r.street || undefined, city: r.city || undefined, state: r.governorate || undefined }
    : undefined

  return {
    firstName: p.firstName.trim(),
    lastName: p.lastName.trim(),
    dateOfBirth: p.birth || null,
    registeredOn: p.opened || undefined,
    gender: r ? genderOf(r.gender) : undefined,
    phoneNumber: phone,
    phoneRegion: "TN",
    additionalPhones: extraPhones.length ? extraPhones : undefined,
    email,
    address,
    medicalHistory: r?.antecedents && !/^(ras|néant|neant|aucun)\.?$/i.test(r.antecedents.trim()) ? r.antecedents : undefined,
    referredBy: r?.referredBy || undefined,
    emergencyContactName: r?.otherContactName || undefined,
    emergencyContactPhone: contactPhoneOk ? contactPhone : undefined,
    // Free text on the server; a server without them ignores the keys.
    occupation: r?.occupation || undefined,
    maritalStatus: r ? maritalStatusOf(r.maritalStatus) || undefined : undefined,
    countryOfOrigin: r?.countryOfOrigin || undefined,
    notes: notes.join("\n"),
    allowDuplicate: allowDuplicate || undefined,
  }
}

/** Server-side file names lose a few characters; compare on letters, digits and the size. */
const fileKey = (name: string, size: number) => `${name.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, "")}|${size}`

async function ensureFolder(pacer: Pacer, patientId: string): Promise<string> {
  const folders = await pacer.call(() => patientFilesApi.getFolders(patientId))
  const held = folders.find((f) => !f.parentFolderId && f.name.trim().toLowerCase() === FOLDER_NAME.toLowerCase())
  return held ? held.id : (await pacer.call(() => patientFilesApi.createFolder(patientId, FOLDER_NAME))).id
}

async function sendFile(patientId: string, folderId: string, f: PlanFile, policy: UploadPolicy | null, signal: AbortSignal) {
  const preview = await buildPreview(f.file).catch(() => null)
  const description = f.isRecord ? "Dossier de l'ancien logiciel" : "Importé de l'ancien logiciel"
  if (shouldUploadInParts(policy, f.file)) {
    await uploadInParts({ patientId, file: f.file, folderId, description, preview, signal })
  } else {
    await patientFilesApi.uploadFile(patientId, f.file, folderId, description, preview)
  }
}

interface LegacyHistoryDto {
  date: string | null
  category: string
  title: string
  details: string | null
}

const historyKey = (e: { date: string | null; category: string; title: string; details: string | null }) =>
  `${e.date ?? ""}|${e.category}|${e.title}|${e.details ?? ""}`

async function addHistory(pacer: Pacer, patientId: string, entries: LegacyHistoryEntry[]): Promise<number> {
  if (!entries.length) return 0
  const held = new Set(
    (await pacer.call(() => apiGet<LegacyHistoryDto[]>(`/patients/${patientId}/legacy-history`))).map(historyKey),
  )
  const missing = entries.filter((e) => !held.has(historyKey(e)))
  for (let i = 0; i < missing.length; i += 500) {
    const batch = missing.slice(i, i + 500)
    await pacer.call(() => apiPost(`/patients/${patientId}/legacy-history`, { entries: batch }))
  }
  return missing.length
}

export interface RunOptions {
  policy: UploadPolicy | null
  signal: AbortSignal
  onRow: (key: string, result: RowResult) => void
  /** The server asked to slow down: the run waits this many seconds, then carries on by itself. */
  onPause?: (seconds: number) => void
  /**
   * Patients an earlier run on this page already created or found. ⚠️ Needed for « Créer quand même »: that row
   * skips the name lookup on purpose, so without this a second run would create a third fiche.
   */
  known: Record<string, string | undefined>
}

/** Returns null when every row was attempted, or the sentence that stopped (or limited) the run. */
export async function runImport(
  plan: ImportPlan,
  { policy, signal, onRow, onPause = () => {}, known }: RunOptions,
): Promise<string | null> {
  const pacer = new Pacer(signal, onPause)
  const space: Space = { full: false, message: "" }
  const stoppedBy = (error: unknown) =>
    error instanceof ApiError && error.status === 429
      ? `Le serveur demande de ralentir depuis plusieurs minutes. ${RESUME}`
      : `${getErrorMessage(error)} ${RESUME}`

  const roots = plan.patients.filter((p) => !p.mergeInto)
  for (const root of roots) {
    if (signal.aborted) return `Import arrêté. ${RESUME}`
    const result = emptyResult()
    const publish = () => onRow(root.key, { ...result, errors: [...result.errors] })
    if (root.skip) {
      result.state = "skipped"
      publish()
      continue
    }
    result.state = "running"
    publish()

    try {
      // 1. The patient: the one the plan found, the one an earlier run created, or a new fiche.
      let patientId = known[root.key] ?? (root.createAnyway ? null : root.existing?.id ?? null)
      let ambiguous = root.existingAmbiguous
      if (!patientId && !root.createAnyway) {
        const found = await pacer.call(() => findExisting(root))
        patientId = found.existing?.id ?? null
        ambiguous = found.ambiguous
      }
      if (!patientId) {
        try {
          const body = createBody(root, root.createAnyway || !ambiguous)
          patientId = (await pacer.call(() => apiPost<PatientDto>("/patients", body))).id
          result.created = true
        } catch (error) {
          if (error instanceof ApiError && error.code === ApiErrorCode.PatientDuplicate) {
            result.duplicate = error.message
            result.state = "failed"
            publish()
            continue
          }
          throw error
        }
      }
      result.patientId = patientId
      publish()

      // 2. Its files, into « Ancien logiciel ».
      await sendFiles(pacer, space, patientId, filesFor(plan, root.key), result, publish, policy, signal)

      // 3. The read-only history, from this record and every record merged into it.
      const history = plan.patients.filter((p) => (p.mergeInto ?? p.key) === root.key && !p.skip).flatMap((p) => p.history)
      try {
        result.historyAdded = await addHistory(pacer, patientId, history)
      } catch (error) {
        if (isFatal(error)) throw error
        result.errors.push(
          error instanceof ApiError && error.status === 404
            ? "Historique non importé : ce serveur ne le prend pas encore en charge."
            : `Historique non importé : ${getErrorMessage(error)}`,
        )
      }

      result.state = result.errors.length ? "failed" : "done"
      publish()
    } catch (error) {
      result.errors.push(getErrorMessage(error))
      result.state = "failed"
      publish()
      if (isFatal(error)) return stoppedBy(error)
    }
  }

  // Folders of radios/photos for patients already in the cabinet — the « images only » pass, or a folder whose PDF
  // came in an earlier batch.
  const cabinetKeys = [...new Set(plan.folders.map((f) => f.choice).filter((k): k is string => !!k?.startsWith(CABINET_PREFIX)))]
  for (const key of cabinetKeys) {
    if (signal.aborted) return `Import arrêté. ${RESUME}`
    const result = emptyResult()
    const publish = () => onRow(key, { ...result, errors: [...result.errors] })
    result.state = "running"
    result.patientId = key.slice(CABINET_PREFIX.length)
    publish()
    try {
      await sendFiles(pacer, space, result.patientId, filesFor(plan, key), result, publish, policy, signal)
      result.state = result.errors.length ? "failed" : "done"
      publish()
    } catch (error) {
      result.errors.push(getErrorMessage(error))
      result.state = "failed"
      publish()
      if (isFatal(error)) return stoppedBy(error)
    }
  }

  if (space.full) {
    return `${space.message} Les patients et l'historique sont importés ; les fichiers restants attendent. Libérez de l'espace ou demandez-en davantage, puis ${RESUME.charAt(0).toLowerCase()}${RESUME.slice(1)}`
  }
  return null
}

/**
 * Into the patient's « Ancien logiciel », three at a time, skipping what is already there. Throws only a fatal error.
 * Once the cabinet's storage is full nothing more is sent: each remaining file is counted as waiting for space.
 */
async function sendFiles(
  pacer: Pacer,
  space: Space,
  patientId: string,
  files: PlanFile[],
  result: RowResult,
  publish: () => void,
  policy: UploadPolicy | null,
  signal: AbortSignal,
) {
  result.refused = files.filter((f) => f.refusal).length
  const sendable = files.filter((f) => !f.refusal)
  if (!sendable.length) return
  if (space.full) {
    result.waitingForSpace = sendable.length
    return
  }
  const folderId = await ensureFolder(pacer, patientId)
  const held = new Set(
    (await pacer.call(() => patientFilesApi.getFiles(patientId, folderId))).map((f) => fileKey(f.fileName, f.fileSize)),
  )
  const queue = sendable.filter((f) => {
    const known = held.has(fileKey(f.file.name, f.file.size))
    if (known) result.already++
    return !known
  })
  publish()
  let fatal: unknown = null
  const worker = async () => {
    while (queue.length && !signal.aborted && !fatal) {
      if (space.full) {
        result.waitingForSpace += queue.splice(0).length
        break
      }
      const f = queue.shift()!
      try {
        await pacer.call(() => sendFile(patientId, folderId, f, policy, signal))
        result.sent++
      } catch (error) {
        if (error instanceof ApiError && error.code === PhotoImportCode.StorageFull) {
          space.full = true
          space.message = error.message
          result.waitingForSpace++
        } else if (isFatal(error)) fatal = error
        else {
          result.failedFiles++
          result.errors.push(`${f.rel} : ${getErrorMessage(error)}`)
        }
      }
      publish()
    }
  }
  await Promise.all([worker(), worker(), worker()])
  publish()
  if (fatal) throw fatal
}

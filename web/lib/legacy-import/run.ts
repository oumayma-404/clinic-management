import { ApiError, ApiErrorCode, apiGet, apiPost } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
import { PhotoImportCode } from "@/lib/api/photo-import"
import { shouldUploadInParts, type UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"
import { buildPreview } from "@/lib/files/preview"
import { uploadInParts } from "@/lib/files/resumable-upload"
import { todayLocalIso } from "@/lib/format"
import { toE164 } from "@/lib/phone"
import { CABINET_PREFIX, filesFor, findExisting, type ImportPlan, type PlanFile, type PlanPatient } from "./plan"
import { genderOf, maritalStatusOf, nameKey, phoneOf, type LegacyHistoryEntry } from "./parse-record"

/**
 * Writes a reviewed plan into the cabinet through the ordinary API, as whoever is signed in (a secretary may: every
 * door used here is open to every clinic role).
 *
 * ⚠️ **Safe to run again.** Each step asks the cabinet what it already holds: the patient (same name, same birth
 * date), the « Ancien logiciel » folder, each file (same name and size), each history line (same day, category, title
 * and text). A closed tab, a lost connection or a second press of « Importer » continues where it stopped and creates
 * nothing twice. A row already finished on this page is not asked again.
 *
 * ⚠️ **Runs on its own.** It was measured on a cabinet's real connection, where one call in a few hundred is lost or
 * arrives too slowly: a lost call is retried after a pause (3 s → 60 s), and the run waits for the connection to come
 * back rather than stopping. A row gives up only when the problem is its own (the connection works for the others),
 * and the run stops only when nothing has answered for {@link LINK_GIVE_UP_MS}, when the server refuses every row the
 * same way (session, subscription, rights), or when « Arrêter » is pressed.
 *
 * ⚠️ **Several patients at once.** The server answers in milliseconds; the time is the connection's, one call after
 * another. {@link PATIENT_LANES} patients go through together — two records with the same name never at the same time,
 * so the second still finds the first. Every call shares one {@link Pacer}: the API allows 600 requests a minute per
 * address (a whole cabinet shares it), so by default the import takes half (300) and leaves the rest to the
 * colleagues' screens; a 429 halves the pace and pauses the whole run. Other cabinets are never affected: the limit is
 * per address.
 *
 * ⚠️ **A field the server refuses does not cost the patient.** The record is created again with the name and the
 * medical fields only, and what was refused (telephone, e-mail, date of creation) is kept in the notes.
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
  /** The server refused a field: the patient was created without it and it went to the notes — the refusal. */
  simplified?: string
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

/** Patients imported at the same time. */
const PATIENT_LANES = 6
/** Files sent at the same time, across every patient — a cabinet's upload is the narrow pipe. */
const UPLOAD_LANES = 3
/** Pauses before each new try of a call the connection lost, in seconds. */
const BACKOFF_S = [3, 8, 15, 30, 60]
/** A row gives up after this many lost tries while the connection works for the others. */
const ROW_ATTEMPTS = 6
/** Nothing has come back from the server for this long: the connection is down, the run stops. */
const LINK_GIVE_UP_MS = 30 * 60_000
/** Reading a file from the PC's disk: a file the browser cannot read is the file's problem, not the run's. */
const READ_TIMEOUT_MS = 60_000
/** Building a thumbnail is a courtesy; a file whose picture will not come is sent without one. */
const PREVIEW_TIMEOUT_MS = 15_000

/** A refusal that will refuse every row the same way: stop rather than repeat it a hundred times. */
function isFatal(error: unknown): boolean {
  return error instanceof ApiError && [401, 402, 403, 423, 426, 429].includes(error.status)
}

/**
 * Says nothing about the row: the call was lost on the way (0), arrived too slowly (408), met a restarting server
 * (502–504) or a fault (500), or is still being answered (`idempotency_in_progress`). Tried again after a pause.
 */
function isTransient(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    ([0, 408, 500, 502, 503, 504].includes(error.status) || error.code === ApiErrorCode.IdempotencyInProgress)
  )
}

/** The run cannot go on — its sentence is the stop message. */
class StopRun extends Error {}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** `promise`, or `fallback` once `ms` have passed — the promise keeps running and its late answer is ignored. */
function within<T>(promise: Promise<T>, ms: number, fallback: () => T): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      try {
        resolve(fallback())
      } catch (error) {
        reject(error)
      }
    }, ms)
    promise.then(
      (value) => {
        clearTimeout(timer)
        resolve(value)
      },
      (error) => {
        clearTimeout(timer)
        reject(error)
      },
    )
  })
}

/** One import's share of the request budget, what it does when the server says « not now », and when it last heard. */
class Pacer {
  private static readonly SLOWEST_MS = 1000
  /** The fastest this run may go: 80 % of a raised ceiling, else half the cabinet's 600 a minute. */
  private readonly fastestMs: number
  private gapMs: number
  private cleanCalls = 0
  private nextSlot = 0
  private pausedUntil = 0
  private lastAnswer = Date.now()
  private halted = false

  constructor(
    private readonly signal: AbortSignal,
    private readonly onPause: (seconds: number, reason: PauseReason) => void,
    permitPerMinute: number | null,
  ) {
    this.fastestMs = permitPerMinute ? Math.max(20, Math.ceil(60_000 / (permitPerMinute * 0.8))) : 200
    this.gapMs = this.fastestMs
  }

  /** One call per `gapMs` across the whole run (every lane included). */
  private async turn() {
    for (;;) {
      const now = Date.now()
      const wait = Math.max(this.pausedUntil, this.nextSlot) - now
      if (wait <= 0) {
        this.nextSlot = now + this.gapMs
        return
      }
      await sleep(Math.min(wait, 1000))
    }
  }

  /** Another lane stopped the run: a row still waiting on a lost call gives up instead of trying again. */
  halt() {
    this.halted = true
  }

  /** How long the server has been silent — any answer, a refusal included, means the connection works. */
  get silentFor(): number {
    return Date.now() - this.lastAnswer
  }

  /** Runs `fn` when its turn comes; on a 429, slows down, pauses everything and runs it again (~10 min at most). */
  async call<T>(fn: () => Promise<T>): Promise<T> {
    for (let attempt = 0; ; attempt++) {
      await this.turn()
      try {
        const value = await fn()
        this.lastAnswer = Date.now()
        // A long clean run earns the pace back: after 100 calls without a refusal, twice as fast.
        if (++this.cleanCalls >= 100 && this.gapMs > this.fastestMs) {
          this.gapMs = Math.max(this.fastestMs, Math.floor(this.gapMs / 2))
          this.cleanCalls = 0
        }
        return value
      } catch (error) {
        if (!(error instanceof ApiError) || error.status !== 0) this.lastAnswer = Date.now()
        if (!(error instanceof ApiError && error.status === 429) || attempt >= 12 || this.signal.aborted) throw error
        this.cleanCalls = 0
        this.gapMs = Math.min(Pacer.SLOWEST_MS, Math.max(200, this.gapMs * 2))
        const seconds = Math.min(60, 15 * (attempt + 1))
        this.pausedUntil = Math.max(this.pausedUntil, Date.now() + seconds * 1000)
        this.onPause(seconds, "slow-down")
      }
    }
  }

  /**
   * Runs `step` until it goes through. `step` receives the attempt number and must be safe to run again: from the
   * second attempt on, it asks the cabinet what the lost call may already have written. A lost call is retried after a
   * pause; the row gives up after {@link ROW_ATTEMPTS} while the server still answers the other lanes, and the whole run
   * stops once nothing has answered for {@link LINK_GIVE_UP_MS}.
   */
  async persist<T>(step: (attempt: number) => Promise<T>): Promise<T> {
    for (let attempt = 0; ; attempt++) {
      try {
        return await step(attempt)
      } catch (error) {
        if (!isTransient(error) || this.signal.aborted || this.halted) throw error
        if (this.silentFor > LINK_GIVE_UP_MS) {
          throw new StopRun(`Le serveur ne répond plus depuis ${Math.round(LINK_GIVE_UP_MS / 60_000)} minutes : vérifiez la connexion internet de ce PC. ${RESUME}`)
        }
        if (attempt + 1 >= ROW_ATTEMPTS && this.silentFor < 60_000) throw error
        const seconds = BACKOFF_S[Math.min(attempt, BACKOFF_S.length - 1)]
        this.onPause(seconds, "connection")
        await sleep(seconds * 1000)
      }
    }
  }
}

export type PauseReason = "slow-down" | "connection"

/** At most `size` of these at once. */
class Gate {
  private active = 0
  private readonly waiting: Array<() => void> = []
  constructor(private readonly size: number) {}

  async run<T>(fn: () => Promise<T>): Promise<T> {
    if (this.active >= this.size) await new Promise<void>((resolve) => this.waiting.push(resolve))
    this.active++
    try {
      return await fn()
    } finally {
      this.active--
      this.waiting.shift()?.()
    }
  }
}

/** Set once the server answers « storage full »: no more files are sent this run, patients and history go on. */
interface Space {
  full: boolean
  message: string
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** « 07/03/2015 » from « 2015-03-07 ». */
const frDate = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`

/**
 * ⚠️ `allowDuplicate` is true unless several patients of the cabinet carry this exact name. The page has already
 * looked for this person by name (and birth date) and found nobody, so what the server's guard can still catch is a
 * shared TELEPHONE — a mother and her daughter, a whole family on one number — which is not a duplicate here.
 *
 * ⚠️ `bare` is the second attempt after the server refused a field: the identity, the address and the medical
 * fields only; every contact detail and the date of creation go to the notes, so nothing of the record is lost.
 */
function createBody(p: PlanPatient, allowDuplicate: boolean, bare = false) {
  const r = p.record
  const notes = [`Importé de l'ancien logiciel (dossier ${p.numero ? `n° ${p.numero}` : p.key}).`]
  const keep = (label: string, value: string | undefined) => {
    if (value?.trim()) notes.push(`${label} : ${value.trim()}`)
  }

  const phone = !bare && toE164(p.phone, "TN") ? p.phone : null
  if (!phone) keep("Téléphone (ancien logiciel)", p.phone)
  const phone2 = r?.phone2 ? phoneOf(r.phone2) : ""
  const extraPhones = !bare && phone2 && toE164(phone2, "TN") ? [{ value: phone2, region: "TN" }] : []
  if (phone2 && !extraPhones.length) keep("Téléphone 2 (ancien logiciel)", phone2)
  const email = !bare && r?.email && EMAIL.test(r.email.trim()) ? r.email.trim() : null
  if (r?.email && !email) keep("E-mail (ancien logiciel)", r.email)
  const contactPhone = r?.otherContactPhone ? phoneOf(r.otherContactPhone) : ""
  const contactPhoneOk = !bare && !!contactPhone && !!toE164(contactPhone, "TN")
  if (contactPhone && !contactPhoneOk) keep("Téléphone de l'autre contact", contactPhone)
  keep("Assurance", r?.insurance)
  const birthOk = !!p.birth && p.birth >= "1900-01-01" && p.birth <= todayLocalIso()
  const birth = bare && !birthOk ? null : p.birth || null
  if (bare && p.birth && !birthOk) keep("Date de naissance (ancien logiciel)", frDate(p.birth))
  if (bare && p.opened) keep("Dossier créé le (ancien logiciel)", frDate(p.opened))
  const address = r && (r.street || r.city || r.governorate)
    ? { street: r.street || undefined, city: r.city || undefined, state: r.governorate || undefined }
    : undefined

  return {
    firstName: p.firstName.trim(),
    lastName: p.lastName.trim(),
    dateOfBirth: birth,
    registeredOn: bare ? undefined : p.opened || undefined,
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

/** A refusal about one field of the record (a 400 or 422 that is not the duplicate question): worth a bare retry. */
function isFieldRefusal(error: unknown): error is ApiError {
  return (
    error instanceof ApiError &&
    (error.status === 400 || error.status === 422) &&
    error.code !== ApiErrorCode.PatientDuplicate
  )
}

/** Server-side file names lose a few characters; compare on letters, digits and the size. */
const fileKey = (name: string, size: number) => `${name.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, "")}|${size}`

/** The patient's « Ancien logiciel » folder — created directly for a patient this run just created. */
async function ensureFolder(pacer: Pacer, patientId: string, fresh: boolean): Promise<string> {
  return pacer.persist(async (attempt) => {
    if (!fresh || attempt > 0) {
      const folders = await pacer.call(() => patientFilesApi.getFolders(patientId))
      const held = folders.find((f) => !f.parentFolderId && f.name.trim().toLowerCase() === FOLDER_NAME.toLowerCase())
      if (held) return held.id
    }
    return (await pacer.call(() => patientFilesApi.createFolder(patientId, FOLDER_NAME))).id
  })
}

/** One read; the caller decides whether it is tried again. */
async function heldFileKeys(pacer: Pacer, patientId: string, folderId: string): Promise<Set<string>> {
  const files = await pacer.call(() => patientFilesApi.getFiles(patientId, folderId))
  return new Set(files.map((f) => fileKey(f.fileName, f.fileSize)))
}

/** The file's bytes, read once from the PC's disk — so a lost upload is sent again from memory, never re-read. */
async function readFromDisk(file: File): Promise<File> {
  const bytes = await within(file.arrayBuffer(), READ_TIMEOUT_MS, () => {
    throw new Error("timeout")
  }).catch(() => {
    throw new Error("Le fichier n'a pas pu être lu sur ce PC (il a peut-être été déplacé, renommé ou ouvert ailleurs).")
  })
  return new File([bytes], file.name, { type: file.type, lastModified: file.lastModified })
}

/** Sends one file; from the second try on, first asks whether the lost try had in fact arrived. */
async function sendFile(
  pacer: Pacer,
  patientId: string,
  folderId: string,
  f: PlanFile,
  policy: UploadPolicy | null,
  signal: AbortSignal,
): Promise<"sent" | "already"> {
  const file = await readFromDisk(f.file)
  const preview = await within(buildPreview(file).catch(() => null), PREVIEW_TIMEOUT_MS, () => null)
  const description = f.isRecord ? "Dossier de l'ancien logiciel" : "Importé de l'ancien logiciel"
  return pacer.persist(async (attempt) => {
    if (attempt > 0 && (await heldFileKeys(pacer, patientId, folderId)).has(fileKey(f.file.name, f.file.size))) {
      return "already"
    }
    await pacer.call(() =>
      shouldUploadInParts(policy, file)
        ? uploadInParts({ patientId, file, folderId, description, preview, signal })
        : patientFilesApi.uploadFile(patientId, file, folderId, description, preview),
    )
    return "sent"
  })
}

interface LegacyHistoryDto {
  date: string | null
  category: string
  title: string
  details: string | null
}

const historyKey = (e: { date: string | null; category: string; title: string; details: string | null }) =>
  `${e.date ?? ""}|${e.category}|${e.title}|${e.details ?? ""}`

async function addHistory(pacer: Pacer, patientId: string, entries: LegacyHistoryEntry[], fresh: boolean): Promise<number> {
  if (!entries.length) return 0
  let added = 0
  await pacer.persist(async (attempt) => {
    // A fresh patient holds no history; after a lost try, ask what arrived.
    const held = fresh && attempt === 0
      ? new Set<string>()
      : new Set((await pacer.call(() => apiGet<LegacyHistoryDto[]>(`/patients/${patientId}/legacy-history`))).map(historyKey))
    const missing = entries.filter((e) => !held.has(historyKey(e)))
    for (let i = 0; i < missing.length; i += 500) {
      const batch = missing.slice(i, i + 500)
      await pacer.call(() => apiPost(`/patients/${patientId}/legacy-history`, { entries: batch }))
    }
    added = missing.length
  })
  return added
}

export interface RunOptions {
  policy: UploadPolicy | null
  signal: AbortSignal
  onRow: (key: string, result: RowResult) => void
  /** The run waits this many seconds, then carries on by itself: the server asked to slow down, or a call was lost. */
  onPause?: (seconds: number, reason: PauseReason) => void
  /**
   * Patients an earlier run on this page already created or found. ⚠️ Needed for « Créer quand même »: that row
   * skips the name lookup on purpose, so without this a second run would create a third fiche.
   */
  known: Record<string, string | undefined>
  /** Rows an earlier run on this page finished: not asked again. */
  done?: ReadonlySet<string>
}

/** Returns null when every row was attempted, or the sentence that stopped (or limited) the run. */
export async function runImport(
  plan: ImportPlan,
  { policy, signal, onRow, onPause = () => {}, known, done = new Set() }: RunOptions,
): Promise<string | null> {
  // Only a raise the server confirms for THIS account lets the run go faster than the cabinet's shared half.
  const boost = await apiGet<{ permitPerMinute: number | null }>("/meta/rate-boost").catch(() => null)
  const pacer = new Pacer(signal, onPause, boost?.permitPerMinute ?? null)
  const uploads = new Gate(UPLOAD_LANES)
  const space: Space = { full: false, message: "" }
  let stop: string | null = null
  const stoppedBy = (error: unknown): string =>
    error instanceof StopRun
      ? error.message
      : error instanceof ApiError && error.status === 429
        ? `Le serveur demande de ralentir depuis plusieurs minutes. ${RESUME}`
        : `${getErrorMessage(error)} ${RESUME}`
  const halted = () => signal.aborted || stop !== null

  /** One patient: the fiche, its files, its history. Throws only what must stop the run. */
  const importPatient = async (root: PlanPatient) => {
    const result = emptyResult()
    const publish = () => onRow(root.key, { ...result, errors: [...result.errors] })
    if (root.skip) {
      result.state = "skipped"
      publish()
      return
    }
    result.state = "running"
    publish()

    try {
      // 1. The patient: the one the plan found, the one an earlier run created, or a new fiche.
      let patientId = known[root.key] ?? (root.createAnyway ? null : root.existing?.id ?? null)
      let fresh = false
      if (!patientId) {
        const outcome = await pacer.persist(async (attempt) => {
          // Asked again on every try: a lost « create » may have arrived, and must be found rather than made twice.
          let ambiguous = root.existingAmbiguous
          if (!root.createAnyway || attempt > 0) {
            const found = await pacer.call(() => findExisting(root))
            if (found.existing && !root.createAnyway) return { id: found.existing.id, created: false }
            ambiguous = found.ambiguous
          }
          const allowDuplicate = root.createAnyway || !ambiguous
          try {
            return { id: (await pacer.call(() => apiPost<PatientDto>("/patients", createBody(root, allowDuplicate)))).id, created: true }
          } catch (error) {
            if (!isFieldRefusal(error)) throw error
            const id = (await pacer.call(() => apiPost<PatientDto>("/patients", createBody(root, allowDuplicate, true)))).id
            return { id, created: true, simplified: error.message }
          }
        }).catch((error: unknown) => {
          if (error instanceof ApiError && error.code === ApiErrorCode.PatientDuplicate) return { duplicate: error.message }
          throw error
        })
        if ("duplicate" in outcome) {
          result.duplicate = outcome.duplicate
          result.state = "failed"
          publish()
          return
        }
        patientId = outcome.id
        fresh = outcome.created
        result.created = outcome.created
        if ("simplified" in outcome) result.simplified = outcome.simplified
      }
      result.patientId = patientId
      publish()

      // 2. Its files, into « Ancien logiciel ».
      await sendFiles(pacer, uploads, space, patientId, filesFor(plan, root.key), result, publish, policy, signal, fresh)

      // 3. The read-only history, from this record and every record merged into it.
      const history = plan.patients.filter((p) => (p.mergeInto ?? p.key) === root.key && !p.skip).flatMap((p) => p.history)
      try {
        result.historyAdded = await addHistory(pacer, patientId, history, fresh)
      } catch (error) {
        if (isFatal(error) || error instanceof StopRun) throw error
        result.errors.push(
          error instanceof ApiError && error.status === 404
            ? "Historique non importé : ce serveur ne le prend pas encore en charge."
            : `Historique non importé : ${getErrorMessage(error)}`,
        )
      }

      result.state = result.errors.length ? "failed" : "done"
      publish()
    } catch (error) {
      result.errors.push(error instanceof StopRun ? error.message : getErrorMessage(error))
      result.state = "failed"
      publish()
      if (isFatal(error) || error instanceof StopRun) throw error
    }
  }

  /** Radios/photos for a patient already in the cabinet — the « images only » pass. */
  const importCabinetFolder = async (key: string) => {
    const result = emptyResult()
    const publish = () => onRow(key, { ...result, errors: [...result.errors] })
    result.state = "running"
    result.patientId = key.slice(CABINET_PREFIX.length)
    publish()
    try {
      await sendFiles(pacer, uploads, space, result.patientId, filesFor(plan, key), result, publish, policy, signal, false)
      result.state = result.errors.length ? "failed" : "done"
      publish()
    } catch (error) {
      result.errors.push(error instanceof StopRun ? error.message : getErrorMessage(error))
      result.state = "failed"
      publish()
      if (isFatal(error) || error instanceof StopRun) throw error
    }
  }

  /** Several lanes over one queue; the first stop ends them all after their current row. */
  const lanes = async <T>(items: T[], work: (item: T) => Promise<void>) => {
    const queue = [...items]
    const lane = async () => {
      while (queue.length && !halted()) {
        const item = queue.shift()!
        try {
          await work(item)
        } catch (error) {
          stop ??= stoppedBy(error)
          pacer.halt()
        }
      }
    }
    await Promise.all(Array.from({ length: Math.min(PATIENT_LANES, queue.length) }, lane))
  }

  // Two records with the same name go through one after the other, in one lane: the second must find the first.
  const roots = plan.patients.filter((p) => !p.mergeInto && !done.has(p.key))
  const groups = new Map<string, PlanPatient[]>()
  for (const root of roots) {
    const key = nameKey(`${root.firstName} ${root.lastName}`) || root.key
    groups.set(key, [...(groups.get(key) ?? []), root])
  }
  await lanes([...groups.values()], async (group) => {
    for (const root of group) {
      if (halted()) return
      await importPatient(root)
    }
  })

  // Folders of radios/photos for patients already in the cabinet — the « images only » pass, or a folder whose PDF
  // came in an earlier batch.
  if (!halted()) {
    const cabinetKeys = [...new Set(plan.folders.map((f) => f.choice).filter((k): k is string => !!k?.startsWith(CABINET_PREFIX)))]
    await lanes(cabinetKeys.filter((k) => !done.has(k)), importCabinetFolder)
  }

  if (stop) return stop
  if (signal.aborted) return `Import arrêté. ${RESUME}`
  if (space.full) {
    // The server's sentence already says how to free space; ours says what happened to the rest of the import.
    return `${space.message} Les patients et l'historique sont importés ; les fichiers restants attendent. Une fois de la place libérée, ${RESUME.charAt(0).toLowerCase()}${RESUME.slice(1)}`
  }
  return null
}

/**
 * Into the patient's « Ancien logiciel », skipping what is already there; {@link UPLOAD_LANES} files at a time across
 * the whole run. Throws only what must stop the run. Once the cabinet's storage is full nothing more is sent: each
 * remaining file is counted as waiting for space. A file that cannot be sent is counted and named; the others go on.
 */
async function sendFiles(
  pacer: Pacer,
  uploads: Gate,
  space: Space,
  patientId: string,
  files: PlanFile[],
  result: RowResult,
  publish: () => void,
  policy: UploadPolicy | null,
  signal: AbortSignal,
  fresh: boolean,
) {
  result.refused = files.filter((f) => f.refusal).length
  const sendable = files.filter((f) => !f.refusal)
  if (!sendable.length) return
  if (space.full) {
    result.waitingForSpace = sendable.length
    return
  }
  const folderId = await ensureFolder(pacer, patientId, fresh)
  // A folder this run just created holds nothing yet.
  const held = fresh ? new Set<string>() : await pacer.persist(() => heldFileKeys(pacer, patientId, folderId))
  const queue = sendable.filter((f) => {
    const known = held.has(fileKey(f.file.name, f.file.size))
    if (known) result.already++
    return !known
  })
  publish()
  let fatal: unknown = null
  await Promise.all(
    queue.map((f) =>
      uploads.run(async () => {
        if (signal.aborted || fatal) return
        if (space.full) {
          result.waitingForSpace++
          return
        }
        try {
          const outcome = await sendFile(pacer, patientId, folderId, f, policy, signal)
          if (outcome === "sent") result.sent++
          else result.already++
        } catch (error) {
          if (error instanceof ApiError && error.code === PhotoImportCode.StorageFull) {
            space.full = true
            space.message = error.message
            result.waitingForSpace++
          } else if (isFatal(error) || error instanceof StopRun) fatal = error
          else {
            result.failedFiles++
            result.errors.push(`${f.rel} : ${getErrorMessage(error)}`)
          }
        }
        publish()
      }),
    ),
  )
  publish()
  if (fatal) throw fatal
}

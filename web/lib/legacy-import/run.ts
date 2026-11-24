import { ApiError, ApiErrorCode, apiGet, apiPost } from "@/lib/api/client"
import { patientFilesApi } from "@/lib/api/patient-files"
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
  historyAdded: 0,
  errors: [],
})

/** A refusal that will refuse every row the same way: stop rather than repeat it a hundred times. */
function isFatal(error: unknown): boolean {
  return error instanceof ApiError && [0, 401, 402, 403, 426].includes(error.status)
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function createBody(p: PlanPatient) {
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
    allowDuplicate: p.createAnyway || undefined,
  }
}

/** Server-side file names lose a few characters; compare on letters, digits and the size. */
const fileKey = (name: string, size: number) => `${name.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, "")}|${size}`

async function ensureFolder(patientId: string): Promise<string> {
  const folders = await patientFilesApi.getFolders(patientId)
  const held = folders.find((f) => !f.parentFolderId && f.name.trim().toLowerCase() === FOLDER_NAME.toLowerCase())
  return held ? held.id : (await patientFilesApi.createFolder(patientId, FOLDER_NAME)).id
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

async function addHistory(patientId: string, entries: LegacyHistoryEntry[]): Promise<number> {
  if (!entries.length) return 0
  const held = new Set((await apiGet<LegacyHistoryDto[]>(`/patients/${patientId}/legacy-history`)).map(historyKey))
  const missing = entries.filter((e) => !held.has(historyKey(e)))
  for (let i = 0; i < missing.length; i += 500) {
    await apiPost(`/patients/${patientId}/legacy-history`, { entries: missing.slice(i, i + 500) })
  }
  return missing.length
}

export interface RunOptions {
  policy: UploadPolicy | null
  signal: AbortSignal
  onRow: (key: string, result: RowResult) => void
  /**
   * Patients an earlier run on this page already created or found. ⚠️ Needed for « Créer quand même »: that row
   * skips the name lookup on purpose, so without this a second run would create a third fiche.
   */
  known: Record<string, string | undefined>
}

/** Returns null when every row was attempted, or the sentence that stopped the run. */
export async function runImport(plan: ImportPlan, { policy, signal, onRow, known }: RunOptions): Promise<string | null> {
  const roots = plan.patients.filter((p) => !p.mergeInto)
  for (const root of roots) {
    if (signal.aborted) return "Import arrêté. Relancez-le : il reprend où il s'est arrêté."
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
      if (!patientId && !root.createAnyway) patientId = (await findExisting(root)).existing?.id ?? null
      if (!patientId) {
        try {
          patientId = (await apiPost<PatientDto>("/patients", createBody(root))).id
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
      await sendFiles(patientId, filesFor(plan, root.key), result, publish, policy, signal)

      // 3. The read-only history, from this record and every record merged into it.
      const history = plan.patients.filter((p) => (p.mergeInto ?? p.key) === root.key && !p.skip).flatMap((p) => p.history)
      try {
        result.historyAdded = await addHistory(patientId, history)
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
      if (isFatal(error)) return `${getErrorMessage(error)} Relancez l'import : il reprend où il s'est arrêté.`
    }
  }

  // Folders of radios/photos for patients already in the cabinet — the « images only » pass, or a folder whose PDF
  // came in an earlier batch.
  const cabinetKeys = [...new Set(plan.folders.map((f) => f.choice).filter((k): k is string => !!k?.startsWith(CABINET_PREFIX)))]
  for (const key of cabinetKeys) {
    if (signal.aborted) return "Import arrêté. Relancez-le : il reprend où il s'est arrêté."
    const result = emptyResult()
    const publish = () => onRow(key, { ...result, errors: [...result.errors] })
    result.state = "running"
    result.patientId = key.slice(CABINET_PREFIX.length)
    publish()
    try {
      await sendFiles(result.patientId, filesFor(plan, key), result, publish, policy, signal)
      result.state = result.errors.length ? "failed" : "done"
      publish()
    } catch (error) {
      result.errors.push(getErrorMessage(error))
      result.state = "failed"
      publish()
      if (isFatal(error)) return `${getErrorMessage(error)} Relancez l'import : il reprend où il s'est arrêté.`
    }
  }
  return null
}

/** Into the patient's « Ancien logiciel », three at a time, skipping what is already there. Throws only a fatal error. */
async function sendFiles(
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
  const folderId = await ensureFolder(patientId)
  const held = new Set((await patientFilesApi.getFiles(patientId, folderId)).map((f) => fileKey(f.fileName, f.fileSize)))
  const queue = sendable.filter((f) => {
    const known = held.has(fileKey(f.file.name, f.file.size))
    if (known) result.already++
    return !known
  })
  publish()
  let fatal: unknown = null
  const worker = async () => {
    while (queue.length && !signal.aborted && !fatal) {
      const f = queue.shift()!
      try {
        await sendFile(patientId, folderId, f, policy, signal)
        result.sent++
      } catch (error) {
        if (isFatal(error)) fatal = error
        else {
          result.failedFiles++
          result.errors.push(`${f.rel} : ${getErrorMessage(error)}`)
        }
      }
      publish()
    }
  }
  await Promise.all([worker(), worker(), worker()])
  if (fatal) throw fatal
}

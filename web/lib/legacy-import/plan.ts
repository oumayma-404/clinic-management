import { patientsApi } from "@/lib/api/patients"
import type { PatientDto } from "@/lib/api/types"
import { extensionOf, refusalFor, type UploadPolicy } from "@/lib/api/upload-policy"
import { quoteFr, todayLocalIso } from "@/lib/format"
import { toE164 } from "@/lib/phone"
import { historyOf } from "./history"
import { matchFolder, type MatchTarget, type MatchVerdict } from "./match"
import {
  isoDate,
  leadingNumber,
  nameKey,
  parseRecord,
  phoneOf,
  splitFileName,
  titleCase,
  type LegacyHistoryEntry,
  type LegacyRecord,
} from "./parse-record"
import { pdfRows } from "./pdf-rows"

/**
 * The dry run: what the import WOULD do, built from the two folders the user picked, before anything is written.
 * Everything a person may correct is on the plan (names, dates, phone, which folder goes to whom, skip, create
 * anyway); the run reads only the plan.
 */

export interface PlanFile {
  id: string
  file: File
  /** Path under the picked folder, for display. */
  rel: string
  isRecord: boolean
  /** The server's own refusal sentence, or null when it can be sent. */
  refusal: string | null
}

export interface ExistingPatient {
  id: string
  label: string
}

export interface PlanPatient {
  /** The dossier as exported: « 8-Prénom Nom ». Unique within a plan. */
  key: string
  numero: string
  identitySource: "pdf" | "fileName"
  // Editable on the plan.
  lastName: string
  firstName: string
  /** `yyyy-MM-dd` or empty. */
  birth: string
  phone: string
  /** `yyyy-MM-dd` — the old software's « Dossier créé le », sent as the registration date. */
  opened: string
  record: LegacyRecord | null
  history: LegacyHistoryEntry[]
  recordFiles: PlanFile[]
  /** Facts found while reading, fixed for this plan. Live checks of the editable fields are `warningsOf`. */
  remarks: string[]
  /** Key of the first record of the same person in this export; its files and history go there. */
  mergeInto: string | null
  /** The cabinet already holds this person: files and history are added to that file, nothing is overwritten. */
  existing: ExistingPatient | null
  /** Several patients of the cabinet carry this name; the server will ask. */
  existingAmbiguous: boolean
  skip: boolean
  /** « Créer quand même » — a new fiche although the cabinet seems to hold this person. */
  createAnyway: boolean
}

export interface AttachmentFolder {
  name: string
  files: PlanFile[]
  bytes: number
  verdict: MatchVerdict
  /**
   * Where it goes: a plan patient's key, or `cab:<id>` for a patient already in the cabinet; null = not imported;
   * undefined = somebody must choose.
   */
  choice: string | null | undefined
}

/** A patient already in the cabinet — what a folder of radios can go to when no PDF of this batch names them. */
export interface CabinetPatient {
  /** `cab:<id>` — never collides with a dossier key. */
  key: string
  id: string
  label: string
  /** The old software's dossier number, from the note an earlier import wrote: « 8 » for « n° 8/2022 ». */
  number: string | null
}

export interface ImportPlan {
  patients: PlanPatient[]
  folders: AttachmentFolder[]
  /** The cabinet's patients that a folder may go to (every patient, minus those a PDF of this batch already is). */
  cabinet: CabinetPatient[]
  /** Files sitting directly in the attachments folder, outside any patient folder: never imported. */
  looseFiles: string[]
  /** The cabinet's storage ceiling, or null where none is enforced. */
  quota: { total: number; used: number } | null
}

export const CABINET_PREFIX = "cab:"

/** How a destination reads on screen, whichever kind it is. */
export function destinationLabel(plan: ImportPlan, key: string): string {
  const p = plan.patients.find((x) => x.key === key)
  if (p) return `${p.lastName} ${p.firstName} — dossier ${p.key}`
  return plan.cabinet.find((c) => c.key === key)?.label ?? key
}

const isSystemFile = (n: string) => /^(thumbs\.db|desktop\.ini|\.ds_store)$/i.test(n) || /^(~\$|\._)/.test(n)
const pathOf = (f: File) => ((f as File & { webkitRelativePath?: string }).webkitRelativePath || f.name).split("/")

let nextId = 0
function planFile(policy: UploadPolicy | null, file: File, rel: string, isRecord = false): PlanFile {
  let refusal: string | null = null
  if (file.size === 0) refusal = "Fichier vide."
  else if (policy) refusal = refusalFor(policy, file)
  else if (!extensionOf(file.name)) refusal = "Fichier sans extension : le logiciel ne sait pas ce que c'est."
  return { id: `f${++nextId}`, file, rel, isRecord, refusal }
}

/** The live checks of what a person can still correct on the plan. */
export function warningsOf(p: PlanPatient): string[] {
  const out: string[] = []
  if (p.lastName.replace(/[^\p{L}]/gu, "").length < 2) out.push("Nom trop court")
  if (p.firstName.replace(/[^\p{L}]/gu, "").length < 2) out.push("Prénom trop court")
  if (p.birth) {
    if (p.birth < "1900-01-01" || p.birth > todayLocalIso()) out.push("Date de naissance improbable")
    else if (p.opened && p.opened < p.birth) out.push("Dossier créé avant la naissance : la date de création ira dans les notes")
    else if (p.opened && Number(p.opened.slice(0, 4)) - Number(p.birth.slice(0, 4)) < 2) {
      out.push("Moins de 2 ans à l'ouverture du dossier")
    }
  }
  if (p.phone && !toE164(p.phone, "TN")) out.push("Téléphone illisible : il ira dans les notes")
  return out
}

/** « Mohamed Ben Salah · né(e) le 03/02/1980 ». */
function patientLabel(c: PatientDto): string {
  const d = c.dateOfBirth
  return `${c.firstName} ${c.lastName}${d ? ` · né(e) le ${d.slice(8, 10)}/${d.slice(5, 7)}/${d.slice(0, 4)}` : ""}`
}

type Identity = Pick<PlanPatient, "lastName" | "firstName" | "birth">

/** Exact name (words in any order, accents off) and, when both are known, the same birth date. */
function sameInCabinet(p: Identity, cabinet: PatientDto[]): { existing: ExistingPatient | null; ambiguous: boolean } {
  const wanted = nameKey(`${p.firstName} ${p.lastName}`)
  if (!wanted) return { existing: null, ambiguous: false }
  const same = cabinet.filter((c) => {
    if (nameKey(`${c.firstName} ${c.lastName}`) !== wanted) return false
    const dob = c.dateOfBirth?.slice(0, 10)
    return !(p.birth && dob && dob !== p.birth)
  })
  if (same.length !== 1) return { existing: null, ambiguous: same.length > 1 }
  return { existing: { id: same[0].id, label: patientLabel(same[0]) }, ambiguous: false }
}

/** The same question asked of the server at import time — a patient created since the plan was built is found. */
export async function findExisting(p: Identity): Promise<{ existing: ExistingPatient | null; ambiguous: boolean }> {
  return sameInCabinet(p, await patientsApi.list({ searchTerm: p.lastName, limit: 100 }))
}

export interface PlanProgress {
  done: number
  total: number
}

/** Record PDFs read at the same time while the plan is built. */
const PDF_READERS = 4

export async function buildPlan(
  recordFiles: File[],
  attachmentFiles: File[],
  policy: UploadPolicy | null,
  onProgress: (p: PlanProgress) => void,
): Promise<ImportPlan> {
  // 1. One unit per patient: her export puts the record PDFs side by side; a folder per patient is read too.
  const records = recordFiles.filter((f) => !isSystemFile(f.name))
  const flat = records.filter((f) => pathOf(f).length <= 2 && /\.pdf$/i.test(f.name))
  const units: Array<{ key: string; record: File | null; others: File[] }> = []
  if (flat.length) {
    for (const f of flat) units.push({ key: f.name.replace(/\.pdf$/i, ""), record: f, others: [] })
  } else {
    const byFolder = new Map<string, File[]>()
    for (const f of records) {
      const parts = pathOf(f)
      if (parts.length < 3) continue
      byFolder.set(parts[1], [...(byFolder.get(parts[1]) ?? []), f])
    }
    for (const [key, files] of byFolder) {
      const pdfs = files.filter((f) => /\.pdf$/i.test(f.name))
      const record = pdfs.find((f) => f.name.replace(/\.pdf$/i, "").toLowerCase() === key.toLowerCase()) ?? pdfs[0] ?? null
      units.push({ key, record, others: files.filter((f) => f !== record) })
    }
  }
  units.sort((a, b) => (Number(leadingNumber(a.key)) || Infinity) - (Number(leadingNumber(b.key)) || Infinity) || a.key.localeCompare(b.key))

  // 2. Read each record PDF — several at once, since each waits on the disk and on pdf.js' worker in turn.
  const read: Array<{ record: LegacyRecord | null; history: LegacyHistoryEntry[]; remarks: string[] }> = new Array(units.length)
  let done = 0
  let next = 0
  const total = units.length
  const reader = async () => {
    while (next < units.length) {
      const i = next++
      const u = units[i]
      const remarks: string[] = []
      let record: LegacyRecord | null = null
      let history: LegacyHistoryEntry[] = []
      if (u.record) {
        try {
          const { rows, textChars } = await pdfRows(u.record)
          if (textChars === 0) remarks.push("PDF scanné (une image) : le nom vient du nom du fichier")
          else {
            record = parseRecord(rows)
            history = historyOf(rows, record)
          }
        } catch {
          remarks.push("PDF illisible : le nom vient du nom du fichier")
        }
      } else remarks.push("Aucun PDF de dossier")
      read[i] = { record, history, remarks }
      onProgress({ done: ++done, total })
    }
  }
  await Promise.all(Array.from({ length: Math.min(PDF_READERS, units.length) }, reader))

  const patients: PlanPatient[] = []
  for (const [i, u] of units.entries()) {
    const { record, history, remarks } = read[i]

    const fromPdf = !!(record?.lastName && record.firstName)
    const guess = splitFileName(u.key)
    if (record?.birth && !isoDate(record.birth)) remarks.push(`Date de naissance illisible : ${record.birth}`)
    if (record && !record.opened) {
      remarks.push(record.openedRaw ? `Date de création illisible : ${record.openedRaw}` : "Pas de date de création : ce sera le jour de l'import")
    }
    if (record?.unknownSections.length) remarks.push(`Section non lue : ${record.unknownSections.join(", ")}`)

    patients.push({
      key: u.key,
      numero: record?.numero || (leadingNumber(u.key) ?? ""),
      identitySource: fromPdf ? "pdf" : "fileName",
      lastName: titleCase(fromPdf ? record!.lastName : guess.lastName),
      firstName: titleCase(fromPdf ? record!.firstName : guess.firstName),
      birth: isoDate(record?.birth ?? ""),
      phone: phoneOf(record?.phone ?? ""),
      opened: isoDate(record?.opened ?? ""),
      record,
      history,
      recordFiles: [
        ...(u.record ? [planFile(policy, u.record, pathOf(u.record).slice(1).join("/"), true)] : []),
        ...u.others.filter((f) => !isSystemFile(f.name)).map((f) => planFile(policy, f, pathOf(f).slice(1).join("/"))),
      ],
      remarks,
      mergeInto: null,
      existing: null,
      existingAmbiguous: false,
      skip: false,
      createAnyway: false,
    })
  }

  // 3. Two records of one person in the export: the second goes into the first.
  const seen = new Map<string, string>()
  for (const p of patients) {
    const k = `${nameKey(`${p.firstName} ${p.lastName}`)}|${p.birth}`
    const first = seen.get(k)
    if (first) {
      p.mergeInto = first
      p.remarks.push(`Même personne que ${quoteFr(first)} : ses fichiers et son historique y sont ajoutés`)
    } else seen.set(k, p.key)
  }

  // 4. Already in the cabinet? One read of the whole cabinet, matched here — a patient created by an earlier pass
  //    of this import is found again, which is what makes « PDF today, radios next week » work.
  const cabinetAll = recordFiles.length || attachmentFiles.length ? await patientsApi.list() : []
  for (const p of patients) {
    if (!p.mergeInto) {
      const found = sameInCabinet(p, cabinetAll)
      p.existing = found.existing
      p.existingAmbiguous = found.ambiguous
    }
  }
  const taken = new Set(patients.map((p) => p.existing?.id).filter(Boolean))
  const cabinet: CabinetPatient[] = cabinetAll
    .filter((c) => !taken.has(c.id))
    .map((c) => ({
      key: `${CABINET_PREFIX}${c.id}`,
      id: c.id,
      label: patientLabel(c),
      number: c.notes?.match(/ancien logiciel \(dossier n° (\d+)\//)?.[1] ?? null,
    }))

  // 5. The attachments: one folder per patient, matched on the name, tolerant of typos — and asking when unsure.
  const rootOf = (key: string) => patients.find((p) => p.key === key)?.mergeInto ?? key
  const targets: MatchTarget[] = [
    ...patients.map((p) => ({
      key: rootOf(p.key),
      names: [`${p.firstName} ${p.lastName}`, p.key],
      number: p.numero.split("/")[0] || leadingNumber(p.key),
    })),
    ...cabinet.map((c) => ({ key: c.key, names: [c.label.split(" · ")[0]], number: c.number })),
  ]
  const byFolder = new Map<string, File[]>()
  const looseFiles: string[] = []
  for (const f of attachmentFiles) {
    if (isSystemFile(f.name)) continue
    const parts = pathOf(f)
    if (parts.length < 3) {
      looseFiles.push(f.name)
      continue
    }
    byFolder.set(parts[1], [...(byFolder.get(parts[1]) ?? []), f])
  }
  const folders: AttachmentFolder[] = [...byFolder]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, files]) => {
      const verdict = matchFolder(name, targets)
      return {
        name,
        files: files.map((f) => planFile(policy, f, pathOf(f).slice(1).join("/"))),
        bytes: files.reduce((n, f) => n + f.size, 0),
        verdict,
        choice: verdict.kind === "sure" ? verdict.key : verdict.kind === "none" ? null : undefined,
      }
    })

  const quota = policy && policy.storageQuotaBytes > 0 ? { total: policy.storageQuotaBytes, used: policy.storageUsedBytes } : null
  return { patients, folders, cabinet, looseFiles, quota }
}

/** The files a patient will receive: its record PDF, plus every attachment folder chosen for it. */
export function filesFor(plan: ImportPlan, key: string): PlanFile[] {
  const own = plan.patients.filter((p) => (p.mergeInto ?? p.key) === key && !p.skip).flatMap((p) => p.recordFiles)
  return [...own, ...plan.folders.filter((f) => f.choice === key).flatMap((f) => f.files)]
}

/** Folders still waiting for somebody to choose: the import does not start while there is one. */
export const undecided = (plan: ImportPlan) => plan.folders.filter((f) => f.choice === undefined)

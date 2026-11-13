/**
 * One import of camera photos, from the card to the patients' « Photographie » folders (`features/camera-photo-import`).
 *
 * ⚠️ **Module state, not component state, on purpose.** Filing forty photos takes a minute or two, and the dentist
 * will leave the screen while it runs: the queue lives here, so changing page neither cancels the uploads nor loses
 * the review she was half-way through. `beforeunload` warns only while photos are actually being sent.
 *
 * ⚠️ **Nothing is filed before « Ranger ».** Reading, the duplicate check, the camera registry and the match are all
 * reads (pairing a camera is the one write, and it changes nothing a patient's record shows).
 */
import { useSyncExternalStore } from "react"
import { toast } from "sonner"
import { ApiError } from "@/lib/api/client"
import { clinicCamerasApi, type ClinicCameraDto } from "@/lib/api/clinic-cameras"
import { patientFilesApi } from "@/lib/api/patient-files"
import {
  PHOTO_IMPORT_LIMITS,
  PhotoImportCode,
  photoImportApi,
  type PhotoImportContextDto,
} from "@/lib/api/photo-import"
import { uploadPolicyApi, type UploadPolicy } from "@/lib/api/upload-policy"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { buildPreview } from "@/lib/files/preview"
import { refreshUploadPolicy } from "@/lib/hooks/use-upload-policy"
import { CloudCopyError, makeCloudCopy } from "./cloud-copy"
import { ackShellPhoto } from "./shell-camera"
import { readExif, type ExifFacts } from "./exif"
import { cameraFingerprintOf, cameraLabelOf, captureKeyOf, serialSuffixOf } from "./keys"
import {
  agendaFor,
  detectClockOffset,
  matchBurst,
  matchPhotosToVisits,
  type MatchCandidate,
  type MatchPhoto,
  type MatchVisit,
  type MatchWalkIn,
  type PhotoGroup,
} from "./match"
import type { SourceBatch, SkippedKind } from "./sources"
import {
  MINUTE_MS,
  parseExifDateTime,
  parseExifOffset,
  parseWallClockIso,
  toWallClockIso,
  wallClockDay,
  wallClockOfDay,
  wallClockOfInstant,
  type WallClock,
} from "./wall-clock"

/** A camera that wrote no make or model (a screenshot, a messaging app's re-encode) — never registered. */
export const UNKNOWN_CAMERA = "unknown"

/** Tunisia; replaced by the server's own figure as soon as the context arrives. */
const DEFAULT_CLINIC_OFFSET = 60

/** A first import shows the last week; later ones start half a day before the last import. */
const FIRST_IMPORT_DAYS = 7
const OVERLAP_HOURS = 12

/** Uploads in flight at once. */
const FILING_CONCURRENCY = 3

export interface ImportPhoto {
  /** The capture key — unique per shot. */
  id: string
  file: File
  exif: ExifFacts
  cameraKey: string
  /** The camera's own face (EXIF), or the file's date when there was none. */
  rawTime: WallClock | null
  uncertainTime: boolean
  /** OffsetTimeOriginal, when the camera wrote it. */
  zoneMinutes: number | null
  /** An object URL over the EXIF thumbnail's bytes — no copy, no decode. */
  thumbUrl: string | null
  /** The Windows app's id for this photo, which every outcome is reported back by; null on a browser path. */
  shellItemId: string | null
}

export interface ImportCamera {
  key: string
  label: string
  make: string
  model: string
  serialSuffix: string | null
  dto: ClinicCameraDto | null
  /** Never imported before this session — the screen asks whose camera it is. */
  isNew: boolean
  /** The agenda its photos are matched against; null = the whole cabinet. */
  doctorId: string | null
  /** True once the user picked the agenda here (a choice of « Tout le cabinet » included). */
  doctorChosen: boolean
  storedOffset: number
  /** What the photos say on top of the stored correction; 0 when nothing is proposed. */
  detectedOffset: number
  offsetAccepted: boolean
  photoCount: number
}

export interface PatientRef {
  patientId: string
  patientName: string
}

export interface ReviewGroup extends PhotoGroup {
  cameraKey: string
  /** The cut halves of a burst carry the key of the burst they came from. */
  splitFrom: string | null
}

export type FilingStatus = "pending" | "sending" | "filed" | "already" | "failed" | "blocked"

export interface FilingItem {
  photoId: string
  patientId: string
  patientName: string
  cameraKey: string
  time: WallClock | null
  status: FilingStatus
  fileId?: string
  message?: string
}

export type ImportPhase = "idle" | "reading" | "review" | "filing" | "done" | "failed"

export interface ImportState {
  phase: ImportPhase
  read: { done: number; total: number }
  error: string | null
  skipped: Record<SkippedKind, number>
  alreadyImported: number
  photos: ImportPhoto[]
  /** Photos older than each camera's window, hidden until asked for. */
  olderCount: number
  showOlder: boolean
  cameras: ImportCamera[]
  context: PhotoImportContextDto | null
  /** A context range was cut to the server's cap — the oldest photos have no agenda to match against. */
  rangeTrimmed: boolean
  groups: ReviewGroup[]
  choices: Record<string, PatientRef>
  ignored: Record<string, true>
  splits: Record<string, true>
  excluded: Record<string, true>
  filing: FilingItem[]
  storageFullMessage: string | null
  /** A Windows-app batch: its source, and what stayed on it (« plus anciennes ») or could not be copied. */
  shell: { label: string; older: number; failed: number } | null
}

const EMPTY_SKIPPED: Record<SkippedKind, number> = { raw: 0, heic: 0, video: 0, other: 0 }

function initialState(): ImportState {
  return {
    phase: "idle",
    read: { done: 0, total: 0 },
    error: null,
    skipped: { ...EMPTY_SKIPPED },
    alreadyImported: 0,
    photos: [],
    olderCount: 0,
    showOlder: false,
    cameras: [],
    context: null,
    rangeTrimmed: false,
    groups: [],
    choices: {},
    ignored: {},
    splits: {},
    excluded: {},
    filing: [],
    storageFullMessage: null,
    shell: null,
  }
}

const SERVER_STATE = initialState()
let state: ImportState = initialState()
const listeners = new Set<() => void>()
/** Every async step checks it, so a reset or a new import makes an old one's late answers harmless. */
let runId = 0

function set(patch: Partial<ImportState>) {
  state = { ...state, ...patch }
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useImportSession(): ImportState {
  return useSyncExternalStore(subscribe, () => state, () => SERVER_STATE)
}

/** Read outside React — the filing loop and the beforeunload guard. */
export function importSnapshot(): ImportState {
  return state
}

// ── Times ──────────────────────────────────────────────────────────────────────────────────────────────────────

function clinicOffset(): number {
  return state.context?.clinicUtcOffsetMinutes ?? DEFAULT_CLINIC_OFFSET
}

function cameraOf(key: string): ImportCamera | undefined {
  return state.cameras.find((camera) => camera.key === key)
}

/** The photo's time on the clinic's wall clock, with the camera's zone and clock correction applied. */
export function correctedTime(photo: ImportPhoto, camera = cameraOf(photo.cameraKey)): WallClock | null {
  if (photo.rawTime === null) return null
  let time = photo.rawTime
  if (photo.zoneMinutes !== null) time = time - photo.zoneMinutes * MINUTE_MS + clinicOffset() * MINUTE_MS
  const offset = (camera?.storedOffset ?? 0) + (camera?.offsetAccepted ? camera.detectedOffset : 0)
  return time + offset * MINUTE_MS
}

function windowStart(camera: ImportCamera): WallClock {
  const last = parseWallClockIso(camera.dto?.lastImportedAtLocal)
  if (last !== null) return last - OVERLAP_HOURS * 60 * MINUTE_MS
  const today = wallClockOfDay(wallClockDay(wallClockOfInstant(Date.now())))!
  return today - (FIRST_IMPORT_DAYS - 1) * 24 * 60 * MINUTE_MS
}

/** The photos this import is about: inside their camera's window, or every one once « plus anciennes » is on. */
export function visiblePhotos(): ImportPhoto[] {
  if (state.showOlder) return state.photos
  return state.photos.filter((photo) => {
    const camera = cameraOf(photo.cameraKey)
    const time = correctedTime(photo, camera)
    return time === null || !camera || time >= windowStart(camera)
  })
}

// ── Reading ────────────────────────────────────────────────────────────────────────────────────────────────────

/** Reads a batch: EXIF, keys, the duplicate check, the camera registry, the agenda — then the review. */
export async function startImport(batch: SourceBatch): Promise<void> {
  // A new card while the last one is still being sent would orphan that queue's files.
  if (state.phase === "filing") return
  resetImport()
  const run = ++runId
  set({
    phase: "reading",
    read: { done: 0, total: batch.photos.length },
    skipped: { ...batch.skipped },
    shell: batch.shell ? { label: batch.shell.label, older: batch.shell.older, failed: batch.shell.failed } : null,
  })

  try {
    const fingerprints = new Map<string, Promise<string | null>>()
    const photos: ImportPhoto[] = []
    let done = 0

    const readOne = async (file: File) => {
      const exif = await readExif(file)
      const identity = `${exif.make ?? ""}|${exif.model ?? ""}|${exif.bodySerialNumber ?? ""}`
      if (!fingerprints.has(identity)) fingerprints.set(identity, cameraFingerprintOf(exif))
      const fingerprint = await fingerprints.get(identity)!
      const id = await captureKeyOf(fingerprint, exif, file)

      const exifTime = parseExifDateTime(exif.dateTimeOriginal, exif.subSecTimeOriginal)
      const fileTime = file.lastModified > 0 ? wallClockOfInstant(file.lastModified) : null

      photos.push({
        id,
        file,
        exif,
        cameraKey: fingerprint ?? UNKNOWN_CAMERA,
        rawTime: exifTime ?? fileTime,
        uncertainTime: exifTime === null,
        zoneMinutes: exifTime === null ? null : parseExifOffset(exif.offsetTimeOriginal),
        thumbUrl: exif.thumbnail
          ? URL.createObjectURL(file.slice(exif.thumbnail.start, exif.thumbnail.start + exif.thumbnail.length, "image/jpeg"))
          : null,
        shellItemId: batch.shell?.itemIds.get(file) ?? null,
      })

      done++
      if (run === runId && (done % 25 === 0 || done === batch.photos.length)) {
        set({ read: { done, total: batch.photos.length } })
      }
    }

    // A few at a time: a card reader is the bottleneck, and a burst of 500 reads helps nobody.
    const queue = [...batch.photos]
    await Promise.all(
      Array.from({ length: Math.min(8, queue.length) }, async () => {
        for (let file = queue.shift(); file; file = queue.shift()) await readOne(file)
      }),
    )
    if (run !== runId) return revokeAll(photos)

    // The same shot twice in one batch (a folder and its copy) is one photo.
    const unique = new Map<string, ImportPhoto>()
    for (const photo of photos) {
      if (unique.has(photo.id)) {
        revoke(photo)
        // The same shot twice (two cards of one camera) is covered by the first: the shell need not offer it again.
        ackShellPhoto(photo.shellItemId, "already")
      } else unique.set(photo.id, photo)
    }

    const known = new Set(
      unique.size > 0 ? (await photoImportApi.known([...unique.keys()])).map((row) => row.captureKey) : [],
    )
    if (run !== runId) return revokeAll([...unique.values()])

    const fresh: ImportPhoto[] = []
    for (const photo of unique.values()) {
      if (known.has(photo.id)) {
        revoke(photo)
        ackShellPhoto(photo.shellItemId, "already")
      } else fresh.push(photo)
    }
    fresh.sort((a, b) => (a.rawTime ?? 0) - (b.rawTime ?? 0))

    const cameras = await registerCameras(fresh)
    if (run !== runId) return revokeAll(fresh)

    set({ photos: fresh, cameras, alreadyImported: known.size })
    await loadContextAndMatch(run)
  } catch (error) {
    if (run !== runId) return
    set({ phase: "failed", error: getErrorMessage(error, "Les photos n'ont pas pu être lues.") })
  }
}

async function registerCameras(photos: ImportPhoto[]): Promise<ImportCamera[]> {
  const byKey = new Map<string, ImportPhoto[]>()
  for (const photo of photos) byKey.set(photo.cameraKey, [...(byKey.get(photo.cameraKey) ?? []), photo])

  const cameras: ImportCamera[] = []
  for (const [key, list] of byKey) {
    const exif = list[0].exif
    const base: ImportCamera = {
      key,
      label: key === UNKNOWN_CAMERA ? "Appareil inconnu" : cameraLabelOf(exif),
      make: exif.make?.trim() ?? "",
      model: exif.model?.trim() ?? "",
      serialSuffix: serialSuffixOf(exif),
      dto: null,
      isNew: key !== UNKNOWN_CAMERA,
      doctorId: null,
      doctorChosen: false,
      storedOffset: 0,
      detectedOffset: 0,
      offsetAccepted: false,
      photoCount: list.length,
    }

    if (key !== UNKNOWN_CAMERA) {
      try {
        const dto = await clinicCamerasApi.register({
          fingerprint: key,
          make: base.make,
          model: base.model,
          serialSuffix: base.serialSuffix,
        })
        Object.assign(base, {
          dto,
          label: dto.label,
          isNew: dto.lastImportedAt === null,
          doctorId: dto.doctorId,
          storedOffset: dto.clockOffsetMinutes,
        })
      } catch {
        // The registry is a convenience: without it the photos still match the whole cabinet's agenda.
      }
    }
    cameras.push(base)
  }
  return cameras.sort((a, b) => b.photoCount - a.photoCount)
}

/** The days the visible photos cover, one day of slack each side (a corrected clock can cross midnight). */
function contextRange(): { from: string; to: string; trimmed: boolean } | null {
  const times = visiblePhotos()
    .map((photo) => correctedTime(photo))
    .filter((time): time is WallClock => time !== null)
  if (times.length === 0) return null

  const day = 24 * 60 * MINUTE_MS
  const last = Math.max(...times) + day
  let first = Math.min(...times) - day
  const span = PHOTO_IMPORT_LIMITS.maxRangeDays - 1
  const trimmed = (last - first) / day > span
  if (trimmed) first = last - span * day
  return { from: wallClockDay(first), to: wallClockDay(last), trimmed }
}

async function loadContextAndMatch(run: number) {
  const range = contextRange()
  let context: PhotoImportContextDto | null = null
  if (range) {
    context = await photoImportApi.context(range.from, range.to)
    if (run !== runId) return
  }

  const cameras = state.cameras.map((camera) => {
    // A new camera matches the signed-in practitioner's agenda until somebody says otherwise; a camera already
    // used with « Tout le cabinet » keeps it.
    const doctorId = camera.doctorChosen ? camera.doctorId : camera.dto?.doctorId ?? (camera.isNew ? context?.myDoctorId ?? null : null)
    return { ...camera, doctorId }
  })
  set({ context, cameras, rangeTrimmed: range?.trimmed ?? false })
  detectOffsets()
  set({ phase: "review" })
}

// ── Matching ───────────────────────────────────────────────────────────────────────────────────────────────────

function agendaOf(camera: ImportCamera): { visits: MatchVisit[]; walkIns: MatchWalkIn[] } {
  const context = state.context
  if (!context) return { visits: [], walkIns: [] }
  const { visits, fiches } = agendaFor(camera.doctorId, context.visits, context.fiches)

  const ficheTimes = new Map<string, WallClock>()
  const walkIns: MatchWalkIn[] = []
  for (const fiche of fiches) {
    const savedAt = parseWallClockIso(fiche.savedAt)
    if (savedAt === null) continue
    // A fiche linked to its visit says when that patient really left; a fiche without one is a walk-in — unless
    // the same patient had a visit that day, which is then the visit it belongs to.
    const visit =
      (fiche.appointmentId && visits.find((item) => item.appointmentId === fiche.appointmentId)) ||
      (!fiche.appointmentId &&
        visits.find(
          (item) => item.patientId === fiche.patientId && item.start.slice(0, 10) === fiche.savedAt.slice(0, 10),
        ))
    if (visit) {
      ficheTimes.set(visit.appointmentId, Math.max(ficheTimes.get(visit.appointmentId) ?? 0, savedAt))
    } else if (!fiche.appointmentId) {
      walkIns.push({
        key: fiche.dentalRecordId,
        patientId: fiche.patientId,
        patientName: fiche.patientName,
        doctorId: fiche.doctorId,
        savedAt,
      })
    }
  }

  const matchVisits: MatchVisit[] = []
  for (const visit of visits) {
    const start = parseWallClockIso(visit.start)
    const expectedEnd = parseWallClockIso(visit.expectedEnd)
    if (start === null || expectedEnd === null) continue
    matchVisits.push({
      appointmentId: visit.appointmentId,
      patientId: visit.patientId,
      patientName: visit.patientName,
      doctorId: visit.doctorId,
      start,
      expectedEnd,
      lateArrivalMinutes: visit.lateArrivalMinutes ?? 0,
      ficheSavedAt: ficheTimes.get(visit.appointmentId) ?? null,
    })
  }
  return { visits: matchVisits, walkIns }
}

function detectOffsets() {
  const visible = visiblePhotos()
  const cameras = state.cameras.map((camera) => {
    if (camera.key === UNKNOWN_CAMERA) return { ...camera, detectedOffset: 0, offsetAccepted: false }
    const base = { ...camera, offsetAccepted: false }
    const times = visible
      .filter((photo) => photo.cameraKey === camera.key && !photo.uncertainTime)
      .map((photo) => correctedTime(photo, base))
      .filter((time): time is WallClock => time !== null)
    const { visits, walkIns } = agendaOf(camera)
    const detected = detectClockOffset(times, visits, walkIns)
    return { ...camera, detectedOffset: detected, offsetAccepted: detected !== 0 }
  })
  set({ cameras, choices: {}, ignored: {}, splits: {} })
  recompute()
}

function recompute() {
  const visible = visiblePhotos()
  const groups: ReviewGroup[] = []

  for (const camera of state.cameras) {
    const photos: MatchPhoto[] = visible
      .filter((photo) => photo.cameraKey === camera.key)
      .map((photo) => ({ id: photo.id, takenAt: correctedTime(photo, camera), uncertainTime: photo.uncertainTime }))
    if (photos.length === 0) continue

    const { visits, walkIns } = agendaOf(camera)
    for (const group of matchPhotosToVisits(photos, visits, walkIns)) {
      if (state.splits[group.key] && group.splitAt !== null) {
        const byId = new Map(photos.map((photo) => [photo.id, photo]))
        const halves = [group.photoIds.slice(0, group.splitAt), group.photoIds.slice(group.splitAt)]
        for (const half of halves) {
          const decided = matchBurst(half.map((id) => byId.get(id)!), visits, walkIns)
          groups.push({ key: `burst-${half[0]}`, ...decided, cameraKey: camera.key, splitFrom: group.key })
        }
      } else {
        groups.push({ ...group, cameraKey: camera.key, splitFrom: null })
      }
    }
  }

  groups.sort((a, b) => (a.start ?? Number.MAX_SAFE_INTEGER) - (b.start ?? Number.MAX_SAFE_INTEGER))
  const keys = new Set(groups.map((group) => group.key))
  const keep = <T,>(record: Record<string, T>) =>
    Object.fromEntries(Object.entries(record).filter(([key]) => keys.has(key))) as Record<string, T>

  set({
    groups,
    olderCount: state.photos.length - visible.length,
    choices: keep(state.choices),
    ignored: keep(state.ignored),
  })
}

// ── Review edits ───────────────────────────────────────────────────────────────────────────────────────────────

/** The patient a group will be filed to: the user's pick, else the matcher's sure answer. */
export function patientOf(group: ReviewGroup, current = state): PatientRef | null {
  const chosen = current.choices[group.key]
  if (chosen) return chosen
  return group.decision === "sure" && group.match
    ? { patientId: group.match.patientId, patientName: group.match.patientName }
    : null
}

export function choosePatient(groupKey: string, patient: PatientRef | MatchCandidate) {
  set({ choices: { ...state.choices, [groupKey]: { patientId: patient.patientId, patientName: patient.patientName } } })
}

export function toggleIgnored(groupKey: string) {
  const ignored = { ...state.ignored }
  if (ignored[groupKey]) delete ignored[groupKey]
  else ignored[groupKey] = true
  set({ ignored })
}

export function toggleExcluded(photoId: string) {
  const excluded = { ...state.excluded }
  if (excluded[photoId]) delete excluded[photoId]
  else excluded[photoId] = true
  set({ excluded })
}

export function splitGroup(groupKey: string) {
  set({ splits: { ...state.splits, [groupKey]: true } })
  recompute()
}

export async function setShowOlder(on: boolean) {
  if (state.phase !== "review") return
  set({ showOlder: on })
  const run = runId
  try {
    set({ phase: "reading" })
    await loadContextAndMatch(run)
  } catch (error) {
    if (run === runId) set({ phase: "failed", error: getErrorMessage(error, "L'agenda n'a pas pu être lu.") })
  }
}

export function setOffsetAccepted(cameraKey: string, accepted: boolean) {
  set({
    cameras: state.cameras.map((camera) => (camera.key === cameraKey ? { ...camera, offsetAccepted: accepted } : camera)),
    choices: {},
    ignored: {},
    splits: {},
  })
  recompute()
}

/** « Cet appareil est à : … » — saved on the camera at once, so the next import asks nothing. */
export async function setCameraDoctor(cameraKey: string, doctorId: string | null) {
  const camera = cameraOf(cameraKey)
  if (!camera) return
  set({
    cameras: state.cameras.map((item) =>
      item.key === cameraKey ? { ...item, doctorId, doctorChosen: true } : item,
    ),
  })
  detectOffsets()

  if (camera.dto) {
    try {
      const dto = await clinicCamerasApi.update(camera.dto.id, { doctorId, version: camera.dto.version })
      set({ cameras: state.cameras.map((item) => (item.key === cameraKey ? { ...item, dto } : item)) })
    } catch (error) {
      showErrorToast(error, "Le praticien de l'appareil n'a pas pu être enregistré.")
    }
  }
}

/** Every active group has a patient — the condition for « Ranger ». */
export function unresolvedGroups(current = state): ReviewGroup[] {
  return current.groups.filter(
    (group) => !current.ignored[group.key] && !patientOf(group, current) && activePhotoIds(group, current).length > 0,
  )
}

export function activePhotoIds(group: ReviewGroup, current = state): string[] {
  return group.photoIds.filter((id) => !current.excluded[id])
}

/** The photos « Ranger » will send, with their patient. */
export function filingPlan(current = state): FilingItem[] {
  const byId = new Map(current.photos.map((photo) => [photo.id, photo]))
  const items: FilingItem[] = []
  for (const group of current.groups) {
    if (current.ignored[group.key]) continue
    const patient = patientOf(group, current)
    if (!patient) continue
    for (const id of activePhotoIds(group, current)) {
      const photo = byId.get(id)
      if (!photo) continue
      items.push({
        photoId: id,
        patientId: patient.patientId,
        patientName: patient.patientName,
        cameraKey: photo.cameraKey,
        time: photo.uncertainTime ? null : correctedTime(photo),
        status: "pending",
      })
    }
  }
  return items
}

// ── Filing ─────────────────────────────────────────────────────────────────────────────────────────────────────

function warnBeforeLeaving(event: BeforeUnloadEvent) {
  event.preventDefault()
  // Chrome ignores the text, but a non-empty returnValue is what makes it ask.
  event.returnValue = ""
}

function setItem(photoId: string, patch: Partial<FilingItem>) {
  set({ filing: state.filing.map((item) => (item.photoId === photoId ? { ...item, ...patch } : item)) })
}

/** « Ranger » — sends every planned photo, three at a time. */
export async function startFiling(policy: UploadPolicy | null) {
  if (state.phase !== "review" || unresolvedGroups().length > 0) return
  const plan = filingPlan()
  if (plan.length === 0) return
  set({ phase: "filing", filing: plan, storageFullMessage: null })
  await runFiling(policy)
}

/** Sends the failed photos again (a dropped connection, a refused copy fixed by another try). */
export async function retryFailed() {
  if (state.phase !== "done") return
  set({
    phase: "filing",
    storageFullMessage: null,
    filing: state.filing.map((item) =>
      item.status === "failed" || item.status === "blocked" ? { ...item, status: "pending", message: undefined } : item,
    ),
  })
  await runFiling(null)
}

async function runFiling(given: UploadPolicy | null) {
  const run = runId
  window.addEventListener("beforeunload", warnBeforeLeaving)

  try {
    // The copy rule is the server's: read it now rather than trust a page that may have opened yesterday.
    let policy = given
    if (!policy) {
      try {
        policy = await uploadPolicyApi.get()
      } catch (error) {
        set({
          phase: "done",
          filing: state.filing.map((item) =>
            item.status === "pending" ? { ...item, status: "failed", message: getErrorMessage(error) } : item,
          ),
        })
        return
      }
    }
    const rule = policy.cameraCopy ?? null
    const byId = new Map(state.photos.map((photo) => [photo.id, photo]))
    const folders = new Map<string, Promise<string>>()
    const folderOf = (patientId: string) => {
      let folder = folders.get(patientId)
      if (!folder) {
        folder = patientFilesApi.ensureDefaultFolder(patientId, "photographie").then((dto) => dto.id)
        // A refused folder must not poison the patient's next photo.
        folder.catch(() => folders.delete(patientId))
        folders.set(patientId, folder)
      }
      return folder
    }

    let stopped = false
    const next = () => (stopped ? undefined : state.filing.find((item) => item.status === "pending"))

    const send = async (item: FilingItem) => {
      const photo = byId.get(item.photoId)
      if (!photo) {
        setItem(item.photoId, { status: "failed", message: "Photo introuvable : relancez l'importation." })
        return
      }
      setItem(item.photoId, { status: "sending" })
      try {
        const folderId = await folderOf(item.patientId)
        const upload = await makeCloudCopy(photo.file, rule)
        const preview = await buildPreview(upload).catch(() => null)
        const camera = cameraOf(photo.cameraKey)
        const dto = await patientFilesApi.uploadFile(item.patientId, upload, folderId, undefined, preview, {
          captureKey: photo.id,
          capturedAtLocal: item.time !== null ? toWallClockIso(item.time) : null,
          cameraModel: camera && camera.key !== UNKNOWN_CAMERA ? camera.label : null,
        })
        setItem(item.photoId, { status: "filed", fileId: dto.id })
        // The Windows app keeps the full-size original only where the cloud stored a reduced copy.
        ackShellPhoto(photo.shellItemId, "filed", {
          fileId: dto.id,
          patientId: item.patientId,
          patientName: item.patientName,
          keepOriginal: rule !== null,
        })
      } catch (error) {
        if (error instanceof ApiError && error.code === PhotoImportCode.AlreadyImported) {
          setItem(item.photoId, { status: "already" })
          ackShellPhoto(photo.shellItemId, "already")
        } else if (error instanceof ApiError && error.code === PhotoImportCode.StorageFull) {
          stopped = true
          set({ storageFullMessage: error.message })
          setItem(item.photoId, { status: "blocked", message: error.message })
          ackShellPhoto(photo.shellItemId, "failed")
        } else {
          const message = error instanceof CloudCopyError ? error.message : getErrorMessage(error)
          setItem(item.photoId, { status: "failed", message })
          ackShellPhoto(photo.shellItemId, "failed")
        }
      }
    }

    await Promise.all(
      Array.from({ length: FILING_CONCURRENCY }, async () => {
        for (let item = next(); item && run === runId; item = next()) await send(item)
      }),
    )
    if (run !== runId) return

    if (stopped) {
      set({
        filing: state.filing.map((item) =>
          item.status === "pending" ? { ...item, status: "blocked", message: state.storageFullMessage ?? undefined } : item,
        ),
      })
    }

    // What the dentist set aside (an ignored group, an excluded photo) is not offered again by the Windows app.
    // A photo merely hidden as « plus ancienne » is not answered: it was never looked at.
    const planned = new Set(state.filing.map((item) => item.photoId))
    for (const photo of visiblePhotos()) {
      if (!planned.has(photo.id)) ackShellPhoto(photo.shellItemId, "skipped")
    }

    await recordImports()
    refreshUploadPolicy()

    const filed = state.filing.filter((item) => item.status === "filed").length
    const failed = state.filing.filter((item) => item.status === "failed" || item.status === "blocked").length
    set({ phase: "done" })
    if (failed === 0) toast.success(filed === 1 ? "1 photo rangée." : `${filed} photos rangées.`)
    else toast.error(`${failed} photo${failed > 1 ? "s" : ""} non rangée${failed > 1 ? "s" : ""} : voir « Importer les photos ».`)
  } finally {
    window.removeEventListener("beforeunload", warnBeforeLeaving)
  }
}

/** Tells each camera where its next import starts, the clock correction it used and — for a new one — whose it is. */
async function recordImports() {
  for (const camera of state.cameras) {
    if (!camera.dto) continue
    const times = state.filing
      .filter((item) => item.cameraKey === camera.key && (item.status === "filed" || item.status === "already"))
      .map((item) => item.time)
      .filter((time): time is WallClock => time !== null)

    const offset = camera.storedOffset + (camera.offsetAccepted ? camera.detectedOffset : 0)
    try {
      let dto = camera.dto
      if (camera.isNew && !camera.doctorChosen && dto.doctorId === null && camera.doctorId !== null) {
        dto = await clinicCamerasApi.update(dto.id, { doctorId: camera.doctorId, version: dto.version })
      }
      if (times.length > 0 || offset !== camera.storedOffset) {
        dto = await clinicCamerasApi.recordImport(
          dto.id,
          times.length > 0 ? toWallClockIso(Math.max(...times)) : null,
          offset !== camera.storedOffset ? offset : null,
        )
      }
      set({ cameras: state.cameras.map((item) => (item.key === camera.key ? { ...item, dto } : item)) })
    } catch {
      // The photos are filed; a camera that does not remember this import only re-offers them, as « déjà importées ».
    }
  }
}

// ── Reset ──────────────────────────────────────────────────────────────────────────────────────────────────────

function revoke(photo: ImportPhoto) {
  if (photo.thumbUrl) URL.revokeObjectURL(photo.thumbUrl)
}

function revokeAll(photos: ImportPhoto[]) {
  for (const photo of photos) revoke(photo)
}

/** Back to the source step. Refused while photos are being sent — the queue would lose its files. */
export function resetImport() {
  if (state.phase === "filing") return
  runId++
  revokeAll(state.photos)
  state = initialState()
  for (const listener of listeners) listener()
}

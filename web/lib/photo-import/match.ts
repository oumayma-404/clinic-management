/**
 * Which patient a burst of photos belongs to (`features/camera-photo-import`) — pure: no React, no fetch, no clock.
 *
 * The dentist photographs a patient in the chair, a few shots in a minute or two, then the next patient. So photos are
 * cut into **bursts** by the gaps between them, and each burst is matched to the visit whose **core** — from the
 * patient's real arrival (`start + lateArrival`) to the later of the expected end and the moment the fiche was saved —
 * holds the burst's median time.
 *
 * ⚠️ **The rule every case is held to: never sure and wrong.** « À vérifier » costs one tap; a photo filed in the wrong
 * patient's record is a clinical error, and the dentist will trust a « sûr » she has seen right a hundred times. So
 * every doubt asks: two visits holding the time, a burst that straddles two patients, bursts whose order contradicts
 * the agenda's, a photo whose time came from the file rather than the camera.
 *
 * ⚠️ **{@link MATCH_RULES} are starting values**, to be tuned on the pilot's real day (blueprint § Tests, « owed »).
 * They live in one object so a tuning is one edit, and `check:responsive`'s `match-rules-have-one-owner` keeps a second
 * copy from appearing in a component.
 */
import { MINUTE_MS, type WallClock } from "./wall-clock"

export const MATCH_RULES = {
  /** A new burst starts when two consecutive photos are further apart than this. */
  burstGapMinutes: 4,
  /** A visit's margin: its core widened this much before… */
  leadMinutes: 15,
  /** …and this much after. */
  tailMinutes: 20,
  /** A fiche saved with no appointment (a walk-in) stands for a visit this long before its save… */
  walkInBeforeMinutes: 45,
  /** …and this long after. */
  walkInAfterMinutes: 10,
  /** The clock-offset search: every whole minute in ±this. */
  offsetSearchMinutes: 180,
  /** A proposed offset smaller than this is noise, not a wrong clock. */
  offsetMinimumMinutes: 3,
  /** A proposed offset must explain this many photos more than no offset does… */
  offsetGainRatio: 1.3,
  /** …and at least this many photos. */
  offsetMinimumPhotos: 5,
  /** Candidates further than this from a burst are not offered — they are not plausible, only present. */
  candidateWindowMinutes: 180,
  /** At most this many candidates under a burst to check. */
  maxCandidates: 3,
} as const

export interface MatchPhoto {
  id: string
  /** The camera's own time (EXIF), or the file's date when the camera wrote none. */
  takenAt: WallClock | null
  /** True when `takenAt` is the file's date — the burst always asks. */
  uncertainTime: boolean
}

export interface MatchVisit {
  appointmentId: string
  patientId: string
  patientName: string
  doctorId: string | null
  start: WallClock
  /** Already includes the late arrival (the server's `ExpectedEnd`). */
  expectedEnd: WallClock
  lateArrivalMinutes: number
  /** When this visit's fiche was saved, if one was. */
  ficheSavedAt: WallClock | null
}

/** A fiche saved with no appointment — evidence that a patient was in the chair around `savedAt`. */
export interface MatchWalkIn {
  key: string
  patientId: string
  patientName: string
  doctorId: string | null
  savedAt: WallClock
}

export interface MatchCandidate {
  patientId: string
  patientName: string
  doctorId: string | null
  appointmentId: string | null
  /** Orders candidates and checks the agenda's order: the visit's real arrival, or the walk-in window's start. */
  at: WallClock
  /** What the screen prints: « RDV 09:00 », or « fiche à 10:30 » for a walk-in. */
  shownAt: WallClock
  kind: "visit" | "walk-in"
  /** Minutes between the burst and the visit's core; 0 inside it. */
  distanceMinutes: number
}

export type MatchReason =
  | "core" // exactly one patient's core holds the burst
  | "margin" // no core, exactly one margin holds it
  | "walk-in" // a fiche saved without an appointment holds it
  | "overlap" // two patients' cores hold it
  | "straddle" // its first and last photos sit in two patients' cores
  | "order" // it contradicts the order of the bursts around it
  | "near" // nothing holds it; the nearest visits are offered
  | "none" // nothing near it at all
  | "no-date" // the camera wrote no time

export interface PhotoGroup {
  key: string
  photoIds: string[]
  start: WallClock | null
  end: WallClock | null
  median: WallClock | null
  decision: "sure" | "ask"
  reason: MatchReason
  /** Set when `decision` is `sure`. */
  match: MatchCandidate | null
  /** Ranked; under a sure group, the alternatives. */
  candidates: MatchCandidate[]
  /** Where « Couper ici » cuts a straddling burst: the first photo of the second half. */
  splitAt: number | null
  uncertainTime: boolean
}

interface Holder {
  candidate: MatchCandidate
  from: WallClock
  to: WallClock
}

const minutes = (value: number) => value * MINUTE_MS

/** A visit's core: from the real arrival to the later of the expected end and the fiche's save. */
export function coreOf(visit: MatchVisit): { from: WallClock; to: WallClock } {
  const from = visit.start + minutes(Math.max(0, visit.lateArrivalMinutes))
  const to = Math.max(visit.expectedEnd, visit.ficheSavedAt ?? visit.expectedEnd, from)
  return { from, to }
}

function holders(visits: MatchVisit[], walkIns: MatchWalkIn[]) {
  const cores: Holder[] = []
  const margins: Holder[] = []

  for (const visit of visits) {
    const core = coreOf(visit)
    const candidate: MatchCandidate = {
      patientId: visit.patientId,
      patientName: visit.patientName,
      doctorId: visit.doctorId,
      appointmentId: visit.appointmentId,
      // The real arrival, not the booked start: a patient booked at 09:00 who arrived at 10:00 sat after the
      // 09:30 one, and the order check must not call the agenda's true order a contradiction.
      at: core.from,
      shownAt: visit.start,
      kind: "visit",
      distanceMinutes: 0,
    }
    cores.push({ candidate, ...core })
    margins.push({
      candidate,
      from: core.from - minutes(MATCH_RULES.leadMinutes),
      to: core.to + minutes(MATCH_RULES.tailMinutes),
    })
  }

  for (const walkIn of walkIns) {
    const from = walkIn.savedAt - minutes(MATCH_RULES.walkInBeforeMinutes)
    // A walk-in fiche is direct evidence, so its window counts as a core: a photo inside it and inside another
    // patient's core is two patients, and asks.
    cores.push({
      candidate: {
        patientId: walkIn.patientId,
        patientName: walkIn.patientName,
        doctorId: walkIn.doctorId,
        appointmentId: null,
        at: from,
        shownAt: walkIn.savedAt,
        kind: "walk-in",
        distanceMinutes: 0,
      },
      from,
      to: walkIn.savedAt + minutes(MATCH_RULES.walkInAfterMinutes),
    })
  }

  return { cores, margins }
}

const inside = (holder: Holder, time: WallClock) => time >= holder.from && time <= holder.to

/** One candidate per patient — two visits of one patient are not an ambiguity about whose photo it is. */
function distinctPatients(list: Holder[]): Holder[] {
  const seen = new Map<string, Holder>()
  for (const holder of list) if (!seen.has(holder.candidate.patientId)) seen.set(holder.candidate.patientId, holder)
  return [...seen.values()]
}

function distanceTo(holder: Holder, time: WallClock): number {
  if (time < holder.from) return (holder.from - time) / MINUTE_MS
  if (time > holder.to) return (time - holder.to) / MINUTE_MS
  return 0
}

function ranked(cores: Holder[], time: WallClock, limit: number = MATCH_RULES.maxCandidates): MatchCandidate[] {
  const byPatient = new Map<string, MatchCandidate>()
  for (const holder of cores) {
    const distance = Math.round(distanceTo(holder, time))
    if (distance > MATCH_RULES.candidateWindowMinutes) continue
    const existing = byPatient.get(holder.candidate.patientId)
    if (!existing || distance < existing.distanceMinutes) {
      byPatient.set(holder.candidate.patientId, { ...holder.candidate, distanceMinutes: distance })
    }
  }
  return [...byPatient.values()]
    .sort((a, b) => a.distanceMinutes - b.distanceMinutes || a.at - b.at || a.patientName.localeCompare(b.patientName))
    .slice(0, limit)
}

/**
 * How many minutes to add to a camera's clock, when its photos say it is wrong — or 0.
 *
 * Every whole-minute offset in ±{@link MATCH_RULES.offsetSearchMinutes} is scored by how many photos it puts inside a
 * visit's core. One is proposed only when it explains clearly more photos than leaving the clock alone, and when it is
 * big enough to be a wrong clock rather than a slow visit.
 *
 * ⚠️ **The middle of the best run, never its smallest member.** Many offsets tie at the best score — every one that
 * keeps each photo inside its visit — and the smallest of them lands every photo on the very first minute of its
 * visit, one minute from the previous patient. The middle of the run is the correction that leaves the most room on
 * both sides.
 *
 * ⚠️ On a fully booked day, a clock off by exactly one slot can still score like a right one — the thumbnails beside
 * each name on the review screen are the last line, and the Windows app's reading of the camera's own clock
 * (phase 2) is the real fix.
 */
export function detectClockOffset(times: WallClock[], visits: MatchVisit[], walkIns: MatchWalkIn[] = []): number {
  if (times.length < MATCH_RULES.offsetMinimumPhotos) return 0
  const { cores } = holders(visits, walkIns)
  if (cores.length === 0) return 0

  const score = (offset: number) => {
    let count = 0
    for (const time of times) {
      const shifted = time + minutes(offset)
      if (cores.some((core) => inside(core, shifted))) count++
    }
    return count
  }

  const range = MATCH_RULES.offsetSearchMinutes
  const scores: number[] = []
  for (let offset = -range; offset <= range; offset++) scores.push(score(offset))

  const atZero = scores[range]
  const best = Math.max(...scores)
  // Leaving the clock alone explains as much as anything else: nothing to correct.
  if (best <= atZero) return 0
  if (best < Math.max(MATCH_RULES.offsetMinimumPhotos, MATCH_RULES.offsetGainRatio * atZero)) return 0

  // The runs of consecutive offsets reaching the best score; the widest wins, then the one nearest zero.
  let chosen: { from: number; to: number } | null = null
  let runFrom: number | null = null
  for (let index = 0; index <= scores.length; index++) {
    const reaches = index < scores.length && scores[index] === best
    if (reaches && runFrom === null) runFrom = index - range
    if (!reaches && runFrom !== null) {
      const run = { from: runFrom, to: index - 1 - range }
      const width = run.to - run.from
      const chosenWidth = chosen ? chosen.to - chosen.from : -1
      if (
        !chosen ||
        width > chosenWidth ||
        (width === chosenWidth && Math.abs(run.from + run.to) < Math.abs(chosen.from + chosen.to))
      ) {
        chosen = run
      }
      runFrom = null
    }
  }

  const middle = chosen ? Math.round((chosen.from + chosen.to) / 2) : 0
  return Math.abs(middle) < MATCH_RULES.offsetMinimumMinutes ? 0 : middle
}

/** Photos in time order, cut where two consecutive ones are more than {@link MATCH_RULES.burstGapMinutes} apart. */
export function groupIntoBursts<T extends { takenAt: WallClock | null }>(photos: T[]): T[][] {
  const timed = photos.filter((photo) => photo.takenAt !== null).sort((a, b) => a.takenAt! - b.takenAt!)
  const bursts: T[][] = []
  for (const photo of timed) {
    const current = bursts[bursts.length - 1]
    const previous = current?.[current.length - 1]
    if (previous && photo.takenAt! - previous.takenAt! <= minutes(MATCH_RULES.burstGapMinutes)) current.push(photo)
    else bursts.push([photo])
  }
  return bursts
}

function median(values: WallClock[]): WallClock {
  const sorted = [...values].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 === 1 ? sorted[middle] : Math.round((sorted[middle - 1] + sorted[middle]) / 2)
}

/** The first photo of the second half: after the largest gap inside the burst. */
function largestGapSplit(times: WallClock[]): number | null {
  if (times.length < 2) return null
  let at = 1
  let widest = -1
  for (let index = 1; index < times.length; index++) {
    const gap = times[index] - times[index - 1]
    if (gap > widest) {
      widest = gap
      at = index
    }
  }
  return at
}

function decide(photos: MatchPhoto[], cores: Holder[], margins: Holder[]): Omit<PhotoGroup, "key"> {
  const times = photos.map((photo) => photo.takenAt!)
  const start = times[0]
  const end = times[times.length - 1]
  const at = median(times)
  const uncertainTime = photos.some((photo) => photo.uncertainTime)
  const base = { photoIds: photos.map((photo) => photo.id), start, end, median: at, splitAt: null, uncertainTime }

  // 5 · A burst whose first and last photos sit in two different patients' cores is two patients.
  const firstIn = distinctPatients(cores.filter((core) => inside(core, start)))
  const lastIn = distinctPatients(cores.filter((core) => inside(core, end)))
  if (
    firstIn.length === 1 &&
    lastIn.length === 1 &&
    firstIn[0].candidate.patientId !== lastIn[0].candidate.patientId
  ) {
    // The cut goes where the first patient's visit ends — the first photo none of their cores holds. Photos taken
    // every two minutes have no « widest gap » to find, so that is only the fallback.
    const firstPatient = firstIn[0].candidate.patientId
    const boundary = times.findIndex(
      (time) => !cores.some((core) => core.candidate.patientId === firstPatient && inside(core, time)),
    )
    return {
      ...base,
      decision: "ask",
      reason: "straddle",
      match: null,
      candidates: ranked(cores, at),
      splitAt: boundary > 0 ? boundary : largestGapSplit(times),
    }
  }

  const coreHolders = distinctPatients(cores.filter((core) => inside(core, at)))
  const candidates = ranked(cores, at)

  if (coreHolders.length === 1) {
    const match = { ...coreHolders[0].candidate, distanceMinutes: 0 }
    return {
      ...base,
      decision: uncertainTime ? "ask" : "sure",
      reason: uncertainTime ? "no-date" : match.kind === "walk-in" ? "walk-in" : "core",
      match: uncertainTime ? null : match,
      candidates,
    }
  }

  if (coreHolders.length > 1) {
    return { ...base, decision: "ask", reason: uncertainTime ? "no-date" : "overlap", match: null, candidates }
  }

  const marginHolders = distinctPatients(margins.filter((margin) => inside(margin, at)))
  if (marginHolders.length === 1 && !uncertainTime) {
    // The candidate's distance is to its CORE, which `ranked` already measured.
    const patientId = marginHolders[0].candidate.patientId
    const match = candidates.find((candidate) => candidate.patientId === patientId) ?? marginHolders[0].candidate
    return { ...base, decision: "sure", reason: "margin", match, candidates }
  }

  return {
    ...base,
    decision: "ask",
    reason: uncertainTime ? "no-date" : candidates.length > 0 ? "near" : "none",
    match: null,
    candidates,
  }
}

/**
 * One group of photos decided as a whole — the two halves of a burst the user cut with « Couper ici ». Every photo
 * must carry a time; the halves keep the burst's own rules (a straddle still asks).
 */
export function matchBurst(photos: MatchPhoto[], visits: MatchVisit[], walkIns: MatchWalkIn[]): Omit<PhotoGroup, "key"> {
  const timed = photos.filter((photo) => photo.takenAt !== null).sort((a, b) => a.takenAt! - b.takenAt!)
  const { cores, margins } = holders(visits, walkIns)
  return decide(timed, cores, margins)
}

/**
 * Bursts → groups, each `sure` of one patient or asking. `photos` carry times already corrected for the camera's
 * clock; `visits` and `walkIns` are the agenda the camera's photos belong to.
 */
export function matchPhotosToVisits(photos: MatchPhoto[], visits: MatchVisit[], walkIns: MatchWalkIn[]): PhotoGroup[] {
  const { cores, margins } = holders(visits, walkIns)
  const groups: PhotoGroup[] = []

  for (const burst of groupIntoBursts(photos)) {
    const decided = decide(burst, cores, margins)
    groups.push({ key: `burst-${burst[0].id}`, ...decided })
  }

  // 6 · Order: walking the sure groups in time order, the visits they name must not go backwards. One that does
  // means a clock or a burst is wrong somewhere, and both sides of the contradiction ask.
  const sure = groups.filter((group) => group.decision === "sure" && group.match)
  const contradicted = new Set<PhotoGroup>()
  for (let index = 1; index < sure.length; index++) {
    const before = sure[index - 1].match!
    const after = sure[index].match!
    if (after.patientId !== before.patientId && after.at < before.at) {
      contradicted.add(sure[index - 1])
      contradicted.add(sure[index])
    }
  }
  for (const group of contradicted) {
    group.decision = "ask"
    group.reason = "order"
    group.match = null
  }

  // 8 · A photo with no time cannot be matched: one group per photo, always asking, with nothing proposed.
  for (const photo of photos.filter((item) => item.takenAt === null)) {
    groups.push({
      key: `undated-${photo.id}`,
      photoIds: [photo.id],
      start: null,
      end: null,
      median: null,
      decision: "ask",
      reason: "no-date",
      match: null,
      candidates: [],
      splitAt: null,
      uncertainTime: true,
    })
  }

  return groups
}

/**
 * The visits a camera's photos are matched against: one practitioner's agenda when the camera belongs to one,
 * else the whole cabinet's. A fiche counts when its visit is in that agenda, when it names that practitioner, or when it
 * names nobody — exactly the server's own doctor filter (`GetPhotoImportContextQuery`).
 */
export function agendaFor<V extends { doctorId: string | null; appointmentId: string }, F extends { doctorId: string | null; appointmentId: string | null }>(
  doctorId: string | null,
  visits: V[],
  fiches: F[],
): { visits: V[]; fiches: F[] } {
  if (!doctorId) return { visits, fiches }
  const kept = visits.filter((visit) => visit.doctorId === doctorId)
  const ids = new Set(kept.map((visit) => visit.appointmentId))
  return {
    visits: kept,
    fiches: fiches.filter((fiche) =>
      fiche.appointmentId ? ids.has(fiche.appointmentId) : fiche.doctorId === null || fiche.doctorId === doctorId,
    ),
  }
}

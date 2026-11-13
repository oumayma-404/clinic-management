import { expect, test } from "@playwright/test"
import { parseExif } from "../../web/lib/photo-import/exif"
import { captureKeyOf, cameraFingerprintOf, cameraLabelOf } from "../../web/lib/photo-import/keys"
import {
  agendaFor,
  detectClockOffset,
  matchBurst,
  matchPhotosToVisits,
  type MatchPhoto,
  type MatchVisit,
  type MatchWalkIn,
  type PhotoGroup,
} from "../../web/lib/photo-import/match"
import { parseExifDateTime, parseExifOffset, parseWallClockIso } from "../../web/lib/photo-import/wall-clock"

/**
 * The camera import's matcher and EXIF reader (`features/camera-photo-import`) — pure functions, no page and no API.
 * They live here because `web/` has no test runner; they need no stack, but the suite's global setup signs in
 * before any test, so run them alone with a config that skips it when the stack is down.
 *
 * ⚠️ **The rule every scenario is held to: never sure and wrong.** An « à vérifier » costs one tap; a photo filed
 * in the wrong patient's record is a clinical error. `assertNeverSureAndWrong` runs after every scenario, on top of
 * what that scenario expects.
 */

const DAY = Date.UTC(2026, 10, 12)
const MINUTE = 60_000

/** « 09:40 » (or « 2|09:40 » for the 2nd day after) → the wall-clock line. */
function at(time: string): number {
  const [dayPart, clock] = time.includes("|") ? time.split("|") : ["0", time]
  const [hours, minutes] = clock.split(":").map(Number)
  return DAY + Number(dayPart) * 24 * 60 * MINUTE + (hours * 60 + minutes) * MINUTE
}

function visit(
  patientId: string,
  start: string,
  minutes = 30,
  options: { late?: number; ficheAt?: string; doctorId?: string } = {},
): MatchVisit {
  const late = options.late ?? 0
  return {
    appointmentId: `rdv-${patientId}-${start}`,
    patientId,
    patientName: `Patient ${patientId}`,
    doctorId: options.doctorId ?? null,
    start: at(start),
    expectedEnd: at(start) + (minutes + late) * MINUTE,
    lateArrivalMinutes: late,
    ficheSavedAt: options.ficheAt ? at(options.ficheAt) : null,
  }
}

/** `count` photos from `start`, `everySeconds` apart, all truly of `patientId`. */
function burst(patientId: string, start: string, count = 3, everySeconds = 40, shiftMinutes = 0) {
  return Array.from({ length: count }, (_, index) => ({
    id: `${patientId}@${start}#${index}`,
    takenAt: at(start) + index * everySeconds * 1000 + shiftMinutes * MINUTE,
    uncertainTime: false,
    truth: patientId,
  }))
}

type TruthPhoto = MatchPhoto & { truth: string }

function assertNeverSureAndWrong(groups: PhotoGroup[], photos: TruthPhoto[]) {
  const truth = new Map(photos.map((photo) => [photo.id, photo.truth]))
  for (const group of groups) {
    if (group.decision !== "sure") continue
    for (const id of group.photoIds) {
      expect(group.match?.patientId, `sure group ${group.key} filed ${id} under the wrong patient`).toBe(truth.get(id))
    }
  }
}

function groupOf(groups: PhotoGroup[], photoId: string): PhotoGroup {
  const found = groups.find((group) => group.photoIds.includes(photoId))
  expect(found, `no group holds ${photoId}`).toBeTruthy()
  return found!
}

test.describe("photo import — matching photos to visits", () => {
  test("an on-time day is sure of every burst", () => {
    const visits = [visit("A", "09:00"), visit("B", "09:30"), visit("C", "10:00"), visit("D", "10:30")]
    const photos = [...burst("A", "09:10"), ...burst("B", "09:41"), ...burst("C", "10:12"), ...burst("D", "10:44")]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups).toHaveLength(4)
    expect(groups.every((group) => group.decision === "sure")).toBe(true)
    assertNeverSureAndWrong(groups, photos)
  })

  test("a late day with fiches is sure where only one patient can have been in the chair", () => {
    // A booked 09:00, arrived 09:20, fiche saved 09:58. B booked 09:30, seated after A, fiche saved 10:35.
    const visits = [visit("A", "09:00", 30, { late: 20, ficheAt: "09:58" }), visit("B", "09:30", 30, { ficheAt: "10:35" })]
    const photos = [...burst("A", "09:45"), ...burst("B", "10:20")]

    const groups = matchPhotosToVisits(photos, visits, [])

    // 09:45 sits inside both cores (B was booked from 09:30): it asks. 10:20 is B's alone: it is sure.
    expect(groupOf(groups, "A@09:45#0").decision).toBe("ask")
    expect(groupOf(groups, "B@10:20#0").decision).toBe("sure")
    assertNeverSureAndWrong(groups, photos)
  })

  test("a late day without fiches asks rather than guessing", () => {
    const visits = [visit("A", "09:00", 30, { late: 20 }), visit("B", "09:30")]
    // 09:45 is inside both cores; 10:05 is past both, inside both margins.
    const photos = [...burst("A", "09:45"), ...burst("B", "10:05")]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups.every((group) => group.decision === "ask")).toBe(true)
    // Both are still offered by name — one tap each.
    expect(groupOf(groups, "B@10:05#0").candidates.map((candidate) => candidate.patientId)).toContain("B")
    assertNeverSureAndWrong(groups, photos)
  })

  test("a camera an hour behind is detected, and the corrected day is sure", () => {
    const visits = [
      visit("A", "09:00"),
      visit("B", "09:30"),
      visit("C", "10:00"),
      visit("D", "10:30"),
      visit("E", "11:30"),
      visit("F", "12:00"),
    ]
    const truth = [
      ...burst("A", "09:12"),
      ...burst("B", "09:43"),
      ...burst("C", "10:11"),
      ...burst("D", "10:42"),
      ...burst("E", "11:41"),
      ...burst("F", "12:13"),
    ]
    // The camera's face reads 62 minutes earlier than the clinic's clock.
    const raw = truth.map((photo) => ({ ...photo, takenAt: photo.takenAt - 62 * MINUTE }))

    const offset = detectClockOffset(raw.map((photo) => photo.takenAt), visits)
    expect(Math.abs(offset - 62)).toBeLessThanOrEqual(10)

    const corrected = raw.map((photo) => ({ ...photo, takenAt: photo.takenAt + offset * MINUTE }))
    const groups = matchPhotosToVisits(corrected, visits, [])
    expect(groups.filter((group) => group.decision === "sure").length).toBeGreaterThanOrEqual(5)
    assertNeverSureAndWrong(groups, corrected)
  })

  test("a camera four minutes off is left alone", () => {
    const visits = [visit("A", "09:00"), visit("B", "09:30"), visit("C", "10:00")]
    const photos = [...burst("A", "09:10", 3, 40, 4), ...burst("B", "09:40", 3, 40, 4), ...burst("C", "10:10", 3, 40, 4)]

    expect(detectClockOffset(photos.map((photo) => photo.takenAt), visits)).toBe(0)
  })

  test("a burst straddling two visits asks, and « Couper » cuts where the first visit ends", () => {
    const visits = [visit("A", "09:00"), visit("B", "09:30")]
    const photos = [
      { id: "A1", takenAt: at("09:26"), uncertainTime: false, truth: "A" },
      { id: "A2", takenAt: at("09:28"), uncertainTime: false, truth: "A" },
      { id: "B1", takenAt: at("09:31"), uncertainTime: false, truth: "B" },
      { id: "B2", takenAt: at("09:33"), uncertainTime: false, truth: "B" },
    ]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups).toHaveLength(1)
    expect(groups[0].reason).toBe("straddle")
    expect(groups[0].splitAt).toBe(2)

    // Each half, cut there, is its own patient's — and sure of it.
    const first = matchBurst(photos.slice(0, 2), visits, [])
    const second = matchBurst(photos.slice(2), visits, [])
    expect(first.match?.patientId).toBe("A")
    expect(second.match?.patientId).toBe("B")
    assertNeverSureAndWrong(groups, photos)
  })

  test("a before and an after on one visit are two groups of the same patient", () => {
    const visits = [visit("A", "09:00", 45)]
    const photos = [...burst("A", "09:03"), ...burst("A", "09:38")]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups).toHaveLength(2)
    expect(groups.map((group) => group.match?.patientId)).toEqual(["A", "A"])
    assertNeverSureAndWrong(groups, photos)
  })

  test("a fiche saved with no appointment files its patient's photos", () => {
    const walkIn: MatchWalkIn = { key: "fiche-W", patientId: "W", patientName: "Patient W", doctorId: null, savedAt: at("11:00") }
    const photos = burst("W", "10:40")

    const groups = matchPhotosToVisits(photos, [], [walkIn])

    expect(groups[0].decision).toBe("sure")
    expect(groups[0].reason).toBe("walk-in")
    assertNeverSureAndWrong(groups, photos)
  })

  test("a day with no appointment proposes nobody", () => {
    const photos = burst("X", "15:00")

    const groups = matchPhotosToVisits(photos, [], [])

    expect(groups[0].decision).toBe("ask")
    expect(groups[0].reason).toBe("none")
    expect(groups[0].candidates).toHaveLength(0)
  })

  test("two chairs at once: the camera's own agenda decides, the whole cabinet asks", () => {
    const visits = [visit("A", "09:00", 30, { doctorId: "dr-1" }), visit("B", "09:00", 30, { doctorId: "dr-2" })]
    const photos = burst("A", "09:10")

    const cabinet = matchPhotosToVisits(photos, visits, [])
    expect(cabinet[0].decision).toBe("ask")
    expect(cabinet[0].reason).toBe("overlap")

    const mine = agendaFor("dr-1", visits, [] as Array<{ doctorId: string | null; appointmentId: string | null }>)
    const doctorOnly = matchPhotosToVisits(photos, mine.visits, [])
    expect(doctorOnly[0].decision).toBe("sure")
    assertNeverSureAndWrong(doctorOnly, photos)
  })

  test("a visit booked with no practitioner is in every practitioner's agenda", () => {
    // Most real visits name nobody; the camera's dentist must still find them (found by the browser walk).
    const visits = [visit("A", "09:00", 30, { doctorId: "dr-1" }), visit("C", "17:00")]
    const photos = [...burst("A", "09:10"), ...burst("C", "17:05")]

    const mine = agendaFor("dr-1", visits, [] as Array<{ doctorId: string | null; appointmentId: string | null }>)
    expect(mine.visits.map((v) => v.patientId)).toEqual(["A", "C"])

    const groups = matchPhotosToVisits(photos, mine.visits, [])
    expect(groups.map((group) => group.match?.patientId)).toEqual(["A", "C"])
    assertNeverSureAndWrong(groups, photos)

    // Another practitioner's visit stays out.
    expect(agendaFor("dr-2", visits, []).visits.map((v) => v.patientId)).toEqual(["C"])
  })

  test("a card holding three days files each day against its own agenda", () => {
    const visits = [visit("A", "0|09:00"), visit("B", "1|09:00"), visit("C", "2|09:00")]
    const photos = [...burst("A", "0|09:10"), ...burst("B", "1|09:10"), ...burst("C", "2|09:10")]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups.map((group) => group.match?.patientId)).toEqual(["A", "B", "C"])
    assertNeverSureAndWrong(groups, photos)
  })

  test("a photo with no time is its own group and always asks", () => {
    const visits = [visit("A", "09:00")]
    const photos = [{ id: "nodate", takenAt: null, uncertainTime: true, truth: "A" }]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups).toHaveLength(1)
    expect(groups[0].decision).toBe("ask")
    expect(groups[0].reason).toBe("no-date")
  })

  test("a photo dated by its file, not its camera, asks even inside a visit", () => {
    const visits = [visit("A", "09:00")]
    const photos = [{ id: "file-date", takenAt: at("09:10"), uncertainTime: true, truth: "A" }]

    const groups = matchPhotosToVisits(photos, visits, [])

    expect(groups[0].decision).toBe("ask")
    expect(groups[0].candidates[0]?.patientId).toBe("A")
  })
})

// ── The EXIF reader, on a JPEG built byte by byte ──────────────────────────────────────────────────────────────

/** A minimal camera JPEG: SOI, APP1 « Exif » with IFD0 (Make, Model, Orientation, Exif pointer), the Exif IFD
 *  (DateTimeOriginal, SubSec, OffsetTime, BodySerialNumber) and IFD1 with a 4-byte « thumbnail ». */
function cameraJpeg(little: boolean): Uint8Array {
  const tiff: number[] = []
  const u16 = (value: number) => (little ? [value & 0xff, value >> 8] : [value >> 8, value & 0xff])
  const u32 = (value: number) =>
    little
      ? [value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff, (value >>> 24) & 0xff]
      : [(value >>> 24) & 0xff, (value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff]
  const ascii = (text: string) => [...Array.from(text, (char) => char.charCodeAt(0)), 0]

  // Layout (offsets from the TIFF header): header 8 · IFD0 at 8 (4 entries) · Exif IFD · IFD1 · data.
  const ifd0At = 8
  const ifd0Size = 2 + 4 * 12 + 4
  const exifAt = ifd0At + ifd0Size
  const exifSize = 2 + 4 * 12 + 4
  const ifd1At = exifAt + exifSize
  const ifd1Size = 2 + 2 * 12 + 4
  let dataAt = ifd1At + ifd1Size

  const data: number[] = []
  const place = (bytes: number[]) => {
    const offset = dataAt + data.length
    data.push(...bytes)
    return offset
  }
  const make = place(ascii("Canon"))
  const model = place(ascii("Canon EOS 250D"))
  const date = place(ascii("2026:11:12 09:14:03"))
  const zone = place(ascii("+01:00"))
  const serial = place(ascii("123456789012"))
  const thumb = place([0xff, 0xd8, 0xff, 0xd9])

  const entry = (tag: number, type: number, count: number, value: number[]) => [...u16(tag), ...u16(type), ...u32(count), ...value]
  const inline16 = (value: number) => [...u16(value), 0, 0]

  tiff.push(...(little ? [0x49, 0x49] : [0x4d, 0x4d]), ...u16(0x2a), ...u32(ifd0At))
  tiff.push(...u16(4))
  tiff.push(...entry(0x010f, 2, 6, u32(make)))
  tiff.push(...entry(0x0110, 2, 15, u32(model)))
  tiff.push(...entry(0x0112, 3, 1, inline16(6)))
  tiff.push(...entry(0x8769, 4, 1, u32(exifAt)))
  tiff.push(...u32(ifd1At))
  tiff.push(...u16(4))
  tiff.push(...entry(0x9003, 2, 20, u32(date)))
  tiff.push(...entry(0x9011, 2, 7, u32(zone)))
  tiff.push(...entry(0x9291, 2, 3, [0x34, 0x35, 0x00, 0x00]))
  tiff.push(...entry(0xa431, 2, 13, u32(serial)))
  tiff.push(...u32(0))
  tiff.push(...u16(2))
  tiff.push(...entry(0x0201, 4, 1, u32(thumb)))
  tiff.push(...entry(0x0202, 4, 1, u32(4)))
  tiff.push(...u32(0))
  tiff.push(...data)

  const app1 = [0x45, 0x78, 0x69, 0x66, 0, 0, ...tiff]
  const length = app1.length + 2
  // A JFIF APP0 in front, as many cameras write it.
  const app0 = [0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 1, 1, 0, 0, 1, 0, 1, 0, 0]
  return new Uint8Array([0xff, 0xd8, ...app0, 0xff, 0xe1, length >> 8, length & 0xff, ...app1, 0xff, 0xda, 0, 2, 0xff, 0xd9])
}

test.describe("photo import — reading EXIF", () => {
  for (const little of [true, false]) {
    test(`a ${little ? "little" : "big"}-endian camera JPEG gives its seven facts and its thumbnail`, () => {
      const bytes = cameraJpeg(little)
      const exif = parseExif(bytes)

      expect(exif.make).toBe("Canon")
      expect(exif.model).toBe("Canon EOS 250D")
      expect(exif.orientation).toBe(6)
      expect(exif.dateTimeOriginal).toBe("2026:11:12 09:14:03")
      expect(exif.subSecTimeOriginal).toBe("45")
      expect(exif.offsetTimeOriginal).toBe("+01:00")
      expect(exif.bodySerialNumber).toBe("123456789012")
      expect(exif.thumbnail).not.toBeNull()
      const { start, length } = exif.thumbnail!
      expect(Array.from(bytes.slice(start, start + length))).toEqual([0xff, 0xd8, 0xff, 0xd9])
    })
  }

  test("a file that is not a JPEG, or a JPEG with no EXIF, reads as nothing — never throws", () => {
    expect(parseExif(new Uint8Array([0x89, 0x50, 0x4e, 0x47])).dateTimeOriginal).toBeNull()
    expect(parseExif(new Uint8Array([0xff, 0xd8, 0xff, 0xda, 0, 2])).make).toBeNull()
    expect(parseExif(cameraJpeg(true).slice(0, 40)).dateTimeOriginal).toBeNull()
  })

  test("the camera's clock face is read without any timezone", () => {
    expect(parseExifDateTime("2026:11:12 09:14:03", "45")).toBe(Date.UTC(2026, 10, 12, 9, 14, 3, 450))
    expect(parseExifDateTime("0000:00:00 00:00:00")).toBeNull()
    expect(parseExifDateTime("2026:02:31 10:00:00")).toBeNull()
    expect(parseExifOffset("+01:00")).toBe(60)
    expect(parseExifOffset("-05:30")).toBe(-330)
    expect(parseWallClockIso("2026-11-12T09:00:00")).toBe(Date.UTC(2026, 10, 12, 9))
    expect(parseWallClockIso("2026-11-12T09:00:00Z")).toBeNull()
  })

  test("the same shot always hashes to the same key, and a different shot does not", async () => {
    const exif = parseExif(cameraJpeg(true))
    const fingerprint = await cameraFingerprintOf(exif)
    expect(fingerprint).toMatch(/^[0-9a-f]{64}$/)

    const one = await captureKeyOf(fingerprint, exif, { name: "IMG_4521.JPG", size: 8_400_000 })
    const again = await captureKeyOf(fingerprint, exif, { name: "IMG_4521.JPG", size: 8_400_000 })
    const other = await captureKeyOf(fingerprint, exif, { name: "IMG_4522.JPG", size: 8_400_000 })
    expect(one).toBe(again)
    expect(other).not.toBe(one)
    expect(cameraLabelOf(exif)).toBe("Canon EOS 250D")
    expect(cameraLabelOf({ make: "FUJIFILM", model: "X-T30" })).toBe("FUJIFILM X-T30")
  })
})

/**
 * The two hashes the import computes (`features/camera-photo-import`): which camera took a photo, and which shot it is.
 *
 * - **Fingerprint** — SHA-256 of `Make|Model|BodySerialNumber` (or `Make|Model` with no serial). It is how a camera is
 *   « paired » across PCs and browsers: the server keeps its doctor, its clock correction and its last import under it.
 * - **Capture key** — SHA-256 of `fingerprint|DateTimeOriginal|SubSecTimeOriginal|name|size`. It is what stops a card
 *   inserted twice from being filed twice, and it needs only what EXIF and the directory listing already said:
 *   hashing whole files would mean reading gigabytes back from a card reader.
 *
 * ⚠️ **The server re-checks the key's shape and its uniqueness, never its recipe** — it never sees the photo's EXIF.
 * So the recipe lives here and only here, and changing it re-imports every card once.
 */
import type { ExifFacts } from "./exif"

/** Lower-case hex SHA-256 of a UTF-8 string. `crypto.subtle` where the page is a secure context, else hash-wasm. */
export async function sha256Hex(text: string): Promise<string> {
  const bytes = new TextEncoder().encode(text)
  const subtle = typeof crypto !== "undefined" ? crypto.subtle : undefined
  if (subtle) {
    const digest = await subtle.digest("SHA-256", bytes)
    return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("")
  }
  // `crypto.subtle` is withheld outside a secure context (a LAN install reached over plain HTTP). hash-wasm is
  // already in the bundle for the coffre, and the CSP admits it ('wasm-unsafe-eval').
  const { sha256 } = await import("hash-wasm")
  return sha256(bytes)
}

/** Null when the photo names no camera at all (a screenshot, a messaging app's re-encode). */
export async function cameraFingerprintOf(exif: ExifFacts): Promise<string | null> {
  const make = exif.make?.trim() ?? ""
  const model = exif.model?.trim() ?? ""
  if (!make && !model) return null
  const serial = exif.bodySerialNumber?.trim() ?? ""
  return sha256Hex(serial ? `${make}|${model}|${serial}` : `${make}|${model}`)
}

export async function captureKeyOf(
  fingerprint: string | null,
  exif: ExifFacts,
  file: { name: string; size: number },
): Promise<string> {
  return sha256Hex(
    [fingerprint ?? "", exif.dateTimeOriginal ?? "", exif.subSecTimeOriginal ?? "", file.name, String(file.size)].join("|"),
  )
}

/** The last characters of a body's serial — enough to tell two identical bodies apart on screen. */
export function serialSuffixOf(exif: ExifFacts): string | null {
  const serial = exif.bodySerialNumber?.trim()
  return serial ? serial.slice(-4) : null
}

/** « Canon EOS 250D » — the model when it already names the brand, else « brand model ». Mirrors `ClinicCamera.DefaultLabel`. */
export function cameraLabelOf(exif: Pick<ExifFacts, "make" | "model">): string {
  const make = exif.make?.trim() ?? ""
  const model = exif.model?.trim() ?? ""
  if (!model) return make
  if (!make) return model
  const brand = make.split(/\s+/)[0]
  return model.toLowerCase().includes(brand.toLowerCase()) ? model : `${make} ${model}`
}

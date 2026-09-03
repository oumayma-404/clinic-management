/** Byte helpers shared by the decoders and the DICOM study reader. */

/**
 * A `Uint8Array` may be a view into a larger buffer; a `Blob` — and a typed array constructed over a buffer —
 * must be given only its own bytes.
 *
 * ⚠️ It lived privately in `decoders/dicom.ts` and is now shared with `dicom/study.ts`, which is the whole
 * reason it moved: handing `bytes.buffer` to a `Blob` without this slice attaches the **entire file** to the
 * blob, so a 40 Mo study's single JPEG fragment becomes a 40 Mo blob that decodes to garbage.
 */
export function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer
}

/**
 * Builds `radio-jpeg-encapsule.dcm` — a DICOM whose pixel data is an ordinary JPEG.
 *
 * ⚠️ **Constructed here rather than downloaded, and that is stated on the tin.** The decoder has a branch for
 * encapsulated JPEG Baseline (`1.2.840.10008.1.2.4.50`), where the fragment *is* a JPEG file and the browser
 * decodes it natively — and none of the public DICOM test sets that are actually reachable carry one. The two
 * that were reachable are JPEG **Extended** at 12 bits, which no browser decodes, so they exercise the failure
 * path and not the success path. This is a minimal, standards-shaped Part 10 file: a preamble, a file-meta
 * group naming the transfer syntax, the image-pixel tags, and the pixel data as one encapsulated fragment.
 *
 * It is a fixture, not a scanner export: the picture inside it is generated, and no patient is involved.
 */
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const DIR = path.dirname(fileURLToPath(import.meta.url))
const sharp = (await import(
  pathToFileURL(path.join(DIR, '..', '..', 'web', 'node_modules', 'sharp', 'dist', 'index.cjs')).href
)).default

const W = 1200
const H = 900

// A greyscale field with an arch, so a wrong window or a missed inversion is visible at a glance.
const raw = Buffer.alloc(W * H)
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    const arch = Math.abs(y - (H * 0.4 + Math.sin((x / W) * Math.PI) * H * 0.3))
    raw[y * W + x] = Math.max(0, Math.min(255, 245 - arch * 1.1 + ((x * 5 + y * 11) % 13)))
  }
}

const jpeg = await sharp(raw, { raw: { width: W, height: H, channels: 1 } })
  .jpeg({ quality: 88 })
  .toBuffer()

// ── DICOM Part 10 assembly, Explicit VR Little Endian ────────────────────────────────────────────────────

/** A short-form explicit-VR element: tag, two-letter VR, 16-bit length, value. */
function element(group, el, vr, value) {
  const padded = value.length % 2 === 0 ? value : Buffer.concat([value, Buffer.from([vr === 'UI' ? 0x00 : 0x20])])
  const head = Buffer.alloc(8)
  head.writeUInt16LE(group, 0)
  head.writeUInt16LE(el, 2)
  head.write(vr, 4, 'latin1')
  head.writeUInt16LE(padded.length, 6)
  return Buffer.concat([head, padded])
}

const ui = (s) => Buffer.from(s, 'latin1')
const cs = (s) => Buffer.from(s, 'latin1')
const us = (n) => { const b = Buffer.alloc(2); b.writeUInt16LE(n, 0); return b }

const TRANSFER_SYNTAX = '1.2.840.10008.1.2.4.50' // JPEG Baseline (Process 1)

const meta = Buffer.concat([
  element(0x0002, 0x0002, 'UI', ui('1.2.840.10008.5.1.4.1.1.7')), // Secondary Capture Image Storage
  element(0x0002, 0x0003, 'UI', ui('1.2.826.0.1.3680043.8.498.1')),
  element(0x0002, 0x0010, 'UI', ui(TRANSFER_SYNTAX)),
])

// (0002,0000) is the byte count of everything after it in group 2 — dicom-parser reads it to find the dataset.
const metaLength = Buffer.alloc(12)
metaLength.writeUInt16LE(0x0002, 0)
metaLength.writeUInt16LE(0x0000, 2)
metaLength.write('UL', 4, 'latin1')
metaLength.writeUInt16LE(4, 6)
metaLength.writeUInt32LE(meta.length, 8)

const dataset = Buffer.concat([
  element(0x0008, 0x0060, 'CS', cs('OT')), // Modality
  element(0x0028, 0x0002, 'US', us(1)), // SamplesPerPixel
  element(0x0028, 0x0004, 'CS', cs('MONOCHROME2')),
  element(0x0028, 0x0010, 'US', us(H)), // Rows
  element(0x0028, 0x0011, 'US', us(W)), // Columns
  element(0x0028, 0x0100, 'US', us(8)), // BitsAllocated
  element(0x0028, 0x0101, 'US', us(8)), // BitsStored
  element(0x0028, 0x0102, 'US', us(7)), // HighBit
  element(0x0028, 0x0103, 'US', us(0)), // PixelRepresentation
])

/** An encapsulated pixel-data element: OB with undefined length, an empty offset table, one fragment, a delimiter. */
function encapsulated(frame) {
  const body = frame.length % 2 === 0 ? frame : Buffer.concat([frame, Buffer.from([0x00])])

  const head = Buffer.alloc(12)
  head.writeUInt16LE(0x7fe0, 0)
  head.writeUInt16LE(0x0010, 2)
  head.write('OB', 4, 'latin1')
  head.writeUInt16LE(0, 6) // reserved
  head.writeUInt32LE(0xffffffff, 8) // undefined length

  const item = (length) => {
    const b = Buffer.alloc(8)
    b.writeUInt16LE(0xfffe, 0)
    b.writeUInt16LE(0xe000, 2)
    b.writeUInt32LE(length, 4)
    return b
  }

  const delimiter = Buffer.alloc(8)
  delimiter.writeUInt16LE(0xfffe, 0)
  delimiter.writeUInt16LE(0xe0dd, 2)
  delimiter.writeUInt32LE(0, 4)

  return Buffer.concat([head, item(0), item(body.length), body, delimiter])
}

const file = Buffer.concat([
  Buffer.alloc(128), // preamble
  Buffer.from('DICM', 'latin1'),
  metaLength,
  meta,
  dataset,
  encapsulated(jpeg),
])

const out = path.join(DIR, 'radio-jpeg-encapsule.dcm')
fs.writeFileSync(out, file)
console.log('wrote', path.basename(out), (file.length / 1024).toFixed(0), 'Ko —', `${W}x${H}`, TRANSFER_SYNTAX)

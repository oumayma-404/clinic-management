/**
 * Turning decoded pixels into something an `<img>` can take, with the browser's own limits respected.
 *
 * ⚠️ **A canvas has a maximum area, and exceeding it fails silently.** Chrome caps a 2D canvas at roughly
 * 268 megapixels and simply produces a blank one past it — no exception, no console message, a white rectangle
 * where the radiograph was. A stitched panoramique or a full-mouth TIFF montage reaches that, so the pixels go
 * through an `ImageBitmap` (which has no such cap) and the only canvas ever created is the one at the size
 * {@link fitWithin} allows.
 */

/** A decoded image, ready for an `<img>`. `width`/`height` are the drawn size, after {@link fitWithin}. */
export interface DecodedImage {
  blob: Blob
  width: number
  height: number
  /** How many images the container held. A full-mouth series arrives as one multi-page TIFF. */
  pages: number
  /**
   * A sentence the viewer must show beside the picture, when the decode makes a claim the reader has to know
   * about. Absent for a format whose rendering is simply the file.
   *
   * ⚠️ **It exists for DICOM, where it is not decoration.** Turning sensor readings into 256 greys means
   * choosing a window, and a picture produced that way can be *misleading* rather than merely approximate — so
   * every DICOM preview says so, and the original stays one click away.
   */
  advisory?: string
}

/**
 * The longest edge a decoded image is drawn at.
 *
 * ⚠️ **It was 8192, chosen as « comfortably inside the canvas limit », and that was the wrong question.** The
 * viewer is a dialog about 1000 px wide; 2560 covers it at 2× on the largest screen anybody opens this on, and
 * the difference is measurable rather than theoretical — on a 51 Mpx HEIF, encoding at 8192 took **1171 ms and
 * produced a 8,9 Mo blob**, against **91 ms and 1,4 Mo** at 2560, for a picture nobody can tell apart in a
 * 1000 px box. The full-resolution original is one click away on every one of these files.
 */
const MAX_EDGE = 2560

/** Well inside Chrome's ~268 Mpx area cap, with room for the decode that precedes it. */
const MAX_PIXELS = 60 * 1000 * 1000

/**
 * The size to draw at: the source's own, unless it would exceed what a canvas can hold. Aspect ratio is kept,
 * so nothing is ever distorted to fit.
 */
export function fitWithin(width: number, height: number): { width: number; height: number } {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return { width: 0, height: 0 }
  }

  const scale = Math.min(1, MAX_EDGE / Math.max(width, height), Math.sqrt(MAX_PIXELS / (width * height)))
  if (scale >= 1) return { width: Math.round(width), height: Math.round(height) }

  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  }
}

/**
 * RGBA pixels as an encoded image, or null when they cannot be drawn.
 *
 * ⚠️ **JPEG at high quality, not PNG**, and the reason is time rather than bytes: encoding an eight-megapixel
 * radiograph as PNG takes seconds on a clinic's machine and blocks the tab for all of them. This is a viewer,
 * the original is one click away, and 0.95 is past the point where a greyscale radiograph shows the difference.
 */
export async function encodeRgba(
  rgba: Uint8Array | Uint8ClampedArray,
  width: number,
  height: number,
): Promise<Blob | null> {
  const fitted = fitWithin(width, height)
  if (fitted.width === 0) return null
  if (rgba.byteLength < width * height * 4) return null

  // A view over the decoder's own buffer rather than a copy — an eight-megapixel radiograph is 32 Mo, and
  // duplicating it to hand it to `ImageData` doubles the peak for nothing. The cast is because a typed array's
  // `buffer` is `ArrayBufferLike`, i.e. possibly shared; nothing here ever runs on a `SharedArrayBuffer`.
  const pixels = new ImageData(
    new Uint8ClampedArray(rgba.buffer as ArrayBuffer, rgba.byteOffset, width * height * 4),
    width,
    height,
  )

  // ⚠️ `resizeWidth`/`resizeHeight` are a hint, not a contract — Safari ignored them until 17. The canvas is
  // sized from `fitted` and `drawImage` is given explicit destination dimensions, so the result is right at the
  // requested size whether or not the bitmap arrived already scaled.
  const bitmap = await createImageBitmap(pixels, {
    resizeWidth: fitted.width,
    resizeHeight: fitted.height,
    resizeQuality: 'high',
  })

  try {
    return await encodeBitmap(bitmap, fitted.width, fitted.height)
  } finally {
    bitmap.close()
  }
}

/** Draws a bitmap at the given size and encodes it. The caller owns closing the bitmap. */
export async function encodeBitmap(
  bitmap: ImageBitmap,
  width: number,
  height: number,
): Promise<Blob | null> {
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height

  const context = canvas.getContext('2d')
  if (!context) return null
  context.imageSmoothingQuality = 'high'
  context.drawImage(bitmap, 0, 0, width, height)

  return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.95))
}

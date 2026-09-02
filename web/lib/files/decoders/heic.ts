/**
 * HEIC/HEIF — what an iPhone produces, and what no desktop browser paints.
 *
 * ⚠️ **This is the format the drawer most often holds and least often showed.** A dentist photographing a case
 * on an iPhone gets `.heic` by default; the catalog has accepted them since AC-3.1, and until this decoder they
 * arrived, stored correctly, and rendered as a grey icon on every machine in the practice.
 *
 * ⚠️ **`heic-to/csp` is the import, never bare `heic-to`.** The default build evaluates a string as JavaScript,
 * which this deployment's `script-src` (no `'unsafe-eval'`) refuses. The `/csp` entry point is the same API
 * built without it. Both still run libheif inside a `blob:` worker, which is why
 * `SecurityHeadersMiddleware` and the Caddyfile carry `worker-src 'self' blob:` — see the note there.
 *
 * The module is ~3 Mo of compiled libheif, so it is behind a dynamic `import()`: only a person who opens a HEIC
 * ever downloads it.
 */
import type { DecodedImage } from './raster'

/** The catalog caps a HEIC at 25 Mo; this is headroom over that, not a second policy. */
const MAX_BYTES = 64 * 1024 * 1024

/**
 * The image as a JPEG, or null when it cannot be decoded. ⚠️ **Null is an ordinary answer** — a `.heic`
 * containing a burst, a depth map or a format libheif was not built for is a file the viewer declines to show,
 * not a failure worth a toast.
 */
export async function decodeHeic(source: Blob): Promise<DecodedImage | null> {
  if (source.size <= 0 || source.size > MAX_BYTES) return null

  try {
    const { heicTo } = await import('heic-to/csp')

    // `type: "bitmap"` rather than a MIME type: it hands back the dimensions the dialog wants without a second
    // decode, and re-encoding is one `drawImage` away.
    const bitmap = await heicTo({ blob: source, type: 'bitmap' })

    try {
      const { fitWithin, encodeBitmap } = await import('./raster')
      const fitted = fitWithin(bitmap.width, bitmap.height)
      if (fitted.width === 0) return null

      const blob = await encodeBitmap(bitmap, fitted.width, fitted.height)
      return blob ? { blob, width: fitted.width, height: fitted.height, pages: 1 } : null
    } finally {
      bitmap.close()
    }
  } catch {
    return null
  }
}

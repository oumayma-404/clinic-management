/**
 * The formats this build can show that no browser paints on its own.
 *
 * <p>Before this existed, « le navigateur ne sait pas l'afficher » was the end of the conversation: a HEIC from
 * an iPhone, a TIFF from a scanner and a laboratory's ZIP all reached the drawer, stored correctly, and showed
 * a grey icon. That is the complaint this module answers — a file you cannot look at is a file you have no
 * reason to have uploaded.</p>
 *
 * <p>⚠️ <b>This is not a second copy of the server's list, and the distinction matters.</b>
 * `file-kind.ts` carries a standing rule that whether a format is previewable is the <i>server's</i> answer
 * (AC-5.2), and it still is: `FileTypeEntry.IsBrowserPreviewable` says whether a <b>browser</b> paints the
 * format unaided, which is a fact about the format and is unchanged by anything here. What a <i>build</i>
 * ships a decoder for is a different fact, and it is one the server cannot know — it is a property of this
 * bundle's module graph. The two are unioned at the point of use and never compared, so neither can drift into
 * disagreeing with the other. `check:responsive`'s `decoder-extensions-are-in-the-catalog` holds the one thing
 * that <i>could</i> go wrong: a typo naming an extension the catalog never accepts.</p>
 *
 * <p>⚠️ <b>Every decoder is behind a dynamic `import()`.</b> libheif alone is about 3 Mo; loading it for
 * everybody so that the occasional iPhone photo opens would be a tax on every page in the app. Nothing here is
 * fetched until somebody opens a file of that format.</p>
 *
 * <p>⚠️ <b>A decoder may return a picture AND a warning about it.</b> DICOM is the reason: a radiograph only
 * becomes 256 greys once somebody chooses a window, so the result carries an `advisory` the viewer is obliged to
 * show. A format whose rendering is simply the file carries none.</p>
 */
import type { DecodedImage } from './raster'
import type { ArchiveListing } from './zip'

export type { DecodedImage } from './raster'
export type { ArchiveEntry, ArchiveListing } from './zip'
export { DICOM_ADVISORY } from './advisory'

/** What kind of answer a format yields. An archive has no picture — its content *is* a list. */
export type DecoderKind = 'heic' | 'tiff' | 'dicom' | 'archive'

export type DecodedContent =
  | ({ kind: 'image' } & DecodedImage)
  | ({ kind: 'archive' } & ArchiveListing)

/**
 * Lower-case, dot-less extensions to the decoder that handles them.
 *
 * ⚠️ **`3mf`, `docx` and the OpenDocument formats are ZIPs and are deliberately absent.** Listing the XML parts
 * inside a `.docx` tells a dentist nothing; a laboratory's `.zip` of scans and prescriptions is the only one
 * whose contents are the point.
 */
const DECODERS: Readonly<Record<string, DecoderKind>> = {
  heic: 'heic',
  heif: 'heic',
  tiff: 'tiff',
  tif: 'tiff',
  dcm: 'dicom',
  dicom: 'dicom',
  zip: 'archive',
}

/** Every extension this build can decode — the set `check:responsive` checks against the catalog. */
export const DECODER_EXTENSIONS: readonly string[] = Object.keys(DECODERS)

/** The extension of a file name, lower-cased and without its dot; empty when it carries none. */
function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  if (dot <= 0 || dot === fileName.length - 1) return ''
  return fileName.slice(dot + 1).toLowerCase()
}

/** Which decoder handles this file, or null when the app has none for it. */
export function decoderFor(fileName: string): DecoderKind | null {
  return DECODERS[extensionOf(fileName)] ?? null
}

/** Whether this build can show `fileName` at all, ignoring what the browser can do unaided. */
export function hasDecoder(fileName: string): boolean {
  return decoderFor(fileName) !== null
}

/** Whether the decoder for this file produces a picture (as opposed to a listing, or nothing). */
export function decodesToImage(fileName: string): boolean {
  const kind = decoderFor(fileName)
  return kind === 'heic' || kind === 'tiff' || kind === 'dicom'
}

/**
 * The file, decoded into whatever this build can show of it.
 *
 * ⚠️ **Null is an ordinary answer** and is never an error: a format with no decoder, a file past a decoder's
 * size ceiling, a corrupt container, a `.heic` holding something libheif was not built for. Every one of those
 * means « show the placeholder », so nothing here throws and the caller has one branch, not five.
 */
export async function decodeForViewing(source: Blob, fileName: string): Promise<DecodedContent | null> {
  switch (decoderFor(fileName)) {
    case 'heic': {
      const { decodeHeic } = await import('./heic')
      const image = await decodeHeic(source)
      return image ? { kind: 'image', ...image } : null
    }
    case 'tiff': {
      const { decodeTiff } = await import('./tiff')
      const image = await decodeTiff(source)
      return image ? { kind: 'image', ...image } : null
    }
    case 'dicom': {
      const { decodeDicom } = await import('./dicom')
      const image = await decodeDicom(source)
      return image ? { kind: 'image', ...image } : null
    }
    case 'archive': {
      const { readArchiveListing } = await import('./zip')
      const listing = await readArchiveListing(source)
      return listing ? { kind: 'archive', ...listing } : null
    }
    default:
      return null
  }
}

/**
 * Just the picture, for the paths that only want one — building the stand-in image an upload carries with it.
 * An archive yields null here, because a list is not a thumbnail.
 */
export async function decodeToImage(source: Blob, fileName: string): Promise<Blob | null> {
  if (!decodesToImage(fileName)) return null

  const decoded = await decodeForViewing(source, fileName)
  return decoded?.kind === 'image' ? decoded.blob : null
}

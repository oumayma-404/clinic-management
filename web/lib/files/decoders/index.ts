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
export { DICOM_ADVISORY, DICOM_RENDERED_VALUES_NOTE, DICOM_VIEWER_ADVISORY } from './advisory'

/** What kind of answer a format yields. An archive has no picture — its content *is* a list. */
export type DecoderKind = 'heic' | 'tiff' | 'dicom' | 'archive' | 'mesh'

export type DecodedContent =
  | ({ kind: 'image' } & DecodedImage)
  | ({ kind: 'archive' } & ArchiveListing)

/**
 * Lower-case, dot-less extensions to the decoder that handles them.
 *
 * ⚠️ **`3mf`, `docx` and the OpenDocument formats are ZIPs and are deliberately absent.** Listing the XML parts
 * inside a `.docx` tells a dentist nothing; a laboratory's `.zip` of scans and prescriptions is the only one
 * whose contents are the point.
 *
 * ⚠️ **`3mf` stays absent now that meshes decode, and that is a decision rather than an oversight.** It is a
 * ZIP of XML rather than a mesh container, so it needs the archive to be opened before anything can be parsed
 * out of it — a different piece of work from the three below, for the format a dental scanner is least likely
 * to write. It remains uploadable, storable and downloadable exactly as it was.
 */
const DECODERS: Readonly<Record<string, DecoderKind>> = {
  heic: 'heic',
  heif: 'heic',
  tiff: 'tiff',
  tif: 'tiff',
  dcm: 'dicom',
  dicom: 'dicom',
  zip: 'archive',
  stl: 'mesh',
  ply: 'mesh',
  obj: 'mesh',
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
  return kind === 'heic' || kind === 'tiff' || kind === 'dicom' || kind === 'mesh'
}

/**
 * Whether this file has a viewer of its own that goes beyond the still picture {@link decodeForViewing} makes.
 *
 * ⚠️ **Asked by the preview dialog to decide which « Visionneuse » button to offer**, and it is deliberately a
 * question about the *file* rather than a pair of `decoderFor(...) === ...` tests written out at the call site
 * — which is how the DICOM check started, and how a third viewer would have quietly not been offered.
 */
export function interactiveViewerFor(fileName: string): 'dicom' | 'mesh' | null {
  const kind = decoderFor(fileName)
  return kind === 'dicom' || kind === 'mesh' ? kind : null
}

/**
 * ⚠️ **Above this, a format that has its own viewer is not decoded merely to fill the preview dialog.**
 *
 * <p>The dialog is a *browsing* surface — the arrows walk the whole drawer — so whatever it does automatically,
 * it does for every file somebody arrows past. Producing a still picture of a 150 Mo model means pulling
 * 150 Mo across a clinic's uplink, and the reader who actually wants to see it is one tap from a viewer that
 * will fetch exactly the same bytes deliberately, say so while it does, and give them something better than a
 * still at the end of it.</p>
 *
 * <p>24 Mo is about the largest file worth spending unasked: a scan of a single quadrant, an ordinary
 * radiograph. It is well under the 150 Mo hosted line the catalogue allows, which is the point.</p>
 */
export const AUTO_DECODE_MAX_BYTES = 24 * 1024 * 1024

/**
 * Whether the preview dialog should decode this file **without being asked**.
 *
 * ⚠️ **The size only matters for a format that has somewhere better to send the reader.** A HEIC is slow to
 * decode and has no viewer of its own, so refusing to decode it automatically would leave nothing at all on
 * screen; a `.stl` or a `.dcm` refused here still gets its « Visionneuse » button, which is a better answer
 * than the still it replaces. This is why the rule is one predicate and not a size check at a call site: the
 * two halves are only correct together.
 */
export function decodesWithoutAsking(fileName: string, sizeBytes: number): boolean {
  // ⚠️ A format with no decoder is not this rule's business, and answering `false` for one would have stopped
  // every ordinary PNG from being shown. The rule is about *decoder* work, not about size in general.
  if (!decoderFor(fileName)) return true
  if (!interactiveViewerFor(fileName)) return true
  return sizeBytes <= AUTO_DECODE_MAX_BYTES
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
    case 'mesh': {
      // ⚠️ A *rendered* picture, not a decoded one: there is no image inside an STL to extract. The still frame
      // is what a tile and the preview dialog show, and the interactive viewer is one button away — the same
      // arrangement a DICOM's flattened stand-in has, for the same reason.
      const { renderMeshThumbnail } = await import('../mesh/thumbnail')
      const image = await renderMeshThumbnail(source, fileName)
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

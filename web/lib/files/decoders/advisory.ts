/**
 * What the viewer is obliged to say about a picture this build produced rather than merely displayed.
 *
 * ⚠️ **Its own module because two paths need the same sentence and neither may own it.** The decoder produces it
 * when it decodes; the viewer needs it again on the **fast path**, where a DICOM's stored stand-in is painted
 * without the decoder ever loading — and that stand-in was made by exactly the same windowing, so it carries
 * exactly the same caveat. A copy in each would be two wordings of one clinical warning.
 */

/**
 * ⚠️ **Not decoration, and not a disclaimer for its own sake.** A DICOM stores sensor readings, not
 * brightnesses; turning them into 256 greys means choosing which slice of the range to show. The file usually
 * says which, and when it does not the decoder derives one from the frame's own range. Either way the result
 * can be *misleading* rather than merely approximate — a lesion outside the chosen window is simply not in the
 * picture — so this appears under every DICOM the viewer draws, and the original is always one click away.
 */
const BASE =
  'Aperçu non diagnostique : le contraste est approximatif et l’image est réduite. ' +
  'Téléchargez l’original pour l’interpréter.'

/**
 * What the **fast path** says — the stand-in is on screen and nothing has parsed the file, so the frame count is
 * genuinely unknown.
 *
 * ⚠️ **The hedge is the honest part, and its absence was a real gap.** A study of sixteen slices and a single
 * radiograph produce the same stand-in, and the fast path is the *normal* path — so saying nothing about frames
 * left a reader looking at one slice of a CBCT with no reason to suspect there were fifteen more. Stating the
 * uncertainty is worth more than silence when the consequence is that scale. Found by uploading a real
 * sixteen-frame study and reading what the strip actually said.
 */
export const DICOM_ADVISORY =
  `${BASE} Si l’étude contient plusieurs images, seule la première est affichée.`

/**
 * What the **decoder** says, which knows. A single-frame file gets no clause at all: having parsed it, there is
 * nothing to hedge about and « seule la première » would invent images that do not exist.
 */
export function dicomAdvisoryFor(frames: number): string {
  return frames > 1
    ? `${BASE} Cette étude contient ${frames} images ; seule la première est affichée.`
    : BASE
}

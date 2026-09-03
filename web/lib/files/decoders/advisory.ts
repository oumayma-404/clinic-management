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
export const DICOM_ADVISORY =
  'Aperçu non diagnostique : le contraste est approximatif et l’image est réduite. ' +
  'Téléchargez l’original pour l’interpréter.'

/** The same sentence, naming how much of the study is not on screen. */
export function dicomAdvisoryFor(frames: number): string {
  return frames > 1
    ? `${DICOM_ADVISORY} Cette étude contient ${frames} images ; seule la première est affichée.`
    : DICOM_ADVISORY
}

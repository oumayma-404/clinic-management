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

/**
 * What the **interactive viewer** says, and it is a stronger claim than the flattened preview's — not a weaker
 * one.
 *
 * ⚠️ **Choosing the window yourself makes the caveat more necessary, not less.** The flattened stand-in at
 * least used the window the file declared; in the viewer the operator has moved it, so what is on screen is a
 * slice of the range *they* picked and a structure outside it is not dim — it is **absent**. « I looked and saw
 * nothing » is therefore not a finding here. The second half is the other half of the same honesty: a practice
 * monitor is not a calibrated diagnostic display, whatever the window is set to.
 */
export const DICOM_VIEWER_ADVISORY =
  'Affichage non diagnostique : vous réglez le contraste, et une structure hors de la fenêtre choisie ' +
  'n’apparaît pas du tout. Écran non calibré — téléchargez l’original pour interpréter.'

/**
 * Why the window controls are inert on some files.
 *
 * ⚠️ **It is not a limitation of this build, and it does not mean the controls are inert.** An
 * encapsulated-JPEG DICOM holds 8-bit output the exporting device already windowed; the sensor readings are
 * not in the file at all, so re-applying the file's declared window over them would apply it a second time —
 * which is why `DicomStudy.declaredWindows` is empty on that path. What the window controls then adjust is
 * that already-rendered picture: a real and useful thing to do, and **not** the DICOM VOI transform. The
 * sentence has to say which, or it contradicts a control the reader can plainly still use — measured in the
 * browser, `radio-jpeg-encapsule.dcm` renders and its window control is live, while an earlier wording of
 * this line claimed there was nothing to window at all.
 */
export const DICOM_RENDERED_VALUES_NOTE =
  'Ce fichier est compressé en JPEG : l’appareil a déjà choisi son contraste, et le réglage agit sur cette ' +
  'image-là, pas sur les valeurs du capteur.'

/**
 * DICOM, flattened to one picture — the stand-in a drawer shows and the thumbnail an upload carries.
 *
 * ⚠️ **This file used to BE the DICOM implementation and is now a consumer of it.** Reading the pixels,
 * rescaling them, choosing a window and inverting `MONOCHROME1` moved to `lib/files/dicom/` when the
 * interactive viewer needed the same values with the window *not* yet applied. Leaving a copy here is exactly
 * the defect shape this repo keeps finding — a correct rule wired to one call site — and the consequence for
 * this particular rule is a radiograph rendered as its own negative, which reads as a *finding* rather than as
 * a bug. So: {@link openDicomStudy} for the values, `dicom/window.ts` for the window, and the flattening below.
 *
 * ⚠️ **What this produces is the FIRST frame only, and the advisory says so.** A picture is what a drawer tile
 * and the preview dialog need; the whole study is what `components/patients/files/dicom-viewer.tsx` opens.
 */
import { dicomAdvisoryFor } from './advisory'
import { encodeRgba, fitWithin, type DecodedImage } from './raster'
import { openDicomStudy } from '../dicom/study'
import { defaultWindowFor, frameStats, renderFrame } from '../dicom/window'

/**
 * The first frame of a DICOM, as a JPEG. Null covers every ordinary refusal — too large, not a DICOM, a codec
 * this build does not carry, a truncated file — so nothing here throws at the caller.
 *
 * ⚠️ The **same** default window as the interactive viewer's first paint (`defaultWindowFor`), deliberately:
 * opening the viewer on a file must not appear to change the image before anybody has touched a control.
 */
export async function decodeDicom(source: Blob): Promise<DecodedImage | null> {
  const opened = await openDicomStudy(source)
  if (!opened.ok) return null

  const study = opened.study
  try {
    const frame = await study.frame(0)
    if (!frame) return null

    const pixels = study.rows * study.columns
    const window =
      frame.kind === 'grey'
        ? defaultWindowFor(study, frame, frameStats(study, frame))
        : { centre: 128, width: 256 }

    const rgba = renderFrame(study, frame, window, false, pixels)
    const blob = await encodeRgba(rgba, study.columns, study.rows)
    if (!blob) return null

    const fitted = fitWithin(study.columns, study.rows)
    return {
      blob,
      width: fitted.width,
      height: fitted.height,
      pages: study.frameCount,
      advisory: dicomAdvisoryFor(study.frameCount),
    }
  } catch {
    return null
  } finally {
    study.release()
  }
}

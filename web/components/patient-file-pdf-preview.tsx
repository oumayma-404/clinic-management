"use client"

import { ExternalLink } from "lucide-react"
import { Button } from "@/components/ui/button"

/**
 * A patient file's PDF preview — a frame on a mouse, the file itself on a finger (AC-6, AC-7).
 *
 * <h4>Why this is a component and not two inline blocks</h4>
 * <p>The patient record (`app/patients/[id]/page.tsx`) and the files manager
 * (`components/patient-files-manager.tsx`) each render the same preview dialog, and their own comments already
 * said « Kept in sync with the same dialog in … » — i.e. the duplication was known and being maintained by hand.
 * Phase 4 routes this exact surface through the native PDF viewer, so leaving two copies schedules that change to
 * land in one of them.</p>
 *
 * <h4>The two trees, and why CSS decides between them</h4>
 * <p>Below a coarse pointer an embedded PDF frame is not a preview:</p>
 * <ul>
 *   <li>The `#toolbar=0&navpanes=0` fragment both call sites used to pass is <b>Adobe/Chromium-only</b>. Android
 *       WebView ignores it and renders the frame <b>blank</b> — a white A4 rectangle with no error, which reads as
 *       a corrupted radiograph rather than as an unsupported viewer. The fragment is gone from here entirely
 *       (AC-6), on every pointer.</li>
 *   <li>iOS Safari renders an `<iframe>` of a `blob:` PDF as a single non-scrollable page, so a two-page bilan is
 *       half-readable at best.</li>
 * </ul>
 * <p>So on a finger the frame is replaced by the file: `onDeliver` runs the same delivery the dialog's own
 * « Télécharger » button runs, which ends in the OS share sheet or the platform viewer — somewhere the document can
 * actually be read, pinched and rotated.</p>
 *
 * <p>⚠️ <b>The choice is made in CSS, not from a `matchMedia` read.</b> `useMediaQuery` returns `false` on the
 * first client render by contract ("treat `false` as *not yet known*, never as *definitely a mouse*"), so a JS
 * branch would paint the frame for one frame on every phone and then swap it — and a swap here would tear down a
 * loaded PDF. Two trees behind the `coarse:` variant is also how the app already does table→cards.</p>
 *
 * <p>⚠️ <b>Nothing here may set `display` in an inline style.</b> The frame previously carried
 * `style={{ display: 'block' }}`, and an inline style beats a class — so `coarse:hidden` would have been silently
 * inert and both trees would render at once. Display lives in the class list for exactly that reason.</p>
 */

interface PatientFilePdfPreviewProps {
  /** The `blob:` URL the parent already holds in state and revokes on close. */
  previewUrl: string
  /** Used as the frame's accessible title, so a screen reader names the document rather than "iframe". */
  fileName: string
  /** The parent's own file delivery — the same one its « Télécharger » button calls. */
  onDeliver: () => void
}

export function PatientFilePdfPreview({ previewUrl, fileName, onDeliver }: PatientFilePdfPreviewProps) {
  return (
    <div className="flex min-h-full w-full items-start justify-center">
      {/* ⚠️ `calc(100vw-8rem)` is `md:`-only — a DESKTOP allowance. Unconditionally it clamped a 342px phone to
          262px, discarding 23% of the screen on the one surface (a panoramique) that wants every pixel. */}
      <div className="aspect-[210/297] w-full overflow-hidden rounded-lg bg-white shadow-2xl md:max-w-[calc(100vw-8rem)] dark:bg-slate-800">
        <iframe
          src={previewUrl}
          title={fileName}
          className="block h-full w-full border-0 coarse:hidden"
        />

        <div className="hidden h-full w-full flex-col items-center justify-center gap-3 p-6 text-center coarse:flex">
          <p className="font-medium text-foreground">Aperçu non disponible sur cet appareil</p>
          <p className="max-w-[42ch] text-sm text-muted-foreground">
            Les visionneuses PDF intégrées ne fonctionnent pas de façon fiable sur mobile. Ouvrez le document pour
            le consulter dans la visionneuse de votre appareil.
          </p>
          {/* 44px on a finger. Grown rather than overlaid with `.touch-target`: this is the panel's only control,
              and it is the whole reason the panel exists. */}
          <Button onClick={onDeliver} className="coarse:h-11">
            <ExternalLink className="me-2 h-4 w-4" />
            Ouvrir le document
          </Button>
        </div>
      </div>
    </div>
  )
}

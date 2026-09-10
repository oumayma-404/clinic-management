import { downloadBlob } from './download';

/**
 * **The one way a PDF this app rendered reaches a printer.**
 *
 * <h4>Why it exists</h4>
 * <p>The document editor's « Imprimer » used to clone its on-screen A4 `<Card>` into a blank window and inject
 * `document.querySelector('style')?.textContent` — the page's *first* `<style>` element, which in a Tailwind v4
 * app is not the stylesheet. So the print preview arrived essentially unstyled: no margins, a collapsed table,
 * labels at body size, everything hard against the left edge. Reported with a screenshot of `about:blank`.</p>
 *
 * <p>⚠️ It was also a **second renderer of a legal document**, which this codebase already refuses in as many
 * words: `DocumentPreviewDialog` frames the server-rendered PDF rather than re-render the editor's A4 block,
 * because « the bytes a pharmacist reads are the only honest preview ». A DOM clone cannot be that, however
 * carefully its CSS is copied — and copying the CSS is what nobody will maintain.</p>
 *
 * <h4>How it prints</h4>
 * <p>An **off-screen iframe** holding the real bytes, then `contentWindow.print()`. The frame is positioned
 * out of view rather than `display: none` — a frame with no box has no layout and browsers decline to print
 * it — and it is removed once the print dialog has been handed the document.</p>
 *
 * <p>⚠️ **On a coarse pointer it does not print at all, it delivers.** An embedded `blob:` PDF is blank in an
 * Android WebView and a single non-scrollable page in iOS Safari, and there is no `window.print()` to reach in
 * a WebView — the same rule `patient-file-pdf-preview.tsx` carries. `downloadBlob` hands the file to the OS,
 * whose viewer owns printing there. The check is a direct `matchMedia` at click time, deliberately not
 * `useMediaQuery`, which answers `false` on a first render by contract.</p>
 *
 * @returns how the document was dispatched, so a caller can word its own toast honestly.
 */
export async function printPdfBlob(blob: Blob, fileName: string): Promise<'printed' | 'delivered'> {
  if (typeof window === 'undefined') {
    return 'delivered';
  }

  // A finger has no print dialog to open; the platform viewer is the print route.
  if (window.matchMedia('(pointer: coarse)').matches) {
    await downloadBlob(blob, fileName);
    return 'delivered';
  }

  const url = URL.createObjectURL(blob);
  const frame = document.createElement('iframe');
  // Off screen with a real box, never `display: none` — see the note above.
  frame.style.position = 'fixed';
  frame.style.left = '-10000px';
  frame.style.top = '0';
  frame.style.width = '210mm';
  frame.style.height = '297mm';
  frame.style.border = '0';
  frame.setAttribute('aria-hidden', 'true');
  frame.title = fileName;

  const printed = await new Promise<boolean>((resolve) => {
    let settled = false;
    const finish = (ok: boolean) => {
      if (settled) return;
      settled = true;
      resolve(ok);
    };

    /*
     * ⚠️ **The deadline covers LOADING ONLY, and clearing it on load is not tidiness.** `print()` is
     * synchronous and does not return until the user dismisses the print dialog — so a deadline still armed
     * at that moment fires while they are choosing a printer, takes the `!printed` branch, and **downloads
     * the file behind their back**. Caught by this helper's own first run, where the dialog stayed open longer
     * than the timeout.
     */
    const deadline = window.setTimeout(() => finish(false), 5000);

    frame.onload = () => {
      const view = frame.contentWindow;
      if (!view) {
        finish(false);
        return;
      }
      // Loading succeeded; how long the dialog stays open is the user's business, not a failure.
      window.clearTimeout(deadline);
      try {
        view.focus();
        view.print();
        finish(true);
      } catch {
        // A browser that refuses to print an embedded PDF, rather than one that failed to load it.
        finish(false);
      }
    };
    frame.onerror = () => finish(false);

    document.body.appendChild(frame);
    frame.src = url;
  });

  /*
   * ⚠️ Removed on a delay, and the delay is load-bearing: `print()` returns as soon as the dialog is up, but
   * the dialog reads the document from the frame while it is open. Tearing the frame down synchronously
   * produces an empty print preview — the same shape as revoking a blob URL that is still being displayed.
   * 60 s is longer than anyone spends choosing a printer, and the frame costs nothing while it waits.
   */
  window.setTimeout(() => {
    frame.remove();
    URL.revokeObjectURL(url);
  }, 60_000);

  if (!printed) {
    await downloadBlob(blob, fileName);
    return 'delivered';
  }

  return 'printed';
}

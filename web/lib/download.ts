/**
 * How long a `blob:` URL stays alive after we hand it to the browser (AC-41).
 *
 * ⚠️ This file used to call `URL.revokeObjectURL(url)` **synchronously**, one line after `a.click()`. On a
 * desktop that happens to work — the anchor click starts the save before the next statement matters — but it
 * is a race, and it loses in the two cases that matter on a device:
 *
 * - **iOS Safari**, where a `blob:` download is handed to a viewer *asynchronously*. Revoking immediately
 *   invalidates the URL before the viewer reads it, so the receipt simply never arrives and nothing reports
 *   an error.
 * - **`window.open(url)`**, where the navigation has not begun when the next line runs, so the new tab opens
 *   on a dead URL.
 *
 * A minute is far longer than any viewer needs and still bounded, so a long session that downloads many
 * documents does not accumulate blobs for its whole life. The two preview flows that hold a URL in state and
 * revoke on close are the stricter precedent; this is the fire-and-forget equivalent.
 */
const REVOKE_DELAY_MS = 60_000;

/** A finger, not a mouse — the same rule the rest of the app uses for touch behaviour. */
function isCoarsePointer(): boolean {
  return typeof window !== "undefined" && window.matchMedia("(pointer: coarse)").matches;
}

/**
 * Deliver an in-memory blob to the device (AC-41).
 *
 * Three paths, because « télécharger » means three different things:
 *
 * 1. **Coarse pointer + Web Share that accepts files** → the OS share sheet. On iOS this is the only route
 *    that reliably lands a file somewhere the user chose (Fichiers, Mail, WhatsApp), and « Partager » is what
 *    a dentist handing a receipt to a patient actually wants.
 * 2. **Coarse pointer without file sharing** → open the blob in a new tab. ⚠️ `<a download>` is **ignored**
 *    for `blob:` URLs by iOS Safari, so the anchor route below silently does nothing there — the document
 *    never arrives and no error is raised. Opening it at least puts it on screen with the OS viewer's own
 *    save/share affordances.
 * 3. **Fine pointer** → the classic hidden anchor. Unchanged behaviour.
 *
 * Returns a promise so a caller *may* await the share sheet, but nothing has to: every path is safe to
 * fire-and-forget, which is how all existing call sites use it.
 */
export async function downloadBlob(blob: Blob, filename: string): Promise<void> {
  if (isCoarsePointer() && typeof navigator !== "undefined" && "canShare" in navigator) {
    const file = new File([blob], filename, { type: blob.type || "application/octet-stream" });
    // ⚠️ `canShare({ files })`, not merely the presence of `navigator.share` — Android Chrome exposes `share`
    // while refusing files on some versions, and calling it anyway throws *after* the user has tapped.
    if (navigator.canShare({ files: [file] })) {
      try {
        await navigator.share({ files: [file], title: filename });
        return;
      } catch (err) {
        // A dismissed share sheet is the user's decision, not a failure — do not fall through to a second
        // attempt that reopens something they just cancelled.
        if (err instanceof DOMException && err.name === "AbortError") return;
        // Anything else (an OS refusal, an unsupported type) falls through to the tab below.
      }
    }
  }

  const url = URL.createObjectURL(blob);
  const revokeLater = () => window.setTimeout(() => URL.revokeObjectURL(url), REVOKE_DELAY_MS);

  if (isCoarsePointer()) {
    window.open(url, "_blank", "noopener");
    revokeLater();
    return;
  }

  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  revokeLater();
}

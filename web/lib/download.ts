import { formatFileSize } from "@/lib/format";
import { showErrorToast } from "@/lib/errors";

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

/**
 * The default ceiling on a file crossing the shell bridge — 25 MB (AC-20).
 *
 * ⚠️ **A fallback, not the policy.** The shell states its own limit in `window.__clinicShell.maxFileBytes`,
 * because the constraint belongs to that platform's JS bridge and not to the web app: base64 inflates a file by
 * ~1.33×, so 25 MB arrives as a ~33 MB Java `String` in one allocation, and what a low-memory Android device
 * survives is a per-device fact only the shell can measure. This constant is what the web bundle uses when the
 * bridge does not say — never "unlimited", which would be an unbounded marshalling attempt on the one path that
 * cannot afford it.
 */
const SHELL_MAX_FILE_BYTES = 25 * 1024 * 1024;

/** A finger, not a mouse — the same rule the rest of the app uses for touch behaviour. */
function isCoarsePointer(): boolean {
  return typeof window !== "undefined" && window.matchMedia("(pointer: coarse)").matches;
}

/**
 * The native shell's bridge, or `undefined` in every browser.
 *
 * `typeof window` is guarded because this module is importable server-side — Next renders client components on
 * the server, and an unguarded `window` here would throw during SSR before any of it ran (LEARNINGS: "Guard
 * browser globals in any module importable server-side").
 */
function shellBridge(): ClinicShell | undefined {
  return typeof window !== "undefined" ? window.__clinicShell : undefined;
}

/**
 * The bridge, but only when it can actually receive a file.
 *
 * ⚠️ **A shell is not automatically a save route, and assuming it was broke every download in the Windows
 * app.** `clinic-file-vault` gave the desktop shell a `window.__clinicShell` for the first time — carrying
 * `version` and `platform` and nothing else, because `bridge.md` states plainly that the desktop needs no
 * `saveFile` (« a WebView2 download works ») and therefore no `maxFileBytes` to bound it. This function used to
 * branch on the bridge merely EXISTING, so on Windows every download took the mobile path: over 25 Mo it was
 * refused with a sentence about « l'application mobile », and under 25 Mo it called an undefined `saveFile`
 * and reported « Échec du téléchargement ». Both sizes, every file, on a platform that downloads natively.
 *
 * So the question is « can this bridge save? », never « is there a bridge? ». A shell without `saveFile` falls
 * through to the ordinary browser routes below, which is exactly what the contract says it should do.
 */
function shellThatCanSave(): ClinicShell | undefined {
  const shell = shellBridge();
  return typeof shell?.saveFile === "function" ? shell : undefined;
}

/** Base64 for the bridge — no `data:` prefix, which is what `bridge.md` specifies. */
async function toBase64(blob: Blob): Promise<string> {
  const buffer = await blob.arrayBuffer();
  const bytes = new Uint8Array(buffer);
  // Chunked, not `String.fromCharCode(...bytes)`: spreading megabytes into an argument list blows the call stack,
  // and it does so as a crash rather than as the refusal above — the exact "silent nothing" AC-20 removes.
  const CHUNK = 0x8000;
  let binary = "";
  for (let i = 0; i < bytes.length; i += CHUNK) {
    binary += String.fromCharCode(...bytes.subarray(i, i + CHUNK));
  }
  return btoa(binary);
}

/**
 * Deliver an in-memory blob to the device (AC-41, AC-19, AC-20).
 *
 * Four paths, because « télécharger » means four different things:
 *
 * 1. **A native shell** → hand it to the OS through the bridge, which writes it and offers to open or share it.
 *    First, and unconditionally first: inside a `WebView` a `blob:` download has nowhere to go and
 *    `navigator.share` does not exist, so every path below it silently delivers nothing there.
 * 2. **Coarse pointer + Web Share that accepts files** → the OS share sheet. On iOS this is the only route
 *    that reliably lands a file somewhere the user chose (Fichiers, Mail, WhatsApp), and « Partager » is what
 *    a dentist handing a receipt to a patient actually wants.
 * 3. **Coarse pointer without file sharing** → open the blob in a new tab. ⚠️ `<a download>` is **ignored**
 *    for `blob:` URLs by iOS Safari, so the anchor route below silently does nothing there — the document
 *    never arrives and no error is raised. Opening it at least puts it on screen with the OS viewer's own
 *    save/share affordances.
 * 4. **Fine pointer** → the classic hidden anchor. Unchanged behaviour.
 *
 * ⚠️ **The size refusal applies to the shell path only.** A browser has no base64 marshalling to run out of
 * memory on — it streams the blob to disk — so imposing a limit there would refuse a 40 MB panoramique that
 * downloads perfectly well today. That is a capability removed by a defensive check, which § 0 forbids.
 *
 * Returns a promise so a caller *may* await the share sheet, but nothing has to: every path is safe to
 * fire-and-forget, which is how all existing call sites use it.
 */
export async function downloadBlob(
  blob: Blob,
  filename: string,
  /**
   * Where the original already sits on this machine, when the caller knows — a coffre file does. Named in the
   * size refusal so « trop volumineux » ends with somewhere to go instead of a dead end: the bytes are on the
   * disk in front of the person reading it.
   */
  options: { savedAt?: string } = {},
): Promise<void> {
  const shell = shellThatCanSave();
  if (shell) {
    const limit = shell.maxFileBytes ?? SHELL_MAX_FILE_BYTES;
    // `blob.size` BEFORE the bytes are read (AC-20): `arrayBuffer()` + the base64 encode are where the memory is
    // actually spent, so a refusal after them is a refusal the device has already crashed past.
    if (blob.size > limit) {
      showErrorToast(null, {
        title: "Fichier trop volumineux",
        // ⚠️ « cette application » rather than « l'application mobile ». The sentence is reached from whatever
        // shell declared a `saveFile`, and naming the wrong device sends somebody looking for a phone they are
        // not holding — which is precisely how this was reported: « why application mobile ? I'm on the
        // desktop app ». What follows is advice the reader can act on from where they are.
        fallback:
          `Ce fichier fait ${formatFileSize(blob.size)} et cette application est limitée à ` +
          `${formatFileSize(limit)}. ` +
          (options.savedAt
            ? `L'original est déjà sur ce poste, dans ${options.savedAt}.`
            : "Ouvrez-le depuis un navigateur pour le télécharger."),
      });
      return;
    }

    try {
      await shell.saveFile(await toBase64(blob), filename, blob.type || "application/octet-stream");
      return;
    } catch {
      // Say so rather than falling through: inside a WebView the paths below cannot deliver either, so a
      // fall-through would turn a reported failure back into a silent one.
      showErrorToast(null, {
        title: "Échec du téléchargement",
        fallback: "Le fichier n'a pas pu être enregistré sur cet appareil. Réessayez.",
      });
      return;
    }
  }

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

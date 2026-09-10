import { downloadBlob } from "@/lib/download"
import { toE164 } from "@/lib/phone"
import { quoteFr } from "@/lib/format"

/**
 * The one place a `wa.me` URL is built (AC-3, and the spec's « one authority for the link »).
 *
 * <p>Five surfaces now offer « contacter par WhatsApp » — the fournisseurs list, the stock table, the stock
 * item's own row, the bell's « Stock faible » alert and the laboratory board — and every one of them needs the
 * same three decisions: is this number deliverable, how is it written into the URL, and does the link carry a
 * pre-filled message. Five copies is how one of them ships `+216 20 123 456` verbatim (spaces and a `+` in a
 * path segment), or opens in the same tab and takes the user out of the app.</p>
 *
 * <p><b>The number in a `wa.me` path is E.164 with no `+` and no separators.</b> That is the format's rule, not
 * a preference — `wa.me/+21620123456` resolves to a "phone number shared via url is invalid" page, which is
 * indistinguishable to the user from the supplier having given a wrong number.</p>
 */

/** The `rel` every external opener needs. `noopener` is the security half; `noreferrer` is the privacy half. */
export const EXTERNAL_LINK_REL = "noopener noreferrer"

/**
 * A `wa.me` link for `phone`, or `null` when no country can parse it.
 *
 * <p>⚠️ Any country works and always did — `wa.me/<digits>` is international by construction, so this function
 * needed no change when the phone rule widened. What changed is how often it returns a link: a supplier or
 * patient with a foreign number used to fall to the null branch on the *rule*, not on the format.</p>
 *
 * <p><b>Returning null rather than a best-effort link is the point.</b> AC-3 says a supplier with no usable
 * number gets « Ajouter un numéro » instead — never a disabled control and never a link that opens WhatsApp on
 * an error page. Callers branch on null to choose between the two.</p>
 *
 * <p>`text` is optional and is left out entirely when absent: a supplier's own row opens a conversation with no
 * pre-filled message (AC-3), while the « Stock faible » alert pre-fills the order (AC-6).</p>
 */
export function whatsAppUrl(phone: string | null | undefined, text?: string | null): string | null {
  const e164 = toE164(phone)
  if (!e164) return null

  // `wa.me/<digits>` — the leading `+` is dropped, which is what the format expects.
  const digits = e164.replace(/\D/g, "")
  const query = text && text.trim() ? `?text=${encodeURIComponent(text.trim())}` : ""
  return `https://wa.me/${digits}${query}`
}

/**
 * The French order message a « Stock faible » alert pre-fills (AC-6).
 *
 * <p>It names the article and its on-hand figure because that is the whole content of the alert the user is
 * acting on — a message reading « bonjour, il nous faut du stock » costs a round trip to say which. Built here
 * rather than at the call site so the bell row and any later surface that offers the same action word it
 * identically.</p>
 */
export function lowStockOrderMessage(itemName: string, currentStock: number, unit?: string | null): string {
  const quantity = unit ? `${currentStock} ${unit}` : `${currentStock}`
  return (
    `Bonjour, nous souhaitons commander ${quoteFr(itemName)}. ` +
    `Notre stock actuel est de ${quantity}. Pouvez-vous nous indiquer votre disponibilité et vos délais ? Merci.`
  )
}

/**
 * The same order message, built from a « Stock faible » alert's own sentence (AC-6).
 *
 * <p>The bell row does not carry the article and the figure as separate fields — it carries the rendered French
 * message, which already names both. Re-deriving them by parsing that sentence would be the
 * `Contains("déjà facturée")` defect; quoting it is exact by construction and cannot drift from what the person
 * is looking at when they tap.</p>
 */
export function lowStockOrderMessageFromAlert(alertMessage: string): string {
  return (
    `Bonjour, nous souhaitons passer commande. ${alertMessage.trim()} ` +
    `Pouvez-vous nous indiquer votre disponibilité et vos délais ? Merci.`
  )
}

/**
 * The French message the laboratory board pre-fills when chasing a bon de prothèse.
 *
 * <p>Names the patient and the work, which is what identifies the piece to a prothésiste — they hold several at
 * once and « où en est notre commande ? » identifies nothing.</p>
 */
export function labOrderFollowUpMessage(
  workDescription: string,
  patientName?: string | null,
  expectedDate?: string | null,
): string {
  const forPatient = patientName ? ` pour ${patientName}` : ""
  const due = expectedDate ? ` (prévu le ${expectedDate})` : ""
  return (
    `Bonjour, nous vous contactons au sujet du travail ${quoteFr(workDescription)}${forPatient}${due}. ` +
    `Pouvez-vous nous indiquer où il en est ? Merci.`
  )
}

/**
 * How a document reached WhatsApp, so a caller can word its toast honestly.
 *
 * ⚠️ **No URL can attach a file to a conversation, and that is why this is three outcomes rather than a
 * boolean.** `wa.me` carries text only — there is no parameter, on any platform, that attaches a document, and
 * WhatsApp will not add one because it would let any page push a file into a chat. The bytes reach WhatsApp
 * only through the **operating system's own share sheet**, where WhatsApp appears as a target.
 */
export type WhatsAppShareOutcome =
  /** The shell or the OS sheet took the file: the user picks WhatsApp and the document is attached. */
  | "shared"
  /** The file was delivered and the conversation opened; the user attaches it themselves. */
  | "delivered-and-opened"
  /** The file was delivered but the conversation could not be opened (a blocked pop-up). */
  | "delivered"

/**
 * Hand a PDF this app rendered to WhatsApp. The ladder mirrors {@link downloadBlob}'s, which is the same
 * problem for the same reasons:
 *
 * 1. **A native shell** → `saveFile`, whose open/share path *is* the platform sheet (`bridge.md`: « Write the
 *    file and offer to open or share it »).
 * 2. **`navigator.canShare({ files })`** → the share sheet with the PDF attached. True on Android Chrome and
 *    iOS Safari — the phone and the tablet this app is mostly used on — and sometimes on Windows Chrome.
 *    ⚠️ Asked as `canShare({ files })`, never « does `share` exist »: Android exposes `share` while refusing
 *    files on some versions, and it throws *after* the user has tapped.
 * 3. **Neither** (an ordinary desktop browser) → deliver the file **and** open the conversation with the
 *    message ready, so the two halves are one click apart. Reported as such rather than claimed as sent.
 *
 * ⚠️ **The number is a convenience, not a requirement**: `navigator.share` cannot choose the recipient — the
 * sheet does — so a patient with no phone on file still shares fine. Only branch 3 addresses the chat, and with
 * no usable number it opens WhatsApp on its contact picker rather than on an « invalid number » page.
 */
/** A finger, not a mouse — the same test `download.ts` uses, and for the reason documented below. */
function isCoarsePointer(): boolean {
  return typeof window !== "undefined" && window.matchMedia("(pointer: coarse)").matches
}

export async function shareDocumentToWhatsApp(
  blob: Blob,
  fileName: string,
  options: { phone?: string | null; message: string },
): Promise<WhatsAppShareOutcome> {
  if (typeof window === "undefined") return "delivered"

  const shell = window.__clinicShell
  if (typeof shell?.saveFile === "function") {
    await downloadBlob(blob, fileName)
    return "shared"
  }

  /*
   * ⚠️ **A COARSE pointer as well as `canShare`, and the pointer half is not belt-and-braces.** Measured on
   * desktop Chrome for Windows: `canShare({ files: [pdf] })` answers **true** and `navigator.share()` then
   * **never settles** — it neither resolves nor rejects — so this function would hang, its caller's toast would
   * spin on « Préparation… » for ever and the file would never be delivered. `downloadBlob` gates its own share
   * path the same way; this ladder was written without it and the probe caught it before it shipped.
   */
  if (isCoarsePointer() && typeof navigator !== "undefined" && "canShare" in navigator) {
    const file = new File([blob], fileName, { type: blob.type || "application/pdf" })
    if (navigator.canShare({ files: [file] })) {
      try {
        await navigator.share({ files: [file], text: options.message })
        return "shared"
      } catch (err) {
        // A dismissed sheet is the user's decision — never fall through and deliver a file they cancelled.
        if (err instanceof DOMException && err.name === "AbortError") return "shared"
        // Anything else (an OS refusal, a type it will not carry) falls to the desktop route.
      }
    }
  }

  await downloadBlob(blob, fileName)
  // ⚠️ Opened AFTER the delivery, so a blocked pop-up cannot cost the file.
  const url = whatsAppUrl(options.phone, options.message) ?? `https://wa.me/?text=${encodeURIComponent(options.message)}`
  const opened = window.open(url, "_blank", EXTERNAL_LINK_REL)
  return opened ? "delivered-and-opened" : "delivered"
}

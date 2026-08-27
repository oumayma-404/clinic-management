import { toE164Tunisian } from "@/lib/phone"
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
 * A `wa.me` link for `phone`, or `null` when it is not a deliverable Tunisian number.
 *
 * <p><b>Returning null rather than a best-effort link is the point.</b> AC-3 says a supplier with no usable
 * number gets « Ajouter un numéro » instead — never a disabled control and never a link that opens WhatsApp on
 * an error page. Callers branch on null to choose between the two.</p>
 *
 * <p>`text` is optional and is left out entirely when absent: a supplier's own row opens a conversation with no
 * pre-filled message (AC-3), while the « Stock faible » alert pre-fills the order (AC-6).</p>
 */
export function whatsAppUrl(phone: string | null | undefined, text?: string | null): string | null {
  const e164 = toE164Tunisian(phone)
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

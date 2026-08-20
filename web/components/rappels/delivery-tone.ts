import type { StatusTone } from "@/components/ui/status-tone"
import type { ReminderDeliveryStatus } from "@/lib/api/reminder-settings"

/**
 * **The one place a delivery status is given a colour**, for every surface on « Rappels » that carries one.
 *
 * <p>Four surfaces read this: the counter tiles (`reminder-counters.tsx`), the log's status pill and row stripe
 * and reason line (`reminder-log-table.tsx`), the toolbar's active-filter pill, and the empty states. They used
 * to hold <b>three parallel maps</b> — a class map, a stripe-colour map and a reason-colour map, plus a fourth
 * dot-colour map on the page itself — which is four chances for « bloqué » to mean amber in one place and
 * something else two components over.</p>
 *
 * <p>A status maps to a <b>tone</b>, and `ui/status-tone.ts` owns what a tone looks like. That is the app's one
 * status palette; « Rappels » no longer keeps a private copy of it.</p>
 *
 * <p>⚠️ <b>`pending` is azure, not amber</b>, and that is a deliberate change of meaning rather than a tidy-up.
 * « En attente » and « Bloqué » were both `bg-warning-wash`: two of the four counters were the same colour, so
 * the one distinction a reader actually needs — <i>nothing is expected of you</i> versus <i>a setting is waiting
 * for you</i> — was the one the palette threw away. Azure is `status-tone.ts`'s own `pending` tone, which every
 * other list in the app already uses for « booked, queued, nothing to do yet », so this brings the log <i>onto</i>
 * the shared palette rather than off it.</p>
 *
 * <p>⚠️ `blocked` stays amber (`active`) and never red: nothing failed, nothing was even attempted, and the
 * message still goes out once the channel works. Painting it beside real failures would bury the rows that need
 * a phone call.</p>
 */
export const DELIVERY_TONE: Record<ReminderDeliveryStatus, StatusTone> = {
  sent: "positive",
  pending: "pending",
  blocked: "active",
  failed: "negative",
}

/**
 * The status in words.
 *
 * <p>« Bloqué » and not « En attente »: the row is not queued behind others, it is not going anywhere until
 * somebody changes a setting. The reason printed beside it says which one.</p>
 */
export const DELIVERY_LABEL: Record<ReminderDeliveryStatus, string> = {
  sent: "Envoyé",
  pending: "En attente",
  blocked: "Bloqué",
  failed: "Échec",
}

/**
 * The plural label a **counter** and the **active-filter pill** wear — « Envoyés », not « Envoyé ».
 *
 * <p>Separate from `DELIVERY_LABEL` because a pill on one row states that row's status while a tile states a
 * count of them, and French does not let one string do both.</p>
 */
export const DELIVERY_LABEL_PLURAL: Record<ReminderDeliveryStatus, string> = {
  sent: "Envoyés",
  pending: "En attente",
  blocked: "Bloqués",
  failed: "Échecs",
}

/** `all` is « ne filtre rien », a first-class value rather than the absence of one. */
export type StatusFilter = ReminderDeliveryStatus | "all"

/** Narrow an untrusted string — a deep-link's `?status=` — to a real filter, or `null` to leave the filter alone. */
export function asStatusFilter(value: string | null): ReminderDeliveryStatus | null {
  return value === "sent" || value === "pending" || value === "failed" || value === "blocked" ? value : null
}

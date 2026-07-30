/**
 * The app's one status palette.
 *
 * <p>Four label modules — invoices, treatment plans, appointments, lab orders — each carried their own
 * `bg-X-100 text-X-800 dark:bg-X-950 dark:text-X-200` map. Two different greens and two different ambers meant the
 * same thing on different screens, none of them followed `globals.css`, and every one of them maintained dark mode
 * by hand. Now the semantic tokens exist (`--success` / `--warning` / `--destructive` and their `-wash` pairs), so
 * a status maps to a **tone** and the tone owns the classes.</p>
 *
 * <p>The tones are deliberately few. A status palette with a colour per status is a legend nobody learns; six tones
 * that each mean something — nothing to do, booked, agreed, happening, finished, failed — can be read without one.</p>
 */
export type StatusTone =
  /** Nothing is expected of anyone: a draft, an archived record, a cancelled visit. */
  | "neutral"
  /** Booked / queued — real but not yet agreed or begun. */
  | "pending"
  /** Agreed or accepted by the other party. Stronger than `pending`, still not an outcome. */
  | "accepted"
  /** Underway right now, or waiting on someone. Amber, because it is the tone that asks for attention. */
  | "active"
  /** A finished, successful outcome. */
  | "positive"
  /** Refused, failed, absent, rejected. */
  | "negative"

/**
 * ⚠️ `text-warning-ink`, not `text-warning`, on the amber wash: `--warning` sits at L 0.62, which lands near
 * 3.5:1 against its own wash — under the floor for badge-sized text. `--warning-ink` is the darkened step that
 * exists purely so this pairing is legible. `--success` and `--destructive` clear it at their normal step.
 */
export const STATUS_TONE_CLASS: Record<StatusTone, string> = {
  neutral: "bg-muted text-muted-foreground",
  pending: "bg-accent text-accent-foreground",
  accepted: "bg-primary/12 text-primary",
  active: "bg-warning-wash text-warning-ink",
  positive: "bg-success-wash text-success",
  negative: "bg-destructive-wash text-destructive",
}

/** Unknown tones fall back to `neutral` — a status we cannot classify must not claim to be good or bad news. */
export function statusToneClass(tone: StatusTone | undefined): string {
  return tone ? STATUS_TONE_CLASS[tone] : STATUS_TONE_CLASS.neutral
}

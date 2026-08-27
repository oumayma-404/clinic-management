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

/**
 * The same six tones as **ink alone**, with no wash behind them.
 *
 * <p>For text that must carry the tone without becoming a pill: the failure reason under a log row, or a figure
 * sitting on a surface that is <i>already</i> the right wash. `STATUS_TONE_CLASS` cannot serve there — it would
 * paint a second background over one that is already correct, and on a tinted tile that reads as a smudge.</p>
 *
 * <p>⚠️ `active` is `text-warning-ink`, not `text-warning`, for the reason stated above `STATUS_TONE_CLASS`:
 * `--warning` sits at L 0.62 and lands near 3.5:1 on any near-white ground — wash or card. The darkened step
 * exists for exactly this.</p>
 */
export const STATUS_TONE_INK: Record<StatusTone, string> = {
  neutral: "text-muted-foreground",
  pending: "text-accent-foreground",
  accepted: "text-primary",
  active: "text-warning-ink",
  positive: "text-success",
  negative: "text-destructive",
}

/**
 * The tone at **full strength, as a raw CSS colour** for an inline `backgroundColor` — a row's 2 px stripe, a
 * card's accent rail, a meter's fill. A hairline at wash strength is simply not there, which is why these are
 * the ink values rather than the `-wash` ones.
 *
 * <p>⚠️ <b>`--success`, never `--color-success`.</b> The `--color-` aliases are `@theme inline` entries, and
 * Tailwind v4 emits one to `:root` only when it judges it <i>used</i> — so which of them exist at runtime is a
 * property of the current build rather than of the stylesheet. A `var(--color-…)` in an inline style is
 * therefore a coin flip that re-flips whenever utility usage changes, and it paints <b>transparent</b> when it
 * loses, silently, because an unresolvable custom property is not an error. The raw tokens are declared
 * unconditionally on `:root` and again under `.dark`, so they always resolve <i>and</i> follow the theme.</p>
 */
export const STATUS_TONE_RAIL: Record<StatusTone, string> = {
  neutral: "var(--muted-foreground)",
  pending: "var(--primary)",
  accepted: "var(--primary)",
  active: "var(--warning)",
  positive: "var(--success)",
  negative: "var(--destructive)",
}

/**
 * The tone as a **ring colour**, for a surface that is *selected* rather than merely tinted.
 *
 * <p>Pair it with `ring-2 ring-inset` (the repo's idiom — see `appointment-calendar.tsx`). A ring colour on its
 * own paints nothing, so it is safe to apply unconditionally and toggle only the width.</p>
 *
 * <p>⚠️ Utilities, not raw tokens, unlike `STATUS_TONE_RAIL`: the `--color-` emission problem above affects
 * <i>inline styles only</i>. `ring-success` is generated from the `@theme` key and is always correct.</p>
 */
export const STATUS_TONE_RING: Record<StatusTone, string> = {
  neutral: "ring-border",
  pending: "ring-primary",
  accepted: "ring-primary",
  active: "ring-warning",
  positive: "ring-success",
  negative: "ring-destructive",
}

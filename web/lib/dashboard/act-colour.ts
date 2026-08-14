import type { CSSProperties } from 'react';

/**
 * How an act's catalogue colour is painted on the dashboard.
 *
 * <p><b>The pastel is derived, never a second list.</b> `Domain/ValueObjects/ColorHex` is the single authority on
 * what colour an act is — twelve hue families × three nuances, served named by
 * `GET /api/procedure-types/colors` and mirrored nowhere. A hand-written table of pastel equivalents here would
 * be a second answer to that question, and it would drift the first time somebody widens the palette. So each
 * surface takes the stored hex and mixes it toward the card, in the browser.</p>
 *
 * <p><b>Large areas are tinted; small marks keep the whole colour.</b> A ribbon block is wide enough that a
 * saturated fill dominates the page, while a 3 px row rail or an 8 px legend dot at 22 % is simply invisible.
 * That is the rule the two helpers below encode, and it is why there are two of them.</p>
 *
 * <p>The mix percentages live in `globals.css` as `--act-tint` / `--act-tint-edge`, because dark mode needs a
 * *stronger* tint rather than an inverted one: a pale wash on a near-black card disappears.</p>
 */

/** The neutral used for an act with no colour — a hand-typed devis line that matches no catalogue entry. */
const NEUTRAL = 'var(--muted-foreground)';

/** A wide surface: the pastel fill plus a slightly stronger hairline of the same hue. */
export function actTintStyle(colorHex: string | null | undefined): CSSProperties {
  const hue = colorHex || NEUTRAL;
  return {
    background: `color-mix(in oklab, ${hue} var(--act-tint), var(--card))`,
    boxShadow: `inset 0 0 0 1px color-mix(in oklab, ${hue} var(--act-tint-edge), var(--card))`,
  };
}

/** A small mark — a rail, a dot, a card's top edge. Full strength, or the neutral when the act has no colour. */
export function actSolidStyle(colorHex: string | null | undefined): CSSProperties {
  return { background: colorHex || NEUTRAL };
}

/**
 * The landscape-phone layout shared by the file viewers, as three class strings rather than a custom variant in
 * `globals.css`.
 *
 * <p>⚠️ **A height query, not a breakpoint, and that distinction is the whole point.** The device that breaks a
 * stacked viewer layout is a phone *turned sideways* — 844 × 390 — and its width sits in the same 820–1180 band
 * an iPad gets, where stacking is right. § 1's table is about widths and has nothing to say here; the rule this
 * satisfies is § 0's « usable at a 380 px viewport height ».</p>
 *
 * <p>Measured on the DICOM viewer, which is where this was first needed: header 77 + advisory 108 + controls
 * 143 is 281 px of furniture in a 359 px dialog, leaving the picture **78 px of 390**. As a row the stage gets
 * 524 × 256 instead.</p>
 *
 * <p>⚠️ **Shared rather than copied, and that is not tidiness.** Two viewers now open over the file dialog —
 * DICOM and 3D — and a landscape phone is equally hostile to both. Copied constants are this repo's dominant
 * defect shape: a correct answer wired to one call site, so the next fix lands in one copy and the other keeps
 * the old behaviour with nothing to show it.</p>
 *
 * <p>⚠️ **Written out in full, because Tailwind extracts candidates from source literals.** A composed
 * `` `${SHORT}flex-row` `` is never generated and the class then silently does nothing. A constant holding a
 * complete class name is fine; concatenating one is not.</p>
 */

/** The dialog body becomes a row: stage beside chrome instead of above it. */
export const SHORT_VIEWPORT_ROW = "[@media(max-height:560px)]:flex-row"

/**
 * The chrome becomes a scrolling column beside the stage.
 *
 * ⚠️ **320 px wide, and the two narrower attempts are worth recording because both looked reasonable.** The
 * control strip *wraps* in this column instead of scrolling, so the column's width decides how tall it is. At
 * 240 px (content box 216) every button took a row of its own — « Inverser » + « Ajuster » do not fit side by
 * side — and the column came to **537 px of content in a 256 px box**. At 288 px it was 521, i.e. the extra
 * 48 px bought 16. At 320 px the buttons pack two-up *and* the three tool options fit on one row rather than
 * stacking, which together bring it to **365 px** — so about a row and a half is below the fold, visible as a
 * partial row, which is the only honest cue that the column scrolls at all.
 */
export const SHORT_VIEWPORT_ASIDE =
  "[@media(max-height:560px)]:w-80 [@media(max-height:560px)]:min-h-0 " +
  "[@media(max-height:560px)]:overflow-y-auto [@media(max-height:560px)]:border-s"

/** The strip scrolls sideways under the picture and wraps beside it, where there is no width to scroll. */
export const SHORT_VIEWPORT_STRIP =
  "[@media(max-height:560px)]:flex-wrap [@media(max-height:560px)]:overflow-x-visible"

/**
 * The two pieces a non-`<button>` needs before it may be clickable.
 *
 * ⚠️ **They live here because the pattern is required in more than one place and existed in exactly one.**
 * `.claude/rules/frontend-web.md` § 13 states it as a floor — « A clickable `Card`: `role="button"` +
 * `tabIndex={0}` + Enter/Space » — and `patient-files-manager.tsx` implemented it correctly and privately, so
 * the folder cards on the patient page's Fichiers tab (the same control, one route away) shipped as a bare
 * `<Card onClick>`: no role, no tab stop, no key handler, no accessible name. Folders being that tab's only
 * route to a filed file, a keyboard or screen-reader user could not reach **any** of them.
 *
 * A shared module rather than an export from that component: a page importing a helper out of a 1000-line
 * feature component is how the helper ends up copied instead.
 */

/**
 * Enter and Space activate. `preventDefault` is not optional on Space — left alone it scrolls the page, so the
 * control would fire *and* the view would jump.
 */
export const activateOnKey =
  (action: () => void) =>
  (event: React.KeyboardEvent) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault()
      action()
    }
  }

/**
 * The visible focus ring for a element that is not a `Button` (which paints its own through `buttonVariants`).
 *
 * `globals.css` gives every keyboard-reachable element a `:focus-visible` floor, but a `Card` promoted to
 * `role="button"` reads as a control and deserves the same ring the real ones get.
 */
export const FOCUS_CLASSES =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"

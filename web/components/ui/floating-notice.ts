/**
 * What floats over every dialog on purpose: the staff chat's notice (staff-chat AC-4.2). Radix reads a press on it
 * as a press « outside » the open dialog and would close it — the fiche de soins, with what was typed in it.
 */
const FLOATING_OVER_DIALOGS = "[data-staff-chat-notice]"

type OutsideEvent = { target: EventTarget | null; preventDefault(): void }

/** A dialog's outside handler that ignores a press on the notice: the dialog stays, the caller is not told. */
export function unlessOnFloatingNotice<E extends OutsideEvent>(handler?: (event: E) => void) {
  return (event: E) => {
    if (event.target instanceof Element && event.target.closest(FLOATING_OVER_DIALOGS)) {
      event.preventDefault()
      return
    }
    handler?.(event)
  }
}

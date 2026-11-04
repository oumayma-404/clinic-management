import { DEMO_READ_ONLY_MESSAGE } from "@/lib/demo/demo-refusal"
import { showErrorToast } from "@/lib/errors"

/**
 * Refuses an action the demo can never complete, before any request or file read (`demo-cabinet` FR-4). Returns
 * true when refused. Inside a form, pass its banner setter so the notice shows there; otherwise one toast.
 */
export function refuseInDemo(isDemo: boolean | null, showInForm?: (message: string) => void): boolean {
  if (isDemo !== true) return false
  if (showInForm) showInForm(DEMO_READ_ONLY_MESSAGE)
  else showErrorToast(DEMO_READ_ONLY_MESSAGE)
  return true
}

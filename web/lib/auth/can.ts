/**
 * Role predicates for UI gating.
 *
 * Every client-side gate in this app compared against `"admin"` alone, which meant **a doctor — the primary
 * user — was denied by all of them**. The four financial-reversal actions also disagreed with each other about
 * whether to hide the control or call the endpoint and surface the 403. These helpers make the rule explicit
 * and shared so the UI and the API's `AdminOrDoctor` policy cannot drift.
 *
 * The gate is a UX affordance, never a security boundary — the server re-checks every one of these.
 */

/** Admin-only: clinic settings, user management, reference-data catalogs. */
export function isAdmin(role: string | undefined): boolean {
  return role === "admin"
}

/**
 * May reverse or alter an issued financial document — cancel an invoice, establish an avoir, void a payment,
 * cancel or amend a devis. Mirrors the server's `AdminOrDoctor` policy exactly.
 */
export function canReverseFinancials(role: string | undefined): boolean {
  return role === "admin" || role === "doctor"
}

/** Shown on a disabled control so the user knows who to ask rather than assuming the feature is missing. */
export const REVERSAL_FORBIDDEN_HINT = "Réservé au praticien ou à l'administrateur"

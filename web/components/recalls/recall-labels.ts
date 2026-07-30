import type { RecallReasonKind } from "@/lib/api/types"

/**
 * French labels for the « à rappeler » reasons — display-time mapping over the English wire keys, the standing
 * convention (`appointment-labels.ts`, `invoice-labels.ts`, `treatment-plan-labels.ts`).
 */
export const RECALL_REASON_LABELS: Record<string, string> = {
  OverdueInstallment: "Échéance en retard",
  StalledPlan: "Devis en pause",
  UnansweredDevis: "Devis sans réponse",
  OverdueVisit: "Contrôle à faire",
}

/** What each reason actually means, for the row's tooltip — staff should never have to guess. */
export const RECALL_REASON_HINTS: Record<string, string> = {
  OverdueInstallment: "Une échéance est arrivée à terme sans être réglée.",
  StalledPlan: "Devis accepté avec des actes restants et aucune séance planifiée.",
  UnansweredDevis: "Devis présenté, sans réponse du patient.",
  OverdueVisit: "Dernière visite au-delà de l’intervalle de rappel du cabinet.",
}

/**
 * Badge styling per reason. Money and a stalled surgical case read as urgent; an unanswered quote and a routine
 * check-up read as informational — the palette carries the same priority order the backend enum declares.
 */
export const RECALL_REASON_BADGE_CLASS: Record<string, string> = {
  OverdueInstallment: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
  StalledPlan: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  UnansweredDevis: "bg-sky-100 text-sky-800",
  OverdueVisit: "bg-muted text-muted-foreground",
}

/** An unmapped kind renders a French fallback, never a raw enum name — a reason is a closed backend set. */
export function recallReasonLabel(kind: RecallReasonKind | string | null | undefined): string {
  if (!kind) return "Motif inconnu"
  return RECALL_REASON_LABELS[kind] ?? "Motif inconnu"
}

export function recallReasonHint(kind: RecallReasonKind | string | null | undefined): string | undefined {
  return kind ? RECALL_REASON_HINTS[kind] : undefined
}

export function recallReasonBadgeClass(kind: RecallReasonKind | string | null | undefined): string {
  if (!kind) return "bg-muted text-muted-foreground"
  return RECALL_REASON_BADGE_CLASS[kind] ?? "bg-muted text-muted-foreground"
}

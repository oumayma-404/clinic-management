// French labels for treatment plan / devis status + item status (backend enum names → UI copy).

export const PLAN_STATUS_LABELS: Record<string, string> = {
  Draft: "Brouillon",
  Accepted: "Accepté",
  InProgress: "En cours",
  Completed: "Terminé",
  Cancelled: "Annulé",
};

// Tailwind badge classes per plan status (light + dark), mirroring the fiscal-status palette.
export const PLAN_STATUS_BADGE_CLASS: Record<string, string> = {
  Draft: "bg-muted text-muted-foreground",
  Accepted: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  InProgress: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Completed: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
  Cancelled: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
};

export const ITEM_STATUS_LABELS: Record<string, string> = {
  Planned: "Planifié",
  Done: "Réalisé",
};

export function planStatusLabel(status: string): string {
  return PLAN_STATUS_LABELS[status] ?? status;
}

export function planStatusBadgeClass(status: string): string {
  return PLAN_STATUS_BADGE_CLASS[status] ?? "bg-muted text-muted-foreground";
}

export function itemStatusLabel(status: string): string {
  return ITEM_STATUS_LABELS[status] ?? status;
}

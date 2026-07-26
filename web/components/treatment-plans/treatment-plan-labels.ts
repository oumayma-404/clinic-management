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

// Derived workflow état of an act (see plan-next-action.ts). Richer than ITEM_STATUS_LABELS above, which only
// knows the persisted Planned/Done: these four also account for whether a live appointment exists and whether
// its date has passed, so a visit that happened without a fiche reads « À enregistrer » instead of « Planifié ».
export const ITEM_WORKFLOW_LABELS: Record<string, string> = {
  "to-schedule": "À planifier",
  scheduled: "Planifié",
  "to-record": "À enregistrer",
  done: "Réalisé",
};

export const ITEM_WORKFLOW_BADGE_CLASS: Record<string, string> = {
  "to-schedule": "bg-muted text-muted-foreground",
  scheduled: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  "to-record": "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  done: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
};

// The single next step a plan needs, as button copy.
export const PLAN_NEXT_ACTION_LABELS: Record<string, string> = {
  accept: "Accepter le devis",
  record: "Enregistrer la fiche",
  schedule: "Planifier la suite",
  collect: "Encaisser",
  open: "Voir le plan",
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

export function itemWorkflowLabel(state: string): string {
  return ITEM_WORKFLOW_LABELS[state] ?? state;
}

export function itemWorkflowBadgeClass(state: string): string {
  return ITEM_WORKFLOW_BADGE_CLASS[state] ?? "bg-muted text-muted-foreground";
}

export function planNextActionLabel(kind: string): string {
  return PLAN_NEXT_ACTION_LABELS[kind] ?? PLAN_NEXT_ACTION_LABELS.open;
}

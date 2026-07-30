// French labels for treatment plan / devis status + item status (backend enum names → UI copy).
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone";

export const PLAN_STATUS_LABELS: Record<string, string> = {
  Draft: "Brouillon",
  Accepted: "Accepté",
  InProgress: "En cours",
  Completed: "Terminé",
  Cancelled: "Annulé",
};

export const PLAN_STATUS_TONE: Record<string, StatusTone> = {
  Draft: "neutral",
  Accepted: "accepted",
  InProgress: "active",
  Completed: "positive",
  Cancelled: "negative",
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

/** « À enregistrer » is `active`: the visit happened and nobody has written it up — the one état that asks for work. */
export const ITEM_WORKFLOW_TONE: Record<string, StatusTone> = {
  "to-schedule": "neutral",
  scheduled: "pending",
  "to-record": "active",
  done: "positive",
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
  return statusToneClass(PLAN_STATUS_TONE[status]);
}

export function itemStatusLabel(status: string): string {
  return ITEM_STATUS_LABELS[status] ?? status;
}

export function itemWorkflowLabel(state: string): string {
  return ITEM_WORKFLOW_LABELS[state] ?? state;
}

export function itemWorkflowBadgeClass(state: string): string {
  return statusToneClass(ITEM_WORKFLOW_TONE[state]);
}

export function planNextActionLabel(kind: string): string {
  return PLAN_NEXT_ACTION_LABELS[kind] ?? PLAN_NEXT_ACTION_LABELS.open;
}

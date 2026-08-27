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

/**
 * The **ink** of a tone as a raw CSS variable — the very colour `STATUS_TONE_CLASS` puts on the badge's text.
 *
 * <p>It exists so a surface that cannot use a badge's Tailwind classes — a 10 px pip painted through an inline
 * `boxShadow`, an SVG fill, a canvas — still speaks the same palette. Before it, `plan-act-pips.tsx` carried
 * its own four colours and <b>disagreed with the badges on every one of the four états</b>: « réalisé » was
 * green as a badge and teal as a pip, « à enregistrer » was `--warning-ink` as a badge and a hard-coded
 * `oklch(0.77 0.16 70)` as a pip — a value present in no token, so the next palette edit would have moved the
 * badge and left the pip behind. Two visual languages for one état on the same screen is not a style
 * inconsistency; it is the reader being told the pip and the badge are about different things.</p>
 *
 * <p>Keyed on the <b>tone</b>, not on the état, so it cannot drift from {@link ITEM_WORKFLOW_TONE}: retone an
 * état and its pip follows in the same commit. Mirrors `ui/status-tone.ts`'s `STATUS_TONE_CLASS` one for one —
 * `pending` resolves to `--accent-foreground` because that is the ink half of `bg-accent text-accent-foreground`,
 * and `active` to `--warning-ink` (never `--warning`) for the contrast reason that token exists for.</p>
 */
const STATUS_TONE_INK: Record<StatusTone, string> = {
  neutral: "var(--muted-foreground)",
  pending: "var(--accent-foreground)",
  accepted: "var(--primary)",
  active: "var(--warning-ink)",
  positive: "var(--success)",
  negative: "var(--destructive)",
};

/** The CSS colour for one derived act état — see {@link STATUS_TONE_INK}. Unknown états read as neutral. */
export function itemWorkflowInk(state: string): string {
  return STATUS_TONE_INK[ITEM_WORKFLOW_TONE[state] ?? "neutral"];
}

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

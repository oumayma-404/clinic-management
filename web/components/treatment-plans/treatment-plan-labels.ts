// French labels for treatment plan / devis status + item status (backend enum names → UI copy).
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone";

export const PLAN_STATUS_LABELS: Record<string, string> = {
  /*
   * ⚠️ « Brouillon » described a form somebody had not finished, and that is no longer what this status is.
   * « Suivre ce traitement » creates one deliberately: real work, séances, recorded fiches — everything except
   * a devis number. « Brouillon » on a treatment under way reads as unfinished paperwork and invites a dentist
   * to think it is not counting; « Sans devis » says the one thing that is actually true of it, and names what
   * « Éditer le devis » would change.
   */
  Draft: "Sans devis",
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

/**
 * Has any work actually been carried out on this treatment?
 *
 * <p>Read off the ACTS, not off `itemsDone` or `amountPaid`. `itemsDone` counts acts that are entirely
 * finished, so a bridge two séances into three reads 0 — the very case this question exists for; and an
 * un-numbered treatment's `amountPaid` is always 0, because collecting anything on one mints its devis.</p>
 */
export function planHasRecordedWork(plan: { items?: { status?: string }[] }): boolean {
  return (plan.items ?? []).some((i) => i.status === "InProgress" || i.status === "Done");
}

/**
 * The badge's words for a plan's status.
 *
 * <p>⚠️ <b>« Sans devis » alone was half the truth, and the missing half is the one a dentist acts on.</b> The
 * status column answers two different questions at once — <i>has a quote been issued</i> and <i>is work under
 * way</i> — because `PlanBillingRules.CarriesDebt` reads it, so an un-numbered treatment must never be
 * promoted to `InProgress`: it would owe its whole total on a devis nobody wrote. The stored status therefore
 * keeps answering the financial question, and the badge answers both — « En cours · sans devis » on a
 * treatment that has séances recorded, plain « Sans devis » on one where nothing has been done yet.</p>
 *
 * <p>The deeper fix is to make debt key on the devis <i>number</i> rather than on the status, which would let
 * the status itself tell the clinical truth. That changes every money read (`DebtBearingPlanStatuses` is a SQL
 * filter in four of them), so it is a decision of its own and not a consequence of this wording.</p>
 */
export function planStatusLabel(status: string, hasRecordedWork = false): string {
  if (status === "Draft" && hasRecordedWork) {
    return `${PLAN_STATUS_LABELS.InProgress} · ${PLAN_STATUS_LABELS.Draft.toLowerCase()}`;
  }
  return PLAN_STATUS_LABELS[status] ?? status;
}

/**
 * ⚠️ Takes the same second argument as {@link planStatusLabel} and must always be passed the same value: a
 * badge reading « En cours · sans devis » in the neutral grey of a dormant plan, beside a real « En cours » in
 * amber, tells the reader the two are different kinds of thing when they are the same kind of thing.
 */
export function planStatusBadgeClass(status: string, hasRecordedWork = false): string {
  if (status === "Draft" && hasRecordedWork) {
    return statusToneClass(PLAN_STATUS_TONE.InProgress);
  }
  return statusToneClass(PLAN_STATUS_TONE[status]);
}

/**
 * What to CALL a plan on screen — « Plan 2026-0004 », or « Traitement suivi » when it has no number.
 *
 * <p>⚠️ <b>« Devis — brouillon » was written by hand in the patient band, directly beside the badge that
 * {@link planStatusLabel} deliberately renamed to « Sans devis ».</b> Two names for one object, eight pixels
 * apart, one of them the exact word the rename existed to remove. That is why this is a function and not a
 * ternary at a call site: a name this feature disagrees with itself about is the defect, not the wording.</p>
 *
 * <p>A hand-written Draft devis keeps whatever title the dentist typed — only the absence of a title, or a
 * title that merely repeats the act (see {@link planItemHeading}), falls back to the generic name.</p>
 */
export function planDisplayName(plan: { number?: string | null; title?: string | null }): string {
  const number = plan.number?.trim();
  if (number) return `Plan ${number}`;
  const title = plan.title?.trim();
  return title ? title : "Traitement suivi";
}

/**
 * The prefix that identifies which plan one act belongs to, in a list that mixes several.
 *
 * <p>⚠️ <b>A followed treatment's title IS its act's designation</b> — `StartTreatmentCommand` sets it from
 * the procedure, because « the dentist named it by picking it » — so the obvious
 * <code>number ?? title</code> renders « Couronne / bridge (par élément) · Couronne / bridge (par élément) ».
 * Reported from use as « pourquoi l'acte est écrit deux fois », and true of every followed treatment ever
 * created (five rows in the live database when this was written).</p>
 *
 * <p>The comparison is against <b>this</b> act rather than a flag, because it is the only honest test: a
 * two-act Draft whose title happens to match one of them still says something about the other.</p>
 */
export function planItemHeading(
  plan: { number?: string | null; title?: string | null },
  item: { designationFr: string },
): string {
  const number = plan.number?.trim();
  if (number) return number;
  const title = plan.title?.trim();
  if (!title || title === item.designationFr.trim()) return "Traitement suivi";
  return title;
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

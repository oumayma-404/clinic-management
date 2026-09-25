// French labels for treatment plan / devis status + item status (backend enum names → UI copy).
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone";
import { formatDateFr } from "@/lib/format";

/**
 * What ONE échéance is called, wherever it is named: on the échéancier, in the encaissement modal's heading,
 * in the void confirmation and on the revise form's rows.
 *
 * <p>⚠️ <b>An auto-raised row has NO date, and four surfaces printed one.</b> `TreatmentPlan.Accept` writes a
 * single lump-sum row dated at the acceptance instant whenever no schedule was given — a ledger container so a
 * payment has somewhere to live, not a day anybody promised. 159 of the 184 installment rows on the dev
 * database are these. The workspace's own cell stopped showing that date (and `InstallmentLateness` stopped
 * comparing it), while « Échéance du 14/03/2026 » went on appearing in the modal that collects against it, in
 * the dialog that annuls a payment on it, and as an <i>editable</i> pre-filled `type="date"` in the revise
 * form — where re-saving it turns a fabricated instant into a date a dentist appears to have typed.</p>
 *
 * <p>One owner, `check:responsive`'s N39 holds it: a surface printing `formatDateFr(inst.dueDate)` directly
 * fails the scan.</p>
 */
export function installmentDueLabel(inst: { dueDate: string; isAutoRaised?: boolean }): string {
  return inst.isAutoRaised ? "Solde à régler" : formatDateFr(inst.dueDate);
}

/**
 * The same answer inside a sentence — « sur le solde à régler de ce devis » / « sur l'échéance du 14/03/2026 ».
 *
 * <p>⚠️ <b>It exists because the label form cannot be dropped into prose, and four surfaces wrote their own
 * instead.</b> The timeline said « Échéance du 14/03/2026 » on a row nobody dated; the workspace's card title
 * said « Total dû » and its menu « du solde à régler » where this file said « Solde à régler »; the void panel
 * and the revise form each had a fifth wording. Three names for one row, two of them eight pixels apart. A
 * second export is the cheap way to make the sentence form reachable, so nobody re-derives it.</p>
 */
export function installmentDueSentence(inst: { dueDate: string; isAutoRaised?: boolean }): string {
  return inst.isAutoRaised ? "le solde à régler de ce devis" : `l'échéance du ${formatDateFr(inst.dueDate)}`;
}

/**
 * The same answer as a heading — « Solde à régler » / « Échéance du 14/03/2026 ».
 *
 * <p>Third and last shape: {@link installmentDueLabel} is the bare cell, {@link installmentDueSentence} goes
 * mid-sentence, and this one starts one. They exist as three exports rather than as a capitalisation helper
 * because the auto-raised branch is not a case change — « Solde à régler » never gains the word « Échéance ».</p>
 */
export function installmentDueTitle(inst: { dueDate: string; isAutoRaised?: boolean }): string {
  return inst.isAutoRaised ? "Solde à régler" : `Échéance du ${formatDateFr(inst.dueDate)}`;
}

/**
 * The same answer for a form field: the day to pre-fill, or `""` for a row that has no agreed date.
 *
 * <p>Separate from {@link installmentDueLabel} because a `<input type="date">` cannot hold a sentence — and
 * pre-filling the auto-raised instant is the half of M25 that <b>writes</b> rather than merely displays.</p>
 */
export function installmentDueInputValue(inst: { dueDate: string; isAutoRaised?: boolean }): string {
  return inst.isAutoRaised ? "" : inst.dueDate.slice(0, 10);
}

/**
 * The words for each status in a FILTER or a chip count — one entry per stored status, so no two read the same.
 *
 * <p>⚠️ The BADGE reads {@link planStatusLabel}, not this map: a Draft there says where the work stands
 * (« À commencer » / « En cours »), and whether a devis exists is its own chip (« Devis n° … » / « Pas de
 * devis »). Only a filter, which must tell a Draft from an Accepted plan, prints « Sans devis ».</p>
 */
export const PLAN_STATUS_LABELS: Record<string, string> = {
  /*
   * ⚠️ « Brouillon » described a form somebody had not finished, and that is no longer what this status is.
   * « Suivre ce traitement » creates one deliberately: real work, séances, recorded fiches — everything except
   * a devis number. « Brouillon » on a treatment under way reads as unfinished paperwork and invites a dentist
   * to think it is not counting; « Sans devis » says the one thing that is actually true of it, and names what
   * « Éditer le devis » would change.
   */
  Draft: "Sans devis",
  // A filter must tell a signed devis from a treatment followed without one; the BADGE says « À commencer » for both.
  Accepted: "Devis accepté",
  InProgress: "En cours",
  Completed: "Terminé",
  /*
   * ⚠️ « Arrêté » exists because « Arrêter le traitement » used to write `Completed`, so a treatment the patient
   * abandoned wore the badge « Terminé » — the same word as one carried to term, in the database and on screen
   * alike. « Le patient ne poursuit pas » and « le travail est fini » are opposite facts about a mouth and about
   * a devis; a dentist reading the list has to be able to tell them apart at a glance.
   */
  Stopped: "Arrêté",
  /*
   * ⚠️ « Non réclamé », not « Perte » or « Irrécouvrable ». The badge is read on a patient's file by whoever
   * picks it up next, and it has to say what happened to the MONEY without accusing the patient: the work was
   * done, the cabinet decided it would not chase what was left. « Reprendre le traitement » brings it back, so
   * this is not a terminal judgement either. (It read « Créance abandonnée » — two words of accounting jargon.)
   */
  WrittenOff: "Non réclamé",
  Cancelled: "Annulé",
};

export const PLAN_STATUS_TONE: Record<string, StatusTone> = {
  Draft: "neutral",
  Accepted: "accepted",
  InProgress: "active",
  Completed: "positive",
  /*
   * `negative`, not `neutral`. An arrêt is not a neutral outcome — work was quoted, some of it delivered, and
   * the balance is still owed — and it is the one closed state a dentist may want to act on (« Reprendre »).
   * It shares the tone with « Annulé » rather than with « Terminé », which is the honest grouping: both are
   * outcomes nobody wanted, and only « Terminé » is the green one.
   */
  Stopped: "negative",
  /*
   * `neutral`, not `negative` — and the difference from « Arrêté » is the point. An arrêt leaves a balance
   * somebody may still chase, which is why it is red; a write-off is a decision that has been taken and
   * closed, so nothing about it is outstanding. Red would put a permanent alarm on a row nobody needs to act
   * on, which is how a colour stops being read.
   */
  WrittenOff: "neutral",
  Cancelled: "negative",
};

export const ITEM_STATUS_LABELS: Record<string, string> = {
  Planned: "Prévu",
  Done: "Fait",
};

// Derived workflow état of an act (see plan-next-action.ts). Richer than ITEM_STATUS_LABELS above, which only
// knows the persisted Planned/Done: these four also account for whether a live appointment exists and whether
// its date has passed, so a visit that happened without a fiche reads « À enregistrer » instead of « Planifié ».
export const ITEM_WORKFLOW_LABELS: Record<string, string> = {
  "to-schedule": "À planifier",
  scheduled: "Prévu",
  "to-record": "À enregistrer",
  done: "Fait",
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
  accept: "Créer le devis",
  record: "Enregistrer la fiche",
  schedule: "Planifier la suite",
  collect: "Encaisser",
  open: "Voir le traitement",
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
 * The badge's words for a plan's status — where the WORK stands, in the six words a dentist already uses.
 *
 * <p>⚠️ A Draft is « À commencer » or « En cours », never « Sans devis » or « En cours · sans devis »: whether a
 * devis exists is a second fact, and it has its own chip ({@link planDevisLabel}). The stored status still
 * answers the financial question — `PlanBillingRules.CarriesDebt` reads it, so an un-numbered treatment is never
 * promoted to `InProgress` — and this function is only the display.</p>
 */
/** Nothing done yet — a devis signed or not, the work is the same: still to begin. */
const NOT_STARTED_LABEL = "À commencer";

export function planStatusLabel(status: string, hasRecordedWork = false): string {
  // One word per idea: the badge says where the WORK stands; « Pas de devis » is the devis chip's job.
  if (status === "Draft" && hasRecordedWork) return PLAN_STATUS_LABELS.InProgress;
  if (status === "Draft" || status === "Accepted") return NOT_STARTED_LABEL;
  return PLAN_STATUS_LABELS[status] ?? status;
}

/**
 * ⚠️ Takes the same second argument as {@link planStatusLabel} and must always be passed the same value: a
 * badge reading « En cours · sans devis » in the neutral grey of a dormant plan, beside a real « En cours » in
 * amber, tells the reader the two are different kinds of thing when they are the same kind of thing.
 */
export function planStatusBadgeClass(status: string, hasRecordedWork = false): string {
  // Same word, same colour: a Draft wears the tone of the status whose word it borrows.
  if (status === "Draft") {
    return statusToneClass(hasRecordedWork ? PLAN_STATUS_TONE.InProgress : PLAN_STATUS_TONE.Accepted);
  }
  return statusToneClass(PLAN_STATUS_TONE[status]);
}

/** The devis chip: « Devis n° 2026-0677 », or « Pas de devis » on a treatment followed without one. */
export function planDevisLabel(plan: { number?: string | null }): string {
  const number = plan.number?.trim();
  return number ? `Devis n° ${number}` : "Pas de devis";
}

/**
 * A stored title that names nothing — « Plan de traitement » (the default for 2+ acts), « Devis », « Traitement ».
 * The one test: the page name falls back to the acts, and « Tout modifier » opens such a title empty.
 */
export function isPlaceholderTitle(title: string | null | undefined): boolean {
  const raw = title?.trim();
  return !raw || /^(plan( de traitement)?|devis|traitement)$/i.test(raw);
}

/**
 * What a treatment is CALLED on screen — the act and its teeth (« Couronne · dent 16 »), or the title the
 * dentist typed. Never « Plan 2026-0677 »: the number is the devis, and it has its own chip.
 */
export function treatmentName(plan: {
  number?: string | null;
  title?: string | null;
  items?: { designationFr: string; toothNumbers?: number[]; isWithdrawn?: boolean }[];
}): string {
  const items = (plan.items ?? []).filter((i) => !i.isWithdrawn);
  // A stored placeholder title (« Plan de traitement », « Devis ») names nothing — fall back to the acts.
  const raw = plan.title?.trim();
  const title = raw && !isPlaceholderTitle(raw) ? raw : undefined;
  const sole = items.length === 1 ? items[0] : null;
  // A followed treatment's title IS its act's designation — add the teeth rather than print it bare.
  if (title && !(sole && title === sole.designationFr.trim())) return title;
  if (sole) return `${sole.designationFr}${teethSuffix(sole.toothNumbers)}`;
  if (items.length > 1) return `${items[0].designationFr} + ${items.length - 1} acte${items.length > 2 ? "s" : ""}`;
  return title || (plan.number?.trim() ? `Devis n° ${plan.number.trim()}` : "Traitement");
}

/** « · dent 16 » / « · dents 14, 15, 16 » / nothing for an act on no tooth. */
export function teethSuffix(teeth: number[] | undefined): string {
  if (!teeth || teeth.length === 0) return "";
  return teeth.length === 1 ? ` · dent ${teeth[0]}` : ` · dents ${teeth.join(", ")}`;
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
  const title = plan.title?.trim();
  if (title) return title;
  const number = plan.number?.trim();
  return number ? `Devis n° ${number}` : "Traitement";
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
  if (!title || title === item.designationFr.trim()) return "Sans devis";
  return title;
}

export function itemStatusLabel(status: string): string {
  return ITEM_STATUS_LABELS[status] ?? status;
}

export function itemWorkflowLabel(state: string): string {
  return ITEM_WORKFLOW_LABELS[state] ?? state;
}

/**
 * The état word for ONE act: once a séance is done, « À planifier » / « Prévu » read as « not started », so a
 * stepped act says « Suite à planifier » / « Suite prévue » — still the next action, never a rank.
 */
export function planItemStateLabel(
  item: { steps?: { doneDate?: string | null }[]; stepsDone?: number },
  state: string,
): string {
  const done = item.stepsDone ?? (item.steps ?? []).filter((s) => s.doneDate).length;
  if (done > 0 && state === "to-schedule") return "Suite à planifier";
  if (done > 0 && state === "scheduled") return "Suite prévue";
  return itemWorkflowLabel(state);
}

export function itemWorkflowBadgeClass(state: string): string {
  return statusToneClass(ITEM_WORKFLOW_TONE[state]);
}

export function planNextActionLabel(kind: string): string {
  return PLAN_NEXT_ACTION_LABELS[kind] ?? PLAN_NEXT_ACTION_LABELS.open;
}

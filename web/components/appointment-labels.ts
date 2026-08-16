// French labels for appointment status + patient gender (backend enum names → UI copy).
//
// AC-P1.41-1.46. Before this, appointment status was rendered four different ways and none of them was
// French in every case:
//   - `edit-appointment-dialog.tsx` built a label by string-mangling — and because the value had already been
//     lower-cased at hydration, its `([A-Z])` spacing branch was dead, so "inprogress" rendered as
//     « Inprogress » and "noshow" as « Noshow ».
//   - `appointment-list.tsx` and the patient-history table printed the raw English enum name.
//   - the status `<Select>` held the only French copy in the app, hardcoded inline.
//   - the calendar legend hardcoded three of the six statuses and omitted the rest.
// One map, one accessor, `?? key` pass-through — the convention `factures/invoice-labels.ts` established.
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone";

/** The seven statuses, in lifecycle order. Backend enum names — this is the wire form. */
export const APPOINTMENT_STATUSES = [
  "Scheduled",
  "Confirmed",
  "InProgress",
  "AwaitingClosure",
  "Completed",
  "Cancelled",
  "NoShow",
] as const;

export type AppointmentStatusName = (typeof APPOINTMENT_STATUSES)[number];

/**
 * The statuses a human may choose. `AwaitingClosure` is written **only** by `AppointmentProgressJob` — it states
 * that a slot has ended, which is a fact about the clock and not a decision anybody makes.
 *
 * <p>It exists because `edit-appointment-dialog` falls back to the full list when the server sends no
 * `allowedNextStatuses`, and that fallback would otherwise offer « Séance passée » as a manual option.</p>
 */
export const MANUALLY_SETTABLE_STATUSES = APPOINTMENT_STATUSES.filter(
  (s) => s !== "AwaitingClosure",
);

export const APPOINTMENT_STATUS_LABELS: Record<string, string> = {
  Scheduled: "Planifié",
  Confirmed: "Confirmé",
  InProgress: "En cours",
  AwaitingClosure: "Séance passée",
  Completed: "Terminé",
  Cancelled: "Annulé",
  NoShow: "Absent",
};

/**
 * Status → tone, through the shared scale (`components/ui/status-tone.ts`).
 *
 * `Scheduled` and `Confirmed` are deliberately two different tones and not one: in an agenda, « the patient said
 * yes » versus « we put them in the book » is the distinction the colour is actually there to carry, and collapsing
 * them would leave the desk reading six labels to find the unconfirmed ones.
 */
export const APPOINTMENT_STATUS_TONE: Record<string, StatusTone> = {
  Scheduled: "pending",
  Confirmed: "accepted",
  InProgress: "active",
  // Shares `pending` with `Scheduled` — the scale has six tones and there are now seven statuses, so one pair
  // must share. This is the safe pair: both mean « awaiting an action or a decision », which is the tone's own
  // definition, and the clock separates them on the grid. The pair that must NEVER share is `InProgress` vs this
  // one — « quelqu'un est au fauteuil » against « le créneau est passé » — and they do not.
  AwaitingClosure: "pending",
  Completed: "positive",
  Cancelled: "neutral",
  NoShow: "negative",
};

/**
 * AC-P1.44: an unmapped value renders a **French fallback**, never a raw enum name. Deliberately not the
 * `?? key` pass-through the other label modules use: a status is a closed backend enum, so an unknown value is
 * a bug rather than user data worth showing — unlike a custom specialty, which a clinic legitimately typed.
 */
export function appointmentStatusLabel(status: string | null | undefined): string {
  if (!status) return "Statut inconnu";
  return APPOINTMENT_STATUS_LABELS[normalizeStatus(status)] ?? "Statut inconnu";
}

export function appointmentStatusBadgeClass(status: string | null | undefined): string {
  if (!status) return "bg-muted text-muted-foreground";
  return statusToneClass(APPOINTMENT_STATUS_TONE[normalizeStatus(status)]);
}

/**
 * Map any casing the app has historically used back to the canonical backend name.
 *
 * Load-bearing: `edit-appointment-dialog.tsx` lower-cases the status at hydration and posts the lower-cased
 * string straight back, so `"inprogress"` and `"noshow"` are real values flowing through the UI today. A map
 * keyed only on `"InProgress"` would silently miss them and render the fallback for a perfectly valid status.
 */
export function normalizeStatus(status: string): AppointmentStatusName | string {
  const match = APPOINTMENT_STATUSES.find((s) => s.toLowerCase() === status.trim().toLowerCase());
  return match ?? status;
}

// ---- Gender (AC-P1.45-1.46) -------------------------------------------------

/**
 * The stored gender values. `Unknown` is in the set on purpose (AC-P1.46): it is written by three separate
 * paths — `edit-patient-dialog`'s create fallback, `CreatePatientCommand`'s default for the appointment
 * dialog's inline patient create, and `GoogleCalendarSyncService`'s auto-created placeholder patients — so it
 * exists in real data and must render as something. `""` occurs too, and reads as « Non renseigné ».
 */
export const GENDER_LABELS: Record<string, string> = {
  Male: "Homme",
  Female: "Femme",
  Other: "Autre",
  Unknown: "Non précisé",
};

/** The three values the patient form offers. `Unknown` is readable but never selectable. */
export const SELECTABLE_GENDERS = ["Male", "Female", "Other"] as const;

/**
 * AC-P1.45/1.46: « Homme / Femme / Autre » everywhere a gender is shown. A patient reading « Male » — or
 * « Unknown » — in an otherwise-French record is the defect; an unmapped custom value still renders verbatim
 * rather than blank.
 */
export function genderLabel(gender: string | null | undefined): string {
  if (!gender || !gender.trim()) return "Non renseigné";
  const trimmed = gender.trim();
  const match = Object.keys(GENDER_LABELS).find((k) => k.toLowerCase() === trimmed.toLowerCase());
  return match ? GENDER_LABELS[match] : trimmed;
}

/**
 * Whether this row is a « créneau occupé » — time the practitioner blocked out, not a patient.
 *
 * <p>The server writes no patient at all on such a row and names it `"Occupé"`
 * (`CreateAppointmentCommand`), so `patientId` is the real test and the name is the belt-and-braces half.
 * It lived inline in `appointment-calendar.tsx` twice and nowhere else, which is why the day board rendered a
 * blocked hour as « Au fauteuil » — a sentence that asserts a patient is being treated.</p>
 */
export function isBusySlot(appointment: { patientId?: string | null; patientName?: string | null }): boolean {
  return !appointment.patientId || appointment.patientName === "Occupé";
}

/**
 * How a séance's acts read on a list or a calendar card: « Détartrage + Obturation ».
 *
 * <p>One helper rather than a join at each surface, for the same reason the label maps above are shared: the agenda,
 * the dashboard list and the patient page must describe the same visit the same way. It falls back to the lead-act
 * name so a response that predates the `procedures` field still renders, and returns `null` — never `""` — when
 * there is no act, so callers keep choosing their own placeholder (« Rendez-vous », « Occupé »).</p>
 */
export function appointmentActsSummary(appointment: {
  procedures?: { name?: string | null; sequenceNumber: number }[]
  procedureTypeName?: string | null
}): string | null {
  const names = (appointment.procedures ?? [])
    .slice()
    .sort((a, b) => a.sequenceNumber - b.sequenceNumber)
    .map((p) => p.name?.trim())
    .filter((n): n is string => !!n)

  if (names.length > 0) return names.join(" + ")
  return appointment.procedureTypeName?.trim() || null
}

/** Number of acts booked into a séance — `> 1` is what a « +N » marker keys off on a cramped calendar card. */
export function appointmentActsCount(appointment: {
  procedures?: unknown[]
  procedureTypeName?: string | null
}): number {
  const count = appointment.procedures?.length ?? 0
  if (count > 0) return count
  return appointment.procedureTypeName ? 1 : 0
}

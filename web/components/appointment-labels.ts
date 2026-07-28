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

/** The six statuses, in lifecycle order. Backend enum names — this is the wire form. */
export const APPOINTMENT_STATUSES = [
  "Scheduled",
  "Confirmed",
  "InProgress",
  "Completed",
  "Cancelled",
  "NoShow",
] as const;

export type AppointmentStatusName = (typeof APPOINTMENT_STATUSES)[number];

export const APPOINTMENT_STATUS_LABELS: Record<string, string> = {
  Scheduled: "Planifié",
  Confirmed: "Confirmé",
  InProgress: "En cours",
  Completed: "Terminé",
  Cancelled: "Annulé",
  NoShow: "Absent",
};

/** Tailwind badge classes per status (light + dark), mirroring the fiscal-status palette. */
export const APPOINTMENT_STATUS_BADGE_CLASS: Record<string, string> = {
  Scheduled: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  Confirmed: "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-200",
  InProgress: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Completed: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
  Cancelled: "bg-muted text-muted-foreground",
  NoShow: "bg-orange-100 text-orange-800 dark:bg-orange-950 dark:text-orange-200",
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
  return APPOINTMENT_STATUS_BADGE_CLASS[normalizeStatus(status)] ?? "bg-muted text-muted-foreground";
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

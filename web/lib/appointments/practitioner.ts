import type { DoctorDto } from "@/lib/api/clinics"

/**
 * Which dentist a booking is for — the browser's half of `AppointmentPractitioner` (cabinet-de-groupe US-1), read by
 * both booking dialogs so they cannot disagree.
 *
 * ⚠️ The server is the authority: it fills in a one-dentist cabinet's dentist and refuses a patient RDV with none
 * once there are several (`practitioner_required`). This module only pre-picks and pre-checks, with the server's own
 * sentence, so the refusal is met on the field rather than after a round trip.
 */

/** The server's `AppointmentPractitioner.RequiredMessage`, word for word. */
export const PRACTITIONER_REQUIRED_MESSAGE =
  "Choisissez le praticien de ce rendez-vous : le cabinet en compte plusieurs."

/** The server's `AppointmentPractitioner.RequiredCode`. */
export const PRACTITIONER_REQUIRED_CODE = "practitioner_required"

/** A Radix `SelectItem` cannot carry an empty value, so « Tout le cabinet » travels as this and maps back to "". */
export const CABINET_WIDE = "__cabinet__"

/**
 * The dentist a NEW booking opens on: the one the caller names (the agenda's filter, a waiting-list entry's
 * preference), else the signed-in dentist, else the only active one, else none. A name that is not on the active
 * roster is ignored rather than trusted — a retired dentist must not be booked by default.
 */
export function initialDoctorId(
  activeDoctors: readonly DoctorDto[],
  preferredId?: string | null,
  ownId?: string | null,
): string {
  const ids = activeDoctors.map((d) => d.id).filter((id): id is string => Boolean(id))
  if (preferredId && ids.includes(preferredId)) return preferredId
  if (ownId && ids.includes(ownId)) return ownId
  return ids.length === 1 ? ids[0] : ""
}

/** True when a booking with a patient must name its dentist before it can be saved. */
export function practitionerRequired(activeDoctors: readonly DoctorDto[], hasPatient: boolean): boolean {
  return hasPatient && activeDoctors.filter((d) => d.id).length > 1
}

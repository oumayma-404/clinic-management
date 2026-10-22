import type { CountryCode } from "libphonenumber-js/max"

import type { PatientDto, ReminderConsent, SmokingStatus, TobaccoUnit } from "@/lib/api/types"
import type { Dentition } from "@/lib/dentition"
import { storedPhoneCountry } from "@/lib/phone"

/**
 * The patient form, as « Recharger » reconciles it (`lib/forms/form-merge.ts`) and as it is carried across a switch of
 * server (`clinic-pc-copy` D23). ONE reading of a stored patient — the form hydrates through it and the merge
 * compares with it, so the two cannot fill a field two different ways.
 */
export interface PatientFormValues {
  firstName: string
  lastName: string
  gender: string
  birthdate: string
  dentition: Dentition | null
  phone: string
  phoneCountry: CountryCode
  extraPhones: { value: string; country: CountryCode }[]
  email: string
  addressLine: string
  referredBy: string
  reminderConsent: ReminderConsent
  consultationReason: string
  allergies: string
  medications: string
  chronicDiseases: string
  smokingStatus: SmokingStatus | null
  smokingPerDay: string
  smokingUnit: TobaccoUnit
  notes: string
  importantNotes: string
}

/** Fields that only make sense together are one section — a merge never assembles a patient nobody wrote. */
export type PatientSection = "identity" | "contact" | "reason" | "health" | "notes"

export const PATIENT_SECTIONS: readonly PatientSection[] = ["identity", "contact", "reason", "health", "notes"]

export const PATIENT_SECTION_LABEL: Record<PatientSection, string> = {
  identity: "Identité",
  contact: "Coordonnées",
  reason: "Motif de consultation",
  health: "Informations médicales",
  notes: "Notes",
}

/** A stored patient as the form shows it. */
export function patientFormValuesOf(patient: PatientDto): PatientFormValues {
  return {
    firstName: patient.firstName || "",
    lastName: patient.lastName || "",
    gender: patient.gender || "",
    birthdate: patient.dateOfBirth ? patient.dateOfBirth.split("T")[0] : "",
    dentition: (patient.dentition as Dentition) || null,
    phone: patient.phoneNumber || "",
    // A stored number re-opens the country its WRITER chose — `phoneE164`, never `phoneNumber`, which re-derives
    // against Tunisia and is why a French patient came back +216. See `storedPhoneCountry`.
    phoneCountry: storedPhoneCountry(patient.phoneE164, patient.phoneNumber),
    // ⚠️ Read back AND sent again: `SetAdditionalPhoneNumbers` replaces the whole list server-side.
    extraPhones: (patient.additionalPhones ?? []).map((extra) => ({
      value: extra.value,
      country: storedPhoneCountry(extra.e164, extra.value),
    })),
    email: patient.email || "",
    // ⚠️ Folded, not truncated: the four stored parts come back into the one box in FULL, adjacent duplicates
    // dropped (« Ariana, Ariana » reads as a bug), or a save would drop three quarters of the address.
    addressLine: [patient.address?.street, patient.address?.city, patient.address?.state, patient.address?.zipCode]
      .map((part) => part?.trim())
      .filter((part): part is string => Boolean(part))
      .filter((part, index, all) => index === 0 || part.toLowerCase() !== all[index - 1].toLowerCase())
      .join(", "),
    referredBy: patient.referredBy || "",
    reminderConsent: patient.reminderConsent ?? "NotRecorded",
    consultationReason: patient.consultationReason || "",
    allergies: patient.allergies || "",
    medications: patient.medications || "",
    chronicDiseases: patient.medicalHistory || "",
    // A null block is « jamais renseigné » — never seeded as « Non-fumeur ».
    smokingStatus: patient.tobaccoUse?.status ?? null,
    smokingPerDay: patient.tobaccoUse?.perDay != null ? String(patient.tobaccoUse.perDay) : "",
    smokingUnit: patient.tobaccoUse?.unit ?? "Cigarettes",
    notes: patient.notes || "",
    importantNotes: patient.importantNotes || "",
  }
}

const t = (s: string) => s.trim()

/** Each section as the save sends it — trimmed, blank rows dropped — so an untouched form never reads « edited ». */
export function patientSnapshot(v: PatientFormValues): Record<PatientSection, string> {
  return {
    identity: JSON.stringify([t(v.firstName), t(v.lastName), v.gender || "Unknown", v.birthdate, v.dentition]),
    contact: JSON.stringify([
      t(v.phone),
      t(v.phone) ? v.phoneCountry : null,
      v.extraPhones.filter((p) => t(p.value)).map((p) => [t(p.value), p.country]),
      t(v.email),
      t(v.addressLine),
      t(v.referredBy),
      v.reminderConsent,
    ]),
    reason: t(v.consultationReason),
    health: JSON.stringify([
      t(v.allergies),
      t(v.medications),
      t(v.chronicDiseases),
      v.smokingStatus,
      v.smokingStatus ? t(v.smokingPerDay) : "",
      v.smokingStatus ? v.smokingUnit : "",
    ]),
    notes: JSON.stringify([t(v.notes), t(v.importantNotes)]),
  }
}

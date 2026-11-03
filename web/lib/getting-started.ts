import type { OnboardingVideoKey } from "@/lib/onboarding-videos"

/**
 * « Premiers pas » — the seven setup steps of a new cabinet (guided-first-days B), in card order.
 *
 * ⚠️ Mirrors the server's `GettingStartedKeys.All`, which decides which steps exist and whether each is done;
 * `GettingStartedKeysTests` reads this file and holds the two equal in both directions.
 */
export const GETTING_STARTED_KEYS = [
  "securityCode",
  "secretary",
  "phoneApp",
  "patients",
  "appointment",
  "dentalRecord",
  "letterhead",
] as const

export type GettingStartedKey = (typeof GETTING_STARTED_KEYS)[number]

export interface GettingStartedStepMeta {
  title: string
  /** Its « Premiers pas » video; a video not filmed yet offers no link. */
  video: OnboardingVideoKey
  /** Where the step is done. Null when the card opens a dialog instead (« Votre téléphone »). */
  href: string | null
}

export const GETTING_STARTED_STEPS: Record<GettingStartedKey, GettingStartedStepMeta> = {
  securityCode: { title: "Code de sécurité", video: "firstSignIn", href: "/securite" },
  secretary: { title: "Votre secrétaire", video: "prepareCabinet", href: "/users?invite=1" },
  phoneApp: { title: "Votre téléphone", video: "phoneApp", href: null },
  patients: { title: "Vos patients", video: "firstPatient", href: "/patients?newPatient=1" },
  appointment: { title: "Premier rendez-vous", video: "firstAppointment", href: "/appointments?newAppointment=1" },
  dentalRecord: { title: "Première fiche de soins", video: "session", href: "/patients" },
  letterhead: { title: "Papier à en-tête", video: "documents", href: "/settings#en-tete" },
}

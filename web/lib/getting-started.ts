import type { GuideKey } from "@/lib/guides"
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
  /** « Me guider » over the real screen (guided-first-days F); a step with a guide offers it instead of « Commencer ». */
  guide?: GuideKey
  /** Said with the confetti the first time the step ticks (owner's idea, 2026-11-04) — dental, kind, one line. */
  joke: string
}

export const GETTING_STARTED_STEPS: Record<GettingStartedKey, GettingStartedStepMeta> = {
  securityCode: {
    title: "Code de sécurité",
    video: "firstSignIn",
    href: "/securite",
    joke: "Votre cabinet est maintenant aussi bien protégé qu'une molaire sous son émail.",
  },
  secretary: {
    title: "Votre secrétaire",
    video: "prepareCabinet",
    href: "/users?invite=1",
    guide: "secretary",
    joke: "Seul on va vite, à deux on croque la vie à pleines dents.",
  },
  phoneApp: {
    title: "Votre téléphone",
    video: "phoneApp",
    href: null,
    joke: "Le cabinet tient dans votre poche : de quoi garder le sourire même en déplacement.",
  },
  patients: {
    title: "Vos patients",
    video: "firstPatient",
    href: "/patients?newPatient=1",
    guide: "patients",
    joke: "Pourquoi les dents ne s'ennuient jamais ? Elles ont toujours quelque chose à se mettre sous la dent.",
  },
  appointment: {
    title: "Premier rendez-vous",
    video: "firstAppointment",
    href: "/appointments?newAppointment=1",
    guide: "appointment",
    joke: "Votre agenda commence à avoir du mordant.",
  },
  dentalRecord: {
    title: "Première fiche de soins",
    video: "session",
    href: "/patients",
    guide: "dentalRecord",
    joke: "Le comble pour un dentiste ? Se casser les dents sur une fiche de soins. Pas vous !",
  },
  letterhead: {
    title: "Papier à en-tête",
    video: "documents",
    href: "/settings#en-tete",
    guide: "letterhead",
    joke: "Que dit une dent à sa voisine avant les vacances ? « On fait le pont ? » Vos documents, eux, ont fière allure.",
  },
}

/** Said with the confetti when the last step ticks and the card leaves the dashboard. */
export const GETTING_STARTED_READY_JOKE = "Votre cabinet a désormais toutes ses dents."

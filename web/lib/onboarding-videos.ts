/**
 * The « Premiers pas » videos (guided-first-days). One owner, so the sign-up page, the « Premiers pas » card and the
 * site's « Tutoriels » page cannot point at different cuts of the same lesson.
 *
 * ⚠️ Opened on YouTube, never embedded: `frame-src` is `'self' blob:` in four byte-identical CSP copies, and both
 * shells already hand a foreign https link to the system browser.
 *
 * `youtubeId: null` means « not filmed yet » — no « Voir la vidéo » is offered for it.
 */
export interface OnboardingVideo {
  readonly title: string
  readonly youtubeId: string | null
  /** As printed beside the link: « 0:53 ». */
  readonly length: string | null
}

export const ONBOARDING_VIDEOS = {
  createAccount: { title: "Créer votre compte", youtubeId: "vXwtPbCL_UY", length: "0:51" },
  firstSignIn: { title: "Première connexion", youtubeId: "wQ-WNx39U50", length: "0:53" },
  prepareCabinet: { title: "Préparer le cabinet", youtubeId: "UZokOPtfJBc", length: "1:22" },
  firstPatient: { title: "Votre premier patient", youtubeId: "u8KUh00l3QA", length: "0:45" },
  firstAppointment: { title: "Votre premier rendez-vous", youtubeId: "FZzPvkufkKU", length: "0:39" },
  session: { title: "La séance", youtubeId: "ixdLZxswDU8", length: "0:58" },
  treatmentPlan: { title: "Le plan de traitement", youtubeId: "zVTUW-ZP17g", length: "0:59" },
  documents: { title: "Les documents", youtubeId: "XOXCFck4GgA", length: "0:33" },
  phoneApp: { title: "L'application sur votre téléphone", youtubeId: null, length: null },
} as const satisfies Record<string, OnboardingVideo>

export type OnboardingVideoKey = keyof typeof ONBOARDING_VIDEOS

/** The site page listing all eight (apexa.tn « Tutoriels ») — « Aide » and the welcome e-mail point here. */
export const ONBOARDING_VIDEOS_PAGE = "https://apexa.tn/tutoriels.html"

/** The watch URL, or null for a video not filmed yet. */
export function onboardingVideoUrl(video: OnboardingVideo): string | null {
  return video.youtubeId ? `https://www.youtube.com/watch?v=${video.youtubeId}` : null
}

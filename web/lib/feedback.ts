import { baseSections, buildConfigItems } from "@/lib/nav"

/**
 * « Donner mon avis » and the week-one question (guided-first-days E) — the browser's copy of the closed sets
 * `FeedbackRules` refuses outside of. `FeedbackMirrorTests` reads this file and fails on any drift, both ways: a key
 * the server does not know is refused as `feedback_invalid`, and one missing here can never be chosen.
 */

export const FEEDBACK_KINDS = [
  { key: "idee", label: "Idée" },
  { key: "probleme", label: "Problème" },
  { key: "question", label: "Question" },
  { key: "avis", label: "Avis" },
] as const

export type FeedbackKind = (typeof FEEDBACK_KINDS)[number]["key"]

/** The app's main areas, so « ce que vous aimez » and « ce qui a été difficile » can be counted. */
export const FEEDBACK_AREAS = [
  { key: "agenda", label: "Agenda" },
  { key: "dossier-patient", label: "Dossier patient" },
  { key: "odontogramme", label: "Odontogramme" },
  { key: "fiche-de-soins", label: "Fiche de soins" },
  { key: "devis", label: "Devis" },
  { key: "caisse", label: "Caisse" },
  { key: "documents", label: "Documents" },
  { key: "telephone", label: "Téléphone" },
] as const

export type FeedbackArea = (typeof FEEDBACK_AREAS)[number]["key"]

/** `semaine-1` closes the week-one question for good; `aide` leaves it alone. */
export type FeedbackSource = "semaine-1" | "aide"

/**
 * Set on `<body>` while the week-one card is on screen. The « Séance terminée » question waits on it, exactly as it
 * waits on `data-sheet-open`, so the two never ask at once.
 */
export const FEEDBACK_CARD_OPEN_ATTR = "data-feedback-open"

/** « Séance terminée » has a visit to ask about and only the week-one card is in its way: the card steps aside. */
export const VISIT_REVIEW_WAITING_EVENT = "visit-review:waiting"

/** The card's height on `<body>` while it is up, so a phone's « + » and last rows can sit above it. */
export const FEEDBACK_CARD_HEIGHT_VAR = "--feedback-card-h"

/**
 * The screen a message was written from, in the menu's own words (« Traitements »), never the raw path — the
 * message reaches the vendor and « /treatment-plans/3f2… » says less than the name the person sees. Longest
 * matching menu entry wins, so a patient's file reads « Patients ». Nothing else from the screen is sent.
 */
export function screenLabel(pathname: string | null | undefined): string | null {
  if (!pathname) return null
  const items = [...baseSections.flatMap((s) => s.items), ...buildConfigItems(true, true)]
  let best: { name: string; href: string } | null = null
  for (const item of items) {
    const matches =
      item.href === "/" ? pathname === "/" : pathname === item.href || pathname.startsWith(`${item.href}/`)
    if (matches && (!best || item.href.length > best.href.length)) best = item
  }
  return best?.name ?? null
}

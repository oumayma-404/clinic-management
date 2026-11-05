import { quoteFr } from "@/lib/format"

import type { Guide } from "./types"

/**
 * The hand-over between two tours (owner's call, 2026-11-04): back on the dashboard, the screen dims around the next
 * open step of « Premiers pas » and says so — « On attaque l'étape suivante : … ! Votre cabinet est configuré à N % » —
 * with « C'est parti » (that step's own tour) and « Plus tard ». Also the first thing a new cabinet sees, as the
 * start of the tour. Built per call because the sentence carries the step and the figure; the mark is fixed.
 */
export function nextStepPrompt({
  title,
  percent,
  first,
}: {
  title: string
  percent: number
  /** The very first prompt of a new cabinet: « on commence par », not « l'étape suivante ». */
  first?: boolean
}): Guide {
  const figure = `${percent} %`
  return {
    // Not one of the five tours: it ticks nothing, so its « done » hands over to the next step instead.
    key: "nextStep",
    title: "Premiers pas",
    startHref: "/",
    routes: /^\/$/,
    continueLabel: "C'est parti",
    quitLabel: "Plus tard",
    steps: [
      {
        mark: "premiers-pas-next",
        text: first
          ? `Bienvenue ! On commence par ${quoteFr(title)}. Votre cabinet est configuré à ${figure}.`
          : `On attaque l'étape suivante : ${quoteFr(title)} ! Votre cabinet est configuré à ${figure}.`,
        next: { manual: true },
      },
    ],
    doneText: "",
  }
}

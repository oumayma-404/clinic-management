import type { Guide } from "./types"

/** « Papier à en-tête » — a tour of the import window. No file is asked for, nothing is saved. */
export const letterheadGuide: Guide = {
  key: "letterhead",
  title: "Papier à en-tête",
  startHref: "/settings#en-tete",
  routes: /^\/settings\/?$/,
  steps: [
    {
      mark: "letterhead-import",
      text: "Ici, vous importez votre papier à en-tête : un PDF ou une photo.",
      press: true,
      next: { manual: true },
    },
    {
      mark: "letterhead-file",
      scope: "letterhead-dialog",
      text: "Le fichier choisi, APEXA découpe l'en-tête et le pied de page, et montre l'aperçu avant d'enregistrer.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez mettre votre papier à en-tête.",
}

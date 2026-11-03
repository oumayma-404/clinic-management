import type { Guide } from "./types"

/** « Papier à en-tête » — the file picker is the system's own window: the guide waits for « Découpe » to appear. */
export const letterheadGuide: Guide = {
  key: "letterhead",
  title: "Papier à en-tête",
  startHref: "/settings#en-tete",
  routes: /^\/settings\/?$/,
  steps: [
    {
      mark: "letterhead-import",
      text: "Cliquez sur « Importer mon papier à en-tête ».",
      touchText: "Touchez « Importer mon papier à en-tête ».",
      next: { visible: "letterhead-file" },
    },
    {
      mark: "letterhead-file",
      scope: "letterhead-dialog",
      text: "Choisissez un PDF ou une photo de votre papier.",
      next: { visible: "letterhead-cut-next" },
    },
    {
      mark: "letterhead-cut-next",
      scope: "letterhead-dialog",
      text: "Placez les deux lignes sur l'image, puis cliquez ici.",
      touchText: "Placez les deux lignes sur l'image, puis touchez ici.",
      next: { visible: "letterhead-save" },
    },
    {
      mark: "letterhead-save",
      scope: "letterhead-dialog",
      text: "Si l'aperçu vous convient, cliquez sur « Enregistrer ».",
      touchText: "Si l'aperçu vous convient, touchez « Enregistrer ».",
      next: { savedAndClosed: true },
    },
  ],
  doneText: "Vos documents s'impriment sur votre papier.",
}

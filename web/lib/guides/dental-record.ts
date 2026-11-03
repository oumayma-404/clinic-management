import type { Guide } from "./types"

/** « Première fiche de soins » — on a patient's page. Needs a patient: the list is the first step. */
export const dentalRecordGuide: Guide = {
  key: "dentalRecord",
  title: "Première fiche de soins",
  startHref: "/patients",
  routes: /^\/patients(\/[^/]+)?\/?$/,
  steps: [
    {
      mark: "patients-list",
      text: "Ouvrez le dossier d'un patient.",
      next: { route: /^\/patients\/[^/]+\/?$/ },
    },
    {
      mark: "record-new",
      text: "Cliquez sur « Nouvelle fiche de soins ».",
      touchText: "Touchez « Nouvelle fiche de soins ».",
      next: { visible: "record-dialog" },
    },
    {
      mark: "record-acts",
      scope: "record-dialog",
      text: "Choisissez l'acte, puis les dents sur le schéma.",
      next: { manual: true },
    },
    {
      mark: "record-submit",
      scope: "record-dialog",
      text: "Cliquez ici pour enregistrer la séance.",
      touchText: "Touchez ici pour enregistrer la séance.",
      next: { savedAndClosed: true },
    },
  ],
  missingText: "Ce guide a besoin d'un patient : commencez par « Vos patients ».",
  doneText: "Votre première fiche de soins est enregistrée.",
}

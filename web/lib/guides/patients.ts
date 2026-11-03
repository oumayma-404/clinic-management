import type { Guide } from "./types"

/** « Vos patients » — the first patient. Saving opens the patient's page, which the guide expects. */
export const patientsGuide: Guide = {
  key: "patients",
  title: "Vos patients",
  startHref: "/patients",
  routes: /^\/patients(\/[^/]+)?\/?$/,
  steps: [
    {
      mark: "patients-add",
      text: "Cliquez sur « Ajouter un patient ».",
      touchText: "Touchez « Ajouter un patient ».",
      next: { visible: "patient-dialog" },
    },
    {
      mark: "patient-names",
      scope: "patient-dialog",
      text: "Son prénom et son nom suffisent. Le reste peut attendre.",
      next: { manual: true },
    },
    {
      mark: "patient-submit",
      scope: "patient-dialog",
      text: "Cliquez sur « Créer le patient ».",
      touchText: "Touchez « Créer le patient ».",
      next: { savedAndClosed: true },
    },
  ],
  doneText: "Votre premier patient est enregistré.",
}

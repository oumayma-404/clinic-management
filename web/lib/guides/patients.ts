import type { Guide } from "./types"

/** « Vos patients » — a tour of the patient window. Nothing is saved. */
export const patientsGuide: Guide = {
  key: "patients",
  title: "Vos patients",
  startHref: "/patients",
  routes: /^\/patients(\/[^/]+)?\/?$/,
  steps: [
    {
      mark: "patients-add",
      text: "Ici, vous ajoutez un patient.",
      press: true,
      next: { manual: true },
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
      text: "Ce bouton crée son dossier, pour de vrai : la visite, elle, n'y touche pas.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez ajouter un patient.",
}

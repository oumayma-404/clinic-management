import type { Guide } from "./types"

/**
 * « Première fiche de soins » — a TOUR of the fiche, on a patient's page (owner's call, 2026-11-04).
 *
 * It opens the first patient's file and the fiche itself, then shows what the fiche holds — several acts, their
 * teeth, the notes, the ordonnance, the payment — one « Suivant » at a time; nothing is saved. With no patient yet
 * there is no fiche to open: one sentence, and « Terminer ».
 */
export const dentalRecordGuide: Guide = {
  key: "dentalRecord",
  title: "Première fiche de soins",
  startHref: "/patients",
  routes: /^\/patients(\/[^/]+)?\/?$/,
  steps: [
    {
      mark: "patients-list",
      text: "La fiche de soins se remplit dans le dossier du patient. Ouvrons-en un.",
      press: 'tbody tr, li a[href^="/patients/"]',
      emptyMark: "patients-empty",
      emptyText: "La fiche de soins se remplit dans le dossier du patient, après la séance : vous la verrez dès votre premier patient.",
      next: { manual: true },
    },
    {
      mark: "record-new",
      text: "Après la séance, la fiche de soins s'ouvre ici.",
      press: true,
      next: { manual: true },
    },
    {
      mark: "record-acts",
      scope: "record-dialog",
      text: "L'acte fait pendant la séance, choisi dans votre liste d'actes.",
      next: { manual: true },
    },
    {
      mark: "record-add-act",
      scope: "record-dialog",
      text: "Plusieurs actes dans la même séance : ajoutez-les ici. Chacun a ses dents et son prix.",
      next: { manual: true },
    },
    {
      mark: "record-chart",
      scope: "record-dialog",
      text: "Cliquez sur les dents, ou glissez sur plusieurs : elles vont à l'acte ouvert.",
      touchText: "Touchez les dents, ou glissez sur plusieurs : elles vont à l'acte ouvert.",
      next: { manual: true },
    },
    {
      mark: "record-notes",
      scope: "record-dialog",
      text: "Les notes de la séance. Une note importante s'affiche en alerte sur le dossier du patient.",
      next: { manual: true },
    },
    {
      mark: "record-prescription",
      scope: "record-dialog",
      text: "Médicaments et examens : l'ordonnance est créée en enregistrant la fiche.",
      next: { manual: true },
    },
    {
      mark: "record-payment",
      scope: "record-dialog",
      text: "Ce que le patient paie aujourd'hui, et comment.",
      next: { manual: true },
    },
    {
      mark: "record-submit",
      scope: "record-dialog",
      text: "Après une vraie séance, enregistrez ici. La visite, elle, n'y touche pas.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez remplir une fiche de soins.",
}

import type { Guide } from "./types"

/**
 * « Congés » — a tour from Paramètres → Équipe: the switch, each secretary's droit, one secretary's file and its rule,
 * then « À décider ». Not a « Premiers pas » step; it opens a file and goes back, and nothing is chosen or saved.
 */
export const congesGuide: Guide = {
  key: "conges",
  title: "Congés",
  startHref: "/settings#equipe",
  routes: /^\/(settings|users)(\/[^/]+)?\/?$/,
  standalone: true,
  steps: [
    {
      mark: "conges-switch",
      text: "Les demandes de congé des secrétaires s'activent ici.",
      next: { manual: true },
    },
    {
      mark: "conges-people",
      text: "Le droit à congé de chaque secrétaire. Son nom ouvre sa fiche.",
      press: "a",
      next: { manual: true },
    },
    {
      mark: "conges-rule",
      text: "Sa règle : tant de jours par an ou par mois, et ce que deviennent les jours non pris au 31 décembre.",
      next: { manual: true },
    },
    {
      mark: "conges-back",
      text: "« Utilisateurs » ramène à l'onglet Congés.",
      press: true,
      next: { manual: true },
    },
    {
      mark: "conges-pending",
      text: "Les demandes à décider arrivent ici : « Accepter », ou « Refuser » avec un motif.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez régler les congés des secrétaires.",
}

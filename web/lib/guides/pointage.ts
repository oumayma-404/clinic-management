import type { Guide } from "./types"

/**
 * « Pointage » — a tour of Paramètres → Équipe while its setup is unfinished: the switch, the four steps, who pointe,
 * and the borne window with its devices. Not a « Premiers pas » step; nothing is chosen or saved.
 */
export const pointageGuide: Guide = {
  key: "pointage",
  title: "Pointage",
  startHref: "/settings#equipe",
  routes: /^\/settings\/?$/,
  standalone: true,
  steps: [
    {
      mark: "pointage-switch",
      text: "Le pointage des arrivées et des départs se règle ici.",
      next: { manual: true },
    },
    {
      mark: "pointage-setup",
      text: "Les quatre étapes de la mise en place, et où en est chacune : horaires, borne, schémas, blocage de l'app.",
      next: { manual: true },
    },
    {
      mark: "pointage-people",
      text: "Qui pointe. Chacun crée son schéma, un tracé qu'il refait à la borne pour pointer.",
      next: { manual: true },
    },
    {
      mark: "pointage-add-borne",
      text: "La borne est l'appareil de l'accueil où chacun pointe. Ce bouton la choisit.",
      press: true,
      next: { manual: true },
    },
    {
      mark: "pointage-devices",
      scope: "pointage-borne-dialog",
      text: "Les appareils connectés du cabinet : choisissez le PC ou la tablette de l'accueil, pas un téléphone.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez mettre en place le pointage.",
}

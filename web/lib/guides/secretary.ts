import type { Guide } from "./types"

/** « Votre secrétaire » — her account, from « Utilisateurs ». Online the button says « Inviter », on a cabinet PC « Créer ». */
export const secretaryGuide: Guide = {
  key: "secretary",
  title: "Votre secrétaire",
  startHref: "/users",
  routes: /^\/users\/?$/,
  steps: [
    {
      mark: "users-create",
      text: "Pour ajouter votre secrétaire, cliquez ici.",
      touchText: "Pour ajouter votre secrétaire, touchez ici.",
      next: { visible: "user-dialog" },
    },
    {
      mark: "user-fields",
      scope: "user-dialog",
      text: "Son nom et son e-mail. Le rôle est déjà « Secrétaire ».",
      next: { manual: true },
    },
    {
      mark: "user-submit",
      scope: "user-dialog",
      text: "Cliquez ici pour créer son compte.",
      touchText: "Touchez ici pour créer son compte.",
      next: { savedAndClosed: true },
    },
  ],
  doneText: "Votre secrétaire a son compte.",
}

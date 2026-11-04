import type { Guide } from "./types"

/** « Votre secrétaire » — a tour of « Utilisateurs » and its invitation window. Nothing is sent. */
export const secretaryGuide: Guide = {
  key: "secretary",
  title: "Votre secrétaire",
  startHref: "/users",
  routes: /^\/users\/?$/,
  steps: [
    {
      mark: "users-create",
      text: "Ici, vous ajoutez votre secrétaire.",
      press: true,
      next: { manual: true },
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
      text: "Ce bouton crée son compte. Pendant la visite, rien n'est envoyé.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez ajouter votre secrétaire.",
}

import type { Guide } from "./types"

/**
 * « Premier rendez-vous ». ⚠️ Starts on the agenda, never `?newAppointment=1`: that deep link keeps the page's
 * opening minute as the start time, so a slow first booking meets « …dans le passé ? ».
 */
export const appointmentGuide: Guide = {
  key: "appointment",
  title: "Premier rendez-vous",
  startHref: "/appointments",
  routes: /^\/appointments\/?$/,
  steps: [
    {
      mark: "agenda-new",
      text: "Pour un nouveau rendez-vous, cliquez ici.",
      touchText: "Pour un nouveau rendez-vous, touchez ici.",
      next: { visible: "booking-dialog" },
    },
    {
      mark: "booking-patient",
      scope: "booking-dialog",
      text: "Choisissez le patient, ou « Nouveau patient » et son nom.",
      next: { manual: true },
    },
    {
      mark: "booking-when",
      scope: "booking-dialog",
      text: "Le jour et l'heure.",
      next: { manual: true },
    },
    {
      mark: "booking-acts",
      scope: "booking-dialog",
      text: "Ajoutez l'acte prévu : la durée suit.",
      next: { manual: true },
    },
    {
      mark: "booking-submit",
      scope: "booking-dialog",
      text: "Cliquez sur « Créer le rendez-vous ».",
      touchText: "Touchez « Créer le rendez-vous ».",
      next: { savedAndClosed: true },
    },
  ],
  doneText: "Votre premier rendez-vous est dans l'agenda.",
}

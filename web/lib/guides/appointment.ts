import type { Guide } from "./types"

/**
 * « Premier rendez-vous » — a tour of the booking window. Nothing is saved. ⚠️ Starts on the agenda, never
 * `?newAppointment=1`: that deep link keeps the page's opening minute as the start time.
 */
export const appointmentGuide: Guide = {
  key: "appointment",
  title: "Premier rendez-vous",
  startHref: "/appointments",
  routes: /^\/appointments\/?$/,
  steps: [
    {
      mark: "agenda-new",
      text: "Ici, vous créez un rendez-vous.",
      press: true,
      next: { manual: true },
    },
    {
      mark: "booking-patient",
      scope: "booking-dialog",
      text: "Le patient : un patient existant, ou « Nouveau patient » et son nom.",
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
      text: "L'acte prévu : la durée suit.",
      next: { manual: true },
    },
    {
      mark: "booking-submit",
      scope: "booking-dialog",
      text: "Ce bouton crée le rendez-vous. Pendant la visite, rien n'est enregistré.",
      next: { manual: true },
    },
  ],
  doneText: "Vous savez prendre un rendez-vous.",
}

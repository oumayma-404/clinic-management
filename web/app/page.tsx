import type { Metadata } from "next"

import { DashboardPage } from "@/components/dashboard/dashboard-page"

/**
 * The one route whose title IS « Tableau de bord ». Every other route now says its own name; this file is a
 * server shell purely so the dashboard can export a `metadata` its client body cannot.
 */
export const metadata: Metadata = {
  title: "Tableau de bord",
  description: "Vue d'ensemble de la journée : rendez-vous, encaissements et alertes.",
}

export default function Page() {
  return <DashboardPage />
}

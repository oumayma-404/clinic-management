import type { Metadata } from "next"

/** Title-only layout: the page is a client component, which cannot export `metadata`. */
export const metadata: Metadata = {
  title: "Mes présences",
  description: "Votre schéma de pointage et vos arrivées et départs du mois.",
}

export default function MesPresencesLayout({ children }: { children: React.ReactNode }) {
  return children
}

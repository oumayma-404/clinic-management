import type { Metadata } from "next"

/** Title-only layout: the borne is a client component, which cannot export `metadata`. */
export const metadata: Metadata = {
  title: "Pointage",
  description: "La borne du cabinet : arrivées et départs de l'équipe.",
}

export default function PointageLayout({ children }: { children: React.ReactNode }) {
  return children
}

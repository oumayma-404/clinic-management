import type { Metadata } from "next"

/** Title-only layout: the page is a client component and cannot export `metadata` itself. */
export const metadata: Metadata = {
  title: "Salle d'attente",
  description: "Qui est arrivé, qui est au fauteuil, qui est le suivant.",
}

export default function SalleAttenteLayout({ children }: { children: React.ReactNode }) {
  return children
}

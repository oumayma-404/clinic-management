import type { Metadata } from "next"

/** Title-only layout: the page is a client component, which cannot export `metadata`. The title is also the link preview's. */
export const metadata: Metadata = {
  title: { absolute: "APEXA Démo" },
  description: "Visitez un cabinet dentaire de démonstration : agenda, dossiers patients, devis, caisse. Rien n'est enregistré.",
}

export default function DemoLayout({ children }: { children: React.ReactNode }) {
  return children
}

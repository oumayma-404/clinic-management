import type { Metadata } from "next"

/**
 * Title-only layout. A page in this app is a client component, and a client component cannot export
 * `metadata` — so the route's own `<title>` has to live in a server file beside it. `%s` is filled into the
 * template on the root layout.
 */
export const metadata: Metadata = {
  title: "Caisse",
  description: "Les encaissements, les dépenses et l'extrait de la période.",
}

export default function CaisseLayout({ children }: { children: React.ReactNode }) {
  return children
}

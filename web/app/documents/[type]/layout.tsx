import type { Metadata } from "next"

/**
 * Title-only layout. A page in this app is a client component, and a client component cannot export
 * `metadata` — so the route's own `<title>` has to live in a server file beside it. `%s` is filled into the
 * template on the root layout.
 */
export const metadata: Metadata = {
  title: "Nouveau document",
  description: "Rédaction d'une ordonnance, d'un certificat ou d'une lettre.",
}

export default function DocumentsTypeLayout({ children }: { children: React.ReactNode }) {
  return children
}

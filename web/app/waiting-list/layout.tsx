import type { Metadata } from "next"

/**
 * Title-only layout. A page in this app is a client component, and a client component cannot export
 * `metadata` — so the route's own `<title>` has to live in a server file beside it. `%s` is filled into the
 * template on the root layout.
 */
export const metadata: Metadata = {
  title: "Liste d'attente",
  description: "Les patients en attente d'un créneau.",
}

export default function WaitingListLayout({ children }: { children: React.ReactNode }) {
  return children
}

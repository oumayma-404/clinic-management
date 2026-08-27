import type { Metadata } from "next"

/**
 * Title-only layout. A page in this app is a client component, and a client component cannot export
 * `metadata` — so the route's own `<title>` has to live in a server file beside it. `%s` is filled into the
 * template on the root layout.
 */
export const metadata: Metadata = {
  title: "Stock",
  description: "Les articles, les lots et les mouvements.",
}

export default function StockLayout({ children }: { children: React.ReactNode }) {
  return children
}

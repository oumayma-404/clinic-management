import type { Metadata } from "next"

/** Title-only layout, for the reason `app/settings/layout.tsx` gives. */
export const metadata: Metadata = {
  title: "Retours du PC de secours",
  description: "Ce qu'une coupure d'internet a laissé à vérifier ou à reprendre dans le cloud.",
}

export default function RelayReturnsLayout({ children }: { children: React.ReactNode }) {
  return children
}

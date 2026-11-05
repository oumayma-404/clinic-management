import type { Metadata } from "next"

/** Title-only layout — the page is a client component and cannot export `metadata` (see `reinitialiser-mot-de-passe`). */
export const metadata: Metadata = {
  title: "Invitation",
  description: "Choisir le mot de passe de votre compte APEXA.",
}

export default function InvitationLayout({ children }: { children: React.ReactNode }) {
  return children
}

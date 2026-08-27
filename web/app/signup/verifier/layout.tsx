import type { Metadata } from "next"

/**
 * Title-only layout. A page in this app is a client component, and a client component cannot export
 * `metadata` — so the route's own `<title>` has to live in a server file beside it. `%s` is filled into the
 * template on the root layout.
 */
export const metadata: Metadata = {
  title: "Confirmer votre e-mail",
  description: "La dernière étape avant l'ouverture du cabinet.",
}

export default function SignupVerifierLayout({ children }: { children: React.ReactNode }) {
  return children
}

import type { Metadata } from "next"

export const metadata: Metadata = {
  title: "Mes congés",
  description: "Demander un congé et suivre ses demandes.",
}

export default function CongesLayout({ children }: { children: React.ReactNode }) {
  return children
}

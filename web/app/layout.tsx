import type React from "react"
import type { Metadata } from "next"
import { Geist, Geist_Mono } from "next/font/google"
import { Analytics } from "@vercel/analytics/next"
import { resolveAuthMode } from "@/lib/auth/local-auth"
import { CloudSessionProvider, LocalSessionProvider } from "@/lib/auth/session"
import { ConnectivityProvider } from "@/lib/connectivity/connectivity"
import { SidebarProvider } from "@/contexts/sidebar-context"
import { Toaster } from "sonner"
import { AIChat } from "@/components/ai-chat"
import { PRODUCT_NAME } from "@/lib/brand"
import "./globals.css"

const _geist = Geist({ subsets: ["latin"] })
const _geistMono = Geist_Mono({ subsets: ["latin"] })

export const metadata: Metadata = {
  title: `${PRODUCT_NAME} — Tableau de bord`,
  description: "Système de gestion de clinique pour les professionnels de santé",
  icons: {
    icon: [
      {
        url: "/icon-light-32x32.png",
        media: "(prefers-color-scheme: light)",
      },
      {
        url: "/icon-dark-32x32.png",
        media: "(prefers-color-scheme: dark)",
      },
      {
        url: "/icon.svg",
        type: "image/svg+xml",
      },
    ],
    apple: "/apple-icon.png",
  },
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  // Mount the session provider that matches the configured auth mode.
  // Cloud mounts Auth0Provider; Local mounts a lightweight cookie-backed context.
  const SessionProvider = resolveAuthMode() === "local" ? LocalSessionProvider : CloudSessionProvider

  return (
    <html lang="fr">
      <body className={`font-sans antialiased`}>
        <SessionProvider>
          <ConnectivityProvider>
            <SidebarProvider>
              {children}
            </SidebarProvider>
            {/* Inside ConnectivityProvider so it can gate on internet reachability (Local mode). */}
            <AIChat />
          </ConnectivityProvider>
        </SessionProvider>
        <Toaster
          position="top-right"
          richColors
          closeButton
          duration={3000}
          expand={true}
          visibleToasts={5}
        />
        <Analytics />
      </body>
    </html>
  )
}

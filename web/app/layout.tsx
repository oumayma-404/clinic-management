import type React from "react"
import type { Metadata, Viewport } from "next"
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

/*
 * Next injects `width=device-width, initial-scale=1` when no viewport is exported, which is why the app scales
 * at all today — but the two settings that matter on a phone are not in that default:
 *
 *   viewportFit: "cover"          without it `env(safe-area-inset-*)` resolves to 0px, so the bottom nav bar
 *                                 would sit under the home indicator and the notch would clip the header.
 *   interactiveWidget             "resizes-content" makes the on-screen keyboard shrink the viewport instead of
 *                                 scrolling the page over it, which is what keeps a sheet's sticky footer — and
 *                                 the primary action in it — visible while typing.
 *
 * `maximumScale` / `userScalable` are deliberately NOT set: capping zoom is an accessibility regression, and a
 * clinician reading a chart at arm's length is a real user of it.
 */
export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  interactiveWidget: "resizes-content",
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
    /*
      `suppressHydrationWarning` is required by the theme provider landing in P7: next-themes' pre-hydration
      script sets the theme class on <html> before React hydrates, so the server and client markup differ on
      this element by design. Added now rather than in P7 so layout.tsx is edited once.
    */
    <html lang="fr" suppressHydrationWarning>
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
        {/*
          4 s, not 3. Several of this app's toasts are full French sentences with a `description` under them
          (« La connexion a été interrompue. Réessayez une fois la connexion rétablie. »), and 3 s is not
          enough time to read one — the message is gone before it has been understood, which is the same as
          not having shown it.

          Error toasts get their own, longer life at the call site (`showErrorToast` in `lib/errors.ts`), which
          is the only place sonner lets a duration vary by kind: a success toast is a confirmation the user can
          afford to miss (the row already changed on screen), while an error toast is the *only* place the
          reason exists. `closeButton` means the longer duration never traps anyone.
        */}
        <Toaster
          position="top-right"
          richColors
          closeButton
          duration={4000}
          expand={true}
          visibleToasts={5}
        />
        <Analytics />
      </body>
    </html>
  )
}

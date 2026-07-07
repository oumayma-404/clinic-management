import type React from "react"
import type { Metadata } from "next"
import { Geist, Geist_Mono } from "next/font/google"
import { Analytics } from "@vercel/analytics/next"
import { Auth0Provider } from '@auth0/nextjs-auth0/client'
import { SidebarProvider } from "@/contexts/sidebar-context"
import { Toaster } from "sonner"
import { AIChat } from "@/components/ai-chat"
import "./globals.css"

const _geist = Geist({ subsets: ["latin"] })
const _geistMono = Geist_Mono({ subsets: ["latin"] })

export const metadata: Metadata = {
  title: "MediCare Clinic - Dashboard",
  description: "Professional clinic management system for healthcare providers",
  generator: "v0.app",
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
  return (
    <html lang="en">
      <body className={`font-sans antialiased`}>
        <Auth0Provider>
          <SidebarProvider>
            {children}
          </SidebarProvider>
        </Auth0Provider>
        <Toaster 
          position="top-right"
          richColors
          closeButton
          duration={3000}
          expand={true}
          visibleToasts={5}
        />
        <AIChat />
        <Analytics />
      </body>
    </html>
  )
}

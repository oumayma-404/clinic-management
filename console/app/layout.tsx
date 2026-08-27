import type { Metadata, Viewport } from "next";
import "./globals.css";

/**
 * The console's shell (platform-console FR-7).
 *
 * ⚠️ **No clinic chrome, and that is a requirement rather than a simplification**: no navigation rail, no patient
 * search, no notification bell, no AI assistant. This application is served by its own container on its own
 * private address and contains none of the clinic bundle — so those pieces are not hidden here, they are absent.
 */
export const metadata: Metadata = {
  title: "Console éditeur · APEXA",
  description: "Administration des abonnements et de l'activité des cabinets.",
  // The console is reached through a tunnel by one operator; there is nothing here for a crawler, and this
  // costs nothing to state.
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  // Deliberately NOT `maximumScale: 1` — pinch-zoom is how somebody reads a figure on a phone, and disabling it
  // is the § 0 « no capability removed by a layout decision » rule broken in one line.
  themeColor: "#1e293b",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fr">
      <body className="antialiased">{children}</body>
    </html>
  );
}

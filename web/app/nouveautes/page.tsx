"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { NewsFeed } from "@/components/news/news-feed"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { AppLoader } from "@/components/ui/app-loader"
import { useSession } from "@/lib/auth/session"

export default function NouveautesPage() {
  const { user, isLoading } = useSession()
  const isAdmin = user?.role === "admin"

  return (
    <ClinicGuard>
      <AppShell width={isAdmin ? "7xl" : "none"} gutter={isAdmin} contentClassName={isAdmin ? "space-y-6" : undefined}>
        {isLoading ? (
          <AppLoader />
        ) : !isAdmin ? (
          <AccessDeniedCard
            title="Réservé aux administrateurs"
            description="Les nouveautés d'Apexa sont réservées aux administrateurs du cabinet."
          />
        ) : (
          <NewsFeed />
        )}
      </AppShell>
    </ClinicGuard>
  )
}

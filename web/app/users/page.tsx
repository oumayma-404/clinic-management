"use client"

import { useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { UserManagement } from "@/components/user-management"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Lock, ArrowLeft } from "lucide-react"

export default function UsersPage() {
  const { user, isLoading } = useSession()
  const router = useRouter()
  const isAdmin = user?.role === "admin"

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto">
            {isLoading ? (
              <p className="p-8 text-center text-muted-foreground">Chargement…</p>
            ) : isAdmin ? (
              <UserManagement />
            ) : (
              // AC-5.4: the user-management screen is only reachable by an admin.
              <div className="flex min-h-full items-center justify-center p-6">
                <Card className="w-full max-w-md">
                  <CardHeader className="space-y-3 text-center">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-red-100 dark:bg-red-900/20">
                      <Lock className="h-7 w-7 text-red-600 dark:text-red-400" />
                    </div>
                    <CardTitle>Administrateurs uniquement</CardTitle>
                    <CardDescription>La gestion des utilisateurs est réservée aux administrateurs de la clinique.</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <Button variant="outline" className="w-full gap-2" onClick={() => router.push("/")}>
                      <ArrowLeft className="h-4 w-4" />
                      Retour au tableau de bord
                    </Button>
                  </CardContent>
                </Card>
              </div>
            )}
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

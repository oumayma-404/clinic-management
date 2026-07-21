"use client"

import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { MonProfilContent } from "@/components/mon-profil-content"

export default function MonProfilPage() {
  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-3xl space-y-6">
              <div>
                <h1 className="text-3xl font-semibold">Mon profil</h1>
                <p className="text-sm text-muted-foreground mt-1">
                  Vos informations professionnelles et votre cachet apparaissent sur les documents que vous générez.
                </p>
              </div>
              <MonProfilContent />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

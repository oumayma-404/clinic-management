"use client"

import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { MonProfilContent } from "@/components/mon-profil-content"

export default function MonProfilPage() {
  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-3xl space-y-6">
              <PageHeader
                zone="Paramètres"
                title="Mon profil"
                subtitle="Vos informations professionnelles et votre cachet apparaissent sur les documents que vous générez."
              />
              <MonProfilContent />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

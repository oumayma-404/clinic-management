"use client"

import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ReceivablesTable } from "@/components/creances/receivables-table"

export default function CreancesPage() {
  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-5xl space-y-6">
              <PageHeader
                zone="Argent"
                title="Créances"
                subtitle="Qui doit combien — soldes dus par patient (factures + échéanciers), les plus élevés en tête."
              />

              <ReceivablesTable />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

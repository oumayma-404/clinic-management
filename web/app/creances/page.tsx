"use client"

import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { ReceivablesTable } from "@/components/creances/receivables-table"

export default function CreancesPage() {
  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-5xl space-y-6">
              <div>
                <h1 className="text-3xl font-semibold text-foreground">Créances</h1>
                <p className="mt-1 text-sm text-muted-foreground">
                  Qui doit combien — soldes dus par patient (factures + échéanciers), les plus élevés en tête.
                </p>
              </div>

              <ReceivablesTable />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ReceivablesTable } from "@/components/creances/receivables-table"

export default function CreancesPage() {
  return (
    <ClinicGuard>
      <AppShell width="5xl" contentClassName="space-y-6">
        <PageHeader
          zone="Argent"
          title="Créances"
          subtitle="Qui doit combien — soldes dus par patient (factures + échéanciers), les plus élevés en tête."
        />

        <ReceivablesTable />
      </AppShell>
    </ClinicGuard>
  )
}

"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { MonProfilContent } from "@/components/mon-profil-content"

export default function MonProfilPage() {
  return (
    <ClinicGuard>
      <AppShell width="3xl" contentClassName="space-y-6">
        <PageHeader
          title="Mon profil"
          subtitle="Vos informations professionnelles et votre cachet apparaissent sur les documents que vous générez."
        />
        <MonProfilContent />
      </AppShell>
    </ClinicGuard>
  )
}

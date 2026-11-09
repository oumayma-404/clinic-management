"use client"

import { Fingerprint } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { AppLoader } from "@/components/ui/app-loader"
import { EmptyState } from "@/components/ui/empty-state"
import { PageHeader } from "@/components/ui/page-header"
import { SchemaSetupCard } from "@/components/team/schema-setup-card"
import { useUserStatus } from "@/lib/hooks/use-user-status"

/** « Mes présences » — her schéma and her month of pointage, read-only; also in Mon profil. */
export default function MesPresencesPage() {
  const { data: status, isLoading } = useUserStatus()
  const enabled = status?.clinic?.attendanceEnabled === true

  return (
    <ClinicGuard>
      <AppShell width="3xl" contentClassName="space-y-6">
        <PageHeader title="Mes présences" />
        {isLoading && !status ? (
          <AppLoader />
        ) : enabled ? (
          <SchemaSetupCard page />
        ) : (
          <EmptyState
            icon={Fingerprint}
            title="Le pointage n'est pas activé"
            description="L'administrateur du cabinet peut l'activer dans « Paramètres »."
          />
        )}
      </AppShell>
    </ClinicGuard>
  )
}

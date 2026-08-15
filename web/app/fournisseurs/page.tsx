"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { SuppliersTable } from "@/components/suppliers/suppliers-table"

/**
 * « Fournisseurs » — the cabinet's outside contacts.
 *
 * <p><b>No role gate</b>, deliberately: `SuppliersController` is `AnyClinicRole` because ordering supplies and
 * chasing a prothèse is reception's job, and none of this is clinic-wide money. The three Finances screens gate
 * because their reads are `AdminOrDoctor`; this one has nothing to gate on.</p>
 */
export default function FournisseursPage() {
  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Fournisseurs"
          subtitle="Les laboratoires, dépôts et prestataires du cabinet — avec le numéro qu'il faut pour les joindre. Un fournisseur lié à des articles de stock ou à des bons de prothèse se désactive plutôt qu'il ne se supprime : il quitte les listes de sélection sans effacer les liens existants."
        />

        <SuppliersTable />
      </AppShell>
    </ClinicGuard>
  )
}

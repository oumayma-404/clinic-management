"use client"

import { useState } from "react"
import { Plus } from "lucide-react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { PageHeader } from "@/components/ui/page-header"
import { SuppliersTable } from "@/components/suppliers/suppliers-table"

/**
 * « Fournisseurs » — the cabinet's outside contacts.
 *
 * <p><b>No role gate</b>, deliberately: `SuppliersController` is `AnyClinicRole` because ordering supplies and
 * chasing a prothèse is reception's job, and none of this is clinic-wide money. The three Finances screens gate
 * because their reads are `AdminOrDoctor`; this one has nothing to gate on.</p>
 *
 * <p><b>The page owns « Nouveau fournisseur », and the table asks for it.</b> `PageHeader` is where a page's one
 * primary action lives (`/patients`, `/factures`, `/treatment-plans` all do this), and `ListToolbar`'s own contract
 * is that only controls which *narrow* the list belong in the filter row. It used to sit under the filters **and**
 * again in the empty state, so a cabinet with no fournisseur saw the same primary button twice.</p>
 */
export default function FournisseursPage() {
  // Bumped to ask the table to open its create dialog. A counter rather than a boolean: two clicks in a row must
  // both arrive, and a boolean that has to be reset is a second piece of state to keep in step.
  const [createRequest, setCreateRequest] = useState(0)

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Fournisseurs"
          subtitle="Laboratoires, dépôts et prestataires — et le numéro qu'il faut pour les joindre."
          actions={
            <Button onClick={() => setCreateRequest((n) => n + 1)} className="gap-2">
              <Plus aria-hidden="true" className="size-4" />
              Nouveau fournisseur
            </Button>
          }
        />

        <SuppliersTable createRequest={createRequest} />
      </AppShell>
    </ClinicGuard>
  )
}

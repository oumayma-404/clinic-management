"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { ChequesTable } from "@/components/caisse/cheques-table"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"

/**
 * « Chèques à encaisser » (L8 slice B) — the clinic's uncashed cheques, over both payment ledgers.
 *
 * <p>Gated exactly like the other three Finances screens: it is a clinic-wide money read (the practice's uncashed
 * exposure in one figure), and `GET /api/billing/cheques` is `AdminOrDoctor`. Presentation only — the rail already
 * hides the entry; this covers a bookmark, a shared link and a role changed while the tab was open.</p>
 */
export default function ChequesPage() {
  const { user, isLoading } = useSession()
  const denied = hidesClinicWideMoney(user?.role)

  return (
    <ClinicGuard>
      {/* `7xl`, the default — the table is eight columns wide and the `5xl` /creances uses would crush it. */}
      <AppShell width={denied ? "none" : "7xl"} gutter={!denied} contentClassName={denied ? undefined : "space-y-6"}>
        {isLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : denied ? (
          <AccessDeniedCard description="Les chèques détenus par le cabinet sont réservés au praticien et à l'administrateur. Un paiement par chèque reste enregistrable depuis la facture ou l'échéancier du patient." />
        ) : (
          <>
            <PageHeader
              title="Chèques à encaisser"
              subtitle="Les chèques que le cabinet détient, du plus urgent au plus lointain — factures et échéanciers confondus. Marquez un chèque « encaissé » une fois porté en banque : il quitte la liste sans qu'aucun montant ne bouge, et reste consultable sous « Encaissés »."
            />

            <ChequesTable />
          </>
        )}
      </AppShell>
    </ClinicGuard>
  )
}

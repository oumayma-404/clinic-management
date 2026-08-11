"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { RetiredPageCard } from "@/components/ui/retired-page-card"
import { ReceivablesTable } from "@/components/creances/receivables-table"
import { useSession } from "@/lib/auth/session"
import { hidesClinicWideMoney } from "@/lib/nav"

/**
 * « Créances » is withdrawn from the product. The rail no longer lists it and the dashboard no longer carries the
 * figure that linked here, so this covers a bookmark or a shared link.
 *
 * <p><b>Nothing behind it was removed</b> — `GET /api/billing/receivables` and {@link ReceivablesTable} are intact,
 * and {@link CreancesScreen} below is the screen verbatim, so restoring it is re-pointing this export. It is kept
 * in the module rather than deleted so `tsc` and `npm run build` keep covering it; a screen parked outside the
 * compiler is a screen that stops compiling.</p>
 */
export default function CreancesPage() {
  return (
    <ClinicGuard>
      <AppShell width="none" gutter={false}>
        <RetiredPageCard description="Le suivi des créances a été retiré. Le solde d'un patient reste consultable depuis sa fiche, onglet « Factures »." />
      </AppShell>
    </ClinicGuard>
  )
}

/** The « Créances » screen as it shipped. Not routed — see {@link CreancesPage}. */
function CreancesScreen() {
  const { user, isLoading } = useSession()
  // I3: presentation only. `GET /api/billing/receivables` is `AdminOrDoctor` server-side, so for a secretary this
  // page would render a French error over an empty table; the refusal says why instead. The rail already hides
  // the entry — this covers a bookmark, a shared link, and a role changed while the tab was open.
  const denied = hidesClinicWideMoney(user?.role)

  return (
    <ClinicGuard>
      <AppShell width={denied ? "none" : "5xl"} gutter={!denied} contentClassName={denied ? undefined : "space-y-6"}>
        {isLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : denied ? (
          <AccessDeniedCard description="Les créances de la clinique sont réservées au praticien et à l'administrateur. Le solde d'un patient reste consultable depuis sa fiche." />
        ) : (
          <>
            <PageHeader
              title="Créances"
              subtitle="Qui doit combien — soldes dus par patient (factures + échéanciers), les plus élevés en tête."
            />

            <ReceivablesTable />
          </>
        )}
      </AppShell>
    </ClinicGuard>
  )
}

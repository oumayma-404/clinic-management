"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { LegacyImportScreen } from "@/components/legacy-import/import-screen"

/**
 * « Importer l'ancien logiciel » — TEMPORARY: one practice's migration, run from its secretary's PC, then removed
 * together with `components/legacy-import/`, `lib/legacy-import/` and the « Ancien logiciel » link on `/patients`.
 * Every clinic role: every door it writes through is open to a secretary.
 */
export default function LegacyImportPage() {
  return (
    <ClinicGuard>
      <AppShell width="5xl" mainClassName="overflow-y-auto scroll-pb-36 sm:scroll-pb-24">
        <LegacyImportScreen />
      </AppShell>
    </ClinicGuard>
  )
}

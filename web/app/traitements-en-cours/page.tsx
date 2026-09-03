"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { TreatmentsInProgressList } from "@/components/treatments/treatments-in-progress-list"

/**
 * « Traitements en cours » — the acts a cabinet has started and not finished.
 *
 * <p><b>Its own route, and open to every role</b>, on `/a-cloturer`'s reasoning: `GET /api/dashboard` is
 * `AdminOrDoctor` and `/` sends a secretary to `/appointments`, so a worklist living only on the dashboard
 * would be invisible to reception — the person who telephones the patient and books the next séance. The
 * journée's own « traitements en cours » pastille lands here.</p>
 *
 * <p><b>Nothing here is stored.</b> An act is on this list because its steps say some are done and some are
 * not, and whether its next step is booked is derived per request from the appointments — so the list cannot
 * drift from reality and there is no worklist table to repair.</p>
 */
export default function TreatmentsInProgressPage() {
  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Traitements en cours"
          subtitle="Les actes commencés et non terminés, avec l'étape qui reste à planifier."
        />
        <TreatmentsInProgressList />
      </AppShell>
    </ClinicGuard>
  )
}

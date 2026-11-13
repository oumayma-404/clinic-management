"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { ImportScreen } from "@/components/photo-import/import-screen"

/**
 * « Importer les photos » (`features/camera-photo-import`) — the camera's card, sorted by the agenda into each
 * patient's « Photographie » folder. Reached from « Fichiers », a patient's files and (phase 2) the Windows app; not
 * from the rail. Every clinic role: filing photos is reception's work as much as the dentist's.
 */
export default function PhotoImportPage() {
  return (
    <ClinicGuard>
      <AppShell width="5xl">
        <ImportScreen />
      </AppShell>
    </ClinicGuard>
  )
}

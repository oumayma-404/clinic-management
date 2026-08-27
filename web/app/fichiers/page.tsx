"use client"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PatientFilesDirectory } from "@/components/files/patient-files-directory"

/**
 * « Fichiers » — a directory of the clinic's file drawers, one card per patient, opening onto that patient's own
 * files page.
 *
 * <p>A thin route on purpose: the header's subtitle quotes the read's own total, so it is rendered by the
 * component that performs the read rather than duplicated here from a second one (see
 * `components/files/patient-files-directory.tsx`).</p>
 *
 * <p><b>`AnyClinicRole`, matching the endpoint behind it.</b> Filing and finding a patient's radiographs is
 * reception's work as much as the dentist's — « record yes, erase no » is the line the clinical record is on, and
 * deleting a file stays on the patient's own page where it always was.</p>
 */
export default function FichiersPage() {
  return (
    <ClinicGuard>
      <AppShell>
        <PatientFilesDirectory />
      </AppShell>
    </ClinicGuard>
  )
}

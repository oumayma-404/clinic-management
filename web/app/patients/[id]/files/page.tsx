"use client"

import { useParams, useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { ArrowLeft } from "lucide-react"
import { PatientFilesManager } from "@/components/patient-files-manager"
import { patientsApi } from "@/lib/api/patients"
import { useState, useEffect, useCallback } from "react"
import type { PatientDto } from "@/lib/api/types"
import { getErrorMessage } from "@/lib/errors"

const getPatientName = (patient: PatientDto | null) => {
  if (!patient) return "Patient"
  return `${patient.firstName} ${patient.lastName}`.trim()
}

export default function PatientFilesPage() {
  const params = useParams()
  const router = useRouter()
  const patientId = params.id as string
  const [patient, setPatient] = useState<PatientDto | null>(null)
  const [loading, setLoading] = useState(true)
  // AC-P3.27 — the load failure used to be a bare console.error, so the page rendered the file manager under
  // the literal heading « Patient »: the operator could not tell whose files they were looking at, and the
  // manager below was scoped to an id whose patient may not even exist.
  const [error, setError] = useState<string | null>(null)

  const loadPatient = useCallback(async () => {
    if (!patientId) return
    try {
      setLoading(true)
      setError(null)
      const patientData = await patientsApi.get(patientId)
      setPatient(patientData)
    } catch (err) {
      setError(getErrorMessage(err, "Ce patient n'a pas pu être chargé."))
      setPatient(null)
    } finally {
      setLoading(false)
    }
  }, [patientId])

  useEffect(() => {
    void loadPatient()
  }, [loadPatient])

  if (loading) {
    return (
      <ClinicGuard>
        <AppShell width="none" mainClassName="flex items-center justify-center">
        <p className="text-muted-foreground">Chargement…</p>
        </AppShell>
      </ClinicGuard>
    )
  }

  const patientName = getPatientName(patient)

  // ⚠️ The API's own not-found sentence IS this card's heading, so printing both made the whole card
  // « Patient introuvable » twice. Dropped when it matches; the recovery line below is unconditional.
  const detail =
    error && error.replace(/[\s.!]+$/u, "").toLocaleLowerCase("fr") !== "patient introuvable" ? error : null

  const backButton = (
    <Button variant="ghost" onClick={() => router.push(`/patients/${patientId}`)} className="gap-2">
      <ArrowLeft className="h-4 w-4" />
      Retour au patient
    </Button>
  )

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        {backButton}

        {/* AC-P3.27 — an error state instead of the manager under the literal heading « Patient ». */}
        {error ? (
          <div role="status" className="rounded-lg border border-destructive/40 bg-destructive/5 p-6">
            <p className="font-medium text-foreground">Patient introuvable</p>
            {detail ? <p className="mt-1 text-sm text-muted-foreground">{detail}</p> : null}
            <p className="mt-1 text-sm text-muted-foreground">
              Ce dossier a peut-être été supprimé, ou le lien n&apos;est plus valable.
            </p>
            <Button variant="outline" className="mt-4" onClick={() => void loadPatient()}>
              Réessayer
            </Button>
          </div>
        ) : (
          <PatientFilesManager patientName={patientName} />
        )}
      </AppShell>
    </ClinicGuard>
  )
}







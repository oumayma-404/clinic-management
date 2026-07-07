"use client"

import { useParams, useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Button } from "@/components/ui/button"
import { ArrowLeft } from "lucide-react"
import { PatientFilesManager } from "@/components/patient-files-manager"
import { patientsApi } from "@/lib/api/patients"
import { useState, useEffect } from "react"
import type { PatientDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"

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

  useEffect(() => {
    const loadPatient = async () => {
      try {
        setLoading(true)
        const patientData = await patientsApi.get(patientId)
        setPatient(patientData)
      } catch (err) {
        console.error("Failed to load patient:", err)
      } finally {
        setLoading(false)
      }
    }

    if (patientId) {
      loadPatient()
    }
  }, [patientId])

  if (loading) {
    return (
      <ClinicGuard>
        <div className="flex h-screen">
          <DashboardSidebar />
          <div className="flex-1 flex flex-col">
            <DashboardHeader />
            <main className="flex-1 p-6">
              <div className="flex items-center justify-center h-full">
                <p className="text-muted-foreground">Loading...</p>
              </div>
            </main>
          </div>
        </div>
      </ClinicGuard>
    )
  }

  const patientName = getPatientName(patient)

  return (
    <ClinicGuard>
      <div className="flex h-screen">
        <DashboardSidebar />
        <div className="flex-1 flex flex-col">
          <DashboardHeader />
          <main className="flex-1 overflow-y-auto p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Back Button */}
              <Button variant="ghost" onClick={() => router.push(`/patients/${patientId}`)} className="gap-2">
                <ArrowLeft className="h-4 w-4" />
                Back to Patient
              </Button>

              <PatientFilesManager patientName={patientName} />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}







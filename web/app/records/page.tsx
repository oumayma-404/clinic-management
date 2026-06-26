"use client"

import { useState, useEffect } from "react"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { User, Search, X, Loader2, FileText } from "lucide-react"
import { patientsApi } from "@/lib/api/patients"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { PatientSummaryModal } from "@/components/patient-summary-modal"
import type { PatientDto, DentalRecordDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { toast } from "sonner"

export default function MedicalRecordsPage() {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [selectedPatient, setSelectedPatient] = useState<PatientDto | null>(null)
  const [selectedPatientDentalRecords, setSelectedPatientDentalRecords] = useState<DentalRecordDto[]>([])
  const [loading, setLoading] = useState(true)
  const [loadingPatientData, setLoadingPatientData] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [summaryModalOpen, setSummaryModalOpen] = useState(false)

  // Load all patients
  useEffect(() => {
    const loadPatients = async () => {
      try {
        setLoading(true)
        const patientsData = await patientsApi.list()
        setPatients(patientsData)
      } catch (error) {
        console.error("Failed to load patients:", error)
        toast.error("Failed to load patients", {
          description: "Please try again later",
        })
      } finally {
        setLoading(false)
      }
    }
    loadPatients()
  }, [])

  const handlePatientClick = async (patient: PatientDto) => {
    try {
      setLoadingPatientData(true)
      setSelectedPatient(patient)

      // Load full patient data and dental records
      const [fullPatientData, dentalRecordsData] = await Promise.all([
        patientsApi.get(patient.id).catch(() => patient), // Fallback to basic patient data if full load fails
        dentalRecordsApi.list(patient.id).catch(() => []),
      ])

      setSelectedPatient(fullPatientData)
      setSelectedPatientDentalRecords(dentalRecordsData)
      setSummaryModalOpen(true)
    } catch (error) {
      console.error("Failed to load patient data:", error)
      toast.error("Failed to load patient data", {
        description: "Please try again later",
      })
    } finally {
      setLoadingPatientData(false)
    }
  }

  const handleCloseModal = () => {
    setSummaryModalOpen(false)
    setSelectedPatient(null)
    setSelectedPatientDentalRecords([])
  }

  const filteredPatients = patients.filter((patient) => {
    if (!searchQuery) return true
    const query = searchQuery.toLowerCase()
    
    // Search by name
    const nameMatch =
      patient.firstName.toLowerCase().includes(query) ||
      patient.lastName.toLowerCase().includes(query) ||
      `${patient.firstName} ${patient.lastName}`.toLowerCase().includes(query)
    
    // Search by date of birth - format in multiple ways for flexibility
    const dob = new Date(patient.dateOfBirth)
    const dobFormats = [
      dob.toLocaleDateString('en-US'), // MM/DD/YYYY
      dob.toLocaleDateString('en-GB'), // DD/MM/YYYY
      dob.toISOString().split('T')[0], // YYYY-MM-DD
      dob.toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' }), // MM/DD/YYYY
      dob.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }), // MMM DD, YYYY
      dob.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' }), // Month DD, YYYY
    ]
    
    const dobMatch = dobFormats.some(format => 
      format.toLowerCase().includes(query)
    )
    
    return nameMatch || dobMatch
  })

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-auto p-4">
            <div className="mx-auto max-w-[1400px]">
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h1 className="text-3xl font-bold text-foreground">Medical Records</h1>
                    <p className="text-sm text-muted-foreground mt-1">
                      Click on a patient card to view their medical summary
                    </p>
                  </div>
                </div>

                {/* Search */}
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                  <Input
                    placeholder="Search patients by name or date of birth..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    className="pl-10"
                  />
                  {searchQuery && (
                    <Button
                      variant="ghost"
                      size="sm"
                      className="absolute right-2 top-1/2 transform -translate-y-1/2 h-6 w-6 p-0"
                      onClick={() => setSearchQuery("")}
                    >
                      <X className="h-4 w-4" />
                    </Button>
                  )}
                </div>

                {/* Patients Grid */}
                {loading ? (
                  <div className="flex items-center justify-center p-12">
                    <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
                  </div>
                ) : filteredPatients.length === 0 ? (
                  <Card className="p-12 border-dashed">
                    <div className="text-center text-muted-foreground">
                      <FileText className="h-16 w-16 mx-auto mb-4 opacity-50" />
                      <p className="text-lg font-medium">
                        {searchQuery ? "No patients found" : "No patients yet"}
                      </p>
                      <p className="text-sm mt-2">
                        {searchQuery
                          ? "Try a different search term"
                          : "Patients will appear here once they are added to the system"}
                      </p>
                    </div>
                  </Card>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                    {filteredPatients.map((patient) => (
                      <Card
                        key={patient.id}
                        className={cn(
                          "p-4 cursor-pointer hover:shadow-md transition-all duration-200 hover:scale-105 border-border hover:border-primary bg-gradient-to-br from-card to-primary/5 relative group",
                          loadingPatientData && selectedPatient?.id === patient.id && "opacity-50 cursor-wait"
                        )}
                        onClick={() => !loadingPatientData && handlePatientClick(patient)}
                      >
                        <div className="flex flex-col items-center gap-3 text-center">
                          <div className="p-3 rounded-lg bg-primary/10">
                            <User className="h-12 w-12 text-primary" />
                          </div>
                          <div className="flex-1 min-w-0 w-full">
                            <p className="text-base font-semibold truncate text-foreground">
                              {patient.firstName} {patient.lastName}
                            </p>
                            <p className="text-xs text-muted-foreground mt-1">
                              {new Date(patient.dateOfBirth).toLocaleDateString()}
                            </p>
                          </div>
                          {loadingPatientData && selectedPatient?.id === patient.id ? (
                            <Loader2 className="h-5 w-5 animate-spin text-primary" />
                          ) : (
                            <FileText className="h-5 w-5 text-muted-foreground group-hover:text-primary transition-colors" />
                          )}
                        </div>
                      </Card>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </main>
        </div>
      </div>

      {/* Patient Summary Modal */}
      <PatientSummaryModal
        open={summaryModalOpen}
        onOpenChange={handleCloseModal}
        patient={selectedPatient}
        dentalRecords={selectedPatientDentalRecords}
      />
    </ClinicGuard>
  )
}


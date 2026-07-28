"use client"

import { useState, useEffect } from "react"
import { useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { PatientsTable } from "@/components/patients-table"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Search, Filter, Plus } from "lucide-react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function PatientsPage() {
  const router = useRouter()
  const [searchQuery, setSearchQuery] = useState("")
  const [showFlaggedOnly, setShowFlaggedOnly] = useState(false)
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)

  // Real-time: refetch the table when any client of this clinic adds/edits a patient (remounts via key).
  useClinicRealtime(RealtimeResource.Patients, () => setRefreshKey((prev) => prev + 1))

  // Dashboard "Urgents" drill-through: arriving with ?flagged=1 pre-applies the flagged filter. Read from
  // window.location (client-only, in an effect) to avoid a useSearchParams Suspense boundary.
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("flagged") === "1") {
      setShowFlaggedOnly(true)
    }
  }, [])

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-y-auto p-4 md:p-6">
            <div className="mx-auto max-w-7xl space-y-6">
              {/* Page Header */}
              <div>
                <h1 className="text-3xl font-semibold text-foreground">Patients</h1>
                <p className="mt-1 text-sm text-muted-foreground">Consultez et gérez tous les dossiers patients</p>
              </div>

              {/* Search and Filters */}
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="relative flex-1 max-w-md">
                  <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                  <Input
                    type="text"
                    placeholder="Rechercher par nom ou téléphone…"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    className="pl-9"
                  />
                </div>

                <div className="flex gap-2">
                  <Button
                    variant={showFlaggedOnly ? "default" : "outline"}
                    onClick={() => setShowFlaggedOnly(!showFlaggedOnly)}
                    className="gap-2"
                  >
                    <Filter className="h-4 w-4" />
                    {showFlaggedOnly ? "Signalés affichés" : "Afficher les signalés"}
                  </Button>
                  
                  <Button
                    onClick={() => setCreateDialogOpen(true)}
                    className="gap-2"
                  >
                    <Plus className="h-4 w-4" />
                    Ajouter un patient
                  </Button>
                </div>
              </div>

              {/* Patients Table */}
              <PatientsTable key={refreshKey} searchQuery={searchQuery} showFlaggedOnly={showFlaggedOnly} />

              {/* Create Patient Dialog */}
              <EditPatientDialog
                open={createDialogOpen}
                onOpenChange={setCreateDialogOpen}
                patient={null}
                onSuccess={(created) => {
                  setCreateDialogOpen(false)
                  // Open the new patient's detail page so clinical work can start immediately;
                  // fall back to refreshing the list if the id is somehow missing.
                  if (created?.id) {
                    router.push(`/patients/${created.id}`)
                  } else {
                    setRefreshKey(prev => prev + 1)
                  }
                }}
              />
            </div>
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

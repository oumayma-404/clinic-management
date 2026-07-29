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
import { formatDateFr } from "@/lib/format"

export default function PatientsPage() {
  const router = useRouter()
  const [searchQuery, setSearchQuery] = useState("")
  const [showFlaggedOnly, setShowFlaggedOnly] = useState(false)
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)
  // Registration-date window from the dashboard's « Nouveaux patients » drill-through.
  const [createdFrom, setCreatedFrom] = useState<string | undefined>()
  const [createdTo, setCreatedTo] = useState<string | undefined>()

  // Real-time: refetch the table when any client of this clinic adds/edits a patient (remounts via key).
  useClinicRealtime(RealtimeResource.Patients, () => setRefreshKey((prev) => prev + 1))

  // Dashboard drill-throughs: ?flagged=1 pre-applies the flagged filter, and ?createdFrom/?createdTo narrow the list
  // to the patients registered in the window the KPI counted. Read from window.location (client-only, in an effect)
  // to avoid a useSearchParams Suspense boundary.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    if (params.get("flagged") === "1") {
      setShowFlaggedOnly(true)
    }
    // A malformed date is ignored rather than refused — a stale link lands on the full list, never a broken state.
    const from = params.get("createdFrom")
    const to = params.get("createdTo")
    if (from && !Number.isNaN(Date.parse(from))) setCreatedFrom(from)
    if (to && !Number.isNaN(Date.parse(to))) setCreatedTo(to)
  }, [])

  const clearDateWindow = () => {
    setCreatedFrom(undefined)
    setCreatedTo(undefined)
    const url = new URL(window.location.href)
    url.searchParams.delete("createdFrom")
    url.searchParams.delete("createdTo")
    window.history.replaceState({}, "", url)
  }

  const hasDateWindow = Boolean(createdFrom || createdTo)

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

              {/* The active date window, stated explicitly and removable. An invisible filter is how a user
                  concludes their patients have disappeared. */}
              {hasDateWindow && (
                <div
                  role="status"
                  className="flex flex-wrap items-center gap-3 rounded-lg border bg-muted/40 p-3 text-sm"
                >
                  <span className="min-w-0 flex-1">
                    Inscrits {createdFrom ? `du ${formatDateFr(createdFrom)}` : ""}
                    {createdTo ? ` au ${formatDateFr(createdTo)}` : ""}
                  </span>
                  <Button size="sm" variant="outline" onClick={clearDateWindow}>
                    Afficher tous les patients
                  </Button>
                </div>
              )}

              {/* Patients Table */}
              <PatientsTable
                key={refreshKey}
                searchQuery={searchQuery}
                showFlaggedOnly={showFlaggedOnly}
                createdFrom={createdFrom}
                createdTo={createdTo}
              />

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

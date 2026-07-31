"use client"

import { useState, useEffect } from "react"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ListToolbar, FilterChip } from "@/components/ui/list-toolbar"
import { PatientsTable } from "@/components/patients-table"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Plus } from "lucide-react"
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
      <AppShell contentClassName="space-y-6">
        {/* The page's ONE primary action lives in the header, not in the filter row — a create button and a
            filter had the same weight there, which left the row with no single meaning. */}
        <PageHeader
          zone="Dossiers"
          title="Patients"
          subtitle="Rechercher un dossier, ou en créer un."
          actions={
            <Button onClick={() => setCreateDialogOpen(true)} className="gap-2">
              <Plus className="h-4 w-4" />
              Ajouter un patient
            </Button>
          }
        />

        {/* Only what NARROWS the list. « Signalés » is a chip with a stable label and `aria-pressed`, where it
            used to be a Button whose text flipped between « Afficher les signalés » and « Signalés affichés »
            — so the only way to know the filter was on was to read a sentence and infer its tense. */}
        <ListToolbar
          search={{
            value: searchQuery,
            onChange: setSearchQuery,
            placeholder: "Nom ou téléphone…",
            label: "Rechercher un patient",
          }}
        >
          <FilterChip
            label="Signalés"
            active={showFlaggedOnly}
            onToggle={() => setShowFlaggedOnly(!showFlaggedOnly)}
          />
        </ListToolbar>

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
      </AppShell>
    </ClinicGuard>
  )
}

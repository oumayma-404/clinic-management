"use client"

import { useState, useEffect } from "react"
import { ExportButton } from "@/components/ui/export-button"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ListToolbar, FilterChip } from "@/components/ui/list-toolbar"
import { PatientsTable } from "@/components/patients-table"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { ImportPatientsDialog } from "@/components/patients/import-patients-dialog"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Plus, Upload } from "lucide-react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useSession } from "@/lib/auth/session"
import { isAdminOrDoctor } from "@/lib/nav"
import { formatDateFr } from "@/lib/format"

export default function PatientsPage() {
  const router = useRouter()
  const [searchQuery, setSearchQuery] = useState("")
  const [showFlaggedOnly, setShowFlaggedOnly] = useState(false)
  // « Archiver » had no counterpart: an archived patient leaves this list, the header search AND /fichiers, so
  // the « Restaurer » control on their own page could only be reached by someone who already knew their UUID.
  // Off by default — archiving means "stop offering this person", and a list that showed them by default would
  // undo the only thing the action does.
  const [showArchived, setShowArchived] = useState(false)
  const [createDialogOpen, setCreateDialogOpen] = useState(false)
  const [importDialogOpen, setImportDialogOpen] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)
  // L5 — « Importer » is `AdminOrDoctor` server-side; hidden rather than shown-and-refused, for the reason
  // `access-denied-card` documents. Presentation only, the endpoint is authoritative.
  const { user } = useSession()
  const canImport = isAdminOrDoctor(user?.role)
  // Registration-date window from the dashboard's « Nouveaux patients » drill-through.
  const [createdFrom, setCreatedFrom] = useState<string | undefined>()
  const [createdTo, setCreatedTo] = useState<string | undefined>()

  // Real-time: REFETCH the table when any client of this clinic adds/edits a patient.
  //
  // ⚠️ It used to remount it, via `key={refreshKey}`. The table owns the « Modifier » dialog, so a colleague
  // editing any other patient unmounted that dialog mid-edit — `useDirtyGuard` never fired and the typing was
  // gone with nothing said. The signal now goes in as a prop the table folds into its own refetch key.
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
          title="Patients"
          subtitle="Rechercher un dossier, ou en créer un."
          actions={
            // L5 — « Exporter » sits BESIDE the primary action, not in it: exporting is not creating, and the
            // header is where a page's actions live. The filters currently on screen are passed straight
            // through, so the file is the list.
            <div className="flex flex-wrap items-center gap-2">
              <ExportButton
                path="/patients/export"
                label="patients"
                compact
                params={{
                  searchTerm: searchQuery || undefined,
                  flaggedOnly: showFlaggedOnly || undefined,
                  createdFrom,
                  createdTo,
                }}
              />
              {/* L5 — beside « Exporter », the other direction of the same capability. `size="sm"` + the
                  `touch-target` overlay match the export button so the trio reads as one row, and the label
                  collapses below `sm:` where there is no room for three labelled controls. */}
              {canImport && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setImportDialogOpen(true)}
                  className="touch-target gap-1.5"
                  aria-label="Importer des patients depuis un fichier CSV"
                >
                  <Upload className="size-4" aria-hidden="true" />
                  <span className="sr-only sm:not-sr-only">Importer</span>
                </Button>
              )}
              <Button onClick={() => setCreateDialogOpen(true)} className="gap-2">
                <Plus className="h-4 w-4" />
                Ajouter un patient
              </Button>
            </div>
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
          {/* This one WIDENS the list rather than narrowing it, which is why its label says so plainly instead
              of naming a subset: « Archivés » beside « Signalés » would read as "show only the archived ones". */}
          <FilterChip
            label="Avec les archivés"
            active={showArchived}
            onToggle={() => setShowArchived(!showArchived)}
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

        {/* Patients Table.
            The two callbacks exist so the table's empty state can be a real one: this page owns both the create
            dialog and every narrowing control, so without them « Aucun patient » could only ever be a sentence.
            `onClearFilters` clears ALL three — search, the « Signalés » chip and the date window — because the
            user reading « aucun résultat » wants their list back, not a guessing game about which filter did it. */}
        <PatientsTable
          reloadKey={refreshKey}
          searchQuery={searchQuery}
          showFlaggedOnly={showFlaggedOnly}
          showArchived={showArchived}
          createdFrom={createdFrom}
          createdTo={createdTo}
          onCreatePatient={() => setCreateDialogOpen(true)}
          onClearFilters={() => {
            setSearchQuery("")
            setShowFlaggedOnly(false)
            setShowArchived(false)
            clearDateWindow()
          }}
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

        {/* Mounted only for a role that may use it — the dialog's first action is an upload to an `AdminOrDoctor`
            endpoint, so keeping it mounted for a secretary would only make a 403 reachable. */}
        {canImport && (
          <ImportPatientsDialog
            open={importDialogOpen}
            onOpenChange={setImportDialogOpen}
            // Deliberately reloads the list in place instead of navigating: an import creates many patients, so
            // there is no single record to open — unlike « Ajouter un patient » above.
            onImported={() => setRefreshKey((prev) => prev + 1)}
          />
        )}
      </AppShell>
    </ClinicGuard>
  )
}

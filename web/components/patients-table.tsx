"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Users, Flag, FileText, Folder, Trash2, Pencil, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { toast } from "sonner"
import { patientsApi } from "@/lib/api/patients"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import type { PatientDto, DentalRecordDto, PatientDeletionCheckDto } from "@/lib/api/types"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { PatientSummaryModal } from "@/components/patient-summary-modal"
import { useSession } from "@/lib/auth/session"
import { formatDate } from "@/lib/format"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { usePagedList } from "@/lib/hooks/use-paged-list"

interface PatientsTableProps {
  searchQuery: string
  showFlaggedOnly: boolean
  /**
   * Inclusive registration-date bounds (`yyyy-MM-dd`), applied server-side. Set by the dashboard's « Nouveaux
   * patients » drill-through so the list shows exactly the patients that KPI counted.
   */
  createdFrom?: string
  createdTo?: string
}

/**
 * Column widths the loading skeleton mirrors, in the table's own order — Nom, Date de naissance, Téléphone,
 * Email, Signalements, Actions. Kept beside the table so the two cannot drift into different shapes.
 */
const PATIENT_COLUMN_WIDTHS = ["w-[22%]", "w-[16%]", "w-[16%]", "w-[22%]", "w-[14%]", "w-[10%]"] as const

export function PatientsTable({ searchQuery, showFlaggedOnly, createdFrom, createdTo }: PatientsTableProps) {
  const router = useRouter()
  // Bumped to refetch the current page after a mutation (edit / archive / delete). The list is server-paged, so
  // patching a row in place is no longer right: an edit can change the patient's name and therefore which page
  // they belong on, and a delete changes the total the pager renders.
  const [refreshKey, setRefreshKey] = useState(0)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [selectedPatient, setSelectedPatient] = useState<PatientDto | null>(null)
  const [summaryModalOpen, setSummaryModalOpen] = useState(false)
  const [summaryPatient, setSummaryPatient] = useState<PatientDto | null>(null)
  const [summaryDentalRecords, setSummaryDentalRecords] = useState<DentalRecordDto[]>([])
  const [patientToDelete, setPatientToDelete] = useState<PatientDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  // The pre-check runs when the dialog OPENS, so the user learns what blocks the deletion before clicking
  // rather than after. Null while it is still loading.
  const [deletionCheck, setDeletionCheck] = useState<PatientDeletionCheckDto | null>(null)
  const [checkFailed, setCheckFailed] = useState(false)
  // Delete is admin-gated (finding #15) — matches the app's admin-only destructive-action convention.
  const { user } = useSession()
  const isAdmin = user?.role === "admin"

  // Ask the server what blocks this patient as soon as the dialog opens.
  useEffect(() => {
    if (!patientToDelete) {
      setDeletionCheck(null)
      setCheckFailed(false)
      return
    }

    let active = true
    setCheckFailed(false)
    patientsApi
      .deletionCheck(patientToDelete.id)
      .then((check) => { if (active) setDeletionCheck(check) })
      // A failed pre-check must not block the action: fall back to letting the user try, and let the
      // command's own refusal be the authority.
      .catch(() => { if (active) setCheckFailed(true) })

    return () => { active = false }
  }, [patientToDelete])

  const handleConfirmDelete = async () => {
    if (!patientToDelete) return
    try {
      setDeleting(true)
      await patientsApi.delete(patientToDelete.id)
      refreshList()
      toast.success(`Patient « ${patientToDelete.firstName} ${patientToDelete.lastName} » supprimé`)
      setPatientToDelete(null)
    } catch (err) {
      showErrorToast(err, "Échec de la suppression du patient")
    } finally {
      setDeleting(false)
    }
  }

  const handleArchive = async () => {
    if (!patientToDelete) return
    try {
      setDeleting(true)
      await patientsApi.archive(patientToDelete.id)
      // Archived patients leave the list — the list read excludes them.
      refreshList()
      toast.success(`Patient « ${patientToDelete.firstName} ${patientToDelete.lastName} » archivé`)
      setPatientToDelete(null)
    } catch (err) {
      showErrorToast(err, "Échec de l'archivage du patient")
    } finally {
      setDeleting(false)
    }
  }

  // Search, the flag filter, the date window, the ordering and the page are ALL server-side now. The flag filter
  // in particular used to be a `.filter()` over the fetched array, which was only ever equivalent because the
  // fetch returned every patient in the clinic; over a page it would hide flagged patients on other pages and
  // badge a count of "the flagged ones among these 25".
  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      patientsApi.listPaged({
        page,
        pageSize,
        search,
        flaggedOnly: showFlaggedOnly || undefined,
        createdFrom,
        createdTo,
      }),
    [showFlaggedOnly, createdFrom, createdTo],
  )

  const {
    items: patients,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<PatientDto>({ fetchPage, search: searchQuery, refreshKey })

  const refreshList = () => setRefreshKey((key) => key + 1)

  // Calculate age from date of birth
  const calculateAge = (dob: string | undefined) => {
    if (!dob) return null
    try {
      const birthDate = new Date(dob)
      const today = new Date()
      let age = today.getFullYear() - birthDate.getFullYear()
      const monthDiff = today.getMonth() - birthDate.getMonth()
      if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
        age--
      }
      return age
    } catch {
      return null
    }
  }

  const handleRowClick = (patient: PatientDto, e: React.MouseEvent) => {
    // Check if click was on a link or button (let those handle navigation)
    const target = e.target as HTMLElement
    if (target.closest('a') || target.closest('button')) {
      return
    }
    
    // Navigate to patient details page on row click
    router.push(`/patients/${patient.id}`)
  }

  // AC-P3.22 — the missing caller. `editDialogOpen`/`selectedPatient` existed and the dialog was mounted;
  // nothing ever set them, so the patients list had no edit action at all.
  const handleEdit = (patient: PatientDto) => {
    setSelectedPatient(patient)
    setEditDialogOpen(true)
  }

  // AC-P3.23 — refresh the edited ROW, not the page. The dialog hands back the saved patient, so the row is
  // replaced in place; only if that is somehow missing do we fall back to re-reading the list.
  // Refetches instead of patching the row in place (which is what it did before paging). An edit can change the
  // surname the list is ordered by, so the saved patient may not belong on this page any more — replacing the row
  // would leave it visibly out of order until the next load.
  const handleEditSuccess = () => {
    setEditDialogOpen(false)
    refreshList()
  }

  const handleOpenSummary = async (patient: PatientDto) => {
    setSummaryPatient(patient)
    try {
      // Load dental records for the patient
      const records = await dentalRecordsApi.list(patient.id)
      setSummaryDentalRecords(records)
      setSummaryModalOpen(true)
    } catch (err) {
      // Still open the modal — the patient's own details are worth showing — but say the records are
      // missing rather than presenting an empty record list as though the patient had none (AC-P3.33).
      showErrorToast(err, "Les fiches de soins de ce patient n'ont pas pu être chargées.")
      setSummaryDentalRecords([])
      setSummaryModalOpen(true)
    }
  }

  const getPatientName = (patient: PatientDto) => {
    return `${patient.firstName} ${patient.lastName}`.trim()
  }

  const hasActiveFlags = (patient: PatientDto) => {
    return patient.flags && patient.flags.some(flag => flag.isActive)
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Users className="h-5 w-5" />
          Dossiers patients
          <Badge variant="secondary" className="ml-auto">
            {pageInfo.totalCount} {pageInfo.totalCount === 1 ? "patient" : "patients"}
          </Badge>
        </CardTitle>
      </CardHeader>
      <CardContent>
        {error && (
          <div className="mb-4 rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-800 dark:bg-red-950 dark:border-red-800 dark:text-red-200">
            {error}
          </div>
        )}
        {loading ? (
          // AC-P3.35/3.36 — a skeleton shaped like the table it is standing in for, so the page does not
          // jump when the rows arrive. Follows the only existing precedent (`stats-card.tsx`):
          // `animate-pulse rounded bg-muted`, announced once via aria-label rather than per cell.
          <div className="space-y-3" role="status" aria-label="Chargement des patients">
            <div className="flex gap-4 border-b pb-3">
              {PATIENT_COLUMN_WIDTHS.map((width, i) => (
                <div key={i} className={`h-4 animate-pulse rounded bg-muted ${width}`} />
              ))}
            </div>
            {Array.from({ length: 6 }).map((_, row) => (
              <div key={row} className="flex items-center gap-4">
                {PATIENT_COLUMN_WIDTHS.map((width, i) => (
                  <div key={i} className={`h-5 animate-pulse rounded bg-muted ${width}`} />
                ))}
              </div>
            ))}
          </div>
        ) : (
          // `refreshing` dims the rows already on screen instead of blanking them, so a debounced search does not
          // strobe the table between keystrokes.
          <div className={refreshing ? "opacity-60 transition-opacity" : undefined}>
          {/*
            Four unlabelled icon buttons become one menu below `md:` (AC-15). ⚠️ « Non renseigné » is NOT
            carried over: AC-17 omits an absent field rather than printing a placeholder for it — on a phone a
            line that says "there is nothing here" costs the same space as one that says something.
          */}
          <CardList
            className={CARDS_ONLY}
            ariaLabel="Patients"
            items={patients}
            getKey={(p) => p.id}
            title={(p) => getPatientName(p)}
            subtitle={(p) => {
              const age = calculateAge(p.dateOfBirth)
              return age !== null ? `${age} ans` : null
            }}
            status={(p) =>
              hasActiveFlags(p) ? (
                <span className="flex flex-wrap gap-1">
                  {p.flags
                    ?.filter((flag) => flag.isActive)
                    .map((flag) => (
                      <Badge key={flag.id} variant="destructive" className="gap-1">
                        <Flag className="h-3 w-3" />
                        {flag.flagType}
                      </Badge>
                    ))}
                </span>
              ) : null
            }
            /*
              Tapping the card opens the patient's FULL record, the same destination the desktop row click has
              always had (`handleRowClick` → `/patients/{id}`). It used to open the résumé modal instead, so
              the identical gesture led somewhere different depending on the width of the screen — and the
              phone got the read-only summary, which is the lesser of the two: the full page is the one that
              holds the fiches, the odontogramme, the documents, the factures and the devis.

              `href` rather than `onSelect` so the card is a real link: long-press, « ouvrir dans un nouvel
              onglet » and middle-click all behave. « Voir le résumé » stays in the ⋯ menu below, so nothing
              is lost — and the desktop keeps its own résumé icon button.
            */
            href={(p) => `/patients/${p.id}`}
            fields={(p) => [
              { label: "Téléphone", value: p.phoneNumber },
              { label: "Email", value: p.email },
              { label: "Naissance", value: formatDate(p.dateOfBirth) },
            ]}
            actions={(p) => (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon" aria-label={`Actions pour ${getPatientName(p)}`}>
                    <MoreHorizontal className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onSelect={() => handleOpenSummary(p)}>Voir le résumé</DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => handleEdit(p)}>Modifier</DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => router.push(`/patients/${p.id}/files`)}>
                    Fichiers
                  </DropdownMenuItem>
                  {isAdmin && (
                    <DropdownMenuItem
                      className="text-destructive focus:text-destructive"
                      onSelect={() => setPatientToDelete(p)}
                    >
                      Supprimer
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            )}
            empty={
              isSearching
                ? "Aucun patient ne correspond à votre recherche"
                : showFlaggedOnly
                  ? "Aucun patient signalé"
                  : "Aucun patient"
            }
          />
          <Table containerClassName={TABLE_ONLY}>
            <TableHeader>
              <TableRow>
                <TableHead>Nom</TableHead>
                <TableHead>Date de naissance</TableHead>
                <TableHead>Téléphone</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Signalements</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {patients.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6} className="h-24 text-center">
                    <p className="text-muted-foreground">
                      {isSearching
                        ? "Aucun patient ne correspond à votre recherche"
                        : showFlaggedOnly
                          ? "Aucun patient signalé"
                          : "Aucun patient"}
                    </p>
                  </TableCell>
                </TableRow>
              ) : (
                patients.map((patient) => {
                  const age = calculateAge(patient.dateOfBirth)
                  const hasFlags = hasActiveFlags(patient)
                  return (
                    <TableRow 
                      key={patient.id} 
                      onClick={(e) => handleRowClick(patient, e)} 
                      className="cursor-pointer hover:bg-muted/50"
                    >
                      <TableCell className="font-medium">
                        <div>
                          <p className="text-foreground">{getPatientName(patient)}</p>
                          {age !== null && (
                            <p className="text-xs text-muted-foreground">{age} ans</p>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {formatDate(patient.dateOfBirth)}
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {patient.phoneNumber || "Non renseigné"}
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {patient.email || "Non renseigné"}
                      </TableCell>
                      <TableCell>
                        {hasFlags ? (
                          <div className="flex flex-wrap gap-1">
                            {patient.flags?.filter(flag => flag.isActive).map((flag) => (
                              <Badge key={flag.id} variant="destructive" className="gap-1">
                                <Flag className="h-3 w-3" />
                                {flag.flagType}
                              </Badge>
                            ))}
                          </div>
                        ) : (
                          <span className="text-muted-foreground">-</span>
                        )}
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex items-center justify-end gap-2">
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-8 w-8 p-0"
                            onClick={(e) => {
                              e.stopPropagation()
                              handleOpenSummary(patient)
                            }}
                            title="Voir le résumé du patient"
                            aria-label={`Voir le résumé de ${getPatientName(patient)}`}
                          >
                            <FileText className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-8 w-8 p-0"
                            onClick={(e) => {
                              e.stopPropagation()
                              handleEdit(patient)
                            }}
                            title="Modifier le patient"
                            aria-label={`Modifier ${getPatientName(patient)}`}
                          >
                            <Pencil className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-8 w-8 p-0"
                            onClick={(e) => {
                              e.stopPropagation()
                              router.push(`/patients/${patient.id}/files`)
                            }}
                            title="Voir les fichiers du patient"
                            aria-label={`Voir les fichiers de ${getPatientName(patient)}`}
                          >
                            <Folder className="h-4 w-4" />
                          </Button>
                          {isAdmin && (
                            <Button
                              variant="ghost"
                              size="sm"
                              className="h-8 w-8 p-0 text-destructive hover:text-destructive"
                              onClick={(e) => {
                                e.stopPropagation()
                                setPatientToDelete(patient)
                              }}
                              title="Supprimer le patient"
                              aria-label={`Supprimer ${getPatientName(patient)}`}
                            >
                              <Trash2 className="h-4 w-4" />
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  )
                })
              )}
            </TableBody>
          </Table>
          <DataTablePagination
            page={pageInfo}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            loading={refreshing}
            label={["patient", "patients"]}
          />
          </div>
        )}
      </CardContent>

      <EditPatientDialog
        open={editDialogOpen}
        onOpenChange={(open) => {
          setEditDialogOpen(open)
          // Drop the selection on close so a later open cannot briefly show the previous patient.
          if (!open) setSelectedPatient(null)
        }}
        patient={selectedPatient}
        onSuccess={handleEditSuccess}
      />

      <PatientSummaryModal
        open={summaryModalOpen}
        onOpenChange={setSummaryModalOpen}
        patient={summaryPatient}
        dentalRecords={summaryDentalRecords}
      />

      <AlertDialog open={!!patientToDelete} onOpenChange={(open) => { if (!open) setPatientToDelete(null) }}>
        <AlertDialogContent>
          {deletionCheck && !deletionCheck.canDelete ? (
            // Blocked: say what is attached, and offer archiving instead of leaving a dead end.
            <>
              <AlertDialogHeader>
                <AlertDialogTitle>Suppression impossible</AlertDialogTitle>
                <AlertDialogDescription>
                  <span className="font-semibold">{deletionCheck.patientName}</span> ne peut pas être supprimé :
                  des données lui sont rattachées. Rien n'a été supprimé.
                </AlertDialogDescription>
              </AlertDialogHeader>

              <ul className="space-y-1 text-sm">
                {deletionCheck.blockers.map((blocker) => (
                  <li key={blocker.kind} className="flex items-center gap-2">
                    <span className="text-muted-foreground">•</span>
                    {blocker.tab ? (
                      <button
                        type="button"
                        className="underline underline-offset-2 hover:text-foreground"
                        onClick={() => router.push(`/patients/${deletionCheck.patientId}?tab=${blocker.tab}`)}
                      >
                        {blocker.count} {blocker.label}
                      </button>
                    ) : (
                      <span>{blocker.count} {blocker.label}</span>
                    )}
                  </li>
                ))}
              </ul>

              <p className="text-sm text-muted-foreground">
                {deletionCheck.canArchive
                  ? "Vous pouvez archiver ce patient : il disparaît des listes et des recherches, sans rien supprimer, et reste restaurable."
                  : deletionCheck.archiveBlockedReason}
              </p>

              <AlertDialogFooter>
                <AlertDialogCancel disabled={deleting}>Fermer</AlertDialogCancel>
                {deletionCheck.canArchive && (
                  <AlertDialogAction
                    onClick={(e) => { e.preventDefault(); handleArchive() }}
                    disabled={deleting}
                  >
                    {deleting ? "Archivage…" : "Archiver"}
                  </AlertDialogAction>
                )}
              </AlertDialogFooter>
            </>
          ) : (
            // Deletable, still checking, or the check itself failed — let the command be the authority.
            <>
              <AlertDialogHeader>
                <AlertDialogTitle>Supprimer ce patient ?</AlertDialogTitle>
                <AlertDialogDescription>
                  Cela supprimera définitivement{" "}
                  <span className="font-semibold">{patientToDelete?.firstName} {patientToDelete?.lastName}</span>.
                  {" "}Cette action est irréversible.
                  {!deletionCheck && !checkFailed && " Vérification des données liées…"}
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
                <AlertDialogAction
                  onClick={(e) => { e.preventDefault(); handleConfirmDelete() }}
                  disabled={deleting || (!deletionCheck && !checkFailed)}
                  className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                >
                  {deleting ? "Suppression…" : "Supprimer"}
                </AlertDialogAction>
              </AlertDialogFooter>
            </>
          )}
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}

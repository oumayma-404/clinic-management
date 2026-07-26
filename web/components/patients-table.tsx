"use client"

import { useState, useEffect, useMemo } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Users, Flag, FileText, Folder, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { patientsApi } from "@/lib/api/patients"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import type { PatientDto, DentalRecordDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { PatientSummaryModal } from "@/components/patient-summary-modal"
import { useSession } from "@/lib/auth/session"
import { formatDate } from "@/lib/format"

interface PatientsTableProps {
  searchQuery: string
  showFlaggedOnly: boolean
}

export function PatientsTable({ searchQuery, showFlaggedOnly }: PatientsTableProps) {
  const router = useRouter()
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [selectedPatient, setSelectedPatient] = useState<PatientDto | null>(null)
  const [summaryModalOpen, setSummaryModalOpen] = useState(false)
  const [summaryPatient, setSummaryPatient] = useState<PatientDto | null>(null)
  const [summaryDentalRecords, setSummaryDentalRecords] = useState<DentalRecordDto[]>([])
  const [patientToDelete, setPatientToDelete] = useState<PatientDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  // Delete is admin-gated (finding #15) — matches the app's admin-only destructive-action convention.
  const { user } = useSession()
  const isAdmin = user?.role === "admin"

  const handleConfirmDelete = async () => {
    if (!patientToDelete) return
    try {
      setDeleting(true)
      await patientsApi.delete(patientToDelete.id)
      setPatients((prev) => prev.filter((p) => p.id !== patientToDelete.id))
      toast.success(`Patient « ${patientToDelete.firstName} ${patientToDelete.lastName} » supprimé`)
      setPatientToDelete(null)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression du patient")
    } finally {
      setDeleting(false)
    }
  }

  // Load patients from API
  useEffect(() => {
    // Guard against out-of-order responses: each keystroke fires a request; only the latest may apply its
    // result (a slower earlier request must not overwrite a newer one's patients).
    let ignore = false
    const loadPatients = async () => {
      try {
        setLoading(true)
        setError(null)
        const term = searchQuery.trim()
        const data = await patientsApi.list(term ? { searchTerm: term } : undefined)
        if (!ignore) setPatients(data)
      } catch (err) {
        console.error("Failed to load patients:", err)
        if (!ignore) setError(err instanceof ApiError ? err.message : "Échec du chargement des patients")
      } finally {
        if (!ignore) setLoading(false)
      }
    }

    loadPatients()
    return () => {
      ignore = true
    }
  }, [searchQuery]) // Reload when search query changes

  // Filter patients based on flagged status (search is handled by API)
  const filteredPatients = useMemo(() => {
    if (showFlaggedOnly) {
      return patients.filter((patient) => {
        const hasActiveFlags = patient.flags && patient.flags.some(flag => flag.isActive)
        return hasActiveFlags
      })
    }
    return patients
  }, [patients, showFlaggedOnly])

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

  const handleEditSuccess = () => {
    // Reload patients after successful edit
    const loadPatients = async () => {
      try {
        const term = searchQuery.trim()
        const data = await patientsApi.list(term ? { searchTerm: term } : undefined)
        setPatients(data)
      } catch (err) {
        console.error("Failed to reload patients:", err)
      }
    }
    loadPatients()
  }

  const handleOpenSummary = async (patient: PatientDto) => {
    setSummaryPatient(patient)
    try {
      // Load dental records for the patient
      const records = await dentalRecordsApi.list(patient.id)
      setSummaryDentalRecords(records)
      setSummaryModalOpen(true)
    } catch (err) {
      console.error("Failed to load dental records:", err)
      // Still open modal even if records fail to load
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
            {filteredPatients.length} {filteredPatients.length === 1 ? "patient" : "patients"}
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
          <div className="h-24 flex items-center justify-center">
            <p className="text-muted-foreground">Chargement des patients…</p>
          </div>
        ) : (
          <Table>
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
              {filteredPatients.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6} className="h-24 text-center">
                    <p className="text-muted-foreground">
                      {showFlaggedOnly ? "Aucun patient signalé" : searchQuery ? "Aucun patient ne correspond à votre recherche" : "Aucun patient"}
                    </p>
                  </TableCell>
                </TableRow>
              ) : (
                filteredPatients.map((patient) => {
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
                          >
                            <FileText className="h-4 w-4" />
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
        )}
      </CardContent>

      <EditPatientDialog
        open={editDialogOpen}
        onOpenChange={setEditDialogOpen}
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
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce patient ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cela supprimera définitivement{" "}
              <span className="font-semibold">{patientToDelete?.firstName} {patientToDelete?.lastName}</span>.
              Si des données liées (factures, rendez-vous, dossiers) existent, la suppression sera refusée.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => { e.preventDefault(); handleConfirmDelete() }}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}

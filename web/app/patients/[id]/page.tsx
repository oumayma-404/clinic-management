"use client"

import { useState, useEffect } from "react"
import { useParams, useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Separator } from "@/components/ui/separator"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { formatDT } from "@/lib/format"
import {
  ArrowLeft,
  Flag,
  Calendar,
  FileText,
  Sparkles,
  Download,
  Eye,
  User,
  Activity,
  Heart,
  Stethoscope,
  FileCheck,
  CreditCard,
  Bell,
  ImageIcon,
  Folder,
  ChevronRight,
  ChevronDown,
  ChevronUp,
  Pencil,
  X,
  Loader2,
} from "lucide-react"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { patientsApi } from "@/lib/api/patients"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { patientFilesApi } from "@/lib/api/patient-files"
import type { PatientDto, AppointmentDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto, DentalRecordDto, PatientFileDto, PatientFolderDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { format, parseISO } from "date-fns"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { PatientRecordModal } from "@/components/patient-record-modal"
import { PatientSummaryModal } from "@/components/patient-summary-modal"
import { Edit } from "lucide-react"
import { Receipt } from "lucide-react"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"
import { invoicesApi } from "@/lib/api/invoices"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

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

const formatDate = (dateString: string | undefined) => {
  if (!dateString) return "N/A"
  try {
    const date = parseISO(dateString)
    return format(date, "MMM d, yyyy")
  } catch {
    try {
      const date = new Date(dateString)
      return format(date, "MMM d, yyyy")
    } catch {
      return "N/A"
    }
  }
}

const formatDateTime = (dateString: string | undefined) => {
  if (!dateString) return "N/A"
  try {
    const date = parseISO(dateString)
    return format(date, "MMM d, yyyy h:mm a")
  } catch {
    try {
      const date = new Date(dateString)
      return format(date, "MMM d, yyyy h:mm a")
    } catch {
      return "N/A"
    }
  }
}

const getPatientName = (patient: PatientDto) => {
  return `${patient.firstName} ${patient.lastName}`.trim()
}

const formatAddress = (address: PatientDto["address"]) => {
  if (!address) return "Not provided"
  const parts = [address.street, address.city, address.state, address.zipCode].filter(Boolean)
  return parts.join(", ") || "Not provided"
}

const hasActiveFlags = (patient: PatientDto) => {
  return patient.flags && patient.flags.some(flag => flag.isActive)
}

export default function PatientDetailsPage() {
  const params = useParams()
  const router = useRouter()
  const patientId = params.id as string
  const [patient, setPatient] = useState<PatientDto | null>(null)
  const [appointments, setAppointments] = useState<AppointmentDto[]>([])
  const [medicalHistoryEntries, setMedicalHistoryEntries] = useState<PatientMedicalHistoryDto[]>([])
  const [familyHistoryEntries, setFamilyHistoryEntries] = useState<PatientFamilyHistoryDto[]>([])
  const [dentalRecords, setDentalRecords] = useState<DentalRecordDto[]>([])
  const [files, setFiles] = useState<PatientFileDto[]>([])
  const [folders, setFolders] = useState<PatientFolderDto[]>([])
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  const [recordModalOpen, setRecordModalOpen] = useState(false)
  const [editingRecord, setEditingRecord] = useState<DentalRecordDto | null>(null)
  const [summaryModalOpen, setSummaryModalOpen] = useState(false)
  const [expandedNotes, setExpandedNotes] = useState<Set<string>>(new Set())
  // Dental records already tied to a non-cancelled invoice (guards against double-invoicing).
  const [invoicedDentalRecordIds, setInvoicedDentalRecordIds] = useState<Set<string>>(new Set())
  // The dental record being invoiced (drives the pre-filled invoice modal); null = closed.
  const [billingRecord, setBillingRecord] = useState<DentalRecordDto | null>(null)
  const [previewFile, setPreviewFile] = useState<PatientFileDto | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)
  const [aiSummary, setAiSummary] = useState("")
  const [aiLoading, setAiLoading] = useState(false)
  const [aiError, setAiError] = useState(false)
  const { internetReachable } = useConnectivity()

  // Real AI summary (HuggingFace via GET /patients/{id}/ai-summary). Offline (Local) → skip + FR fallback.
  const loadAiSummary = async () => {
    if (!internetReachable) {
      setAiSummary("")
      setAiError(true)
      return
    }
    setAiLoading(true)
    setAiError(false)
    try {
      const res = await patientsApi.getAiSummary(patientId)
      setAiSummary(res.summary || "")
      setAiError(!res.summary)
    } catch {
      setAiSummary("")
      setAiError(true)
    } finally {
      setAiLoading(false)
    }
  }

  // Auto-generate on page open; re-run automatically when internet becomes reachable again (AC-5/AC-7).
  useEffect(() => {
    if (patientId) {
      loadAiSummary()
    }
  }, [patientId, internetReachable])

  // Real-time: when any client of this clinic edits this patient's record, appointments, or files, the
  // server signals the resource and we re-run the loader below (bump refreshKey). Additive (AC-5).
  useClinicRealtime(
    [RealtimeResource.Patients, RealtimeResource.Appointments, RealtimeResource.Files],
    () => setRefreshKey((k) => k + 1),
  )

  // Load patient data
  useEffect(() => {
    const loadPatientData = async () => {
      try {
        setLoading(true)
        setError(null)
        
        // Load patient
        const patientData = await patientsApi.get(patientId)
        setPatient(patientData)
        
        // Load patient appointments
        const appointmentsData = await appointmentsApi.list({ patientId })
        setAppointments(appointmentsData)
        
        // Load medical and family history entries, dental records, files, folders, and invoices
        // (invoices power the "already billed" guard on the dental-records list).
        const [medicalHistory, familyHistory, dentalRecordsData, filesData, foldersData, invoicesData] = await Promise.all([
          patientMedicalHistoryApi.list(patientId).catch(() => []),
          patientFamilyHistoryApi.list(patientId).catch(() => []),
          dentalRecordsApi.list(patientId).catch(() => []),
          patientFilesApi.getFiles(patientId).catch(() => []),
          patientFilesApi.getFolders(patientId).catch(() => []),
          invoicesApi.list({ patientId }).catch(() => [])
        ])
        setMedicalHistoryEntries(medicalHistory)
        setFamilyHistoryEntries(familyHistory)
        setDentalRecords(dentalRecordsData)
        setFiles(filesData)
        setFolders(foldersData)
        // A dental record counts as "already invoiced" only if a NON-cancelled invoice links to it
        // (a cancelled invoice frees it for re-billing) — via the header link OR any line link (a
        // multi-record note d'honoraires links each billed record at the line level). Safe degradation:
        // a failed invoices fetch yields an empty set, so the Facturer action stays available.
        const invoicedIds = new Set<string>()
        for (const inv of invoicesData) {
          if (inv.status === "Cancelled") continue
          if (inv.dentalRecordId) invoicedIds.add(inv.dentalRecordId)
          for (const line of inv.lines ?? []) {
            if (line.dentalRecordId) invoicedIds.add(line.dentalRecordId)
          }
        }
        setInvoicedDentalRecordIds(invoicedIds)
      } catch (err) {
        console.error("Failed to load patient data:", err)
        setError(err instanceof ApiError ? err.message : "Failed to load patient data")
      } finally {
        setLoading(false)
      }
    }

    if (patientId) {
      loadPatientData()
    }
  }, [patientId, refreshKey])

  // Reload files when folder changes
  useEffect(() => {
    const loadFilesForFolder = async () => {
      if (!patientId) return
      try {
        const filesData = await patientFilesApi.getFiles(patientId, currentFolderId || undefined).catch(() => [])
        setFiles(filesData)
      } catch (error) {
        console.error("Failed to load files:", error)
      }
    }
    loadFilesForFolder()
  }, [patientId, currentFolderId])

  const handleEditSuccess = () => {
    // Reload patient data after successful edit
    const loadPatientData = async () => {
      try {
        const patientData = await patientsApi.get(patientId)
        setPatient(patientData)
        
        // Reload appointments as well
        const appointmentsData = await appointmentsApi.list({ patientId })
        setAppointments(appointmentsData)
        
        // Reload medical and family history entries, dental records, files, and folders
        const [medicalHistory, familyHistory, dentalRecordsData, filesData, foldersData] = await Promise.all([
          patientMedicalHistoryApi.list(patientId).catch(() => []),
          patientFamilyHistoryApi.list(patientId).catch(() => []),
          dentalRecordsApi.list(patientId).catch(() => []),
          patientFilesApi.getFiles(patientId).catch(() => []),
          patientFilesApi.getFolders(patientId).catch(() => [])
        ])
        setMedicalHistoryEntries(medicalHistory)
        setFamilyHistoryEntries(familyHistory)
        setDentalRecords(dentalRecordsData)
        setFiles(filesData)
        setFolders(foldersData)
      } catch (err) {
        console.error("Failed to reload patient data:", err)
      }
    }
    loadPatientData()
  }

  if (loading) {
    return (
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex flex-1 items-center justify-center">
            <div className="text-center">
              <p className="text-muted-foreground">Loading patient data...</p>
            </div>
          </main>
        </div>
      </div>
    )
  }

  if (error || !patient) {
    return (
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex flex-1 items-center justify-center">
            <div className="text-center">
              <h2 className="text-2xl font-semibold text-foreground">Patient Not Found</h2>
              <p className="mt-2 text-muted-foreground">
                {error || "The patient you are looking for does not exist."}
              </p>
              <Button onClick={() => router.push("/patients")} className="mt-4">
                Back to Patients
              </Button>
            </div>
          </main>
        </div>
      </div>
    )
  }

  const patientName = getPatientName(patient)
  const age = calculateAge(patient.dateOfBirth)
  const hasFlags = hasActiveFlags(patient)
  
  // Compute current files based on folder selection
  // When in a folder, all loaded files belong to that folder
  // When at root, show only root files (files without folderId)
  const currentFiles = currentFolderId
    ? files // All files loaded are for this folder
    : files.filter(f => !f.folderId) // Root files (not in any folder)
  
  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return bytes + " B"
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB"
    return (bytes / (1024 * 1024)).toFixed(1) + " MB"
  }

  const isImageFile = (file: PatientFileDto) => {
    return file.contentType.startsWith("image/")
  }

  const isPdfFile = (file: PatientFileDto) => {
    return file.contentType === "application/pdf" || file.fileName.toLowerCase().endsWith(".pdf")
  }

  const isPreviewableFile = (file: PatientFileDto) => {
    return isImageFile(file) || isPdfFile(file)
  }

  const handlePreviewFile = async (file: PatientFileDto) => {
    try {
      setPreviewLoading(true)
      setPreviewFile(file)
      
      // For previewable files, load the blob for preview
      if (isPreviewableFile(file)) {
        const blob = await patientFilesApi.downloadFile(patientId, file.id)
        const url = window.URL.createObjectURL(blob)
        setPreviewUrl(url)
      } else {
        // For non-previewable files, just show the dialog without loading the file
        setPreviewUrl(null)
      }
    } catch (error) {
      console.error("Failed to preview file:", error)
      setPreviewFile(null)
    } finally {
      setPreviewLoading(false)
    }
  }

  const handleClosePreview = () => {
    if (previewUrl) {
      window.URL.revokeObjectURL(previewUrl)
    }
    setPreviewFile(null)
    setPreviewUrl(null)
    setPreviewLoading(false)
  }

  const handleDownloadFile = async (file: PatientFileDto) => {
    try {
      const blob = await patientFilesApi.downloadFile(patientId, file.id)
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      a.download = file.fileName
      document.body.appendChild(a)
      a.click()
      window.URL.revokeObjectURL(url)
      document.body.removeChild(a)
    } catch (error) {
      console.error("Failed to download file:", error)
    }
  }
  
  // Parse allergies from string (comma-separated)
  const allergiesList = patient.allergies 
    ? patient.allergies.split(',').map(a => a.trim()).filter(Boolean)
    : []
  
  // Parse medical history (if it contains structured data, otherwise show as text)
  const medicalHistoryText = patient.medicalHistory || "None reported"
  

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

        <main className="flex-1 overflow-y-auto p-6">
          <div className="mx-auto max-w-7xl space-y-6">
            {/* Back Button */}
            <Button variant="ghost" onClick={() => router.push("/patients")} className="gap-2">
              <ArrowLeft className="h-4 w-4" />
              Back to Patients
            </Button>

            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <h1 className="text-3xl font-semibold text-foreground">{patientName}</h1>
                {hasFlags && (
                  <div className="flex gap-1">
                    {patient.flags?.filter(flag => flag.isActive).map((flag) => (
                      <Badge key={flag.id} variant="destructive" className="gap-1">
                        <Flag className="h-3 w-3" />
                        {flag.flagType}
                      </Badge>
                    ))}
                  </div>
                )}
              </div>
              <div className="flex gap-2">
                <Button variant="default" onClick={() => setSummaryModalOpen(true)} className="gap-2">
                  <Eye className="h-4 w-4" />
                  Patient Summary
                </Button>
                <Button variant="outline" onClick={() => setEditDialogOpen(true)} className="gap-2">
                  <Edit className="h-4 w-4" />
                  Edit Patient
                </Button>
                <Button variant="outline" onClick={() => setRecordModalOpen(true)} className="gap-2">
                  <FileText className="h-4 w-4" />
                  Add Medical Record
                </Button>
                <Button onClick={() => router.push(`/appointments?patientId=${patient.id}`)}>
                  Schedule Appointment
                </Button>
              </div>
            </div>

            <Card className="border-blue-200 bg-blue-50/50 dark:border-blue-900 dark:bg-blue-950/20">
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-blue-700 dark:text-blue-400">
                  <Sparkles className="h-5 w-5" />
                  AI-Generated Patient Summary
                </CardTitle>
                <CardDescription>Automatically generated overview based on patient records</CardDescription>
              </CardHeader>
              <CardContent>
                {aiLoading ? (
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Génération du résumé…
                  </div>
                ) : aiError || !aiSummary ? (
                  <p className="text-sm text-muted-foreground">
                    {internetReachable
                      ? "Résumé indisponible pour le moment. Cliquez sur « Régénérer » pour réessayer."
                      : "Connexion internet requise pour générer le résumé."}
                  </p>
                ) : (
                  <div className="text-sm leading-relaxed text-foreground whitespace-pre-line">{aiSummary}</div>
                )}
                <div className="mt-4 flex items-center gap-2">
                  <Button variant="outline" size="sm" onClick={loadAiSummary} disabled={aiLoading} className="gap-2">
                    <Sparkles className="h-3 w-3" />
                    Régénérer
                  </Button>
                </div>
              </CardContent>
            </Card>

            <Tabs defaultValue="medical-records" className="space-y-4">
              <TabsList className="grid w-full grid-cols-5">
                <TabsTrigger value="medical-records" className="gap-2">
                  <FileCheck className="h-4 w-4" />
                  Medical Records
                </TabsTrigger>
                <TabsTrigger value="appointments" className="gap-2">
                  <Calendar className="h-4 w-4" />
                  Appointments
                </TabsTrigger>
                <TabsTrigger value="notes" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Notes
                </TabsTrigger>
                <TabsTrigger value="files" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Files
                </TabsTrigger>
                <TabsTrigger value="factures" className="gap-2">
                  <Receipt className="h-4 w-4" />
                  Factures
                </TabsTrigger>
              </TabsList>

              {/* Medical Records Tab - Unified View */}
              <TabsContent value="medical-records" className="space-y-4">
                {/* Dental Records Section */}
                <Card>
                  <CardHeader>
                    <div className="flex items-center justify-between">
                      <div>
                        <CardTitle className="flex items-center gap-2">
                          <FileCheck className="h-5 w-5" />
                          Dental Records
                        </CardTitle>
                        <CardDescription>Complete history of dental procedures and interventions</CardDescription>
                      </div>
                      <Button onClick={() => {
                        setEditingRecord(null)
                        setRecordModalOpen(true)
                      }} size="sm">
                        Add Dental Record
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {dentalRecords.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">No dental records found</p>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Date</TableHead>
                              <TableHead>Procedure Type</TableHead>
                              <TableHead>Teeth Type</TableHead>
                              <TableHead>Teeth</TableHead>
                              <TableHead>Amount Paid</TableHead>
                              <TableHead>Reste</TableHead>
                              <TableHead>Notes</TableHead>
                              <TableHead className="text-right">Actions</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {dentalRecords.map((record) => (
                              <TableRow key={record.id}>
                                <TableCell className="font-medium">
                                  {formatDate(record.interventionDate)}
                                </TableCell>
                                <TableCell>{record.procedureType}</TableCell>
                                <TableCell>
                                  <Badge variant="outline">
                                    {record.isAdultTeeth ? "Adult" : "Child"}
                                  </Badge>
                                </TableCell>
                                <TableCell>
                                  {record.toothNumbers.length > 0 ? (
                                    <div className="flex flex-wrap gap-1">
                                      {record.toothNumbers.map((toothNum) => (
                                        <Badge key={toothNum} variant="secondary" className="text-xs">
                                          {toothNum}
                                        </Badge>
                                      ))}
                                    </div>
                                  ) : (
                                    <span className="text-muted-foreground text-sm">-</span>
                                  )}
                                </TableCell>
                                <TableCell>{formatDT(record.amountPaid)}</TableCell>
                                <TableCell>
                                  {(() => {
                                    const reste = Math.max(0, record.balance ?? (record.cost - record.amountPaid))
                                    return reste > 0
                                      ? <span className="font-semibold text-amber-600">{formatDT(reste)}</span>
                                      : <span className="text-muted-foreground">{formatDT(0)}</span>
                                  })()}
                                </TableCell>
                                <TableCell className="max-w-xs">
                                  {(() => {
                                    const hasNotes = (record.notes && record.notes.length > 0) || (record.importantNotes && record.importantNotes.length > 0)
                                    const isExpanded = expandedNotes.has(record.id)
                                    const totalNotesCount = (record.importantNotes?.length || 0) + (record.notes?.length || 0)

                                    if (!hasNotes) {
                                      return <span className="text-muted-foreground text-sm">-</span>
                                    }

                                    return (
                                      <div className="space-y-1">
                                        {isExpanded ? (
                                          <div className="space-y-2">
                                            {record.importantNotes && record.importantNotes.length > 0 && (
                                              <div className="space-y-1">
                                                <p className="text-xs font-semibold text-amber-700 dark:text-amber-400 mb-1">
                                                  Important Notes:
                                                </p>
                                                <ul className="list-disc list-inside space-y-1 ml-2">
                                                  {record.importantNotes.map((note, idx) => (
                                                    <li key={idx} className="text-xs font-medium text-amber-900 dark:text-amber-100 bg-amber-50 dark:bg-amber-950/40 px-2 py-1 rounded border border-amber-200 dark:border-amber-800">
                                                      ⚠ {note}
                                                    </li>
                                                  ))}
                                                </ul>
                                              </div>
                                            )}
                                            {record.notes && record.notes.length > 0 && (
                                              <div className="space-y-1">
                                                {record.importantNotes && record.importantNotes.length > 0 && (
                                                  <p className="text-xs font-semibold text-muted-foreground mb-1">
                                                    Notes:
                                                  </p>
                                                )}
                                                <ul className="list-disc list-inside space-y-1 ml-2">
                                                  {record.notes.map((note, idx) => (
                                                    <li key={idx} className="text-sm text-foreground bg-muted/50 px-2 py-1 rounded">
                                                      {note}
                                                    </li>
                                                  ))}
                                                </ul>
                                              </div>
                                            )}
                                            <Button
                                              variant="ghost"
                                              size="sm"
                                              className="h-6 text-xs text-muted-foreground hover:text-foreground"
                                              onClick={(e) => {
                                                e.stopPropagation()
                                                setExpandedNotes(prev => {
                                                  const next = new Set(prev)
                                                  next.delete(record.id)
                                                  return next
                                                })
                                              }}
                                            >
                                              <ChevronUp className="h-3 w-3 mr-1" />
                                              Collapse
                                            </Button>
                                          </div>
                                        ) : (
                                          <div className="space-y-1">
                                            <div className="flex items-center gap-2">
                                              <span className="text-sm text-muted-foreground">
                                                {totalNotesCount} {totalNotesCount === 1 ? 'note' : 'notes'}
                                              </span>
                                              {record.importantNotes && record.importantNotes.length > 0 && (
                                                <Badge variant="outline" className="text-xs bg-amber-50 dark:bg-amber-950/20 text-amber-700 dark:text-amber-400 border-amber-200 dark:border-amber-800">
                                                  {record.importantNotes.length} important
                                                </Badge>
                                              )}
                                            </div>
                                            <Button
                                              variant="ghost"
                                              size="sm"
                                              className="h-6 text-xs text-muted-foreground hover:text-foreground"
                                              onClick={(e) => {
                                                e.stopPropagation()
                                                setExpandedNotes(prev => new Set(prev).add(record.id))
                                              }}
                                            >
                                              <ChevronDown className="h-3 w-3 mr-1" />
                                              View notes
                                            </Button>
                                          </div>
                                        )}
                                      </div>
                                    )
                                  })()}
                                </TableCell>
                                <TableCell className="text-right">
                                  <div className="flex items-center justify-end gap-1">
                                    {invoicedDentalRecordIds.has(record.id) ? (
                                      <Badge variant="outline" className="text-xs gap-1">
                                        <Receipt className="h-3 w-3" />
                                        Facturé
                                      </Badge>
                                    ) : (
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-8 w-8 p-0"
                                        onClick={() => setBillingRecord(record)}
                                        title="Facturer cette intervention"
                                      >
                                        <Receipt className="h-4 w-4" />
                                      </Button>
                                    )}
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      className="h-8 w-8 p-0"
                                      onClick={() => {
                                        setEditingRecord(record)
                                        setRecordModalOpen(true)
                                      }}
                                      title="Edit record"
                                    >
                                      <Pencil className="h-4 w-4" />
                                    </Button>
                                  </div>
                                </TableCell>
                              </TableRow>
                            ))}
                          </TableBody>
                        </Table>
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Notes Tab */}
              <TabsContent value="notes">
                <Card>
                  <CardHeader>
                    <CardTitle>Medical Records Notes</CardTitle>
                    <CardDescription>Notes and important notes from dental records</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {dentalRecords.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">No medical records found</p>
                    ) : (
                      <div className="space-y-4">
                        {dentalRecords
                          .filter(record => 
                            (record.notes && record.notes.length > 0) || 
                            (record.importantNotes && record.importantNotes.length > 0)
                          )
                          .map((record) => (
                          <div key={record.id} className="rounded-lg border bg-card p-4 space-y-3">
                            <div className="flex items-center justify-between">
                              <div className="flex items-center gap-2">
                                <p className="text-sm font-medium text-foreground">
                                  {record.procedureType}
                                </p>
                                <Badge variant="outline" className="text-xs">
                                  {formatDate(record.interventionDate)}
                                </Badge>
                              </div>
                            </div>
                            
                            {/* Important Notes - Highlighted */}
                            {record.importantNotes && record.importantNotes.length > 0 && (
                              <div className="space-y-2">
                                <p className="text-xs font-semibold text-amber-700 dark:text-amber-400 uppercase tracking-wide">
                                  Important Notes
                                </p>
                                <div className="space-y-2">
                                  {record.importantNotes.map((note, idx) => (
                                    <div 
                                      key={idx} 
                                      className="text-sm font-medium text-amber-900 dark:text-amber-100 bg-amber-50 dark:bg-amber-950/40 px-3 py-2 rounded border border-amber-200 dark:border-amber-800"
                                    >
                                      ⚠ {note}
                                    </div>
                                  ))}
                                </div>
                              </div>
                            )}

                            {/* Regular Notes */}
                            {record.notes && record.notes.length > 0 && (
                              <div className="space-y-2">
                                {record.importantNotes && record.importantNotes.length > 0 && (
                                  <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                                    Notes
                                  </p>
                                )}
                                <div className="space-y-2">
                                  {record.notes.map((note, idx) => (
                                    <p 
                                      key={idx} 
                                      className="text-sm text-foreground bg-muted/50 px-3 py-2 rounded"
                                    >
                                      {note}
                                    </p>
                                  ))}
                                </div>
                              </div>
                            )}
                          </div>
                        ))}
                        {dentalRecords.filter(record => 
                          (record.notes && record.notes.length > 0) || 
                          (record.importantNotes && record.importantNotes.length > 0)
                        ).length === 0 && (
                          <p className="text-center text-muted-foreground py-8">No notes found in medical records</p>
                        )}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Appointments Tab - Merged with Procedures */}
              <TabsContent value="appointments">
                <Card>
                  <CardHeader>
                    <CardTitle>Appointment History</CardTitle>
                    <CardDescription>Complete history of all appointments and procedures</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {appointments.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">No appointments found</p>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Date & Time</TableHead>
                              <TableHead>Procedure/Type</TableHead>
                              <TableHead>Doctor</TableHead>
                              <TableHead>Duration</TableHead>
                              <TableHead>Status</TableHead>
                              <TableHead>Notes</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {appointments
                              .sort((a, b) => {
                                const dateA = new Date(a.appointmentDateTime).getTime()
                                const dateB = new Date(b.appointmentDateTime).getTime()
                                return dateB - dateA // Sort descending (newest first)
                              })
                              .map((appointment) => {
                                const durationMinutes = appointment.duration 
                                  ? parseInt(appointment.duration.split(':')[0]) * 60 + parseInt(appointment.duration.split(':')[1] || '0')
                                  : 0
                                
                                // Determine row color based on status and procedure type
                                const isCanceled = appointment.status === "Cancelled"
                                const rowColor = isCanceled 
                                  ? "bg-muted/50" 
                                  : appointment.procedureColorHex 
                                    ? undefined 
                                    : "bg-background"
                                
                                const borderColor = isCanceled 
                                  ? undefined 
                                  : appointment.procedureColorHex 
                                    ? appointment.procedureColorHex 
                                    : undefined
                                
                                return (
                                  <TableRow 
                                    key={appointment.id}
                                    className={rowColor}
                                    style={borderColor ? { borderLeft: `4px solid ${borderColor}` } : undefined}
                                  >
                                    <TableCell className="font-medium">
                                      {formatDateTime(appointment.appointmentDateTime)}
                                    </TableCell>
                                    <TableCell>
                                      {appointment.procedureTypeName ? (
                                        <div className="flex items-center gap-2">
                                          <div
                                            className="h-3 w-3 rounded-full"
                                            style={{ backgroundColor: appointment.procedureColorHex || "#6C757D" }}
                                          />
                                          <span>{appointment.procedureTypeName}</span>
                                        </div>
                                      ) : (
                                        <span className="text-muted-foreground">General Appointment</span>
                                      )}
                                    </TableCell>
                                    <TableCell>
                                      {appointment.doctorName || (
                                        <span className="text-muted-foreground">-</span>
                                      )}
                                    </TableCell>
                                    <TableCell>
                                      {durationMinutes > 0 ? (
                                        `${durationMinutes} min`
                                      ) : (
                                        <span className="text-muted-foreground">-</span>
                                      )}
                                    </TableCell>
                                    <TableCell>
                                      <Badge 
                                        variant={
                                          appointment.status === "Scheduled" || appointment.status === "Confirmed" 
                                            ? "default" 
                                            : appointment.status === "Completed"
                                            ? "secondary"
                                            : appointment.status === "Cancelled"
                                            ? "outline"
                                            : "outline"
                                        }
                                        className={isCanceled ? "bg-muted text-muted-foreground" : undefined}
                                      >
                                        {appointment.status}
                                      </Badge>
                                    </TableCell>
                                <TableCell className="max-w-xs">
                                  {appointment.notes ? (
                                    <p className="text-sm truncate" title={appointment.notes}>
                                      {appointment.notes}
                                    </p>
                                  ) : (
                                    <span className="text-muted-foreground text-sm">-</span>
                                  )}
                                </TableCell>
                                  </TableRow>
                                )
                              })}
                          </TableBody>
                        </Table>
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Files Tab */}
              <TabsContent value="files">
                <Card>
                  <CardHeader>
                    <div className="flex items-center justify-between">
                      <div>
                        <CardTitle>Patient Files</CardTitle>
                        <CardDescription>
                          {currentFolderId 
                            ? `Files in folder` 
                            : `All uploaded files and documents (${files.length} file${files.length !== 1 ? 's' : ''})`}
                        </CardDescription>
                      </div>
                      <div className="flex items-center gap-2">
                        {currentFolderId && (
                          <Button 
                            variant="outline" 
                            size="sm"
                            onClick={() => setCurrentFolderId(null)}
                            className="gap-2"
                          >
                            <ArrowLeft className="h-4 w-4" />
                            Back
                          </Button>
                        )}
                        <Button onClick={() => router.push(`/patients/${patientId}/files`)} variant="default">
                          Manage Files
                        </Button>
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {files.length === 0 && folders.length === 0 ? (
                      <div className="text-center py-8">
                        <FileText className="h-12 w-12 mx-auto mb-3 opacity-50 text-muted-foreground" />
                        <p className="text-sm text-muted-foreground mb-4">
                          No files uploaded yet
                        </p>
                        <Button onClick={() => router.push(`/patients/${patientId}/files`)}>
                          Upload Files
                        </Button>
                      </div>
                    ) : (
                      <div className="space-y-4">
                        {/* Folders List (only show at root level) */}
                        {!currentFolderId && folders.length > 0 && (
                          <div>
                            <h3 className="text-sm font-semibold mb-3 text-foreground">Folders</h3>
                            <div className="space-y-2">
                              {folders.map((folder) => (
                                <Card
                                  key={folder.id}
                                  className="p-3 cursor-pointer hover:bg-accent transition-colors hover:border-blue-300 dark:hover:border-blue-700"
                                  onClick={() => setCurrentFolderId(folder.id)}
                                >
                                  <div className="flex items-center justify-between">
                                    <div className="flex items-center gap-3 flex-1 min-w-0">
                                      <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-900/30">
                                        <Folder className="h-5 w-5 text-blue-600 dark:text-blue-400" />
                                      </div>
                                      <div className="flex-1 min-w-0">
                                        <p className="text-sm font-semibold truncate text-foreground">{folder.name}</p>
                                        <p className="text-xs text-muted-foreground">
                                          {folder.fileCount} {folder.fileCount === 1 ? "file" : "files"}
                                        </p>
                                      </div>
                                    </div>
                                    <ChevronRight className="h-4 w-4 text-muted-foreground" />
                                  </div>
                                </Card>
                              ))}
                            </div>
                          </div>
                        )}

                        {/* Files List */}
                        {currentFiles.length === 0 ? (
                          <div>
                            <h3 className="text-sm font-semibold mb-3 text-foreground">
                              {currentFolderId ? "Files in this folder" : "Files"}
                            </h3>
                            <Card className="p-8 border-dashed">
                              <div className="text-center text-muted-foreground">
                                <FileText className="h-12 w-12 mx-auto mb-3 opacity-50" />
                                <p className="text-sm">
                                  {currentFolderId ? "No files in this folder" : "No files in root"}
                                </p>
                              </div>
                            </Card>
                          </div>
                        ) : (
                          <div>
                            <h3 className="text-sm font-semibold mb-3 text-foreground">
                              {currentFolderId ? "Files in this folder" : "Files"}
                            </h3>
                            <div className="overflow-x-auto">
                              <Table>
                                <TableHeader>
                                  <TableRow>
                                    <TableHead>File Name</TableHead>
                                    <TableHead>Type</TableHead>
                                    <TableHead>Size</TableHead>
                                    <TableHead>Uploaded</TableHead>
                                    <TableHead className="text-right">Actions</TableHead>
                                  </TableRow>
                                </TableHeader>
                                <TableBody>
                                  {currentFiles
                                    .sort((a, b) => {
                                      const dateA = new Date(a.uploadedAt).getTime()
                                      const dateB = new Date(b.uploadedAt).getTime()
                                      return dateB - dateA // Sort descending (newest first)
                                    })
                                    .map((file) => {
                                      const isImage = isImageFile(file)
                                      const isPdf = isPdfFile(file)
                                      const isPreviewable = isPreviewableFile(file)

                                      return (
                                        <TableRow 
                                          key={file.id}
                                          className="cursor-pointer hover:bg-muted/50"
                                          onClick={() => handlePreviewFile(file)}
                                        >
                                          <TableCell className="font-medium">
                                            <div className="flex items-center gap-2">
                                              {isImage ? (
                                                <ImageIcon className="h-4 w-4 text-muted-foreground" />
                                              ) : isPdf ? (
                                                <FileText className="h-4 w-4 text-muted-foreground" />
                                              ) : (
                                                <FileText className="h-4 w-4 text-muted-foreground" />
                                              )}
                                              <span className="truncate max-w-xs" title={file.fileName}>
                                                {file.fileName}
                                              </span>
                                            </div>
                                          </TableCell>
                                          <TableCell>
                                            <Badge variant="outline" className="text-xs">
                                              {file.fileType || file.contentType.split('/')[1] || 'Unknown'}
                                            </Badge>
                                          </TableCell>
                                          <TableCell className="text-muted-foreground">
                                            {formatFileSize(file.fileSize)}
                                          </TableCell>
                                          <TableCell className="text-muted-foreground">
                                            {formatDate(file.uploadedAt)}
                                          </TableCell>
                                          <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                                            <div className="flex items-center justify-end gap-2">
                                              {isPreviewable && (
                                                <Button
                                                  variant="ghost"
                                                  size="sm"
                                                  className="h-8 w-8 p-0"
                                                  onClick={() => handlePreviewFile(file)}
                                                  title="Preview file"
                                                >
                                                  <Eye className="h-4 w-4" />
                                                </Button>
                                              )}
                                              <Button
                                                variant="ghost"
                                                size="sm"
                                                className="h-8 w-8 p-0"
                                                onClick={() => handleDownloadFile(file)}
                                                title="Download file"
                                              >
                                                <Download className="h-4 w-4" />
                                              </Button>
                                            </div>
                                          </TableCell>
                                        </TableRow>
                                      )
                                    })}
                                </TableBody>
                              </Table>
                            </div>
                          </div>
                        )}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Factures Tab */}
              <TabsContent value="factures" className="space-y-4">
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2">
                      <Receipt className="h-5 w-5" />
                      Factures
                    </CardTitle>
                    <CardDescription>Notes d'honoraires du patient — création, émission, paiement et PDF.</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <InvoicesTable patientId={patientId} patientName={patientName} showPatientColumn={false} />
                  </CardContent>
                </Card>
              </TabsContent>

            </Tabs>

            <div className="grid gap-6 lg:grid-cols-3">
              {/* Personal Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <User className="h-5 w-5 text-muted-foreground" />
                    Personal Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Full Name</p>
                    <p className="text-sm text-foreground">{patientName}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Date of Birth</p>
                    <p className="text-sm text-foreground">
                      {formatDate(patient.dateOfBirth)} {age !== null && `(${age} years old)`}
                    </p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Gender</p>
                    <p className="text-sm text-foreground">{patient.gender || "Not provided"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Mobile</p>
                    <p className="text-sm text-foreground">{patient.phoneNumber || "Not provided"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Email</p>
                    <p className="text-sm text-foreground">{patient.email || "Not provided"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Address</p>
                    <p className="text-sm text-foreground">{formatAddress(patient.address)}</p>
                  </div>
                  {patient.emergencyContactName && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Emergency Contact</p>
                        <p className="text-sm text-foreground">
                          {patient.emergencyContactName}
                          {patient.emergencyContactPhone && ` - ${patient.emergencyContactPhone}`}
                        </p>
                      </div>
                    </>
                  )}
                </CardContent>
              </Card>

              {/* Medical Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Activity className="h-5 w-5 text-muted-foreground" />
                    Medical Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Chronic Diseases / Conditions</p>
                    <p className="text-sm text-foreground whitespace-pre-wrap">
                      {medicalHistoryText}
                    </p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground mb-2">Medical History</p>
                    {medicalHistoryEntries.length > 0 ? (
                      <div className="space-y-2">
                        {medicalHistoryEntries.map((entry) => (
                          <div key={entry.id} className="rounded-lg border bg-muted/30 p-2">
                            <p className="text-sm font-medium text-foreground">{entry.description}</p>
                            {entry.date && (
                              <p className="text-xs text-muted-foreground mt-1">
                                Date: {formatDate(entry.date)}
                              </p>
                            )}
                            {entry.notes && (
                              <p className="text-xs text-muted-foreground mt-1">{entry.notes}</p>
                            )}
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted-foreground">No medical history entries</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground mb-2">Family Medical History</p>
                    {familyHistoryEntries.length > 0 ? (
                      <div className="space-y-2">
                        {familyHistoryEntries.map((entry) => (
                          <div key={entry.id} className="rounded-lg border bg-muted/30 p-2">
                            <p className="text-sm font-medium text-foreground">
                              {entry.relationship}: {entry.condition}
                            </p>
                            {entry.notes && (
                              <p className="text-xs text-muted-foreground mt-1">{entry.notes}</p>
                            )}
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted-foreground">No family history entries</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Allergies</p>
                    {allergiesList.length > 0 ? (
                      <div className="mt-1 flex flex-wrap gap-1">
                        {allergiesList.map((allergy: string, index: number) => (
                          <Badge key={index} variant="destructive" className="text-xs">
                            {allergy}
                          </Badge>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted-foreground">None reported</p>
                    )}
                  </div>
                </CardContent>
              </Card>

              {/* Administrative Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <CreditCard className="h-5 w-5 text-muted-foreground" />
                    Administrative Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Insurance Provider</p>
                    <p className="text-sm text-foreground">{patient.insuranceInfo?.provider || "Not provided"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Policy Number</p>
                    <p className="font-mono text-sm text-foreground">{patient.insuranceInfo?.policyNumber || "Not provided"}</p>
                  </div>
                  {patient.insuranceInfo?.groupNumber && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Group Number</p>
                        <p className="text-sm text-foreground">{patient.insuranceInfo.groupNumber}</p>
                      </div>
                    </>
                  )}
                  {patient.insuranceInfo?.expiryDate && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Expiry Date</p>
                        <p className="text-sm text-foreground">{formatDate(patient.insuranceInfo.expiryDate)}</p>
                      </div>
                    </>
                  )}
                </CardContent>
              </Card>
            </div>

          </div>
        </main>
      </div>

      <EditPatientDialog
        open={editDialogOpen}
        onOpenChange={setEditDialogOpen}
        patient={patient}
        onSuccess={handleEditSuccess}
      />

      <PatientRecordModal
        open={recordModalOpen}
        onOpenChange={(open) => {
          setRecordModalOpen(open)
          if (!open) {
            setEditingRecord(null)
          }
        }}
        patientName={patientName}
        patientId={patient.id}
        record={editingRecord}
        onSuccess={handleEditSuccess}
      />

      <PatientSummaryModal
        open={summaryModalOpen}
        onOpenChange={setSummaryModalOpen}
        patient={patient}
        dentalRecords={dentalRecords}
      />

      {/* Facturer cette intervention — pre-filled draft from a dental record (create-only). */}
      <InvoiceFormModal
        open={!!billingRecord}
        onOpenChange={(open) => { if (!open) setBillingRecord(null) }}
        presetPatientId={patient.id}
        presetPatientName={patientName}
        presetLines={
          billingRecord
            ? [{ designation: billingRecord.procedureType, quantity: 1, unitPriceHt: billingRecord.cost }]
            : undefined
        }
        dentalRecordId={billingRecord?.id}
        onSuccess={() => setRefreshKey((k) => k + 1)}
      />

      {/* File Preview Dialog */}
      <Dialog open={!!previewFile} onOpenChange={handleClosePreview}>
        <DialogContent className={`${previewFile && isPdfFile(previewFile) ? 'max-w-[98vw] w-[98vw]' : 'max-w-6xl'} max-h-[98vh] p-0 flex flex-col`}>
          {previewFile && (
            <>
              <DialogHeader className="px-6 pt-6 pb-4 flex-shrink-0 border-b bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <DialogTitle className="truncate text-lg font-semibold">{previewFile.fileName}</DialogTitle>
                <DialogDescription className="mt-1">
                  {formatFileSize(previewFile.fileSize)} • {formatDate(previewFile.uploadedAt)}
                </DialogDescription>
              </DialogHeader>
              <div className={`relative flex items-start justify-center flex-1 min-h-0 ${previewFile && isPdfFile(previewFile) ? 'bg-slate-100 dark:bg-slate-900 p-6 overflow-auto' : 'bg-black/5 p-6 overflow-auto'}`}>
                {previewLoading ? (
                  <div className="flex flex-col items-center justify-center gap-3 h-full">
                    <Loader2 className="h-8 w-8 animate-spin text-blue-600" />
                    <p className="text-sm text-muted-foreground">Loading preview...</p>
                  </div>
                ) : previewUrl ? (
                  <>
                    {isImageFile(previewFile) ? (
                      <div className="flex items-center justify-center w-full h-full">
                        <img
                          src={previewUrl}
                          alt={previewFile.fileName}
                          className="max-w-full max-h-full w-auto h-auto object-contain rounded-lg shadow-lg"
                        />
                      </div>
                    ) : isPdfFile(previewFile) ? (
                      <div className="w-full flex items-start justify-center min-h-full">
                        <div className="bg-white dark:bg-slate-800 shadow-2xl rounded-lg overflow-hidden" style={{ 
                          width: '100%', 
                          maxWidth: 'calc(100vw - 8rem)',
                          aspectRatio: '210 / 297'
                        }}>
                          <iframe
                            src={`${previewUrl}#toolbar=0&navpanes=0&scrollbar=1`}
                            className="w-full h-full"
                            style={{ 
                              border: 'none',
                              display: 'block',
                              aspectRatio: '210 / 297'
                            }}
                            title={previewFile.fileName}
                          />
                        </div>
                      </div>
                    ) : (
                      <div className="flex flex-col items-center gap-3 p-8">
                        <FileText className="h-16 w-16 text-muted-foreground" />
                        <p className="text-sm text-muted-foreground">Preview not available for this file type</p>
                        <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                          <Download className="h-4 w-4 mr-2" />
                          Download to view
                        </Button>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="flex flex-col items-center gap-3 p-8">
                    <FileText className="h-16 w-16 text-muted-foreground" />
                    <p className="text-sm text-muted-foreground">Preview not available for this file type</p>
                    <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                      <Download className="h-4 w-4 mr-2" />
                      Download to view
                    </Button>
                  </div>
                )}
              </div>
              <DialogFooter className="px-6 py-4 flex-shrink-0 border-t bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <div className="flex items-center gap-3 w-full justify-between">
                  <Button variant="outline" onClick={handleClosePreview} className="min-w-[100px]">
                    Close
                  </Button>
                  <Button variant="outline" onClick={() => handleDownloadFile(previewFile!)} className="gap-2">
                    <Download className="h-4 w-4" />
                    Download
                  </Button>
                </div>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>
      </div>
    </ClinicGuard>
  )
}


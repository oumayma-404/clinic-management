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
import { formatDT, formatDateFr, formatDate, formatDateTime, formatFileSize } from "@/lib/format"
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
  Trash2,
} from "lucide-react"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { toast } from "sonner"
import { useSession } from "@/lib/auth/session"
import { patientsApi } from "@/lib/api/patients"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { appointmentsApi } from "@/lib/api/appointments"
import { patientMedicalHistoryApi } from "@/lib/api/patient-medical-history"
import { patientFamilyHistoryApi } from "@/lib/api/patient-family-history"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import { patientFilesApi } from "@/lib/api/patient-files"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import type { PatientDto, AppointmentDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto, DentalRecordDto, PatientFileDto, PatientFolderDto, TreatmentPlanDto, MedicalDocumentDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { PatientRecordModal } from "@/components/patient-record-modal"
import { Edit } from "lucide-react"
import { Receipt } from "lucide-react"
import { Smile, ClipboardCheck } from "lucide-react"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { InvoiceFormModal } from "@/components/factures/invoice-form-modal"
import { Odontogram } from "@/components/odontogram"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PatientPlanCard } from "@/components/treatment-plans/patient-plan-card"
import { TreatmentPlanFormModal, type TreatmentPlanSeedLine } from "@/components/treatment-plans/treatment-plan-form-modal"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { PlanItemOption } from "@/components/patient-record-modal"
import { invoicesApi } from "@/lib/api/invoices"
import { billingApi } from "@/lib/api/billing"
import type { PatientBillingSummaryDto } from "@/lib/api/types"
import { HandCoins } from "lucide-react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { appointmentStatusBadgeClass, appointmentStatusLabel, genderLabel } from "@/components/appointment-labels"
import { showErrorToast } from "@/lib/errors"
import { downloadBlob } from "@/lib/download"

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

const getPatientName = (patient: PatientDto) => {
  return `${patient.firstName} ${patient.lastName}`.trim()
}

const formatAddress = (address: PatientDto["address"]) => {
  if (!address) return "Non renseigné"
  const parts = [address.street, address.city, address.state, address.zipCode].filter(Boolean)
  return parts.join(", ") || "Non renseigné"
}

const hasActiveFlags = (patient: PatientDto) => {
  return patient.flags && patient.flags.some(flag => flag.isActive)
}

// French labels for saved medical-document types (the document-editor route uses the raw type key).
const DOCUMENT_TYPE_LABELS: Record<string, string> = {
  prescription: "Ordonnance",
  liaison: "Lettre de liaison",
  certificat: "Certificat médical",
  "bulletin-cnam": "Bulletin de soins CNAM",
}
const documentTypeLabel = (type: string) => DOCUMENT_TYPE_LABELS[type] ?? type

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
  // Appointment carried by the post-visit "record the visit" deep-link, threaded into the record modal so
  // saving the dental record closes that appointment's post-visit prompt (findings #4 + #10).
  const [reviewAppointmentId, setReviewAppointmentId] = useState<string | null>(null)
  const [expandedNotes, setExpandedNotes] = useState<Set<string>>(new Set())
  // Dental records already tied to a non-cancelled invoice (guards against double-invoicing).
  const [invoicedDentalRecordIds, setInvoicedDentalRecordIds] = useState<Set<string>>(new Set())
  // The note d'honoraires that bills each of those records, so the delete confirmation can NAME it
  // (AC-P2.17) instead of vaguely warning that the fiche is billed. Same pass as the set above.
  const [invoicingNumberByRecordId, setInvoicingNumberByRecordId] = useState<Map<string, string>>(new Map())
  const [billingSummary, setBillingSummary] = useState<PatientBillingSummaryDto | null>(null)
  const [unarchiving, setUnarchiving] = useState(false)
  // The dental record being invoiced (drives the pre-filled invoice modal); null = closed.
  const [billingRecord, setBillingRecord] = useState<DentalRecordDto | null>(null)
  // Pending destructive confirmations (AC-P2.16 / AC-P2.20). null = dialog closed.
  const [recordToDelete, setRecordToDelete] = useState<DentalRecordDto | null>(null)
  const [documentToDelete, setDocumentToDelete] = useState<MedicalDocumentDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [previewFile, setPreviewFile] = useState<PatientFileDto | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [refreshKey, setRefreshKey] = useState(0)
  const [aiSummary, setAiSummary] = useState("")
  const [aiLoading, setAiLoading] = useState(false)
  const [aiError, setAiError] = useState(false)
  const [treatmentPlans, setTreatmentPlans] = useState<TreatmentPlanDto[]>([])
  // Controlled so PatientPlanCard can send the user to the plans tab.
  const [activeTab, setActiveTab] = useState("medical-records")
  const [medicalDocuments, setMedicalDocuments] = useState<MedicalDocumentDto[]>([])
  const [planSeeds, setPlanSeeds] = useState<TreatmentPlanSeedLine[]>([])
  const [seededPlanOpen, setSeededPlanOpen] = useState(false)
  const { internetReachable } = useConnectivity()
  // Both delete endpoints are AdminOrDoctor (A-12). Offer the action only to those roles so a secretary is
  // never sent into a guaranteed 403 — the same rationale procedure-types-table.tsx documents for its writes.
  const { user: sessionUser } = useSession()
  const canDeleteClinicalRecords = sessionUser?.role === "admin" || sessionUser?.role === "doctor"

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
  //
  // TreatmentPlans + Invoices are here for PatientPlanCard (AC-9a): its progress, « prochaine séance » and
  // « Facturé » badge are derived from three different aggregates, and RealtimeBroadcastBehavior keys off
  // the *command's* namespace — so a peer accepting a plan broadcasts "treatmentplans" and issuing its
  // invoice broadcasts "invoices", neither of which "patients" would catch. Without them the card silently
  // goes stale while the rest of the page refreshes.
  useClinicRealtime(
    [
      RealtimeResource.Patients,
      RealtimeResource.Appointments,
      RealtimeResource.Files,
      RealtimeResource.TreatmentPlans,
      RealtimeResource.Invoices,
    ],
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
        const [medicalHistory, familyHistory, dentalRecordsData, filesData, foldersData, invoicesData, billingSummaryData] = await Promise.all([
          patientMedicalHistoryApi.list(patientId).catch(() => []),
          patientFamilyHistoryApi.list(patientId).catch(() => []),
          dentalRecordsApi.list(patientId).catch(() => []),
          patientFilesApi.getFiles(patientId).catch(() => []),
          patientFilesApi.getFolders(patientId).catch(() => []),
          invoicesApi.list({ patientId }).catch(() => []),
          billingApi.getPatientSummary(patientId).catch(() => null)
        ])
        setMedicalHistoryEntries(medicalHistory)
        setFamilyHistoryEntries(familyHistory)
        setDentalRecords(dentalRecordsData)
        setFiles(filesData)
        setFolders(foldersData)
        setBillingSummary(billingSummaryData)
        // A dental record counts as "already invoiced" only if a NON-cancelled invoice links to it
        // (a cancelled invoice frees it for re-billing) — via the header link OR any line link (a
        // multi-record note d'honoraires links each billed record at the line level). Safe degradation:
        // a failed invoices fetch yields an empty set, so the Facturer action stays available.
        const invoicedIds = new Set<string>()
        // Same walk records WHICH invoice bills each fiche, so the delete confirmation can name it. A draft
        // has no number yet, so fall back to « brouillon » rather than printing an empty string.
        const invoicingNumbers = new Map<string, string>()
        for (const inv of invoicesData) {
          if (inv.status === "Cancelled") continue
          const label = inv.number?.trim() || "brouillon"
          const remember = (recordId: string) => {
            invoicedIds.add(recordId)
            if (!invoicingNumbers.has(recordId)) invoicingNumbers.set(recordId, label)
          }
          if (inv.dentalRecordId) remember(inv.dentalRecordId)
          for (const line of inv.lines ?? []) {
            if (line.dentalRecordId) remember(line.dentalRecordId)
          }
        }
        setInvoicedDentalRecordIds(invoicedIds)
        setInvoicingNumberByRecordId(invoicingNumbers)
      } catch (err) {
        console.error("Failed to load patient data:", err)
        setError(err instanceof ApiError ? err.message : "Échec du chargement des données du patient")
      } finally {
        setLoading(false)
      }
    }

    if (patientId) {
      loadPatientData()
    }
  }, [patientId, refreshKey])

  // Load the patient's treatment plans (for the record-modal plan-step picker). Refreshes with the page.
  useEffect(() => {
    if (!patientId) return
    treatmentPlansApi
      .list({ patientId })
      .then(setTreatmentPlans)
      .catch(() => setTreatmentPlans([]))
  }, [patientId, refreshKey])

  // Load the patient's saved medical documents (ordonnances, certificats, BS1…) for the Documents tab.
  useEffect(() => {
    if (!patientId) return
    medicalDocumentsApi
      .list(patientId)
      .then(setMedicalDocuments)
      .catch(() => setMedicalDocuments([]))
  }, [patientId, refreshKey])

  // Deep-link from the post-visit "record the visit" bell (?addRecord=1&appointmentId=…): open the
  // add-record modal (finding #4) and thread the appointment id so saving closes the prompt (finding #10).
  // Uses window.location.search + history.replaceState (no useSearchParams) so a refresh doesn't reopen it,
  // matching the appointments page's deep-link pattern.
  useEffect(() => {
    const query = new URLSearchParams(window.location.search)
    if (query.get("addRecord") === "1") {
      setReviewAppointmentId(query.get("appointmentId"))
      setEditingRecord(null)
      setRecordModalOpen(true)
      window.history.replaceState({}, "", `/patients/${patientId}`)
    }
  }, [patientId])

  // ?tab=… lands the visitor on a specific tab — used by the plan workspace's « Voir la fiche », which needs
  // to open the medical-records tab rather than dumping the user on the default one. Same window.location
  // idiom as above (useSearchParams would force this page out of static prerendering); the param is left in
  // the URL so a refresh keeps the tab.
  useEffect(() => {
    const tab = new URLSearchParams(window.location.search).get("tab")
    if (tab) setActiveTab(tab)
  }, [patientId])

  // Reload files when folder changes
  useEffect(() => {
    const loadFilesForFolder = async () => {
      if (!patientId) return
      try {
        const filesData = await patientFilesApi.getFiles(patientId, currentFolderId || undefined).catch(() => [])
        setFiles(filesData)
      } catch (error) {
        // The inner `.catch(() => [])` already absorbs the API failure, so this arm is only reachable on a
        // genuine render/state fault. Surface it rather than swallow (AC-P3.33).
        showErrorToast(error, "Les fichiers de ce dossier n'ont pas pu être chargés.")
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
        const [medicalHistory, familyHistory, dentalRecordsData, filesData, foldersData, plansData] = await Promise.all([
          patientMedicalHistoryApi.list(patientId).catch(() => []),
          patientFamilyHistoryApi.list(patientId).catch(() => []),
          dentalRecordsApi.list(patientId).catch(() => []),
          patientFilesApi.getFiles(patientId).catch(() => []),
          patientFilesApi.getFolders(patientId).catch(() => []),
          treatmentPlansApi.list({ patientId }).catch(() => [])
        ])
        setMedicalHistoryEntries(medicalHistory)
        setFamilyHistoryEntries(familyHistory)
        setDentalRecords(dentalRecordsData)
        setFiles(filesData)
        setFolders(foldersData)
        setTreatmentPlans(plansData)
      } catch (err) {
        // The edit itself succeeded (the dialog already said so); this is the re-read failing. Saying so
        // is what stops the user believing their change was lost (AC-P3.33).
        showErrorToast(err, "Patient enregistré, mais le dossier n'a pas pu être rechargé.")
      }
    }
    loadPatientData()
  }

  /**
   * Every devis act whose « réalisé » state is evidenced by this fiche. Deleting the fiche returns each of
   * them to « prévu » and reopens its plan (AC-P2.13), so the confirmation has to say so *before* the user
   * commits — being told afterwards is the defect this closes (AC-P2.18).
   */
  const planActsEvidencedBy = (recordId: string) =>
    treatmentPlans.flatMap((plan) =>
      plan.items
        .filter((item) => item.linkedDentalRecordId === recordId)
        .map((item) => ({ planTitle: plan.title, designation: item.designationFr })),
    )

  const confirmDeleteRecord = async () => {
    if (!recordToDelete) return
    try {
      setDeleting(true)
      await dentalRecordsApi.delete(patientId, recordToDelete.id)
      toast.success("Fiche de soins supprimée.")
      setRecordToDelete(null)
      // Refresh through the page's single loader: the delete also detaches invoice lines and plan acts, so
      // the invoices, the plans and the « Facturé » badges all have to be re-read, not just the fiche list.
      setRefreshKey((k) => k + 1)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression de la fiche de soins.")
    } finally {
      setDeleting(false)
    }
  }

  const confirmDeleteDocument = async () => {
    if (!documentToDelete) return
    try {
      setDeleting(true)
      await medicalDocumentsApi.delete(documentToDelete.id)
      toast.success("Document supprimé.")
      setDocumentToDelete(null)
      setRefreshKey((k) => k + 1)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la suppression du document.")
    } finally {
      setDeleting(false)
    }
  }

  if (loading) {
    return (
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex flex-1 items-center justify-center">
            <div className="text-center">
              <p className="text-muted-foreground">Chargement des données du patient…</p>
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
              <h2 className="text-2xl font-semibold text-foreground">Patient introuvable</h2>
              <p className="mt-2 text-muted-foreground">
                {error || "Le patient recherché n'existe pas."}
              </p>
              <Button onClick={() => router.push("/patients")} className="mt-4">
                Retour aux patients
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

  // Open (not-yet-done) steps of the patient's active plans — offered in the record modal to close the
  // plan→record loop, and completed automatically when a linked record is saved.
  const openPlanItems: PlanItemOption[] = treatmentPlans
    .filter((p) => p.status === "Accepted" || p.status === "InProgress")
    .flatMap((p) =>
      p.items
        .filter((it) => it.status !== "Done")
        .map((it) => ({
          itemId: it.id,
          planId: p.id,
          label: `${p.number ?? p.title} · ${it.designationFr}${it.toothNumbers.length > 0 ? ` (dents ${it.toothNumbers.join(", ")})` : ""}`,
          designationFr: it.designationFr,
          plannedCost: it.plannedCost,
          toothNumbers: it.toothNumbers,
        })),
    )

  // The appointment the record documents, so its booked procedure can be PROPOSED in the record modal and
  // its plan step pre-selected (AC-9). Two sources, in order: the post-visit deep-link
  // (`?addRecord=1&appointmentId=…`), then — when the modal was opened straight from this page — today's
  // live appointment for this patient. A record being edited is never re-proposed.
  const recordAppointment: AppointmentDto | null = editingRecord
    ? null
    : (reviewAppointmentId
        ? appointments.find((a) => a.id === reviewAppointmentId)
        : appointments.find((a) => {
            // Useful if it names a procedure to propose OR a plan step to pre-select — an appointment booked
            // from a devis often carries only the latter, and that is exactly the case AC-9 is about.
            if (!a.procedureTypeId && !a.treatmentPlanItemId) return false
            if (a.status === "Cancelled" || a.status === "NoShow") return false
            const when = new Date(a.appointmentDateTime)
            const today = new Date()
            return (
              when.getFullYear() === today.getFullYear() &&
              when.getMonth() === today.getMonth() &&
              when.getDate() === today.getDate()
            )
          })) ?? null

  // Compute current files based on folder selection
  // When in a folder, all loaded files belong to that folder
  // When at root, show only root files (files without folderId)
  const currentFiles = currentFolderId
    ? files // All files loaded are for this folder
    : files.filter(f => !f.folderId) // Root files (not in any folder)
  

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
      // AC-P3.30 — the dialog used to close itself with no explanation, which reads as "the click did
      // nothing". Say why, and offer the download as the way through.
      showErrorToast(error, "Impossible d'afficher l'aperçu de ce fichier. Essayez de le télécharger.")
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
      downloadBlob(blob, file.fileName)
    } catch (error) {
      // AC-P3.29 — matches what the same action already does in `patient-files-manager.tsx`; a silent
      // console.error made a failed download indistinguishable from a browser that blocked the save.
      showErrorToast(error, `Impossible de télécharger « ${file.fileName} ».`)
    }
  }
  
  // Parse allergies from string (comma-separated)
  const allergiesList = patient.allergies 
    ? patient.allergies.split(',').map(a => a.trim()).filter(Boolean)
    : []
  
  // Parse medical history (if it contains structured data, otherwise show as text)
  const medicalHistoryText = patient.medicalHistory || "Aucun renseignement"
  

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

        <main className="flex-1 overflow-y-auto p-4 md:p-6">
          <div className="mx-auto max-w-7xl space-y-6">
            {/* Back Button */}
            <Button variant="ghost" onClick={() => router.push("/patients")} className="gap-2">
              <ArrowLeft className="h-4 w-4" />
              Retour aux patients
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
                <Button variant="outline" onClick={() => setEditDialogOpen(true)} className="gap-2">
                  <Edit className="h-4 w-4" />
                  Modifier le patient
                </Button>
                <Button variant="outline" onClick={() => setRecordModalOpen(true)} className="gap-2">
                  <FileText className="h-4 w-4" />
                  Ajouter un dossier médical
                </Button>
                <Button onClick={() => router.push(`/appointments?patientId=${patient.id}`)}>
                  Planifier un rendez-vous
                </Button>
              </div>
            </div>

            <Card className="border-blue-200 bg-blue-50/50 dark:border-blue-900 dark:bg-blue-950/20">
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-blue-700 dark:text-blue-400">
                  <Sparkles className="h-5 w-5" />
                  Résumé du patient généré par l&apos;IA
                </CardTitle>
                <CardDescription>Aperçu généré automatiquement à partir des dossiers du patient</CardDescription>
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

            {/* An archived patient is hidden from every list and search but still reachable by direct URL —
                which makes this page the only place that can say so. */}
            {patient?.isArchived && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 dark:border-amber-900 dark:bg-amber-950">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="space-y-1">
                    <p className="text-sm font-medium text-amber-900 dark:text-amber-200">
                      Ce patient est archivé
                      {patient.archivedAt ? ` depuis le ${formatDateFr(patient.archivedAt)}` : ""}.
                    </p>
                    <p className="text-sm text-amber-800 dark:text-amber-300">
                      Il n&apos;apparaît plus dans les listes, la recherche, les relances ni les sélecteurs de
                      patient. Aucune donnée n&apos;a été supprimée.
                      {patient.archiveReason ? ` Motif : ${patient.archiveReason}` : ""}
                    </p>
                  </div>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={unarchiving}
                    onClick={async () => {
                      try {
                        setUnarchiving(true)
                        const restored = await patientsApi.unarchive(patient.id)
                        setPatient(restored)
                      } finally {
                        setUnarchiving(false)
                      }
                    }}
                  >
                    {unarchiving ? "Restauration…" : "Restaurer"}
                  </Button>
                </div>
              </div>
            )}

            {/* Unified per-patient balance (« Solde patient ») across invoices + treatment-plan installments. */}
            {billingSummary && (
              <Card>
                <CardHeader className="pb-3">
                  <CardTitle className="flex items-center gap-2 text-base">
                    <HandCoins className="h-5 w-5 text-muted-foreground" />
                    Solde patient
                  </CardTitle>
                  <CardDescription>
                    Solde unifié sur les deux circuits de facturation (factures + échéanciers).
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                    <div>
                      <p className="text-xs text-muted-foreground">Solde dû total</p>
                      <p className={`text-2xl font-semibold ${billingSummary.totalOutstanding > 0 ? "text-amber-600" : "text-foreground"}`}>
                        {formatDT(billingSummary.totalOutstanding)}
                      </p>
                      <p className="text-[11px] text-muted-foreground">= factures + échéanciers</p>
                      {billingSummary.oldestOverdueDate && (
                        <Badge variant="destructive" className="mt-1">
                          En retard depuis le {formatDateFr(billingSummary.oldestOverdueDate)}
                        </Badge>
                      )}
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Solde factures</p>
                      <p className="text-lg font-medium">{formatDT(billingSummary.invoiceOutstanding)}</p>
                      <p className="mt-1 text-xs text-muted-foreground">Solde échéanciers</p>
                      <p className="text-lg font-medium">{formatDT(billingSummary.installmentOutstanding)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Estimation CNAM</p>
                      <p className="text-lg font-medium">{formatDT(billingSummary.cnamReimbursable)}</p>
                    </div>
                    <div>
                      <p className="text-xs text-muted-foreground">Reste à charge patient</p>
                      <p className="text-lg font-medium">{formatDT(billingSummary.patientOutOfPocket)}</p>
                      {/* An avoir returns the cash AND cancels the fee, so it leaves the balance at zero.
                          Without this line a refunded patient is indistinguishable from one who never had
                          anything to settle. */}
                      {billingSummary.creditedTotal > 0 && (
                        <>
                          <p className="mt-1 text-xs text-muted-foreground">Remboursé (avoirs)</p>
                          <p className="text-lg font-medium text-blue-700 dark:text-blue-400">
                            −{formatDT(billingSummary.creditedTotal)}
                          </p>
                        </>
                      )}
                    </div>
                  </div>
                </CardContent>
              </Card>
            )}

            {/* Treatment leads the patient page now. A devis buried in the 8th tab was the whole reason the
                plan felt disconnected from the patient it belongs to. Renders nothing when there is no plan. */}
            <PatientPlanCard
              plans={treatmentPlans}
              onOpen={() => setActiveTab("treatment-plans")}
              onChanged={() => setRefreshKey((k) => k + 1)}
            />

            <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4">
              <TabsList className="grid w-full grid-cols-8">
                <TabsTrigger value="medical-records" className="gap-2">
                  <FileCheck className="h-4 w-4" />
                  Dossiers médicaux
                </TabsTrigger>
                <TabsTrigger value="odontogram" className="gap-2">
                  <Smile className="h-4 w-4" />
                  Odontogramme
                </TabsTrigger>
                <TabsTrigger value="appointments" className="gap-2">
                  <Calendar className="h-4 w-4" />
                  Rendez-vous
                </TabsTrigger>
                <TabsTrigger value="notes" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Notes
                </TabsTrigger>
                <TabsTrigger value="documents" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Documents
                </TabsTrigger>
                <TabsTrigger value="files" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Fichiers
                </TabsTrigger>
                <TabsTrigger value="factures" className="gap-2">
                  <Receipt className="h-4 w-4" />
                  Factures
                </TabsTrigger>
                <TabsTrigger value="treatment-plans" className="gap-2">
                  <ClipboardCheck className="h-4 w-4" />
                  Plan de traitement
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
                          Dossiers dentaires
                        </CardTitle>
                        <CardDescription>Historique complet des actes et interventions dentaires</CardDescription>
                      </div>
                      <Button onClick={() => {
                        setEditingRecord(null)
                        setRecordModalOpen(true)
                      }} size="sm">
                        Ajouter un dossier dentaire
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {dentalRecords.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">Aucun dossier dentaire</p>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Date</TableHead>
                              <TableHead>Type d'acte</TableHead>
                              <TableHead>Type de dents</TableHead>
                              <TableHead>Dents</TableHead>
                              <TableHead>Montant payé</TableHead>
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
                                    {record.isAdultTeeth ? "Adulte" : "Enfant"}
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
                                <TableCell>
                                  {invoicedDentalRecordIds.has(record.id) ? (
                                    <span className="text-muted-foreground line-through" title="Facturé — le montant est géré par la facture">
                                      {formatDT(record.amountPaid)}
                                    </span>
                                  ) : (
                                    formatDT(record.amountPaid)
                                  )}
                                </TableCell>
                                <TableCell>
                                  {invoicedDentalRecordIds.has(record.id) ? (
                                    <span className="text-muted-foreground text-xs">Facturé</span>
                                  ) : (() => {
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
                                                  Notes importantes :
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
                                                    Notes :
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
                                              Réduire
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
                                                  {record.importantNotes.length} importantes
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
                                              Voir les notes
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
                                      title="Modifier le dossier"
                                    >
                                      <Pencil className="h-4 w-4" />
                                    </Button>
                                    {canDeleteClinicalRecords && (
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-8 w-8 p-0 text-destructive hover:text-destructive"
                                        onClick={() => setRecordToDelete(record)}
                                        title="Supprimer la fiche de soins"
                                      >
                                        <Trash2 className="h-4 w-4" />
                                      </Button>
                                    )}
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

              {/* Odontogramme Tab */}
              <TabsContent value="odontogram">
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2">
                      <Smile className="h-5 w-5" />
                      Odontogramme
                    </CardTitle>
                    <CardDescription>
                      Cliquez sur une dent pour noter un diagnostic (à traiter) ; les actes réalisés s'ajoutent automatiquement lors de l'enregistrement d'un acte médical.
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <Odontogram
                      patientId={patientId}
                      onCreatePlan={(seeds) => {
                        setPlanSeeds(seeds)
                        setSeededPlanOpen(true)
                      }}
                    />
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Notes Tab */}
              <TabsContent value="notes">
                <Card>
                  <CardHeader>
                    <CardTitle>Notes des dossiers médicaux</CardTitle>
                    <CardDescription>Notes et notes importantes des dossiers dentaires</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {dentalRecords.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">Aucun dossier médical</p>
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
                                  Notes importantes
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
                          <p className="text-center text-muted-foreground py-8">Aucune note dans les dossiers médicaux</p>
                        )}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Documents Tab — saved medical documents; reopen the editor to edit / reprint. */}
              <TabsContent value="documents">
                <Card>
                  <CardHeader>
                    <div className="flex items-start justify-between gap-4">
                      <div className="space-y-1.5">
                        <CardTitle className="flex items-center gap-2">
                          <FileText className="h-5 w-5" />
                          Documents médicaux
                        </CardTitle>
                        <CardDescription>
                          Ordonnances, certificats, lettres de liaison et bulletins CNAM enregistrés. Cliquez sur « Ouvrir » pour modifier ou réimprimer.
                        </CardDescription>
                      </div>
                      {/* P2-A: prescribe for the open patient without leaving the page / re-searching them. */}
                      <Button
                        size="sm"
                        className="shrink-0 gap-1"
                        onClick={() => router.push(`/documents/prescription?patientId=${patientId}`)}
                      >
                        <FileText className="h-4 w-4" />
                        Nouvelle ordonnance
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {medicalDocuments.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">Aucun document enregistré</p>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Type</TableHead>
                              <TableHead>Date</TableHead>
                              <TableHead className="text-right">Actions</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {medicalDocuments.map((doc) => (
                              <TableRow key={doc.id}>
                                <TableCell className="font-medium">{documentTypeLabel(doc.documentType)}</TableCell>
                                <TableCell className="text-muted-foreground">{formatDate(doc.documentDate)}</TableCell>
                                <TableCell className="text-right">
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="gap-1"
                                    onClick={() =>
                                      router.push(
                                        // The "honoraires" document type is retired (PDF now rejects it) —
                                        // route legacy rows to the Factures module instead of the dead editor (#13).
                                        doc.documentType === "honoraires"
                                          ? "/factures"
                                          : `/documents/${doc.documentType}?id=${doc.id}`,
                                      )
                                    }
                                    title="Ouvrir le document"
                                  >
                                    <Eye className="h-4 w-4" />
                                    Ouvrir
                                  </Button>
                                  {canDeleteClinicalRecords && (
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      className="gap-1 text-destructive hover:text-destructive"
                                      onClick={() => setDocumentToDelete(doc)}
                                      title="Supprimer le document"
                                    >
                                      <Trash2 className="h-4 w-4" />
                                      Supprimer
                                    </Button>
                                  )}
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

              {/* Appointments Tab - Merged with Procedures */}
              <TabsContent value="appointments">
                <Card>
                  <CardHeader>
                    <CardTitle>Historique des rendez-vous</CardTitle>
                    <CardDescription>Historique complet des rendez-vous et des actes</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {appointments.length === 0 ? (
                      <p className="text-center text-muted-foreground py-8">Aucun rendez-vous</p>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Date et heure</TableHead>
                              <TableHead>Acte / Type</TableHead>
                              <TableHead>Médecin</TableHead>
                              <TableHead>Durée</TableHead>
                              <TableHead>Statut</TableHead>
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
                                        <span className="text-muted-foreground">Rendez-vous général</span>
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
                                      {/* AC-P1.42: printed the raw English enum name. */}
                                      <Badge
                                        variant="secondary"
                                        className={appointmentStatusBadgeClass(appointment.status)}
                                      >
                                        {appointmentStatusLabel(appointment.status)}
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
                        <CardTitle>Fichiers du patient</CardTitle>
                        <CardDescription>
                          {currentFolderId 
                            ? `Fichiers du dossier`
                            : `Tous les fichiers et documents téléversés (${files.length} fichier${files.length !== 1 ? 's' : ''})`}
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
                            Retour
                          </Button>
                        )}
                        <Button onClick={() => router.push(`/patients/${patientId}/files`)} variant="default">
                          Gérer les fichiers
                        </Button>
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {files.length === 0 && folders.length === 0 ? (
                      <div className="text-center py-8">
                        <FileText className="h-12 w-12 mx-auto mb-3 opacity-50 text-muted-foreground" />
                        <p className="text-sm text-muted-foreground mb-4">
                          Aucun fichier téléversé
                        </p>
                        <Button onClick={() => router.push(`/patients/${patientId}/files`)}>
                          Téléverser des fichiers
                        </Button>
                      </div>
                    ) : (
                      <div className="space-y-4">
                        {/* Folders List (only show at root level) */}
                        {!currentFolderId && folders.length > 0 && (
                          <div>
                            <h3 className="text-sm font-semibold mb-3 text-foreground">Dossiers</h3>
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
                                          {folder.fileCount} {folder.fileCount === 1 ? "fichier" : "fichiers"}
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
                              {currentFolderId ? "Fichiers du dossier" : "Fichiers"}
                            </h3>
                            <Card className="p-8 border-dashed">
                              <div className="text-center text-muted-foreground">
                                <FileText className="h-12 w-12 mx-auto mb-3 opacity-50" />
                                <p className="text-sm">
                                  {currentFolderId ? "Aucun fichier dans ce dossier" : "Aucun fichier à la racine"}
                                </p>
                              </div>
                            </Card>
                          </div>
                        ) : (
                          <div>
                            <h3 className="text-sm font-semibold mb-3 text-foreground">
                              {currentFolderId ? "Fichiers du dossier" : "Fichiers"}
                            </h3>
                            <div className="overflow-x-auto">
                              <Table>
                                <TableHeader>
                                  <TableRow>
                                    <TableHead>Nom du fichier</TableHead>
                                    <TableHead>Type</TableHead>
                                    <TableHead>Taille</TableHead>
                                    <TableHead>Téléversé le</TableHead>
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
                                              {file.fileType || file.contentType.split('/')[1] || 'Inconnu'}
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
                                                  title="Aperçu du fichier"
                                                  aria-label={`Aperçu de ${file.fileName}`}
                                                >
                                                  <Eye className="h-4 w-4" />
                                                </Button>
                                              )}
                                              <Button
                                                variant="ghost"
                                                size="sm"
                                                className="h-8 w-8 p-0"
                                                onClick={() => handleDownloadFile(file)}
                                                title="Télécharger le fichier"
                                                aria-label={`Télécharger ${file.fileName}`}
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
                    {/* onChanged was missing: recording a payment here left « Solde patient » and the plan
                        card above showing the pre-payment figures until a manual refresh. */}
                    <InvoicesTable
                      patientId={patientId}
                      patientName={patientName}
                      showPatientColumn={false}
                      onChanged={() => setRefreshKey((k) => k + 1)}
                    />
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Plan de traitement Tab */}
              <TabsContent value="treatment-plans" className="space-y-4">
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2">
                      <ClipboardCheck className="h-5 w-5" />
                      Plans de traitement
                    </CardTitle>
                    <CardDescription>Devis, actes planifiés et échéanciers de paiement du patient.</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <TreatmentPlansTable patientId={patientId} patientName={patientName} showPatientColumn={false} />
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
                    Informations personnelles
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Nom complet</p>
                    <p className="text-sm text-foreground">{patientName}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Date de naissance</p>
                    <p className="text-sm text-foreground">
                      {formatDate(patient.dateOfBirth)} {age !== null && `(${age} ans)`}
                    </p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Sexe</p>
                    <p className="text-sm text-foreground">{genderLabel(patient.gender)}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Téléphone</p>
                    <p className="text-sm text-foreground">{patient.phoneNumber || "Non renseigné"}</p>
                    {/* The blank alone reads as "nobody typed it in yet". What matters is that this patient
                        is silently excluded from every automated contact. */}
                    {!patient.phoneNumber && (
                      <p className="text-xs text-amber-700 dark:text-amber-400">
                        Ni rappel ni relance ne peuvent lui être envoyés.
                      </p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Email</p>
                    <p className="text-sm text-foreground">{patient.email || "Non renseigné"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Adresse</p>
                    <p className="text-sm text-foreground">{formatAddress(patient.address)}</p>
                  </div>
                  {patient.emergencyContactName && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Contact d'urgence</p>
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
                    Informations médicales
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Maladies chroniques / affections</p>
                    <p className="text-sm text-foreground whitespace-pre-wrap">
                      {medicalHistoryText}
                    </p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground mb-2">Antécédents médicaux</p>
                    {medicalHistoryEntries.length > 0 ? (
                      <div className="space-y-2">
                        {medicalHistoryEntries.map((entry) => (
                          <div key={entry.id} className="rounded-lg border bg-muted/30 p-2">
                            <p className="text-sm font-medium text-foreground">{entry.description}</p>
                            {entry.date && (
                              <p className="text-xs text-muted-foreground mt-1">
                                Date : {formatDate(entry.date)}
                              </p>
                            )}
                            {entry.notes && (
                              <p className="text-xs text-muted-foreground mt-1">{entry.notes}</p>
                            )}
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted-foreground">Aucun antécédent médical</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground mb-2">Antécédents familiaux</p>
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
                      <p className="text-sm text-muted-foreground">Aucun antécédent familial</p>
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
                      <p className="text-sm text-muted-foreground">Aucune signalée</p>
                    )}
                  </div>
                </CardContent>
              </Card>

              {/* Administrative Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <CreditCard className="h-5 w-5 text-muted-foreground" />
                    Informations administratives
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Assureur</p>
                    <p className="text-sm text-foreground">{patient.insuranceInfo?.provider || "Non renseigné"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Numéro de police</p>
                    <p className="font-mono text-sm text-foreground">{patient.insuranceInfo?.policyNumber || "Non renseigné"}</p>
                  </div>
                  {patient.insuranceInfo?.groupNumber && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Numéro de groupe</p>
                        <p className="text-sm text-foreground">{patient.insuranceInfo.groupNumber}</p>
                      </div>
                    </>
                  )}
                  {patient.insuranceInfo?.expiryDate && (
                    <>
                      <Separator />
                      <div>
                        <p className="text-xs font-medium text-muted-foreground">Date d'expiration</p>
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
            setReviewAppointmentId(null)
          }
        }}
        patientName={patientName}
        patientId={patient.id}
        record={editingRecord}
        isInvoiced={editingRecord ? invoicedDentalRecordIds.has(editingRecord.id) : false}
        patient={patient}
        planItems={openPlanItems}
        appointmentId={editingRecord ? null : reviewAppointmentId}
        appointment={recordAppointment}
        onSuccess={handleEditSuccess}
      />

      {/* Create a plan pre-filled from charted diagnoses ("Créer un plan depuis l'odontogramme"). */}
      <TreatmentPlanFormModal
        open={seededPlanOpen}
        onOpenChange={setSeededPlanOpen}
        presetPatientId={patient.id}
        presetPatientName={patientName}
        seedLines={planSeeds}
        onSuccess={() => {
          setSeededPlanOpen(false)
          setRefreshKey((k) => k + 1)
        }}
      />

      {/* Facturer cette intervention — pre-filled draft from a dental record (create-only). */}
      <InvoiceFormModal
        open={!!billingRecord}
        onOpenChange={(open) => { if (!open) setBillingRecord(null) }}
        presetPatientId={patient.id}
        presetPatientName={patientName}
        presetLines={
          billingRecord
            ? billingRecord.acts && billingRecord.acts.length > 0
              ? billingRecord.acts.map((act) => {
                  const teeth = act.toothNumbers ?? []
                  const designation =
                    teeth.length > 0 ? `${act.procedureName} (dents ${teeth.join(", ")})` : act.procedureName
                  // A per-tooth act bills as quantity × unit price, so the note d'honoraires shows what the
                  // total covers. A flat fee (or a legacy act with no captured unit price) stays one line.
                  const unit = act.unitCost
                  if (act.isPerTooth && teeth.length > 0 && unit != null) {
                    return { designation, quantity: teeth.length, unitPriceHt: unit }
                  }
                  return { designation, quantity: 1, unitPriceHt: act.cost }
                })
              : [{ designation: billingRecord.procedureType, quantity: 1, unitPriceHt: billingRecord.cost }]
            : undefined
        }
        dentalRecordId={billingRecord?.id}
        onSuccess={() => setRefreshKey((k) => k + 1)}
      />

      {/*
        Supprimer une fiche de soins (AC-P2.16). The copy is built from what the page already knows, because
        a fiche is never just a fiche: it can be the provenance of an invoice line (AC-P2.17) and the evidence
        for a devis act (AC-P2.18). Both consequences are named here, before the user confirms.
      */}
      <AlertDialog
        open={!!recordToDelete}
        onOpenChange={(open) => { if (!open) setRecordToDelete(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer cette fiche de soins ?</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div className="space-y-2">
                <p>
                  {recordToDelete
                    ? `Fiche du ${formatDate(recordToDelete.interventionDate)} — ${recordToDelete.procedureType}. Cette action est irréversible.`
                    : "Cette action est irréversible."}
                </p>
                {recordToDelete && invoicedDentalRecordIds.has(recordToDelete.id) && (
                  <p>
                    Cette fiche est facturée sur la note d&apos;honoraires{" "}
                    <span className="font-semibold">
                      {invoicingNumberByRecordId.get(recordToDelete.id) ?? "en cours"}
                    </span>
                    . La note d&apos;honoraires, son numéro et son montant ne changent pas : seul le lien vers
                    la fiche est retiré.
                  </p>
                )}
                {recordToDelete && planActsEvidencedBy(recordToDelete.id).length > 0 && (
                  <p>
                    {planActsEvidencedBy(recordToDelete.id).length === 1
                      ? "L'acte suivant repassera à « prévu » et son devis sera réouvert : "
                      : "Les actes suivants repasseront à « prévu » et leur devis sera réouvert : "}
                    <span className="font-semibold">
                      {planActsEvidencedBy(recordToDelete.id)
                        .map((act) => `${act.designation} (${act.planTitle})`)
                        .join(", ")}
                    </span>
                    .
                  </p>
                )}
              </div>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void confirmDeleteRecord()
              }}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Supprimer un document médical (AC-P2.20) — same AlertDialog pattern. */}
      <AlertDialog
        open={!!documentToDelete}
        onOpenChange={(open) => { if (!open) setDocumentToDelete(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Supprimer ce document ?</AlertDialogTitle>
            <AlertDialogDescription>
              {documentToDelete
                ? `${documentTypeLabel(documentToDelete.documentType)} du ${formatDate(documentToDelete.documentDate)}. Le document et son PDF enregistré seront supprimés. Cette action est irréversible.`
                : "Cette action est irréversible."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void confirmDeleteDocument()
              }}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

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
                    <p className="text-sm text-muted-foreground">Chargement de l&apos;aperçu…</p>
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
                        <p className="text-sm text-muted-foreground">Aperçu non disponible pour ce type de fichier</p>
                        <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                          <Download className="h-4 w-4 mr-2" />
                          Télécharger pour consulter
                        </Button>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="flex flex-col items-center gap-3 p-8">
                    <FileText className="h-16 w-16 text-muted-foreground" />
                    <p className="text-sm text-muted-foreground">Aperçu non disponible pour ce type de fichier</p>
                    <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                      <Download className="h-4 w-4 mr-2" />
                      Télécharger pour consulter
                    </Button>
                  </div>
                )}
              </div>
              <DialogFooter className="px-6 py-4 flex-shrink-0 border-t bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <div className="flex items-center gap-3 w-full justify-between">
                  <Button variant="outline" onClick={handleClosePreview} className="min-w-[100px]">
                    Fermer
                  </Button>
                  <Button variant="outline" onClick={() => handleDownloadFile(previewFile!)} className="gap-2">
                    <Download className="h-4 w-4" />
                    Télécharger
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


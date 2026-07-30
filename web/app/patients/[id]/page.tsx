"use client"

import { useState, useEffect, useRef } from "react"
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
import { Smile, ClipboardCheck, FolderOpen } from "lucide-react"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { BillDentalRecordDialog } from "@/components/factures/bill-dental-record-dialog"
import { Odontogram } from "@/components/odontogram"
import { PatientNotesStrip } from "@/components/patient/patient-notes-strip"
import { PatientUndocumentedVisits } from "@/components/patient/patient-undocumented-visits"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PatientPlansStrip } from "@/components/treatment-plans/patient-plans-strip"
import { TreatmentPlanFormModal, type TreatmentPlanSeedLine } from "@/components/treatment-plans/treatment-plan-form-modal"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { PlanItemOption } from "@/components/patient-record-modal"
import { invoicesApi } from "@/lib/api/invoices"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import {
  appointmentActsSummary, appointmentStatusBadgeClass, appointmentStatusLabel, genderLabel, normalizeStatus,
} from "@/components/appointment-labels"
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

/**
 * An empty section, or one that has not been asked for yet.
 *
 * Load-bearing since the page began painting its identity before its details: `[]` used to be reachable only
 * after every request had answered, so « Aucun dossier dentaire » was always true. It is now also the state
 * *before* the request answers — and a page that tells a dentist their patient has no records, no
 * appointments and no files, a beat before listing all three, is worse than one that took longer to appear.
 * Every empty state in the tabs goes through here so none of them can assert an absence we have not verified.
 */
function EmptyOrLoading({ loading, children }: { loading: boolean; children: React.ReactNode }) {
  if (loading) {
    return (
      <div className="space-y-2 py-6" role="status" aria-label="Chargement…">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-5 animate-pulse rounded bg-muted" />
        ))}
      </div>
    )
  }
  return <p className="py-8 text-center text-muted-foreground">{children}</p>
}

/**
 * The tab values this page renders, so a `?tab=` param can be validated against them. `odontogram` is
 * deliberately absent — it is a card above the tabs now, not a tab.
 */
const PATIENT_TABS = [
  "medical-records",
  "appointments",
  "notes",
  "documents",
  "files",
  "factures",
  "treatment-plans",
]

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
  /**
   * `loading` gates the *identity* only — one request. `detailsLoading` gates everything the cards and tabs
   * below are built from.
   *
   * They used to be a single flag over eight requests awaited in three phases (`get`, then `list`, then a
   * `Promise.all` of six), with `setLoading(false)` in the `finally` — so nothing at all appeared, not even
   * the patient's *name*, until every one of them had answered. Three serial round trips on a LAN install is
   * a visibly blank page while the dentist is standing at the chair. The identity now paints after the first
   * request and the rest fills in behind it.
   */
  const [loading, setLoading] = useState(true)
  const [detailsLoading, setDetailsLoading] = useState(true)
  /**
   * The patient whose data is currently on screen. A `refreshKey` bump (a save, or a peer's edit arriving over
   * realtime) must refetch *quietly*; only navigating to a different patient may blank the page. Without this
   * distinction, recording a payment threw the whole page away and rebuilt it — the same defect the calendar
   * had with its `key={refreshKey}` remount.
   */
  const loadedPatientIdRef = useRef<string | null>(null)
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
  const [treatmentPlans, setTreatmentPlans] = useState<TreatmentPlanDto[]>([])
  // Controlled so PatientPlansStrip can send the user to the plans tab.
  const [activeTab, setActiveTab] = useState("medical-records")
  // The tab strip sits below the odontogram, so a control *above* it that only calls setActiveTab appears to do
  // nothing on a tall screen — the panel it switched to is off-screen. Anything sending the user to a tab from
  // higher up the page goes through openTab so the tabs are actually brought into view.
  const tabsRef = useRef<HTMLDivElement>(null)
  const openTab = (tab: string) => {
    setActiveTab(tab)
    tabsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" })
  }
  const [medicalDocuments, setMedicalDocuments] = useState<MedicalDocumentDto[]>([])
  const [planSeeds, setPlanSeeds] = useState<TreatmentPlanSeedLine[]>([])
  const [seededPlanOpen, setSeededPlanOpen] = useState(false)
  // Both delete endpoints are AdminOrDoctor (A-12). Offer the action only to those roles so a secretary is
  // never sent into a guaranteed 403 — the same rationale procedure-types-table.tsx documents for its writes.
  const { user: sessionUser } = useSession()
  const canDeleteClinicalRecords = sessionUser?.role === "admin" || sessionUser?.role === "doctor"

  // Real-time: when any client of this clinic edits this patient's record, appointments, or files, the
  // server signals the resource and we re-run the loader below (bump refreshKey). Additive (AC-5).
  //
  // TreatmentPlans + Invoices are here for PatientPlansStrip (AC-9a): its progress, « prochaine séance » and
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
      // AC-P4.22 (A-15) — `documents` was declared and emitted with NO subscriber anywhere, and this is the
      // one screen that lists saved medical documents (the Documents tab). `Files` is a different key for a
      // different thing (uploaded blobs), which is how the gap survived: the page looked subscribed.
      RealtimeResource.Documents,
    ],
    () => setRefreshKey((k) => k + 1),
  )

  // Load patient data — identity first, then everything else.
  useEffect(() => {
    if (!patientId) return

    // Only an actual navigation to a different patient is allowed to blank the page; a refresh is quiet.
    const isDifferentPatient = loadedPatientIdRef.current !== patientId
    if (isDifferentPatient) {
      setLoading(true)
      setDetailsLoading(true)
    }

    let cancelled = false

    const loadPatientData = async () => {
      // ---- Phase 1: the identity. One request; the header, the alerts and the odontogram paint on it. ----
      try {
        const patientData = await patientsApi.get(patientId)
        if (cancelled) return
        setPatient(patientData)
        setError(null)
        loadedPatientIdRef.current = patientId
      } catch (err) {
        if (cancelled) return
        // A page already on screen is not replaced by an error screen: a transient failure on a background
        // refresh must not turn a loaded patient into « Patient introuvable ». Say so and keep what we have.
        if (loadedPatientIdRef.current === patientId) {
          showErrorToast(err, "Le dossier du patient n'a pas pu être rechargé.")
        } else {
          setError(err instanceof ApiError ? err.message : "Échec du chargement des données du patient")
        }
        return
      } finally {
        if (!cancelled) setLoading(false)
      }

      // ---- Phase 2: everything the cards and tabs read. All seven in parallel; each degrades to empty. ----
      try {
        // `appointments` was awaited on its own between the two phases for no reason — nothing in phase 1
        // needed it, so it cost a whole serial round trip before the other six could even start.
        // The treatment plans and the saved medical documents used to be fetched by two effects of their own,
        // keyed on the same `[patientId, refreshKey]`. Folded in here so `detailsLoading` actually covers
        // everything it gates — a flag that is false while two of the tabs are still empty would put the
        // « Aucun document enregistré » it is meant to suppress right back on screen.
        const [
          appointmentsData,
          medicalHistory,
          familyHistory,
          dentalRecordsData,
          filesData,
          foldersData,
          invoicesData,
          plansData,
          documentsData,
        ] = await Promise.all([
          appointmentsApi.list({ patientId }).catch(() => []),
          patientMedicalHistoryApi.list(patientId).catch(() => []),
          patientFamilyHistoryApi.list(patientId).catch(() => []),
          dentalRecordsApi.list(patientId).catch(() => []),
          patientFilesApi.getFiles(patientId).catch(() => []),
          patientFilesApi.getFolders(patientId).catch(() => []),
          invoicesApi.list({ patientId }).catch(() => []),
          treatmentPlansApi.list({ patientId }).catch(() => []),
          medicalDocumentsApi.list(patientId).catch(() => []),
        ])
        if (cancelled) return
        setTreatmentPlans(plansData)
        setMedicalDocuments(documentsData)
        setAppointments(appointmentsData)
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
        // Every call above already degrades to `[]`, so reaching here means a genuine fault rather than one
        // endpoint being down. The identity is on screen either way, so this is a toast, not an error page.
        if (!cancelled) showErrorToast(err, "Certaines données du dossier n'ont pas pu être chargées.")
      } finally {
        if (!cancelled) setDetailsLoading(false)
      }
    }

    void loadPatientData()
    return () => {
      cancelled = true
    }
  }, [patientId, refreshKey])

  // (The treatment plans — for the record modal's plan-step picker and the plan card — and the saved medical
  // documents are loaded by the phase-2 batch above, not by effects of their own.)

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
  // Only a tab that still exists is honoured. `odontogram` was one until it became a card of its own, and a
  // value with no trigger leaves Radix showing an empty panel under an unselected tab strip — so an old
  // bookmark or a stale link falls back to the default rather than to a blank page.
  useEffect(() => {
    const tab = new URLSearchParams(window.location.search).get("tab")
    if (tab && PATIENT_TABS.includes(tab)) setActiveTab(tab)
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

  /**
   * Re-read after a save, via the page's single loader.
   *
   * This used to be a second, hand-written copy of the whole load sequence, and the two had already drifted:
   * the copy refreshed the treatment plans but *not* the invoices, so editing a patient left the « Facturé »
   * badges and the double-invoicing guard (`invoicedDentalRecordIds`) stale — while the main loader refreshed
   * the invoices but not the plans. One loader, one `refreshKey`, which is what every other handler on this
   * page already does. The « rechargé » failure message it carried lives in the loader now.
   */
  const handleEditSuccess = () => setRefreshKey((k) => k + 1)

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
          {/* A skeleton in the shape of the page, so nothing jumps when the identity lands — replacing a
              single line of centred text that gave no hint of what was coming. This branch is now short-lived
              (one request) rather than covering eight. */}
          <main className="flex-1 overflow-y-auto p-4 md:p-6" role="status" aria-label="Chargement du dossier patient">
            <div className="mx-auto max-w-7xl space-y-6">
              <div className="h-9 w-48 animate-pulse rounded bg-muted" />
              <div className="space-y-3">
                <div className="h-9 w-72 animate-pulse rounded bg-muted" />
                <div className="h-5 w-full max-w-2xl animate-pulse rounded bg-muted" />
              </div>
              <div className="h-64 animate-pulse rounded-lg bg-muted" />
              <div className="h-10 w-full animate-pulse rounded-lg bg-muted" />
              <div className="h-48 animate-pulse rounded-lg bg-muted" />
            </div>
          </main>
        </div>
      </div>
    )
  }

  // `!patient`, not `error || !patient`: a background refresh that fails now toasts and keeps the page, so
  // this screen is reserved for the case where there is genuinely nothing to show.
  if (!patient) {
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

            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="min-w-0 space-y-2">
                <div className="flex min-w-0 flex-wrap items-center gap-3">
                  {/* `truncate` + `min-w-0` is what makes the `shrink-0` on the action row mean something: without
                      it the name refuses to shrink below its text, so it keeps pushing until the group wraps
                      anyway. A very long name ellipsizes and carries the full value in its `title`. */}
                  <h1 className="min-w-0 truncate text-3xl font-semibold text-foreground" title={patientName}>
                    {patientName}
                  </h1>
                  {hasFlags && (
                    <div className="flex flex-wrap gap-1">
                      {patient.flags?.filter(flag => flag.isActive).map((flag) => (
                        <Badge key={flag.id} variant="destructive" className="gap-1">
                          <Flag className="h-3 w-3" />
                          {flag.flagType}
                        </Badge>
                      ))}
                    </div>
                  )}
                </div>

                {/*
                  Identity strip — âge · téléphone · assureur, and allergies.

                  Every one of these facts already existed on this page, in the three-card grid at the very
                  bottom — *below* a full-width odontogram, the plan card and seven tabs of tables. Allergies
                  in particular sat at the end of the second card, which means the one thing a dentist must
                  see before injecting anything was several screens of scrolling away, on the page they open
                  to check it. The cards below stay as the complete record; this is the part that cannot wait.

                  Allergies use `destructive`, the same weight as a patient flag, and the « Aucune allergie
                  signalée » case is stated explicitly rather than rendering nothing — an empty space cannot
                  distinguish « nothing to declare » from « nobody has asked yet », and those are different
                  clinical facts.
                */}
                <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
                  {age !== null && (
                    <span className="text-muted-foreground">
                      <span className="font-medium text-foreground">{age} ans</span>
                      {patient.gender ? ` · ${genderLabel(patient.gender)}` : ""}
                    </span>
                  )}
                  {patient.phoneNumber ? (
                    <a
                      href={`tel:${patient.phoneNumber}`}
                      className="font-medium text-foreground underline-offset-2 hover:underline"
                    >
                      {patient.phoneNumber}
                    </a>
                  ) : (
                    <span className="text-amber-700 dark:text-amber-400">Aucun téléphone</span>
                  )}
                  {patient.insuranceInfo?.provider && (
                    <span className="text-muted-foreground">{patient.insuranceInfo.provider}</span>
                  )}
                  {/* « Adressé par » belongs in the strip and not only in the card below: a referred patient owes
                      the referrer a lettre de liaison, and that obligation has to be visible on opening the file
                      rather than three screens down. Rendered only when there is one — a patient who came on
                      their own has nothing to state. */}
                  {patient.referredBy && (
                    <span className="text-muted-foreground">
                      Adressé par <span className="font-medium text-foreground">{patient.referredBy}</span>
                    </span>
                  )}
                </div>

                <div className="flex flex-wrap items-center gap-2">
                  {allergiesList.length > 0 ? (
                    <>
                      <span className="text-xs font-semibold uppercase tracking-wide text-destructive">
                        Allergies
                      </span>
                      {allergiesList.map((allergy: string, index: number) => (
                        <Badge key={index} variant="destructive" className="text-xs">
                          {allergy}
                        </Badge>
                      ))}
                    </>
                  ) : (
                    <span className="text-xs text-muted-foreground">Aucune allergie signalée</span>
                  )}
                </div>
              </div>

              {/*
                `size="sm"` + trimmed labels so the four actions stay on the NAME's line with the rail expanded.
                At default size and full wording they measured ~780px, which does not fit beside a `text-3xl`
                name once the sidebar takes its 256px — so the whole group dropped to a second row, pushing the
                identity strip and everything under it down by a button's height.

                The words removed are the ones the context already supplies: this *is* the patient's page, so
                « Modifier le patient » is « Modifier », and each button keeps its icon plus a `title` carrying
                the full phrase. `shrink-0` stops the row being compressed instead of the name.

                `flex-wrap` is kept deliberately as the last resort: below roughly 1100px of content the group
                genuinely cannot fit, and wrapping there is far better than overflowing horizontally on a phone.
              */}
              <div className="flex shrink-0 flex-wrap gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setEditDialogOpen(true)}
                  className="gap-2"
                  title="Modifier le patient"
                >
                  <Edit className="h-4 w-4" />
                  Modifier
                </Button>
                {/* Files live on their own route, which is the whole manager — folders, upload, delete. It sits in
                    the action row rather than as a panel above the odontogram: « do they have a panoramique? » is a
                    question you go and answer, not one worth spending permanent vertical space on. */}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => router.push(`/patients/${patient.id}/files`)}
                  className="gap-2"
                  title="Fichiers et dossiers du patient"
                >
                  <FolderOpen className="h-4 w-4" />
                  Fichiers
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setRecordModalOpen(true)}
                  className="gap-2"
                  title="Ajouter un dossier médical"
                >
                  <FileText className="h-4 w-4" />
                  Dossier médical
                </Button>
                <Button size="sm" onClick={() => router.push(`/appointments?patientId=${patient.id}`)}>
                  Planifier un RDV
                </Button>
              </div>
            </div>

            {/* Past visits with no fiche yet. Renders nothing when there are none, so it costs no space in the
                steady state — and when it does appear it is the most actionable thing on the page, which is why it
                sits above the notes rather than below them. */}
            <PatientUndocumentedVisits
              appointments={appointments}
              records={dentalRecords}
              onRecord={(appointmentId) => {
                // Exactly the state the `?addRecord=1&appointmentId=…` deep-link sets: thread the visit so the
                // editor proposes its booked acts and saving closes that visit's post-visit prompt. `editingRecord`
                // must be cleared first — a stale edit target forces `recordAppointment` to null.
                setEditingRecord(null)
                setReviewAppointmentId(appointmentId)
                setRecordModalOpen(true)
              }}
            />

            {/* Notes own this row now — alerts on the left, ordinary notes on the right. Files moved to a button in
                the action row above, which freed the whole width for the one thing here that must be read. */}
            <PatientNotesStrip
              patient={patient}
              records={dentalRecords}
              onEdit={() => setEditDialogOpen(true)}
            />

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

            {/*
              The odontogram leads the patient page: for a dentist it is the chart the whole consultation is
              read off, and it spent its life as the 2nd of 8 tabs — one click away, and invisible until asked
              for. Promoted to a full-width card of its own (it needs the width: 16 teeth per arch, two
              dentitions), above the plan card and above the tabs.

              It deliberately replaced the « Solde patient » card that used to sit here. That card put six money
              figures across the top of every patient page, two of which measured different things — « Solde dû »
              is what is still owed, « Reste à charge » is lifetime gross billed minus CNAM's share — so the same
              patient legitimately read « 90,000 DT » and « 1 770,000 DT » side by side with nothing saying why.
              Outstanding debt is still one click away in « Créances », the patient's Factures tab, and the plan
              card's own encaissé / total line.
            */}
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2">
                  <Smile className="h-5 w-5" />
                  Odontogramme
                </CardTitle>
                <CardDescription>
                  Cliquez sur une dent pour noter un diagnostic (à traiter) ; les actes réalisés s&apos;ajoutent
                  automatiquement lors de l&apos;enregistrement d&apos;un acte médical.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Odontogram
                  patientId={patientId}
                  dentition={patient.dentition}
                  onCreatePlan={(seeds) => {
                    setPlanSeeds(seeds)
                    setSeededPlanOpen(true)
                  }}
                />
              </CardContent>
            </Card>

            {/* Treatment leads the patient page now. A devis buried in the 8th tab was the whole reason the plan
                felt disconnected from the patient it belongs to. A band rather than a card since the redesign —
                ~76 px instead of ~250 — and it renders only when the patient has no plans at all. */}
            <PatientPlansStrip
              plans={treatmentPlans}
              onOpen={() => openTab("treatment-plans")}
              onChanged={() => setRefreshKey((k) => k + 1)}
            />

            <div ref={tabsRef} className="scroll-mt-4" />
            <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4">
              {/*
                Seven, not eight: the odontogram is now a card above, not a tab.

                Seven equal columns of icon + French label do not fit a laptop: « Dossiers médicaux » and
                « Plan de traitement » in one seventh of the width crushed or clipped below roughly 1280 px,
                and this page is outside the responsive pass. It now wraps into rows — 2 across on a phone,
                4 on a tablet, 7 only when there is genuinely room — which needs `h-auto` to override the
                primitive's fixed `h-9`, and `items-stretch` so a wrapped row's triggers keep equal heights.
              */}
              <TabsList className="grid h-auto w-full grid-cols-2 items-stretch gap-1 p-1 sm:grid-cols-4 lg:grid-cols-7">
                <TabsTrigger value="medical-records" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <FileCheck className="h-4 w-4" />
                  Dossiers médicaux
                </TabsTrigger>
                <TabsTrigger value="appointments" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <Calendar className="h-4 w-4" />
                  Rendez-vous
                </TabsTrigger>
                <TabsTrigger value="notes" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <FileText className="h-4 w-4" />
                  Notes
                </TabsTrigger>
                <TabsTrigger value="documents" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <FileText className="h-4 w-4" />
                  Documents
                </TabsTrigger>
                <TabsTrigger value="files" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <FileText className="h-4 w-4" />
                  Fichiers
                </TabsTrigger>
                <TabsTrigger value="factures" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
                  <Receipt className="h-4 w-4" />
                  Factures
                </TabsTrigger>
                <TabsTrigger value="treatment-plans" className="h-auto min-h-9 gap-2 whitespace-normal py-1.5 text-center leading-tight">
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
                      <EmptyOrLoading loading={detailsLoading}>Aucun dossier dentaire</EmptyOrLoading>
                    ) : (
                      <div className="overflow-x-auto">
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Date</TableHead>
                              <TableHead>Type d'acte</TableHead>
                              {/* « Type de dents » removed: the dentition is a property of the patient, stated once
                                  in their file, so repeating it on every row said nothing per-row. */}
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

              {/* Notes Tab */}
              <TabsContent value="notes">
                <Card>
                  <CardHeader>
                    <CardTitle>Notes des dossiers médicaux</CardTitle>
                    <CardDescription>Notes et notes importantes des dossiers dentaires</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {dentalRecords.length === 0 ? (
                      <EmptyOrLoading loading={detailsLoading}>Aucun dossier médical</EmptyOrLoading>
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
                      <EmptyOrLoading loading={detailsLoading}>Aucun document enregistré</EmptyOrLoading>
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
                      <EmptyOrLoading loading={detailsLoading}>Aucun rendez-vous</EmptyOrLoading>
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
                              <TableHead className="text-right">Actions</TableHead>
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
                                
                                /*
                                 * « Enregistrer la fiche » — the same action the post-visit notification offers,
                                 * reachable from the history instead of only from the bell (which is dismissible,
                                 * and gone once read).
                                 *
                                 * Offered when the visit is OVER and not yet recorded. "Over" is measured from the
                                 * appointment's END, not its start, matching what makes the post-visit review due
                                 * server-side — a 30-minute visit is not finished ten minutes in.
                                 *
                                 * `Cancelled` / `NoShow` are excluded even though neither is « Terminé ». Saving a
                                 * fiche calls `Appointment.MarkVisitCompleted`, which returns `Contradicted` for
                                 * exactly those two and is swallowed by its best-effort caller — so the fiche would
                                 * persist while the appointment silently stayed cancelled. A visit recorded as not
                                 * having happened should not offer to record what happened during it.
                                 */
                                const status = normalizeStatus(appointment.status)
                                const endedAt =
                                  new Date(appointment.appointmentDateTime).getTime() + durationMinutes * 60_000
                                const canRecordVisit =
                                  endedAt < Date.now() &&
                                  status !== "Completed" &&
                                  status !== "Cancelled" &&
                                  status !== "NoShow"

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
                                      {/* A visit can be several acts; the shared summary joins them
                                          (« Détartrage + Obturation ») and the dot keeps the lead act's colour,
                                          which is what the row's own left border already uses. */}
                                      {appointmentActsSummary(appointment) ? (
                                        <div className="flex items-center gap-2">
                                          <div
                                            className="h-3 w-3 rounded-full shrink-0"
                                            style={{ backgroundColor: appointment.procedureColorHex || "#6C757D" }}
                                          />
                                          <span>{appointmentActsSummary(appointment)}</span>
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
                                    <TableCell className="text-right">
                                      {canRecordVisit ? (
                                        <Button
                                          variant="outline"
                                          size="sm"
                                          className="gap-1.5 whitespace-nowrap"
                                          onClick={() => {
                                            /*
                                             * Exactly the state the `?addRecord=1&appointmentId=…` deep-link sets,
                                             * so the modal prefills identically: `reviewAppointmentId` feeds
                                             * `recordAppointment`, which proposes the visit's booked act and
                                             * pre-selects its devis step. Setting it here rather than navigating
                                             * avoids a round trip through the URL for something already on screen.
                                             *
                                             * `setEditingRecord(null)` is required, not tidying: a non-null
                                             * `editingRecord` forces `recordAppointment` to null (an edit must never
                                             * be re-proposed), so a stale value would open the modal with no prefill.
                                             */
                                            setEditingRecord(null)
                                            setReviewAppointmentId(appointment.id)
                                            setRecordModalOpen(true)
                                          }}
                                          title="Enregistrer la fiche de soins de cette séance"
                                        >
                                          <FileText className="h-3.5 w-3.5" />
                                          Enregistrer la fiche
                                        </Button>
                                      ) : (
                                        <span className="text-muted-foreground/60">—</span>
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
                    {detailsLoading ? (
                      <EmptyOrLoading loading>Aucun fichier téléversé</EmptyOrLoading>
                    ) : files.length === 0 && folders.length === 0 ? (
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
                                  className="p-3 cursor-pointer hover:bg-accent transition-colors hover:border-primary/40"
                                  onClick={() => setCurrentFolderId(folder.id)}
                                >
                                  <div className="flex items-center justify-between">
                                    <div className="flex items-center gap-3 flex-1 min-w-0">
                                      <div className="p-2 rounded-lg bg-accent/30">
                                        <Folder className="h-5 w-5 text-primary" />
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
                    {/* onChanged was missing: recording a payment here left the plan card above showing the
                        pre-payment figures until a manual refresh. */}
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
                    <p className="text-xs font-medium text-muted-foreground">Adressé par</p>
                    <p className="text-sm text-foreground">{patient.referredBy || "Non renseigné"}</p>
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

      {/*
        Facturer cette intervention — issues the note d'honoraires AND records the cash taken at the end of the
        session, in one action.

        This replaced a prefilled `InvoiceFormModal`, and the replacement is the point. That flow produced a
        *draft*, so money the dentist had already been handed still needed a second, separate action nobody was
        prompted to take — which is how `DentalRecord.AmountPaid` became a field shaped like a receipt that no
        money read has ever touched.

        The per-tooth pricing rule that used to be computed right here (quantity × unit price vs. one flat fee)
        moved to the server (`DentalRecordInvoiceLines`). It was **moved, not copied**: two implementations of
        how recorded work becomes money is the § 5.10 defect in a new place.
      */}
      <BillDentalRecordDialog
        record={billingRecord}
        patientName={patientName}
        onOpenChange={(open) => { if (!open) setBillingRecord(null) }}
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
                    <Loader2 className="h-8 w-8 animate-spin text-primary" />
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


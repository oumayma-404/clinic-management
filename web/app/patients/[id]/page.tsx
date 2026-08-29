"use client"

import { useState, useEffect, useMemo, useRef } from "react"
import { useParams, useRouter } from "next/navigation"
import Link from "next/link"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Separator } from "@/components/ui/separator"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import type { PagedResponse } from "@/lib/api/paging"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { formatDT, formatDate, formatDateFr, formatDateTime, formatFileSize, quoteFr } from "@/lib/format"
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
  MoreHorizontal,
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
import { patientFlagLabel } from "@/components/patient/patient-flag-labels"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ZONES, zoneChipClass } from "@/lib/zones"
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
import { FilePreviewDialog } from "@/components/patients/files/file-preview-dialog"
import { useFilePreview } from "@/components/patients/files/use-file-preview"
import { isImageFile, isPdfFile, isPreviewableFile } from "@/components/patients/files/file-kind"

const calculateAge = (dob: string | null | undefined) => {
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
 * The placeholder for a section whose request has not answered yet.
 *
 * Load-bearing since the page began painting its identity before its details: `[]` used to be reachable only
 * after every request had answered, so « Aucun dossier dentaire » was always true. It is now also the state
 * *before* the request answers — and a page that tells a dentist their patient has no records, no
 * appointments and no files, a beat before listing all three, is worse than one that took longer to appear.
 */
function SectionSkeleton() {
  return (
    <div className="space-y-2 py-6" role="status" aria-label="Chargement…">
      {[0, 1, 2].map((i) => (
        <div key={i} className="h-5 animate-pulse rounded bg-muted" />
      ))}
    </div>
  )
}

/**
 * The nine phase-2 reads, named — so a tab can say which one of them failed.
 *
 * <p>Naming them is the whole fix. Every one of these calls carried `.catch(() => [])`, which is a perfectly
 * reasonable way to keep one dead endpoint from taking down a patient's file — and a terrible way to *report*
 * it, because `[]` is also what a genuinely empty section returns. A failed `dentalRecordsApi` rendered « Aucune
 * fiche de soins » about a patient with forty; a failed `treatmentPlansApi` made `PatientPlansStrip` return
 * `null`, silently asserting « never had a plan » about someone with three. The outer `catch` could never fire,
 * so nothing anywhere on the page said a word.</p>
 */
type PatientSection =
  | "appointments"
  | "medicalHistory"
  | "familyHistory"
  | "dentalRecords"
  | "files"
  | "folders"
  | "invoices"
  | "plans"
  | "documents"

/**
 * What a section shows when its read FAILED — deliberately not what it shows when it is empty.
 *
 * <p>The page already knows this rule: the identity loader states that « a transient failure on a background
 * refresh must not turn a loaded patient into "Patient introuvable" ». This is the same reasoning one level
 * down, per tab.</p>
 *
 * <p>The treatment itself now lives in `ui/load-failure.tsx` — it was written here and used only here, which is why
 * six other surfaces reached for `.catch(() => setX([]))` instead and rendered their failures as « aucun ». This
 * wrapper is kept because « cette section » is the page's own wording and nine call sites share it.</p>
 */
function SectionLoadFailure({ onRetry }: { onRetry: () => void }) {
  return (
    <LoadFailureNotice
      message="Cette section n'a pas pu être chargée."
      detail="Son contenu n'est pas forcément vide."
      onRetry={onRetry}
    />
  )
}

/**
 * What a visit row derives about itself: how long it ran, whether it is cancelled, and whether it can still be
 * written up. One implementation because the table and the card list must agree — the rule below is subtle
 * enough that a second copy would drift.
 *
 * « Enregistrer la fiche » is offered when the visit is OVER and not yet recorded. "Over" is measured from the
 * appointment's END, not its start, matching what makes the post-visit review due server-side — a 30-minute
 * visit is not finished ten minutes in.
 *
 * `Cancelled` / `NoShow` are excluded even though neither is « Terminé ». Saving a fiche calls
 * `Appointment.MarkVisitCompleted`, which returns `Contradicted` for exactly those two and is swallowed by its
 * best-effort caller — so the fiche would persist while the appointment silently stayed cancelled. A visit
 * recorded as not having happened should not offer to record what happened during it.
 */
function appointmentVisitState(appointment: AppointmentDto) {
  const durationMinutes = appointment.duration
    ? parseInt(appointment.duration.split(":")[0]) * 60 + parseInt(appointment.duration.split(":")[1] || "0")
    : 0
  const status = normalizeStatus(appointment.status)
  const endedAt = new Date(appointment.appointmentDateTime).getTime() + durationMinutes * 60_000

  return {
    durationMinutes,
    isCanceled: appointment.status === "Cancelled",
    canRecordVisit:
      endedAt < Date.now() && status !== "Completed" && status !== "Cancelled" && status !== "NoShow",
  }
}

/**
 * A dental record's notes, expanded or folded to a count — the most complex cell on this page, and now the one
 * implementation behind both the table row and its card.
 *
 * It has to survive the card conversion rather than flatten to a count: the notes are what a dentist opens the
 * dossier to read, and a fiche whose notes are only reachable on a desktop is a fiche that is not readable at
 * the chair.
 *
 * ⚠️ Returns `null` when there is nothing to show, but the **caller** must still test for that: the table prints
 * « - » and the card omits the field (AC-17), and `CardList` cannot tell an element that renders nothing from
 * one that renders something.
 */
function DentalRecordNotes({
  record,
  isExpanded,
  onToggle,
}: {
  record: DentalRecordDto
  isExpanded: boolean
  onToggle: (expanded: boolean) => void
}) {
  const importantNotes = record.importantNotes ?? []
  const notes = record.notes ?? []
  if (importantNotes.length === 0 && notes.length === 0) return null

  if (!isExpanded) {
    return (
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-muted-foreground">
            {importantNotes.length + notes.length}{" "}
            {importantNotes.length + notes.length === 1 ? "note" : "notes"}
          </span>
          {importantNotes.length > 0 && (
            <Badge variant="outline" className="text-xs bg-amber-50 dark:bg-amber-950/20 text-amber-700 dark:text-amber-400 border-amber-200 dark:border-amber-800">
              {importantNotes.length} importantes
            </Badge>
          )}
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="h-6 text-xs text-muted-foreground hover:text-foreground"
          onClick={(e) => {
            e.stopPropagation()
            onToggle(true)
          }}
        >
          <ChevronDown className="h-3 w-3 mr-1" />
          Voir les notes
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-2 text-start">
      {importantNotes.length > 0 && (
        <div className="space-y-1">
          <p className="text-xs font-semibold text-amber-700 dark:text-amber-400 mb-1">Notes importantes :</p>
          <ul className="list-disc list-inside space-y-1 ml-2">
            {importantNotes.map((note, idx) => (
              <li key={idx} className="text-xs font-medium text-amber-900 dark:text-amber-100 bg-amber-50 dark:bg-amber-950/40 px-2 py-1 rounded border border-amber-200 dark:border-amber-800">
                ⚠ {note}
              </li>
            ))}
          </ul>
        </div>
      )}
      {notes.length > 0 && (
        <div className="space-y-1">
          {importantNotes.length > 0 && (
            <p className="text-xs font-semibold text-muted-foreground mb-1">Notes :</p>
          )}
          <ul className="list-disc list-inside space-y-1 ml-2">
            {notes.map((note, idx) => (
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
          onToggle(false)
        }}
      >
        <ChevronUp className="h-3 w-3 mr-1" />
        Réduire
      </Button>
    </div>
  )
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

/**
 * Rows per page in « Dossiers dentaires ».
 *
 * Five, not the app's `DEFAULT_PAGE_SIZE` of 25: this list sits inside a tab under the patient's identity, its
 * rows are tall (teeth badges, expandable notes) and a fiche is *read* rather than scanned — a long-standing
 * patient's forty séances pushed everything below them off the screen.
 */
const DENTAL_RECORDS_PAGE_SIZE = 5

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
   * Which of the nine phase-2 reads failed on the last load. See {@link PatientSection}.
   *
   * <p>Replaced entirely on each load rather than merged, so a successful retry clears the band it raised —
   * a section that stayed marked "failed" after it had loaded would be the same lie in the other direction.</p>
   */
  const [failedSections, setFailedSections] = useState<Set<PatientSection>>(new Set())
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
  /**
   * Open a saved medical document. The « honoraires » type is retired (the PDF endpoint now rejects it), so
   * legacy rows route to the Factures module instead of the dead editor (#13) — one implementation, because the
   * table and the card list both offer « Ouvrir » and a second copy is a second place to forget the redirect.
   */
  const openMedicalDocument = (doc: MedicalDocumentDto) =>
    router.push(doc.documentType === "honoraires" ? "/factures" : `/documents/${doc.documentType}?id=${doc.id}`)

  /**
   * Open the record modal already bound to a finished visit — exactly the state the
   * `?addRecord=1&appointmentId=…` deep-link sets, so the modal prefills identically: `reviewAppointmentId`
   * feeds `recordAppointment`, which proposes the visit's booked act and pre-selects its devis step. Setting it
   * here rather than navigating avoids a round trip through the URL for something already on screen.
   *
   * `setEditingRecord(null)` is required, not tidying: a non-null `editingRecord` forces `recordAppointment` to
   * null (an edit must never be re-proposed), so a stale value would open the modal with no prefill.
   */
  const openVisitRecord = (appointmentId: string) => {
    setEditingRecord(null)
    setReviewAppointmentId(appointmentId)
    setRecordModalOpen(true)
  }

  /** One expansion set behind both the dossiers table and its card list — expanding on a phone must stick. */
  const toggleRecordNotes = (recordId: string, expanded: boolean) =>
    setExpandedNotes((prev) => {
      const next = new Set(prev)
      if (expanded) next.add(recordId)
      else next.delete(recordId)
      return next
    })
  /**
   * « Dossiers dentaires » pages **in the browser**, deliberately.
   *
   * `dentalRecordsApi.list` takes no paging parameters, and four other things on this page read the *whole*
   * history anyway — the Notes tab, the odontogram band, the plan-act reconciliation and the delete
   * confirmation — so asking the server for a slice would mean fetching the same list twice. This is the
   * `PagedResult.FromSource` case (`web/lib/api/paging.ts`), not a client-side filter over a server page: the
   * list here really is complete, so the page it cuts is the page the count describes.
   */
  const [recordsPageRequest, setRecordsPageRequest] = useState(1)
  const recordsPage = useMemo<PagedResponse<DentalRecordDto>>(() => {
    const totalCount = dentalRecords.length
    const totalPages = Math.max(1, Math.ceil(totalCount / DENTAL_RECORDS_PAGE_SIZE))
    // Clamped at render rather than corrected by an effect: deleting the last fiche of the last page must land
    // on a page that exists, and an effect would first paint the empty one.
    const page = Math.min(Math.max(1, recordsPageRequest), totalPages)
    const start = (page - 1) * DENTAL_RECORDS_PAGE_SIZE
    return {
      items: dentalRecords.slice(start, start + DENTAL_RECORDS_PAGE_SIZE),
      page,
      pageSize: DENTAL_RECORDS_PAGE_SIZE,
      totalCount,
      totalPages,
      hasPreviousPage: page > 1,
      hasNextPage: page < totalPages,
    }
  }, [dentalRecords, recordsPageRequest])
  // A different patient is a different history. Navigating between two patients does **not** remount this page
  // (only `params.id` changes), so without this the header search would open the next file on page 3.
  useEffect(() => {
    setRecordsPageRequest(1)
  }, [patientId])
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
  // When in a folder, every loaded file belongs to it; at root, only the files in no folder.
  const currentFiles = currentFolderId ? files : files.filter((f) => !f.folderId)

  /*
   * Newest first, sorted ONCE. Sorting inline in the JSX sorts the state array **in place**, and two trees
   * render this data — so a copy is also what keeps the card list and the table in the same order.
   */
  const filesNewestFirst = [...currentFiles].sort(
    (a, b) => new Date(b.uploadedAt).getTime() - new Date(a.uploadedAt).getTime(),
  )

  // AC-5.3 — the preview lives in one place now; this page held a byte-identical second copy of the hook and
  // the dialog, only the PDF frame having ever been extracted. The sequence is what the viewer's arrows walk:
  // this tab loads every file at once, so there is no page to turn.
  const preview = useFilePreview(patientId, undefined, { files: filesNewestFirst })
  const [refreshKey, setRefreshKey] = useState(0)
  /** Band C — the identity read answered 404 (the patient really is gone), as opposed to failing. */
  const [identityMissing, setIdentityMissing] = useState(false)
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
  /**
   * Keep the SELECTED tab inside the strip's visible window.
   *
   * <p>Below `sm:` the seven tabs are a horizontally scrolling row that always starts at « Dossiers médicaux ».
   * A `?tab=documents` deep-link — which is how `plan-act-row` and the post-visit prompt route here — therefore
   * landed on a panel whose tab was off the right edge, with nothing selected in view: the page looked like it
   * had ignored the link.</p>
   *
   * <p>⚠️ `list.scrollTo`, deliberately NOT `activeTrigger.scrollIntoView`. `scrollIntoView` walks up every
   * scrollable ancestor, and at `sm:` and up this strip does not scroll at all — so it would bubble to the
   * document and drag the viewport down to the tabs (which sit below the odontogram) on every single open. The
   * `scrollWidth <= clientWidth` guard says the same thing twice on purpose: no overflow, nothing to do.</p>
   */
  const tabsListRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const list = tabsListRef.current
    if (!list || list.scrollWidth <= list.clientWidth) return
    const active = list.querySelector<HTMLElement>('[data-state="active"]')
    if (!active) return
    list.scrollTo({ left: Math.max(0, active.offsetLeft - (list.clientWidth - active.offsetWidth) / 2) })
  }, [activeTab])
  const [medicalDocuments, setMedicalDocuments] = useState<MedicalDocumentDto[]>([])
  const [planSeeds, setPlanSeeds] = useState<TreatmentPlanSeedLine[]>([])
  const [seededPlanOpen, setSeededPlanOpen] = useState(false)
  // Both delete endpoints are AdminOrDoctor (A-12). Offer the action only to those roles so a secretary is
  // never sent into a guaranteed 403 — the same rationale procedure-types-table.tsx documents for its writes.
  //
  // ⚠️ This is now the **only** role gate on this page, and that is deliberate rather than an oversight: the five
  // clinical controllers behind these tabs moved to `AnyClinicRole` (reading and recording the patient's file is
  // reception's job in a Tunisian cabinet), while deleting from it stayed `AdminOrDoctor`. So « Modifier »,
  // « Dossier médical », the odontogramme and the antécédents are open on purpose — only destruction is not.
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
        setIdentityMissing(false)
        loadedPatientIdRef.current = patientId
      } catch (err) {
        if (cancelled) return
        // A page already on screen is not replaced by an error screen: a transient failure on a background
        // refresh must not turn a loaded patient into « Patient introuvable ». Say so and keep what we have.
        if (loadedPatientIdRef.current === patientId) {
          showErrorToast(err, "Le dossier du patient n'a pas pu être rechargé.")
        } else {
          /*
           * Band C — a 404 and a 500 are DIFFERENT facts and this screen used to state the first for both. « Le
           * patient recherché n'existe pas » on a transient failure sends a dentist to look for a record that is
           * sitting right there, and « Retour aux patients » was the only way out of it.
           */
          setIdentityMissing(err instanceof ApiError && err.status === 404)
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
        //
        // ⚠️ Each read still degrades to `[]` — one dead endpoint must not take down the whole file — but it now
        // RECORDS that it failed. `[]` alone was indistinguishable from a genuinely empty section, which is how
        // a failed fetch came to render « Aucun rendez-vous » about a patient with a full history. See
        // `PatientSection` / `SectionLoadFailure`.
        //
        const failed = new Set<PatientSection>()
        const attempt = <T,>(section: PatientSection, request: Promise<T[]>): Promise<T[]> =>
          request.catch(() => {
            failed.add(section)
            return [] as T[]
          })

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
          attempt("appointments", appointmentsApi.list({ patientId })),
          attempt("medicalHistory", patientMedicalHistoryApi.list(patientId)),
          attempt("familyHistory", patientFamilyHistoryApi.list(patientId)),
          attempt("dentalRecords", dentalRecordsApi.list(patientId)),
          attempt("files", patientFilesApi.getFiles(patientId)),
          attempt("folders", patientFilesApi.getFolders(patientId)),
          attempt("invoices", invoicesApi.list({ patientId })),
          attempt("plans", treatmentPlansApi.list({ patientId })),
          attempt("documents", medicalDocumentsApi.list(patientId)),
        ])
        if (cancelled) return
        setFailedSections(failed)
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

  /*
   * Reload files when the folder changes.
   *
   * ⚠️ The failure is recorded in `failedSections`, not absorbed into `[]`. This read used to carry
   * `.catch(() => [])`, which made **an empty folder and an unreachable server the same screen** — on the tab whose
   * folders hold radiographs, so « aucun fichier » about a patient's panoramics is exactly the wrong answer. The
   * outer `catch` could then only fire on a render fault, which is why the toast never appeared.
   *
   * It reuses the `"files"` section rather than inventing a second flag: the same tab body renders both this read
   * and the phase-2 one, so two flags would be two ways to say one thing and the tab would have to pick.
   */
  useEffect(() => {
    const loadFilesForFolder = async () => {
      if (!patientId) return
      try {
        const filesData = await patientFilesApi.getFiles(patientId, currentFolderId || undefined)
        setFiles(filesData)
        setFailedSections((prev) => {
          if (!prev.has("files")) return prev
          const next = new Set(prev)
          next.delete("files")
          return next
        })
      } catch (error) {
        setFailedSections((prev) => (prev.has("files") ? prev : new Set(prev).add("files")))
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
    // A skeleton in the shape of the page, so nothing jumps when the identity lands — replacing a single line
    // of centred text that gave no hint of what was coming. This branch is now short-lived (one request)
    // rather than covering eight. `role="status"` moved from `<main>` onto the skeleton itself: the shell's
    // `<main>` is shared by every page, so the live region has to belong to the thing that is loading.
    return (
      <AppShell>
        <div role="status" aria-label="Chargement du dossier patient" className="space-y-6">
          <div className="h-9 w-48 animate-pulse rounded bg-muted" />
          <div className="space-y-3">
            <div className="h-9 w-72 animate-pulse rounded bg-muted" />
            <div className="h-5 w-full max-w-2xl animate-pulse rounded bg-muted" />
        </div>
        <div className="h-64 animate-pulse rounded-lg bg-muted" />
        <div className="h-10 w-full animate-pulse rounded-lg bg-muted" />
        <div className="h-48 animate-pulse rounded-lg bg-muted" />
        </div>
      </AppShell>
    )
  }

  // `!patient`, not `error || !patient`: a background refresh that fails now toasts and keeps the page, so
  // this screen is reserved for the case where there is genuinely nothing to show.
  if (!patient) {
    // Band C — « introuvable » is reserved for a 404. Anything else is a read that failed, and it gets a retry.
    const genuinelyMissing = identityMissing
    return (
      <AppShell width="none" gutter={false} mainClassName="flex items-center justify-center">
        <div className="max-w-md text-center">
          <h2 className="text-2xl font-semibold text-foreground">
            {genuinelyMissing ? "Patient introuvable" : "Dossier non chargé"}
          </h2>
          <p className="mt-2 text-muted-foreground">
            {genuinelyMissing
              ? "Le patient recherché n'existe pas — il a peut-être été supprimé."
              : (error ??
                "Le dossier n'a pas pu être lu. Cela ne veut pas dire qu'il n'existe pas.")}
          </p>
          <div className="mt-4 flex flex-col items-center gap-2 sm:flex-row sm:justify-center">
            {!genuinelyMissing && (
              <Button onClick={() => setRefreshKey((k) => k + 1)} className="w-full sm:w-auto">
                Réessayer
              </Button>
            )}
            <Button
              variant={genuinelyMissing ? "default" : "outline"}
              onClick={() => router.push("/patients")}
              className="w-full sm:w-auto"
            >
              Retour aux patients
            </Button>
          </div>
        </div>
      </AppShell>
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

  /**
   * The appointment the record documents, so its booked procedure can be PROPOSED in the record modal and its
   * plan step pre-selected (AC-9). **One source: the visit the modal was opened from** — « Enregistrer la fiche »
   * on an appointment row, or the `?addRecord=1&appointmentId=…` post-visit deep-link. A record being edited is
   * never re-proposed.
   *
   * ⚠️ There used to be a second source, and it invented data. When the modal was opened from « Ajouter une
   * fiche » (which sets no `reviewAppointmentId`), it fell back to « today's live appointment » — the FIRST of
   * this patient's appointments today carrying an act. The list arrives ordered by `appointmentDateTime`, so
   * that was the *earliest* one of the day, not the visit being recorded: booking a RDV with no act and then
   * adding a fiche proposed some other visit's procedure, and the dentist had to notice and undo it. The
   * fiche was never even linked to the guessed visit — `appointmentId` below stays null on that path — so the
   * guess shaped the content of a record that would never reference it.
   *
   * A visit that wants its act proposed has a button that says so on its own row; guessing which of the day's
   * visits a fiche is for is not something this page can know.
   */
  const recordAppointment: AppointmentDto | null =
    editingRecord || !reviewAppointmentId
      ? null
      : (appointments.find((a) => a.id === reviewAppointmentId) ?? null)

  const appointmentsNewestFirst = [...appointments].sort(
    (a, b) => new Date(b.appointmentDateTime).getTime() - new Date(a.appointmentDateTime).getTime(),
  )


  const handleDownloadFile = async (file: PatientFileDto) => {
    try {
      const blob = await patientFilesApi.downloadFile(patientId, file.id)
      downloadBlob(blob, file.fileName)
    } catch (error) {
      // AC-P3.29 — matches what the same action already does in `patient-files-manager.tsx`; a silent
      // console.error made a failed download indistinguishable from a browser that blocked the save.
      showErrorToast(error, `Impossible de télécharger ${quoteFr(file.fileName)}.`)
    }
  }
  
  /** Did any of the named reads fail on the last load? */
  const sectionFailed = (...sections: PatientSection[]) => sections.some((s) => failedSections.has(s))

  /** Re-run the whole phase-2 batch. The reads are cheap and always fetched together, so a per-section retry
   *  would be three code paths for one gesture. */
  const retrySections = () => setRefreshKey((k) => k + 1)

  /**
   * What a tab body shows when it holds no rows — **three** states, never two.
   *
   * <p>Loading wins (the request has not answered, so nothing can be asserted); then failure (it answered
   * badly, and « aucun » would be a claim we cannot make); only then the real empty state, which is the one
   * case where being welcoming and specific is worth the space. Routing all four tabs through one helper is
   * what stops the third of them from quietly regressing to a grey sentence.</p>
   */
  const renderSectionEmpty = (sections: PatientSection[], empty: React.ReactNode) => {
    if (detailsLoading) return <SectionSkeleton />
    if (sectionFailed(...sections)) return <SectionLoadFailure onRetry={retrySections} />
    return empty
  }

  /**
   * The Notes tab reaches "nothing to show" two different ways — no fiches at all, and fiches that carry no
   * notes — and both are the same fact to the reader, so they share one element rather than two near-identical
   * blocks that would drift.
   */
  const notesEmptyState = (
    <EmptyState
      icon={FileText}
      size="compact"
      chipClassName={zoneChipClass(ZONES.daily)}
      title="Aucune note de séance"
      description="Les notes saisies dans une fiche de soins apparaissent ici."
    />
  )

  // Parse allergies from string (comma-separated)
  const allergiesList = patient.allergies
    ? patient.allergies.split(',').map(a => a.trim()).filter(Boolean)
    : []
  
  // Parse medical history (if it contains structured data, otherwise show as text)
  const medicalHistoryText = patient.medicalHistory || "Aucun renseignement"
  

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        {/*
          Back — desktop only.

          Below `md:` this ghost button cost a full ~60px row directly above the patient's own name, on every
          single open, to duplicate navigation the phone already has twice over: `bottom-nav.tsx` puts
          « Patients » on the thumb at all times, and the browser's own back gesture is the habit users actually
          use. That row plus the (now wrapping) name is what was pushing the allergies strip below the fold.
        */}
        <Button
          variant="ghost"
          onClick={() => router.push("/patients")}
          className="hidden gap-2 md:inline-flex"
        >
          <ArrowLeft className="h-4 w-4" />
          Retour aux patients
        </Button>

        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0 space-y-2">
            <div className="flex min-w-0 flex-wrap items-center gap-3">
              {/*
                ⚠️ The name WRAPS. It must never be `truncate`, and the `title` that used to carry the full
                value is gone with it.

                This is the page's strongest identity, and `ui/card-list.tsx` already states the rule for the
                weaker version of the same thing: « the heading WRAPS; it must never be `truncate` …
                "Mohamed Ali Ben Romdh…" is not a weaker label, it is a different person ». At 390px, 30px type
                fits roughly 23 characters, so « Mohamed Amine Ben Abdallah » ellipsised — and `title=` is
                unreachable on touch, which left the full name recoverable only by scrolling to
                « Informations personnelles ».

                `text-2xl` with `sm:text-title` rather than a flat `text-3xl`: a wrapping name needs to not
                consume three lines of a phone before the clinical strip below it.
              */}
              <h1 className="min-w-0 text-2xl font-semibold leading-tight text-foreground [overflow-wrap:anywhere] sm:text-title">
                {patientName}
              </h1>
              {hasFlags && (
                <div className="flex flex-wrap gap-1">
                  {patient.flags?.filter(flag => flag.isActive).map((flag) => (
                    <Badge key={flag.id} variant="destructive" className="gap-1">
                      <Flag className="h-3 w-3" />
                      {/* The raw enum name was printed here — « HighPriority » in a red badge beside the
                          patient's name, at the top of an otherwise entirely French record. */}
                      {patientFlagLabel(flag.flagType)}
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
                  /* `touch-target`: an isolated 20px-tall control, and on a phone it is the one link on this
                     screen someone actually taps (it dials the patient). */
                  className="touch-target inline-flex items-center font-medium text-foreground underline-offset-2 hover:underline"
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
            the full phrase. `sm:shrink-0` stops the row being compressed instead of the name.

            ⚠️ **`shrink-0` is `sm:`-prefixed, and that is the whole fix for a real phone defect.** Unprefixed,
            it pinned this group at its ~500 px max-content width inside a 343 px column — so `flex-wrap` never
            fired (the box was already as wide as its content), « Planifier un RDV » sat off-screen and the whole
            page scrolled sideways. Below `sm:` the group must be allowed to shrink so its own `flex-wrap` can do
            the job it is here for; above it, the name is what needs protecting and the pin is right.
          */}
          <div className="flex min-w-0 flex-wrap gap-2 sm:shrink-0">
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
          // A dead fiches read must not make the band say « Aucune alerte » — see the prop's own note.
          recordsFailed={sectionFailed("dentalRecords")}
          onRetryRecords={retrySections}
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
              dateOfBirth={patient.dateOfBirth}
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
        {/* ⚠️ The band renders NOTHING when `plans` is empty, so a failed `treatmentPlansApi` read is invisible
            and silently asserts « never had a plan » about a patient with three. This is the one section whose
            empty state is "no element at all", which is why the failure has to be reported beside it. */}
        {sectionFailed("plans") && <SectionLoadFailure onRetry={retrySections} />}
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
          {/*
            ⚠️ Below `sm:` this is a SCROLLING ROW, not a grid. Seven tabs in `grid-cols-2` is four rows of
            chrome — roughly a third of a phone screen — pushed above the content on every single open, so the
            patient's actual record started below the fold. A horizontal strip costs one row and keeps the
            same seven destinations.
            `scrollbar-none` is deliberate: the strip is thumb-swiped, and a scrollbar under 44px targets adds
            visual noise for a control nobody drags on a phone. The active tab is styled, so the row never
            looks like it has no state.
          */}
          {/* The strip and its right-edge fade. The fade is `sm:hidden` because that is exactly where the row
              stops scrolling and becomes a grid — a gradient over a grid would shade a tab for no reason. It is
              `pointer-events-none`, so it never intercepts a tap on the tab underneath it. */}
          <div className="relative">
          <TabsList
            ref={tabsListRef}
            className="flex h-auto w-full items-stretch gap-1 overflow-x-auto p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden sm:grid sm:grid-cols-4 sm:overflow-visible lg:grid-cols-7"
          >
            <TabsTrigger value="medical-records" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileCheck className="h-4 w-4" />
              Dossiers médicaux
            </TabsTrigger>
            <TabsTrigger value="appointments" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <Calendar className="h-4 w-4" />
              Rendez-vous
            </TabsTrigger>
            <TabsTrigger value="notes" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileText className="h-4 w-4" />
              Notes
            </TabsTrigger>
            <TabsTrigger value="documents" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileText className="h-4 w-4" />
              Documents
            </TabsTrigger>
            <TabsTrigger value="files" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileText className="h-4 w-4" />
              Fichiers
            </TabsTrigger>
            <TabsTrigger value="factures" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <Receipt className="h-4 w-4" />
              Factures
            </TabsTrigger>
            <TabsTrigger value="treatment-plans" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <ClipboardCheck className="h-4 w-4" />
              Plan de traitement
            </TabsTrigger>
          </TabsList>
          <span
            aria-hidden="true"
            className="pointer-events-none absolute inset-y-0 right-0 w-6 rounded-r-lg bg-gradient-to-l from-muted to-transparent sm:hidden"
          />
          </div>

          {/* Medical Records Tab - Unified View */}
          <TabsContent value="medical-records" className="space-y-4">
            {/* Dental Records Section */}
            <Card>
              {/*
                ⚠️ `flex-wrap` + `min-w-0 flex-1` + a full-width action below `sm:`.

                A Card's content box is ~310px on a 390px phone, and « Ajouter un dossier dentaire » at
                `size="sm"` is ~218px of unwrappable French — so the un-wrapped row left the title block ~92px,
                which wrapped « Dossiers dentaires » onto three lines and its description onto eight. All three
                tab headers on this page carried the same construction.
              */}
              <CardHeader>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <CardTitle className="flex items-center gap-2">
                      <FileCheck className="h-5 w-5" />
                      Dossiers dentaires
                    </CardTitle>
                    <CardDescription>Historique complet des actes et interventions dentaires</CardDescription>
                  </div>
                  <Button
                    onClick={() => {
                      setEditingRecord(null)
                      setRecordModalOpen(true)
                    }}
                    size="sm"
                    className="w-full sm:w-auto"
                  >
                    Ajouter un dossier dentaire
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                {dentalRecords.length === 0 ? (
                  renderSectionEmpty(
                    ["dentalRecords"],
                    <EmptyState
                      icon={FileCheck}
                      size="compact"
                      chipClassName={zoneChipClass(ZONES.daily)}
                      title="Aucune fiche de soins"
                      description="Enregistrez la première séance de ce patient."
                      action={
                        <Button
                          onClick={() => {
                            setEditingRecord(null)
                            setRecordModalOpen(true)
                          }}
                        >
                          Ajouter un dossier dentaire
                        </Button>
                      }
                    />,
                  )
                ) : (
                  <>
                    {/* No « Facturé » badge: the struck-through « Montant payé » is the one place this list says
                        a fiche is billed. It used to be said three times on the same row — a status badge here,
                        the word again in place of the Reste figure, and a third badge in the desktop table's
                        Actions column — which is what pushed the figure staff actually read off the row. */}
                    <CardList
                      className={CARDS_ONLY}
                      ariaLabel="Dossiers dentaires"
                      items={recordsPage.items}
                      getKey={(record) => record.id}
                      title={(record) => record.procedureType}
                      subtitle={(record) => formatDate(record.interventionDate)}
                      fields={(record) => {
                        const invoiced = invoicedDentalRecordIds.has(record.id)
                        const reste = Math.max(0, record.balance ?? record.cost - record.amountPaid)
                        // ⚠️ Tested here, not by letting the component return null: `CardList` drops a field on an
                        // empty *value*, and a React element is never empty — the row would keep an « NOTES »
                        // label over nothing.
                        const hasNotes =
                          (record.notes?.length ?? 0) + (record.importantNotes?.length ?? 0) > 0
                        return [
                          {
                            label: "Dents",
                            value:
                              record.toothNumbers.length > 0 ? (
                                <span className="inline-flex flex-wrap justify-end gap-1">
                                  {record.toothNumbers.map((toothNum) => (
                                    <Badge key={toothNum} variant="secondary" className="text-xs">
                                      {toothNum}
                                    </Badge>
                                  ))}
                                </span>
                              ) : null,
                          },
                          {
                            label: "Montant payé",
                            value: invoiced ? (
                              <span className="text-muted-foreground line-through">
                                {formatDT(record.amountPaid)}
                              </span>
                            ) : (
                              formatDT(record.amountPaid)
                            ),
                          },
                          {
                            label: "Reste",
                            value:
                              reste > 0 ? (
                                // `--warning-ink`: `text-amber-600` had no `dark:` pair and measures ~3.2:1 on
                                // the card — on the figure that says money is still owed.
                                <span className="font-semibold text-warning-ink">{formatDT(reste)}</span>
                              ) : (
                                <span className="text-muted-foreground">{formatDT(0)}</span>
                              ),
                          },
                          hasNotes && {
                            label: "Notes",
                            value: (
                              <DentalRecordNotes
                                record={record}
                                isExpanded={expandedNotes.has(record.id)}
                                onToggle={(expanded) => toggleRecordNotes(record.id, expanded)}
                              />
                            ),
                          },
                        ]
                      }}
                      actions={(record) => (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon" aria-label="Actions du dossier dentaire">
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            {!invoicedDentalRecordIds.has(record.id) && (
                              <DropdownMenuItem onSelect={() => setBillingRecord(record)}>
                                Facturer cette intervention
                              </DropdownMenuItem>
                            )}
                            <DropdownMenuItem
                              onSelect={() => {
                                setEditingRecord(record)
                                setRecordModalOpen(true)
                              }}
                            >
                              Modifier le dossier
                            </DropdownMenuItem>
                            {canDeleteClinicalRecords && (
                              <DropdownMenuItem
                                className="text-destructive focus:text-destructive"
                                onSelect={() => setRecordToDelete(record)}
                              >
                                Supprimer la fiche de soins
                              </DropdownMenuItem>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )}
                    />
                    <Table containerClassName={TABLE_ONLY}>
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
                        {recordsPage.items.map((record) => (
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
                            {/* The figure, on every row. This cell used to print the word « Facturé » instead of
                                a number for a billed fiche — a status in the money column, and the second of
                                three places the same row said it. */}
                            <TableCell>
                              {(() => {
                                const reste = Math.max(0, record.balance ?? (record.cost - record.amountPaid))
                                return reste > 0
                                  // `--warning-ink` — same fix as the card list above.
                                  ? <span className="font-semibold text-warning-ink">{formatDT(reste)}</span>
                                  : <span className="text-muted-foreground">{formatDT(0)}</span>
                              })()}
                            </TableCell>
                            <TableCell className="max-w-xs">
                              {/* The table keeps its « - »; the card drops the field instead (AC-17), which is
                                  why the fallback lives here rather than inside the shared component. */}
                              {(record.notes?.length ?? 0) + (record.importantNotes?.length ?? 0) > 0 ? (
                                <DentalRecordNotes
                                  record={record}
                                  isExpanded={expandedNotes.has(record.id)}
                                  onToggle={(expanded) => toggleRecordNotes(record.id, expanded)}
                                />
                              ) : (
                                <span className="text-muted-foreground text-sm">-</span>
                              )}
                            </TableCell>
                            <TableCell className="text-right">
                              {/*
                                ⚠️ The three actions below carry `coarse:size-11` rather than relying on the
                                inherited `.touch-target`. On a tablet — this app's primary device, and 820 px is
                                already past `md:` so this table is what a tablet gets — they measured 32 px, and
                                three overlays 4 px apart overhang each other so the last one painted steals its
                                neighbours' taps. Deleting a fiche de soins is not a control to reach by accident.
                                The row grows to 44 px on a finger and is untouched on a mouse.
                              */}
                              <div className="flex items-center justify-end gap-1">
                                {/* Billed → the action is simply absent, not replaced by a « Facturé » badge in
                                    the Actions column. A status has no business there, and the struck-through
                                    « Montant payé » on the same row already carries it. */}
                                {!invoicedDentalRecordIds.has(record.id) && (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="h-8 w-8 p-0 coarse:size-11"
                                    onClick={() => setBillingRecord(record)}
                                    title="Facturer cette intervention"
                                  >
                                    <Receipt className="h-4 w-4" />
                                  </Button>
                                )}
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="h-8 w-8 p-0 coarse:size-11"
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
                                    className="h-8 w-8 p-0 coarse:size-11 text-destructive hover:text-destructive"
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
                    {/*
                      One pager for both renderings — only one of them is ever visible (`CARDS_ONLY` is
                      `md:hidden`, `TABLE_ONLY` is `hidden md:block`), which is the shape `stock-table.tsx`
                      already uses.

                      No `onPageSizeChange` on purpose: `PAGE_SIZE_OPTIONS` starts at 10, so offering the
                      selector here would render a `<Select>` whose value (5) matches none of its items — and a
                      patient's fiches are not a list anybody wants 100 of at once. With the selector absent the
                      pager hides itself entirely below six fiches, which is most patients.
                    */}
                    <DataTablePagination
                      page={recordsPage}
                      onPageChange={setRecordsPageRequest}
                      label={["fiche de soins", "fiches de soins"]}
                    />
                  </>
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
                  // Same read as « Dossiers dentaires », so the same failure band — but the copy describes what
                  // THIS tab shows. « Aucun dossier médical » answered a question the tab does not ask.
                  renderSectionEmpty(["dentalRecords"], notesEmptyState)
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
                    ).length === 0 && notesEmptyState}
                  </div>
                )}
              </CardContent>
            </Card>
          </TabsContent>

          {/* Documents Tab — saved medical documents; reopen the editor to edit / reprint. */}
          <TabsContent value="documents">
            <Card>
              <CardHeader>
                {/* Same wrap/flex-1/full-width-action fix as « Dossiers dentaires » above. */}
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0 flex-1 space-y-1.5">
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
                    className="w-full gap-1 sm:w-auto"
                    onClick={() => router.push(`/documents/prescription?patientId=${patientId}`)}
                  >
                    <FileText className="h-4 w-4" />
                    Nouvelle ordonnance
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                {medicalDocuments.length === 0 ? (
                  renderSectionEmpty(
                    ["documents"],
                    <EmptyState
                      icon={FileText}
                      size="compact"
                      chipClassName={zoneChipClass(ZONES.daily)}
                      title="Aucun document enregistré"
                      description="Ordonnances, certificats et bulletins CNAM apparaîtront ici."
                      action={
                        <Button onClick={() => router.push(`/documents/prescription?patientId=${patientId}`)}>
                          Nouvelle ordonnance
                        </Button>
                      }
                    />,
                  )
                ) : (
                  <>
                    {/* Tapping the card opens the document — « Ouvrir » is what the row already did, so the menu
                        exists only for the destructive second action and is omitted when the user cannot delete. */}
                    <CardList
                      className={CARDS_ONLY}
                      ariaLabel="Documents médicaux"
                      items={medicalDocuments}
                      getKey={(doc) => doc.id}
                      title={(doc) => documentTypeLabel(doc.documentType)}
                      onSelect={(doc) => openMedicalDocument(doc)}
                      fields={(doc) => [
                        { label: "Date", value: formatDate(doc.documentDate) },
                        // AC-25: which visit produced this document. `MedicalDocument.AppointmentId` has always
                        // been written (creating a document marks that appointment Completed) and returned by the
                        // DTO — it simply had no UI consumer, so « de quelle séance vient cette ordonnance ? »
                        // had no answer on any screen. A field with no value is omitted, never « — ».
                        {
                          label: "Séance",
                          value: doc.appointmentId ? (
                            <Link
                              href={`/appointments?appointmentId=${doc.appointmentId}`}
                              className="underline-offset-4 hover:underline"
                            >
                              Voir le rendez-vous
                            </Link>
                          ) : null,
                        },
                      ]}
                      // ⚠️ The role gate is on the **delete item**, not on the menu. It used to wrap the whole
                      // `DropdownMenu`, so a secretary lost « Ouvrir le document » along with it — on the phone
                      // tree only; the table below gates just its delete button (§ 0: no capability removed by a
                      // layout decision, and here one tree quietly removed one the other kept).
                      actions={(doc) => (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon" aria-label="Actions du document">
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuItem onSelect={() => openMedicalDocument(doc)}>
                              Ouvrir le document
                            </DropdownMenuItem>
                            {canDeleteClinicalRecords && (
                              <DropdownMenuItem
                                className="text-destructive focus:text-destructive"
                                onSelect={() => setDocumentToDelete(doc)}
                              >
                                Supprimer le document
                              </DropdownMenuItem>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )}
                    />
                    <Table containerClassName={TABLE_ONLY}>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Type</TableHead>
                          <TableHead>Date</TableHead>
                          <TableHead>Séance</TableHead>
                          <TableHead className="text-right">Actions</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {medicalDocuments.map((doc) => (
                          <TableRow key={doc.id}>
                            <TableCell className="font-medium">{documentTypeLabel(doc.documentType)}</TableCell>
                            <TableCell className="text-muted-foreground">{formatDate(doc.documentDate)}</TableCell>
                            {/* AC-25 — the visit that produced it. Written since the document feature shipped,
                                displayed by nothing until now. */}
                            <TableCell className="text-muted-foreground">
                              {doc.appointmentId ? (
                                <Link
                                  href={`/appointments?appointmentId=${doc.appointmentId}`}
                                  className="underline-offset-4 hover:underline"
                                >
                                  Voir le rendez-vous
                                </Link>
                              ) : (
                                "—"
                              )}
                            </TableCell>
                            <TableCell className="text-right">
                              <Button
                                variant="ghost"
                                size="sm"
                                className="gap-1"
                                onClick={() => openMedicalDocument(doc)}
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
                  </>
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
                  renderSectionEmpty(
                    ["appointments"],
                    <EmptyState
                      icon={Calendar}
                      size="compact"
                      chipClassName={zoneChipClass(ZONES.daily)}
                      title="Aucun rendez-vous"
                      description="Planifiez la première visite de ce patient."
                      action={
                        <Button onClick={() => router.push(`/appointments?patientId=${patientId}`)}>
                          Planifier un rendez-vous
                        </Button>
                      }
                    />,
                  )
                ) : (
                  <>
                    {/* The row's per-procedure left border becomes the card's accent — it is the same 4 px stripe
                        in the same place, and it is decoration, not a field whose value is a colour. */}
                    <CardList
                      className={CARDS_ONLY}
                      ariaLabel="Historique des rendez-vous"
                      items={appointmentsNewestFirst}
                      getKey={(appointment) => appointment.id}
                      title={(appointment) => formatDateTime(appointment.appointmentDateTime)}
                      subtitle={(appointment) =>
                        appointmentActsSummary(appointment) || "Rendez-vous général"
                      }
                      accent={(appointment) =>
                        appointmentVisitState(appointment).isCanceled
                          ? undefined
                          : appointment.procedureColorHex || undefined
                      }
                      muted={(appointment) => appointmentVisitState(appointment).isCanceled}
                      status={(appointment) => (
                        <Badge
                          variant="secondary"
                          className={appointmentStatusBadgeClass(appointment.status)}
                        >
                          {appointmentStatusLabel(appointment.status)}
                        </Badge>
                      )}
                      fields={(appointment) => {
                        const { durationMinutes } = appointmentVisitState(appointment)
                        return [
                          { label: "Médecin", value: appointment.doctorName },
                          { label: "Durée", value: durationMinutes > 0 ? `${durationMinutes} min` : null },
                          // Untruncated: the table clipped it behind a hover-only `title=`, which no touch
                          // device can reach, and a visit note is read at the chair.
                          { label: "Notes", value: appointment.notes },
                        ]
                      }}
                      /*
                        ⚠️ `primaryAction`, not `actions`. « Enregistrer la fiche » is ~175px of `whitespace-nowrap`
                        French, and `actions` renders into a `shrink-0` div sharing the card's header row with the
                        wrapping title — so at 320–390px the date was crushed to a few characters per line by a
                        button that refused to give any width back. `primaryAction` is the slot `ui/card-list.tsx`
                        documents for exactly this: the verb gets its own full-width row and a real 44px target,
                        and the identity gets the header back. (`app/waiting-list/page.tsx` is the template.)
                      */
                      primaryAction={(appointment) =>
                        appointmentVisitState(appointment).canRecordVisit ? (
                          <Button
                            variant="outline"
                            className="w-full gap-1.5"
                            onClick={() => openVisitRecord(appointment.id)}
                          >
                            <FileText className="h-4 w-4" />
                            Enregistrer la fiche
                          </Button>
                        ) : null
                      }
                    />
                    <Table containerClassName={TABLE_ONLY}>
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
                        {appointmentsNewestFirst
                          .map((appointment) => {
                            /*
                             * « Enregistrer la fiche » — the same action the post-visit notification offers,
                             * reachable from the history instead of only from the bell (which is dismissible,
                             * and gone once read). The rule for when it applies lives in
                             * `appointmentVisitState`, shared with the card list above.
                             */
                            const { durationMinutes, canRecordVisit, isCanceled } =
                              appointmentVisitState(appointment)

                            // Determine row color based on status and procedure type
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
                                      onClick={() => openVisitRecord(appointment.id)}
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
                  </>
                )}
              </CardContent>
            </Card>
          </TabsContent>

          {/* Files Tab */}
          <TabsContent value="files">
            <Card>
              <CardHeader>
                {/* The third header carrying the same construction — and the only one with TWO actions, so the
                    wrapper takes the full width below `sm:` and its buttons share it. */}
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <CardTitle>Fichiers du patient</CardTitle>
                    <CardDescription>
                      {currentFolderId
                        ? `Fichiers du dossier`
                        : `Tous les fichiers et documents téléversés (${files.length} fichier${files.length !== 1 ? 's' : ''})`}
                    </CardDescription>
                  </div>
                  <div className="flex w-full items-center gap-2 sm:w-auto">
                    {currentFolderId && (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setCurrentFolderId(null)}
                        className="flex-1 gap-2 sm:flex-none"
                      >
                        <ArrowLeft className="h-4 w-4" />
                        Retour
                      </Button>
                    )}
                    <Button
                      onClick={() => router.push(`/patients/${patientId}/files`)}
                      variant="default"
                      className="flex-1 sm:flex-none"
                    >
                      Gérer les fichiers
                    </Button>
                  </div>
                </div>
              </CardHeader>
              <CardContent>
                {files.length === 0 && folders.length === 0 ? (
                  renderSectionEmpty(
                    ["files", "folders"],
                    <EmptyState
                      icon={FolderOpen}
                      size="compact"
                      chipClassName={zoneChipClass(ZONES.daily)}
                      title="Aucun fichier téléversé"
                      description="Radiographies, photos et documents scannés se rangent ici."
                      action={
                        <Button onClick={() => router.push(`/patients/${patientId}/files`)}>
                          Téléverser des fichiers
                        </Button>
                      }
                    />,
                  )
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
                        {/* AC-17's truncate case. The name is the title, so it truncates to one line and the
                            whole value is reachable by tapping the card — which opens the preview. The table's
                            `title=` tooltip did the same job on a desktop and nothing at all on a phone. */}
                        <CardList
                            className={CARDS_ONLY}
                            ariaLabel={currentFolderId ? "Fichiers du dossier" : "Fichiers du patient"}
                            items={filesNewestFirst}
                            getKey={(file) => file.id}
                            title={(file) => file.fileName}
                            onSelect={(file) => preview.open(file)}
                            fields={(file) => [
                              {
                                label: "Type",
                                value: (
                                  <Badge variant="outline" className="text-xs">
                                    {file.fileType || file.contentType.split("/")[1] || "Inconnu"}
                                  </Badge>
                                ),
                              },
                              { label: "Taille", value: formatFileSize(file.fileSize) },
                              { label: "Téléversé le", value: formatDate(file.uploadedAt) },
                            ]}
                            actions={(file) => (
                              <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                  <Button
                                    variant="ghost"
                                    size="icon"
                                    aria-label={`Actions du fichier ${file.fileName}`}
                                  >
                                    <MoreHorizontal className="h-4 w-4" />
                                  </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end">
                                  {isPreviewableFile(file) && (
                                    <DropdownMenuItem onSelect={() => preview.open(file)}>
                                      Aperçu du fichier
                                    </DropdownMenuItem>
                                  )}
                                  <DropdownMenuItem onSelect={() => handleDownloadFile(file)}>
                                    Télécharger le fichier
                                  </DropdownMenuItem>
                                </DropdownMenuContent>
                              </DropdownMenu>
                            )}
                          />
                          <Table containerClassName={TABLE_ONLY}>
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
                              {filesNewestFirst
                                .map((file) => {
                                  const isImage = isImageFile(file)
                                  const isPdf = isPdfFile(file)
                                  const isPreviewable = isPreviewableFile(file)

                                  return (
                                    <TableRow 
                                      key={file.id}
                                      className="cursor-pointer hover:bg-muted/50"
                                      onClick={() => preview.open(file)}
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
                                              onClick={() => preview.open(file)}
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
                  {formatDate(patient.dateOfBirth)} {age !== null ? `(${age} ans)` : "(âge inconnu)"}
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
                <p className="text-xs font-medium text-muted-foreground">E-mail</p>
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
                  /*
                   * ⚠️ Through `renderSectionEmpty`, not a bare sentence — this is the card a dentist checks before
                   * extracting a tooth from someone on Sintrom, and « Aucun antécédent médical » about a failed read
                   * is a confidently wrong clinical answer rather than a missing one. It also fixes the third state
                   * the sentence swallowed: it used to assert « aucun » while the read was still in flight.
                   */
                  renderSectionEmpty(
                    ["medicalHistory"],
                    <p className="text-sm text-muted-foreground">Aucun antécédent médical</p>,
                  )
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
                  // Same rule as the médicaux above — a family history of endocarditis is not « aucun ».
                  renderSectionEmpty(
                    ["familyHistory"],
                    <p className="text-sm text-muted-foreground">Aucun antécédent familial</p>,
                  )
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

      </AppShell>

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

      {/* AC-5.3 — one preview, shared with the files manager. */}
      <FilePreviewDialog
        preview={preview}
        patientId={patientId}
        onDownload={(file) => void handleDownloadFile(file)}
      />
    </ClinicGuard>
  )
}


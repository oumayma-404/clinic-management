"use client"

import { useState, useEffect, useMemo, useRef } from "react"
import { useParams, useRouter } from "next/navigation"
import Link from "next/link"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import type { PagedResponse } from "@/lib/api/paging"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { formatDT, formatDate, formatDateFr, formatDateTime, formatFileSize, quoteFr } from "@/lib/format"
import { CREATABLE_DOCUMENT_TEMPLATES, documentTypeLabel } from "@/lib/documents"
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
import type { PatientDto, AppointmentDto, PatientMedicalHistoryDto, PatientFamilyHistoryDto, DentalRecordDto, PatientFileDto, PatientFolderDto, TreatmentPlanDto, MedicalDocumentDto, PatientBillingSummaryDto, PatientDebtLineDto, VisitToCloseDto, InvoiceDto, InstallmentDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { EditPatientDialog } from "@/components/edit-patient-dialog"
import { ExportButton, useCsvExport } from "@/components/ui/export-button"
import { PatientRecordModal } from "@/components/patient-record-modal"
import { RecordActsSummary } from "@/components/patient/record-acts-summary"
import { Edit } from "lucide-react"
import { Receipt } from "lucide-react"
import { Smile, ClipboardCheck, FolderOpen, CalendarPlus, Plus } from "lucide-react"
import { InvoicesTable } from "@/components/factures/invoices-table"
import { BillDentalRecordDialog } from "@/components/factures/bill-dental-record-dialog"
import { Odontogram } from "@/components/odontogram"
import { PatientNotesStrip } from "@/components/patient/patient-notes-strip"
import { PatientHealthCard } from "@/components/patient/patient-health-card"
import { PatientTabSection } from "@/components/patient/patient-tab-section"
import { dentitionLabel } from "@/lib/dentition"
import { splitHealthList } from "@/lib/health-list"
import { PatientUndocumentedVisits } from "@/components/patient/patient-undocumented-visits"
import { isActiveSmoker, tobaccoSummary } from "@/lib/tobacco"
import { cn } from "@/lib/utils"
import { activateOnKey, FOCUS_CLASSES } from "@/lib/a11y"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { TreatmentPlansTable } from "@/components/treatment-plans/treatment-plans-table"
import { PatientPlansStrip } from "@/components/treatment-plans/patient-plans-strip"
import {
  PatientOutstandingStrip,
  PATIENT_OUTSTANDING_SECTION_ID,
} from "@/components/patient/patient-outstanding-strip"
import { PaymentModal } from "@/components/factures/payment-modal"
import { InstallmentPaymentModal } from "@/components/treatment-plans/installment-payment-modal"
import { TreatmentPlanFormModal, type TreatmentPlanSeedLine } from "@/components/treatment-plans/treatment-plan-form-modal"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { PlanItemOption } from "@/components/patient-record-modal"
import { isPlanLive, schedulablePlanItems } from "@/components/treatment-plans/plan-next-action"
import { planItemHeading } from "@/components/treatment-plans/treatment-plan-labels"
import { teethUnderTreatment } from "@/components/treatment-plans/teeth-under-treatment"
import { invoicesApi } from "@/lib/api/invoices"
import { billingApi } from "@/lib/api/billing"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import {
  appointmentActsSummary, appointmentStatusBadgeClass, appointmentStatusLabel, genderLabel, normalizeStatus,
} from "@/components/appointment-labels"
import { showErrorToast } from "@/lib/errors"
import { downloadBlob } from "@/lib/download"
import { FilePreviewDialog } from "@/components/patients/files/file-preview-dialog"
import {
  DocumentPreviewDialog,
  type DocumentPreviewTarget,
} from "@/components/documents/document-preview-dialog"
import { useFilePreview } from "@/components/patients/files/use-file-preview"
import { isImageFile, isPreviewableFile } from "@/components/patients/files/file-kind"
import { useUploadPolicy } from "@/lib/hooks/use-upload-policy"
import { useVault } from "@/lib/hooks/use-vault"

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

/**
 * The address as one line, or `null` when nothing was given.
 *
 * ⚠️ **Consecutive identical parts collapse**, and that is not cosmetic tidying: in Tunisia the ville and the
 * gouvernorat are routinely the same word — Ariana, Sfax, Tunis, Nabeul, Bizerte — so the naive join printed
 * « 12 rue de la République, Ariana, Ariana, 1002 » and read as a rendering fault rather than as an address.
 * Only *adjacent* duplicates go, so a street genuinely repeating its town's name is left alone.
 *
 * ⚠️ Returns `null` rather than « Non renseigné »: since every part became optional this is the one formatter
 * that has to distinguish « no address » from a partial one, and the caller decides how an absence is worded.
 */
/**
 * A health list — allergies, maladies, médicaments — inside a `RecordField`.
 *
 * <p>⚠️ A real `<ul>` past one item and a bare line at exactly one: a single-item list still announces « liste,
 * 1 élément » to a screen reader and pays a marker's indent for nothing. Same rule, same shape, as
 * `PatientHealthCard` at the top of this page — these are the same three values twice on one screen, so they
 * must not be drawn two ways.</p>
 *
 * <p>⚠️ `list-none` plus an explicit « • » rather than `list-disc list-inside`: the built-in marker sits outside
 * the text box, so a wrapped second line hangs under the bullet instead of aligning with the first character.
 * `ps-3 -indent-3` is that hanging indent.</p>
 */
/**
 * What an antécédents column says when its read failed.
 *
 * ⚠️ It exists because this card **omits an empty column**, which is right for « ce patient n'en a aucun » and
 * wrong for « je n'ai pas pu lire » — the two arrive identically as `[]`. Omitting on a failure would let a
 * network blip render as a confident « aucun antécédent » on the block a dentist checks before injecting, which
 * is the failure `frontend-web.md` § 13 names outright. A short sentence in the column is the honest version;
 * the record card at the foot of the page offers the « Réessayer ».
 */
const HISTORY_UNREADABLE = "Non chargés — voir plus bas"

function HealthItems({ items }: { items: string[] }) {
  if (items.length === 1) return <>{items[0]}</>

  return (
    <ul className="list-none space-y-0.5">
      {items.map((item, index) => (
        <li key={index} className="-indent-3 ps-3">
          <span aria-hidden="true" className="me-1.5 text-muted-foreground">
            •
          </span>
          {item}
        </li>
      ))}
    </ul>
  )
}

const formatAddress = (address: PatientDto["address"]): string | null => {
  if (!address) return null

  const parts = [address.street, address.city, address.state, address.zipCode]
    .map((part) => part?.trim())
    .filter((part): part is string => Boolean(part))

  const collapsed = parts.filter(
    (part, i) => i === 0 || part.toLowerCase() !== parts[i - 1].toLowerCase(),
  )

  return collapsed.length > 0 ? collapsed.join(", ") : null
}

/**
 * One label-over-value row of the two record cards at the foot of the page.
 *
 * <p>It exists because that markup was written twelve times, each pair wrapped in its own `<div>` and joined by
 * a `<Separator />` — twelve horizontal rules through what is a definition list, which is most of why the two
 * cards read as heavy. Whitespace separates them now; the labels are the structure.</p>
 *
 * ⚠️ **An absent value is muted, a real one is not**, and that is the substantive half of this component. Both
 * used to render `text-foreground`, so « Non renseigné » had exactly the weight of a phone number — three of
 * them down one card, each looking like data. The distinction is kept rather than the field being dropped
 * (`frontend-web.md` § 6's omit-don't-dash rule is about a *card list*, where a row is a summary): this is the
 * patient's record, and « nobody has filled this in » is a fact the person reading it can act on.
 *
 * @param wide spans both columns — for a value that is a sentence, a list or a row of badges.
 */
function RecordField({
  label,
  value,
  children,
  wide,
  hint,
  omitWhenEmpty,
}: {
  label: string
  /** Rendered muted as « Non renseigné » when blank. Ignored when `children` is supplied. */
  value?: string | null
  children?: React.ReactNode
  wide?: boolean
  /** A consequence of the value, under it — e.g. that no reminder can reach this patient. */
  hint?: React.ReactNode
  /**
   * Render nothing at all when there is no value, instead of « Non renseigné » (§ 6: a field with no value is
   * omitted).
   *
   * ⚠️ **Opt-in, and it must stay opt-in — the default is load-bearing on the medical card.** « Informations
   * médicales » is the card a dentist reads before extracting a tooth from somebody on Sintrom, and there an
   * absent line and « aucun antécédent » are opposite facts: silence would read as « rien à signaler » when
   * what is true is « personne n'a posé la question ». That is the exact distinction `patient-health-card.tsx`
   * was rebuilt five times to protect. So this is passed only where the blank carries no clinical claim — a
   * missing e-mail, a missing « Adressé par » — and never on a health field.
   */
  omitWhenEmpty?: boolean
}) {
  const filled = children ?? (value?.trim() ? value : null)
  if (!filled && omitWhenEmpty) return null

  return (
    <div className={cn("min-w-0", wide && "sm:col-span-2")}>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "mt-0.5 text-sm [overflow-wrap:anywhere]",
          filled ? "text-foreground" : "text-muted-foreground",
        )}
      >
        {filled ?? "Non renseigné"}
      </dd>
      {hint}
    </div>
  )
}

/*
 * Every template but the one that gets its own button. « Ordonnance » stays a single tap because it is far and
 * away the most frequent, and burying it in a menu to make the six symmetrical would cost a click on the
 * common path to save one on the rare path.
 */
const OTHER_DOCUMENT_TEMPLATES = CREATABLE_DOCUMENT_TEMPLATES.filter(
  (template) => template.type !== "prescription",
)

/**
 * The placeholder for a section whose request has not answered yet.
 *
 * Load-bearing since the page began painting its identity before its details: `[]` used to be reachable only
 * after every request had answered, so « Aucun acte dentaire » was always true. It is now also the state
 * *before* the request answers — and a page that tells a dentist their patient has no records, no
 * appointments and no files, a beat before listing all three, is worse than one that took longer to appear.
 */
/**
 * What lets a badge in a card’s « Montant payé » cell stay inside the card.
 *
 * <p>⚠️ <b>Both badges below were clipped at 320 px</b>, measured on the card tree: `CardList` gives its `<dd>`
 * `min-w-0 break-words`, but `Badge`’s base is `whitespace-nowrap shrink-0`, so with the `<dt>` label taking its
 * share the value cell is ~86 px and a ~132 px pill simply ran past the card — « traitement 2026-0027 »
 * rendered as « traitement 202 », i.e. the devis number the badge exists to state was the part cut off.
 * `break-words` cannot rescue it, because the pill refuses to wrap in the first place.</p>
 *
 * <p>So the label breaks (§ 10.1: let the one child break its label, never truncate — it is the control’s name),
 * `shrink` is <b>spelled out</b> because `whitespace-normal` alone leaves `shrink-0` standing (different
 * tailwind-merge groups). The `justify-end` that keeps a wrapped line reading from the right edge belongs to
 * the <i>wrapper</i> of each of the two, not here.</p>
 */
const MONEY_CELL_BADGE = "min-w-0 shrink whitespace-normal text-2xs font-normal"

/**
 * The « Montant payé » of a fiche whose money has moved onto an invoice.
 *
 * ⚠️ **This used to be a strikethrough, and that is the defect it exists to fix.** `line-through` means
 * « annulé, ne compte pas » everywhere else money is shown in this app — nine sites, every one of them keyed
 * on `isVoided` or `isCancelled`, and la caisse documents it in those words: « annulé … barré, ne compte pas
 * dans le solde ». Striking a *successfully billed* amount with the same mark makes a user who has learned
 * either one actively misread the other, on money. The card form carried no explanation at all; the table's
 * lived in a `title`, which a finger cannot reach (§ 9.2).
 *
 * <p>So the amount is muted — it is no longer the authority — and the reason is stated in words, with the
 * invoice's own number when the map has it. « en cours » is deliberately not printed here: the badge is a
 * statement about where the money lives, and a number that has not arrived yet is simply not part of it.</p>
 */
function BilledAmount({ amount, invoiceNumber }: { amount: number; invoiceNumber?: string }) {
  return (
    <span className="inline-flex min-w-0 flex-wrap items-baseline justify-end gap-1.5">
      <span className="text-muted-foreground">{formatDT(amount)}</span>
      <Badge variant="outline" className={MONEY_CELL_BADGE}>
        {/* The number in its own `whitespace-nowrap` span: a hyphen is an ordinary break opportunity, so a
            wrapping pill split « 2026-0016 » across two lines — the defect `card-list.tsx` already records
            for a date (« 26/08 · /2026 reads as two dates »). The label may break; the number may not. */}
        {invoiceNumber ? (
          <>facturé n° <span className="whitespace-nowrap">{invoiceNumber}</span></>
        ) : (
          "facturé"
        )}
      </Badge>
    </span>
  )
}

/**
 * The « Montant payé » of a séance whose money went onto its TREATMENT rather than onto a note d’honoraires.
 *
 * <p>⚠️ <b>Without it this column read « 0,000 DT » on every séance of a multi-séance act.</b> Such an act is
 * priced once, on the treatment, so its fiche is 0 by rule and both money columns are derived from that 0 — a
 * patient’s history showed three visits that had taken 1 000 DT between them as three empty rows, while the
 * treatment page reported the money correctly. The figure and the plan it went to are read back from the
 * échéancier ledger, so a voided payment corrects this row with it.</p>
 *
 * <p>Shaped after {@link BilledAmount}: the amount, then a badge saying <i>where the money lives</i>. Not
 * muted, unlike that one — this figure IS the authority for the séance, it simply sits on another document.</p>
 *
 * <p>⚠️ <b>The badge is the way to that devis.</b> It names the document the money is on and was inert, so the
 * one row that knows which treatment took the payment sent the reader to « Traitements » to find it again — and a
 * followed treatment has no number to search by. Same destination as the séance-identity line inside
 * {@link RecordActsSummary} on the row above: both mentions are links, because one of two identical mentions
 * being clickable teaches nobody which one to press. Shaped after the « Devis » badge on
 * <c>invoices-table</c>, the product’s existing badge-as-link.</p>
 */
function CollectedOnTreatment({
  amount,
  planNumber,
  planId,
}: {
  amount: number
  planNumber?: string | null
  /** Absent only for a caller with no plan in hand — the badge then stays the inert statement it was. */
  planId?: string | null
}) {
  // See `BilledAmount`: the label may break, the devis number may not.
  const badgeLabel = planNumber ? (
    <>traitement <span className="whitespace-nowrap">{planNumber}</span></>
  ) : (
    "sur le traitement"
  )
  return (
    <span className="inline-flex min-w-0 flex-wrap items-baseline justify-end gap-1.5">
      {/* ⚠️ A séance that collected nothing prints « — », not « 0,000 DT ». Zero here is the ORDINARY case on a
          multi-séance act (the patient pays on some visits and not others) and « 0,000 DT » beside a patient
          owing 1 500 on the devis reads as « rien encaissé, rien dû » — the same misreading the « Reste »
          column already avoids. The badge still says where the money lives. */}
      <span className={amount > 0 ? undefined : "text-muted-foreground"}>
        {amount > 0 ? formatDT(amount) : "—"}
      </span>
      {planId ? (
        <Link
          href={`/treatment-plans/${planId}`}
          // The rows are not clickable today; kept so a row handler added later cannot win the race and
          // leave the badge looking right while doing nothing (`PatientNameLink`’s scar).
          onClick={(e) => e.stopPropagation()}
          aria-label={`Ouvrir le traitement${planNumber ? ` ${planNumber}` : ""}`}
          title={`Ouvrir le traitement${planNumber ? ` ${planNumber}` : ""}`}
          // Grows its own box rather than taking `.touch-target`: it sits inside a dense money cell whose
          // 44 px overlay would overhang the row above it.
          className="inline-flex items-center rounded-md coarse:min-h-11"
        >
          {/* ⚠️ The affordance at rest is a GLYPH, not an underline — `PatientNameLink`’s rule is right about
              needing one (a pointer-less device never hovers) and wrong about the shape here: measured at
              1440 px, an underline inside a 22 px pill lands ~2 px from its bottom border and reads as a
              cramped double line, on « traitement 2026-0027 » running into the border’s curve. `Badge` is
              built for this instead — `gap-1` and `[&>svg]:size-3` are in its base — and a chevron survives
              greyscale and 11 px, which is what the card tree needs: a séance with no tooth recorded renders
              no « Actes » field, so this badge is then the row’s only mention of the treatment.
              ⚠️ Deliberately not `ArrowUpRight`: that glyph already means « sortie » in this product’s money
              tables (`caisse-ledger-table`), and this is a money cell. `hover:bg-accent` is written out
              because the variant’s own is `[a&]:` and the anchor here is the wrapper, not the badge. */}
          <Badge variant="outline" className={cn(MONEY_CELL_BADGE, "transition-colors hover:bg-accent")}>
            {badgeLabel}
            <ChevronRight aria-hidden="true" className="shrink-0" />
          </Badge>
        </Link>
      ) : (
        <Badge variant="outline" className={MONEY_CELL_BADGE}>
          {badgeLabel}
        </Badge>
      )}
    </span>
  )
}

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
  | "billing"

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
/**
 * « 12 fichiers dans ce dossier », « 1 dossier », « 0 fichier à la racine ».
 *
 * <p>French agreement is on the noun **and** nowhere else here, so the singular/plural pair is passed rather than
 * derived: « fichier / fichiers » is regular and « bilan / bilans » is too, but the caller owns the word. The
 * optional `scope` is the half that matters — a bare count on this page had claimed to cover « tous les
 * fichiers » while counting one folder's worth.</p>
 */
function countLabel(n: number, one: string, many: string, scope?: string): string {
  // ⚠️ **Zero takes the SINGULAR in French** — « 0 fichier », not « 0 fichiers ». The `=== 1` test that reads
  // naturally to an English eye is wrong here, and it is wrong in the same way elsewhere in this file (the
  // folder card's own `fileCount === 1` says « 0 fichiers »); this helper is the version to copy.
  return [`${n} ${n < 2 ? one : many}`, scope].filter(Boolean).join(" ")
}

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
  "treatment-plans",
  "appointments",
  "documents",
  "files",
  "factures",
]

/**
 * `?tab=` values that no longer name a panel, and where they land instead.
 *
 * ⚠️ **« Notes » was retired as a tab, and this is what keeps every old link working.** It was a strict subset
 * of two surfaces that are both on this page already: `PatientNotesStrip` — directly above the strip, showing
 * the patient's own alerts *and* every séance note, each dated and named by its act, with « Modifier » on both
 * halves — and the « Notes » field on every fiche row of « Dossiers médicaux », which carries
 * `DentalRecordNotes` and « Ouvrir la fiche ». The tab's one distinct property was rendering the same notes
 * unbounded rather than in a 120 px scroller: a scroll ceiling, not a capability (§ 0). A seventh destination
 * that shows nothing the sixth does not is what makes a screen feel complicated.
 */
const RETIRED_PATIENT_TABS: Record<string, string> = {
  notes: "medical-records",
}

/**
 * Rows per page in « Actes dentaires ».
 *
 * Five, not the app's `DEFAULT_PAGE_SIZE` of 25: this list sits inside a tab under the patient's identity, its
 * rows are tall (teeth badges, expandable notes) and a fiche is *read* rather than scanned — a long-standing
 * patient's forty séances pushed everything below them off the screen.
 */
const DENTAL_RECORDS_PAGE_SIZE = 5

/**
 * The page size for the three tabs that had no bound at all — Rendez-vous, Fichiers and Documents.
 *
 * ⚠️ **Ten, not the fiches' five, and the difference is the row.** A fiche row is three lines and carries
 * actions, so five fills a screen; an appointment, a file and a document are one line each and are read as a
 * history, so five would turn one scroll into four clicks. Ten is also `PAGE_SIZE_OPTIONS`' first step, which
 * is what lets these pagers offer the size selector the fiches' deliberately cannot.
 */
const TAB_PAGE_SIZE = 10

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

  /**
   * The fiche a duplicate merge has just deleted, set before the navigation to the surviving patient.
   *
   * ⚠️ Without it the merge reports its own success as a failure: the command broadcasts the `patients` realtime
   * key, this page's subscription bumps `refreshKey`, the effect below re-reads the id that was just deleted, and
   * the 404 lands as « Patient introuvable. » on top of the fiche the user has already been sent to. A ref rather
   * than state, because the request already in flight has to see it from inside its own `catch`.
   *
   * ⚠️ It holds the **id**, not a boolean. Next reuses this component across `/patients/[id]` → `/patients/[other]`,
   * so a flag left standing would refuse to load the survivor — the very patient the merge sends you to.
   */
  const [error, setError] = useState<string | null>(null)
  const [editDialogOpen, setEditDialogOpen] = useState(false)
  /**
   * Which block of the edit dialog to unfold and scroll to when it opens, or null for « as it comes ».
   *
   * ⚠️ Set together with `setEditDialogOpen(true)` by the panels' own « Modifier » buttons, so a reader who
   * spots a wrong allergy lands on the allergies rather than at the top of a form and hunting. The plain
   * « Modifier » in the header leaves it null and opens the form as normal.
   */
  const [editSection, setEditSection] = useState<"essentiel" | "medical" | null>(null)
  const [recordModalOpen, setRecordModalOpen] = useState(false)
  /**
   * The document being READ, if any. A clinical surface opens a document to read it in place; the Documents
   * module still opens the editor. See `openPrescription`.
   */
  const [previewTarget, setPreviewTarget] = useState<DocumentPreviewTarget | null>(null)
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
  const openMedicalDocument = (doc: MedicalDocumentDto) => {
    if (doc.documentType === "honoraires") {
      router.push("/factures")
      return
    }
    // ⚠️ A « demande d'examens » has no form in the standalone editor — the fiche de soins is its only writer
    // (`DOCUMENT_TEMPLATES`' `creatable: false`) — so routing there would open a paper the editor cannot
    // render. It opens the read dialog instead, which prints it, sends it, and points at the fiche to change it.
    if (doc.documentType === "examens") {
      setPreviewTarget({ mode: "saved", documentId: doc.id })
      return
    }
    router.push(`/documents/${doc.documentType}?id=${doc.id}`)
  }

  /**
   * Open a séance's ordonnance — or its demande d'examens — from its history row, IN PLACE.
   *
   * ⚠️ **It used to `router.push` to the document editor**, which meant leaving the patient file to look at one
   * sheet and then finding your way back. The rule now is: a CLINICAL surface opens a document to READ (this
   * dialog: aperçu, Imprimer, Télécharger, Envoyer), and the Documents module opens it to EDIT — with
   * « Modifier » inside the dialog as the door between them. Two different questions, two different doors.
   *
   * ⚠️ A fiche owns up to **two** documents, so this takes the id rather than assuming the prescription: a
   * médicament and an examen may not share a sheet. See `DocumentTypes.Examens`.
   */
  const openPrescription = (documentId: string) =>
    setPreviewTarget({ mode: "saved", documentId })

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
   * « Actes dentaires » pages **in the browser**, deliberately.
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

  /**
   * « Rendez-vous » pages in the browser too, and it is the same `PagedResult.FromSource` case for the same
   * reason: `appointmentsApi.list` takes no paging parameters, and three other things on this page read the
   * whole list anyway (« À compléter », the visit-state derivations, the record modal's booked acts).
   *
   * ⚠️ **It was the ONE list tab on this page with no bound at all**, and the measurement is why it now has
   * one: 23 visits rendered 1 556 px at 1440 px and **3 518 px — 4,2 écrans — at 820 px**. Both trees are in
   * the DOM at once (the hinge is CSS-only), so that is ~46 row subtrees for a three-year patient and ~100 for
   * a five-year one. `ui/card-list.tsx` states the invariant this broke in as many words: « the doubled DOM is
   * bounded because every list is paged ».
   *
   * ⚠️ **Ten, not the fiches' five.** A fiche row is three lines and carries actions; an appointment row is one
   * line and is read as a history, so a page of five would turn one scroll into four clicks. Ten also matches
   * `PAGE_SIZE_OPTIONS`' first step, which is what lets this pager offer the size selector the fiches' cannot.
   */
  const [appointmentsPageRequest, setAppointmentsPageRequest] = useState(1)
  const [appointmentsPageSize, setAppointmentsPageSize] = useState(10)

  /**
   * The « Exporter le dossier » behaviour, shared by the desktop button and the phone menu item.
   *
   * ⚠️ **A hook, so it lives above this component's early returns** — the same rule the appointments pager
   * below was moved for. It keys on `patientId` rather than `patient.id` because it must run on the render
   * where the patient has not arrived yet.
   */
  const dossierExport = useCsvExport({
    path: `/patients/${patientId}/dossier`,
    label: "dossier",
    stepUpAction: "export-patient-dossier",
    stepUpPurpose: "Exporter le dossier complet de ce patient, pour le lui remettre",
  })

  /**
   * The slice the « Rendez-vous » tab renders.
   *
   * ⚠️ **It lives HERE, above this component's early returns, and that is not tidiness.** Placed beside the
   * tab's own JSX it sat after the `loading` and `!patient` branches, so on the render where the patient
   * arrived React counted one more hook than the render before it and threw « Rendered more hooks than during
   * the previous render » — a blank page. `tsc` cannot see it and the production build compiles it happily;
   * the browser rejects it on the first paint, which is why the eye pass is the half that matters.
   *
   * The sort is inside the memo rather than read from a const below for the same reason.
   */
  const appointmentsPage = useMemo<PagedResponse<AppointmentDto>>(() => {
    const newestFirst = [...appointments].sort(
      (a, b) => new Date(b.appointmentDateTime).getTime() - new Date(a.appointmentDateTime).getTime(),
    )
    const totalCount = newestFirst.length
    const totalPages = Math.max(1, Math.ceil(totalCount / appointmentsPageSize))
    // Clamped at render, never corrected by an effect — the fiches pager's own reasoning: a page that has just
    // stopped existing must not be painted before being fixed.
    const page = Math.min(Math.max(1, appointmentsPageRequest), totalPages)
    const start = (page - 1) * appointmentsPageSize
    return {
      items: newestFirst.slice(start, start + appointmentsPageSize),
      page,
      pageSize: appointmentsPageSize,
      totalCount,
      totalPages,
      hasPreviousPage: page > 1,
      hasNextPage: page < totalPages,
    }
  }, [appointments, appointmentsPageRequest, appointmentsPageSize])

  // A different patient is a different history. Navigating between two patients does **not** remount this page
  // (only `params.id` changes), so without this the header search would open the next file on page 3.
  //
  // ⚠️ **`currentFolderId` is reset here for the same reason, and its absence was worse than a wrong page.**
  // The Fichiers tab keeps the open folder in state and its read is keyed on `[patientId, currentFolderId]`, so
  // arriving at patient B while inside a folder of patient A asked the server for A's folder under B — which
  // `GetPatientFilesQuery` refuses with « Dossier introuvable. ». The tab then rendered « Fichiers du dossier »
  // and a « Retour » button for a folder that is not this patient's.
  useEffect(() => {
    setRecordsPageRequest(1)
    setCurrentFolderId(null)
    setFilesPageRequest(1)
    setDocumentsPageRequest(1)
    setAppointmentsPageRequest(1)
  }, [patientId])
  // Dental records already tied to a non-cancelled invoice (guards against double-invoicing).
  const [invoicedDentalRecordIds, setInvoicedDentalRecordIds] = useState<Set<string>>(new Set())
  // The note d'honoraires that bills each of those records, so the delete confirmation can NAME it
  // (AC-P2.17) instead of vaguely warning that the fiche is billed. Same pass as the set above.
  const [invoicingNumberByRecordId, setInvoicingNumberByRecordId] = useState<Map<string, string>>(new Map())

  /*
   * « Solde dû » — the ONE figure, and the count is the whole point.
   *
   * ⚠️ **A « Solde patient » card stood on this page and was deliberately removed**, because it put six money
   * figures across the top and two of them measured different things: `TotalOutstanding` is what is still owed,
   * `PatientOutOfPocket` is lifetime gross billed minus CNAM's share, so the same patient legitimately read
   * « 90,000 DT » and « 1 770,000 DT » side by side with nothing saying why. That removal was right and this
   * does not undo it — only `TotalOutstanding` is read, which the DTO itself calls « the single Solde patient ».
   *
   * What changed since is that the removal's stated fallback — « Outstanding debt is still one click away in
   * « Créances », the patient's Factures tab, and the plan card » — lost a leg: `/creances` has been retired.
   * And the question is asked with the patient standing at the desk, which is exactly why
   * `GET /patients/{id}/billing-summary` sits on `AnyClinicRole` rather than behind the clinic-wide money gate.
   */
  const [billingSummary, setBillingSummary] = useState<PatientBillingSummaryDto | null>(null)
  /*
   * « Travail non facturé » — this patient's séances whose remaining question is « combien a-t-il payé ? ».
   *
   * ⚠️ Read from `GET /appointments/to-close?patientId=…&days=` rather than derived here, and that is the whole
   * design: `VisitClosureRules` already knows that a contrôle gratuit, a séance carried by a devis, and a visit
   * marked « rien à facturer » or « retirée » are not gaps. A per-patient rule written beside the band would be
   * a second copy of all four terms, and it would nag about every one of them. No `days` — one patient's list
   * is not a clinic's first worklist, and a séance nobody billed is not less open for being three months old.
   */
  const [unbilledVisits, setUnbilledVisits] = useState<VisitToCloseDto[]>([])
  /*
   * The document a payment dialog is open on. ⚠️ Both are **re-read on open** rather than taken from this
   * page's snapshot: the band's figures are minutes old at best, and a colleague settling the note in the
   * meantime would otherwise prefill the dialog with a stale « reste » and produce a refusal for it. The page
   * already live-refreshes on `invoices`/`treatmentplans`, so this only covers the gap between the last
   * broadcast and the press.
   */
  const [paymentInvoice, setPaymentInvoice] = useState<InvoiceDto | null>(null)
  const [paymentInstallment, setPaymentInstallment] =
    useState<{ planId: string; installment: InstallmentDto } | null>(null)
  const [openingDocumentId, setOpeningDocumentId] = useState<string | null>(null)
  const [unarchiving, setUnarchiving] = useState(false)
  // The dental record being invoiced (drives the pre-filled invoice modal); null = closed.
  const [billingRecord, setBillingRecord] = useState<DentalRecordDto | null>(null)
  // Pending destructive confirmations (AC-P2.16 / AC-P2.20). null = dialog closed.
  const [recordToDelete, setRecordToDelete] = useState<DentalRecordDto | null>(null)

  /*
   * ⚠️ **All six templates, not one.** This panel offered « Nouvelle ordonnance » alone, so « Arrêt de
   * travail » and « Bulletin de soins CNAM » — the two most frequent documents in a Tunisian practice — meant
   * leaving the patient, crossing to the sidebar, picking the template and then finding the patient again. The
   * deep link was never the missing piece: `?patientId=` already worked for the one button that used it.
   *
   * Declared once and rendered in both the header and the empty state, because those two were already a pair
   * of copies of the same button and are exactly where a seventh template would be forgotten.
   */
  const openDocumentTemplate = (type: string) =>
    router.push(`/documents/${type}?patientId=${patientId}`)

  const documentTemplateActions = (
    /* Unprefixed `flex` so it really replaces the display below the hinge, and `w-full sm:w-auto` on each
       child rather than `flex-1` — a `0%` basis would beat `w-full` and collapse both buttons. */
    <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row">
      <Button size="sm" className="w-full gap-1 sm:w-auto" onClick={() => openDocumentTemplate("prescription")}>
        <FileText aria-hidden="true" className="h-4 w-4" />
        Nouvelle ordonnance
      </Button>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button size="sm" variant="outline" className="w-full gap-1 sm:w-auto">
            Autre document
            <ChevronDown aria-hidden="true" className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        {/* Clamped to the viewport: a fixed `w-72` is wider than a 320 px screen and would have no gutter. */}
        <DropdownMenuContent align="end" className="w-[min(18rem,calc(100vw-2rem))]">
          {OTHER_DOCUMENT_TEMPLATES.map((template) => (
            <DropdownMenuItem key={template.type} onClick={() => openDocumentTemplate(template.type)}>
              <template.icon aria-hidden="true" className="size-4" />
              {template.title}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )

  const [documentToDelete, setDocumentToDelete] = useState<MedicalDocumentDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  // When in a folder, every loaded file belongs to it; at root, only the files in no folder.
  const currentFiles = currentFolderId ? files : files.filter((f) => !f.folderId)
  /**
   * The folder currently open, or null at the root.
   *
   * ⚠️ It exists because three surfaces said « Fichiers du dossier » about a folder they could name: the header,
   * the list heading and its `ariaLabel`. `folders` has carried `name` all along — a screen reader announcing
   * « Fichiers du dossier » on every folder in turn is the icon-with-no-label defect in prose form.
   */
  const currentFolder = currentFolderId ? folders.find((f) => f.id === currentFolderId) ?? null : null

  /*
   * Newest first, sorted ONCE. Sorting inline in the JSX sorts the state array **in place**, and two trees
   * render this data — so a copy is also what keeps the card list and the table in the same order.
   */
  const filesNewestFirst = [...currentFiles].sort(
    (a, b) => new Date(b.uploadedAt).getTime() - new Date(a.uploadedAt).getTime(),
  )

  /**
   * The page of files this tab renders — and the same slice the preview's arrows walk.
   *
   * ⚠️ **The read is the unpaged one on purpose** (`getFiles`, not the manager's `getFilesPaged`): the tab has
   * no search and no sort of its own, and the preview needs a sequence. What it did not have was a *bound* —
   * 60 root files rendered 60 rows and 60 cards, both trees in the DOM, with nothing to turn.
   *
   * ⚠️ **The preview is fed `items`, not the whole list, so the two agree.** Arrowing past the end of a page
   * would otherwise open a file the list underneath is not showing.
   */
  const [filesPageRequest, setFilesPageRequest] = useState(1)
  const filesPage = useMemo<PagedResponse<PatientFileDto>>(() => {
    const totalCount = filesNewestFirst.length
    const totalPages = Math.max(1, Math.ceil(totalCount / TAB_PAGE_SIZE))
    const page = Math.min(Math.max(1, filesPageRequest), totalPages)
    const start = (page - 1) * TAB_PAGE_SIZE
    return {
      items: filesNewestFirst.slice(start, start + TAB_PAGE_SIZE),
      page,
      pageSize: TAB_PAGE_SIZE,
      totalCount,
      totalPages,
      hasPreviousPage: page > 1,
      hasNextPage: page < totalPages,
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentFiles, filesPageRequest])

  // AC-5.3 — the preview lives in one place now; this page held a byte-identical second copy of the hook and
  // the dialog, only the PDF frame having ever been extracted. The sequence is what the viewer's arrows walk:
  // this tab loads every file at once, so there is no page to turn.
  // ⚠️ The policy and the coffre are passed here for the same reason they are in the files manager: without the
  // first, a HEIC's « aperçu » is decided by a MIME-type guess rather than by the served catalog, and without
  // the second a coffre original reads as « pas sur ce poste » on the very machine holding it. This tab used to
  // pass neither, so the two surfaces onto the same drawer disagreed about what could be opened.
  const filesPolicy = useUploadPolicy("patient-file")
  const { vault } = useVault()
  const preview = useFilePreview(patientId, filesPolicy, { files: filesPage.items }, vault)
  const [refreshKey, setRefreshKey] = useState(0)
  /** Band C — the identity read answered 404 (the patient really is gone), as opposed to failing. */
  const [identityMissing, setIdentityMissing] = useState(false)
  const [treatmentPlans, setTreatmentPlans] = useState<TreatmentPlanDto[]>([])
  /**
   * The two panels whose body is a shared table (`TreatmentPlansTable`, `InvoicesTable`) put their create
   * action in `PatientTabSection`'s header slot, like every other panel — so the button lives here and the
   * table is told about the press through a counter (`suppliers-table.tsx`'s pattern).
   *
   * ⚠️ Both tables also render `hideToolbar`, which drops the search box **on this page only**: a patient has
   * a handful of devis and a handful of notes, every neighbouring tab lists its rows with no search at all,
   * and the row it sat on was a second bank of controls directly under a header that already has one.
   * `/treatment-plans` and `/factures` pass neither prop and are untouched — there the search is clinic-wide
   * and server-side, which is the case it exists for.
   */
  const [newPlanRequest, setNewPlanRequest] = useState(0)
  const [newInvoiceRequest, setNewInvoiceRequest] = useState(0)
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

  /** The page of documents the Documents tab renders — it had no bound either. */
  const [documentsPageRequest, setDocumentsPageRequest] = useState(1)
  const documentsPage = useMemo<PagedResponse<MedicalDocumentDto>>(() => {
    const totalCount = medicalDocuments.length
    const totalPages = Math.max(1, Math.ceil(totalCount / TAB_PAGE_SIZE))
    const page = Math.min(Math.max(1, documentsPageRequest), totalPages)
    const start = (page - 1) * TAB_PAGE_SIZE
    return {
      items: medicalDocuments.slice(start, start + TAB_PAGE_SIZE),
      page,
      pageSize: TAB_PAGE_SIZE,
      totalCount,
      totalPages,
      hasPreviousPage: page > 1,
      hasNextPage: page < totalPages,
    }
  }, [medicalDocuments, documentsPageRequest])

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
    // A fiche merged away no longer exists; re-reading it can only 404, and the page is already leaving.
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
        /*
         * The same contract for a read that answers with one object rather than a list. `null` is the "we do not
         * know" value and it is NOT rendered as zero — see the header, where a failed balance read shows nothing
         * at all. Telling a dentist a patient owes nothing is the one wrong answer here; saying nothing is
         * merely unhelpful, and the Factures tab is still a click away.
         */
        const attemptOne = <T,>(section: PatientSection, request: Promise<T>): Promise<T | null> =>
          request.catch(() => {
            failed.add(section)
            return null
          })

        const [
          appointmentsData,
          medicalHistory,
          familyHistory,
          dentalRecordsData,
          foldersData,
          invoicesData,
          plansData,
          documentsData,
          billingData,
          unbilledData,
        ] = await Promise.all([
          attempt("appointments", appointmentsApi.list({ patientId })),
          attempt("medicalHistory", patientMedicalHistoryApi.list(patientId)),
          attempt("familyHistory", patientFamilyHistoryApi.list(patientId)),
          attempt("dentalRecords", dentalRecordsApi.list(patientId)),
          /*
            ⚠️ **`getFiles` is deliberately NOT here — it was fetched twice on every page open.**
            The dedicated folder effect below runs on mount with `currentFolderId === null`, which is the
            identical request; both landed on `setFiles`, so the second answer simply overwrote the first. That
            effect owns the read (it is the one that has to re-run when a folder is opened) and it records its
            own `"files"` failure, so nothing about the three-state rendering changes.
          */
          attempt("folders", patientFilesApi.getFolders(patientId)),
          attempt("invoices", invoicesApi.list({ patientId })),
          attempt("plans", treatmentPlansApi.list({ patientId })),
          attempt("documents", medicalDocumentsApi.list(patientId)),
          attemptOne("billing", billingApi.getPatientSummary(patientId)),
          // `attemptOne` and not `attempt`: this read answers with an object, and its own `null` is what makes
          // « we could not ask » distinguishable from « nothing is unbilled » one line below.
          attemptOne("billing", appointmentsApi.visitsToClose({ patientId })),
        ])
        if (cancelled) return
        setFailedSections(failed)
        setTreatmentPlans(plansData)
        setMedicalDocuments(documentsData)
        setAppointments(appointmentsData)
        setMedicalHistoryEntries(medicalHistory)
        setFamilyHistoryEntries(familyHistory)
        setDentalRecords(dentalRecordsData)
        setFolders(foldersData)
        setBillingSummary(billingData)
        /*
         * Only the séances whose remaining question is the MONEY one. `nextStep` is served precisely so this
         * cascade is never re-derived in a browser: a visit nobody has confirmed happened is not « missing a
         * note d'honoraires », and a séance with no fiche has no acts to price. Filtering on the step the
         * server already decided is reading its answer, not repeating its rule.
         */
        setUnbilledVisits(
          (unbilledData?.visits.items ?? []).filter((v: VisitToCloseDto) => v.nextStep === "Billing"),
        )
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
        if (!cancelled) {
          showErrorToast(err, "Certaines données du dossier n'ont pas pu être chargées.")
        }
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

  // Deep-link from « Corriger cette note » on /factures (?editRecord=<ficheId>): open that fiche's editor, which
  // is the only door where the correction is expressible — the price is changed on the acts, and the note follows.
  // Two steps because the modal edits the record itself, and the fiches arrive with the page's phase-2 batch.
  const [pendingEditRecordId, setPendingEditRecordId] = useState<string | null>(null)
  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get("editRecord")
    if (id) setPendingEditRecordId(id)
  }, [patientId])

  // ⚠️ `detailsLoading`, never `loading`: the latter gates the patient's IDENTITY and goes false at the end of
  // phase 1, while the fiches arrive with phase 2 — so waiting on it announces « introuvable » over a list that
  // has not been fetched yet.
  useEffect(() => {
    if (!pendingEditRecordId || detailsLoading) return
    const record = dentalRecords.find((r) => r.id === pendingEditRecordId)
    setPendingEditRecordId(null)
    // ⚠️ Dropped HERE, not when the param was read: `router.push` rewrites the URL *after* the destination
    // page's effects run, so a `replaceState` on mount is silently overwritten and the param survives — which
    // reopens the editor on every refresh. By now the navigation has settled.
    window.history.replaceState({}, "", `/patients/${patientId}?tab=medical-records`)
    // Cleared either way: a fiche deleted between the two screens must not leave the link armed for ever, and
    // silence would read as « the button does nothing ».
    if (!record) {
      toast.error("Cette fiche de soins est introuvable : elle a peut-être été supprimée.")
      return
    }
    setEditingRecord(record)
    setRecordModalOpen(true)
  }, [pendingEditRecordId, detailsLoading, dentalRecords])

  // ?tab=… lands the visitor on a specific tab — used by the plan workspace's « Voir la fiche », which needs
  // to open the medical-records tab rather than dumping the user on the default one. Same window.location
  // idiom as above (useSearchParams would force this page out of static prerendering); the param is left in
  // the URL so a refresh keeps the tab.
  // Only a tab that still exists is honoured. `odontogram` was one until it became a card of its own, and a
  // value with no trigger leaves Radix showing an empty panel under an unselected tab strip — so an old
  // bookmark or a stale link falls back to the default rather than to a blank page.
  useEffect(() => {
    const tab = new URLSearchParams(window.location.search).get("tab")
    if (!tab) return
    // A retired value resolves to the panel that absorbed it, so an old bookmark lands on the content rather
    // than silently on the default tab — see `RETIRED_PATIENT_TABS`.
    const resolved = RETIRED_PATIENT_TABS[tab] ?? tab
    if (PATIENT_TABS.includes(resolved)) setActiveTab(resolved)
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

  /**
   * The teeth this patient has a multi-séance treatment running on — the odontogramme's third reading.
   *
   * Derived from the plans this page already holds for the band below the chart, so the mark on a tooth and
   * the band's own list can never disagree about which treatments are live. See `teethUnderTreatment`.
   */
  const teethInTreatment = teethUnderTreatment(treatmentPlans)

  const patientName = getPatientName(patient)
  const age = calculateAge(patient.dateOfBirth)

  // Open (not-yet-done) steps of the patient's active plans — offered in the record modal to close the
  // plan→record loop, and completed automatically when a linked record is saved.
  const planItemOptionOf = (
    p: TreatmentPlanDto,
    it: TreatmentPlanDto["items"][number],
  ): PlanItemOption => ({
    itemId: it.id,
    planId: p.id,
    /*
     * ⚠️ **A followed treatment's title IS its act's name, so the obvious `number ?? title` prints it
     * twice.** `StartTreatmentCommand` sets the plan title from the procedure (« the dentist named it by
     * picking it »), and such a plan has no number — so « Acte planifié » read
     * « Couronne / bridge (par élément) · Couronne / bridge (par élément) », which is what a dentist
     * reported as « pourquoi l'acte est écrit deux fois ». Five rows in the live database were in exactly
     * that shape, and every future followed treatment is.
     *
     * A hand-written Draft devis whose title is genuinely something else (« Plan esthétique ») keeps it —
     * only the duplicate is replaced, and it is replaced by what the object actually is.
     */
    label: `${planItemHeading(p, it)} · ${it.designationFr}${it.toothNumbers.length > 0 ? ` (dents ${it.toothNumbers.join(", ")})` : ""}`,
    designationFr: it.designationFr,
    plannedCost: it.plannedCost,
    /*
     * ⚠️ **The teeth already treated win over the devis LINE's, and the line is very often empty.** A row
     * reading « Implant dentaire — acte général » carries no teeth at all, so séance 2 opened on a blank
     * chart and the dentist re-picked — or, as measured on a real implant, did not: its three fiches
     * recorded the teeth once between them, on whichever séance they happened to fill in.
     *
     * It stopped being a convenience when the odontogram stopped being charted from the FIRST séance
     * (`ToothChartingRules`): the chart is written when the act finishes, so teeth entered early and absent
     * from the last fiche would now chart nothing at all.
     */
    toothNumbers:
      it.treatedToothNumbers && it.treatedToothNumbers.length > 0
        ? it.treatedToothNumbers
        : it.toothNumbers,
    // The devis this act is priced on, so the fiche can say « déjà facturé » instead of re-charging it.
    // The note is what suppresses the devis' own « reste »: a bridged plan's échéance never sees a payment.
    planNumber: p.number,
    billedOnInvoiceNumber: p.linkedInvoiceNumber ?? null,
    planOutstanding: p.outstanding,
    // The protocol, so the fiche can say WHICH séance it is and name it — « Cette séance : étape 1 sur 3 ·
    // Préparation ». The steps themselves rather than counts: the séance's step is the one the appointment
    // booked, which `stepsDone + 1` only happens to equal when the séances are carried out in order. Empty
    // for an act with no protocol, which is what keeps the ordinary fiche's banner unchanged.
    steps: it.steps ?? [],
    // Which catalogue act this line is priced on — how a reopened fiche knows which of its acts the devis
    // already pays for, so that act's 0 is not read back as a discount the dentist granted.
procedureTypeId: it.procedureTypeId ?? null,
  })

  const openPlanItems: PlanItemOption[] = (() => {
    const options = treatmentPlans
      .filter((p) => isPlanLive(p.status))
      .flatMap((p) => schedulablePlanItems(p).map((it) => planItemOptionOf(p, it)))

    /*
     * ⚠️ **The act a fiche being EDITED already points at, whatever its status and whatever its plan's — and
     * without it the fix above reached only half the fiches.** `schedulablePlanItems` is the *offer* list: it
     * drops a `Done` act, correctly, because proposing one for a new fiche is what `MarkDone` refuses. But an
     * existing record's link is a historical fact, not an offer, and the last séance of every finished
     * treatment has exactly that shape — measured on the dev database, a « Facette » fiche whose act was Done
     * had no « Acte planifié » control at all (the Select renders only for a non-empty list), no
     * « Déjà facturé » notice, and its card announced « Tarif catalogue 700,000 DT — geste de 700,000 DT »
     * beside a « remettre au tarif » link. The phantom discount, on a completed treatment, permanently.
     *
     * ⚠️ Neither `isPlanLive` nor `activeItems` here: a Completed plan is not live and a Withdrawn act is not
     * active, yet the fiche that evidenced either still happened and must still read correctly. Appended only
     * when a record is being edited, so nothing new is ever *offered* — a Select must contain its own value.
     */
    const linkedId = editingRecord?.treatmentPlanItemId
    if (linkedId && !options.some((o) => o.itemId === linkedId)) {
      for (const p of treatmentPlans) {
        const it = p.items.find((i) => i.id === linkedId)
        if (it) {
          options.push(planItemOptionOf(p, it))
          break
        }
      }
    }

    return options
  })()

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

  /*
   * « Encaisser » on a « Reste à payer » row.
   *
   * ⚠️ **The document is re-read before the dialog opens**, never taken from `billingSummary`'s snapshot. Both
   * dialogs prefill and bound themselves on the document's live « reste », and a colleague who settled the note
   * in the meantime would otherwise leave the field prefilled with a figure the server now refuses — a refusal
   * for a number this page itself printed. It also spares the band from having to carry a whole `InvoiceDto`
   * per row. On failure the band is refreshed rather than left asserting the old figure.
   *
   * ⚠️ **No new money writer.** Both dialogs are the ones `/factures` and the devis workspace already use, so
   * a payment recorded here goes through `RecordPaymentCommand` / `RecordInstallmentPaymentCommand` like any
   * other — cash lives in exactly two ledgers and this adds neither a third nor a second route into them.
   */
  const openInvoicePayment = async (line: PatientDebtLineDto) => {
    setOpeningDocumentId(line.documentId)
    try {
      setPaymentInvoice(await invoicesApi.get(line.documentId))
    } catch (err) {
      showErrorToast(err, "La note d'honoraires n'a pas pu être ouverte.")
      setRefreshKey((k) => k + 1)
    } finally {
      setOpeningDocumentId(null)
    }
  }

  const openInstallmentPayment = async (line: PatientDebtLineDto) => {
    setOpeningDocumentId(line.documentId)
    try {
      const plan = await treatmentPlansApi.get(line.documentId)
      /*
       * The oldest échéance that can still take money, re-picked from the FRESH aggregate rather than trusting
       * `line.payableInstallmentId`: between the read and the press that échéance may have been settled, and
       * `Installment.RecordPayment` would refuse the payment against it. Ordered exactly as the server's own
       * projection is — due date, then id — so the two pick the same row.
       */
      const target = [...plan.installments]
        .filter((i) => !i.isPaid && i.outstanding > 0)
        .sort((a, b) => a.dueDate.localeCompare(b.dueDate) || a.id.localeCompare(b.id))[0]
      if (!target) {
        // The band offered « Encaisser » on a devis whose échéancier has since closed. Say so and re-read,
        // rather than opening a dialog with nothing to pay into.
        toast.info("Cet échéancier ne peut plus recevoir de paiement. Ouvrez le devis pour le compléter.")
        setRefreshKey((k) => k + 1)
        return
      }
      setPaymentInstallment({ planId: plan.id, installment: target })
    } catch (err) {
      showErrorToast(err, "Le devis n'a pas pu être ouvert.")
      setRefreshKey((k) => k + 1)
    } finally {
      setOpeningDocumentId(null)
    }
  }

  /**
   * « Solde dû » in the header → the breakdown, wherever it is.
   *
   * ⚠️ **Two steps, and the scroll must wait for the first.** The band lives in the « Actes dentaires » tab,
   * and Radix mounts a `TabsContent` only when it becomes active — so scrolling in the same tick finds no
   * element at all. `requestAnimationFrame` is what puts the lookup after the commit that mounts it.
   *
   * ⚠️ `block: "center"` rather than `"start"`: the page scroller is `AppShell`'s `<main>` and the band sits
   * under a full table, so aligning to the top of the viewport leaves the figure the reader just pressed off
   * screen above it.
   */
  const openOutstandingSection = () => {
    openTab("medical-records")
    requestAnimationFrame(() => {
      document
        .getElementById(PATIENT_OUTSTANDING_SECTION_ID)
        ?.scrollIntoView({ behavior: "smooth", block: "center" })
    })
  }

  /** The document itself — the devis workspace, or the note in this page's own Factures tab. */
  const openOutstandingDocument = (line: PatientDebtLineDto) => {
    if (line.kind === "TreatmentPlan") {
      router.push(`/treatment-plans/${line.documentId}`)
      return
    }
    openTab("factures")
  }

  /**
   * « Facturer » on a « Travail non facturé » row — the existing `BillDentalRecordDialog`, which issues the
   * note and records the cash in one action and states the irreversibility before the press.
   *
   * The visit carries its fiche's id; the fiche itself is already in this page's state, so no read is needed.
   * A row with no fiche cannot reach here — `VisitClosureRules` asks the fiche question before the money one.
   */
  const billUnbilledVisit = (visit: VisitToCloseDto) => {
    const record = dentalRecords.find((r) => r.id === visit.dentalRecordId)
    if (!record) {
      // The fiche moved (deleted, or re-linked) since the band was read. Re-read rather than open a dialog on
      // nothing — the row will simply be gone.
      toast.info("Cette séance a changé. La liste a été actualisée.")
      setRefreshKey((k) => k + 1)
      return
    }
    setBillingRecord(record)
  }

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

  // One splitter for all three, shared with the form that writes them (`lib/health-list.ts`): they are read as
  // a set, and a value that looks different from its neighbour reads as a different KIND of fact.
  const allergiesList = splitHealthList(patient.allergies)
  const diseasesList = splitHealthList(patient.medicalHistory)
  const medicationsList = splitHealthList(patient.medications)
  
  // Parse medical history (if it contains structured data, otherwise show as text)
  

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
            </div>

            {/*
              « Motif de consultation » — why this patient came in the first place, directly under their name.

              ⚠️ Clamped to one line with the full value in the `title`, never truncated away entirely: the column
              is unbounded server-side (a capped one would turn a long paste into a save that fails naming no
              field), so the display is what keeps it to a line rather than the storage.
            */}
            {/*
              « Motif de consultation » — why this patient came in the first place, directly under their name.

              ⚠️ It was briefly moved into a « Consultation » panel of its own and that was wrong twice over: it
              is one short line, so a half-width panel spent the whole right side of the band on it, and it is
              not a health fact — it is the reason this person is on the books, which is exactly what belongs
              beside their name.

              ⚠️ Clamped to one line with the full value in the `title`, never truncated away entirely: the column
              is unbounded server-side (a capped one would turn a long paste into a save that fails naming no
              field), so the display is what keeps it to a line rather than the storage.
            */}
            {patient.consultationReason && (
              <p
                className="line-clamp-1 text-sm text-muted-foreground"
                title={patient.consultationReason}
              >
                <span className="font-medium text-foreground">Motif :</span>{" "}
                {patient.consultationReason}
              </p>
            )}

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
              {/*
                « Tabac » takes the slot the assureur vacated, and it earns it on this strip's own terms: it is a
                fact that changes what the practitioner does (healing, implant survival, periodontal work) and it
                is one short phrase. Omitted when unanswered rather than printed as « non renseigné » — the strip's
                convention, the same one the assureur and « Adressé par » follow.

                Amber for a current smoker and plain for the rest: « Non-fumeur » is reassurance and « Ancien
                fumeur » is history, so tinting either would spend a warning colour on a patient nothing is wrong
                with — the same rule `PatientAlertPanel` applies one component over.
              */}
              {/*
                ⚠️ **« Tabac » used to sit here and has moved into the « Santé » panel.** It took the slot the
                assureur vacated and earned it on this strip's own terms — one short phrase, a fact that changes
                what the practitioner does. What that reasoning missed is the *company* it keeps: beside the age,
                the sexe, the telephone and the solde dû, a risk factor is rendered exactly like a contact
                detail. It is a health fact and it now reads with the health facts.
              */}
              {/*
                Rendered only when something is actually owed. A « Solde dû 0,000 DT » on every settled patient
                is a line that trains the eye to skip the line — and this strip's whole convention is that a
                fact with no value is omitted rather than printed as « — ». A failed read is `null` and also
                shows nothing: see `attemptOne`. `text-warning-ink` is the token every other "still owed" figure
                on this page already uses (`text-amber-600` had no `dark:` pair and measured ~3.2:1).
              */}
              {billingSummary !== null && billingSummary.totalOutstanding > 0 && (
                /*
                 * ⚠️ A real control, not a `<span>`, and that is the whole point of it: « Reste à payer » lives
                 * inside the « Actes dentaires » tab now, so the one figure a header should carry has to be the
                 * way to it — otherwise the breakdown is a section nobody on this page can find. It switches
                 * the tab AND scrolls, because either alone leaves the reader somewhere they did not ask for.
                 *
                 * `touch-target`, not `coarse:py-*`: this is one isolated inline control in a wrapping row of
                 * text, so the 44 px hit area is overlaid rather than painted (§ 2) — growing it would push
                 * « âge · téléphone · assureur » apart on every patient.
                 */
                <button
                  type="button"
                  onClick={openOutstandingSection}
                  className="touch-target inline-flex items-center gap-1 rounded text-muted-foreground underline-offset-2 hover:underline"
                  aria-label={`Solde dû ${formatDT(billingSummary.totalOutstanding)} — voir le détail et encaisser`}
                >
                  Solde dû{" "}
                  <span className="font-semibold text-warning-ink">
                    {formatDT(billingSummary.totalOutstanding)}
                  </span>
                  <ChevronRight aria-hidden="true" className="size-3.5" />
                </button>
              )}
              {/* « Adressé par » belongs in the strip and not only in the card below: a referred patient owes
                  the referrer a lettre de liaison, and that obligation has to be visible on opening the file
                  rather than three screens down. Rendered only when there is one — a patient who came on
                  their own has nothing to state. */}
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

            {/*
              ⚠️ **The allergy badges that used to close this column are now the « Santé » panel below.** The
              line they replaced — « Aucune allergie signalée » — was true and incomplete: maladies, médicaments
              and tabac were equally unrecorded and it said nothing about any of them, so a blank under the name
              read as « rien à signaler » when it meant « on n'a rien demandé ».
            */}
          </div>

          {/*
            `size="sm"` + trimmed labels so the four actions stay on the NAME's line with the rail expanded.
            At default size and full wording they measured ~780px, which does not fit beside a `text-3xl`
            name once the sidebar takes its 256px — so the whole group dropped to a second row, pushing the
            identity strip and everything under it down by a button's height.

            The words removed are the ones the context already supplies: this *is* the patient's page, so
            « Modifier le patient » is « Modifier », and each button keeps its icon plus a `title` carrying
            the full phrase. `sm:shrink-0` stops the row being compressed instead of the name.

            ⚠️ **The pin is `xl:`-prefixed, and a `sm:` pin scrolled every tablet sideways.** Pinned, the group
            cannot shrink, so its own `flex-wrap` never fires — the box is already as wide as its content — and
            the outer row cannot rescue it either: wrapping a 633 px item onto a line of its own still overflows
            a 501 px line. Measured at 820 px with the rail out: group 633 px in a 501 px column, `<main>`
            scrolling 657 in 549, « Planifier un RDV » 93 px past the right edge. That was already true at 609 px
            with the shorter label this button replaced; five actions of French simply do not fit beside a name
            on a tablet.
            So below `xl:` the group takes a **row of its own** (`basis-full`) and is free to shrink, which is
            what lets `flex-wrap` split the five buttons over two rows. The name then keeps the whole width above
            it rather than being crushed to the ~56 px a 1024 px viewport would leave it — which is the defect a
            plain `lg:shrink-0` trades this one for, and the reason the pin exists at all. From 1280 px there is
            room for both, so the group returns beside the name and the pin is right again.
          */}
          {/*
            ⚠️ **Icon-only below `sm:`, with the label kept in the DOM** — `ExportButton compact`'s own pattern,
            which already sat in this very row, applied to the four controls beside it so the row is one shape
            rather than one compact control among four full-width ones.

            Measured at 390 px: five French labels wrapped this group onto **three 44 px rows — ~148 px**, and it
            is one of the blocks standing between the top of the page and the odontogramme, which began at
            **y = 698** with **18 buttons** above it. Icon-only the row is 5 × 44 px + gaps = ~252 px, i.e. a
            single row at 390 px *and* at 320 px (288 px of content box).

            ⚠️ **REVERSED below `sm:`: it IS a menu now, and a labelled one.** The note here used to reject that
            on two grounds. The first — that `ExportButton` carries its own step-up dialog, which Radix unmounts
            in the same tick `onSelect` closes the menu — was real and is now answered: `useCsvExport` splits the
            behaviour from the trigger, so the menu holds a plain item and the dialog is a **sibling** of the
            menu, which is the shape `expense-movement-actions.tsx` prescribes. The second — « nothing here is
            hidden, so § 0 is not engaged » — was true and beside the point: what the icon-only row hid was not
            the controls but their **names**. Five glyphs with no words, on the one device that has no hover to
            reveal a `title`, is the « mystery meat » NN/g measures a 39 % task-time cost for.

            So from `sm:` up nothing changes at all — four labelled buttons, no menu, no extra tap. Below it the
            row becomes « Actions ▾ » + « Planifier un RDV », two controls that both carry their words.
          */}
          <div className="flex min-w-0 basis-full flex-wrap gap-2 xl:basis-auto xl:shrink-0">
            {/* Below `sm:` the three secondaries fold into one labelled menu; from `sm:` they are buttons. */}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="gap-2 coarse:h-11 sm:hidden">
                  Actions
                  <ChevronDown className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start" className="w-[min(16rem,calc(100vw-2rem))]">
                <DropdownMenuItem onSelect={() => { setEditSection(null); setEditDialogOpen(true) }}>
                  Modifier le patient
                </DropdownMenuItem>
                <DropdownMenuItem onSelect={() => router.push(`/patients/${patient.id}/files`)}>
                  Fichiers et dossiers
                </DropdownMenuItem>
                <DropdownMenuItem disabled={dossierExport.working} onSelect={() => dossierExport.start()}>
                  {dossierExport.working ? "Export…" : "Exporter le dossier"}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            {/* ⚠️ The step-up dialog is a SIBLING of the menu, never inside `DropdownMenuContent`. */}
            {dossierExport.dialog}
            <Button
              variant="outline"
              size="sm"
              onClick={() => { setEditSection(null); setEditDialogOpen(true) }}
              className="hidden gap-2 coarse:h-11 sm:inline-flex"
              aria-label="Modifier le patient"
            >
              <Edit className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only">Modifier</span>
            </Button>
            {/* Files live on their own route, which is the whole manager — folders, upload, delete. It sits in
                the action row rather than as a panel above the odontogram: « do they have a panoramique? » is a
                question you go and answer, not one worth spending permanent vertical space on. */}
            <Button
              variant="outline"
              size="sm"
              onClick={() => router.push(`/patients/${patient.id}/files`)}
              className="hidden gap-2 coarse:h-11 sm:inline-flex"
              aria-label="Fichiers et dossiers du patient"
            >
              <FolderOpen className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only">Fichiers</span>
            </Button>
            {/*
              ⚠️ **« Plans de traitement » stood HERE and was removed — it was the third route to one tab.**
              It called `openTab("treatment-plans")`, which is also what the tab strip itself does 200 px below
              and what « Tous les plans » does in `PatientPlansStrip` between the two. Three doors, one room.

              It was also the widest control in the row at **175 px** — wider than « Planifier un RDV », the one
              action that is not navigation — and that width is what made the group wrap: measured at 820 px the
              five buttons came to 654 px against the ~532 px the column has, so they took two rows on the device
              this product is used on most. At four they measure 471 px and fit on one.

              Nothing is unreachable: the tab, the strip's own link and `?tab=treatment-plans` all still work.
            */}
            {/*
              « Dossier » — the patient's own copy of their record, as one archive.
              This is the right of access under la loi organique 2004-63, and it is also the request a cabinet
              fields constantly for an ordinary reason: somebody is changing dentist. It sits here rather than in
              a settings screen because the person who receives that request is the person looking at this page.
              Confirmed like the roster export: it is one person's whole medical history in a single file.
            */}
            <ExportButton
              path={`/patients/${patient.id}/dossier`}
              label="dossier"
              stepUpAction="export-patient-dossier"
              stepUpPurpose="Exporter le dossier complet de ce patient, pour le lui remettre"
              className="hidden sm:inline-flex"
            />
            {/* `coarse:h-11` across the whole row, matching `ExportButton`'s own painted floor: on a coarse
                pointer one 44 px control beside four 32 px ones was visibly misaligned, and their `touch-target`
                overlays overhung each other besides. */}
            <Button
              size="sm"
              className="gap-2 coarse:h-11"
              aria-label="Planifier un rendez-vous"
              onClick={() => router.push(`/appointments?patientId=${patient.id}`)}
            >
              <CalendarPlus className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only">Planifier un RDV</span>
            </Button>
          </div>
        </div>

        {/*
          « Santé » — one card across the whole line, above « À compléter » and the notes.

          ⚠️ **It sits OUTSIDE the name/actions row, and that is a constraint rather than a preference.** Inside
          the header's left column it is a full-width block, so the column demanded the whole line and the five
          action buttons — pinned beside the name from `xl:` up — wrapped to a row of their own at every width.
          As a sibling band it takes the width it wants and the buttons keep the place that pin exists to give
          them.
        */}
        <PatientHealthCard
          allergies={allergiesList}
          diseases={diseasesList}
          medications={medicationsList}
          tobacco={tobaccoSummary(patient.tobaccoUse)}
          /*
            ⚠️ **An empty list is passed as empty ONLY when the read succeeded.** These two live in their own
            tables behind their own endpoints, so `[]` means both « ce patient n'en a aucun » and « je n'ai pas
            pu lire » — and this card omits a column that is empty, which would turn the second into a silent
            claim of the first on the one block a dentist checks before injecting. `sectionFailed` short-circuits
            to a non-empty marker instead, so the column stays and says so; the record card at the foot already
            routes the same two reads through `renderSectionEmpty` for this reason.
          */
          medicalHistory={
            sectionFailed("medicalHistory")
              ? [HISTORY_UNREADABLE]
              : medicalHistoryEntries.map((entry) => entry.description).filter(Boolean)
          }
          familyHistory={
            sectionFailed("familyHistory")
              ? [HISTORY_UNREADABLE]
              : familyHistoryEntries
                  .map((entry) => [entry.relationship, entry.condition].filter(Boolean).join(" : "))
                  .filter(Boolean)
          }
          onEdit={() => { setEditSection("medical"); setEditDialogOpen(true) }}
        />

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
          ⚠️ The « Patient créé depuis Google Agenda, à compléter » band was HERE and was removed deliberately.
          It restated, on every visit to the fiche, something the practice had already been told — and the fiche
          is where somebody goes to *work*, not to be reminded that a record is thin. « Patients à compléter » on
          « À clôturer » is the one place that asks, it carries the same two actions (« Compléter les infos
          patient » and the duplicate-merge prompt), and « Ne plus afficher » there now simply means dismissed.

          A thin fiche is not an error state, and the fields it lacks are optional by design — a walk-in
          registered with a name alone is an ordinary patient. See features/calendar-import-revert/notes.md.
        */}

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
        {/*
          ⚠️ **No `CardDescription`, and its absence is the largest single saving on this card.** It read
          « Cliquez sur une dent pour noter un diagnostic (à traiter) ; les actes réalisés s'ajoutent
          automatiquement … » and measured **80 px — four lines — at 390 px**, permanently, above a chart that is
          177 px. Both halves were already said better elsewhere: tapping a tooth is what the chart teaches in one
          tap and the tooth editor's own heading confirms, and « les actes réalisés s'ajoutent automatiquement »
          is the « Actes réalisés » tab standing right underneath. `pb-2` because the header is now one line.
        */}
        {/* `gap-2` overrides `Card`'s own `gap-6`: with the description gone the header is a single 20 px line,
            and 24 px of gap under it is a quarter of the space between the card's edge and the first tooth. */}
        <Card className="gap-2">
          <CardHeader className="pb-0">
            <CardTitle className="flex items-center gap-2">
              <Smile className="h-5 w-5" />
              Odontogramme
            </CardTitle>
          </CardHeader>
          <CardContent>
            <Odontogram
              patientId={patientId}
              dentition={patient.dentition}
              dentitionAnswered={patient.dentitionAnswered}
              dateOfBirth={patient.dateOfBirth}
              treatments={teethInTreatment}
              onCreatePlan={(seeds) => {
                setPlanSeeds(seeds)
                setSeededPlanOpen(true)
              }}
            />
          </CardContent>
        </Card>

        {/*
          ⚠️ **Under l'odontogramme — a REVERSAL of the placement this file recorded before, and the reasons
          for both are worth keeping.** The band was moved ABOVE the chart on request, with a real argument:
          « où en est le traitement, et qu'est-ce qui reste ? » is a fact the dentist needs before touching
          anything, and below a full-width tooth chart it sat at or past the fold on a laptop. What changed is
          the other half of that trade, asked for in as many words — « the patient page should have the
          odontogramme as focus ». It is the chart the whole consultation is read off, and a band above it
          pushed the mouth itself off the first screen.

          The fold argument is answered rather than abandoned: the chart now MARKS every tooth a treatment is
          under way on (`teethUnderTreatment`), so the first thing on the page carries the treatment's
          presence, and this band — directly beneath it — carries its detail and its next action.
        */}
        {sectionFailed("plans") && <SectionLoadFailure onRetry={retrySections} />}
        <PatientPlansStrip
          plans={treatmentPlans}
          onOpen={() => openTab("treatment-plans")}
          onChanged={() => setRefreshKey((k) => k + 1)}
        />


        {/* Treatment leads the patient page now. A devis buried in the 8th tab was the whole reason the plan
            felt disconnected from the patient it belongs to. A band rather than a card since the redesign —
            ~76 px instead of ~250 — and it renders only when the patient has no plans at all. */}
        {/* ⚠️ The band renders NOTHING when `plans` is empty, so a failed `treatmentPlansApi` read is invisible
            and silently asserts « never had a plan » about a patient with three. This is the one section whose
            empty state is "no element at all", which is why the failure has to be reported beside it. */}
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
          {/*
            ⚠️ **Six columns, and `sm:grid-cols-3` — the counts are tied to the number of triggers.** With seven
            it was `sm:grid-cols-4 lg:grid-cols-7`, which on a tablet meant a row of four over a row of three:
            a ragged block whose last row's tabs are wider than the first's, so the strip read as two unrelated
            groups. Six divides into two equal rows of three, and into one row at `lg:`.

            ⚠️ **Every trigger's icon is distinct, and each one repeats in its own panel's header** (see
            `PatientTabSection`). Notes, Documents and Fichiers all carried `FileText`, which told the reader
            nothing and actively suggested three views of one thing.
          */}
          <TabsList
            ref={tabsListRef}
            className="flex h-auto w-full items-stretch gap-1 overflow-x-auto p-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden sm:grid sm:grid-cols-3 sm:overflow-visible lg:grid-cols-6"
          >
            <TabsTrigger value="medical-records" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileCheck className="h-4 w-4" />
              Dossiers médicaux
            </TabsTrigger>
            {/* Second, not last: treatment is what the first tab's actes lead to, so the two sit side by side. */}
            <TabsTrigger value="treatment-plans" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <ClipboardCheck className="h-4 w-4" />
              Plan de traitement
            </TabsTrigger>
            <TabsTrigger value="appointments" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <Calendar className="h-4 w-4" />
              Rendez-vous
            </TabsTrigger>
            <TabsTrigger value="documents" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FileText className="h-4 w-4" />
              Documents
            </TabsTrigger>
            <TabsTrigger value="files" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <FolderOpen className="h-4 w-4" />
              Fichiers
            </TabsTrigger>
            <TabsTrigger value="factures" className="h-auto min-h-9 shrink-0 gap-2 whitespace-nowrap py-1.5 text-center leading-tight sm:shrink sm:whitespace-normal">
              <Receipt className="h-4 w-4" />
              Factures
            </TabsTrigger>
          </TabsList>
          <span
            aria-hidden="true"
            className="pointer-events-none absolute inset-y-0 right-0 w-6 rounded-r-lg bg-gradient-to-l from-muted to-transparent sm:hidden"
          />
          </div>

          {/* Medical Records Tab - Unified View */}
          <TabsContent value="medical-records" className="space-y-4">
            {/* Dental Records Section. The header construction — wrap, `min-w-0 flex-1`, a full-width action
                below `sm:` — is `PatientTabSection`'s now, and it explains itself there; it was hand-copied
                into three of these panels and missing from the four that had no action yet. */}
            <PatientTabSection
              icon={FileCheck}
              title="Actes dentaires"
              description="Historique complet des actes et interventions dentaires"
              action={
                <Button
                  onClick={() => {
                    setEditingRecord(null)
                    setRecordModalOpen(true)
                  }}
                  size="sm"
                  className="w-full sm:w-auto"
                >
                  Ajouter un acte dentaire
                </Button>
              }
            >
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
                          Ajouter un acte dentaire
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
                      className={CARDS_ONLY_LG}
                      ariaLabel="Actes dentaires"
                      items={recordsPage.items}
                      getKey={(record) => record.id}
                      title={(record) => record.procedureType}
                      subtitle={(record) => formatDate(record.interventionDate)}
                      fields={(record) => {
                        const invoiced = invoicedDentalRecordIds.has(record.id)
                        /*
                         * A séance OF a treatment — see `CollectedOnTreatment`.
                         *
                         * ⚠️ Keyed on the LINK, not on money having moved. Branching on
                         * `collectedOnTreatment > 0` left a séance that collected nothing reading « 0,000 DT ·
                         * 0,000 DT », indistinguishable from an ordinary free visit while the treatment showed
                         * 1 500 DT outstanding — and on a six-visit implant most séances collect nothing.
                         */
                        const onTreatment = !invoiced && record.treatmentPlanId != null
                        const reste = Math.max(0, record.balance ?? record.cost - record.amountPaid)
                        // ⚠️ Tested here, not by letting the component return null: `CardList` drops a field on an
                        // empty *value*, and a React element is never empty — the row would keep an « NOTES »
                        // label over nothing.
                        const hasNotes =
                          (record.notes?.length ?? 0) + (record.importantNotes?.length ?? 0) > 0
                        return [
                          {
                            // « Dents » on a multi-act séance was the union of every act's teeth with nothing
                            // saying which was which — the same defect as the desktop table's two columns.
                            label: (record.acts?.length ?? 0) > 1 ? "Actes" : "Dents",
                            value:
                              (record.acts?.length ?? 0) > 1 || record.toothNumbers.length > 0 ? (
                                <RecordActsSummary record={record} align="end" hideSingleName hidePrescription />
                              ) : null,
                          },
                          {
                            label: "Montant payé",
                            value: invoiced ? (
                              <BilledAmount
                                amount={record.amountPaid}
                                invoiceNumber={invoicingNumberByRecordId.get(record.id)}
                              />
                            ) : onTreatment ? (
                              <CollectedOnTreatment
                                amount={record.collectedOnTreatment!}
                                planNumber={record.treatmentPlanNumber}
                                planId={record.treatmentPlanId}
                              />
                            ) : (
                              formatDT(record.amountPaid)
                            ),
                          },
                          {
                            label: "Reste",
                            value: onTreatment ? (
                              // ⚠️ A séance of a treatment has no « reste » of its OWN — the act is priced once
                              // and what remains is the treatment’s, not this visit’s. Printing 0,000 here read
                              // as « rien à payer » beside a patient owing 500 on the devis; repeating the
                              // treatment’s own balance on every séance row would be the same lie multiplied.
                              <span className="text-muted-foreground">—</span>
                            ) : reste > 0 ? (
                              // `--warning-ink`: `text-amber-600` had no `dark:` pair and measures ~3.2:1 on
                              // the card — on the figure that says money is still owed.
                              <span className="font-semibold text-warning-ink">{formatDT(reste)}</span>
                            ) : (
                              <span className="text-muted-foreground">{formatDT(0)}</span>
                            ),
                          },
                          // A card has room for a labelled row, so the prescription is a FIELD here rather
                          // than a line inside « Actes ». Omitted entirely when there is none — a field with no
                          // value is absent, never « — » (§ 6).
                          (record.prescriptionSummary?.length ?? 0) > 0 && {
                            label: "Prescription",
                            value: (
                              <span className="text-end">
                                {record.prescriptionSummary!.join(", ")}
                              </span>
                            ),
                          },
                          // Its own field, never folded into the one above: an examen is on a separate sheet,
                          // and « Prescription » listing a panoramique beside an antibiotic would say the
                          // patient has one paper to hand over when they have two.
                          (record.examensSummary?.length ?? 0) > 0 && {
                            label: "Examens",
                            value: (
                              <span className="text-end">{record.examensSummary!.join(", ")}</span>
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
                            <Button
                              variant="ghost"
                              size="icon"
                              /* Names the fiche, not the verb: ten cards otherwise announce « Actions de l'acte
                                 dentaire » ten times over (§ 13). `icon-button-is-named` only checks that a
                                 name exists, which is why the table's five buttons were fixed and the card
                                 trees were missed. */
                              aria-label={`Actions de la fiche du ${formatDate(record.interventionDate)}`}
                            >
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            {!invoicedDentalRecordIds.has(record.id) && (
                              <DropdownMenuItem onSelect={() => setBillingRecord(record)}>
                                Facturer cette intervention
                              </DropdownMenuItem>
                            )}
                            {record.prescriptionDocumentId && (
                              // The card's own route to the ordonnance. The desktop row puts it on the
                              // « Prescrit : … » line inside « Actes »; a card lists that as a field, and
                              // every card action lives in this one menu (§ 6).
                              <DropdownMenuItem
                                onSelect={() => openPrescription(record.prescriptionDocumentId!)}
                              >
                                Ouvrir l&apos;ordonnance
                              </DropdownMenuItem>
                            )}
                            {record.examensDocumentId && (
                              // A SECOND item, not a variant of the first: the séance may have issued both,
                              // and each is a separate paper the patient hands to a different place.
                              <DropdownMenuItem
                                onSelect={() => openPrescription(record.examensDocumentId!)}
                              >
                                Ouvrir la demande d&apos;examens
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
                    <Table containerClassName={TABLE_ONLY_LG}>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Date</TableHead>
                          {/* ONE column, because « Type d'acte » and « Dents » were two answers to one question.
                              The first held the server's comma-joined summary of every act, the second the flat
                              union of every act's teeth — so a séance of two acts printed both names beside all
                              five teeth and said nothing about which belonged to which. */}
                          <TableHead>Actes</TableHead>
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
                            <TableCell>
                              <RecordActsSummary record={record} onOpenPrescription={openPrescription} />
                            </TableCell>
                            <TableCell>
                              {invoicedDentalRecordIds.has(record.id) ? (
                                <BilledAmount
                                  amount={record.amountPaid}
                                  invoiceNumber={invoicingNumberByRecordId.get(record.id)}
                                />
                              ) : record.treatmentPlanId != null ? (
                                // The link, not the amount — see the card list above.
                                <CollectedOnTreatment
                                  amount={record.collectedOnTreatment ?? 0}
                                  planNumber={record.treatmentPlanNumber}
                                  planId={record.treatmentPlanId}
                                />
                              ) : (
                                formatDT(record.amountPaid)
                              )}
                            </TableCell>
                            {/* The figure, on every row. This cell used to print the word « Facturé » instead of
                                a number for a billed fiche — a status in the money column, and the second of
                                three places the same row said it. */}
                            <TableCell>
                              {(() => {
                                // A séance of a treatment has no reste of its own — see the card list above.
                                if (!invoicedDentalRecordIds.has(record.id)
                                    && record.treatmentPlanId != null) {
                                  return <span className="text-muted-foreground">—</span>
                                }
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
                                    the Actions column. A status has no business there, and « Montant payé » on
                                    the same row already carries it — see `BilledAmount`, which says so in words
                                    now rather than by striking the figure through. */}
                                {!invoicedDentalRecordIds.has(record.id) && (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    className="h-8 w-8 p-0 coarse:size-11"
                                    onClick={() => setBillingRecord(record)}
                                    title="Facturer cette intervention"
                                    aria-label={`Facturer la fiche du ${formatDate(record.interventionDate)}`}
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
                                  aria-label={`Modifier la fiche du ${formatDate(record.interventionDate)}`}
                                >
                                  <Pencil className="h-4 w-4" />
                                </Button>
                                {/*
                                  ⚠️ **The destructive action left the row for a menu, and this is a SAFETY fix
                                  rather than a density one — the control count does not change.** It was a
                                  third unlabelled glyph sitting 4 px from « Modifier », on a row scanned five
                                  times per patient: NN/g names exactly that shape (« Consequential Options
                                  Close to Benign Options »), and the note above this block was already worried
                                  about it in as many words. Behind the « ⋯ » it is named in full instead of
                                  being a red icon, and it can no longer be hit by a slip aimed at its
                                  neighbour. `Facturer` and `Modifier` stay one gesture away, because both are
                                  frequent and NN/g is equally clear that a frequent action must not be hidden.
                                  The card tree has carried this same menu all along.
                                */}
                                {canDeleteClinicalRecords && (
                                  <DropdownMenu>
                                    <DropdownMenuTrigger asChild>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-8 w-8 p-0 coarse:size-11"
                                        aria-label={`Autres actions sur la fiche du ${formatDate(record.interventionDate)}`}
                                      >
                                        <MoreHorizontal className="h-4 w-4" />
                                      </Button>
                                    </DropdownMenuTrigger>
                                    <DropdownMenuContent align="end">
                                      <DropdownMenuItem
                                        className="text-destructive focus:text-destructive"
                                        onSelect={() => setRecordToDelete(record)}
                                      >
                                        Supprimer la fiche de soins du {formatDate(record.interventionDate)}
                                      </DropdownMenuItem>
                                    </DropdownMenuContent>
                                  </DropdownMenu>
                                )}
                              </div>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                    {/*
                      One pager for both renderings — only one of them is ever visible, which is the shape
                      `stock-table.tsx` already uses.

                      ⚠️ **`_LG`, and the column threshold is LOWER here than the rule's « ~8+ ».** § 1 of
                      `.claude/rules/frontend-web.md` sizes that guidance for a table at page level, where an
                      820 px tablet leaves ~532 px after the 256 px rail. This table is nested one level
                      further — a `Card` inside a `TabsContent` — and its box measures **451 px**, so the
                      budget is ~80 px smaller and the threshold falls to five columns. Measured at 820×1024
                      on 2026-09-02, and it is the `Actions` column that pays: `Button` is
                      `whitespace-nowrap shrink-0`, so the cell cannot give width back and the table's
                      minimum simply exceeds its box.

                      | tab | cols | table | box | hidden |
                      |---|---|---|---|---|
                      | Rendez-vous | 7 | 764 | 451 | **313** |
                      | Fichiers | 5 | 631 | 451 | **180** |
                      | Dossiers médicaux | 6 | 522 | 451 | **71** |
                      | Documents | 4 | 451 | 451 | 0 — **stays on `md:`** |

                      A dentist on an iPad therefore could not see « Modifier » or « Supprimer » at all, which
                      is what a trialling dentist reported as « editing the medical record does not work ». The
                      card form carries the same actions (`actions` here and on Fichiers, `primaryAction` on
                      Rendez-vous), so nothing is lost by rendering it — § 0.

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
            </PatientTabSection>

            {/*
              « Reste à payer » — **under** l'historique des actes, in the same tab, and both halves of that
              are deliberate.

              It sat above the tabs at first, which put a money surface between the odontogramme and the whole
              record and pushed the tabs down on every patient who owed anything. Here it reads as the natural
              second half of the tab it is in: the table above says what was done and what each fiche took, and
              this says what is still owed on it and settles it. Its own `id` is what « Solde dû » in the
              header links to, since a band inside a tab cannot be seen from the top of the page.
            */}
            {sectionFailed("billing") && <SectionLoadFailure onRetry={retrySections} />}
            <PatientOutstandingStrip
              summary={billingSummary}
              unbilled={unbilledVisits}
              busyDocumentId={openingDocumentId}
              onCollectInvoice={openInvoicePayment}
              onCollectInstallment={openInstallmentPayment}
              onOpenDocument={openOutstandingDocument}
              onBillVisit={billUnbilledVisit}
            />
          </TabsContent>

          {/* Plan de traitement Tab */}
          <TabsContent value="treatment-plans" className="space-y-4">
            <PatientTabSection
              icon={ClipboardCheck}
              title="Plans de traitement"
              description="Devis, actes planifiés et échéanciers de paiement du patient."
              action={
                <Button
                  size="sm"
                  className="w-full gap-2 sm:w-auto"
                  onClick={() => setNewPlanRequest((n) => n + 1)}
                >
                  <Plus className="h-4 w-4" />
                  Nouveau plan
                </Button>
              }
            >
                {/*
                  ⚠️ **`onChanged` was missing here, and the Factures tab three panels down carries a comment
                  describing this exact bug being fixed for `InvoicesTable`.** Creating a devis, deleting a
                  brouillon or booking the next séance all fire `afterMutation()`, so without it
                  `PatientPlansStrip`, the odontogramme's `teethUnderTreatment` rings, « Solde dû » and
                  « Reste à payer » all kept their pre-mutation figures. Realtime masks it whenever the hub
                  answers — and `use-clinic-realtime` is explicit that realtime is *additive*, so on a LAN
                  install with the hub down the band silently states the old numbers.
                */}
                <TreatmentPlansTable
                  patientId={patientId}
                  patientName={patientName}
                  showPatientColumn={false}
                  hideToolbar
                  createRequest={newPlanRequest}
                  onChanged={() => setRefreshKey((k) => k + 1)}
                />
            </PatientTabSection>
          </TabsContent>

          {/* Documents Tab — saved medical documents; reopen the editor to edit / reprint. */}
          <TabsContent value="documents">
            <PatientTabSection
              icon={FileText}
              title="Documents médicaux"
              description="Tous les documents enregistrés pour ce patient. Cliquez sur « Ouvrir » pour modifier ou réimprimer."
              /* P2-A: prescribe for the open patient without leaving the page / re-searching them. */
              action={documentTemplateActions}
            >
                {medicalDocuments.length === 0 ? (
                  renderSectionEmpty(
                    ["documents"],
                    <EmptyState
                      icon={FileText}
                      size="compact"
                      chipClassName={zoneChipClass(ZONES.daily)}
                      title="Aucun document enregistré"
                      description="Ordonnances, certificats et bulletins CNAM apparaîtront ici."
                      action={documentTemplateActions}
                    />,
                  )
                ) : (
                  <>
                    {/* Tapping the card opens the document — « Ouvrir » is what the row already did, so the menu
                        exists only for the destructive second action and is omitted when the user cannot delete. */}
                    <CardList
                      className={CARDS_ONLY_LG}
                      ariaLabel="Documents médicaux"
                      items={documentsPage.items}
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
                      /*
                        ⚠️ **The whole menu is gated, and that is a REVERSAL of the note that stood here — read
                        this before putting the old shape back.** The gate used to wrap the `DropdownMenu` and
                        was moved onto the delete item because wrapping it lost « Ouvrir le document » for a
                        secretary. That reasoning is now obsolete: the card carries `onSelect`, so tapping it
                        opens the document for everyone. What the per-item gate left behind was a « ⋯ » whose
                        only entry, for reception, was « Ouvrir le document » — one tap deeper than the tap that
                        already does it. A menu with a single item duplicating its own card is a dead control.
                        The capability is untouched either way (§ 0), and the table tree agrees: there a
                        secretary sees « Ouvrir » and no delete.
                      */
                      actions={(doc) => (!canDeleteClinicalRecords ? null : (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button
                              variant="ghost"
                              size="icon"
                              aria-label={`Actions du document ${documentTypeLabel(doc.documentType)} du ${formatDate(doc.documentDate)}`}
                            >
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
                      ))}
                    />
                    {/*
                      ⚠️ **`_LG`, at four columns, and that is a DELIBERATE step past `frontend-web.md` § 6's
                      threshold** — read this before putting `TABLE_ONLY` back on the strength of the rule.

                      That rule reads « `md:` for four columns or fewer, `lg:` from five up », and its own
                      measurement table records this table as the one that fits exactly (451 px in a 451 px box
                      at 820×1024). The threshold is a **floor** — five columns *must* go to `lg:` or the
                      Actions column falls outside the scrollport — and nothing about it obliges a smaller
                      table to hinge earlier than its neighbours.

                      Hinging earlier is precisely what it did, and it was the loudest inconsistency on this
                      page. Every other panel here is `_LG`, so on an iPad in portrait — the device this
                      product is used on most — five tabs rendered a card list and this one rendered a grid.
                      Swiping between two tabs changed the *form* of the content, which reads as two screens
                      bolted together. Reported as « tables are not same, not same ui, so it feels unrelated ».

                      Nothing is lost (§ 0): the card tree above carries the same « Ouvrir » (the card's own
                      tap) and the same « Supprimer » menu item, gated identically.
                    */}
                    <Table containerClassName={TABLE_ONLY_LG}>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Type</TableHead>
                          <TableHead>Date</TableHead>
                          <TableHead>Séance</TableHead>
                          <TableHead className="text-right">Actions</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {documentsPage.items.map((doc) => (
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
                              {/*
                                ⚠️ **One frequent action inline, the destructive one behind « ⋯ » — the same
                                shape « Actes dentaires » carries, and this row is why that fix had to
                                propagate.** « Ouvrir » and « Supprimer » were two labelled buttons side by
                                side (with no `gap` at all at first, since JSX strips the whitespace-only line
                                between siblings), the destructive one second, on the tab a secretary opens to
                                reprint an ordonnance. That is NN/g's « Consequential Options Close to Benign
                                Options » — named in as many words in the fiches table's own comment one tab
                                over, and fixed only there. Behind the menu it keeps its full name and cannot
                                be hit by a slip aimed at « Ouvrir ».

                                The control count does not change, and neither does the capability (§ 0).
                              */}
                              <div className="flex items-center justify-end gap-2">
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="gap-1 coarse:h-11"
                                  onClick={() => openMedicalDocument(doc)}
                                  aria-label={`Ouvrir le document ${documentTypeLabel(doc.documentType)} du ${formatDate(doc.documentDate)}`}
                                >
                                  <Eye className="h-4 w-4" />
                                  Ouvrir
                                </Button>
                                {canDeleteClinicalRecords && (
                                  <DropdownMenu>
                                    <DropdownMenuTrigger asChild>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        className="h-8 w-8 p-0 coarse:size-11"
                                        aria-label={`Autres actions sur le document ${documentTypeLabel(doc.documentType)} du ${formatDate(doc.documentDate)}`}
                                      >
                                        <MoreHorizontal className="h-4 w-4" />
                                      </Button>
                                    </DropdownMenuTrigger>
                                    <DropdownMenuContent align="end">
                                      <DropdownMenuItem
                                        className="text-destructive focus:text-destructive"
                                        onSelect={() => setDocumentToDelete(doc)}
                                      >
                                        Supprimer le document du {formatDate(doc.documentDate)}
                                      </DropdownMenuItem>
                                    </DropdownMenuContent>
                                  </DropdownMenu>
                                )}
                              </div>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                    <DataTablePagination
                      page={documentsPage}
                      onPageChange={setDocumentsPageRequest}
                      label={["document", "documents"]}
                    />
                  </>
                )}
            </PatientTabSection>
          </TabsContent>

          {/* Appointments Tab - Merged with Procedures */}
          <TabsContent value="appointments">
            <PatientTabSection
              icon={Calendar}
              title="Historique des rendez-vous"
              description="Historique complet des rendez-vous et des actes"
            >
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
                      className={CARDS_ONLY_LG}
                      ariaLabel="Historique des rendez-vous"
                      items={appointmentsPage.items}
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
                    <Table containerClassName={TABLE_ONLY_LG}>
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
                        {appointmentsPage.items
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
                            {/* ⚠️ `clamp`, never `truncate`: § 6 forbids truncation in a cell, and the card
                                field directly above already carries the comment diagnosing this exact defect
                                (« the table clipped it behind a hover-only `title=`, which no touch device can
                                reach, and a visit note is read at the chair ») while fixing only the card. This
                                table renders from 1024 px — an iPad landscape is 1180 px, with a finger. */}
                            <TableCell className="max-w-xs" clamp title={appointment.notes ?? undefined}>
                              {appointment.notes ? (
                                <p className="text-sm">{appointment.notes}</p>
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
                    {/*
                      The bound this tab never had. Unlike the fiches' pager this one DOES offer the size
                      selector: its page size is 10, which is `PAGE_SIZE_OPTIONS`' first step, so the `<Select>`
                      has a value that matches one of its items. Below eleven visits the pager hides itself
                      entirely, which is most patients.
                    */}
                    <DataTablePagination
                      page={appointmentsPage}
                      onPageChange={setAppointmentsPageRequest}
                      onPageSizeChange={(size) => {
                        setAppointmentsPageSize(size)
                        setAppointmentsPageRequest(1)
                      }}
                      label={["rendez-vous", "rendez-vous"]}
                    />
                  </>
                )}
            </PatientTabSection>
          </TabsContent>

          {/* Files Tab */}
          <TabsContent value="files">
            <PatientTabSection
              icon={FolderOpen}
              title={currentFolder ? currentFolder.name : "Fichiers du patient"}
              /*
                ⚠️ **« Tous les fichiers … (N) » counted only the ROOT, and said « tous ».**
                `GET /patients/{id}/files` with no `folderId` filters on `FolderId == null`, so a patient whose
                scanners are filed in « Radiographies » read « (0 fichier) » directly above a folder card
                announcing « 12 fichiers ». The count names its own scope, and the folders are counted beside it
                so the root never reads as an empty drawer.

                ⚠️ It is the one panel description that is a **count** rather than a sentence, and that is the
                point of it — it is the only one whose subject changes as you navigate into a folder.
              */
              description={
                currentFolder
                  ? countLabel(currentFiles.length, "fichier", "fichiers", "dans ce dossier")
                  : [
                      countLabel(folders.length, "dossier", "dossiers"),
                      countLabel(currentFiles.length, "fichier", "fichiers", "à la racine"),
                    ].join(" · ")
              }
              /* The only panel with TWO actions — `PatientTabSection` gives the pair the full width below
                 `sm:` and they share it. */
              action={
                <>
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
                    onClick={() =>
                      router.push(
                        currentFolderId
                          ? `/patients/${patientId}/files?folder=${encodeURIComponent(currentFolderId)}`
                          : `/patients/${patientId}/files`,
                      )
                    }
                    variant="default"
                    className="flex-1 sm:flex-none"
                  >
                    Gérer les fichiers
                  </Button>
                </>
              }
            >
                {/*
                  ⚠️ **The failure branch is tested BEFORE emptiness, and that ordering is the whole fix.**
                  `renderSectionEmpty` already ranks loading → failed → empty correctly, but it was only reached
                  when `files.length === 0 && folders.length === 0` — and inside a folder there is a folder by
                  construction, so a patient with any folder at all could never reach it. A folder of radiographs
                  the server refused to list rendered as « Aucun fichier dans ce dossier »: the toast expires in
                  four seconds and the sentence stays. The `.catch(() => [])` was removed and the flag recorded
                  (see the read's own note); nothing rendered the flag.
                */}
                {detailsLoading || sectionFailed("files", "folders") ? (
                  renderSectionEmpty(["files", "folders"], null)
                ) : files.length === 0 && folders.length === 0 ? (
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
                            /*
                              ⚠️ A folder card is the ONLY route this tab has to a filed file, so a bare
                              `onClick` closed the whole branch to a keyboard or a screen reader — not a
                              discoverability nuisance, an unreachable capability (§ 0). The four pieces § 13
                              requires are `role`, `tabIndex`, Enter/Space and a name; the same control in
                              `patient-files-manager.tsx` already carried all four, which is why the helpers now
                              live in `lib/a11y.ts` rather than being written twice.
                            */
                            <Card
                              key={folder.id}
                              role="button"
                              tabIndex={0}
                              aria-label={`Ouvrir le dossier ${folder.name}`}
                              className={cn(
                                "p-3 cursor-pointer hover:bg-accent transition-colors hover:border-primary/40",
                                FOCUS_CLASSES,
                              )}
                              onClick={() => setCurrentFolderId(folder.id)}
                              onKeyDown={activateOnKey(() => setCurrentFolderId(folder.id))}
                            >
                              <div className="flex items-center justify-between">
                                <div className="flex items-center gap-3 flex-1 min-w-0">
                                  <div className="p-2 rounded-lg bg-accent/30">
                                    <Folder className="h-5 w-5 text-primary" />
                                  </div>
                                  <div className="flex-1 min-w-0">
                                    <p className="text-sm font-semibold truncate text-foreground">{folder.name}</p>
                                    <p className="text-xs text-muted-foreground">
                                      {countLabel(folder.fileCount, "fichier", "fichiers")}
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
                          {currentFolder ? `Fichiers — ${currentFolder.name}` : "Fichiers"}
                        </h3>
                        {/*
                          ⚠️ `EmptyState`, not a hand-rolled dashed Card with a 48 px icon at 50 % opacity.
                          This was the one empty state on the page that did not come from the shared
                          primitive — five panels used `EmptyState size="compact"` with the zone chip and this
                          one drew its own, twice the height, in a different type size, with no zone hue. Two
                          empty tabs looking like two different products is exactly the « feels unrelated »
                          complaint, and it lands hardest here because an empty drawer is what a new patient
                          shows.

                          It is an *invite*, so it carries the action that creates the first record (§ 13's
                          three kinds) — the branch above it already holds the failed-read case.
                        */}
                        <EmptyState
                          icon={FolderOpen}
                          size="compact"
                          chipClassName={zoneChipClass(ZONES.daily)}
                          title={currentFolderId ? "Aucun fichier dans ce dossier" : "Aucun fichier à la racine"}
                          description={
                            currentFolderId
                              ? "Téléversez une radiographie ou un scan dans ce dossier."
                              : "Les fichiers non classés apparaissent ici."
                          }
                          action={
                            <Button
                              variant="outline"
                              onClick={() =>
                                router.push(
                                  currentFolderId
                                    ? `/patients/${patientId}/files?folder=${encodeURIComponent(currentFolderId)}`
                                    : `/patients/${patientId}/files`,
                                )
                              }
                            >
                              Téléverser des fichiers
                            </Button>
                          }
                        />
                      </div>
                    ) : (
                      <div>
                        <h3 className="text-sm font-semibold mb-3 text-foreground">
                          {currentFolder ? `Fichiers — ${currentFolder.name}` : "Fichiers"}
                        </h3>
                        {/* AC-17's truncate case. The name is the title, so it truncates to one line and the
                            whole value is reachable by tapping the card — which opens the preview. The table's
                            `title=` tooltip did the same job on a desktop and nothing at all on a phone. */}
                        <CardList
                            className={CARDS_ONLY_LG}
                            ariaLabel={currentFolder ? `Fichiers du dossier ${currentFolder.name}` : "Fichiers du patient"}
                            items={filesPage.items}
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
                            /*
                              ⚠️ **« Aperçu du fichier » is gone from this menu, and the reasoning is the
                              Documents card's, one tab over.** The card carries `onSelect`, so tapping it
                              already opens the preview — the menu item was a second, slower route to the
                              gesture the card itself performs. That note ends « A menu with a single item
                              duplicating its own card is a dead control »; here it was the first of two,
                              which is the same defect with the evidence one line further away.

                              The menu stays because « Télécharger » has no other route on a card (§ 0).
                            */
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
                                  <DropdownMenuItem onSelect={() => handleDownloadFile(file)}>
                                    Télécharger le fichier
                                  </DropdownMenuItem>
                                </DropdownMenuContent>
                              </DropdownMenu>
                            )}
                          />
                          <Table containerClassName={TABLE_ONLY_LG}>
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
                              {filesPage.items
                                .map((file) => {
                                  const isImage = isImageFile(file)
                                  const isPreviewable = isPreviewableFile(file, filesPolicy)

                                  return (
                                    <TableRow 
                                      key={file.id}
                                      className="cursor-pointer hover:bg-muted/50"
                                      onClick={() => preview.open(file)}
                                    >
                                      <TableCell className="font-medium">
                                        <div className="flex items-center gap-2">
                                          {/* The PDF arm and the fallback arm drew the same icon, so `isPdf`
                                              decided nothing here and could never render differently. */}
                                          {isImage ? (
                                            <ImageIcon className="h-4 w-4 text-muted-foreground" />
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
                                        {/*
                                          ⚠️ **`coarse:size-11` on BOTH, not `.touch-target` on either.**
                                          `buttonVariants` already centres a 44 px overlay on every Button, so
                                          two 32 px controls 8 px apart overhang each other by ~6 px — and the
                                          later sibling paints last, so a thumb aimed at the right edge of
                                          « Aperçu » fired « Télécharger ». This table is `TABLE_ONLY_LG`, i.e.
                                          it renders from 1024 px, which is an iPad in landscape at 1180 px with
                                          a gloved hand. Growing the painted box is the fix `patients-table.tsx`
                                          and `patient-files-manager.tsx` both already carry.
                                        */}
                                        <div className="flex items-center justify-end gap-2">
                                          {isPreviewable && (
                                            <Button
                                              variant="ghost"
                                              size="sm"
                                              className="h-8 w-8 p-0 coarse:size-11"
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
                                            className="h-8 w-8 p-0 coarse:size-11"
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
                          <DataTablePagination
                            page={filesPage}
                            onPageChange={setFilesPageRequest}
                            label={["fichier", "fichiers"]}
                          />
                      </div>
                    )}
                  </div>
                )}
            </PatientTabSection>
          </TabsContent>

          {/* Factures Tab */}
          <TabsContent value="factures" className="space-y-4">
            <PatientTabSection
              icon={Receipt}
              title="Factures"
              description="Notes d'honoraires du patient — création, émission, paiement et PDF."
              action={
                <Button
                  size="sm"
                  className="w-full gap-2 sm:w-auto"
                  onClick={() => setNewInvoiceRequest((n) => n + 1)}
                >
                  <Plus className="h-4 w-4" />
                  Nouvelle facture
                </Button>
              }
            >
                {/* onChanged was missing: recording a payment here left the plan card above showing the
                    pre-payment figures until a manual refresh. */}
                <InvoicesTable
                  patientId={patientId}
                  patientName={patientName}
                  showPatientColumn={false}
                  hideToolbar
                  createRequest={newInvoiceRequest}
                  onChanged={() => setRefreshKey((k) => k + 1)}
                />
            </PatientTabSection>
          </TabsContent>

        </Tabs>

        {/*
          Two cards, two columns — it declared **three** while a third card (« Informations administratives »,
          the retired insurance block) filled the last one, so removing that card left a whole empty column on
          every desktop and two very tall, very narrow stacks beside it.

          Each card then lays its own fields out 2-up from `sm:`, which is what turns the width back into
          something read rather than scrolled: « Informations personnelles » was nine rows in one column.
        */}
        <div className="grid gap-6 lg:grid-cols-2">
          {/* Personal Information */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2 text-base">
                <User className="h-5 w-5 text-muted-foreground" />
                Informations personnelles
              </CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                <RecordField label="Nom complet" value={patientName} />
                {/* The reason the patient is on the books at all — beside their name above, and here in the
                    record. `wide`, because it is a sentence rather than a field. */}
                <RecordField label="Motif de consultation" value={patient.consultationReason} wide omitWhenEmpty />
                <RecordField
                  label="Date de naissance"
                  value={`${formatDate(patient.dateOfBirth)} ${age !== null ? `(${age} ans)` : "(âge inconnu)"}`}
                />
                <RecordField label="Sexe" value={genderLabel(patient.gender)} />
                <RecordField
                  label="Téléphone"
                  value={patient.phoneNumber}
                  /* The blank alone reads as "nobody typed it in yet". What matters is that this patient
                     is silently excluded from every automated contact. */
                  hint={
                    !patient.phoneNumber && (
                      <p className="mt-0.5 text-xs text-amber-700 dark:text-amber-400">
                        Ni rappel ni relance ne peuvent lui être envoyés.
                      </p>
                    )
                  }
                />
                {/* The denture was stored, drove every chart, and appeared NOWHERE on the patient's own file.
                    The record card is where a stored fact nothing else prints belongs. Full label
                    (« Denture mixte »), not the form control's short caption: here there is no group heading to
                    borrow the noun from. */}
                <RecordField label="Denture" value={dentitionLabel(patient.dentition)} />
                <RecordField label="E-mail" value={patient.email} omitWhenEmpty />
                <RecordField label="Adresse" value={formatAddress(patient.address)} wide omitWhenEmpty />
                <RecordField label="Adressé par" value={patient.referredBy} omitWhenEmpty />
                {/* ⚠️ « Contact d'urgence » is gone from this card because it is gone from the form. The two
                    columns are still on the record and still populated for older patients — nothing has been
                    dropped from the database — but a field nobody can edit any more has no business being the
                    last thing this card shows. */}
              </dl>
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
            <CardContent>
              <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                {/* Half width so it pairs with « Tabac » rather than leaving it alone on its own row. It is
                    free text and can run long, which `whitespace-pre-wrap` + the field's own
                    `[overflow-wrap:anywhere]` handle inside the column. */}
                {/* « Maladies », not « Maladies chroniques / affections » — one column carried three names
                    across the product (this one, « Antécédents » in the shared alert panel, and a real
                    « Antécédents médicaux » list from a different table a few centimetres below). */}
                <RecordField label="Maladies">
                  {diseasesList.length > 0 ? <HealthItems items={diseasesList} /> : null}
                </RecordField>
                {/*
                  « Tabac » belongs in this card and had no home in it — the modal records it under Informations
                  médicales, the strip above states it, and the record card that lists every other medical fact
                  about the patient omitted it. That gap is how a field becomes invisible on the one screen
                  somebody opens to read the whole file.

                  Every answer shows here, not only a current smoker's: the amber in the strip and the alert
                  panel is the *warning*, this is the *record*, and « Non-fumeur » is worth reading.
                */}
                <RecordField label="Tabac" value={tobaccoSummary(patient.tobaccoUse)} />
                {/* « Médicaments » — the third of the three lists, `wide` because a posology runs long and a
                    half-width column wraps « Kardégic 75 mg — 1/j » onto three lines. */}
                <RecordField label="Médicaments" wide>
                  {medicationsList.length > 0 ? <HealthItems items={medicationsList} /> : null}
                </RecordField>
                <div className="min-w-0 sm:col-span-2">
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
                <div className="min-w-0 sm:col-span-2">
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
                <div className="min-w-0 sm:col-span-2">
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
              </dl>
            </CardContent>
          </Card>

        </div>

      </AppShell>

      <EditPatientDialog
        open={editDialogOpen}
        focusSection={editSection}
        onOpenChange={(open) => {
          setEditDialogOpen(open)
          // Cleared on close, never on open: leaving it set would send the NEXT plain « Modifier » to whichever
          // panel was used last.
          if (!open) setEditSection(null)
        }}
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
        The two payment dialogs « Reste à payer » opens — mounted here at page level, as siblings of every other
        dialog on this page.

        ⚠️ **The same two components `/factures` and the devis workspace use**, not copies: a payment recorded
        from the patient file goes through `RecordPaymentCommand` / `RecordInstallmentPaymentCommand` like any
        other, so it reaches la caisse, « Créances », the dashboard and its own reçu without this page knowing
        anything about them. They also already carry the parts that are easy to lose — `useDirtyGuard` on money
        being typed, `parseAmountInput`, `todayLocalIso`, the cheque sub-form behind one shared payload builder,
        and the receipt offered in the success toast.

        ⚠️ Mounted at page level and **never inside the band**: `PatientOutstandingStrip` is a plain section, and
        putting a `Dialog` inside a list row is how a focus trap ends up nested in whatever the row is sitting in.
      */}
      <PaymentModal
        open={paymentInvoice !== null}
        onOpenChange={(open) => { if (!open) setPaymentInvoice(null) }}
        invoice={paymentInvoice}
        onSuccess={() => setRefreshKey((k) => k + 1)}
      />
      <InstallmentPaymentModal
        open={paymentInstallment !== null}
        onOpenChange={(open) => { if (!open) setPaymentInstallment(null) }}
        planId={paymentInstallment?.planId ?? null}
        installment={paymentInstallment?.installment ?? null}
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
      {/*
        The document read surface. `onEditInFiche` is what makes « Modifier » safe: a document a fiche owns is
        recomposed from the section on that fiche's next save, so editing it in the standalone editor is work
        that gets silently overwritten — the dialog sends you to the fiche instead.
      */}
      <DocumentPreviewDialog
        target={previewTarget}
        onClose={() => setPreviewTarget(null)}
        onEditInFiche={(dentalRecordId) => {
          const owner = dentalRecords.find((r) => r.id === dentalRecordId)
          setPreviewTarget(null)
          if (!owner) {
            // The fiche is not on the page (a filter, or it has since been deleted). Say so rather than
            // opening a blank « Nouvelle fiche », which would invite recording the séance a second time.
            toast.error("La fiche de cette séance n'est pas dans la liste affichée.")
            return
          }
          setEditingRecord(owner)
          setRecordModalOpen(true)
        }}
        onEditInEditor={(doc) => {
          setPreviewTarget(null)
          openMedicalDocument(doc)
        }}
        patientPhone={patient?.phoneNumber ?? null}
      />

      <FilePreviewDialog
        preview={preview}
        patientId={patientId}
        onDownload={(file) => void handleDownloadFile(file)}
      />
    </ClinicGuard>
  )
}


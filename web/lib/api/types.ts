/** One received lot of a stock item (AC-P4.3), soonest-expiry first in `StockItemDto.batches`. */
export interface StockBatchDto {
  id: string;
  receivedQuantity: number;
  remainingQuantity: number;
  expiryDate?: string | null;
  batchNumber?: string | null;
  receivedAt: string;
  /** At or past its expiry date, and still holding stock. */
  isExpired: boolean;
}

export interface StockItemDto {
  id: string;
  name: string;
  description?: string | null;
  category: string;
  unit: string;
  currentStock: number;
  minimumStockLevel: number;
  maximumStockLevel: number;
  unitPrice?: number | null;
  supplier?: string | null;
  isLowStock: boolean;
  /**
   * The lots on the shelf (AC-P4.1/4.3). Replaces the single scalar expiry the item used to carry, which
   * showed whatever arrived LAST rather than the date that actually matters.
   */
  batches: StockBatchDto[];
  /** The soonest expiry still holding stock — the lot that is actually expiring (AC-P4.3). */
  earliestExpiry?: string | null;
  /** A lot on the shelf is at or past its expiry (AC-P4.5). Drives the row highlight. */
  hasExpiredStock: boolean;
  /** A lot expires inside the clinic's configured lead time (AC-P4.6). */
  isExpiringSoon: boolean;
  /** Concurrency token, echoed back on update so a concurrent consume 409s (AC-P4.18). */
  version: number;
  createdAt: string;
  updatedAt?: string;
}

export interface NotificationDto {
  id: string;
  /** AppointmentCreated | AppointmentCancelled | AppointmentRescheduled | Reminder | LowStock */
  category: string;
  title: string;
  message: string;
  /** Effective feed time (ISO) — creation time for immediate categories, due time for reminders. */
  createdAt: string;
  isRead: boolean;
  /** Appointment | StockItem */
  targetKind: string;
  appointmentId?: string | null;
  stockItemId?: string | null;
}

/** A due, unread post-visit review surfaced by the popup (GET /notifications/pending-reviews). */
export interface PendingReviewDto {
  id: string;
  title: string;
  message: string;
  appointmentId?: string | null;
  /** Effective feed time (ISO) — the appointment end, i.e. when the review became due. */
  visibleAt: string;
}

export interface DashboardStats {
  todaysAppointments: number;
  totalPatients: number;
  upcomingPending: number;
  thisWeekAppointments: number;
  urgentPatients: number;
  /** Total collected (encaissé) in the current month, in TND — includes invoice + installment payments. */
  monthlyRevenueCollected: number;
  /** Total outstanding across the clinic (invoice + installment balances), in TND. */
  totalOutstanding: number;
}

export interface InvoiceLineDto {
  id: string;
  designation: string;
  quantity: number;
  unitPriceHt: number;
  lineTotalHt: number;
  dentalRecordId?: string | null;
  /** Optional catalog CNAM/DCH act this line bills (drives the reimbursable split); null = free text. */
  dentalActCodeId?: string | null;
  codeActe?: string | null;
}

/** The unified per-patient balance (« Solde patient ») across invoices + installments, plus the CNAM split. */
export interface PatientBillingSummaryDto {
  invoiceOutstanding: number;
  installmentOutstanding: number;
  totalOutstanding: number;
  oldestOverdueDate: string | null;
  cnamReimbursable: number;
  patientOutOfPocket: number;
  /**
   * Total refunded to this patient through avoirs. Informational — an avoir returns the cash *and* cancels
   * the fee, so it does not move `totalOutstanding`.
   */
  creditedTotal: number;
}

/** One row of the clinic-wide « Créances » (accounts-receivable) list. */
export interface ReceivableDto {
  patientId: string;
  patientName: string;
  totalOutstanding: number;
  oldestOverdueDate: string | null;
  daysOverdue: number | null;
}

export interface PaymentDto {
  id: string;
  amount: number;
  /** Cash | Cheque | Card | Transfer */
  method: string;
  paidOn: string;
  createdAt: string;
  /**
   * A voided payment was never really received. The row is kept and shown struck through with its motif, so
   * the correction leaves a trail rather than silently disappearing. Voided payments are excluded from every
   * cash read server-side.
   */
  isVoided: boolean;
  voidedAt?: string | null;
  voidReason?: string | null;
  voidedByName?: string | null;
  /** Set when this payment was carried onto the invoice from a treatment-plan installment. */
  sourceInstallmentPaymentId?: string | null;
}

export interface InvoiceDto {
  /**
   * Optimistic-concurrency token (PostgreSQL `xmin`). Send it back on the matching update so the save is
   * checked against the copy this user was shown; a peer's change in between then yields a 409 instead of
   * silently discarding their work.
   */
  version: number;
  id: string;
  patientId: string;
  patientName?: string | null;
  dentalRecordId?: string | null;
  appointmentId?: string | null;
  /** The devis this note was bridged from (devis→facture), or null for a standalone note. */
  treatmentPlanId?: string | null;
  number?: string | null;
  issueDate?: string | null;
  /** Draft | Issued | PartiallyPaid | Paid | Cancelled */
  status: string;
  vatApplicable: boolean;
  vatRate: number;
  stampDutyAmount: number;
  cancellationReason?: string | null;
  totalHt: number;
  totalVat: number;
  totalTtc: number;
  amountCollected: number;
  outstanding: number;
  createdAt: string;
  updatedAt?: string | null;
  // TTN « El Fatoora » e-invoicing state.
  /** NotSubmitted | Queued | Signed | Submitted | Validating | Valid | Rejected | Failed */
  eInvoiceStatus: string;
  ttnIdentifier?: string | null;
  eInvoiceSubmittedAt?: string | null;
  eInvoiceValidatedAt?: string | null;
  eInvoiceLastError?: string | null;
  eInvoiceAttemptCount: number;
  canSubmitToElFatoora: boolean;
  /**
   * Server-computed. Do NOT re-derive these from status + amountCollected: that is exactly how the table
   * ended up offering « Annuler » on invoices the API refuses — after a full void the status is Issued and
   * collected is 0, but the voided payment rows are still there.
   */
  canCancel: boolean;
  canCreateAvoir: boolean;
  hasSignedXml: boolean;
  hasTtnReceipt: boolean;
  /**
   * Sum of the avoirs established against this invoice — always present, 0 when there are none. Present on
   * the list too, so a row can show that money was credited back without fetching the avoirs themselves.
   */
  creditedTotal: number;
  lines: InvoiceLineDto[];
  payments: PaymentDto[];
  /** Only populated by `invoicesApi.get` (the detail modal); the list carries `creditedTotal` alone. */
  creditNotes: CreditNoteDto[];
}

/** An avoir: the lawful correction for cash already collected on an invoice. */
export interface CreditNoteDto {
  id: string;
  invoiceId: string;
  /** Own per-clinic-per-year sequence, AAAA-NNNN — not the invoice's. */
  number: string;
  issueDate: string;
  amount: number;
  reason: string;
  /** Cash | Cheque | Card | Transfer, or null when the means of refund was not recorded. */
  method?: string | null;
  /** When the money went back — the date la caisse nets it against. */
  refundedOn: string;
  /**
   * The corrected invoice is registered with TTN « El Fatoora ». The avoir is not transmitted, so the
   * régularisation with TTN is still the clinic's to do.
   */
  correctedInvoiceIsTtnRegistered: boolean;
}

export interface InvoiceRevenueDto {
  totalInvoiced: number;
  totalCollected: number;
  outstanding: number;
}

export interface AppointmentDto {
  /**
   * Optimistic-concurrency token (PostgreSQL `xmin`). Send it back on the matching update so the save is
   * checked against the copy this user was shown; a peer's change in between then yields a 409 instead of
   * silently discarding their work.
   */
  version: number;
  id: string;
  patientId: string | null;
  patientName: string;
  /** The practitioner (Doctor) this appointment is booked with (FK; null = unassigned). */
  doctorId?: string | null;
  appointmentDateTime: string;
  duration: string; // TimeSpan format from backend (e.g., "00:30:00")
  doctorName?: string;
  notes?: string;
  status: string;
  /**
   * The statuses this appointment may legally move to, from the domain's transition table (AC-P1.6). The status
   * Select offers exactly these, and « Annuler » derives its disabled state from whether `Cancelled` is present
   * — instead of the client re-deriving rules that could disagree with the server (and did: the button was
   * disabled on a completed appointment, which is now a legal cancellation).
   */
  allowedNextStatuses?: string[];
  createdAt: string;
  procedureTypeId?: string;
  procedureTypeName?: string;
  procedureColorHex?: string;
  /** The treatment-plan step this appointment schedules, if any. */
  treatmentPlanItemId?: string | null;
  /** True when the appointment is reflected in Google Calendar (derived server-side from the event id). */
  isSyncedToGoogle: boolean;
}

// Optional CNAM identity block on a patient (spec AC-1). Every field is optional.
export interface CnamInfo {
  identifiantUnique?: string | null;
  regime?: string | null;
  assureFirstName?: string | null;
  assureLastName?: string | null;
  assureAddress?: string | null;
  assurePostalCode?: string | null;
  maladeLien?: string | null;
  maladeLienRang?: string | null;
}

// A CNAM dental nomenclature entry (DB-backed, global reference data from GET /api/cnam-nomenclature).
// Used by the bulletin editor to fill Code acte + Cotation and compute an indicative estimate, and by the
// admin catalog screen. Writes are admin-only.
export interface CnamNomenclatureEntryDto {
  id: string;
  codeActe: string;
  designationFr: string;
  lettreCle: string;
  coefficient: number;
  category: string;
  isActive: boolean;
  isProvisional: boolean;
}

// A valeur de la lettre clé (VLC) — the dinar value per lettre clé used in the reimbursement estimate.
export interface CnamLetterValueDto {
  id: string;
  lettreCle: string;
  value: number;
  isProvisional: boolean;
}

// A medication catalog entry (DB-backed, global reference data from GET /api/medications). Used by the
// ordonnance editor to pick a drug (fills the line name + snapshots the DCI molecules onto it) and by the
// admin catalog screen. Writes are admin-only. `dcis` holds the active ingredient molecules (one or more).
export interface MedicationDto {
  id: string;
  brandName: string;
  form: string;
  strength: string;
  dcis: string[];
  isActive: boolean;
  isProvisional: boolean;
}

export interface PatientDto {
  /**
   * Optimistic-concurrency token (PostgreSQL `xmin`). Send it back on the matching update so the save is
   * checked against the copy this user was shown; a peer's change in between then yields a 409 instead of
   * silently discarding their work.
   */
  version: number;
  id: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  /** Null when the patient gave none — never a placeholder address. */
  email?: string | null;
  /** Null when the patient gave none. Such a patient receives no reminder and no relance. */
  phoneNumber?: string | null;
  medicalHistory?: string;
  allergies?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
  address?: {
    street: string;
    city: string;
    state: string;
    zipCode: string;
    country: string;
  };
  insuranceInfo?: {
    provider: string;
    policyNumber: string;
    groupNumber?: string;
    expiryDate?: string;
  };
  cnamInfo?: CnamInfo | null;
  flags?: Array<{
    id: string;
    flagType: string;
    description: string;
    notes?: string;
    isActive: boolean;
  }>;
  /**
   * Archived patients are hidden from lists, search, recall and every picker, but keep every record and stay
   * reachable by direct URL — so a detail page that loads one must be able to say so.
   */
  isArchived: boolean;
  archivedAt?: string | null;
  archiveReason?: string | null;
  createdAt: string;
}

/** What blocks a patient's deletion, read when the confirm dialog opens rather than after clicking. */
export interface PatientDeletionCheckDto {
  patientId: string;
  patientName: string;
  canDelete: boolean;
  isArchived: boolean;
  canArchive: boolean;
  /** French, ready to display. Null when archiving is available. */
  archiveBlockedReason?: string | null;
  blockers: PatientDeletionBlockerDto[];
}

export interface PatientDeletionBlockerDto {
  /** Stable machine key (e.g. `invoices`) — key off this, never the label. */
  kind: string;
  /** French, already pluralised for `count` (e.g. « factures »). */
  label: string;
  count: number;
  /** Patient-detail tab this record kind lives on, so the dialog can link to it. */
  tab?: string | null;
}

export interface PatientMedicalHistoryDto {
  id: string;
  patientId: string;
  description: string;
  date?: string;
  notes?: string;
  createdAt: string;
}

export interface PatientFamilyHistoryDto {
  id: string;
  patientId: string;
  relationship: string;
  condition: string;
  notes?: string;
  createdAt: string;
}

export interface ProcedureTypeDto {
  id: string;
  name: string;
  defaultDurationMinutes: number;
  defaultCost?: number;
  colorHex: string;
  description?: string;
  /** ToothCondition name this procedure produces on the odontogram, or null. */
  resultingCondition?: string | null;
  isActive: boolean;
  /**
   * AC-P4.9/4.14 — the stock this act consumes each time it is performed. Empty means the act has opted out
   * and consumes nothing (AC-P4.11), which is the default.
   */
  materials: ProcedureTypeMaterialDto[];
  createdAt: string;
  updatedAt?: string;
}

/** One line of an act's material list. */
export interface ProcedureTypeMaterialDto {
  stockItemId: string;
  quantityPerAct: number;
}

// A single act line on a dental record. A record can carry many acts.
export interface DentalRecordActDto {
  id: string;
  procedureTypeId?: string | null;
  procedureName: string;
  /** The act's total fee (authoritative). */
  cost: number;
  /** Per-unit price `cost` was built from; null when never captured (records created before per-tooth pricing). */
  unitCost?: number | null;
  /** True when `cost` is `unitCost` × treated teeth; false = flat session fee. */
  isPerTooth: boolean;
  toothNumbers: number[];
  /** ToothCondition name this act results in on the odontogram, or null. */
  resultingCondition?: string | null;
  surfaces?: string | null;
  note?: string | null;
}

export interface DentalRecordDto {
  /**
   * Optimistic-concurrency token (PostgreSQL `xmin`). Send it back on the matching update so the save is
   * checked against the copy this user was shown; a peer's change in between then yields a 409 instead of
   * silently discarding their work.
   */
  version: number;
  id: string;
  patientId: string;
  interventionDate: string;
  /** Derived summary string over the acts (read-only). */
  procedureType: string;
  /** Derived sum of act costs (read-only). */
  cost: number;
  amountPaid: number;
  balance: number;
  notes: string[];
  importantNotes: string[];
  isAdultTeeth: boolean;
  /** Derived union of all act teeth (read-only). */
  toothNumbers: number[];
  acts: DentalRecordActDto[];
  createdAt: string;
  updatedAt?: string;
}

// Per-act input when creating/updating a dental record (used for both create and update).
export interface DentalActInput {
  procedureTypeId?: string | null;
  procedureName: string;
  /** The act's total fee. The server stores it as sent — never recomputed from the unit price. */
  cost: number;
  /** Optional per-unit price the total was built from (pricing provenance for the editor + invoice). */
  unitCost?: number | null;
  /** Whether `cost` is per treated tooth (else a flat session fee). Ignored when the act has no teeth. */
  isPerTooth: boolean;
  toothNumbers: number[];
  resultingCondition?: string | null;
  surfaces?: string | null;
  note?: string | null;
}

// Practitioner document identity (FR-2.5 / FR-3.1): CNOMDT ordre number + whether a cachet image is on file.
export interface DoctorProfileDto {
  id: string;
  fullName: string;
  specialty: string;
  ordreNumberCnomdt?: string | null;
  hasCachet: boolean;
  cachetContentType?: string | null;
}

export interface PatientFileDto {
  id: string;
  patientId: string;
  folderId?: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  fileType: string;
  description?: string;
  uploadedAt: string;
  uploadedBy?: string;
}

export interface PatientFolderDto {
  id: string;
  patientId: string;
  parentFolderId?: string;
  name: string;
  fileCount: number;
  subFolderCount: number;
  createdAt: string;
  updatedAt?: string;
}

export interface MedicalDocumentDto {
  id: string;
  patientId: string;
  patientName: string;
  patientAge?: string;
  documentType: string;
  documentDate: string;
  recipientDoctorName?: string;
  recipientDoctorSpecialty?: string;
  contentJson: string;
  clinicName: string;
  clinicAddress: string;
  clinicPhone: string;
  doctorName: string;
  doctorSpecialty: string;
  isDraft: boolean;
  fileId?: string;
  appointmentId?: string | null;
  createdAt: string;
  updatedAt?: string;
}

// A dental act catalog entry (DB-backed reference data from GET /api/dental-acts). Used by the treatment
// plan editor to pick an act (snapshots codeActe + designationFr onto the line and prefills the fee from
// defaultFee) and by the admin catalog screen. Writes are admin-only.
export interface DentalActDto {
  id: string;
  codeActe: string;
  designationFr: string;
  lettreCle: string;
  coefficient: number | null;
  category: string;
  defaultFee: number | null;
  requiresAccordPrealable: boolean;
  isActive: boolean;
  isProvisional: boolean;
}

// A recorded tooth-condition entry on a patient's odontogram (GET /patients/{id}/odontogram). A tooth can
// have MANY entries — one per recorded treatment. `condition` is a ToothCondition enum name: Sain | Carie |
// Obturation | Couronne | TraitementDeCanal | Bridge | Implant | ExtraitAbsent | ATraiter. `surfaces` is a
// compact string like "MOD" (M/O/D/V/L). Conditions are captured when adding/editing a dental record, so
// each entry links back to its source record via `dentalRecordId`.
export interface ToothStateDto {
  id: string;
  toothNumber: number;
  condition: string;
  /** "Diagnosis" (charted directly) or "Treatment" (from a dental record). */
  source: string;
  surfaces: string | null;
  note: string | null;
  treatmentDate: string;
  dentalRecordId: string | null;
  createdAt: string;
}

// A single act line on a treatment plan / devis. Either a catalog act (dentalActCodeId + codeActe set) or
// a free-text designation. `status` is Planned | Done.
export interface TreatmentPlanItemDto {
  id: string;
  dentalActCodeId: string | null;
  codeActe: string | null;
  /**
   * The clinic's own procedure this act is performed as, when it was chosen from « Mes actes ». Drives the
   * procedure prefill when the act is booked. Null for CNAM-only lines, hand-typed lines, and any act created
   * before the column existed (those fall back to a name match).
   */
  procedureTypeId: string | null;
  designationFr: string;
  toothNumbers: number[];
  plannedCost: number;
  status: string;
  doneDate: string | null;
  linkedDentalRecordId: string | null;
  /** Clinical order within the plan (0-based). The API returns acts already sorted by it. */
  sequenceNumber: number;
  /**
   * Derived read-back (query paths only): the appointment that currently speaks for this act — the earliest
   * upcoming live one, else the most recent past live one. Null when nothing is booked, *including* when the
   * only linked appointment was cancelled or a no-show, so the act returns to « À planifier » and can be
   * booked again. See `plan-next-action.ts` for the état mapping.
   */
  scheduledAppointmentId?: string | null;
  scheduledAt?: string | null;
  scheduledAppointmentStatus?: string | null;
}

// A payment installment (échéance) on an accepted treatment plan. `lastMethod` is a PaymentMethod enum
// name: Cash | Cheque | Card | Transfer.
export interface InstallmentDto {
  id: string;
  dueDate: string;
  amount: number;
  /** Derived from the payment ledger — no longer monotonic, since a payment can be voided. */
  amountPaid: number;
  outstanding: number;
  isPaid: boolean;
  /** Derived: the most recent LIVE payment's method/date. */
  lastMethod: string | null;
  lastPaidOn: string | null;
  /** Every payment received against this échéance, each on its own date. Newest last. */
  payments: InstallmentPaymentDto[];
}

export interface InstallmentPaymentDto {
  id: string;
  amount: number;
  /** Cash | Cheque | Card | Transfer */
  method: string;
  paidOn: string;
  createdAt: string;
  /** A voided payment was never really received; the row is kept and shown struck through with its motif. */
  isVoided: boolean;
  voidedAt?: string | null;
  voidReason?: string | null;
  voidedByName?: string | null;
}

// ---- Clinical-workflow-depth DTOs ----------------------------------------------------------------

/** A clinic expense / caisse cash-out. `method` is a PaymentMethod name: Cash | Cheque | Card | Transfer. */
export interface ExpenseDto {
  id: string;
  clinicId: string;
  expenseDate: string;
  category: string;
  amount: number;
  method: string;
  description?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

/** The caisse (daily cash) summary — encaissements (cash in) minus dépenses (cash out) and the net. */
export interface CaisseSummaryDto {
  fromDate: string;
  toDate: string;
  cashIn: number;
  cashOut: number;
  net: number;
}

/** A salle-d'attente entry. `priority` is Low|Normal|High; `status` is Waiting|Promoted|Cancelled. */
export interface WaitingListEntryDto {
  id: string;
  clinicId: string;
  patientId: string;
  patientName?: string | null;
  preferredDoctorId?: string | null;
  priority: string;
  desiredTimeframe?: string | null;
  note?: string | null;
  status: string;
  resultingAppointmentId?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

/** A dental-lab work order. `status` is Sent|InProgress|Received|Fitted. */
export interface LabWorkOrderDto {
  id: string;
  clinicId: string;
  patientId: string;
  patientName?: string | null;
  toothNumber?: number | null;
  prosthetist: string;
  workDescription: string;
  sentDate?: string | null;
  expectedDate?: string | null;
  receivedDate?: string | null;
  cost?: number | null;
  status: string;
  /**
   * The stages this order may legally move to from its current one, derived server-side from
   * `LabWorkOrder`'s transition table. The status control offers exactly these, so the client never
   * re-implements the rules — and a legacy row in a state the table cannot produce simply gets an empty list
   * and no transitions offered, rather than failing to load.
   */
  allowedNextStatuses?: string[];
  notes?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

/** One patient due/overdue for a recall (« à relancer »). */
export interface RecallDto {
  patientId: string;
  patientName: string;
  phoneNumber?: string | null;
  lastVisitDate?: string | null;
  dueDate: string;
  daysOverdue: number;
  reason?: string | null;
  lastContactedAt?: string | null;
}

export interface RecallSettingsDto {
  intervalMonths: number;
}

/** A recurring appointment series template. `recurrencePattern` is Daily|Weekly|Monthly. */
export interface RecurringAppointmentDto {
  id: string;
  clinicId: string;
  patientId: string;
  patientName?: string | null;
  doctorId?: string | null;
  doctorName?: string | null;
  procedureTypeId?: string | null;
  startDate: string;
  endDate?: string | null;
  occurrenceCount?: number | null;
  recurrencePattern: string;
  interval: number;
  notes?: string | null;
  isActive: boolean;
  appointmentCount: number;
  createdAt: string;
}

/** The outcome of creating a recurring series. */
export interface RecurringSeriesResultDto {
  recurringAppointmentId: string;
  createdCount: number;
  skippedPastCount: number;
  conflicts: string[];
  /** Occurrences skipped because they fell outside the practitioner's working hours (AC-P1.28/1.36). */
  outsideWorkingHours?: string[];
}

// A treatment plan / devis for a patient. `status` is a TreatmentPlanStatus enum name: Draft | Accepted |
// InProgress | Completed | Cancelled.
export interface TreatmentPlanDto {
  /**
   * Optimistic-concurrency token (PostgreSQL `xmin`). Send it back on the matching update so the save is
   * checked against the copy this user was shown; a peer's change in between then yields a 409 instead of
   * silently discarding their work.
   */
  version: number;
  id: string;
  patientId: string;
  patientName: string | null;
  number: string | null;
  status: string;
  title: string;
  notes: string | null;
  acceptedDate: string | null;
  cancellationReason: string | null;
  totalPlanned: number;
  amountPaid: number;
  outstanding: number;
  createdAt: string;
  /** Backend sends a nullable DateTime? — null for a plan never touched since creation. */
  updatedAt?: string | null;
  /**
   * Post-acceptance amendments so far (0 = never amended). Shown as « · révision N » only when > 0, so a
   * patient holding an earlier printout can tell which version they signed.
   */
  revisionNumber: number;
  /** Derived clinical progress — always populated. */
  itemsDone: number;
  itemsTotal: number;
  /** Derived (query paths only): earliest still-upcoming séance across the plan's acts. */
  nextAppointmentAt?: string | null;
  /**
   * Derived (query paths only): the non-cancelled invoice this devis was billed into. Once set, the plan is
   * represented by that invoice in « Solde patient » — and it can no longer be re-billed.
   */
  linkedInvoiceId?: string | null;
  linkedInvoiceNumber?: string | null;
  linkedInvoiceStatus?: string | null;
  items: TreatmentPlanItemDto[];
  installments: InstallmentDto[];
}


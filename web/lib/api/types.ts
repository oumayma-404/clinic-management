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

/**
 * One page of stock items plus the three clinic-wide figures the stockroom screen shows around them.
 *
 * ⚠️ `lowStockCount`, `expiringCount` and `categories` are clinic-wide and ignore the active filters/search — they
 * are the chips telling staff how much is wrong in the stockroom. Never derive them from `items`: over a page that
 * becomes "the low-stock items among these 25" and a dropdown missing most categories.
 */
export interface StockPageDto {
  items: StockItemDto[];
  lowStockCount: number;
  expiringCount: number;
  categories: string[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
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

/** Which window the dashboard is read over. Mirrors the backend `DashboardPeriodKey`. */
export type DashboardPeriodKey = 'Today' | 'Week' | 'Month';

/**
 * One dashboard figure over the current period and the preceding equivalent one.
 *
 * `current` is null when the figure is undefined (a rate whose denominator was zero — « — », never « 0 % »).
 * `deltaPercent` is null when no meaningful percentage exists (a zero baseline).
 */
export interface PeriodComparison {
  current: number | null;
  previous: number | null;
  deltaPercent: number | null;
}

/**
 * The resolved window, echoed back by the API so drill-through links are built from the SAME bounds the figures
 * were computed over rather than recomputed client-side.
 */
export interface DashboardPeriodDto {
  key: DashboardPeriodKey;
  from: string;
  toInclusive: string;
  previousFrom: string;
  previousToInclusive: string;
}

export interface DashboardActivityDto {
  completedAppointments: PeriodComparison;
  newPatients: PeriodComparison;
  /** (NoShow + Cancelled) ÷ total, as a percentage. */
  absenceRate: PeriodComparison;
  acceptedPlans: PeriodComparison;
}

export interface DashboardMoneyDto {
  /** **Gross** encaissements — refunds are `refunds`, not netted in here. */
  collected: PeriodComparison;
  invoiced: PeriodComparison;

  /**
   * L9 — true when a practitioner filter is active, in which case **Dépenses, Net and Créances remain clinic-wide**.
   * An expense has no practitioner (rent and salaries belong to the practice), so a narrowed « Net » would be one
   * dentist's income minus everybody's costs. The UI must label those lines, never present them as that person's.
   */
  clinicWideOutgoings?: boolean;

  /**
   * L9 — true when `collected` counts **invoice payments only**, because a practitioner filter is active and
   * échéance collections are not attributable in this slice. Stated rather than silently mixed.
   */
  collectedInvoicesOnly?: boolean;
  /** Avoirs refunded in the window. */
  refunds: PeriodComparison;
  expenses: PeriodComparison;
  /** `collected - refunds - expenses`. */
  net: PeriodComparison;
}

/** Point-in-time — deliberately not a PeriodComparison. A live balance has no "last month". */
export interface DashboardReceivablesDto {
  total: number;
}

export interface DashboardAlertsDto {
  waitingList: number;
  draftPlans: number;
  patientsToRecall: number;
  overdueLabOrders: number;
  lowStock: number;
  expiringStock: number;
  /** False when the clinic switched the approaching-expiry alert off — the figure is hidden, not shown as 0. */
  expiryAlertEnabled: boolean;
}

export interface MonthlyCollectedPointDto {
  /** Clinic-local calendar month as `yyyy-MM`. */
  month: string;
  collected: number;
}

/**
 * One act type's share of the period — the dashboard's only figure counted over **acts**.
 *
 * A séance routinely carries several, so `actCount` sums above the appointment count and every surface says
 * « actes ». `procedureTypeId` and `colorHex` are null for a hand-typed devis line, which has no catalogue act
 * behind it: real work, so it is listed under its own name with a neutral swatch rather than dropped.
 */
export interface ProcedureMixPointDto {
  procedureTypeId?: string | null;
  name: string;
  colorHex?: string | null;
  actCount: number;
  /** Total booked minutes. `0` is a real answer — a link-only devis line contributes none. */
  minutes: number;
}

/**
 * One user's dashboard layout choices.
 *
 * `availableKpis` is the set the server validates writes against, sent so the customiser never has to hold its own
 * copy of what the dashboard contains — a second list would drift, and the first block added without updating it
 * would be visible on the page but absent from the panel, i.e. impossible to hide.
 */
export interface DashboardPreferencesDto {
  hiddenKpis: string[];
  availableKpis: string[];
}

export interface DashboardDto {
  period: DashboardPeriodDto;
  activity: DashboardActivityDto;
  money: DashboardMoneyDto;
  receivables: DashboardReceivablesDto;
  alerts: DashboardAlertsDto;
  trend: MonthlyCollectedPointDto[];
  /** Busiest act types of the period, already ordered and capped server-side. */
  procedureMix: ProcedureMixPointDto[];
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
  /**
   * L9 — which practitioner earned this note. **Null is a real answer** (a historical row, or one raised with no
   * practitioner in scope) and must render as « non attribué », never as the clinic.
   */
  doctorId?: string | null;
  /** The practitioner's name, resolved server-side beside the id — the row must not do a lookup per invoice. */
  doctorName?: string | null;
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
  /**
   * Server-computed. Do NOT re-derive these from status + amountCollected: that is exactly how the table
   * ended up offering « Annuler » on invoices the API refuses — after a full void the status is Issued and
   * collected is 0, but the voided payment rows are still there.
   */
  canCancel: boolean;
  canCreateAvoir: boolean;
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
}

export interface InvoiceRevenueDto {
  totalInvoiced: number;
  totalCollected: number;
  outstanding: number;
}

/** One act booked into a séance. */
export interface AppointmentProcedureDto {
  id: string;
  /** The catalog act, or null once that procedure was retired (`name` still stands). */
  procedureTypeId?: string | null;
  /** Live catalog name when the link resolves, else the snapshot taken at booking. */
  name?: string | null;
  durationMinutes?: number | null;
  colorHex?: string | null;
  /** The devis act this line carries out — how a grouped séance reports each of its steps. */
  treatmentPlanItemId?: string | null;
  sequenceNumber: number;
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
  /** The visit's **lead** act — the first of `procedures`. What paints the agenda card. */
  procedureTypeId?: string;
  procedureTypeName?: string;
  procedureColorHex?: string;
  /**
   * Every act booked into this séance, in the dentist's order. A visit is routinely several
   * (« détartrage + deux obturations »); before this existed the second one could only go in the notes.
   *
   * Empty on a « créneau occupé » or a visit booked with no act — a real state, not a missing one. Older
   * responses may omit the key entirely, so read it as `procedures ?? []`.
   */
  procedures?: AppointmentProcedureDto[];
  /**
   * The treatment-plan step this appointment schedules, if any — the **first** one when a séance groups several
   * devis acts. Each act's own link is on its `procedures` entry.
   */
  treatmentPlanItemId?: string | null;
  /**
   * The note d'honoraires raised against this visit, if any (AC-P6.13). Null = not billed yet, which is what
   * « Facturer cette consultation » keys off. A cancelled invoice does not count as billing the visit.
   */
  invoiceId?: string | null;
  /** The billing invoice's number, or null while it is still a draft (a draft consumes no number). */
  invoiceNumber?: string | null;
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
  /**
   * Dependants the insured person declares — the input to the annual-ceiling barème (L10). Not derivable from
   * `maladeLien`: that says how *this* patient relates to the insured person, while the ceiling depends on the
   * household's size, and the other dependants may not be patients of this clinic at all.
   */
  dependantCount?: number | null;
  /**
   * The household's real annual ceiling when somebody knows it — always beats the computed barème, whose amounts are
   * sourced rather than officially confirmed. Also where the dependent-parent / disabled-child / pregnancy
   * supplements land, since each turns on a fact this product does not record.
   */
  annualCeilingOverride?: number | null;
}

/**
 * « Plafond annuel CNAM » for one patient in one clinic year (L10) — the ceiling, what this clinic consumed of it,
 * and what is left.
 *
 * ⚠️ **Every figure is an estimate, for two independent reasons**, and both arrive as fields so the caveat lives
 * beside the number rather than as each screen's own wording: `ceilingIsDefault` (the barème behind it is sourced,
 * not officially confirmed) and `seesThisClinicOnly` (the clinic can only count the acts *it* performed, so
 * `remaining` is an **upper bound**).
 */
export interface CnamCeilingDto {
  year: number;
  ceiling: number;
  /** The household part of a *computed* ceiling. Null when an override was used — an override replaces the derivation, it does not adjust it. */
  baseCeiling?: number | null;
  /** The soins-dentaires-externes allowance included in a computed ceiling. Null for an override, same reason. */
  dentalAllowance?: number | null;
  dependantCount: number;
  /** True when `ceiling` came from the built-in barème rather than from a figure somebody recorded. */
  ceilingIsDefault: boolean;
  /** Reimbursement this clinic's issued invoices represent in the year, counting only acts that consume the ceiling. */
  consumed: number;
  /** Reimbursement for acts that do **not** consume it (prothèse). Reported, never silently dropped. */
  horsPlafond: number;
  /** `max(0, ceiling − consumed)` — floored, because a ceiling has no negative remainder. */
  remaining: number;
  exhausted: boolean;
  /** Always true today: it is what makes `remaining` an upper bound. */
  seesThisClinicOnly: boolean;
  /** Invoices the consumption was computed over — so « 0,000 consommé » can be told from « nothing billed yet ». */
  invoiceCount: number;
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

  // What the CNAM dentist convention currently in force says, so `/cnam-nomenclature` can offer the correction
  // instead of applying it behind an admin's back. The server corrects only rows untouched since seeding; a value
  // an admin has edited is deliberately left alone, which is exactly why the divergence has to be visible here.
  //
  // ⚠️ All three are **null together** for a lettre clé the convention text did not settle (Vd/Rd). Render that as
  // « non fixée par la convention », never as a figure — a null is « we do not know ».
  /** The dinar value the convention in force fixes for this lettre clé, if it fixes one. */
  conventionValue: number | null;
  /** The arrêté + JORT reference, shown beside the prompt so an admin can check the primary text. */
  conventionSource: string | null;
  /** How often the convention revises the lettres clés (SMIG/CPI) — so the next staleness is expected. */
  conventionRevisionIntervalYears: number | null;
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
  /**
   * Optional — a walk-in registered with nothing but a name has none, and the server no longer substitutes
   * « thirty years ago » to keep a NOT NULL column happy. Every age helper already returns null for a falsy value;
   * render « âge inconnu » rather than an age computed from a date nobody gave us.
   */
  dateOfBirth?: string | null;
  gender: string;
  /**
   * Which teeth this patient is charted on — `"Child"` or `"Adult"`. Asked once here instead of by a toggle on the
   * odontogram and another in the fiche editor. See `lib/dentition.ts` for the labels and the known mixed-dentition
   * limitation.
   */
  dentition: string;
  /** Null when the patient gave none — never a placeholder address. */
  email?: string | null;
  /** Null when the patient gave none. Such a patient receives no reminder and no relance. */
  phoneNumber?: string | null;
  /**
   * Chronic conditions and known allergies — free text, and the two most safety-critical strings on the record.
   *
   * ⚠️ On update these are **tri-state like `notes`**: omit to leave unchanged, send `""` to clear. The edit dialog
   * used to send `.trim() || undefined` for both while its neighbours three lines above sent `.trim()`, and
   * `JSON.stringify` drops `undefined` — so an allergy typed on the wrong patient could not be removed by anyone.
   * Send `""`, never `undefined`, when the intent is to clear.
   */
  medicalHistory?: string;
  /** @see medicalHistory — same tri-state, same reason. */
  allergies?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
  /**
   * « Adressé par » — who referred the patient. Free text (the referrer is usually outside this clinic).
   * On update: omit to leave unchanged, send `""` to clear.
   */
  referredBy?: string | null;
  /**
   * Patient-level notes — what to be reminded of on every visit, as opposed to a dental record's notes, which
   * describe one séance. On update: omit to leave unchanged, send `""` to clear.
   */
  notes?: string | null;
  /** Same as `notes` but rendered highlighted at the top of the patient's file. */
  importantNotes?: string | null;
  /**
   * The postal address. ⚠️ Tri-state on update, and `null` is **not** the same as omitting the key: send `null` to
   * clear the stored address, omit it to leave it alone. `undefined` used to be sent for both, so emptying the four
   * address boxes silently did nothing.
   */
  address?: {
    street: string;
    city: string;
    state: string;
    zipCode: string;
    country: string;
  } | null;
  /** Either side may be absent (AC-21): a patient can name their insurer without the card, or the reverse. */
  insuranceInfo?: {
    provider?: string;
    policyNumber?: string;
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
  /**
   * Clinical discipline the act is filed under (« Endodontie », « Prothèse fixe »); null/absent = unfiled, which
   * the catalogue and both act pickers group last under « Sans catégorie ».
   *
   * ⚠️ This is what `description` used to carry: the backend catalog seed had nowhere to put a category, so it
   * wrote each act's discipline into the description slot. Anything reading `description` as a grouping key is
   * looking at the old workaround — read `category`.
   */
  category?: string | null;
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
  /**
   * The appointment this fiche documents, or null when it was entered outside the agenda. Lets a screen answer
   * « cette séance a-t-elle déjà une fiche ? » — nothing could before, because the column was never populated.
   */
  appointmentId?: string | null;
  interventionDate: string;
  /** Derived summary string over the acts (read-only). */
  procedureType: string;
  /** Derived sum of act costs (read-only). */
  cost: number;
  amountPaid: number;
  /**
   * How `amountPaid` was settled — `Cash` | `Cheque` | `Card` | `Transfer`, or null/absent when nothing was
   * recorded, which every read takes as cash. The payment this fiche produces carries it, so a séance settled by
   * cheque finally reaches « Chèques à encaisser » instead of being counted under « dont espèces ».
   */
  paymentMethod?: string | null;
  /** The cheque's identity — null for any other method (the server refuses details on one). */
  chequeNumber?: string | null;
  chequeBankName?: string | null;
  /** A bare `YYYY-MM-DD` calendar day. Never round-trip it through `toISOString()`. */
  chequeDueDate?: string | null;
  balance: number;
  notes: string[];
  importantNotes: string[];
  isAdultTeeth: boolean;
  /** Derived union of all act teeth (read-only). */
  toothNumbers: number[];
  acts: DentalRecordActDto[];
  createdAt: string;
  updatedAt?: string;
  /**
   * What saving this fiche did about its « Montant payé » — present on the create/update responses only, since
   * it is the outcome of a post-commit side effect rather than stored state (a later GET leaves it undefined).
   *
   * The billing is best-effort for the *record* but must never be silent about the *cash*: a swallowed failure
   * would leave the user believing money was recorded when it was not, which is the very defect this replaced.
   */
  billing?: DentalRecordBillingDto | null;
}

/** The money outcome of a fiche save. Mirrors the backend `DentalRecordBillingOutcome`. */
export type DentalRecordBillingOutcome =
  /** No payment on the fiche, so nothing was billed. Not an error. */
  | 'NotCollected'
  /** A note d'honoraires was issued and the payment recorded. */
  | 'Billed'
  /** The fiche was already on a live note with nothing to add — the expected outcome of re-saving one. */
  | 'AlreadyBilled'
  /**
   * « Montant payé » was raised on an already-billed fiche, and the difference was recorded as an additional
   * payment on the **same** note. `amountCollected` is what this save put in the till, not the note's new total.
   */
  | 'ToppedUp'
  /**
   * A rule said no — the amount was lowered, the acts changed after issue, or the note is annulée/créditée.
   * Distinct from `Failed`: nothing went wrong, and `message` names the next step (an avoir).
   */
  | 'Refused'
  /** The record saved, the billing did not. The user has to be told. */
  | 'Failed';

export interface DentalRecordBillingDto {
  outcome: DentalRecordBillingOutcome;
  invoiceId?: string | null;
  invoiceNumber?: string | null;
  amountCollected?: number | null;
  /** French reason, for `Failed`, `Refused` and `AlreadyBilled`. */
  message?: string | null;
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

/**
 * The caisse (daily cash) summary. `cashIn` is **gross** — refunds are their own figure, not a subtraction hidden
 * inside it, because the « extrait » below shows a refund as money leaving and the lines have to sum to the totals.
 * `net === cashIn - refunds - cashOut`.
 */
export interface CaisseSummaryDto {
  fromDate: string;
  toDate: string;
  cashIn: number;
  /** Avoirs refunded in the window — money out, reported apart from expenses. */
  refunds: number;
  cashOut: number;
  net: number;
  /**
   * `cashIn` split by payment method (L8 slice B) — la caisse's « dont espèces », so the drawer can be separated
   * from a post-dated cheque nobody has banked.
   *
   * ⚠️ **Σ `amount` === `cashIn`**, held by construction server-side (the breakdown is a `GROUP BY` sibling of the
   * very SUM that produces `cashIn`, not a grouping of the « extrait »'s rows — those include voided payments).
   * All four methods are always present in enum order, zeros included: « Espèces 0,000 » is a true statement about
   * the drawer, and an absent row is not a statement at all.
   */
  cashInByMethod: CaisseMethodTotalDto[];
}

/** One line of `CaisseSummaryDto.cashInByMethod`. */
export interface CaisseMethodTotalDto {
  /** The storage key — also the value `caisseLedger`'s `method` filter takes, so there is one spelling of « Cheque ». */
  method: string;
  /** The French label, built server-side so the client holds no second copy of it. */
  label: string;
  amount: number;
}

/** Which ledger a caisse movement came from. Mirrors the backend `CaisseMovementKind`. */
export type CaisseMovementKind = 'InvoicePayment' | 'InstallmentPayment' | 'Refund' | 'Expense';

/** Which way the money went. Explicit rather than inferred from the sign of `amount`. */
export type CaisseMovementDirection = 'In' | 'Out';

/**
 * One line of the « extrait de caisse ». Derived server-side from the rows the totals sum — there is no
 * movement table, which is what lets Σ(movements) be asserted equal to the summary above it.
 *
 * A **voided** row is present with its motif and actor (§ 1 keeps a void visible and struck through) and is
 * excluded from `runningBalance` and from every total.
 */
export interface CaisseMovementDto {
  id: string;
  kind: CaisseMovementKind;
  direction: CaisseMovementDirection;
  /** The date the movement is attributed to (paidOn / refundedOn / expenseDate) — never its creation time. */
  occurredOn: string;
  /** Always positive; `direction` carries the sign. */
  amount: number;
  method?: string | null;
  /** French one-line description, built server-side so the four kinds share one wording. */
  label: string;
  /** The document number, when the movement has one (a draft invoice does not). */
  reference?: string | null;
  patientId?: string | null;
  patientName?: string | null;
  /** The aggregate to open — invoice / devis / the invoice an avoir credits / the expense. */
  targetId?: string | null;
  isVoided: boolean;
  voidReason?: string | null;
  voidedByName?: string | null;
  /**
   * Cheque identity (L8) — present only when the movement was paid by cheque, and null for a cheque recorded
   * before the fields existed. What turns « Chèque · 450,000 » into a line naming the paper somebody still has to
   * take to the bank.
   */
  chequeNumber?: string | null;
  /** @see chequeNumber */
  chequeBankName?: string | null;
  /**
   * The day the cheque may be presented — ⚠️ **not** `occurredOn`. A post-dated cheque is received (and appears in
   * the till) on the day it is handed over; the money only arrives on this date.
   */
  chequeDueDate?: string | null;
  /** Cumulative net **across the shown window only** — it opens at zero, it is not an account balance. */
  runningBalance: number;
}

/** The statement plus the window it covers. Carries no totals: those live in `CaisseSummaryDto`. */
/**
 * « Créances »: one page of debtors plus the clinic-wide total the header shows.
 *
 * ⚠️ `totalOutstanding` covers **every** matching debtor, not `items`. Never sum `items` for the header — that is
 * the total of one page, and it would be presented as the clinic's receivables.
 */
export interface ReceivablesPageDto {
  items: ReceivableDto[];
  totalOutstanding: number;
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface CaisseLedgerDto {
  fromDate: string;
  toDate: string;
  /** The movements on the requested page (or all of them when no paging was asked for). */
  movements: CaisseMovementDto[];

  /**
   * Page metadata for `movements`, inlined rather than wrapping the response in a `PagedResponse`: the statement
   * is not a list, it is a period (`fromDate`/`toDate`) that happens to contain one.
   *
   * ⚠️ Each movement keeps the `runningBalance` it had in the **unfiltered, unpaged** window — « Solde de la
   * période » is a fact about where the till stood after that movement, not about the current page or search.
   */
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Which ledger a held cheque came from. Same two names as `CaisseMovementKind`'s money-in kinds, on purpose. */
export type ChequeSourceKind = 'InvoicePayment' | 'InstallmentPayment';

/**
 * Which bucket a cheque falls into, as of the clinic's own today. Computed **server-side** and carried on the row,
 * so a cheque cannot be listed under one heading and counted under another.
 */
export type ChequeBucket = 'Overdue' | 'DueSoon' | 'Later' | 'Undated';

/** One cheque the clinic is holding, from either payment ledger. */
export interface ChequeDto {
  /** The payment row's id — a `Payment` or an `InstallmentPayment`, per `kind`. */
  id: string;
  kind: ChequeSourceKind;
  bucket: ChequeBucket;
  amount: number;
  /** The day the cheque was handed over — **not** the day it can be banked. */
  receivedOn: string;
  /** The day it may be presented. Null when nobody recorded one: a counted case, never a dropped row. */
  dueDate?: string | null;
  chequeNumber?: string | null;
  bankName?: string | null;
  /** The note d'honoraires or devis number the cheque paid, when it has one. */
  reference?: string | null;
  patientId?: string | null;
  patientName?: string | null;
  /**
   * The aggregate to open — the invoice, or the devis for an échéance. Also the id the « encaissé » routes are
   * addressed by, which is why an échéance additionally carries `installmentId`.
   */
  targetId: string;
  /**
   * The échéance this payment sits on, for an `InstallmentPayment`; null for an invoice payment.
   *
   * An installment payment is only addressable as {plan, installment, payment}, so this is what lets the plan
   * half of the list be acted on at all — and it is how the client picks which of the two routes to call.
   */
  installmentId?: string | null;
  /** Whether this cheque has been taken to the bank. False = still held by the clinic. */
  banked: boolean;
  /** When it was marked, and by whom — null while the cheque is still held. */
  bankedOn?: string | null;
  bankedByName?: string | null;
}

export interface ChequeBucketDto {
  count: number;
  amount: number;
}

/**
 * Counts and totals per bucket, over **every** matching cheque rather than over the current page — the same rule
 * `ReceivablesPageDto.totalOutstanding` follows. The four buckets partition the set, so their counts sum to
 * `totalCount` and their amounts sum to `total.amount`.
 */
export interface ChequeGroupsDto {
  overdue: ChequeBucketDto;
  dueSoon: ChequeBucketDto;
  later: ChequeBucketDto;
  /** No due date recorded — its own counted group, because it is the cheque nobody would ever chase. */
  undated: ChequeBucketDto;
  total: ChequeBucketDto;
}

/**
 * « Chèques à encaisser » (L8 slice B) — every cheque the clinic holds, over both payment ledgers, soonest-due
 * first with undated ones last.
 *
 * ⚠️ **The default view is what the clinic still holds.** A cheque marked « encaissé en banque » leaves it unless
 * `banked` asks for the other side; the other exit is a void, which removes it from both. Marking moves **no**
 * figure — la caisse still counts a cheque on the day it was received — and is reversible, because a cheque
 * returned unpaid is the ordinary case.
 *
 * ⚠️ `groups` always describes the **outstanding** set, whichever side is being viewed: « combien me reste-t-il à
 * encaisser ? » is one question, and a header that changed meaning with the filter would be unreadable.
 */
export interface ChequesDueDto {
  items: ChequeDto[];
  groups: ChequeGroupsDto;
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
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
  /**
   * The séance this prothèse belongs to, or null (AC-23) — the visit at which the impression was taken or the
   * piece fitted. Optional because plenty of lab work is ordered between visits.
   */
  appointmentId?: string | null;
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
/** Why a patient is on the « à rappeler » worklist. English enum name; French label mapped at display time. */
export type RecallReasonKind =
  | 'OverdueInstallment'
  | 'StalledPlan'
  | 'UnansweredDevis'
  | 'OverdueVisit';

export interface RecallReasonDto {
  kind: RecallReasonKind | string;
  dueSince: string;
  daysOverdue: number;
  /** Factual context only — a devis number, an amount. Never a sentence. */
  detail?: string | null;
}

/**
 * One patient worth calling, with EVERY reason to call them.
 *
 * The list used to answer only "not seen for the recall interval". It now aggregates four reasons, because for a
 * perio/implant practice that one is the least informative: a patient seen last week who stopped halfway through an
 * accepted devis is both lost revenue and an unfinished surgical case.
 *
 * One row per patient, not per reason — snooze state lives on the patient, so a per-reason row would let « Reporter »
 * on one reason silently hide another.
 */
export interface RecallDto {
  patientId: string;
  patientName: string;
  phoneNumber?: string | null;
  lastVisitDate?: string | null;
  /** The headline (most urgent) reason's date. */
  dueDate: string;
  daysOverdue: number;
  primaryReason: RecallReasonKind | string;
  /** Every reason, most urgent first. Never empty. */
  reasons: RecallReasonDto[];
  /** Free-text note staff attached when snoozing / marking contacted (was `reason`). */
  note?: string | null;
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


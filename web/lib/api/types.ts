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
  /** Total collected (encaissé) in the current month, in TND. */
  monthlyRevenueCollected: number;
}

export interface InvoiceLineDto {
  id: string;
  designation: string;
  quantity: number;
  unitPriceHt: number;
  lineTotalHt: number;
}

export interface PaymentDto {
  id: string;
  amount: number;
  /** Cash | Cheque | Card | Transfer */
  method: string;
  paidOn: string;
}

export interface InvoiceDto {
  id: string;
  patientId: string;
  patientName?: string | null;
  dentalRecordId?: string | null;
  appointmentId?: string | null;
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
  lines: InvoiceLineDto[];
  payments: PaymentDto[];
}

export interface InvoiceRevenueDto {
  totalInvoiced: number;
  totalCollected: number;
  outstanding: number;
}

export interface AppointmentDto {
  id: string;
  patientId: string | null;
  patientName: string;
  appointmentDateTime: string;
  duration: string; // TimeSpan format from backend (e.g., "00:30:00")
  doctorName?: string;
  notes?: string;
  status: string;
  createdAt: string;
  procedureTypeId?: string;
  procedureTypeName?: string;
  procedureColorHex?: string;
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

// A CNAM dental nomenclature entry (static reference data from GET /api/cnam-nomenclature).
// Used by the bulletin editor to fill Code acte + Cotation and compute an indicative estimate.
export interface CnamNomenclatureEntryDto {
  codeActe: string;
  designationFr: string;
  lettreCle: string;
  coefficient: number;
  category: string;
}

export interface PatientDto {
  id: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  gender: string;
  email: string;
  phoneNumber: string;
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
  createdAt: string;
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
  isActive: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface DentalRecordDto {
  id: string;
  patientId: string;
  interventionDate: string;
  procedureType: string;
  cost: number;
  amountPaid: number;
  balance: number;
  notes: string[];
  importantNotes: string[];
  isAdultTeeth: boolean;
  toothNumbers: number[];
  createdAt: string;
  updatedAt?: string;
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


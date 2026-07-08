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

export interface DashboardStats {
  todaysAppointments: number;
  totalPatients: number;
  upcomingPending: number;
  thisWeekAppointments: number;
  urgentPatients: number;
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
  createdAt: string;
  updatedAt?: string;
}


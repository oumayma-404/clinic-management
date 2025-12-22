export interface AppointmentDto {
  id: string;
  patientId: string;
  patientName: string;
  appointmentDateTime: string;
  duration: string; // TimeSpan format from backend (e.g., "00:30:00")
  doctorName?: string;
  notes?: string;
  status: string;
  createdAt: string;
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


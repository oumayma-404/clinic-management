import { apiGet, apiPost, apiPostFormData, apiPut, apiPutFormData } from './client';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

// Get Auth0 access token from client-side
async function getAccessToken(): Promise<string | null> {
  try {
    const response = await fetch('/bff/auth/token', {
      credentials: 'include',
    });
    if (response.ok) {
      const data = await response.json();
      return data.accessToken || null;
    }
  } catch {
    // Token endpoint not available or error
  }
  return null;
}

export interface DoctorDto {
  id?: string;
  name: string;
  specialty: string;
  phone?: string;
  email?: string;
  codeProfessionnelSante?: string | null;
  // Part B/C: CNOMDT ordre + cachet presence, projected by GetUserStatusQuery. Used to pre-fill the
  // certificat ordre (FR-2.5) from the current doctor's profile.
  ordreNumberCnomdt?: string | null;
  hasCachet?: boolean;
}

export interface UserStatusDto {
  hasClinic: boolean;
  clinicId?: string;
  clinicName?: string;
  role?: string;
  user?: {
    id: string;
    clinicId: string;
    role: string;
    email?: string;
    fullName?: string;
    createdAt: string;
  };
  clinic?: ClinicDto;
  doctors?: DoctorDto[];
}

export interface ClinicDto {
  id: string;
  name: string;
  address?: string;
  city?: string;
  phone?: string;
  email?: string;
  code?: string;
  logoUrl?: string;
  // Billing / note-d'honoraires settings.
  matriculeFiscal?: string | null;
  vatApplicable?: boolean;
  vatRate?: number;
  stampDutyEnabled?: boolean;
  stampDutyAmount?: number;
  // TTN « El Fatoora » e-invoicing settings.
  ttnEInvoicingEnabled?: boolean;
  ttnEnvironment?: string;
  createdAt: string;
}

export interface DoctorPersonalInfo {
  firstName: string;
  lastName: string;
  specialty: string;
  phone?: string;
}

export interface CreateClinicRequest {
  name: string;
  address?: string;
  city?: string;
  phone?: string;
  email?: string;
  generateCode?: boolean;
  role: "doctor" | "secretary";
  doctorInfo?: DoctorPersonalInfo; // Required if role is "doctor"
  doctors?: Array<{
    name: string;
    specialty: string;
    phone?: string;
    email?: string;
  }>; // Legacy: additional doctors (not the creator)
}

export interface JoinClinicRequest {
  code: string;
  role: "doctor" | "secretary";
  doctorInfo?: DoctorPersonalInfo; // Required if role is "doctor"
}

interface Result<T> {
  isSuccess: boolean;
  value: T | null;
  error: string | null;
}

export const clinicsApi = {
  getUserStatus: async (): Promise<UserStatusDto> => {
    // Add cache-busting parameter to ensure fresh data
    const timestamp = Date.now();
    const result = await apiGet<Result<UserStatusDto>>('/clinics/user-status', { _t: timestamp });
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to get user status');
    }
    return result.value;
  },

  create: async (data: CreateClinicRequest & { logoFile?: File }): Promise<ClinicDto> => {
    // If logo is provided, use FormData, otherwise use JSON
    if (data.logoFile) {
      const formData = new FormData();
      formData.append('name', data.name);
      if (data.address) formData.append('address', data.address);
      if (data.city) formData.append('city', data.city);
      if (data.phone) formData.append('phone', data.phone);
      if (data.email) formData.append('email', data.email);
      formData.append('generateCode', data.generateCode?.toString() || 'true');
      formData.append('role', data.role);
      if (data.logoFile) formData.append('logo', data.logoFile);
      if (data.doctorInfo) {
        formData.append('doctorInfoJson', JSON.stringify(data.doctorInfo));
      }

      const result = await apiPostFormData<Result<ClinicDto>>('/clinics', formData);
      if (!result.isSuccess || !result.value) {
        throw new Error(result.error || 'Failed to create clinic');
      }
      return result.value;
    } else {
      const result = await apiPost<Result<ClinicDto>>('/clinics', data);
      if (!result.isSuccess || !result.value) {
        throw new Error(result.error || 'Failed to create clinic');
      }
      return result.value;
    }
  },

  // Local (offline) first-run: create the clinic + first admin (email+password).
  // Anonymous + must hit the .NET API directly from the browser so the server's
  // localhost gate (AC-1.2a) sees the real client IP. `null` token skips auth.
  setup: async (data: {
    clinicName: string;
    email: string;
    password: string;
    fullName: string;
    phone?: string;
    address?: string;
    city?: string;
    // When set, the first admin is also the cabinet practitioner: a linked Doctor is created so their
    // document identity (cachet / CNOMDT ordre) and "Mon profil" work.
    doctorInfo?: DoctorPersonalInfo;
  }): Promise<ClinicDto> => {
    const result = await apiPost<Result<ClinicDto>>('/auth/setup', data, null);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to complete setup');
    }
    return result.value;
  },

  // Local (offline) staff self-registration: join a clinic by code with credentials.
  // Anonymous (no session yet) — the clinic code is the gate. `null` token skips auth.
  register: async (data: {
    code: string;
    email: string;
    password: string;
    fullName: string;
    role: "doctor" | "secretary";
    doctorInfo?: DoctorPersonalInfo;
  }): Promise<ClinicDto> => {
    const result = await apiPost<Result<ClinicDto>>('/auth/register', data, null);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to register');
    }
    return result.value;
  },

  join: async (data: JoinClinicRequest): Promise<ClinicDto> => {
    const result = await apiPost<Result<ClinicDto>>('/clinics/join', data);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to join clinic');
    }
    return result.value;
  },

  // AC-4.5: regenerate the clinic's self-registration code (admin-only), invalidating the old one.
  regenerateCode: async (): Promise<ClinicDto> => {
    const result = await apiPost<Result<ClinicDto>>('/clinics/regenerate-code', {});
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to regenerate clinic code');
    }
    return result.value;
  },

  updateDoctors: async (doctors: DoctorDto[]): Promise<DoctorDto[]> => {
    // Send doctors array directly, backend expects UpdateDoctorsRequest with Doctors property
    const result = await apiPut<Result<DoctorDto[]>>('/clinics/doctors', { doctors: doctors });
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to update doctors');
    }
    return result.value;
  },

  update: async (data: {
    name: string;
    address?: string;
    city?: string;
    phone?: string;
    email?: string;
    logoFile?: File;
    matriculeFiscal?: string;
    vatApplicable?: boolean;
    vatRate?: number;
    stampDutyEnabled?: boolean;
    stampDutyAmount?: number;
    ttnEInvoicingEnabled?: boolean;
    ttnEnvironment?: string;
  }): Promise<ClinicDto> => {
    const formData = new FormData();
    formData.append('name', data.name);
    if (data.address) formData.append('address', data.address);
    // Send city even when blank so an admin can clear it (backend: null=keep, ""=clear).
    if (data.city !== undefined) formData.append('city', data.city);
    if (data.phone) formData.append('phone', data.phone);
    if (data.email) formData.append('email', data.email);
    if (data.logoFile) formData.append('logo', data.logoFile);
    // Billing settings (optional). Send the matricule even when blank so it can be cleared.
    if (data.matriculeFiscal !== undefined) formData.append('matriculeFiscal', data.matriculeFiscal);
    if (data.vatApplicable !== undefined) formData.append('vatApplicable', String(data.vatApplicable));
    if (data.vatRate !== undefined) formData.append('vatRate', String(data.vatRate));
    if (data.stampDutyEnabled !== undefined) formData.append('stampDutyEnabled', String(data.stampDutyEnabled));
    if (data.stampDutyAmount !== undefined) formData.append('stampDutyAmount', String(data.stampDutyAmount));
    // TTN e-invoicing settings (optional).
    if (data.ttnEInvoicingEnabled !== undefined) formData.append('ttnEInvoicingEnabled', String(data.ttnEInvoicingEnabled));
    if (data.ttnEnvironment !== undefined) formData.append('ttnEnvironment', data.ttnEnvironment);

    const result = await apiPutFormData<Result<ClinicDto>>('/clinics', formData);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to update clinic');
    }
    return result.value;
  },

  getLogo: async (): Promise<Blob> => {
    const token = await getAccessToken();
    const headers: HeadersInit = {};
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(`${API_BASE_URL}/clinics/logo`, {
      method: 'GET',
      headers,
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error('Failed to get clinic logo');
    }

    return response.blob();
  },
};


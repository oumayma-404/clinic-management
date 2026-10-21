import { apiDelete, apiGet, apiGetBlob, apiPost, apiPut, apiPutFormData } from './client';
import type { DoctorProfileDto } from './types';
import type { WorkingDay } from '@/lib/working-hours';

/**
 * One day of a practitioner's working hours. Re-exported, not redeclared: this was a third copy of the shape,
 * and the mid-day closure added to the shared model would have reached the clinic editor and not this one.
 */
export type { WorkingDay } from '@/lib/working-hours';

export interface UpdateMyDoctorProfileInput {
  ordreNumberCnomdt?: string;
  cachet?: File | null;
  removeCachet?: boolean;
  /** The version read from the server. Omitted (or 0) the server skips the check — see `PatientDto.version`. */
  version?: number;
}

/** A practitioner's congé: clinic-local days `YYYY-MM-DD`, both ends inclusive. */
export interface DoctorLeaveDto {
  id: string;
  doctorId: string;
  doctorName: string;
  startDate: string;
  endDate: string;
  dayCount: number;
  note?: string | null;
  version: number;
}

export interface DoctorLeaveInput {
  startDate: string;
  endDate: string;
  note?: string | null;
}

/** A saved congé and the booked visits it covers — counted, never moved. */
export interface DoctorLeaveSaveResultDto {
  leave: DoctorLeaveDto;
  appointmentsDuring: number;
  firstAppointmentDay?: string | null;
}

/** Build the shared multipart body for both `/doctors/me` and `/doctors/{id}` — one payload shape, one mapper. */
function doctorProfileForm(input: UpdateMyDoctorProfileInput): FormData {
  const form = new FormData();
  // Always sent (empty clears it); the cachet is optional and RemoveCachet wins when set.
  form.append('OrdreNumberCnomdt', input.ordreNumberCnomdt ?? '');
  // Multipart carries no numbers: an omitted version must arrive as 0 (« not supplied »), never as "undefined".
  form.append('Version', String(input.version ?? 0));
  if (input.removeCachet) {
    form.append('RemoveCachet', 'true');
  } else if (input.cachet) {
    form.append('Cachet', input.cachet);
  }
  return form;
}

export const doctorsApi = {
  // FR-2.5 / FR-3.1: the logged-in practitioner's own document identity.
  getMyProfile: async (): Promise<DoctorProfileDto> => apiGet<DoctorProfileDto>('/doctors/me'),

  // Per-dentist working hours (AC-3.3). Empty list = no override (clinic-wide hours apply).
  getWorkingHours: async (doctorId: string): Promise<WorkingDay[]> =>
    apiGet<WorkingDay[]>(`/doctors/${doctorId}/working-hours`),

  setWorkingHours: async (doctorId: string, workingHours: WorkingDay[]): Promise<WorkingDay[]> =>
    apiPut<WorkingDay[]>(`/doctors/${doctorId}/working-hours`, { workingHours }),

  updateMyProfile: async (input: UpdateMyDoctorProfileInput): Promise<DoctorProfileDto> =>
    apiPutFormData<DoctorProfileDto>('/doctors/me', doctorProfileForm(input)),

  /**
   * AC-P2.30/2.31: set **another** practitioner's CNOMDT number and cachet.
   *
   * `PUT /api/doctors/{id}` has existed, with its own-or-admin guard, since the document-identity work, and had
   * **no client function at all** — which is why « Mon profil » could promise that "un admin peut définir le
   * cachet et le numéro d'ordre d'un autre praticien depuis Paramètres → Médecins" while no such control
   * existed. Nothing changes server-side: a doctor editing themselves still goes through `/doctors/me`
   * (AC-P2.32), and a non-admin calling this for someone else is refused by the handler.
   */
  updateProfile: async (doctorId: string, input: UpdateMyDoctorProfileInput): Promise<DoctorProfileDto> =>
    apiPutFormData<DoctorProfileDto>(`/doctors/${doctorId}`, doctorProfileForm(input)),

  /** Every congé of one practitioner, earliest first. */
  getLeaves: async (doctorId: string): Promise<DoctorLeaveDto[]> =>
    apiGet<DoctorLeaveDto[]>(`/doctors/${doctorId}/leaves`),

  /** The cabinet's congés touching an inclusive day window — what the agenda draws. */
  getClinicLeaves: async (fromDay: string, toDay: string): Promise<DoctorLeaveDto[]> =>
    apiGet<DoctorLeaveDto[]>('/doctors/leaves', { from: fromDay, to: toDay }),

  createLeave: async (doctorId: string, input: DoctorLeaveInput): Promise<DoctorLeaveSaveResultDto> =>
    apiPost<DoctorLeaveSaveResultDto>(`/doctors/${doctorId}/leaves`, input),

  updateLeave: async (
    doctorId: string,
    leaveId: string,
    input: DoctorLeaveInput & { version: number },
  ): Promise<DoctorLeaveSaveResultDto> =>
    apiPut<DoctorLeaveSaveResultDto>(`/doctors/${doctorId}/leaves/${leaveId}`, input),

  deleteLeave: async (doctorId: string, leaveId: string): Promise<void> =>
    apiDelete<void>(`/doctors/${doctorId}/leaves/${leaveId}`),

  // The cachet image sits behind the bearer token, so it cannot be an <img src> — it is fetched as a blob.
  fetchCachetBlob: async (doctorId: string): Promise<Blob> =>
    apiGetBlob(`/doctors/${doctorId}/cachet`),
};

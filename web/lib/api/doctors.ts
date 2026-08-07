import { apiGet, apiGetBlob, apiPut, apiPutFormData } from './client';
import type { DoctorProfileDto } from './types';

/** One day of a practitioner's working hours (same shape as the clinic-wide hours). */
export interface WorkingDay {
  day: string;
  enabled: boolean;
  from: string;
  to: string;
}

export interface UpdateMyDoctorProfileInput {
  ordreNumberCnomdt?: string;
  cachet?: File | null;
  removeCachet?: boolean;
}

/** Build the shared multipart body for both `/doctors/me` and `/doctors/{id}` — one payload shape, one mapper. */
function doctorProfileForm(input: UpdateMyDoctorProfileInput): FormData {
  const form = new FormData();
  // Always sent (empty clears it); the cachet is optional and RemoveCachet wins when set.
  form.append('OrdreNumberCnomdt', input.ordreNumberCnomdt ?? '');
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

  // The cachet image sits behind the bearer token, so it cannot be an <img src> — it is fetched as a blob.
  fetchCachetBlob: async (doctorId: string): Promise<Blob> =>
    apiGetBlob(`/doctors/${doctorId}/cachet`),
};

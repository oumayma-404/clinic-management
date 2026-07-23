import { apiGet, apiPost, apiPut } from './client';
import type { RecallDto, RecallSettingsDto } from './types';

export const recallsApi = {
  // The "patients à relancer" list (due/overdue), most overdue first.
  list: async (): Promise<RecallDto[]> => apiGet<RecallDto[]>('/patients/recalls'),

  getSettings: async (): Promise<RecallSettingsDto> => apiGet<RecallSettingsDto>('/patients/recalls/settings'),

  setSettings: async (intervalMonths: number): Promise<RecallSettingsDto> =>
    apiPut<RecallSettingsDto>('/patients/recalls/settings', { intervalMonths }),

  markContacted: async (patientId: string, reason?: string | null): Promise<void> =>
    apiPost<void>(`/patients/recalls/${patientId}/contacted`, { reason: reason ?? null }),

  snooze: async (patientId: string, days?: number, reason?: string | null): Promise<void> =>
    apiPost<void>(`/patients/recalls/${patientId}/snooze`, { days: days ?? null, reason: reason ?? null }),

  // Send an SMS/WhatsApp recall (connectivity-gated server-side) and record the contact.
  send: async (patientId: string, reason?: string | null): Promise<void> =>
    apiPost<void>(`/patients/recalls/${patientId}/send`, { reason: reason ?? null }),
};

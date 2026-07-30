import { apiGet, apiPost, apiPut } from './client';
import type { RecallDto, RecallSettingsDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export const recallsApi = {
  // The "patients à relancer" list (due/overdue), most overdue first.
  list: async (): Promise<RecallDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<RecallDto>>('/patients/recalls')),

  /** One page of relances. `search` matches patient name / phone server-side over the whole due list. */
  listPaged: async (params: PageParams): Promise<PagedResponse<RecallDto>> =>
    apiGet<PagedResponse<RecallDto>>('/patients/recalls', params),

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

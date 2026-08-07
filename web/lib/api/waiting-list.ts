import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { WaitingListEntryDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface WaitingListPayload {
  patientId: string;
  priority?: string; // Low | Normal | High (default Normal)
  preferredDoctorId?: string | null;
  desiredTimeframe?: string | null;
  note?: string | null;
}

export const waitingListApi = {
  list: async (activeOnly: boolean = true): Promise<WaitingListEntryDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<WaitingListEntryDto>>('/waiting-list', { activeOnly })),

  /** One page of the salle d'attente. `search` matches patient / note / créneau souhaité server-side. */
  listPaged: async (
    params: PageParams & { activeOnly?: boolean },
  ): Promise<PagedResponse<WaitingListEntryDto>> =>
    apiGet<PagedResponse<WaitingListEntryDto>>('/waiting-list', params),

  create: async (data: WaitingListPayload): Promise<WaitingListEntryDto> =>
    apiPost<WaitingListEntryDto>('/waiting-list', data),

  update: async (id: string, data: Omit<WaitingListPayload, 'patientId'>): Promise<WaitingListEntryDto> =>
    apiPut<WaitingListEntryDto>(`/waiting-list/${id}`, data),

  // Promote to a booked appointment: the caller books the appointment first, then promotes with its id.
  promote: async (id: string, resultingAppointmentId?: string | null): Promise<WaitingListEntryDto> =>
    apiPost<WaitingListEntryDto>(`/waiting-list/${id}/promote`, { resultingAppointmentId: resultingAppointmentId ?? null }),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/waiting-list/${id}`),
};

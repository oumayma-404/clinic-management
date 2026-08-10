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

  /**
   * « Retirer de la liste » — the patient stopped waiting (AC-25).
   *
   * ⚠️ Deliberately **not** `delete`: cancelling keeps the row and records the outcome, while deleting destroys
   * the evidence that the person ever waited — the wrong answer to « pourquoi n'est-elle plus dans la liste ? ».
   * The delete below stays, for a row entered by mistake. The server refuses to cancel an entry already
   * promoted: that one became a real appointment, and `Appointment.Cancel` is where undoing it belongs.
   */
  cancel: async (id: string): Promise<WaitingListEntryDto> =>
    apiPost<WaitingListEntryDto>(`/waiting-list/${id}/cancel`, {}),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/waiting-list/${id}`),
};

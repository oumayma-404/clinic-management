import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { LabWorkOrderDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface LabWorkOrderPayload {
  patientId: string;
  prosthetist: string;
  workDescription: string;
  toothNumber?: number | null;
  sentDate?: string | null;
  expectedDate?: string | null;
  cost?: number | null;
  notes?: string | null;
  /**
   * The laboratory's fiche fournisseur — what gives the bon a number to chase.
   *
   * ⚠️ **Replace-semantics like every other field here**: sending null detaches the fiche. Deliberately NOT the
   * tri-state `stockApi`'s `supplierId` uses — that payload is a patch in practice, this one replaces the bon.
   */
  /**
   * The séance this prothèse belongs to, or null.
   *
   * ⚠️ **Replace-semantics like the rest of this payload, so it must be echoed back on every update.** Omitting it
   * detached the bon's séance on every edit from the lab-orders screen — the form has no control for it, so the
   * only correct value is the one the bon already holds.
   */
  appointmentId?: string | null;
  supplierId?: string | null;
  /**
   * The version read from the server, on an update. Absent on create; omitted (or 0) the server skips the
   * concurrency check — see `PatientDto.version`. Its absence is what let one save silently revert another's
   * coût, dent and notes under a « Bon mis à jour » toast.
   */
  version?: number;
}

export const labOrdersApi = {
  // Clinic-wide, or per patient when patientId is provided.
  /** `status` narrows to one stage (Sent / InProgress / Received / Fitted); an unknown value is ignored server-side. */
  list: async (patientId?: string, status?: string): Promise<LabWorkOrderDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<LabWorkOrderDto>>('/lab-orders', { patientId, status })),

  /**
   * One page of bons. `search` matches prothésiste / description / notes / patient / the linked fiche's nom
   * server-side; `supplierId` narrows to one laboratory; `sortBy: 'expected'` orders by « Prévu » ascending
   * (dateless last) instead of newest-created first.
   */
  listPaged: async (
    params: PageParams & { patientId?: string; status?: string; supplierId?: string; sortBy?: string },
  ): Promise<PagedResponse<LabWorkOrderDto>> =>
    apiGet<PagedResponse<LabWorkOrderDto>>('/lab-orders', params),

  create: async (data: LabWorkOrderPayload): Promise<LabWorkOrderDto> =>
    apiPost<LabWorkOrderDto>('/lab-orders', data),

  update: async (id: string, data: Omit<LabWorkOrderPayload, 'patientId'>): Promise<LabWorkOrderDto> =>
    apiPut<LabWorkOrderDto>(`/lab-orders/${id}`, data),

  updateStatus: async (id: string, status: string): Promise<LabWorkOrderDto> =>
    apiPut<LabWorkOrderDto>(`/lab-orders/${id}/status`, { status }),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/lab-orders/${id}`),
};

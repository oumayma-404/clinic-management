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
  supplierId?: string | null;
}

export const labOrdersApi = {
  // Clinic-wide, or per patient when patientId is provided.
  /** `status` narrows to one stage (Sent / InProgress / Received / Fitted); an unknown value is ignored server-side. */
  list: async (patientId?: string, status?: string): Promise<LabWorkOrderDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<LabWorkOrderDto>>('/lab-orders', { patientId, status })),

  /** One page of bons. `search` matches prothésiste / description / notes / patient server-side. */
  listPaged: async (
    params: PageParams & { patientId?: string; status?: string },
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

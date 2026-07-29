import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { LabWorkOrderDto } from './types';

export interface LabWorkOrderPayload {
  patientId: string;
  prosthetist: string;
  workDescription: string;
  toothNumber?: number | null;
  sentDate?: string | null;
  expectedDate?: string | null;
  cost?: number | null;
  notes?: string | null;
}

export const labOrdersApi = {
  // Clinic-wide, or per patient when patientId is provided.
  /** `status` narrows to one stage (Sent / InProgress / Received / Fitted); an unknown value is ignored server-side. */
  list: async (patientId?: string, status?: string): Promise<LabWorkOrderDto[]> =>
    apiGet<LabWorkOrderDto[]>('/lab-orders', { patientId, status }),

  create: async (data: LabWorkOrderPayload): Promise<LabWorkOrderDto> =>
    apiPost<LabWorkOrderDto>('/lab-orders', data),

  update: async (id: string, data: Omit<LabWorkOrderPayload, 'patientId'>): Promise<LabWorkOrderDto> =>
    apiPut<LabWorkOrderDto>(`/lab-orders/${id}`, data),

  updateStatus: async (id: string, status: string): Promise<LabWorkOrderDto> =>
    apiPut<LabWorkOrderDto>(`/lab-orders/${id}/status`, { status }),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/lab-orders/${id}`),
};

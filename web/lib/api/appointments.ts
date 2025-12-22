import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { AppointmentDto } from './types';

export const appointmentsApi = {
  list: async (params?: {
    startDate?: string;
    endDate?: string;
    patientId?: string;
    doctorName?: string;
  }): Promise<AppointmentDto[]> => {
    return apiGet<AppointmentDto[]>('/appointments', params);
  },

  get: async (id: string): Promise<AppointmentDto> => {
    return apiGet<AppointmentDto>(`/appointments/${id}`);
  },

  create: async (data: {
    patientId: string;
    appointmentDateTime: string;
    durationMinutes: number;
    doctorName?: string;
    notes?: string;
  }): Promise<AppointmentDto> => {
    return apiPost<AppointmentDto>('/appointments', {
      patientId: data.patientId,
      appointmentDateTime: data.appointmentDateTime,
      durationMinutes: data.durationMinutes,
      doctorName: data.doctorName,
      notes: data.notes,
    });
  },

  update: async (id: string, data: {
    appointmentDateTime?: string;
    durationMinutes?: number;
    doctorName?: string;
    notes?: string;
    status?: string;
  }): Promise<AppointmentDto> => {
    return apiPut<AppointmentDto>(`/appointments/${id}`, data);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/appointments/${id}`);
  },
};


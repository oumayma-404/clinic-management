import { apiGet, apiPost, apiPut } from './client';
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
    patientId?: string | null;
    appointmentDateTime: string;
    durationMinutes: number;
    doctorId?: string;
    doctorName?: string;
    notes?: string;
    procedureTypeId?: string;
  }): Promise<AppointmentDto> => {
    return apiPost<AppointmentDto>('/appointments', {
      patientId: data.patientId || null,
      appointmentDateTime: data.appointmentDateTime,
      durationMinutes: data.durationMinutes,
      doctorId: data.doctorId,
      doctorName: data.doctorName,
      notes: data.notes,
      procedureTypeId: data.procedureTypeId,
    });
  },

  update: async (id: string, data: {
    appointmentDateTime?: string;
    durationMinutes?: number;
    doctorId?: string;
    doctorName?: string;
    notes?: string;
    status?: string;
    procedureTypeId?: string | null;
  }): Promise<AppointmentDto> => {
    return apiPut<AppointmentDto>(`/appointments/${id}`, data);
  },
};



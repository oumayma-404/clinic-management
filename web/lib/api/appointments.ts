import { apiGet, apiPost, apiPut } from './client';
import type { AppointmentDto, RecurringAppointmentDto, RecurringSeriesResultDto } from './types';

export interface CreateRecurringSeriesPayload {
  patientId: string;
  startDateTime: string;
  durationMinutes: number;
  frequency: string; // Daily | Weekly | Monthly
  interval: number;
  endDate?: string | null;
  occurrenceCount?: number | null;
  doctorId?: string | null;
  doctorName?: string | null;
  procedureTypeId?: string | null;
  notes?: string | null;
}

export const appointmentsApi = {
  list: async (params?: {
    startDate?: string;
    endDate?: string;
    patientId?: string;
    doctorId?: string;
    doctorName?: string;
  }): Promise<AppointmentDto[]> => {
    return apiGet<AppointmentDto[]>('/appointments', params);
  },

  // ---- Recurring series (clinical-workflow-depth) --------------------------------------------------
  listRecurring: async (activeOnly: boolean = true): Promise<RecurringAppointmentDto[]> =>
    apiGet<RecurringAppointmentDto[]>('/appointments/recurring', { activeOnly }),

  createRecurring: async (data: CreateRecurringSeriesPayload): Promise<RecurringSeriesResultDto> =>
    apiPost<RecurringSeriesResultDto>('/appointments/recurring', data),

  cancelRecurring: async (
    id: string,
    scope: string, // Occurrence | Following | WholeSeries
    fromAppointmentId?: string | null,
    reason?: string | null,
  ): Promise<{ cancelled: number }> =>
    apiPost<{ cancelled: number }>(`/appointments/recurring/${id}/cancel`, {
      scope,
      fromAppointmentId: fromAppointmentId ?? null,
      reason: reason ?? null,
    }),

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
    treatmentPlanId?: string | null;
    treatmentPlanItemId?: string | null;
  }): Promise<AppointmentDto> => {
    return apiPost<AppointmentDto>('/appointments', {
      patientId: data.patientId || null,
      appointmentDateTime: data.appointmentDateTime,
      durationMinutes: data.durationMinutes,
      doctorId: data.doctorId,
      doctorName: data.doctorName,
      notes: data.notes,
      procedureTypeId: data.procedureTypeId,
      treatmentPlanId: data.treatmentPlanId || null,
      treatmentPlanItemId: data.treatmentPlanItemId || null,
    });
  },

  /**
   * Partially update an appointment.
   *
   * **Every nullable field below is tri-state, and the distinction matters:** *omit* the key to leave the
   * field untouched, send `null` to clear it. `JSON.stringify` drops `undefined` keys entirely, so
   * `x || undefined` means "leave alone" and `x || null` means "clear" — passing the wrong one is a silent
   * no-op in one direction and silent data loss in the other.
   */
  update: async (id: string, data: {
    appointmentDateTime?: string;
    durationMinutes?: number;
    /** `null` unassigns the practitioner. */
    doctorId?: string | null;
    /** `null` clears the free-text practitioner label. */
    doctorName?: string | null;
    /** `null` clears the notes. */
    notes?: string | null;
    status?: string;
    /** `null` clears the booked act along with its snapshot duration and colour. */
    procedureTypeId?: string | null;
    /** Required alongside `treatmentPlanItemId` when linking — the server validates the pair. */
    treatmentPlanId?: string;
    /** `null` clears the plan-act link. */
    treatmentPlanItemId?: string | null;
  }): Promise<AppointmentDto> => {
    return apiPut<AppointmentDto>(`/appointments/${id}`, data);
  },
};



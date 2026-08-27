import { apiGet, apiPost, apiPut } from './client';
import type {
  AppointmentDto,
  RecurringAppointmentDto,
  RecurringSeriesResultDto,
  VisitToCloseDto,
} from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

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
  /** Create out-of-hours occurrences instead of skipping them (AC-P1.31). */
  allowOutsideWorkingHours?: boolean;
  /** Confirmed override for a double-booking with the same practitioner. */
  allowOverlap?: boolean;
}

/**
 * One act as the client asks for it. Name, duration and colour are read from the catalog server-side — the client
 * sends only what the user picked.
 */
export interface AppointmentProcedurePayload {
  /**
   * The catalog act. `null` is allowed **only** alongside a `treatmentPlanItemId`: a hand-typed devis line has no
   * procedure behind it and the server names such a row from the plan step's désignation.
   */
  procedureTypeId: string | null;
  /** The devis act this line carries out. Validated against the request's `treatmentPlanId`. */
  treatmentPlanItemId?: string | null;
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

  // ---- « À clôturer » (visit-closure worklist) -----------------------------------------------------

  /**
   * The séances whose slot has passed and which still owe one of the three answers.
   *
   * `AnyClinicRole`, deliberately: the dashboard is `AdminOrDoctor` and `app/page.tsx` sends a secretary to
   * `/appointments`, so a worklist only reachable from the dashboard would be invisible to reception — who is
   * exactly the person who knows whether the patient came and who took the money.
   *
   * ⚠️ Ask for `pageSize: 1` when all you want is the count: `totalCount` is the whole clinic's figure, never
   * `items.length`. That is how the agenda strip stays one small request.
   */
  visitsToClose: async (
    params?: PageParams & { days?: number; doctorId?: string },
  ): Promise<PagedResponse<VisitToCloseDto>> =>
    apiGet<PagedResponse<VisitToCloseDto>>('/appointments/to-close', params),

  /**
   * Record that a séance raises no note d'honoraires, or withdraw that.
   *
   * The escape hatch of last resort: a fiche worth nothing, a séance carried by a devis and an existing invoice
   * are all derived server-side, so this is only for the case none of those describe. `reason` is mandatory when
   * marking — the server refuses a blank one, because the whole value is that « pourquoi cette séance n'a produit
   * aucun document ? » stays answerable months later.
   */
  setNothingToBill: async (
    appointmentId: string,
    nothingToBill: boolean,
    reason?: string,
  ): Promise<{ nothingToBill: boolean }> =>
    apiPost<{ nothingToBill: boolean }>(`/appointments/${appointmentId}/nothing-to-bill`, {
      nothingToBill,
      reason: reason ?? null,
    }),

  // ---- Recurring series (clinical-workflow-depth) --------------------------------------------------
  listRecurring: async (activeOnly: boolean = true): Promise<RecurringAppointmentDto[]> =>
    unwrapPaged(
      await apiGet<PagedResponse<RecurringAppointmentDto>>('/appointments/recurring', { activeOnly }),
    ),

  /** One page of series. `search` matches patient / praticien / notes server-side over the whole clinic. */
  listRecurringPaged: async (
    params: PageParams & { activeOnly?: boolean },
  ): Promise<PagedResponse<RecurringAppointmentDto>> =>
    apiGet<PagedResponse<RecurringAppointmentDto>>('/appointments/recurring', params),

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
    /** Confirmed out-of-hours override (AC-P1.31). Recorded on the appointment, never silently allowed. */
    allowOutsideWorkingHours?: boolean;
  /** Confirmed override for a double-booking with the same practitioner. */
  allowOverlap?: boolean;
    procedureTypeId?: string;
    /**
     * The acts of this séance. Several acts in one visit is the normal case, and each entry may carry its own
     * devis step — which is how « ces deux actes ensemble, ces deux-là séparément » is expressed: one grouped
     * booking with two entries, then two bookings with one each.
     *
     * When supplied it takes precedence over `procedureTypeId`, and `durationMinutes: 0` makes the server default
     * the visit's length to the **sum** of the acts' own durations.
     */
    procedures?: AppointmentProcedurePayload[];
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
      // Same hand-built-payload trap as `allowOverlap` below: a key missing from this literal never reaches the
      // server, and the request still succeeds — the séance just silently books one act.
      procedures: data.procedures,
      allowOutsideWorkingHours: data.allowOutsideWorkingHours,
      // Must be copied explicitly: this payload is hand-built field by field rather than spread, so a new key on
      // the parameter type reaches the server only if it is listed here. Omitting it is silent — the request
      // succeeds, the server just never sees the override and refuses the booking again.
      allowOverlap: data.allowOverlap,
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
    /**
     * Concurrency token read from the DTO. Send it back so the save is checked against the copy shown to
     * this user; a peer's change in between then yields a 409 instead of silently overwriting them.
     */
    version?: number;
    appointmentDateTime?: string;
    durationMinutes?: number;
    /** `null` unassigns the practitioner. */
    doctorId?: string | null;
    /** `null` clears the free-text practitioner label. */
    doctorName?: string | null;
    /** `null` clears the notes. */
    notes?: string | null;
    status?: string;
    /** Recorded when the status moves to `Cancelled` — previously always null on this path. */
    cancellationReason?: string;
    /** Confirmed out-of-hours override for a move (AC-P1.31). */
    allowOutsideWorkingHours?: boolean;
  /** Confirmed override for a double-booking with the same practitioner. */
  allowOverlap?: boolean;
    /** `null` clears the booked act along with its snapshot duration and colour. */
    procedureTypeId?: string | null;
    /**
     * The séance's acts, replaced wholesale. Tri-state like everything else here and the distinction matters:
     * **omit** the key to leave the acts alone, send `[]` to clear them all. Takes precedence over
     * `procedureTypeId` when present.
     */
    procedures?: AppointmentProcedurePayload[];
    /** Required alongside `treatmentPlanItemId` when linking — the server validates the pair. */
    treatmentPlanId?: string;
    /** `null` clears the plan-act link. */
    treatmentPlanItemId?: string | null;
  }): Promise<AppointmentDto> => {
    return apiPut<AppointmentDto>(`/appointments/${id}`, data);
  },
};



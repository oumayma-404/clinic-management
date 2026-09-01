import { apiGet, apiPost, apiPut } from './client';
import type { PagedResponse } from './paging';

export interface GoogleCalendarStatus {
  isConfigured: boolean;
  hasClientId: boolean;
  hasClientSecret: boolean;
  hasRefreshToken: boolean;
  tokenValid?: boolean;
  calendarId: string;
  message: string;
  /**
   * The practice has declared the connected calendar holds nothing but appointments, so the import treats any
   * event titled « Prénom Nom » as one instead of demanding a keyword in the title.
   */
  holdsOnlyAppointments: boolean;
}

export interface GoogleCalendarAuthResponse {
  success: boolean;
  message: string;
  redirectUrl?: string;
}

export const googleCalendarApi = {
  /**
   * Get Google Calendar sync status
   */
  getStatus: async (): Promise<GoogleCalendarStatus> => {
    return apiGet<GoogleCalendarStatus>('/googlecalendar/status');
  },

  /**
   * Initiate Google Calendar OAuth for the current clinic (admin only). Calls the authenticated connect
   * endpoint to obtain a clinic-bound authorization URL, then navigates the browser to Google. Per-clinic
   * so each clinic connects its OWN Google account/calendar (no shared account across clinics).
   */
  connect: async (): Promise<void> => {
    const { authUrl } = await apiPost<{ authUrl: string }>('/googlecalendar/connect', {});
    window.location.href = authUrl;
  },

  /**
   * AC-P2.33: disconnect this clinic from Google Calendar (admin only) — clears the stored refresh token and
   * calendar id, so pushes stop and `getStatus()` reports « non connecté ». Appointments already pushed keep
   * their `googleCalendarEventId`: nothing is deleted in the clinic's Google account (AC-P2.35).
   */
  disconnect: async (): Promise<void> => {
    await apiPost<{ disconnected: boolean }>('/googlecalendar/disconnect', {});
  },

  /**
   * Sync from Google Calendar to clinic appointments.
   * Routed through the shared client.ts wrapper so a mid-request connectivity loss surfaces as
   * ApiError(status === 0) — unifying calendar failure handling with the AI path (AC-6.5, R-7).
   */
  syncFromGoogle: async (): Promise<CalendarImportOutcome> => {
    return apiPost<CalendarImportOutcome>('/googlecalendar/sync-from-google', {});
  },

  /**
   * The import passes this cabinet has had, newest first — or, with `latestUndoable`, just the one worth
   * offering « Annuler cet import » for.
   *
   * `latestUndoable` is not « the most recent »: the recurring importer writes a run every few hours and most of
   * them create nothing, so taking the latest would put a destructive-looking button in front of a practice for
   * no reason and hide the import that actually filled its worklist behind a dozen that did not.
   */
  listImports: async (
    params?: { latestUndoable?: boolean; page?: number; pageSize?: number },
  ): Promise<PagedResponse<CalendarImportRunDto>> =>
    apiGet<PagedResponse<CalendarImportRunDto>>('/googlecalendar/imports', params),

  /**
   * What « Annuler cet import » would delete and what it would keep — asked **before** anything is written.
   *
   * This is the safety of the whole feature: the person pressing the button is the cabinet, not the vendor, so
   * the confirmation shows the list itself and every row that will survive names its own reason.
   */
  previewRevert: async (runId: string): Promise<CalendarImportRevertPreview> =>
    apiGet<CalendarImportRevertPreview>(`/googlecalendar/imports/${runId}/revert-preview`),

  /** Undo one import pass. Admin only — it deletes patient records. Never touches the Google calendar. */
  revertImport: async (runId: string): Promise<CalendarImportRevertResult> =>
    apiPost<CalendarImportRevertResult>(`/googlecalendar/imports/${runId}/revert`, {}),

  /**
   * Sync a specific appointment to Google Calendar (manual "Push to Google").
   * Routed through client.ts for the same ApiError(status === 0) offline signal (AC-6.5, R-7).
   */
  syncAppointment: async (appointmentId: string): Promise<{ message: string }> => {
    return apiPost<{ message: string }>(`/googlecalendar/sync-appointment/${appointmentId}`, {});
  },

  /**
   * Admin-only: declare whether the connected calendar holds only appointments. Refused with a French message when
   * the clinic has no Google connection — the setting describes a specific calendar.
   */
  setImportSettings: async (holdsOnlyAppointments: boolean): Promise<{ holdsOnlyAppointments: boolean }> => {
    return apiPut<{ holdsOnlyAppointments: boolean }>('/googlecalendar/import-settings', { holdsOnlyAppointments });
  },
};

/** What one « Importer depuis Google » press did. `runId` is null when the clinic has no Google connection. */
export interface CalendarImportOutcome {
  runId: string | null;
  appointmentsCreated: number;
  patientsCreated: number;
  appointmentsUpdated: number;
  appointmentsLinked: number;
}

/** One recorded import pass. */
export interface CalendarImportRunDto {
  id: string;
  startedAtUtc: string;
  /** Already French — « Import automatique » for a scheduled pass, else the person's name. */
  triggeredBy: string;
  appointmentsCreated: number;
  patientsCreated: number;
  appointmentsUpdated: number;
  revertedAtUtc: string | null;
  /** How many of the rows it created still exist. Zero is why a run stops being offered for undo. */
  rowsRemaining: number;
  canRevert: boolean;
}

/** One row an undo will NOT delete, with the reason in French, ready to print. */
export interface CalendarImportKeptRow {
  id: string;
  label: string;
  when: string | null;
  reason: string;
}

export interface CalendarImportRevertPreview {
  runId: string;
  startedAtUtc: string;
  alreadyReverted: boolean;
  appointmentsToDelete: number;
  patientsToDelete: number;
  kept: CalendarImportKeptRow[];
}

export interface CalendarImportRevertResult {
  runId: string;
  appointmentsDeleted: number;
  patientsDeleted: number;
  kept: CalendarImportKeptRow[];
}

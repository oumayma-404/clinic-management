import { apiGet, apiPost } from './client';

export interface GoogleCalendarStatus {
  isConfigured: boolean;
  hasClientId: boolean;
  hasClientSecret: boolean;
  hasRefreshToken: boolean;
  tokenValid?: boolean;
  calendarId: string;
  message: string;
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
   * Initiate Google Calendar OAuth authorization
   * This will redirect the user to Google's authorization page
   */
  authorize: (): void => {
    const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
    window.location.href = `${apiUrl}/googlecalendar/authorize`;
  },

  /**
   * Sync from Google Calendar to clinic appointments.
   * Routed through the shared client.ts wrapper so a mid-request connectivity loss surfaces as
   * ApiError(status === 0) — unifying calendar failure handling with the AI path (AC-6.5, R-7).
   */
  syncFromGoogle: async (): Promise<{ message: string; timestamp: string }> => {
    return apiPost<{ message: string; timestamp: string }>('/googlecalendar/sync-from-google', {});
  },

  /**
   * Sync a specific appointment to Google Calendar (manual "Push to Google").
   * Routed through client.ts for the same ApiError(status === 0) offline signal (AC-6.5, R-7).
   */
  syncAppointment: async (appointmentId: string): Promise<{ message: string }> => {
    return apiPost<{ message: string }>(`/googlecalendar/sync-appointment/${appointmentId}`, {});
  },
};


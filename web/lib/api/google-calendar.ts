import { apiGet } from './client';

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
   * Sync from Google Calendar to clinic appointments
   */
  syncFromGoogle: async (): Promise<{ message: string; timestamp: string }> => {
    const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
    const response = await fetch(`${apiUrl}/googlecalendar/sync-from-google`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || 'Failed to sync from Google Calendar');
    }

    return response.json();
  },

  /**
   * Sync a specific appointment to Google Calendar
   */
  syncAppointment: async (appointmentId: string): Promise<{ message: string }> => {
    const apiUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';
    const response = await fetch(`${apiUrl}/googlecalendar/sync-appointment/${appointmentId}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
    });

    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.error || 'Failed to sync appointment to Google Calendar');
    }

    return response.json();
  },
};


import { apiGet, apiPut } from './client';
import type {
  AppointmentStatusMixDto,
  DashboardDto,
  DashboardPeriodKey,
  DashboardPreferencesDto,
} from './types';

export const dashboardApi = {
  /**
   * The whole dashboard in one call.
   *
   * `period` is the ONLY window input: the server derives both the current and the previous bounds from the clinic
   * clock, so the two halves of every comparison can never have been computed by different rules. The retired
   * `getStats` used to send six client-computed boundaries instead.
   */
  get: async (period: DashboardPeriodKey = 'Month', doctorId?: string): Promise<DashboardDto> => {
    return apiGet<DashboardDto>('/dashboard', { period, doctorId });
  },

  /**
   * « Rendez-vous par statut » for one window.
   *
   * Its own call because that card carries its own period control, so its window is not the page's. The bounds are
   * bare **day keys** (`yyyy-MM-dd`) and never instants: `new Date(day + 'T00:00:00').toISOString()` is midnight
   * on the *workstation*, so on a machine that is not UTC+1 the window is offset by hours — the AC-6 defect
   * la caisse had to fix. Omit both to get the current clinic-local week.
   */
  getAppointmentStatusMix: async (
    from?: string,
    to?: string,
    doctorId?: string,
  ): Promise<AppointmentStatusMixDto> => {
    return apiGet<AppointmentStatusMixDto>('/dashboard/appointments-by-status', { from, to, doctorId });
  },

  /**
   * The signed-in user's layout choices, plus every block the dashboard can show.
   *
   * `availableKpis` comes from the server rather than being derived client-side on purpose: it is the same set the
   * write path validates against, so the customiser can only ever offer what a save would accept.
   */
  getPreferences: async (): Promise<DashboardPreferencesDto> => {
    return apiGet<DashboardPreferencesDto>('/dashboard/preferences');
  },

  /**
   * Replaces the hidden set. Send the full intended state — this is a replace, not a patch, because a merge could
   * not express "show this one again".
   */
  updatePreferences: async (hiddenKpis: string[]): Promise<DashboardPreferencesDto> => {
    return apiPut<DashboardPreferencesDto>('/dashboard/preferences', { hiddenKpis });
  },
};

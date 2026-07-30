import { apiGet, apiPut } from './client';
import type { DashboardDto, DashboardPeriodKey, DashboardPreferencesDto } from './types';

export const dashboardApi = {
  /**
   * The whole dashboard in one call.
   *
   * `period` is the ONLY window input: the server derives both the current and the previous bounds from the clinic
   * clock, so the two halves of every comparison can never have been computed by different rules. The retired
   * `getStats` used to send six client-computed boundaries instead.
   */
  get: async (period: DashboardPeriodKey = 'Month'): Promise<DashboardDto> => {
    return apiGet<DashboardDto>('/dashboard', { period });
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

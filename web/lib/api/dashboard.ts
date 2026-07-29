import { apiGet } from './client';
import type { DashboardDto, DashboardPeriodKey } from './types';

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
};

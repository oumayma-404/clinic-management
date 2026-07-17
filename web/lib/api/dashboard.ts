import { apiGet } from './client';
import type { DashboardStats } from './types';

export const dashboardApi = {
  getStats: async (params?: {
    todayStart?: string;
    todayEnd?: string;
    weekStart?: string;
    weekEnd?: string;
    monthStart?: string;
    monthEnd?: string;
  }): Promise<DashboardStats> => {
    return apiGet<DashboardStats>('/dashboard/stats', params);
  },
};

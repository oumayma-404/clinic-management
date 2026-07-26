import { useState, useEffect, useCallback, useMemo } from 'react';
import { dashboardApi } from '@/lib/api/dashboard';
import type { DashboardStats } from '@/lib/api/types';
import { ApiError } from '@/lib/api/client';
import { startOfDay, endOfDay, startOfWeek, endOfWeek, startOfMonth, endOfMonth } from 'date-fns';

// Send UTC instants (like use-appointments.ts) — a bare wall-clock string is parsed by the API as
// UTC, shifting the day/week/month window by the clinic's offset and disagreeing with the agenda.
const formatRange = (date: Date) => date.toISOString();

export function useDashboardStats() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Stable "now" so the memoized ranges don't change every render (would loop).
  const now = useMemo(() => new Date(), []);

  const fetchStats = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);

      const data = await dashboardApi.getStats({
        todayStart: formatRange(startOfDay(now)),
        todayEnd: formatRange(endOfDay(now)),
        weekStart: formatRange(startOfWeek(now, { weekStartsOn: 1 })),
        weekEnd: formatRange(endOfWeek(now, { weekStartsOn: 1 })),
        monthStart: formatRange(startOfMonth(now)),
        monthEnd: formatRange(endOfMonth(now)),
      });
      setStats(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Échec du chargement des statistiques du tableau de bord');
    } finally {
      setLoading(false);
    }
  }, [now]);

  useEffect(() => {
    fetchStats();
  }, [fetchStats]);

  return { stats, loading, error, refetch: fetchStats };
}

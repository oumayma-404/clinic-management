import { useState, useEffect, useCallback } from 'react';
import { dashboardApi } from '@/lib/api/dashboard';
import type { DashboardDto, DashboardPeriodKey } from '@/lib/api/types';
import { getErrorMessage } from '@/lib/errors';

/**
 * Backs the dashboard page.
 *
 * <p>Note what is <b>absent</b> compared with the retired `useDashboardStats`: no date-fns range computation. That
 * hook built six boundaries client-side with `startOfMonth`/`endOfWeek` and shipped them to the API. The server now
 * owns the window, which is what lets the previous period be derived by the same rule as the current one.</p>
 *
 * <p>`refetching` is separate from `loading` so a period change can hold the previous render at reduced opacity
 * instead of blanking the page — no skeleton flash, no layout jump.</p>
 */
export function useDashboard(period: DashboardPeriodKey) {
  const [data, setData] = useState<DashboardDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [refetching, setRefetching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    async (isRefetch: boolean) => {
      try {
        if (isRefetch) setRefetching(true);
        else setLoading(true);
        setError(null);

        setData(await dashboardApi.get(period));
      } catch (err) {
        // The figure is left as-is on a refetch failure rather than wiped: a stale number with a visible error is
        // more useful than an empty dashboard, and « — » must keep meaning "no value", not "the request failed".
        setError(getErrorMessage(err, 'Le tableau de bord n’a pas pu être chargé.'));
      } finally {
        setLoading(false);
        setRefetching(false);
      }
    },
    [period],
  );

  useEffect(() => {
    // A period change is a refetch, not a first load — unless nothing has ever loaded.
    void load(data !== null);
    // `data` is deliberately not a dependency: including it would re-run on every successful load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load]);

  const refetch = useCallback(() => load(true), [load]);

  return { data, loading, refetching, error, refetch };
}

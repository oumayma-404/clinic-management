import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { appointmentsApi } from '@/lib/api/appointments';
import type { AppointmentDto } from '@/lib/api/types';
import { ApiError } from '@/lib/api/client';
import { startOfDay, endOfDay } from 'date-fns';

/**
 * Fetches the appointments in a date range.
 *
 * The `loading` / `refetching` split is the load-bearing part, and it is the same split `useDashboard` and
 * `usePagedList` already make. Before it, every fetch set `loading = true`, and the calendar renders its
 * loading branch *instead of* the grid — so clicking « ‹ » to see last week replaced the whole agenda with a
 * line of centred text, then rebuilt it and re-ran its scroll-positioning effect, throwing the user back to
 * the current time. Navigating the agenda is the single most repeated action in the product; it must not
 * blank. `loading` is now true only until the first successful load (show a skeleton); after that a fetch
 * sets `refetching`, and the caller dims the rows it already has instead of throwing them away.
 *
 * `reloadToken` replaces the calendar's old `key={refreshKey}` remount. A changed React `key` is a brand-new
 * component: it loses scroll position, re-reads the clinic's working hours, and flashes empty — and it fired
 * on every SignalR `appointments` broadcast, so a colleague booking a patient blanked *your* open calendar.
 * Bumping this token refetches in place instead.
 */
export function useAppointments(
  startDate?: Date,
  endDate?: Date,
  patientId?: string,
  doctorName?: string,
  doctorId?: string,
  reloadToken?: unknown
) {
  const [appointments, setAppointments] = useState<AppointmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [refetching, setRefetching] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Whether a fetch has ever succeeded — decides skeleton-vs-dim without reading state we have already
  // rendered this pass.
  const hasLoadedRef = useRef(false);
  // Guards against a slow earlier request resolving after a faster later one and overwriting the newer
  // window. Real for this hook: paging quickly through weeks issues overlapping requests.
  const requestIdRef = useRef(0);

  // Send the local day/week bounds as UTC instants (ISO 8601 with 'Z'). A bare local wall-clock string was
  // parsed by the API as UTC (via the DateTime value converter), shifting the window by the clinic's UTC
  // offset and dropping appointments near local midnight onto the wrong day. toISOString() is instant-
  // preserving through the model binder + converter, so the query matches the intended local day.
  const formattedStartDate = useMemo(() =>
    startDate ? startOfDay(startDate).toISOString() : undefined,
    [startDate?.getTime()]
  );
  const formattedEndDate = useMemo(() =>
    endDate ? endOfDay(endDate).toISOString() : undefined,
    [endDate?.getTime()]
  );

  const fetchAppointments = useCallback(async () => {
    const requestId = ++requestIdRef.current;
    if (hasLoadedRef.current) setRefetching(true);
    else setLoading(true);

    try {
      const params: { [key: string]: any } = {};

      if (formattedStartDate) {
        params.startDate = formattedStartDate;
      }
      if (formattedEndDate) {
        params.endDate = formattedEndDate;
      }
      if (patientId) {
        params.patientId = patientId;
      }
      if (doctorName) {
        params.doctorName = doctorName;
      }
      if (doctorId) {
        params.doctorId = doctorId;
      }

      const data = await appointmentsApi.list(params);
      if (requestId !== requestIdRef.current) return;
      setAppointments(data);
      setError(null);
      hasLoadedRef.current = true;
    } catch (err) {
      if (requestId !== requestIdRef.current) return;
      // Keep whatever is already on screen. Blanking the grid *and* showing an error would hide the rows the
      // user was looking at, which for an agenda is worse than a stale view with a banner over it.
      setError(err instanceof ApiError ? err.message : 'Échec du chargement des rendez-vous');
    } finally {
      if (requestId === requestIdRef.current) {
        setLoading(false);
        setRefetching(false);
      }
    }
    // `reloadToken` is a dependency on purpose: bumping it refetches the same window in place.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formattedStartDate, formattedEndDate, patientId, doctorName, doctorId, reloadToken]);

  useEffect(() => {
    fetchAppointments();
  }, [fetchAppointments]);

  return {
    appointments,
    /** True only before the first successful load — render a skeleton. */
    loading,
    /** True while refetching with rows already on screen — dim them, never blank them. */
    refetching,
    error,
    refetch: fetchAppointments,
  };
}












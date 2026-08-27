import { useCallback, useEffect, useState } from 'react';
import { dashboardApi } from '@/lib/api/dashboard';
import type { AppointmentStatusMixDto } from '@/lib/api/types';
import { getErrorMessage } from '@/lib/errors';

/**
 * Backs « Rendez-vous par statut ».
 *
 * <p><b>Its own hook, and its own request, because this card owns its window.</b> Everything else below the fold
 * reads `useDashboard`'s single response against the page's period; this card carries Semaine / Mois /
 * Personnalisé in its own header, so it cannot be served by a response whose window was decided elsewhere.</p>
 *
 * <p>`refetching` is separate from `loading` for the same reason it is in `useDashboard`: changing the window must
 * hold the previous columns at reduced opacity rather than blanking the card to a skeleton. A chart that flashes
 * empty on every period change reads as broken, and the height would jump on every click.</p>
 *
 * <p>⚠️ The bounds are <b>day keys</b> (`yyyy-Mm-dd`) all the way down — never `Date` objects. The card builds them
 * with `todayLocalIso`-style arithmetic and hands them over untouched, so nothing in this path can convert through
 * UTC and shift the clinic's day.</p>
 *
 * @param from Inclusive first clinic-local day, or undefined with `to` for the current clinic-local week.
 * @param to Inclusive last clinic-local day.
 * @param doctorId Narrows to one practitioner's own séances.
 */
export function useAppointmentStatusMix(from?: string, to?: string, doctorId?: string) {
  const [data, setData] = useState<AppointmentStatusMixDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [refetching, setRefetching] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(
    async (isRefetch: boolean) => {
      try {
        if (isRefetch) setRefetching(true);
        else setLoading(true);
        setError(null);

        setData(await dashboardApi.getAppointmentStatusMix(from, to, doctorId));
      } catch (err) {
        /*
         * The previous columns are deliberately LEFT STANDING on a refetch failure, with the error shown beside
         * them. Wiping them would turn a network fault into « aucun rendez-vous sur cette période » — a confident
         * statement about the practice, made because a request failed.
         *
         * The server's own French sentence is used when it sent one: the useful refusals here are its own
         * (« la période ne peut pas dépasser 366 jours », « la date de fin doit être postérieure… »), and
         * replacing them with a generic message would hide the only thing the user can act on.
         */
        setError(getErrorMessage(err, 'Les rendez-vous par statut n’ont pas pu être chargés.'));
      } finally {
        setLoading(false);
        setRefetching(false);
      }
    },
    [from, to, doctorId],
  );

  useEffect(() => {
    // A window change is a refetch, not a first load — unless nothing has ever arrived.
    void load(data !== null);
    // `data` is deliberately not a dependency: including it would re-run on every successful load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load]);

  const refetch = useCallback(() => load(true), [load]);

  return { data, loading, refetching, error, refetch };
}

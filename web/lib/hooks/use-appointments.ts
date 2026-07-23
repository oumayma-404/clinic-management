import { useState, useEffect, useCallback, useMemo } from 'react';
import { appointmentsApi } from '@/lib/api/appointments';
import type { AppointmentDto } from '@/lib/api/types';
import { ApiError } from '@/lib/api/client';
import { startOfDay, endOfDay } from 'date-fns';

export function useAppointments(
  startDate?: Date,
  endDate?: Date,
  patientId?: string,
  doctorName?: string,
  doctorId?: string
) {
  const [appointments, setAppointments] = useState<AppointmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
    try {
      setLoading(true);
      setError(null);

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
      setAppointments(data);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError('Échec du chargement des rendez-vous');
      }
    } finally {
      setLoading(false);
    }
  }, [formattedStartDate, formattedEndDate, patientId, doctorName, doctorId]);

  useEffect(() => {
    fetchAppointments();
  }, [fetchAppointments]);

  return { appointments, loading, error, refetch: fetchAppointments };
}












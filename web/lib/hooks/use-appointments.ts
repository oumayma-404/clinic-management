import { useState, useEffect, useCallback, useMemo } from 'react';
import { appointmentsApi } from '@/lib/api/appointments';
import type { AppointmentDto } from '@/lib/api/types';
import { ApiError } from '@/lib/api/client';
import { format, startOfDay, endOfDay } from 'date-fns';

export function useAppointments(
  startDate?: Date,
  endDate?: Date,
  patientId?: string,
  doctorName?: string
) {
  const [appointments, setAppointments] = useState<AppointmentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Memoize date strings for stable dependencies
  const formattedStartDate = useMemo(() => 
    startDate ? format(startOfDay(startDate), "yyyy-MM-dd'T'HH:mm:ss") : undefined, 
    [startDate?.getTime()]
  );
  const formattedEndDate = useMemo(() => 
    endDate ? format(endOfDay(endDate), "yyyy-MM-dd'T'HH:mm:ss") : undefined, 
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

      const data = await appointmentsApi.list(params);
      setAppointments(data);
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message);
      } else {
        setError('Failed to load appointments');
      }
    } finally {
      setLoading(false);
    }
  }, [formattedStartDate, formattedEndDate, patientId, doctorName]);

  useEffect(() => {
    fetchAppointments();
  }, [fetchAppointments]);

  return { appointments, loading, error, refetch: fetchAppointments };
}



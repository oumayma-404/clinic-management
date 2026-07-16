import { useState, useEffect, useMemo } from 'react';
import { appointmentsApi } from '@/lib/api/appointments';
import type { AppointmentDto } from '@/lib/api/types';
import { format, startOfDay, endOfDay } from 'date-fns';
import { parseDurationToMinutes } from '@/lib/utils';

interface OverlapOptions {
  /** Only fetch/compute while the dialog is open (avoids fetching for a closed, always-mounted dialog). */
  enabled: boolean;
  date?: Date;
  startHour: string;
  startMinute: string;
  durationMinutes: number;
  /** Edit: exclude the appointment being edited from the overlap check. */
  excludeAppointmentId?: string;
}

/**
 * Advisory overlap detection for the appointment dialogs (AC-3). Fetches the selected day's
 * appointments once per day via the existing appointmentsApi and returns a French warning message
 * naming the first conflicting appointment, or null when there is no overlap.
 *
 * Non-blocking by design: a fetch failure silently disables the warning (never blocks booking),
 * cancelled appointments are ignored, and busy ("Occupé") slots count as overlaps.
 */
export function useAppointmentOverlap({
  enabled,
  date,
  startHour,
  startMinute,
  durationMinutes,
  excludeAppointmentId,
}: OverlapOptions): string | null {
  const [dayAppointments, setDayAppointments] = useState<AppointmentDto[]>([]);

  const dayKey = date ? format(date, 'yyyy-MM-dd') : undefined;

  useEffect(() => {
    if (!enabled || !date) {
      setDayAppointments([]);
      return;
    }
    let cancelled = false;
    appointmentsApi
      .list({
        startDate: format(startOfDay(date), "yyyy-MM-dd'T'HH:mm:ss"),
        endDate: format(endOfDay(date), "yyyy-MM-dd'T'HH:mm:ss"),
      })
      .then((data) => {
        if (!cancelled) setDayAppointments(data);
      })
      .catch(() => {
        // Silently disable the warning on a fetch failure (spec Edge Cases) — never block booking.
        if (!cancelled) setDayAppointments([]);
      });
    return () => {
      cancelled = true;
    };
    // Refetch only when the day (or enabled) changes; time/duration edits recompute below.
  }, [enabled, dayKey]);

  return useMemo(() => {
    if (!enabled || !date || durationMinutes <= 0) return null;

    const start = new Date(date);
    start.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0);
    const end = new Date(start.getTime() + durationMinutes * 60000);

    const conflict = dayAppointments.find((apt) => {
      if (apt.id === excludeAppointmentId) return false;
      if (apt.status.toLowerCase() === 'cancelled') return false;
      const aptStart = new Date(apt.appointmentDateTime);
      const aptEnd = new Date(aptStart.getTime() + parseDurationToMinutes(apt.duration) * 60000);
      return start < aptEnd && end > aptStart;
    });

    if (!conflict) return null;
    const label = conflict.patientName || 'Occupé';
    return `Chevauchement avec « ${label} » à ${format(new Date(conflict.appointmentDateTime), 'HH:mm')}`;
  }, [enabled, date, startHour, startMinute, durationMinutes, excludeAppointmentId, dayAppointments]);
}

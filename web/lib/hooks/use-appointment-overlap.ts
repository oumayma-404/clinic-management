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
  /** Practitioner being booked — a clash with the SAME doctor is a hard (blocking) conflict. */
  doctorId?: string;
  /** Edit: exclude the appointment being edited from the overlap check. */
  excludeAppointmentId?: string;
}

export interface OverlapResult {
  /** French warning message naming the first conflict, or null when there is no overlap. */
  warning: string | null;
  /** True only for a hard clash with the SAME practitioner — the dialog blocks Save on this. */
  blocking: boolean;
}

/**
 * Overlap detection for the appointment dialogs. Fetches the selected day's appointments once per day
 * via the existing appointmentsApi and classifies a conflict as either a hard clash with the same
 * practitioner (`blocking` — mirrors the server-side double-booking guard) or a soft advisory overlap
 * with another practitioner (non-blocking amber hint).
 *
 * A fetch failure silently disables the warning (never blocks booking); cancelled / no-show
 * appointments are ignored; busy ("Occupé") slots count as overlaps.
 */
export function useAppointmentOverlap({
  enabled,
  date,
  startHour,
  startMinute,
  durationMinutes,
  doctorId,
  excludeAppointmentId,
}: OverlapOptions): OverlapResult {
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

  return useMemo<OverlapResult>(() => {
    if (!enabled || !date || durationMinutes <= 0) return { warning: null, blocking: false };

    const start = new Date(date);
    start.setHours(Number.parseInt(startHour), Number.parseInt(startMinute), 0, 0);
    const end = new Date(start.getTime() + durationMinutes * 60000);

    const conflicts = dayAppointments.filter((apt) => {
      if (apt.id === excludeAppointmentId) return false;
      const status = apt.status.toLowerCase();
      if (status === 'cancelled' || status === 'noshow') return false;
      const aptStart = new Date(apt.appointmentDateTime);
      const aptEnd = new Date(aptStart.getTime() + parseDurationToMinutes(apt.duration) * 60000);
      return start < aptEnd && end > aptStart;
    });

    if (conflicts.length === 0) return { warning: null, blocking: false };

    // A hard clash is one with the SAME practitioner being booked (other-doctor overlaps stay advisory).
    const sameDoctor = doctorId ? conflicts.find((c) => c.doctorId && c.doctorId === doctorId) : undefined;
    const conflict = sameDoctor ?? conflicts[0];
    const label = conflict.patientName || 'Occupé';
    const time = format(new Date(conflict.appointmentDateTime), 'HH:mm');

    if (sameDoctor) {
      return { warning: `Ce créneau est déjà réservé pour ce praticien : « ${label} » à ${time}`, blocking: true };
    }
    return { warning: `Chevauchement avec « ${label} » à ${time}`, blocking: false };
  }, [enabled, date, startHour, startMinute, durationMinutes, doctorId, excludeAppointmentId, dayAppointments]);
}

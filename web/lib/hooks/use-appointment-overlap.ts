import { useState, useEffect, useMemo } from 'react';
import { appointmentsApi } from '@/lib/api/appointments';
import type { AppointmentDto } from '@/lib/api/types';
import { format, startOfDay, endOfDay } from 'date-fns';
import { parseDurationToMinutes } from '@/lib/utils';
import { quoteFr } from "@/lib/format";

interface OverlapOptions {
  /** Only fetch/compute while the dialog is open (avoids fetching for a closed, always-mounted dialog). */
  enabled: boolean;
  date?: Date;
  startHour: string;
  startMinute: string;
  durationMinutes: number;
  /** Practitioner being booked — a clash with the SAME doctor is the loud case (still not a block). */
  doctorId?: string;
  /** Edit: exclude the appointment being edited from the overlap check. */
  excludeAppointmentId?: string;
}

export interface OverlapResult {
  /** French warning message naming the first conflict, or null when there is no overlap. */
  warning: string | null;
  /**
   * True only for a clash with the SAME practitioner. Named for what it IS, not for what it used to cause: this no
   * longer blocks Save. The collision is advisory, and the server offers an explicit override
   * (`slot_taken` → `allowOverlap`), so this only drives how loudly the warning is styled.
   */
  samePractitioner: boolean;
}

/**
 * Overlap detection for the appointment dialogs. Fetches the selected day's appointments once per day
 * via the existing appointmentsApi and classifies a conflict as either a clash with the same
 * practitioner (`samePractitioner` — the loud case, mirroring the server-side guard) or a softer overlap with
 * another practitioner. **Neither blocks**: the server treats a collision as advisory and offers an explicit
 * override, so this hook only decides how the warning reads.
 *
 * A fetch failure silently disables the warning; cancelled / no-show
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
    if (!enabled || !date || durationMinutes <= 0) return { warning: null, samePractitioner: false };

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

    if (conflicts.length === 0) return { warning: null, samePractitioner: false };

    // A hard clash is one with the SAME practitioner being booked (other-doctor overlaps stay advisory).
    const sameDoctor = doctorId ? conflicts.find((c) => c.doctorId && c.doctorId === doctorId) : undefined;
    const conflict = sameDoctor ?? conflicts[0];
    const time = format(new Date(conflict.appointmentDateTime), 'HH:mm');
    // The name is the PATIENT already in the slot — never the practitioner. The old copy read
    // « réservé pour ce praticien : « <nom> » », which named a patient right after the word "praticien" and so
    // said the opposite of what it meant. A busy slot with no patient ("Occupé") has no name to show at all,
    // so it gets its own wording rather than « patient : « Occupé » ».
    const withWhom = conflict.patientName ? `patient : ${quoteFr(conflict.patientName)}` : 'créneau occupé';

    if (sameDoctor) {
      return {
        warning: `Ce praticien a déjà un rendez-vous à ${time} (${withWhom})`,
        samePractitioner: true,
      };
    }
    return { warning: `Chevauchement à ${time} avec un autre praticien (${withWhom})`, samePractitioner: false };
  }, [enabled, date, startHour, startMinute, durationMinutes, doctorId, excludeAppointmentId, dayAppointments]);
}

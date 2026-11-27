import { apiGet, apiPost, apiPut } from './client';
import type {
  WaitingRoomChangeDto,
  WaitingRoomDentistDto,
  WaitingRoomDto,
  WaitingRoomLane,
  WaitingRoomOtherDayVisitDto,
} from './types';

/**
 * The « Salle d'attente » (`salle-attente`): today's board and every move on it. Each move answers with what its
 * « Annuler » posts back to `restore` — the browser never composes an undo of its own.
 */
export const waitingRoomApi = {
  /** Today's board, optionally narrowed to one dentist's chair. */
  get: (doctorId?: string | null) =>
    apiGet<WaitingRoomDto>('/appointments/waiting-room', doctorId ? { doctorId } : undefined),

  /**
   * « Arrivé », « Faire entrer », « Terminer », « Parti sans être vu », « Remettre… », and every drag. `doctorId` is
   * the answer to « Quel praticien ? » when a cabinet de groupe seats a visit naming no dentist.
   */
  move: (appointmentId: string, lane: Exclude<WaitingRoomLane, 'None'>, doctorId?: string | null) =>
    apiPost<WaitingRoomChangeDto>(`/appointments/${appointmentId}/waiting-room`, { lane, doctorId: doctorId ?? null }),

  /** « Annuler »: the change the action answered with, sent back unchanged — refused if a card moved since. */
  restore: (change: Pick<WaitingRoomChangeDto, 'before' | 'after' | 'removeWalkInIds'>) =>
    apiPost<WaitingRoomChangeDto>('/appointments/waiting-room/restore', {
      before: change.before,
      after: change.after ?? [],
      removeWalkInIds: change.removeWalkInIds,
    }),

  /**
   * « Sans RDV » — marks the patient's RDV of the day arrived instead when he has one. With `appointmentId` (a RDV
   * on another day) it is « Le recevoir aujourd'hui »: that RDV moves to now, arrived.
   */
  walkIn: (patientId: string, doctorId?: string | null, appointmentId?: string | null) =>
    apiPost<WaitingRoomChangeDto>('/appointments/waiting-room/walk-in', {
      patientId,
      doctorId: doctorId ?? null,
      appointmentId: appointmentId ?? null,
    }),

  /** Each patient's next RDV on another day — offered as « Le recevoir aujourd'hui ». */
  otherDay: (patientIds: string[]) =>
    apiGet<WaitingRoomOtherDayVisitDto[]>('/appointments/waiting-room/other-day', { patientIds: patientIds.join(',') }),

  /** « Pas là » — the patient called in is not there: back to the salle d'attente, the next one is called. */
  notHere: (appointmentId: string) =>
    apiPost<WaitingRoomChangeDto>(`/appointments/${appointmentId}/waiting-room/not-here`, {}),

  /** « Passer en premier », or its removal. */
  setUrgent: (appointmentId: string, urgent: boolean) =>
    apiPost<WaitingRoomChangeDto>(`/appointments/${appointmentId}/waiting-room/urgent`, { urgent }),

  /** « Changer de praticien » — a visit whose dentist is on congé or gone from the team. */
  setDentist: (appointmentId: string, doctorId: string) =>
    apiPost<WaitingRoomChangeDto>(`/appointments/${appointmentId}/waiting-room/dentist`, { doctorId }),

  /** « 2 fauteuils » — for the caller's own chair, or (an administrator) any dentist's. */
  setTwoChairs: (twoChairs: boolean, doctorId?: string | null) =>
    apiPut<WaitingRoomDentistDto>('/appointments/waiting-room/chairs', { doctorId: doctorId ?? null, twoChairs }),

  /**
   * « Patient suivant », or « Appeler » on one patient. `expectedNextId` is the patient the button was showing:
   * a second press for the same person is refused rather than calling the one after him.
   */
  callNext: (options: { appointmentId?: string | null; expectedNextId?: string | null } = {}) =>
    apiPost<WaitingRoomChangeDto>('/appointments/waiting-room/call-next', {
      appointmentId: options.appointmentId ?? null,
      expectedNextId: options.expectedNextId ?? null,
    }),
};

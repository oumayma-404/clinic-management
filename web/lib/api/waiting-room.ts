import { apiGet, apiPost } from './client';
import type { WaitingRoomChangeDto, WaitingRoomDto, WaitingRoomLane } from './types';

/**
 * The « Salle d'attente » (`salle-attente`): today's board and every move on it. Each move answers with what its
 * « Annuler » posts back to `restore` — the browser never composes an undo of its own.
 */
export const waitingRoomApi = {
  /** Today's board, optionally narrowed to one dentist's chair. */
  get: (doctorId?: string | null) =>
    apiGet<WaitingRoomDto>('/appointments/waiting-room', doctorId ? { doctorId } : undefined),

  /** « Arrivé », « Faire entrer », « Terminer », « Parti sans être vu », « Remettre… », and every drag. */
  move: (appointmentId: string, lane: Exclude<WaitingRoomLane, 'None'>) =>
    apiPost<WaitingRoomChangeDto>(`/appointments/${appointmentId}/waiting-room`, { lane }),

  /** « Annuler »: the change the action answered with, sent back unchanged. */
  restore: (change: Pick<WaitingRoomChangeDto, 'before' | 'removeWalkInIds'>) =>
    apiPost<WaitingRoomChangeDto>('/appointments/waiting-room/restore', {
      before: change.before,
      removeWalkInIds: change.removeWalkInIds,
    }),

  /** « Sans RDV » — marks the patient's RDV of the day arrived instead when he has one. */
  walkIn: (patientId: string, doctorId?: string | null) =>
    apiPost<WaitingRoomChangeDto>('/appointments/waiting-room/walk-in', { patientId, doctorId: doctorId ?? null }),

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

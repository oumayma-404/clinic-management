import { format } from 'date-fns';
import { fr } from 'date-fns/locale';
import { dayFromKey } from '@/lib/doctor-leaves';
import { apiDelete, apiGet, apiPost, apiPut } from './client';
import { fetchExportCsv } from './export';
import type { UserRole } from './users';

/**
 * Pointage (`pointage-borne`): the borne's two anonymous calls and the admin's presences.
 *
 * ⚠️ The borne calls pass `null` as the token on purpose: nobody is signed in on a reception device, and the
 * device proves what it is with an HttpOnly cookie the browser sends by itself (`credentials: 'include'`).
 */

/** « out » pas encore arrivée / partie · « in » présente · « leave » en congé toute la journée. */
export type PointageState = 'out' | 'in' | 'leave';

export interface PointagePersonDto {
  userId: string;
  name: string;
  initials: string;
  state: PointageState;
  /** « 08:02 » while « in ». */
  since: string | null;
  needsSchema: boolean;
  lockedUntil: string | null;
}

export interface PointageStatusDto {
  isBorne: boolean;
  /** Which borne this device is; what « Paramètres → Équipe » marks « cet appareil » and replaces. */
  terminalId: string | null;
  name: string | null;
  /** False on a borne whose cabinet switched the pointage off. */
  enabled: boolean;
  people: PointagePersonDto[];
}

export interface PunchResultDto {
  action: 'arrival' | 'departure';
  at: string;
  name: string;
  terminalName: string;
}

export interface AttendanceTerminalDto {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
  isActive: boolean;
  version: number;
}

export interface AttendancePersonSettingDto {
  userId: string;
  fullName: string;
  role: UserRole;
  isActive: boolean;
  tracked: boolean;
  /** The admin's choice; null when the account follows its role. */
  trackedOverride: boolean | null;
  hasSchema: boolean;
  lockedUntil: string | null;
  version: number;
}

export interface AttendanceSettingsDto {
  terminals: AttendanceTerminalDto[];
  people: AttendancePersonSettingDto[];
}

/** One arrival/departure line; times are cabinet-local « HH:mm ». */
export interface AttendanceEntryDto {
  id: string;
  userId: string;
  userName: string;
  localDay: string;
  arrival: string;
  departure: string | null;
  /** Open on today's line: she is still there. */
  isPresent: boolean;
  /** Open on an earlier day: « Départ non pointé ». No hour is invented for it. */
  departureMissing: boolean;
  isCorrected: boolean;
  correctionReason: string | null;
  correctedByName: string | null;
  correctedAt: string | null;
  punchedArrival: string | null;
  punchedDeparture: string | null;
  terminalName: string | null;
  minutes: number | null;
  version: number;
}

export interface AttendanceLeaveMarkDto {
  userId: string;
  typeLabel: string;
  halfDay: boolean;
}

export interface AttendanceDayDto {
  day: string;
  isOpenDay: boolean;
  isToday: boolean;
  entries: AttendanceEntryDto[];
  leaves: AttendanceLeaveMarkDto[];
}

export interface AttendancePersonDto {
  userId: string;
  fullName: string;
  isActive: boolean;
}

export interface AttendanceTotalsDto {
  userId: string;
  days: number;
  minutes: number;
  missingDepartures: number;
  /** A line still open today — its hours are not counted yet. */
  openToday: boolean;
}

export interface AttendanceSheetDto {
  /** « 2026-11 ». */
  month: string;
  monthLabel: string;
  people: AttendancePersonDto[];
  days: AttendanceDayDto[];
  totals: AttendanceTotalsDto[];
}

export interface TeamTodayPersonDto {
  userId: string;
  fullName: string;
  state: PointageState;
  since: string | null;
  leaveTypeLabel: string | null;
  /** The congé covers half the day: she is expected for the other half, so the state stays « out ». */
  leaveHalfDay: boolean;
  missingDepartureOn: string | null;
}

export interface TeamTodayDto {
  day: string;
  people: TeamTodayPersonDto[];
  /** Null while the congés module is off. */
  pendingLeaveCount: number | null;
}

export interface MyAttendanceDto {
  tracked: boolean;
  hasSchema: boolean;
  sheet: AttendanceSheetDto;
}

/** Failure codes the borne branches on. Mirrors `AttendanceRules` on the server. */
export const PointageErrorCode = {
  TerminalUnknown: 'terminal_unknown',
  SchemaWrong: 'schema_wrong',
  SchemaLocked: 'schema_locked',
  SchemaMissing: 'schema_missing',
  ModuleOff: 'team_module_off',
  PersonNotTracked: 'person_not_tracked',
  OnLeave: 'on_leave',
  SchemaInvalid: 'schema_invalid',
  PunchStateChanged: 'punch_state_changed',
} as const;

/** Mirrors `AttendanceRules.MinDots` / `MaxDots`. */
export const SCHEMA_MIN_DOTS = 4;
export const SCHEMA_MAX_DOTS = 9;
/** Mirrors `User.MaxSchemaFailures`. */
export const SCHEMA_MAX_FAILURES = 5;
/** Mirrors `AttendanceTerminal.MaxNameLength`. */
export const TERMINAL_NAME_MAX = 60;
/** Mirrors `AttendanceEntry.MaxReasonLength`. */
export const CORRECTION_REASON_MAX = 500;
/** Mirrors `AttendanceRules.RegisterTerminalStepUpAction`. */
export const REGISTER_TERMINAL_STEP_UP_ACTION = 'register-attendance-terminal';

/** « Présente depuis 08:02 », « Pas encore arrivée », « En congé · Congé payé » — one wording for the borne and the desk. */
export function pointageStateLabel(person: {
  state: PointageState;
  since: string | null;
  leaveTypeLabel?: string | null;
  leaveHalfDay?: boolean;
}): string {
  if (person.state === 'in') return `Présente depuis ${person.since ?? ''}`.trim();
  if (person.state === 'leave') return person.leaveTypeLabel ? `En congé · ${person.leaveTypeLabel}` : 'En congé';
  if (person.leaveHalfDay) return `Pas encore arrivée · demi-journée de congé`;
  return 'Pas encore arrivée';
}

/** « jeudi 5 novembre » from a `YYYY-MM-DD` key. */
export const attendanceDayLabel = (key: string) => format(dayFromKey(key), 'EEEE d MMMM', { locale: fr });

const userPath = (userId: string) => `/team/users/${encodeURIComponent(userId)}`;

export const pointageApi = {
  /** Anonymous: whether this device is a borne, and its tiles. */
  status: () => apiGet<PointageStatusDto>('/pointage/status', undefined, null),

  /** Anonymous: an arrival or a departure, proved by the schéma (dots 0–8 in drawing order). */
  punch: (userId: string, pattern: number[], expected: PunchResultDto['action']) =>
    apiPost<PunchResultDto>('/pointage/punch', { userId, pattern, expected }, null),

  settings: () => apiGet<AttendanceSettingsDto>('/team/attendance-settings'),

  /** Needs a fresh step-up token; the response sets the borne cookie on THIS device and retires the one it replaces. */
  registerTerminal: (name: string, stepUpToken: string, replacingTerminalId: string | null) =>
    apiPost<AttendanceTerminalDto>('/team/terminals', { name, stepUpToken, replacingTerminalId }),

  revokeTerminal: (id: string) => apiDelete<AttendanceTerminalDto>(`/team/terminals/${id}`),

  setMySchema: (pattern: number[]) => apiPut<AttendancePersonSettingDto>('/team/me/schema', { pattern }),

  resetSchema: (userId: string) => apiDelete<AttendancePersonSettingDto>(`${userPath(userId)}/schema`),

  /** `tracked` null follows the role (secrétaire = pointe). */
  setTracked: (userId: string, tracked: boolean | null, version: number) =>
    apiPut<AttendancePersonSettingDto>(`${userPath(userId)}/attendance-profile`, { tracked, version }),

  sheet: (month?: string, userId?: string) => apiGet<AttendanceSheetDto>('/team/attendance', { month, userId }),

  exportSheet: (month: string) => fetchExportCsv('/team/attendance/export', { month }, undefined, 'presences.csv'),

  mine: (month?: string) => apiGet<MyAttendanceDto>('/team/me/attendance', { month }),

  correct: (id: string, body: { arrival: string; departure: string | null; reason: string; version: number }) =>
    apiPost<AttendanceEntryDto>(`/team/attendance/${id}/correction`, body),

  record: (body: { userId: string; day: string; arrival: string; departure: string | null; reason: string }) =>
    apiPost<AttendanceEntryDto>('/team/attendance', body),

  today: () => apiGet<TeamTodayDto>('/team/today'),
};

/** « 7 h 05 » from minutes; « 0 h » for none. */
export function formatAttendanceMinutes(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} h` : `${h} h ${String(m).padStart(2, '0')}`;
}

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
/** `left`: today's last line is closed (« Départ à 17:02 »), never « pas encore » again. */
export type PointageState = 'out' | 'in' | 'leave' | 'left';

export interface PointagePersonDto {
  userId: string;
  name: string;
  initials: string;
  state: PointageState;
  /** « 08:02 » while « in ». */
  since: string | null;
  needsSchema: boolean;
  lockedUntil: string | null;
  /** « 17:02 » when `state` is `left`. */
  leftAt: string | null;
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
  /** Chosen for another signed-in device that has not picked it up yet: nobody can punch on it. */
  isPending: boolean;
  /** « Ines Gharbi · Chrome sur Windows » — the device it was chosen for, when chosen from the list. */
  deviceDescription: string | null;
  /** Still waiting while that device signed out: it will never pick it up. */
  deviceSignedOut: boolean;
  /** When it became a working borne (direct registration or claim); null while waiting. */
  activatedAt: string | null;
  version: number;
}

/** « Ajouter une borne »: one signed-in device of the cabinet (`GET /team/terminals/devices`). */
export interface BorneDeviceDto {
  sessionId: string;
  personName: string;
  /** « Chrome sur Windows »; null when the device gave no name. */
  deviceLabel: string | null;
  lastActiveAt: string;
  /** From the device label; tracked people's phones are never offered. */
  kind: 'pc' | 'tablet' | 'phone' | 'unknown';
  /** The admin's own device, offered as « Cet appareil ». */
  isThisDevice: boolean;
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

/** Where the pointage setup stands, read server-side so the checklist cannot disagree with the lock rule. */
export interface AttendanceSetupDto {
  hoursSet: boolean;
  borneLive: boolean;
  bornePending: boolean;
  /** An open day past opening + 30 min with nobody pointed, while a borne is live: it may be dead. */
  noPunchSinceOpening: boolean;
  trackedCount: number;
  /** Full names of active tracked people with no schéma. */
  schemasMissing: string[];
  /** `off` switch off · `active` locking now · `from` armed, starts at `lockActiveFrom` · `inactive` see reason. */
  lockState: 'off' | 'active' | 'from' | 'inactive';
  lockActiveFrom: string | null;
  lockInactiveReason: 'hours_unset' | 'no_borne' | 'borne_pending' | null;
}

export interface AttendanceSettingsDto {
  terminals: AttendanceTerminalDto[];
  people: AttendancePersonSettingDto[];
  setup: AttendanceSetupDto;
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
  /** The departure is the hour she declared the next morning (« Déclaré »), untouched since. */
  departureDeclared: boolean;
  /** Beside « Déclaré »: her last save in the app that day (« HH:mm »), null for « aucune ». */
  declaredLastSave: string | null;
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
  /** `YYYY-MM-DD`: from this day an empty open day is « Aucun pointage »; null = never (nobody has pointed yet). */
  countsFrom: string | null;
}

export interface AttendanceTotalsDto {
  userId: string;
  days: number;
  minutes: number;
  missingDepartures: number;
  /** Lines whose departure she declared — counted in `minutes` like any closed line. */
  declaredDepartures: number;
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
  leftAt: string | null;
  needsSchema: boolean;
  schemaLockedUntil: string | null;
}

export interface TeamTodayDto {
  day: string;
  people: TeamTodayPersonDto[];
  isOpenDay: boolean;
  /** One setup problem worth a line above the list, or null. */
  setupIssue: 'no_borne' | 'borne_pending' | 'no_punch_since_opening' | null;
  /** Null while the congés module is off. */
  pendingLeaveCount: number | null;
}

export interface MyAttendanceDto {
  tracked: boolean;
  hasSchema: boolean;
  sheet: AttendanceSheetDto;
}

/** « Pointez votre arrivée » and « Pointer votre départ ? » (`GET /pointage/me/clock-status`). */
export interface ClockStatusDto {
  /** Her own account — what the pad inside the lock punches for. */
  userId: string;
  name: string;
  /** The server's one lock rule — the 423 on a save asks it too. */
  locked: boolean;
  /** The cabinet's active bornes, by name. */
  bornes: string[];
  /** « 08:02 » while today's line is open. */
  presentSince: string | null;
  /** This device is one of her cabinet's bornes: the pad opens right here. */
  canClockOutHere: boolean;
  hasSchema: boolean;
  /** An admin chose this device as the borne named here: the app claims it at once (`claimBorne`). */
  borneWaiting: string | null;
  /** Her account points at all (false: no chip, no « Mes présences »). */
  tracked: boolean;
  /** « 12:05 » when today's last line is closed. */
  leftAt: string | null;
  /** UTC instant while 5 wrong schémas block her. */
  schemaLockedUntil: string | null;
}

/** An earlier day left open, as the borne's declaration step shows it (409 `previous_departure_missing`). */
export interface PreviousDay {
  /** `YYYY-MM-DD`. */
  day: string;
  arrival: string;
  /** Her last save in the app that day, or null for « aucune ». */
  lastSaveAt: string | null;
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
  /** Mirrors `AttendanceLockRules.PreviousDepartureMissingCode`. */
  PreviousDepartureMissing: 'previous_departure_missing',
  /** Mirrors `AttendanceLockRules.DeclaredDepartureInvalidCode`. */
  DeclaredDepartureInvalid: 'declared_departure_invalid',
  /** The device chosen in « Ajouter une borne » signed out meanwhile. */
  DeviceGone: 'borne_device_gone',
  /** Nothing waits for this device (already claimed, or retired). */
  BorneNotWaiting: 'borne_not_waiting',
  /** Another live borne already has this name. */
  BorneNameTaken: 'borne_name_taken',
  /** A tracked person's phone cannot be the borne. */
  BorneDevicePhone: 'borne_device_phone',
  /** Retiring the last live borne while someone is present. */
  BorneLastWhilePresent: 'borne_last_while_present',
  /** Replacing an existing schéma needs the account password. */
  SchemaPasswordRequired: 'schema_password_required',
  SchemaPasswordWrong: 'schema_password_wrong',
} as const;

/** The day, arrival and last save a `previous_departure_missing` refusal carries — or null for any other error. */
export function previousDayOf(details: Readonly<Record<string, unknown>> | undefined): PreviousDay | null {
  if (!details || typeof details.day !== 'string' || typeof details.arrival !== 'string') return null;
  return {
    day: details.day,
    arrival: details.arrival,
    lastSaveAt: typeof details.lastSaveAt === 'string' ? details.lastSaveAt : null,
  };
}

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

/** « Arrivée à 08:02 », « Départ à 17:02 », « Pas encore de pointage » — one wording, any account, borne and desk alike. */
export function pointageStateLabel(person: {
  state: PointageState;
  since: string | null;
  leftAt?: string | null;
  leaveTypeLabel?: string | null;
  leaveHalfDay?: boolean;
}): string {
  if (person.state === 'in') return `Arrivée à ${person.since ?? ''}`.trim();
  if (person.state === 'left') return `Départ à ${person.leftAt ?? ''}`.trim();
  if (person.state === 'leave') return person.leaveTypeLabel ? `En congé · ${person.leaveTypeLabel}` : 'En congé';
  if (person.leaveHalfDay) return 'Pas encore de pointage · demi-journée de congé';
  return 'Pas encore de pointage';
}

/** « jeudi 5 novembre » from a `YYYY-MM-DD` key. */
export const attendanceDayLabel = (key: string) => format(dayFromKey(key), 'EEEE d MMMM', { locale: fr });

const userPath = (userId: string) => `/team/users/${encodeURIComponent(userId)}`;

export const pointageApi = {
  /** Anonymous: whether this device is a borne, and its tiles. */
  status: () => apiGet<PointageStatusDto>('/pointage/status', undefined, null),

  /**
   * Anonymous: an arrival or a departure, proved by the schéma (dots 0–8 in drawing order). `declaredPreviousDeparture`
   * answers a `previous_departure_missing` refusal: the hour she left on that earlier day.
   */
  punch: (
    userId: string,
    pattern: number[],
    expected: PunchResultDto['action'],
    declaredPreviousDeparture?: string,
  ) => apiPost<PunchResultDto>('/pointage/punch', { userId, pattern, expected, declaredPreviousDeparture }, null),

  /** Her own account: locked or not, where the bornes are, and whether this device is one. */
  clockStatus: () => apiGet<ClockStatusDto>('/pointage/me/clock-status'),

  settings: () => apiGet<AttendanceSettingsDto>('/team/attendance-settings'),

  /** The cabinet's signed-in devices « Ajouter une borne » offers (admin). */
  borneDevices: () => apiGet<BorneDeviceDto[]>('/team/terminals/devices'),

  /**
   * Needs a fresh step-up token. With no `deviceSessionId` the response sets the borne cookie on THIS device and retires
   * the one it replaces; with one, the borne waits for that device, which claims it on its next clock-status read.
   */
  registerTerminal: (
    name: string,
    stepUpToken: string,
    replacingTerminalId: string | null,
    deviceSessionId: string | null = null,
  ) => apiPost<AttendanceTerminalDto>('/team/terminals', { name, stepUpToken, replacingTerminalId, deviceSessionId }),

  /** This device was chosen as a borne: picks it up and receives its cookie. */
  claimBorne: () => apiPost<AttendanceTerminalDto>('/pointage/me/claim-borne', {}),

  revokeTerminal: (id: string) => apiDelete<AttendanceTerminalDto>(`/team/terminals/${id}`),

  /** `currentPassword` is required (and checked) when she already has a schéma. */
  setMySchema: (pattern: number[], currentPassword?: string) =>
    apiPut<AttendancePersonSettingDto>('/team/me/schema', { pattern, currentPassword }),

  resetSchema: (userId: string) => apiDelete<AttendancePersonSettingDto>(`${userPath(userId)}/schema`),

  /** Lifts a 5-wrong-schémas block and keeps the schéma. */
  unlockSchema: (userId: string) => apiPost<AttendancePersonSettingDto>(`${userPath(userId)}/schema/unlock`, {}),

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

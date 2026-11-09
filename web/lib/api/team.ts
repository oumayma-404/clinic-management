import { apiGet, apiPost, apiPut } from './client';
import type { UserRole } from './users';

/** `StaffLeaveType` member names — what the server sends and accepts. */
export type StaffLeaveType = 'PaidLeave' | 'Sick' | 'Unpaid' | 'Other';
export type StaffLeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
/** Which half of a demi-journée; null on a whole day (or an older half day nobody qualified). */
export type StaffLeaveHalfDayPart = 'Morning' | 'Afternoon';

/** One secretary congé request (`StaffLeaveRequestDto`). Days are `YYYY-MM-DD` clinic-local keys. */
export interface StaffLeaveRequestDto {
  id: string;
  userId: string;
  userName: string;
  type: StaffLeaveType;
  typeLabel: string;
  startDate: string;
  endDate: string;
  halfDay: boolean;
  halfDayPart: StaffLeaveHalfDayPart | null;
  reason: string;
  status: StaffLeaveStatus;
  statusLabel: string;
  requestedDays: number;
  countedDays: number | null;
  decidedByName: string | null;
  decidedAt: string | null;
  rejectionReason: string | null;
  enteredByAdmin: boolean;
  /** Congé payé left once this request is counted; null when no allowance applies. */
  balanceAfter: number | null;
  /** `YYYY-MM-DD`: the day « Solde après » is read on (credits a monthly rule adds until then count). */
  balanceAfterOn: string | null;
  /** The same figure if her other waiting congés payés of that year were accepted too; null when there are none. */
  balanceAfterWithPending: number | null;
  /** Others already on an accepted congé on one of these days (a waiting request only). */
  othersOff: string[];
  /** Colleagues with a WAITING request on any of these days. */
  othersPending: string[];
  /** Set when cancelled: who, whether it was her, and the admin's motif. */
  cancelledByName: string | null;
  cancelledBySelf: boolean;
  cancelReason: string | null;
  canCancel: boolean;
  createdAt: string;
  version: number;
}

export interface StaffLeaveRequestsPageDto {
  items: StaffLeaveRequestDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** `StaffLeaveAccrual` member names: credited all at once on 1 January, or on the 1st of each month. */
export type StaffLeaveAccrual = 'Year' | 'Month';

/** « 20 j par an » / « 2 j par mois » from `since`; `carryOver` false = days not taken are lost on 31 December. */
export interface StaffLeaveRuleDto {
  days: number;
  accrual: StaffLeaveAccrual;
  carryOver: boolean;
  since: string;
}

export interface StaffLeaveCreditDto {
  day: string;
  days: number;
}

/**
 * A year of congé payé folded from the « Droit à congé » lines and the congés (`StaffLeaveLedger`), read today.
 * `remaining` = acquired − taken − planned − expired; null when nothing was ever set for her.
 */
export interface StaffLeaveBalanceDto {
  year: number;
  hasAccount: boolean;
  carriedIn: number;
  opening: number | null;
  openingDay: string | null;
  credited: number;
  adjusted: number;
  acquired: number;
  /** Accepted congé payé already started. */
  taken: number;
  /** Accepted congé payé still to come this year. */
  planned: number;
  /** Congé payé still waiting for an answer, counted in this year. */
  pending: number;
  /** Lost on 31 December (a past year only). */
  expired: number;
  /** What the rule will still credit before 31 December. */
  upcoming: number;
  remaining: number | null;
  /** Every accepted day already taken, every year. */
  takenTotal: number;
  rule: StaffLeaveRuleDto | null;
  /** A rule that starts after today (shown beside the one in force). */
  nextRule: StaffLeaveRuleDto | null;
  nextCredit: StaffLeaveCreditDto | null;
  /** Days that will be lost on 31/12 if not taken (rule « perdus »); null when nothing expires. */
  lostAtYearEnd: number | null;
  /** Days lost on the previous 31/12; null when none. */
  lostLastYear: number | null;
  /** Congés of this year already inside the « Solde de départ » (counted in `taken`, not subtracted again). */
  takenInOpening: number | null;
}

/** `StaffLeaveMovementKind` member names. */
export type StaffLeaveMovementKind =
  | 'CarriedIn' | 'Opening' | 'Credit' | 'Adjustment' | 'Taken' | 'Planned' | 'Expired' | 'LostLastYear';

/** A line of the relevé: `days` signed (an opening states its value), `balance` after it. */
export interface StaffLeaveMovementDto {
  day: string;
  kind: StaffLeaveMovementKind;
  days: number;
  balance: number;
  entryId: string | null;
  requestId: string | null;
  startDate: string | null;
  endDate: string | null;
  accrual: StaffLeaveAccrual | null;
  reason: string | null;
  cancelled: boolean;
  cancelReason: string | null;
  /** History only: before the first account day, or inside a « Solde de départ » — it does not move `balance`. */
  absorbed: boolean;
}

export type StaffLeaveAllowanceKind = 'Rule' | 'Opening' | 'Adjustment';

/** A « Droit à congé » line as the admin manages it. */
export interface StaffLeaveAllowanceEntryDto {
  id: string;
  kind: StaffLeaveAllowanceKind;
  days: number;
  accrual: StaffLeaveAccrual | null;
  carryOver: boolean;
  onDay: string;
  reason: string | null;
  createdByName: string;
  createdAt: string;
  cancelled: boolean;
  cancelledByName: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
}

/** Days accepted in a year for one type of absence. */
export interface StaffLeaveTypeTotalDto {
  type: StaffLeaveType;
  typeLabel: string;
  days: number;
}

/** One active secretary on « Utilisateurs → Congés »: her balance and her next accepted congé. */
export interface TeamLeaveMemberDto {
  userId: string;
  fullName: string;
  balance: StaffLeaveBalanceDto;
  nextStartDate: string | null;
  nextEndDate: string | null;
  nextHalfDay: boolean;
  nextHalfDayPart: StaffLeaveHalfDayPart | null;
  /** Congé payé days waiting for a decision. */
  pendingDays: number;
}

export interface TeamLeaveOverviewDto {
  year: number;
  members: TeamLeaveMemberDto[];
}

export interface MyStaffLeaveDto {
  balance: StaffLeaveBalanceDto;
  /** The relevé of `balance.year`. */
  statement: StaffLeaveMovementDto[];
  items: StaffLeaveRequestDto[];
}

export interface StaffFileDto {
  userId: string;
  fullName: string;
  email: string | null;
  role: UserRole;
  isActive: boolean;
  balance: StaffLeaveBalanceDto;
  statement: StaffLeaveMovementDto[];
  /** Every « Droit à congé » line, cancelled ones included, newest first. */
  entries: StaffLeaveAllowanceEntryDto[];
  acceptedByType: StaffLeaveTypeTotalDto[];
  items: StaffLeaveRequestDto[];
}

export interface StaffLeavePreviewDto {
  days: number;
  /** No open day of the cabinet in the range: the request would count 0 and is refused (`leave_no_open_day`). */
  noOpenDay: boolean;
  balanceAfter: number | null;
  balanceAfterOn: string | null;
  balanceAfterWithPending: number | null;
  /** Her own pending or accepted request already on these dates (the send would be refused). */
  overlap: { startDate: string; endDate: string; status: 'Pending' | 'Approved' } | null;
}

export interface StaffLeaveInput {
  type: StaffLeaveType;
  startDate: string;
  endDate: string;
  halfDay: boolean;
  halfDayPart: StaffLeaveHalfDayPart | null;
  reason: string;
}

export type StaffLeaveListFilter = 'pending' | 'decided' | 'approved' | 'rejected' | 'cancelled';

const userPath = (userId: string) => `/team/users/${encodeURIComponent(userId)}`;

export const teamApi = {
  listLeaveRequests: (filter?: StaffLeaveListFilter, page?: number, pageSize?: number) =>
    apiGet<StaffLeaveRequestsPageDto>('/team/leave-requests', { status: filter, page, pageSize }),

  pendingCount: () => apiGet<{ count: number }>('/team/leave-requests/pending-count'),

  overview: () => apiGet<TeamLeaveOverviewDto>('/team/overview'),

  /** One request as it is now — re-read before a decision or a withdrawal sends its version. */
  getLeaveRequest: (id: string) => apiGet<StaffLeaveRequestDto>(`/team/leave-requests/${id}`),

  decide: (
    id: string,
    body: { approve: boolean; countedDays?: number; rejectionReason?: string; version: number },
  ) => apiPost<StaffLeaveRequestDto>(`/team/leave-requests/${id}/decision`, body),

  /** `reason` is required when an admin cancels someone else's (any date, any status but refused). */
  cancel: (id: string, version: number, reason?: string) =>
    apiPost<StaffLeaveRequestDto>(`/team/leave-requests/${id}/cancel`, { version, reason }),

  myLeave: (year?: number) => apiGet<MyStaffLeaveDto>('/team/me/leave', { year }),

  submit: (input: StaffLeaveInput) => apiPost<StaffLeaveRequestDto>('/team/me/leave-requests', input),

  preview: (params: { userId?: string; type: StaffLeaveType; startDate: string; endDate: string; halfDay: boolean }) =>
    apiGet<StaffLeavePreviewDto>('/team/leave-preview', params),

  staffFile: (userId: string, year?: number) => apiGet<StaffFileDto>(`${userPath(userId)}/file`, { year }),

  /** `countedDays` overrides the count (a jour férié inside the dates); omitted = the cabinet's open days. */
  recordAbsence: (userId: string, input: StaffLeaveInput & { countedDays?: number }) =>
    apiPost<StaffLeaveRequestDto>(`${userPath(userId)}/absences`, input),

  /** The balance as it would read if this rule were saved — the rule dialog's live result. Writes nothing. */
  previewLeaveRule: (userId: string, rule: { days: number; accrual: StaffLeaveAccrual; carryOver: boolean; since: string }) =>
    apiGet<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-rule-preview`, rule),

  /** A later rule supersedes the earlier one from its own day (`since`: 1 January or the 1st of a month). */
  setLeaveRule: (userId: string, rule: { days: number; accrual: StaffLeaveAccrual; carryOver: boolean; since: string }) =>
    apiPut<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-rule`, rule),

  /** The balance as it would read with this « Solde de départ » saved — the dialog's live result. Writes nothing. */
  previewOpeningBalance: (userId: string, body: { days: number; onDay: string }) =>
    apiGet<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-opening-preview`, body),

  /** « Il lui reste N j au … » — everything before that day is inside the figure. */
  setOpeningBalance: (userId: string, body: { days: number; onDay: string }) =>
    apiPost<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-opening`, body),

  /** Days added (positive) or removed (negative), with a motif. */
  adjustBalance: (userId: string, body: { days: number; onDay: string; reason: string }) =>
    apiPost<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-adjustments`, body),

  /** Strikes a line through; it stays listed. */
  cancelAllowanceEntry: (entryId: string, reason: string) =>
    apiPost<StaffLeaveBalanceDto>(`/team/leave-allowance-entries/${entryId}/cancel`, { reason }),
};

import { apiGet, apiPost, apiPut } from './client';
import type { UserRole } from './users';

/** `StaffLeaveType` member names — what the server sends and accepts. */
export type StaffLeaveType = 'PaidLeave' | 'Sick' | 'Unpaid' | 'Other';
export type StaffLeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

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
  /** Others already on an accepted congé on one of these days (a waiting request only). */
  othersOff: string[];
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

export interface StaffLeaveBalanceDto {
  year: number;
  allowance: number | null;
  taken: number;
  remaining: number | null;
  /** Congé payé still waiting for an answer, counted in this year. */
  pending: number;
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
}

export interface TeamLeaveOverviewDto {
  year: number;
  members: TeamLeaveMemberDto[];
}

export interface MyStaffLeaveDto {
  balance: StaffLeaveBalanceDto;
  items: StaffLeaveRequestDto[];
}

export interface StaffFileDto {
  userId: string;
  fullName: string;
  email: string | null;
  role: UserRole;
  isActive: boolean;
  balance: StaffLeaveBalanceDto;
  acceptedByType: StaffLeaveTypeTotalDto[];
  items: StaffLeaveRequestDto[];
}

export interface StaffLeavePreviewDto {
  days: number;
  balanceAfter: number | null;
}

export interface StaffLeaveInput {
  type: StaffLeaveType;
  startDate: string;
  endDate: string;
  halfDay: boolean;
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

  cancel: (id: string, version: number) =>
    apiPost<StaffLeaveRequestDto>(`/team/leave-requests/${id}/cancel`, { version }),

  myLeave: (year?: number) => apiGet<MyStaffLeaveDto>('/team/me/leave', { year }),

  submit: (input: StaffLeaveInput) => apiPost<StaffLeaveRequestDto>('/team/me/leave-requests', input),

  preview: (params: { userId?: string; type: StaffLeaveType; startDate: string; endDate: string; halfDay: boolean }) =>
    apiGet<StaffLeavePreviewDto>('/team/leave-preview', params),

  staffFile: (userId: string, year?: number) => apiGet<StaffFileDto>(`${userPath(userId)}/file`, { year }),

  recordAbsence: (userId: string, input: StaffLeaveInput) =>
    apiPost<StaffLeaveRequestDto>(`${userPath(userId)}/absences`, input),

  setAllowance: (userId: string, allowanceDays: number | null) =>
    apiPut<StaffLeaveBalanceDto>(`${userPath(userId)}/leave-allowance`, { allowanceDays }),
};

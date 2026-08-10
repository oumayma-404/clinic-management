import { consoleFetch } from "./client";

/**
 * The portfolio reads (`platform-console` US-2), server-side only like everything in this folder.
 *
 * ⚠️ **These interfaces mirror the API's closed read shape** (`PlatformReadShape` on the server), which is what
 * makes « the console cannot see your patient records » a property of the code rather than a promise. Nothing
 * here should ever grow a field that names a patient, an appointment or a document — and on the server such a
 * field fails the build, so a mismatch here means somebody widened the wrong end.
 */

/** One cabinet, exactly as the list shows it. */
export interface PlatformClinicRow {
  clinicId: string;
  name: string;
  city: string | null;
  createdAt: string;
  /** Null while `subscriptionDataAvailable` is false — never a guessed « Actif ». */
  plan: string | null;
  state: string | null;
  endsOn: string | null;
  daysRemaining: number | null;
  users: number;
  patients: number;
  appointments30d: number;
  writes7d: number;
  writes30d: number;
  activeDays30d: number;
  lastWriteAt: string | null;
  lastLoginAt: string | null;
  /** The CABINET's own turnover this month — never the vendor's revenue. */
  clinicCollectedThisMonthDt: number;
  /** Null where the counter pass has never covered this cabinet: « pas encore mesuré », not « rien fait ». */
  countersComputedAt: string | null;
}

export interface PlatformClinicPage {
  items: PlatformClinicRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  /** The oldest measurement on the page (AC-2.8), or null where nothing on it has ever been measured. */
  countersAsOf: string | null;
  subscriptionDataAvailable: boolean;
}

export interface PlatformSummary {
  clinics: number;
  dormant: number;
  neverMeasured: number;
  inTrial: number | null;
  active: number | null;
  expiringWithin14Days: number | null;
  expired: number | null;
  suspended: number | null;
  /** The VENDOR's revenue. Null until the subscription ledger exists — never a sum of the cabinets' own. */
  vendorCollectedThisMonthDt: number | null;
  subscriptionDataAvailable: boolean;
}

/** What the screen may narrow the portfolio by. Mirrors the query string the API accepts. */
export interface PortfolioQuery {
  q?: string;
  dormant?: boolean;
  sort?: string;
  page?: number;
}

/**
 * Builds the query string. Only non-default values are written, so a plain « Cabinets » link stays a clean URL
 * and « is a filter active? » is answerable by reading the address bar — which is also what makes the removable
 * filter chips on the screen honest.
 */
export function portfolioSearchParams(query: PortfolioQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.q) params.set("q", query.q);
  if (query.dormant) params.set("dormant", "true");
  if (query.sort && query.sort !== "name") params.set("sort", query.sort);
  if (query.page && query.page > 1) params.set("page", String(query.page));
  return params;
}

export async function fetchPortfolio(token: string, query: PortfolioQuery): Promise<PlatformClinicPage> {
  const params = portfolioSearchParams(query);
  const suffix = params.toString();
  return consoleFetch<PlatformClinicPage>(`/platform/clinics${suffix ? `?${suffix}` : ""}`, { token });
}

export async function fetchSummary(token: string): Promise<PlatformSummary> {
  return consoleFetch<PlatformSummary>("/platform/summary", { token });
}

// ── One cabinet, opened (US-3) ──────────────────────────────────────────────────────────────────────────────

/** One month of a cabinet's activity. */
export interface PlatformActivityMonth {
  year: number;
  month: number;
  /** « août 2026 », built server-side so the chart's axis and its text alternative cannot disagree. */
  monthLabel: string;
  writes: number;
  appointments: number;
  patientsCreated: number;
  /**
   * How many days of this month the counter pass actually covered.
   *
   * ⚠️ **Zero means « pas encore mesuré », not « rien fait »** — the pass writes a rolling 30-day window, so five
   * of the six months are genuinely unmeasured on a young deployment and drawing them as zero would show every
   * cabinet collapsing the further back the reader looks.
   */
  daysMeasured: number;
}

export interface PlatformClinicDetail {
  /** The row the list renders, verbatim — AC-3.1 is « the same figures ». */
  clinic: PlatformClinicRow;
  /** The cabinet's administrator: staff, never a patient. Null where the cabinet has no admin account. */
  adminName: string | null;
  adminEmail: string | null;
  /** False also when there is no admin at all, so a blank name can never read as somebody reachable. */
  adminIsActive: boolean;
  /** Always six months, oldest first. */
  trend: PlatformActivityMonth[];
  subscriptionDataAvailable: boolean;
  /** Why the state, the end date and the payment history are absent. Null once the companion feature ships. */
  subscriptionExplanation: string | null;
}

/**
 * The refusal code a cabinet deleted since the list was drawn comes back with (EC-13).
 *
 * ⚠️ Branched on the **code**, never on the French sentence — rewording a message must not silently change what
 * the screen does, which is the `Contains("déjà facturée")` defect this codebase has already paid for once.
 */
export const CLINIC_NOT_FOUND_CODE = "clinic_not_found";

export async function fetchClinicDetail(token: string, clinicId: string): Promise<PlatformClinicDetail> {
  return consoleFetch<PlatformClinicDetail>(`/platform/clinics/${clinicId}`, { token });
}

// ── The console's own access ledger (FR-5) ──────────────────────────────────────────────────────────────────

/**
 * One row of « Journal ».
 *
 * ⚠️ `accountEmail` and `clinicName` are the **row's own**, copied in when it was written, so an access to a
 * cabinet that has since been closed still names both parties.
 */
export interface PlatformAccessEntry {
  entryId: string;
  platformAccountId: string;
  accountEmail: string;
  clinicId: string;
  clinicName: string;
  /** The raw enum member — kept for matching; `actionLabel` is what a screen shows. */
  action: string;
  actionLabel: string;
  occurredAt: string;
}

export interface PlatformAccessActor {
  platformAccountId: string;
  accountEmail: string;
}

export interface PlatformAccessLogPage {
  items: PlatformAccessEntry[];
  /** The « Compte » filter's options, derived from the rows rather than from the account table. */
  actors: PlatformAccessActor[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface AccessLogQuery {
  accountId?: string;
  clinicId?: string;
  page?: number;
}

export function accessLogSearchParams(query: AccessLogQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.accountId) params.set("accountId", query.accountId);
  if (query.clinicId) params.set("clinicId", query.clinicId);
  if (query.page && query.page > 1) params.set("page", String(query.page));
  return params;
}

export async function fetchAccessLog(token: string, query: AccessLogQuery): Promise<PlatformAccessLogPage> {
  const suffix = accessLogSearchParams(query).toString();
  return consoleFetch<PlatformAccessLogPage>(`/platform/access-log${suffix ? `?${suffix}` : ""}`, { token });
}

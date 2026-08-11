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
  plan: string | null;
  planLabel: string | null;
  /**
   * `Trial` | `Active` | `Expired` | `Suspended`, derived server-side by the one rule the gate and the cabinet's
   * own screen read — or **null** where the cabinet has no entitlement row at all (FR-13's failure state), which
   * `stateLabel` then says in words. Null is not « sans échéance »: that is an entitlement with a null `endsOn`.
   */
  state: string | null;
  /** Always present. Branch on `state`, never on this — a reworded label must not change what a screen does. */
  stateLabel: string;
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
}

export interface PlatformSummary {
  clinics: number;
  dormant: number;
  neverMeasured: number;
  /**
   * The five state counts are mutually exclusive and sum to `clinics`; `expiringWithin14Days` is a **subset** of
   * the covered ones rather than a sixth bucket, which is why the strip labels it apart.
   */
  inTrial: number;
  active: number;
  expiringWithin14Days: number;
  expired: number;
  suspended: number;
  noEntitlement: number;
  /** The VENDOR's revenue this month — never a sum of the cabinets' own turnover (FR-2). */
  vendorCollectedThisMonthDt: number;
}

/** What the screen may narrow the portfolio by. Mirrors the query string the API accepts. */
export interface PortfolioQuery {
  q?: string;
  dormant?: boolean;
  /** `trial` | `active` | `expiringSoon` | `expired` | `suspended` | `missing` (AC-2.3). */
  state?: string;
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
  if (query.state) params.set("state", query.state);
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
  /** The cabinet's subscription ledger, newest first — cancelled entries included and marked (AC-3.2, AC-5.2). */
  payments: PlatformSubscriptionEntry[];
}

/**
 * One entry of a cabinet's subscription ledger.
 *
 * ⚠️ `coversFrom`/`coversThrough` are **derived by the server's fold**, not stored and not recomputed here — the
 * same spans the cabinet's own « Abonnement » screen shows. A cancelled entry covers nothing and both are null.
 *
 * ⚠️ `amountDt` is null for « offert » (AC-4.8) — **not** zero. « Offert » and « payé 0,000 DT » are different
 * statements, and only one of them is ever true.
 */
export interface PlatformSubscriptionEntry {
  entryId: string;
  kind: string;
  kindLabel: string;
  recordedOn: string;
  coversFrom: string | null;
  coversThrough: string | null;
  amountDt: number | null;
  method: string | null;
  methodLabel: string | null;
  reference: string | null;
  note: string | null;
  recordedBy: string | null;
  isCancelled: boolean;
  cancelledAt: string | null;
  cancelledBy: string | null;
  cancelReason: string | null;
}

/**
 * What recording a payment answers with (AC-4.3).
 *
 * ⚠️ `alreadyRecorded` is a **success**: the second tap of a double-click found the money already taken, which is
 * the outcome the vendor wanted (AC-4.6). The screen says so rather than claiming to have taken it twice.
 */
export interface PlatformPaymentRecorded {
  clinicId: string;
  entryId: string | null;
  previousEndsOn: string | null;
  endsOn: string | null;
  state: string;
  stateLabel: string;
  daysRemaining: number | null;
  alreadyRecorded: boolean;
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

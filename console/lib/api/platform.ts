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
  /**
   * The cabinet's administrator — who set the practice up, and who the vendor writes to. Null where it has no admin
   * account at all, which is a fact worth seeing in the list rather than only after opening a fiche.
   *
   * ⚠️ The only field on this row that names a **person**, and a *staff* account rather than anybody the practice
   * treats — see `PlatformReadShape` on why the `admin` prefix is what keeps that reviewable.
   */
  adminEmail: string | null;
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
 * The order the portfolio arrives in when nobody has chosen one: the **newest cabinet first**.
 *
 * ⚠️ It must stay equal to `ListPlatformClinicsQuery.ParseSort`'s own fallback. That agreement is what lets the
 * default be *omitted* from the URL below — a clean « Cabinets » link — and moving one side alone would leave
 * « Création » looking active on a list that arrived alphabetically.
 */
export const DEFAULT_PORTFOLIO_SORT = "createdAt";

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
  if (query.sort && query.sort !== DEFAULT_PORTFOLIO_SORT) params.set("sort", query.sort);
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
  /**
   * Why this cabinet is suspended, by whom and since when — **null when it is not** (AC-6.1).
   *
   * ⚠️ Whether the cabinet *is* suspended is `clinic.state === "Suspended"`, the server's own FR-1 verdict. This
   * only explains a suspension that state has already declared: branching the screen on `suspension !== null`
   * instead would be a second authority on « suspendu ».
   */
  suspension: PlatformSuspension | null;
}

/**
 * A live suspension's trail (AC-6.1).
 *
 * ⚠️ **It is cleared when the suspension is lifted**, by design on the server — so a cabinet suspended in March and
 * released in April has none of this, and the durable record is the console's own « Journal » (« Cabinet suspendu » /
 * « Suspension levée »). The fiche links there for exactly that reason.
 */
export interface PlatformSuspension {
  /** Mandatory on the server, so this is never blank while a suspension stands. */
  suspensionReason: string;
  suspendedAt: string;
  /** `console|{accountId}` — a console account, never anybody at the practice. Null for a verb that named none. */
  suspendedBy: string | null;
}

/**
 * What suspending or lifting answers with (AC-6.4).
 *
 * ⚠️ `endsOn` is echoed back although nothing in this write can move it — that is the point: no paid day is consumed
 * while a cabinet is suspended, and the only way to see that on the screen that did it is for the date to be the same
 * one. `makesReadOnly` can therefore stay **true** after a lift, on a cabinet whose cover ran out anyway.
 */
export interface PlatformSuspensionChanged {
  clinicId: string;
  isSuspended: boolean;
  endsOn: string | null;
  state: string;
  stateLabel: string;
  daysRemaining: number | null;
  makesReadOnly: boolean;
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
  /**
   * What cancelling **this** entry would do (AC-5.3, EC-7) — null on a row already cancelled, and null for a cabinet
   * with no entitlement at all.
   *
   * ⚠️ **Computed by the server's own fold**, with this entry marked cancelled, and read through the one rule the
   * gate reads. Never derive it here: « the end date minus this entry's duration » is wrong whenever the entry is
   * not the latest one, which is precisely the case a correction is for.
   */
  ifCancelled: PlatformCancellationPreview | null;
}

/** Where the cabinet would stand if one entry went. Every field is the server's. */
export interface PlatformCancellationPreview {
  /** The end day the remaining entries fold to, or null for « sans échéance ». */
  endsOn: string | null;
  state: string;
  stateLabel: string;
  /** EC-7's headline: the practice could no longer record new work. */
  makesReadOnly: boolean;
}

/**
 * What cancelling an entry answers with (AC-5.3).
 *
 * ⚠️ `previousEndsOn` is the date the cabinet held a moment ago, which is what makes a correction that moved the
 * date **into the past** legible on the screen that did it.
 */
export interface PlatformPeriodCancelled {
  clinicId: string;
  entryId: string;
  previousEndsOn: string | null;
  endsOn: string | null;
  state: string;
  stateLabel: string;
  daysRemaining: number | null;
  makesReadOnly: boolean;
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

/**
 * The refusal codes a cancellation can come back with (AC-5.1).
 *
 * ⚠️ `period_already_cancelled` is a **state of the world**, not a rejected request: somebody else struck the entry
 * through, and its motif and author are on the fiche the screen re-reads. Branched on the code so rewording the
 * sentence cannot silently change what the dialog does.
 */
export const PERIOD_ALREADY_CANCELLED_CODE = "period_already_cancelled";

/**
 * The two refusals a suspension change can come back with (AC-6.1, AC-6.4) — both **states of the world**, not
 * rejected requests, which is why each is a 409 and why the fiche re-reads on either.
 *
 * ⚠️ `clinic_not_suspended` is the one worth wording carefully on screen: a vendor reaching it was usually looking at
 * a read-only cabinet and assumed a suspension. Its real problem is an end date, and the server's sentence says so.
 */
export const CLINIC_ALREADY_SUSPENDED_CODE = "clinic_already_suspended";

export const CLINIC_NOT_SUSPENDED_CODE = "clinic_not_suspended";

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

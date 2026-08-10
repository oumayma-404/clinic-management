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

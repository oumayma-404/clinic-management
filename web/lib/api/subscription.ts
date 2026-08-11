import { apiGet } from './client';
import type { PagedResponse, PageParams } from './paging';

/**
 * The cabinet's own subscription, as `GET /api/subscription` answers it (`clinic-subscription` Part C, US-2).
 *
 * Mirrors the backend `SubscriptionDto`. Readable by **every** role (AC-2.2) and by a cabinet whose entitlement has
 * already ended (AC-4.8) — it is the one screen that says what to do about the refusal.
 */
export interface SubscriptionDto {
  /** Derived, never stored (FR-1). */
  state: 'Trial' | 'Active' | 'Expired' | 'Suspended';
  /** Its French name, built server-side so this screen and the notification quote the same word. */
  stateLabel: string;
  plan: 'Cabinet' | 'Clinique' | 'SurMesure' | null;
  planLabel: string | null;
  /**
   * The **inclusive** last day new work may be recorded, or `null` for « sans échéance ».
   *
   * ⚠️ **`null` is a state the screen must render in words** (AC-2.5): a grandfathered or complimentary cabinet has
   * no end date at all, and a far-future placeholder is a sentence nobody can act on.
   */
  endsOn: string | null;
  /** Whole clinic-local days left, **0 on the last working day**. `null` with no end date, and `null` once past. */
  daysRemaining: number | null;
  allowsWrites: boolean;
  /** True inside the warning window — what Part D's banner mounts on. */
  shouldWarn: boolean;
  /** Why the vendor stopped this cabinet. Set only in the `Suspended` state (EC-11). */
  suspensionReason: string | null;
  /** The cabinet's **own** forfait's price. `null` when it has chosen none — see `plans`. */
  priceMonthlyDt: number | null;
  priceAnnualDt: number | null;
  /**
   * The deployment's whole published tariff, one row per forfait.
   *
   * ⚠️ **Not a duplicate of the two fields above.** A cabinet on its free days and every grandfathered one has
   * `plan: null`, so those are null for exactly the readers deciding whether to pay — while AC-2.1 requires the
   * screen to show the price. One answers « what am I paying? », this answers « what would I pay? ».
   */
  plans: SubscriptionPlanPriceDto[];
  /** How to pay, in French, from per-deployment configuration (AC-2.4). Never behind a disclosure. */
  paymentInstructions: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
}

/** One forfait of the published tariff. A label and a price; it **gates nothing** (FR-10). */
export interface SubscriptionPlanPriceDto {
  plan: 'Cabinet' | 'Clinique' | 'SurMesure';
  label: string;
  /** `null` where this deployment publishes no figure — rendered « sur devis », never « 0,000 DT ». */
  priceMonthlyDt: number | null;
  priceAnnualDt: number | null;
}

/** One entry of the cabinet's subscription ledger (AC-2.3). Admin-only. */
export interface SubscriptionPeriodDto {
  id: string;
  kind: 'Trial' | 'Paid' | 'Grandfathered' | 'Complimentary';
  kindLabel: string;
  /**
   * The period this entry covered, **derived by the same fold that produces the end date** — so a row cannot claim a
   * stretch of time the entitlement disagrees with.
   *
   * ⚠️ Both `null` for a **cancelled** entry (it contributes nothing), and `throughDay` alone `null` for an
   * open-ended one (« sans échéance »). The two cases are told apart by `isCancelled`, never by the nulls.
   */
  fromDay: string | null;
  throughDay: string | null;
  /** ⚠️ The **vendor's** revenue, never the clinic's (FR-2) — it reaches no money read in this product. */
  amount: number | null;
  method: 'Transfer' | 'Cash' | 'Cheque' | 'Card' | null;
  methodLabel: string | null;
  reference: string | null;
  recordedAt: string;
  /*
   * ⚠️ No `note` and no `recordedBy`. `--note` is the vendor's own commentary about this customer and `recordedBy`
   * is our internal command vocabulary; neither is rendered by either tree below, so both were dropped from the
   * projection rather than shipped to a cabinet's devtools. They stay on the vendor's console report.
   */
  isCancelled: boolean;
  cancelledAt: string | null;
  /** Mandatory when cancelled — the end date can move into the past as a result (EC-4). */
  cancelReason: string | null;
}

export type SubscriptionHistoryPageDto = PagedResponse<SubscriptionPeriodDto>;

export const subscriptionApi = {
  /**
   * The cabinet's state, dates, tariff and payment instructions.
   *
   * ⚠️ **A 404 means this deployment does not work by subscription** — the server-side half of AC-7.1/7.2 — and is
   * the only status the screen reads as « absent ». Every other failure is a retryable state (EC-13): a network drop
   * must never render as « aucun abonnement ».
   */
  get: async (): Promise<SubscriptionDto> => apiGet<SubscriptionDto>('/subscription'),

  /**
   * One page of what the cabinet has paid, newest first. **Admin only** — the screen itself is not.
   *
   * Takes `PageParams` like every other paged read (`recalls.ts`, `users.ts`) rather than positional numbers with a
   * hand-built query string: that shape could not pick up `search` without another literal, and it restated the
   * default page size the page itself already imports from `./paging`.
   */
  history: async (params: PageParams): Promise<SubscriptionHistoryPageDto> =>
    apiGet<SubscriptionHistoryPageDto>('/subscription/history', params),
};

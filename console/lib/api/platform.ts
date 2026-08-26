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
  /**
   * Whether a WhatsApp-forfait counting row exists for the current Tunisian month
   * (`vendor-whatsapp-messaging-quota` AC-8.3).
   *
   * ⚠️ **False is « non mesuré », not « zéro »** — one row exists per cabinet per month, so its absence is our own
   * bookkeeping fault, while « 0 rappel envoyé » is a fact about the practice. The three figures below are null in
   * that case, and the exhausted/near filters match neither state. Read this before rendering any of them.
   */
  messagingMeasured: boolean;
  messagingAllowance: number | null;
  messagingConsumed: number | null;
  /** Floored at zero server-side: a cancelled allocation can put consumption above the forfait. */
  messagingRemaining: number | null;
  /** The server's own verdict, false where nothing was measured — an unknown is not an exhaustion. */
  messagingExhausted: boolean;
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
  /** The Tunisian month every row's forfait figures are for, `AAAA-MM`. */
  messagingMonth: string;
  messagingMonthLabel: string;
  /**
   * The percentage of its forfait a cabinet must have consumed to count as « presque épuisé » — the **server's own
   * constant**, the same one its SQL predicate reads.
   *
   * ⚠️ **Never retype it here.** The chip's label is `100 - this`, so the filter and the words on it are one figure;
   * two spellings of a threshold is how a filter and its own label come to disagree with neither looking wrong alone.
   */
  messagingNearThresholdPercent: number;
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
  /**
   * `exhausted` | `near` — the WhatsApp-forfait narrowing (AC-8.2), applied over the stored counting row so it
   * narrows the **portfolio** rather than the page. A cabinet with no counting row matches neither (AC-8.3).
   */
  messaging?: string;
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
  if (query.messaging) params.set("messaging", query.messaging);
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

// ── The password policy (hosted-security-hardening FR-1.9) ──────────────────────────────────────────────────

/** What the console needs before it asks anybody to **choose** a password. */
export interface PlatformAuthMeta {
  passwordMinLength: number;
}

/**
 * The server's minimum password length.
 *
 * ⚠️ **Read rather than restated.** « Changer le mot de passe » used to print « Au moins 8 caractères. » as a
 * literal, so raising `PasswordPolicy.MinLength` server-side would have left the console telling an operator a
 * number the API no longer accepts — and the console cannot read the clinic app's `GET /api/auth/mode`, because
 * `ConsolePortGate` 404s anything outside `/api/platform` on this listener. Hence a platform-side read.
 */
export async function fetchAuthMeta(token: string): Promise<PlatformAuthMeta> {
  return consoleFetch<PlatformAuthMeta>("/platform/auth/meta", { token });
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
  /**
   * The cabinet's WhatsApp reminder position (`vendor-whatsapp-messaging-quota` AC-8.1) — **null where the deployment
   * does not sell vendor messaging** (EC-16), in which case the fiche renders no « Messagerie » section at all rather
   * than a heading over zeros.
   */
  messaging: PlatformMessaging | null;
}

/**
 * A cabinet's forfait de rappels WhatsApp, as the fiche shows it (AC-8.1).
 *
 * ⚠️ **All three figures are nullable and null is « non mesuré », never zero** (AC-8.3). Read `measured` first: a
 * missing counting row is our own bookkeeping fault, and rendering it as « 0 rappel envoyé » makes a claim about the
 * practice instead.
 *
 * ⚠️ `standingAllowance` is a *different question* from `allowance` — « what does this cabinet get every month? » vs
 * « what was it allowed **this** month? » — and they differ whenever a top-up is in play or a lowering is pending. Both
 * come from the server; never derive one from the other.
 */
export interface PlatformMessaging {
  month: string;
  monthLabel: string;
  allowance: number | null;
  consumed: number | null;
  remaining: number | null;
  measured: boolean;
  exhausted: boolean;
  standingAllowance: number | null;
  /** `NotConnected` | `PendingReview` | `Ready` | `TemplateRefused` | `Suspended` — branch on this, not the label. */
  senderState: string;
  senderStateLabel: string;
  /** Null until Part 4 stores a per-cabinet template state: « we do not track this yet », not « non soumis ». */
  templateStatus: string | null;
  templateStatusLabel: string | null;
  /**
   * Meta's classification of the reminder template — stated **only when it is not `UTILITY`** (FR-7b), because that is
   * the vendor's cost per message having moved. Never shown to the practice, and it holds no reminders.
   */
  templateCategory: string | null;
  templateCategoryLabel: string | null;
  /** Newest first, cancelled allocations included and marked (AC-6.2). */
  entries: PlatformMessagingEntry[];
}

/**
 * One allocation of a cabinet's forfait ledger.
 *
 * ⚠️ `amountDt` is null for « offert » (AC-6.6) — **not** zero; the server refuses that spelling outright.
 *
 * ⚠️ `effectiveMonth` is **stated rather than derived** (AC-6.4a): it is when a lowering starts applying, and no client
 * can work that out without the whole ledger.
 */
export interface PlatformMessagingEntry {
  entryId: string;
  /** `Standing` | `TopUp` — branch on this; `kindLabel` is what a screen shows. */
  kind: string;
  kindLabel: string;
  messages: number;
  effectiveMonth: string;
  effectiveMonthLabel: string;
  recordedOn: string;
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
   * What cancelling **this** allocation would do (AC-7.3) — null on a row already cancelled.
   *
   * ⚠️ **Computed by the server, by re-folding the real ledger with this entry marked cancelled.** Never derive it
   * here: « the current forfait minus this entry's messages » is wrong for a *standing* allocation, which replaces
   * rather than adds — cancelling one hands the month back to whatever earlier standing figure was in force.
   */
  ifCancelled: PlatformMessagingCancellationPreview | null;
}

/**
 * What the forfait would become if one allocation went. Every field is the server's.
 *
 * ⚠️ `exhausted` is AC-7.4's headline: consumption is **untouched** by a cancellation, so the month can end up over its
 * reduced forfait and the practice's reminders held from that moment. Nothing is unsent and nothing is clawed back —
 * which is exactly why the confirmation has to say it before the vendor commits.
 */
export interface PlatformMessagingCancellationPreview {
  allowance: number | null;
  consumed: number | null;
  remaining: number | null;
  exhausted: boolean;
}

/**
 * What recording a forfait answers with (AC-6.3, AC-6.4).
 *
 * ⚠️ `effectiveMonth` is the whole of AC-6.4a on the wire: a **lowering** comes back with next month's key and
 * `allowanceThisMonth` unchanged. That is correct and surprising, so the screen states it — otherwise a vendor concludes
 * nothing happened and tries again with a bigger figure.
 *
 * ⚠️ `alreadyRecorded` is a **success** (AC-6.7), not a refusal: the second tap of a double-click found the allocation
 * already recorded, which is what the vendor wanted.
 */
export interface PlatformMessagingRecorded {
  clinicId: string;
  entryId: string | null;
  kind: string | null;
  kindLabel: string | null;
  effectiveMonth: string | null;
  effectiveMonthLabel: string | null;
  messages: number | null;
  /** Null on a replay: what the figure was before the first submission is not recoverable, and a guess would lie. */
  previousAllowanceThisMonth: number | null;
  allowanceThisMonth: number | null;
  consumedThisMonth: number | null;
  alreadyRecorded: boolean;
}

/**
 * What cancelling an allocation answers with (AC-7.4), read back **after** the re-fold rather than assumed from the
 * preview the vendor confirmed — the ledger may have moved between the page render and the click.
 */
export interface PlatformMessagingCancelled {
  clinicId: string;
  entryId: string;
  previousAllowanceThisMonth: number | null;
  allowanceThisMonth: number | null;
  /** Untouched by the cancellation (AC-7.4) — echoed so that claim is checkable on the screen that did it. */
  consumedThisMonth: number | null;
  exhaustedThisMonth: boolean;
}

/**
 * The refusals a forfait write can come back with.
 *
 * ⚠️ `messaging_allowance_entry_already_cancelled` is a **state of the world**, not a rejected request: somebody else
 * struck the allocation through, and its motif and author are on the fiche the dialog re-reads. Branched on the code so
 * rewording the sentence cannot silently change what the screen does.
 *
 * ⚠️ `messaging_allowance_past_month` points at the **month field** rather than at the form as a whole, which is the
 * only reason it has a code of its own.
 */
export const MESSAGING_ALLOWANCE_ALREADY_CANCELLED_CODE = "messaging_allowance_entry_already_cancelled";

export const MESSAGING_ALLOWANCE_PAST_MONTH_CODE = "messaging_allowance_past_month";

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

/**
 * What resetting a clinic account's second factor answers with — **who** was actually disarmed.
 *
 * ⚠️ The target is echoed back so « j'ai bien désarmé le bon compte » is checkable on the screen that did it. The
 * vendor typed an address off a telephone call; a mis-keyed character that happens to match a colleague at the same
 * cabinet is the failure this catches, and the only one still fixable by ringing back.
 */
export interface PlatformSecondFactorReset {
  clinicId: string;
  targetEmail: string | null;
  targetName: string | null;
  targetRole: string;
  resetAt: string;
}

export const CLINIC_ACCOUNT_NOT_FOUND_CODE = "clinic_account_not_found";

export const SECOND_FACTOR_NOT_ENROLLED_CODE = "second_factor_not_enrolled";

export const ACCOUNT_HAS_NO_PASSWORD_CODE = "account_has_no_password";

/**
 * What resetting one clinic account's **password** answers with.
 *
 * ⚠️ The target is echoed back for {@link PlatformSecondFactorReset}'s reason, and it matters more here: this write
 * does not merely remove a protection, it hands out a working credential. A mis-keyed character that happens to
 * match a colleague at the same cabinet is the failure it catches.
 *
 * ⚠️ **`oneTimePassword` is shown exactly once and is read out by voice.** It reaches this screen and nowhere else:
 * the API deliberately keeps it out of the e-mail the affected person receives, because mailing a live credential to
 * a mailbox that is either unreachable (the reason the vendor was called) or in somebody else's hands (the reason
 * the notice exists) would make that notice the delivery mechanism for the takeover it exists to reveal.
 */
export interface PlatformPasswordReset {
  clinicId: string;
  targetEmail: string | null;
  targetName: string | null;
  targetRole: string;
  oneTimePassword: string;
  resetAt: string;
}

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
  /**
   * Populated for `SecondFactorReset` and null on every other row: the clinic account that was disarmed, and why.
   *
   * ⚠️ They are on the row because a reset leaves no trace anywhere else — a suspension's motif lives on the
   * entitlement, a cancellation's on the entry it strikes through, but `DisableTotp` writes nothing. A journal
   * showing only « Second facteur d'un compte réinitialisé · Cabinet X » would be evidence of nothing.
   */
  targetEmail: string | null;
  reason: string | null;
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

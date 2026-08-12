import { apiGet } from './client';

/** The backend's `Result<T>` envelope, as `reminder-settings.ts` and `clinics.ts` already declare it locally. */
interface Result<T> {
  isSuccess: boolean;
  value: T | null;
  error: string | null;
}

/**
 * The five states a cabinet's WhatsApp sender can be in (AC-1.4), as the server derives them.
 *
 * ⚠️ Never re-derive this in the browser from a connection status: `MessagingSender.From` is the one derivation, and
 * « connecté » must never be presented as « prêt à envoyer ». The label below is what the screen renders.
 */
export type MessagingSenderState =
  | 'NotConnected'
  | 'PendingReview'
  | 'Ready'
  | 'TemplateRefused'
  | 'Suspended';

/**
 * « Forfait de rappels WhatsApp » for the current Tunisian month (US-2).
 *
 * ⚠️ **`measured` is the field that keeps « 0 restant » and « nous n'avons pas pu lire » apart** (AC-2.4 vs EC-12).
 * `false` means the cabinet has no counting row — a statement about *us* — so the three figures are `null` and the
 * card must say so rather than render zeros. A failed *read* is a third thing again: it throws, and the caller shows
 * `LoadFailureNotice`.
 */
export interface ReminderAllowanceDto {
  /** The Tunisian calendar month, `AAAA-MM`. */
  month: string;
  /** « août 2026 », built server-side with `fr-FR` pinned. */
  monthLabel: string;
  /** Null where nothing was measured — never render 0 in its place. */
  allowance: number | null;
  consumed: number | null;
  /** `max(0, allowance − consumed)`, floored server-side (AC-2.1). */
  remaining: number | null;
  exhausted: boolean;
  /** The day the **forfait** renews — never a promise about the held reminders (AC-4.2). */
  resetsOn: string;
  /** False ⇒ no counting row for this month. See the ⚠️ above. */
  measured: boolean;
  senderState: MessagingSenderState;
  /** The state in words. Rendered as-is — never mapped again here. */
  senderStateLabel: string;
  /** Always null today: nothing stores the cabinet's own WhatsApp number, only Meta's opaque id. */
  senderNumber: string | null;
  /**
   * The vendor's own contact details, from operator configuration (AC-2.7).
   *
   * ⚠️ Null means the section renders **no contact route at all** — not an empty `mailto:`. A dead control is worse
   * than an absent one.
   */
  contactEmail: string | null;
  contactPhone: string | null;
}

/** One month of the history table (AC-2.3). */
export interface ReminderAllowanceMonthDto {
  month: string;
  monthLabel: string;
  /** What was in force **that** month — the stored snapshot, not today's figure applied backwards. */
  allowance: number | null;
  /** A measured 0 reads « 0 rappel envoyé »; `null` with `measured: false` reads « non mesuré » (AC-2.4). */
  consumed: number | null;
  measured: boolean;
}

/**
 * The current Tunisian month plus the twelve before it, newest first.
 *
 * ⚠️ Months below the D-5 floor are **absent** from this list rather than reported unmeasured — a cabinet that
 * predates the rollout is never told we failed to count months nobody was counting.
 */
export interface ReminderAllowanceHistoryDto {
  months: ReminderAllowanceMonthDto[];
}

/**
 * US-2's two clinic reads.
 *
 * ⚠️ **Both 404 where the deployment does not sell vendor messaging** (AC-1.6, EC-16) — absent, not refused. The
 * caller distinguishes that 404 from every other failure, because « cette installation ne fonctionne pas comme ça »
 * and « je n'ai pas pu lire » are opposite facts with the same blank picture (the `/abonnement` precedent).
 */
export const reminderAllowanceApi = {
  current: async (): Promise<ReminderAllowanceDto> => {
    const result = await apiGet<Result<ReminderAllowanceDto>>('/clinics/reminder-allowance');
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to load the WhatsApp reminder allowance');
    }
    return result.value;
  },

  history: async (): Promise<ReminderAllowanceHistoryDto> => {
    const result = await apiGet<Result<ReminderAllowanceHistoryDto>>(
      '/clinics/reminder-allowance/history');
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to load the WhatsApp reminder allowance history');
    }
    return result.value;
  },
};

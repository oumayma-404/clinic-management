import { apiGet, apiPut, apiPost, apiDelete } from './client';
import type { PagedResponse } from './paging';

/** WhatsApp Embedded-Signup connection state (mirrors the backend enum name). */
export type WhatsAppConnectionStatus = 'NotConnected' | 'Connected' | 'Error';

/** Per-channel effective status (mirrors backend ReminderEffectiveStatus): is the channel actually sendable. */
export type ReminderEffectiveStatus = 'configured' | 'not_configured';

/**
 * A clinic's reminder settings (secret-masked). Channel toggles are nullable: `null` = inherit the
 * per-install default. Secret values are never returned — only a per-secret configured flag.
 */
export interface ReminderSettingsDto {
  smsEnabled: boolean | null;
  whatsAppEnabled: boolean | null;
  smsSenderId: string | null;
  whatsAppPhoneNumberId: string | null;
  whatsAppTemplateName: string | null;
  whatsAppTemplateLanguage: string | null;
  smsApiKeyConfigured: boolean;
  whatsAppAccessTokenConfigured: boolean;
  // Per-clinic overrides of previously per-install-only values (non-secret).
  smsApiUrl: string | null;
  whatsAppApiUrl: string | null;
  leadTimeHours: number[] | null;
  messageTemplateBody: string | null;
  // Per-channel effective status: whether the resolved settings + credentials make the channel sendable.
  smsEffectiveStatus: ReminderEffectiveStatus;
  whatsAppEffectiveStatus: ReminderEffectiveStatus;
  // WhatsApp Embedded-Signup connection metadata (read-only; token never returned).
  whatsAppBusinessAccountId: string | null;
  whatsAppConnectionStatus: WhatsAppConnectionStatus;
  whatsAppLastError: string | null;
  whatsAppConnectedAt: string | null;
}

/** Delivery-status value for a reminder outbox row (mirrors backend ReminderDeliveryStatus). */
export type ReminderDeliveryStatus = 'sent' | 'pending' | 'failed';

/** One recent reminder outbox row shown on the admin delivery-status surface. */
export interface ReminderStatusDto {
  id: string;
  channel: string;
  recipientMasked: string;
  status: ReminderDeliveryStatus;
  failureReason: string | null;
  scheduledAt: string;
  sentAt: string | null;
  /**
   * The patient's name (AC-P3.9). The row used to carry a masked phone and nothing else, so a failure read
   * « •••• 56 — Échec » and named nobody. The phone stays masked (AC-P3.10) — it is the name, not the number,
   * that makes the row actionable.
   */
  patientName: string | null;
  /** The appointment the reminder is for; null for a recall (« relance »), which carries no appointment. */
  appointmentAt: string | null;
  /** True when the row is a recall rather than a booking reminder. */
  isRecall: boolean;
}

/**
 * Filters for the delivery log. All optional; an unknown `status`/`channel` is **ignored** server-side rather than
 * refused, so a stale bookmark shows the full log instead of a French error about a query parameter.
 */
export interface ReminderLogParams {
  status?: ReminderDeliveryStatus;
  /** `SMS` | `WhatsApp`. */
  channel?: string;
  /** Inclusive clinic-local calendar days, `yyyy-MM-dd`. */
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The « Rappels » page in one read.
 *
 * ⚠️ The three counters are **clinic-wide and ignore the filters** — never derive them from `page.items`, which
 * would turn them into « les échecs parmi ces 25 ». `failedRecent` spans several days rather than today, so a
 * send that failed at 23:00 is still counted the next morning.
 */
export interface ReminderLogDto {
  page: PagedResponse<ReminderStatusDto>;
  sentToday: number;
  pending: number;
  failedRecent: number;
}

/** Payload posted after a successful Meta Embedded-Signup run (Cloud onboarding). */
export interface ConnectWhatsAppRequest {
  code: string;
  wabaId: string;
  phoneNumberId: string;
}

/**
 * PUT payload. Non-secret fields replace the stored values. Secrets (`smsApiKey`, `whatsAppAccessToken`)
 * are write-only: omit/blank ⇒ the stored secret is left unchanged; a value ⇒ re-encrypted & replaced.
 */
export interface UpdateReminderSettingsRequest {
  smsEnabled?: boolean | null;
  whatsAppEnabled?: boolean | null;
  smsSenderId?: string | null;
  whatsAppPhoneNumberId?: string | null;
  whatsAppTemplateName?: string | null;
  whatsAppTemplateLanguage?: string | null;
  smsApiKey?: string;
  whatsAppAccessToken?: string;
  // Non-secret per-clinic overrides (blank/empty ⇒ cleared/inherit).
  smsApiUrl?: string | null;
  whatsAppApiUrl?: string | null;
  leadTimeHours?: number[] | null;
  messageTemplateBody?: string | null;
}

interface Result<T> {
  isSuccess: boolean;
  value: T | null;
  error: string | null;
}

export const reminderSettingsApi = {
  get: async (): Promise<ReminderSettingsDto> => {
    const result = await apiGet<Result<ReminderSettingsDto>>('/clinics/reminder-settings');
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to load reminder settings');
    }
    return result.value;
  },

  update: async (data: UpdateReminderSettingsRequest): Promise<ReminderSettingsDto> => {
    const result = await apiPut<Result<ReminderSettingsDto>>('/clinics/reminder-settings', data);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to update reminder settings');
    }
    return result.value;
  },

  // Cloud-only WhatsApp Embedded Signup. connect posts the SDK result; disconnect clears the connection.
  // A backend failure surfaces as an ApiError (thrown by the client) carrying the French message.
  connectWhatsApp: async (data: ConnectWhatsAppRequest): Promise<ReminderSettingsDto> => {
    const result = await apiPost<Result<ReminderSettingsDto>>('/clinics/whatsapp/connect', data);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to connect WhatsApp');
    }
    return result.value;
  },

  disconnectWhatsApp: async (): Promise<ReminderSettingsDto> => {
    const result = await apiDelete<Result<ReminderSettingsDto>>('/clinics/whatsapp/connect');
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to disconnect WhatsApp');
    }
    return result.value;
  },

  // Recent reminder outbox rows with their delivery status (admin-only, AC-3).
  status: async (take = 20): Promise<ReminderStatusDto[]> => {
    const result = await apiGet<Result<ReminderStatusDto[]>>(`/clinics/reminder-status?take=${take}`);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to load reminder status');
    }
    return result.value;
  },

  /**
   * One page of the delivery log for the « Rappels » page, plus the clinic's three counters.
   *
   * Every filter is sent to the server. Filtering the returned page in the browser would answer a different
   * question — « les échecs parmi ces 25 » — which is the defect the paging work removed from every other list.
   *
   * Not admin-gated, unlike `status` above: reading the log is what a secretary fielding « je n'ai rien reçu »
   * needs, and a row carries a name and a masked phone, no secrets.
   */
  log: async (params: ReminderLogParams = {}): Promise<ReminderLogDto> => {
    const result = await apiGet<Result<ReminderLogDto>>('/clinics/reminder-log', params);
    if (!result.isSuccess || !result.value) {
      throw new Error(result.error || 'Failed to load reminder log');
    }
    return result.value;
  },
};

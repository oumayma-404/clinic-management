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
  /**
   * Optimistic-concurrency token. ⚠️ Round-trip it on `update` — without it two tabs on « Configurer les canaux »
   * both reported success while one set of channel settings silently replaced the other.
   */
  version: number;

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
  // Outbound email (SMTP) — the channel that delivers generated documents. Password never returned.
  smtpHost: string | null;
  smtpPort: number | null;
  smtpUseTls: boolean | null;
  smtpUsername: string | null;
  smtpPasswordConfigured: boolean;
  smtpFromAddress: string | null;
  smtpFromName: string | null;
  // Per-channel effective status: whether the resolved settings + credentials make the channel sendable.
  smsEffectiveStatus: ReminderEffectiveStatus;
  whatsAppEffectiveStatus: ReminderEffectiveStatus;
  emailEffectiveStatus: ReminderEffectiveStatus;
  // WhatsApp Embedded-Signup connection metadata (read-only; token never returned).
  whatsAppBusinessAccountId: string | null;
  whatsAppConnectionStatus: WhatsAppConnectionStatus;
  whatsAppLastError: string | null;
  whatsAppConnectedAt: string | null;
  /**
   * AC-1.7 — are this cabinet's WhatsApp credentials ours to provision rather than theirs to type? Where true the
   * three manual fields are absent from the form and the handler refuses them; « Connecter WhatsApp » on
   * « Rappels » owns the connection instead.
   */
  whatsAppVendorManaged: boolean
}

/**
 * Delivery-status value for a reminder outbox row (mirrors backend ReminderDeliveryStatus).
 *
 * ⚠️ `blocked` (L3a) is its own state, not a flavour of the other two: the row is **not** waiting its turn
 * (nothing changes on its own — the channel is off, unconfigured or unimplemented) and it has **not** failed
 * (nothing was attempted, and it will send once the channel works). It used to be indistinguishable from
 * `pending`, which is exactly how a queue that had silently stopped sending looked normal.
 */
export type ReminderDeliveryStatus = 'sent' | 'pending' | 'failed' | 'blocked';

/** One recent reminder outbox row shown on the admin delivery-status surface. */
export interface ReminderStatusDto {
  id: string;
  channel: string;
  recipientMasked: string;
  /** Whose reminder this was, so the name can be the way to their fiche. Null with `patientName`. */
  patientId?: string | null;
  status: ReminderDeliveryStatus;
  /** Why it failed — or, for a `blocked` row, why it cannot be sent. Both come off the row's one reason field. */
  failureReason: string | null;
  /**
   * **Why** a blocked row is blocked, machine-readably — the backend `OutboxBlockReason` member's own name. Null on
   * every non-blocked row.
   *
   * ⚠️ Branch on **this**, never on `failureReason`'s French prose (AC-4.9). Recovering behaviour by matching a
   * sentence is the `Contains("déjà facturée")` practice the backend deleted in `adoption-gaps-remediation`, and it
   * would mean rewording a message silently changed what this screen does.
   */
  blockReason: string | null;
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
 * ⚠️ The four counters are **clinic-wide and ignore the filters** — never derive them from `page.items`, which
 * would turn them into « les échecs parmi ces 25 ». `failedRecent` spans several days rather than today, so a
 * send that failed at 23:00 is still counted the next morning.
 */
export interface ReminderLogDto {
  page: PagedResponse<ReminderStatusDto>;
  sentToday: number;
  pending: number;
  failedRecent: number;
  /**
   * Queued but not sendable (L3a). The counter the whole `blocked` status exists for: a queue that has silently
   * stopped sending is the defect, so « N rappels bloqués » has to be a number on the page. Unbounded by date,
   * like `pending`.
   */
  blocked: number;
  /**
   * How many of `blocked` are waiting on the WhatsApp reminder forfait rather than on a channel (AC-4.9).
   *
   * ⚠️ A **subset** of `blocked`, not a fifth status. It exists because « 12 bloqués » cannot tell a practice whether
   * to configure a channel or ask us for more messages — two entirely different actions behind one number.
   */
  heldByAllowance: number;
  /**
   * Blocked because the WhatsApp **sender** cannot send — an unapproved template, or a number Meta has stopped.
   *
   * ⚠️ Counted apart from `heldByAllowance` because the remedy differs: one is answered by more messages, the
   * other by the connection being fixed. They used to be one figure, so a row the log itself badges « numéro »
   * was reported as « en attente de forfait » and the practice waited for something that was never coming.
   */
  heldBySender: number;
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
  /** The version read from the server. Omitted (or 0) the server skips the check. */
  version?: number;

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
  // Outbound email (SMTP). `smtpPassword` is write-only like the other two secrets.
  smtpHost?: string | null;
  smtpPort?: number | null;
  smtpUseTls?: boolean | null;
  smtpUsername?: string | null;
  smtpPassword?: string;
  smtpFromAddress?: string | null;
  smtpFromName?: string | null;
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

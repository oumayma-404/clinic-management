import { apiGet, apiPut, apiPost, apiDelete } from './client';

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
};

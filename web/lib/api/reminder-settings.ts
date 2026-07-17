import { apiGet, apiPut } from './client';

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
};

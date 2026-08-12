import { apiGet, apiPost } from './client';

/** What « Sécurité » shows about this account's second factor (`hosted-security-hardening` FR-1.5). */
export interface TotpState {
  isEnrolled: boolean;
  /**
   * Whether this account is **obliged** to hold one — the deployment requires it of administrators and this
   * account is one.
   *
   * ⚠️ It carries the deployment's answer rather than the role, so the screen's wording follows the same rule
   * the refusal does: a voluntarily-enrolled admin on a profile that requires nothing may disable theirs.
   */
  isRequired: boolean;
  recoveryCodesRemaining: number | null;
  enrolledAt: string | null;
}

export interface RecoveryCodes {
  recoveryCodes: string[];
}

export interface StepUpResult {
  confirmationToken: string;
}

/**
 * Below this many unused codes, « Sécurité » warns (`hosted-security-hardening` FR-1.5).
 *
 * ⚠️ The warning appears **only where the user can act on it** — on this screen, beside « Régénérer ». There is
 * deliberately no nudge, badge or prompt anywhere else in the app (Stated Assumption 7): a standing banner about
 * recovery codes on the agenda is exactly the kind of thing people learn to dismiss without reading.
 */
export const LOW_RECOVERY_CODES = 2;

export const securityApi = {
  getTotpState: async (token?: string | null): Promise<TotpState> =>
    apiGet<TotpState>('/auth/totp', undefined, token ?? undefined),

  regenerateRecoveryCodes: async (totpCode: string, token?: string | null): Promise<RecoveryCodes> =>
    apiPost<RecoveryCodes>('/auth/totp/recovery-codes', { totpCode }, token ?? undefined),

  disableTotp: async (totpCode: string, token?: string | null): Promise<void> => {
    // A POST, not a DELETE: it carries the current code, which is what authorises it.
    await apiPost<unknown>('/auth/totp/disable', { totpCode }, token ?? undefined);
  },

  /**
   * Re-authenticates for one named action and returns a single-use confirmation.
   *
   * Either proof will do — the password **or** a current code (OQ-2): a shell user who signs in by biometrics
   * may genuinely not remember their password, and demanding it would put the guarded action out of reach.
   */
  stepUp: async (
    action: string,
    proof: { password?: string; totpCode?: string },
    token?: string | null
  ): Promise<StepUpResult> =>
    apiPost<StepUpResult>('/auth/step-up', { action, ...proof }, token ?? undefined),

  resetUserTotp: async (userId: string, confirmationToken: string, token?: string | null): Promise<void> => {
    await apiPost<unknown>(`/users/${userId}/totp/reset`, { confirmationToken }, token ?? undefined);
  },
};

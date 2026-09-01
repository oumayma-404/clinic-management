import { apiDelete, apiGet, apiPost } from './client';

/**
 * The backend's `Result<T>` envelope, as `reminder-allowance.ts`, `reminder-settings.ts` and `clinics.ts` already
 * declare it locally.
 *
 * ⚠️ **`AuthController` returns it on these three actions** (`Ok(result)`, not `Ok(result.Value)`) — the same shape
 * `login`, `totp/enrol` and `recovery` return and the BFF routes already unwrap. Reading the DTO level instead was
 * not a type error anywhere: `tsc` believed the annotation, so every field came back `undefined` and each of the
 * three failed *differently and silently*. `stepUp` handed `undefined` to `apiHeaders`, whose `if (stepUpToken)`
 * then omitted the header entirely, so « Télécharger l'archive » answered **403 « confirmation récente »** on a
 * step-up that had just succeeded. `getTotpState` made `enrolledAt !== null` true for **every** account, so the
 * step-up dialog offered a code field to users holding no second factor.
 */
interface Result<T> {
  isSuccess: boolean;
  value: T | null;
  error: string | null;
}

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

  /**
   * Whether the factor may be replaced right now without a code from it — i.e. this session was opened with a
   * recovery code minutes ago.
   *
   * ⚠️ Reported so the screen can say so **while it lasts**. Somebody who redeemed a code and dismissed the
   * prompt on the login screen has no other way to learn the offer exists, and it closes silently.
   */
  mayReplace: boolean;
}

export interface RecoveryCodes {
  recoveryCodes: string[];
}

/**
 * One live session of this account, as « Mes appareils » lists it.
 *
 * <p>It lives here rather than in `auth.ts` for the reason that module states about itself: everything there is
 * <b>anonymous</b> — read before a session exists — while this pair is authenticated and comes back inside the
 * `Result&lt;T&gt;` envelope this module already unwraps.</p>
 */
export interface SessionDevice {
  id: string;
  /** What the device called itself at sign-in. Null is ordinary — most sign-ins send nothing. */
  deviceLabel: string | null;
  createdAtUtc: string;
  /**
   * The last credential rotation.
   *
   * ⚠️ **Not the last human action**, and the screen must not call it one. An open tab renews roughly every half
   * hour on its own, so this advances while nobody is touching the machine. « Dernière activité » is honest;
   * « dernière utilisation » would tell a user their unattended reception PC was being used.
   */
  lastActiveAtUtc: string;
  expiresAtUtc: string;
  /** Whether « Rester connecté sur cet appareil » was ticked when this session was opened. */
  isTrusted: boolean;
  /**
   * Whether this is the session making the request.
   *
   * ⚠️ **`false` on every row means « I cannot tell », not « none of these is you ».** A token issued before the
   * session-family claim existed names no chain, so during a rolling deploy a signed-in user legitimately sees a
   * list with nothing marked — the screen must conclude nothing from the absence.
   */
  isCurrent: boolean;
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

/**
 * Unwraps the envelope, throwing the server's own French sentence when it carries one.
 *
 * A wrapped success whose `value` is null is a contract violation rather than a business refusal, so it throws too:
 * returning it would put `undefined` back into exactly the callers this exists to protect.
 */
function unwrap<T>(result: Result<T>, fallback: string): T {
  if (!result.isSuccess || !result.value) {
    throw new Error(result.error || fallback);
  }
  return result.value;
}

export const securityApi = {
  getTotpState: async (token?: string | null): Promise<TotpState> =>
    unwrap(
      await apiGet<Result<TotpState>>('/auth/totp', undefined, token ?? undefined),
      "L'état du second facteur n'a pas pu être lu.",
    ),

  regenerateRecoveryCodes: async (totpCode: string, token?: string | null): Promise<RecoveryCodes> =>
    unwrap(
      await apiPost<Result<RecoveryCodes>>('/auth/totp/recovery-codes', { totpCode }, token ?? undefined),
      "Les codes de récupération n'ont pas pu être régénérés.",
    ),

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
    unwrap(
      await apiPost<Result<StepUpResult>>('/auth/step-up', { action, ...proof }, token ?? undefined),
      "Votre identité n'a pas pu être confirmée.",
    ),

  resetUserTotp: async (userId: string, confirmationToken: string, token?: string | null): Promise<void> => {
    await apiPost<unknown>(`/users/${userId}/totp/reset`, { confirmationToken }, token ?? undefined);
  },

  /**
   * « Mes appareils » — every live session of the signed-in account.
   *
   * ⚠️ Takes no user parameter, and the server scopes it to the caller's own account. There is no shape of this
   * call that reads a colleague's devices, which is deliberate: when somebody last sat at their desk is not a
   * fact this product hands to anybody else.
   */
  listSessions: async (token?: string | null): Promise<SessionDevice[]> =>
    unwrap(
      await apiGet<Result<SessionDevice[]>>('/auth/sessions', undefined, token ?? undefined),
      "La liste de vos appareils n'a pas pu être lue.",
    ),

  /**
   * Ends one device's session. Succeeds whether it was live or already ended — two tabs pressing
   * « Déconnecter » on the same row is ordinary, and the caller's intent is satisfied either way.
   */
  endSession: async (sessionId: string, token?: string | null): Promise<void> => {
    await apiDelete<unknown>(`/auth/sessions/${sessionId}`, token ?? undefined);
  },
};

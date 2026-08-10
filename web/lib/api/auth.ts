import { apiGet, apiPost } from './client';

/**
 * What `GET /api/auth/mode` answers about this deployment. Anonymous — it is read before anyone has a session.
 */
export interface AuthModeDto {
  /**
   * `Local` when the product issues its own email+password tokens, `Cloud` when Auth0 does.
   *
   * ⚠️ **Capitalised, because that is what the wire carries.** `AuthController.GetMode` returns
   * `LocalAuthConfig.LocalMode`/`CloudMode`, whose values are the strings `"Local"`/`"Cloud"` — the camelCase JSON
   * policy renames *properties*, never string values. This was declared lowercase, which no consumer had caught
   * because both read `selfRegistrationEnabled` only: the next `if (dto.mode === 'local')` would have been `false`
   * for ever, and TypeScript would have accepted it. Not to be confused with `useSession().mode`, which is
   * genuinely lowercase — it comes from Next's own `AUTH_MODE`, not from this endpoint.
   */
  mode: 'Local' | 'Cloud';
  /**
   * Whether staff may mint their own account with the clinic's join code (`POST /api/auth/register`).
   *
   * ⚠️ **Not derivable from `mode`, which is exactly why the server answers it.** The browser learns the mode from
   * the Next server's `AUTH_MODE`, and that reads `local` on a clinic's own PC *and* on the hosted multi-tenant
   * backend — but only the first is a LAN, where reaching the endpoint at all means being inside the surgery. On
   * the internet the six-character code is a password everyone who ever worked there knows, so the hosted profile
   * closes self-registration and an admin creates the accounts instead.
   */
  selfRegistrationEnabled: boolean;
  /**
   * Whether a visitor may create their **own** clinic and admin account from the public internet
   * (`POST /api/auth/signup`, then an emailed verification link).
   *
   * ⚠️ **The opposite question from `selfRegistrationEnabled`, not a rename of it.** That one is « may a stranger
   * join an *existing* clinic by typing its shared six-character code? » — closed on the hosted backend precisely
   * because that code is a password everyone who ever worked at the practice knows. This one hands out no shared
   * secret at all: the gate is a single-use token emailed to an address the visitor has to control, and what it
   * creates is a brand-new clinic with exactly one member. So on the hosted profile the first is `false` and the
   * second is `true`, and reading either as the other would be a real security decision made by accident.
   *
   * ⚠️ **Optional, and read as `=== true`.** `web` and `api` are separate containers in the hosted topology, so a
   * rolling deploy legitimately serves this page from a build newer than the API answering it — and `apiGet` does
   * no shape validation, so a required `boolean` here would simply be `undefined` asserting a type it does not
   * have.
   */
  publicSignupEnabled?: boolean;
  /**
   * Whether a cabinet's right to record new work is a dated entitlement here (`clinic-subscription`, FR-11).
   *
   * ⚠️ **This is what the client mounts the « Abonnement » screen and the banner from — never a probe of
   * `GET /api/subscription`.** That endpoint's 404 stays as the server-side guarantee, but a network failure and a
   * genuine 404 are indistinguishable to a probe, and EC-13 requires a failed read to be *retryable* rather than
   * read as « aucun abonnement ».
   *
   * ⚠️ **Optional, and read as `=== true`**, following `publicSignupEnabled`'s convention: `web` and `api` are
   * separate containers in the hosted topology, so a rolling deploy legitimately serves this page from a build newer
   * than the API answering it. A required `boolean` here would be `undefined` asserting a type it does not have —
   * and the safe direction is « no subscription », which is how the two unaffected deployment kinds already behave.
   */
  requiresSubscription?: boolean;
}

/** What `POST /api/auth/signup` requests. Mirrors the backend `ClinicSignUpRequest`. */
export interface ClinicSignUpRequest {
  clinicName: string;
  fullName: string;
  email: string;
  password: string;
  phone?: string;
  address?: string;
  city?: string;
  /**
   * ⚠️ No `phone`. The form has one phone field and it is the **clinic's**; sending it here persisted it on
   * `Doctor` as the practitioner's own contact, which the visitor never typed. An empty field they can fill from
   * « Mon profil » is easier to notice than a wrong one.
   */
  doctorInfo?: {
    firstName: string;
    lastName: string;
    specialty: string;
  };
  /**
   * The onboarding wizard's « Horaires » step, in `Clinic.workingHoursJson`'s own shape. Sent because signup **is**
   * that wizard now: the visitor answers all three steps in one sitting and the emailed link only confirms, so a
   * field collected and not sent is a step silently discarded between the form and the clinic.
   */
  workingHoursJson?: string;
}

/**
 * The 202 body. One neutral sentence and nothing else — deliberately identical whether the address was free,
 * already an account, or already had a pending signup, so the page cannot become an enumeration oracle by
 * rendering a difference the server took care not to send.
 */
export interface ClinicSignUpResultDto {
  message: string;
}

/** The 200 body of a successful verification. Carries **no** token and sets no cookie — the visitor signs in. */
export interface ClinicSignUpVerificationDto {
  message: string;
  clinicName: string;
}

export const authApi = {
  /**
   * Reads the deployment's auth capabilities. `null` skips the bearer token: this is the one call made before a
   * session exists, and attaching a stale one would fail the request rather than the auth.
   */
  getMode: async (): Promise<AuthModeDto> => apiGet<AuthModeDto>('/auth/mode', undefined, null),

  /** Anonymous, like every other call on this module — a visitor signing up has no session by definition. */
  signUp: async (request: ClinicSignUpRequest): Promise<ClinicSignUpResultDto> =>
    apiPost<ClinicSignUpResultDto>('/auth/signup', request, null),

  verifySignUp: async (token: string): Promise<ClinicSignUpVerificationDto> =>
    apiPost<ClinicSignUpVerificationDto>('/auth/signup/verify', { token }, null),
};

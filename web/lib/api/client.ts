const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public originalError?: unknown,
    /**
     * Machine-readable failure tag from the backend's `{ error, code }` body, when it sent one.
     *
     * Lets a caller branch on *which* refusal this is without matching the French message — prose gets reworded,
     * and a behaviour that hinges on it breaks silently. Undefined for the vast majority of failures, which are
     * only ever displayed. See `Result.Code` on the backend for the same reasoning.
     */
    public code?: string
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** Failure codes the client actually branches on. Mirrors the backend constants that emit them. */
export const ApiErrorCode = {
  /**
   * The appointment falls outside the practitioner's working hours. Advisory, not a prohibition: resubmit with
   * `allowOutsideWorkingHours: true` to book it anyway (the backend records the exception on the appointment).
   * Emitted by `AppointmentScheduling.OutsideWorkingHoursCode`.
   */
  OutsideWorkingHours: 'outside_working_hours',
  /**
   * The slot already holds a booking for the same practitioner. Advisory: retry with `allowOverlap: true` to book it
   * anyway (the backend records the acknowledgement on the appointment, which is also what exempts the row from the
   * database's double-booking exclusion constraint).
   * Emitted by `AppointmentScheduling.SlotTakenCode`.
   */
  SlotTaken: 'slot_taken',
  /**
   * A patient matching the one being created is already on file (same name + date of birth, same name when no date
   * of birth was supplied, or the same phone number). Advisory: resubmit with `allowDuplicate: true` to create the
   * second record anyway.
   *
   * The message names who was matched and why, so a caller shows it verbatim rather than writing its own. Emitted by
   * `PatientDuplicateIndex.RefusalCode`.
   */
  PatientDuplicate: 'patient_duplicate',
  /**
   * The request never reached the server — DNS, TLS, a dropped Wi-Fi link, the API not running. Client-side
   * only: no backend emits it, because by definition nothing answered.
   *
   * It exists so a caller can tell « nous n'avons pas pu joindre le serveur » (worth a « Réessayer », the
   * same request will very likely work in a moment) apart from a real refusal (retrying changes nothing).
   * Branching on `status === 0` alone would also catch the CORS and unexpected-throw paths below, which are
   * faults rather than transport failures. See `isNetworkError` in `lib/errors.ts`.
   */
  Network: 'network',
  /**
   * This build of the native shell is older than the server's floor, so **every** route but
   * `/api/meta/client-requirements` will refuse it with 426 until it is updated. Not advisory and not
   * recoverable in-app: `<ClientVersionGate>` listens for it through {@link onClientTooOld} and takes the screen.
   *
   * ⚠️ It is deliberately **not** a session failure. A 401 signs the user out; this must not, because the
   * account is fine and a login screen the app can never get past is the worse of the two states (AC-33).
   * Emitted by `ClientVersionMiddleware.TooOldCode`.
   */
  ClientTooOld: 'client_too_old',
} as const;

/** Whether the server has refused this client as too old at any point this session. See {@link onClientTooOld}. */
let clientRefusedAsTooOld = false;
type ClientTooOldListener = () => void;
const clientTooOldListeners = new Set<ClientTooOldListener>();

/**
 * Subscribe to « the server has refused this client as too old » (426). Returns an unsubscribe function.
 *
 * The listener fires for calls made through this module's helpers. The dozen raw-`fetch` blob/upload sites
 * deliberately keep their own response handling (plan R-5, so no legacy caller's error message changes) and so
 * do not notify — which costs nothing in practice: the floor refuses every route, so the next ordinary call
 * surfaces it. {@link isClientRefusedAsTooOld} exists for the same reason in the other direction — a gate that
 * mounts *after* the first refusal must still know.
 */
export function onClientTooOld(listener: ClientTooOldListener): () => void {
  clientTooOldListeners.add(listener);
  return () => {
    clientTooOldListeners.delete(listener);
  };
}

/** See {@link onClientTooOld}. */
export function isClientRefusedAsTooOld(): boolean {
  return clientRefusedAsTooOld;
}

/**
 * What the user is told when the request never reached the server (AC-43).
 *
 * ⚠️ This replaced *"Network error: Unable to connect to the API. Please check if the API is running and CORS
 * is configured correctly."* — English, and addressed to whoever deployed the app rather than to the dentist
 * reading it. `lib/errors.ts` passes an `ApiError` message through **verbatim**, so that string was the one
 * the toast actually showed. The wording matches `connectivity.tsx`'s « Serveur injoignable » banner, so the
 * two ways the app can notice the same outage do not describe it differently.
 */
export const NETWORK_ERROR_MESSAGE =
  "Impossible de joindre le serveur de la clinique. Vérifiez votre connexion au réseau local.";

/** In-memory access-token cache — see `getAccessToken`. Module scope so every caller shares one token. */
let cachedToken: { token: string; validUntilMs: number } | null = null;
/** The exchange currently in flight, so parallel callers await one request instead of opening N. */
let inFlightToken: Promise<string | null> | null = null;
/**
 * HTTP status of the last token exchange that failed to produce a token (`0` = network failure, `null` =
 * the last exchange succeeded). Lets callers tell "the session is over" (401 — the BFF has already cleared
 * the cookie) from "the server could not answer right now" (429/5xx/offline), which must not sign anyone out.
 */
let lastTokenFailureStatus: number | null = null;

/** See {@link lastTokenFailureStatus}. */
export function lastAccessTokenFailureStatus(): number | null {
  return lastTokenFailureStatus;
}

/** Renew this long before the reported expiry, so a token can't lapse mid-request. */
const TOKEN_RENEW_SKEW_MS = 60_000;
/** Cache window when the server reports no expiry (Cloud — the Auth0 SDK caches on its own side). */
const TOKEN_FALLBACK_TTL_MS = 30_000;

/**
 * What a status means when nothing better arrived, for the statuses worth naming individually.
 *
 * ⚠️ This map is the *specific* half only — read it through {@link statusMessageFr}, never directly. It used to
 * be the whole mechanism, and the lookup that consumed it did `if (french) errorMessage = french`, so a status
 * with no entry silently kept the raw `HTTP <n>: <statusText>` line built below. That was not a theoretical gap:
 * a 400 whose body failed to parse, and the 502/503/504 the Local same-origin front door (YARP → Next/Kestrel)
 * returns whenever the proxied hop is down, all reached a French-speaking dentist as « HTTP 502: Bad Gateway ».
 * The fallback is therefore **unconditional** now — an unnamed status still gets French.
 */
const STATUS_FALLBACK_FR: Record<number, string> = {
  401: "Votre session a expiré. Reconnectez-vous.",
  403: "Vous n'avez pas les droits nécessaires pour cette action.",
  404: "Élément introuvable.",
  409: "Cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie. "
    + "Rechargez pour voir la version à jour, puis appliquez à nouveau votre modification.",
  // The client-version floor. `<ClientVersionGate>` normally takes the whole screen before this is ever read;
  // it is here for the raw-fetch sites, which surface their own message and would otherwise say « HTTP 426 ».
  426: "Cette version de l'application n'est plus prise en charge. Mettez-la à jour pour continuer.",
  500: "Une erreur est survenue lors du traitement de votre demande.",
  // The three the front-door proxy raises on its own — the API process is down, restarting, or too slow. They
  // are transport-shaped rather than a refusal, so they say « momentanément » : the same request is worth making
  // again, which « Une erreur est survenue » does not convey.
  502: "Le serveur de la clinique est momentanément indisponible.",
  503: "Le serveur de la clinique est momentanément indisponible.",
  504: "Le serveur de la clinique est momentanément indisponible.",
};

/**
 * The catch-all for every other status. Deliberately says three things: it failed, retrying is reasonable, and
 * who to tell if it keeps happening — a clinic has no console to read and no way to know a 418 from a 507.
 */
const GENERIC_STATUS_FALLBACK_FR =
  "Le serveur n'a pas pu traiter votre demande. Réessayez dans un instant, et prévenez votre support si cela persiste.";

/** French for any HTTP status, named or not. Never returns undefined — that was the defect. */
function statusMessageFr(status: number): string {
  return STATUS_FALLBACK_FR[status] ?? GENERIC_STATUS_FALLBACK_FR;
}

/**
 * A C# property path — `CnamInfo.IdentifiantUnique`, `Items[0].PlannedCost`. PascalCase either side of a dot is
 * the giveaway; French prose does not put a capital letter straight after a full stop with no space.
 */
const PASCAL_CASE_PATH = /\b[A-Z][A-Za-z0-9]*\.[A-Z][A-Za-z0-9]*/;

/**
 * Words that are common in English validation text and are **not** French words. Deliberately excludes the
 * near-homographs (`invalide`, `erreur`, `serveur`, `requis`) — `\b` boundaries keep `invalide` from matching
 * `invalid`, and leaving them out entirely costs nothing.
 */
const ENGLISH_MARKERS =
  /\b(the|this|that|these|those|field|value|must|match|is|are|was|were|be|been|being|not|cannot|unable|failed|fail|error|errors|occurred|occurs|required|require|invalid|one|more|and|with|of|to|for|please|try|again|request|bad|gateway|unexpected|internal|forbidden|unauthorized|found|allowed|expected|length|between|greater|less|than)\b/gi;

/**
 * Whether a message is machine text rather than something to put in front of a dentist.
 *
 * <p>Two distinct **English markers** are required, not one: a lone hit is how a legitimate French sentence with
 * a borrowed word gets thrown away, and discarding a real reason is worse than showing a slightly awkward one.
 * A PascalCase property path needs no corroboration — nothing else produces `Foo.Bar`.</p>
 */
function looksTechnical(text: string | undefined | null): boolean {
  const trimmed = text?.trim();
  if (!trimmed) return true;
  if (PASCAL_CASE_PATH.test(trimmed)) return true;
  const hits = trimmed.match(ENGLISH_MARKERS);
  return new Set(hits?.map((h) => h.toLowerCase())).size >= 2;
}

/**
 * The readable half of a ProblemDetails `errors` bag.
 *
 * ⚠️ **The key is dropped.** This used to build `` `${key}: ${value}` ``, so a French sentence was followed by
 * « CnamInfo.IdentifiantUnique: The field must match… » — a C# property path and an English regex complaint, in
 * the one place the user is being told they did something wrong. The key names a DTO field, not anything the
 * user typed, so it can never help them; the *value* sometimes can, when the backend wrote it in French.
 *
 * Filtered per value rather than all-or-nothing: one bag can legitimately hold a French message from a handler
 * and an English one from model binding, and there is no reason to lose the first because of the second.
 */
function readableValidationDetail(errors: unknown): string {
  if (!errors || typeof errors !== 'object') return '';
  const parts: string[] = [];
  for (const value of Object.values(errors as Record<string, unknown>)) {
    for (const entry of Array.isArray(value) ? value : [value]) {
      const text = typeof entry === 'string' ? entry.trim() : '';
      if (text && !looksTechnical(text)) parts.push(text);
    }
  }
  return parts.join(' ');
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    // Kept as a named value rather than rebuilt twice: it is the sentinel meaning "nothing usable arrived",
    // and the two places that compare against it have to compare against the *same* string.
    const rawStatusLine = `HTTP ${response.status}: ${response.statusText}`;
    let errorMessage = rawStatusLine;
    // The backend's optional machine-readable failure tag (`{ error, code }`) — see ApiErrorCode.
    let errorCode: string | undefined;
    try {
      const errorData = await response.json();
      if (errorData && typeof errorData.code === 'string' && errorData.code) {
        errorCode = errorData.code;
      }
      // Some endpoints return the failure reason as a bare JSON string (e.g. BadRequest(result.Error)).
      // Surface it instead of falling back to the generic "HTTP 400: ..." message.
      if (typeof errorData === 'string') {
        if (errorData.trim()) {
          errorMessage = errorData;
        }
      } else if (errorData) {
        // `error` is the canonical backend failure body (`{ error }` from ApiControllerBase /
        // ExceptionMiddleware) and must be read first — without it every Result.Failure reason in the app
        // was dropped and the user only saw "HTTP 400: Bad Request". `title`/`message` still cover ASP.NET
        // ProblemDetails and the raw Result envelope a few endpoints return (Auth/Clinics BadRequest(result)).
        // `typeof … === 'string'`: a body whose `error` is an object (a nested Result, a serialised exception)
        // used to be assigned straight through and reached the toast as « [object Object] ». Ignoring it lets
        // the unconditional French status fallback below answer instead.
        const reason = [errorData.error, errorData.title, errorData.message].find(
          (candidate) => typeof candidate === 'string' && candidate.trim(),
        );
        if (reason) {
          errorMessage = reason;
        }
        if (errorData.errors) {
          // A ProblemDetails validation response. Its `title` is ASP.NET's own English « One or more validation
          // errors occurred. », so the base message is just as likely to be machine text as the detail is —
          // both are tested, and if neither survives the message is reset to the sentinel so the unconditional
          // French status fallback below speaks instead of appending nonsense to nonsense.
          const detail = readableValidationDetail(errorData.errors);
          const baseIsUsable = errorMessage !== rawStatusLine && !looksTechnical(errorMessage);
          if (detail) {
            errorMessage = baseIsUsable ? `${errorMessage} ${detail}` : detail;
          } else if (!baseIsUsable) {
            errorMessage = rawStatusLine;
          }
        }
      }
    } catch {
      // If response is not JSON, use status text
    }

    // Rate-limit refusals carry a French `{ error }` body from the API, so the branch above normally
    // surfaces it. This is the safety net for a 429 whose body is missing or unparseable (e.g. refused by
    // an intermediary): "HTTP 429: Too Many Requests" is not something to show a clinic
    // (security-hardening AC-4.5).
    if (response.status === 429 && errorMessage === rawStatusLine) {
      const retryAfter = Number(response.headers.get('retry-after'));
      errorMessage = Number.isFinite(retryAfter) && retryAfter > 0
        ? `Trop de tentatives. Veuillez réessayer dans ${Math.ceil(retryAfter / 60)} minute(s).`
        : 'Trop de tentatives. Veuillez réessayer dans quelques minutes.';
    }

    // Some statuses arrive with no body at all — most importantly the 403 that ASP.NET's authorization
    // pipeline returns before any handler runs, which short-circuits the `{ error }` contract, and the
    // 502/503/504 the Local front-door proxy raises before the API is even asked. Falling back to the raw
    // status line put « HTTP 403: Forbidden » in front of a French-speaking dentist.
    //
    // ⚠️ Unconditional on purpose (see STATUS_FALLBACK_FR): the previous `if (french)` guard meant every status
    // absent from the map — 400 with an unparseable body, 405, 413, 502… — fell through to English anyway, and
    // `lib/errors.ts` hands an ApiError message to the toast verbatim.
    if (errorMessage === rawStatusLine) {
      errorMessage = statusMessageFr(response.status);
    }

    // The one refusal no screen can act on: hand it to the gate before the error travels on as a toast.
    if (response.status === 426) {
      clientRefusedAsTooOld = true;
      clientTooOldListeners.forEach((listener) => listener());
    }

    throw new ApiError(response.status, errorMessage, undefined, errorCode);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.includes('application/json')) {
    return response.json();
  }
  return response.text() as unknown as T;
}

/**
 * Sends a request with an access token and, on a 401, acquires a fresh token **once** and retries.
 *
 * Access tokens are short-lived (~30 min) and renewed silently from the HttpOnly cookie, so a page left open
 * past expiry must not show the user an error: the first 401 is expected and the retry succeeds
 * (security-hardening AC-5.7). Retrying exactly once is deliberate — a genuine 401 (revoked session,
 * deactivated account, forced password change) must surface promptly rather than spin.
 *
 * The retry is skipped when the caller supplied its own token: that caller owns its lifecycle, and silently
 * substituting a different one would be surprising.
 *
 * `requestFn` therefore takes the token as a parameter rather than closing over it — the retry has to be able
 * to build the request again with a *different* token.
 */
async function handleRequest<T>(
  accessToken: string | null | undefined,
  requestFn: (token: string | null) => Promise<Response>,
): Promise<T> {
  const explicit = accessToken !== undefined;
  let token = explicit ? accessToken! : await getAccessToken();

  for (let attempt = 0; ; attempt++) {
    try {
      return await handleResponse<T>(await requestFn(token));
    } catch (err) {
      const canRetry = !explicit && attempt === 0 && err instanceof ApiError && err.status === 401;
      if (canRetry) {
        // Force a real renewal: the token we just used is the cached one, so without this the retry would
        // replay the identical rejected token and the 401 would surface to the user (AC-5.7).
        token = await getAccessToken(true);
        continue;
      }

      if (err instanceof TypeError && err.message.includes('fetch')) {
        throw new ApiError(0, NETWORK_ERROR_MESSAGE, err, ApiErrorCode.Network);
      }
      if (err instanceof ApiError) {
        throw err;
      }
      // Not a transport failure — something threw where nothing should. It keeps `status: 0` but NOT the
      // network code, so « Réessayer » is not offered for a fault that retrying cannot fix.
      throw new ApiError(0, err instanceof Error ? err.message : "Une erreur inattendue s'est produite.", err);
    }
  }
}

/**
 * The single place the app acquires an API access token (mode-aware: an Auth0 token in Cloud, the local
 * JWT in Local).
 *
 * **Cached in memory until shortly before it expires.** This used to exchange the session cookie on every
 * single API call, so one page load opened a dozen `POST /api/auth/refresh` calls. That endpoint shares a
 * 30-per-5-minutes rate-limit bucket with `login`, so ordinary use exhausted it within seconds: the refresh
 * started returning 429, which surfaced as a lost session, bounced the user to a login page that was itself
 * rate-limited, and read as an endless redirect loop. Pass `forceRenew` to bypass the cache after the API
 * rejects a token as expired.
 *
 * **Exported deliberately, and this must stay the only implementation.** Seven per-resource modules used to
 * carry their own private copy of this fetch. That was harmless while the token lived 12 hours, but it
 * becomes a real defect once tokens are short-lived and renewed: any copy that bypasses this helper keeps
 * using an expired token and fails silently, surfacing to the user as a random unexplained error
 * (security-hardening plan risk R-4). Renewal logic can only live in one place if acquisition does.
 *
 * If you need a token in a new module, import this — do not re-implement the fetch.
 */
export async function getAccessToken(forceRenew = false): Promise<string | null> {
  if (!forceRenew && cachedToken && Date.now() < cachedToken.validUntilMs) {
    return cachedToken.token;
  }

  // A forced renewal must not be served by a fetch that is already in flight for the *old* token.
  if (forceRenew) {
    cachedToken = null;
    inFlightToken = null;
  }

  // Single-flight: a page load fires many parallel API calls, and without this each one would open its
  // own exchange.
  if (inFlightToken) {
    return inFlightToken;
  }

  inFlightToken = fetchAccessToken();
  try {
    return await inFlightToken;
  } finally {
    inFlightToken = null;
  }
}

/** Drop the cached token — call after anything that invalidates the session (e.g. logout). */
export function clearCachedAccessToken(): void {
  cachedToken = null;
  inFlightToken = null;
}

async function fetchAccessToken(): Promise<string | null> {
  try {
    const response = await fetch('/bff/auth/token', {
      credentials: 'include', // Include cookies for session
    });
    if (response.ok) {
      const data = await response.json();
      const token: string | null = data.accessToken || null;
      lastTokenFailureStatus = token ? null : response.status;
      if (token) {
        // Renew a minute early so a request can't leave with a token that expires in flight. When the
        // server does not report an expiry (Cloud, where the Auth0 SDK does its own caching), fall back to
        // a short TTL — still enough to collapse a page load's burst into one exchange.
        const serverExpiry = data.expiresAt ? Date.parse(data.expiresAt) : Number.NaN;
        cachedToken = {
          token,
          validUntilMs: Number.isFinite(serverExpiry)
            ? serverExpiry - TOKEN_RENEW_SKEW_MS
            : Date.now() + TOKEN_FALLBACK_TTL_MS,
        };
      }
      return token;
    }
    // A refusal invalidates the cache: never serve a token the server has stopped standing behind.
    lastTokenFailureStatus = response.status;
    cachedToken = null;
  } catch {
    // Network failure — keep any cached token. A blip must not look like a lost session.
    lastTokenFailureStatus = 0;
  }
  return null;
}


/**
 * What the native shells identify themselves with, so the server can refuse a build below its floor
 * (`ClientVersionMiddleware.HeaderName`). A browser sends it on no request at all, which is exactly what keeps
 * the floor from ever applying to the web app.
 */
export const CLIENT_VERSION_HEADER = 'X-Client-Version';

/**
 * Whether this request declares a JSON body — the only thing that ever differed between the two header shapes.
 * `'none'` covers both callers that need it: a multipart upload (the browser must add its own boundary) and a
 * GET that downloads a blob (no body to describe at all).
 */
export type ApiContentType = 'json' | 'none';

/**
 * **The one place this app writes request headers for the clinic API.** Folded out of the old
 * `createHeaders`/`formDataHeaders` pair, and exported because fourteen raw-`fetch` sites across eight modules
 * hand-wrote the same object — so `X-Client-Version` would have reached the routes that go through this file and
 * silently missed every PDF, every CSV export and every upload. That is the shape of defect the `api-headers`
 * check in `scripts/check-responsive.mjs` now fails on.
 *
 * The shell version is read as a **feature detection**: absent bridge ⇒ no header ⇒ byte-identical to before.
 */
export function apiHeaders(accessToken?: string | null, contentType: ApiContentType = 'json'): HeadersInit {
  const headers: Record<string, string> = {};

  if (contentType === 'json') {
    headers['Content-Type'] = 'application/json';
  }
  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`;
  }

  const shellVersion = typeof window !== 'undefined' ? window.__clinicShell?.version : undefined;
  if (shellVersion) {
    headers[CLIENT_VERSION_HEADER] = shellVersion;
  }

  return headers;
}

export async function apiGet<T>(endpoint: string, params?: Record<string, any>, accessToken?: string | null): Promise<T> {
  // Pass an origin base so a RELATIVE API base (`/api` in the same-origin front-door build, S4) parses —
  // `new URL('/api/foo')` throws "Invalid URL" without a base. Absolute bases ignore the second arg, so
  // this is a no-op for the Cloud build (absolute NEXT_PUBLIC_API_URL). Guard `window` (Finding 11): an
  // SSR render pass / generateMetadata / Node unit test importing this module has no `window`, and an
  // unconditional `window.location.origin` would throw ReferenceError before the URL is even built.
  const base = typeof window !== "undefined" ? window.location.origin : undefined;
  const url = new URL(`${API_BASE_URL}${endpoint}`, base);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        url.searchParams.append(key, String(value));
      }
    });
  }

  return handleRequest<T>(accessToken, (token) => fetch(url.toString(), {
    method: 'GET',
    headers: apiHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPost<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: apiHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiPut<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: apiHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiDelete<T>(endpoint: string, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
    headers: apiHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPostFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  // Headers are built INSIDE the callback so a 401 retry rebuilds them with the renewed token. Uploads are
  // exactly where a stale token bites — they are user-initiated after a period of reading, so they are the
  // most likely request to be the first one past the access token's expiry.
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: apiHeaders(token, 'none'),
    body: formData,
    credentials: 'include',
  }));
}

export async function apiPutFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: apiHeaders(token, 'none'),
    body: formData,
    credentials: 'include',
  }));
}



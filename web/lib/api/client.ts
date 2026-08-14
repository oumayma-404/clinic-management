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
  /**
   * This account must change its password before it may do anything else, so **every** route but
   * `/api/auth/change-password` refuses it with 403 until it has. Not a rights failure and not a session
   * failure: the account is fine and the fix is one screen away, which is why it is routed rather than shown
   * (AC-76 — on the hosted topology accounts are admin-provisioned, so this is the *normal* first sign-in).
   *
   * ⚠️ The login path already handles it through the `local_must_change_password` cookie and `middleware.ts`.
   * This code covers the case that cookie cannot: an admin resetting the password of somebody already signed
   * in. Before it, every call 403'd with the middleware's own **English** sentence, which `lib/errors.ts` hands
   * to the toast verbatim. Emitted by `LocalAuthEnforcementMiddleware`.
   */
  MustChangePassword: 'must_change_password',
  /**
   * This administrator must enrol a second factor before the API will serve them anything else
   * (`hosted-security-hardening` FR-1.2). Emitted by `ClinicAuthRefusals.TotpEnrolmentRequired`, on the login
   * ladder **and** per-request by `LocalAuthEnforcementMiddleware` — the second is why the client half below
   * exists at all: a session predating the requirement gets it on an ordinary call, not at sign-in.
   */
  TotpEnrolmentRequired: 'totp_enrolment_required',
  /**
   * The cabinet's entitlement has ended, so **writes** are refused with 402 until it is renewed
   * (`clinic-subscription` AC-4.4). Every read, every CSV export and every PDF keep working — the gate never
   * inspects a GET — so this is never a reason to take the screen.
   *
   * ⚠️ **It must never sign the user out** (AC-4.5). Nothing here touches {@link handleRequest}'s one-shot 401
   * retry: the account is fine, the session is fine, and only *recording new work* is refused.
   *
   * ⚠️ The server's own French sentence names the end date and points at « Abonnement », so — unlike
   * {@link MustChangePassword} — this code does **not** replace the message. It only tells
   * {@link onSubscriptionRequired} to re-read the subscription, which is what raises the banner with no reload
   * (EC-1). Emitted by `SubscriptionRefusals.RequiredCode`.
   */
  SubscriptionRequired: 'subscription_required',
  /**
   * The vendor has suspended this cabinet. Same 402 handling as {@link SubscriptionRequired} and deliberately a
   * **distinct** code: a suspension is not fixed by paying, so the server's sentence carries no date and does not
   * say « expiré ». Emitted by `SubscriptionRefusals.SuspendedCode`.
   */
  SubscriptionSuspended: 'subscription_suspended',
  /**
   * The cabinet has no entitlement row at all — our fault, not a lapse on theirs (EC-6), so the server's sentence
   * asks them to contact us rather than to renew. Emitted by `SubscriptionRefusals.MissingCode`.
   */
  SubscriptionMissing: 'subscription_missing',
} as const;

/** The three 402 codes, as one set — see {@link onSubscriptionRequired}. */
const SUBSCRIPTION_CODES: ReadonlySet<string> = new Set([
  ApiErrorCode.SubscriptionRequired,
  ApiErrorCode.SubscriptionSuspended,
  ApiErrorCode.SubscriptionMissing,
]);

/** Whether the server has refused this client as too old at any point this session. See {@link onClientTooOld}. */
let clientRefusedAsTooOld = false;
type ClientTooOldListener = () => void;
const clientTooOldListeners = new Set<ClientTooOldListener>();

/**
 * Subscribe to « the server has refused this client as too old » (426). Returns an unsubscribe function.
 *
 * The listener fires for calls made through this module's helpers, which is now **every** call to the clinic API:
 * the blob and upload sites that used to keep their own response handling (and so never notified) were moved onto
 * `apiGetBlob` / `apiGetFile` / `apiPostBlob`. {@link isClientRefusedAsTooOld} exists for the same reason in the
 * other direction — a gate that mounts *after* the first refusal must still know.
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

type MustChangePasswordListener = () => void;
const mustChangePasswordListeners = new Set<MustChangePasswordListener>();

/**
 * Subscribe to « this account must change its password first » (403 + {@link ApiErrorCode.MustChangePassword}).
 * Returns an unsubscribe function.
 *
 * Same shape as {@link onClientTooOld} and for the same reason: the data layer reports the refusal, and the one
 * component that owns the session decides where the user goes. `LocalSessionProvider` is the subscriber.
 */
export function onMustChangePassword(listener: MustChangePasswordListener): () => void {
  mustChangePasswordListeners.add(listener);
  return () => {
    mustChangePasswordListeners.delete(listener);
  };
}

type SecondFactorRequiredListener = () => void;
const secondFactorRequiredListeners = new Set<SecondFactorRequiredListener>();

/**
 * Subscribe to « this administrator must enrol a second factor first » (403 +
 * {@link ApiErrorCode.TotpEnrolmentRequired}). Returns an unsubscribe function.
 *
 * <p>Same shape as {@link onMustChangePassword}, and the same necessity. The refusal can arrive on <b>any</b>
 * call, because the requirement is re-checked per request — so a user whose session predates it, or who was
 * promoted to administrator while signed in, meets it in the middle of ordinary work. Without a destination
 * the app looks perfectly usable and every request fails, which is the worst version of this.</p>
 *
 * <p>⚠️ Two consequences worth writing down. The login screen's <b>enrol</b> mode has to be reachable while
 * holding a session the API refuses — it is, because `/login` is public in `middleware.ts` and the enrolment
 * endpoint is anonymous. And this makes the module's **second** message replacement, where its own docs said
 * there was exactly one: the server's sentence is already French and correct, so the message travels verbatim
 * and only the <i>destination</i> is added.</p>
 */
export function onSecondFactorRequired(listener: SecondFactorRequiredListener): () => void {
  secondFactorRequiredListeners.add(listener);
  return () => {
    secondFactorRequiredListeners.delete(listener);
  };
}

type SubscriptionRequiredListener = () => void;
const subscriptionRequiredListeners = new Set<SubscriptionRequiredListener>();

/**
 * Subscribe to « this cabinet may not record new work » (402 + one of the three subscription codes). Returns an
 * unsubscribe function.
 *
 * <p>Same shape as {@link onClientTooOld} and {@link onMustChangePassword}: the data layer reports the refusal,
 * and the one component that owns the state decides what happens. `SubscriptionProvider` is the subscriber, and
 * what it does is **re-read the subscription** — FR-15's third trigger, and the only thing that raises the banner
 * for a cabinet whose entitlement ended at midnight while a fiche was open (EC-1). Nothing pushes that: midnight
 * has no actor to broadcast from, so the refused save is the event.</p>
 *
 * <p>⚠️ Unlike the two above it, this listener changes <b>nothing</b> about the failing call. The error still
 * travels on to the caller carrying the server's own French sentence, the caller still shows it with
 * `showErrorToast`, and the form stays open with everything typed still in it (AC-4.6).</p>
 */
export function onSubscriptionRequired(listener: SubscriptionRequiredListener): () => void {
  subscriptionRequiredListeners.add(listener);
  return () => {
    subscriptionRequiredListeners.delete(listener);
  };
}

/**
 * What the user is told while being sent to the change-password screen.
 *
 * ⚠️ It **replaces** the body's own `error`, which is the one place this module overrides a server message. The
 * backend sends « You must change your password before continuing. » — English, to a French-speaking dentist —
 * and the machine-readable `code` is exactly what lets us substitute it without matching prose.
 */
const MUST_CHANGE_PASSWORD_MESSAGE_FR =
  'Vous devez changer votre mot de passe avant de continuer.';

/**
 * What the user is told when the request never reached the server (AC-43).
 *
 * ⚠️ This replaced *"Network error: Unable to connect to the API. Please check if the API is running and CORS
 * is configured correctly."* — English, and addressed to whoever deployed the app rather than to the dentist
 * reading it. `lib/errors.ts` passes an `ApiError` message through **verbatim**, so that string was the one
 * the toast actually showed. The wording matches `connectivity.tsx`'s « Serveur injoignable » banner, so the
 * two ways the app can notice the same outage do not describe it differently.
 *
 * ⚠️ It no longer names the **local network** (AC-64). This is the string *every* failed call surfaces, and the
 * same server is reached over a LAN, over Wi-Fi and over a mobile network — telling a dentist on cellular to
 * check their local network sends them to look at something that is not there.
 */
export const NETWORK_ERROR_MESSAGE =
  "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez.";

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

/**
 * Earliest moment another token exchange may be attempted, and the consecutive-429 count that sets it.
 *
 * ⚠️ **Why this exists: without it a 429 is self-sustaining.** Every failed exchange used to drop the cache and
 * let the very next API call open another one — so the moment the server said « trop de requêtes » the app
 * answered by asking again immediately, several times a second, for as long as the page stayed open. That is not
 * a hypothetical: it was observed on the hosted deployment, where the BFF calls the API server-side so *every*
 * user's refresh arrives from one address and shares one rate-limit bucket. The loop then produced a second
 * symptom that hid the first — no token means every call 401s, which reads as « je suis déconnecté » rather than
 * as « j'ai été limité ».
 *
 * A 429 is the one refusal that says **ask later**, not **you are not allowed**: honouring it is how the bucket
 * is allowed to refill. Exponential, capped, and cleared by the first success.
 */
let tokenBackoffUntilMs = 0;
let consecutiveTokenRateLimits = 0;

/** First backoff after a 429. Doubles per consecutive refusal, up to {@link TOKEN_BACKOFF_MAX_MS}. */
const TOKEN_BACKOFF_BASE_MS = 2_000;
const TOKEN_BACKOFF_MAX_MS = 30_000;

/** Milliseconds until another exchange may be attempted; `0` when none is owed. */
export function getTokenBackoffRemainingMs(): number {
  return Math.max(0, tokenBackoffUntilMs - Date.now());
}

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
  // The safety net for a 402 whose body is missing or unparseable (refused by an intermediary). ⚠️ It names no date
  // — inventing one would contradict the gate's own sentence — and, since this build, promises no banner either:
  // the re-read dispatches on the CODE (deliberately, so a 402 from anything but our own gate cannot trigger one),
  // and a bodyless 402 carries none. Telling the user to look at a strip that will not appear is worse than saying
  // less. The screen is still named, because it is where the state can be read on demand.
  402: "Cette action a été refusée. Vérifiez l'état de votre abonnement dans « Abonnement ».",
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

/**
 * Throws for a non-OK response, and returns quietly for an OK one.
 *
 * Split out of `handleResponse` so a caller that reads the body differently — `apiGetBlob`, whose success path is
 * `response.blob()` rather than JSON — gets the identical refusal handling instead of its own copy. Four raw
 * `fetch` sites in `patient-files.ts` each hand-wrote that copy and each read `errorData.message`, while the
 * backend's canonical body is `{ error }`: every French refusal reason was dropped and the user saw
 * « HTTP 400: Bad Request ». A second place that interprets the error contract is the whole defect.
 */
async function throwIfNotOk(response: Response): Promise<void> {
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

    // The refusal that changes the app's *state* rather than its screen: re-read the subscription so the banner
    // appears without a reload (EC-1). Keyed on the code, not on the status, so a 402 from anything but our own
    // gate cannot trigger a re-read — and the message travels on untouched.
    if (errorCode && SUBSCRIPTION_CODES.has(errorCode)) {
      subscriptionRequiredListeners.forEach((listener) => listener());
    }

    // The one refusal every screen can act on, in exactly one way: go and change the password.
    if (errorCode === ApiErrorCode.MustChangePassword) {
      errorMessage = MUST_CHANGE_PASSWORD_MESSAGE_FR;
      mustChangePasswordListeners.forEach((listener) => listener());
    }

    // Its sibling one requirement along. ⚠️ The message is NOT replaced: the server's sentence is French and
    // already names the way out (« depuis l'écran de connexion »), so only the destination is added.
    if (errorCode === ApiErrorCode.TotpEnrolmentRequired) {
      secondFactorRequiredListeners.forEach((listener) => listener());
    }

    throw new ApiError(response.status, errorMessage, undefined, errorCode);
  }
}

async function handleResponse<T>(response: Response): Promise<T> {
  await throwIfNotOk(response);

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.includes('application/json')) {
    return response.json();
  }
  return response.text() as unknown as T;
}

/** The download counterpart of `handleResponse`: the same refusal handling, a blob body. */
async function readBlob(response: Response): Promise<Blob> {
  await throwIfNotOk(response);
  return response.blob();
}

/** A download whose name the server chose. `filename` is `null` when it did not say. */
export interface DownloadedFile {
  blob: Blob;
  filename: string | null;
}

async function readDownloadedFile(response: Response): Promise<DownloadedFile> {
  await throwIfNotOk(response);
  return {
    blob: await response.blob(),
    filename: filenameFromDisposition(response.headers.get('content-disposition')),
  };
}

/**
 * Reads the filename out of `Content-Disposition`, preferring the RFC 5987 `filename*` form — the one that
 * survives the accents every French filename in this product could carry.
 *
 * Here rather than in `export.ts` because the CSV exports are no longer the only download whose name the server
 * owns, and a second parser would be a second answer to « what is this file called ».
 */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) return null;

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      // A malformed value must not lose the download — fall through to the plain form.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : null;
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
 *
 * `readBody` exists so a blob download gets the retry, the deadline mapping and the `{ error }` interpretation
 * without a second implementation of any of them.
 */
async function handleRequest<T>(
  accessToken: string | null | undefined,
  requestFn: (token: string | null) => Promise<Response>,
  readBody: (response: Response) => Promise<T> = handleResponse,
): Promise<T> {
  const explicit = accessToken !== undefined;
  let token = explicit ? accessToken! : await getAccessToken();

  for (let attempt = 0; ; attempt++) {
    try {
      return await readBody(await requestFn(token));
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
      // A fired deadline is the same event as an unreachable server, one layer down: the transport stopped
      // answering. Reported identically so the user gets the retryable French state rather than a raw
      // DOMException — and, crucially, so the caller's promise SETTLES and the submit button releases.
      if (err instanceof DOMException && (err.name === 'TimeoutError' || err.name === 'AbortError')) {
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

  // ⚠️ The backoff is honoured by a FORCED renewal too, and that is the point rather than an oversight: a forced
  // renewal is what the one-shot 401 retry asks for, so exempting it would rebuild the very loop this prevents —
  // 429 → no token → 401 → force → 429. Returning null here costs that one request, which was going to fail
  // anyway, and lets the bucket refill instead of holding it empty.
  if (getTokenBackoffRemainingMs() > 0) {
    return null;
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
  // A deliberate session change is a new situation, so it does not inherit the old one's pause — otherwise
  // signing back in after a rate-limited spell leaves the app inert for up to 30 s with nothing explaining it.
  // Safe to reset here because this is only reached on an explicit sign-out, and the login endpoint carries its
  // own per-account limiter.
  tokenBackoffUntilMs = 0;
  consecutiveTokenRateLimits = 0;
}

async function fetchAccessToken(): Promise<string | null> {
  try {
    const response = await fetch('/bff/auth/token', {
      credentials: 'include', // Include cookies for session
      // A hung token exchange blocks EVERY call behind it, so this deadline is the one that matters most.
      signal: deadline(REQUEST_TIMEOUT_MS),
    });
    if (response.ok) {
      const data = await response.json();
      const token: string | null = data.accessToken || null;
      lastTokenFailureStatus = token ? null : response.status;
      if (token) {
        // The bucket has refilled — start from zero, so one bad minute does not lengthen every later pause.
        consecutiveTokenRateLimits = 0;
        tokenBackoffUntilMs = 0;
      }
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
    lastTokenFailureStatus = response.status;

    if (response.status === 429) {
      // ⚠️ The cache is deliberately LEFT ALONE here, unlike every other refusal below. A 429 is not the server
      // withdrawing the token it issued — it is declining to issue another one yet — so a still-valid cached
      // token stays perfectly good and dropping it would force an exchange the server has just asked us not to
      // make. `Retry-After` wins when the server sends one, because it knows its own window.
      const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
      const advisedMs = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter * 1_000 : 0;
      const backoffMs =
        advisedMs ||
        Math.min(TOKEN_BACKOFF_BASE_MS * 2 ** consecutiveTokenRateLimits, TOKEN_BACKOFF_MAX_MS);

      consecutiveTokenRateLimits += 1;
      tokenBackoffUntilMs = Date.now() + backoffMs;
      return null;
    }

    // A refusal invalidates the cache: never serve a token the server has stopped standing behind.
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
/**
 * Every request gets a deadline, because on a phone a connection does not fail — it hangs.
 *
 * ⚠️ **Found on a physical device: the « Enregistrer » button froze permanently.** With no timeout, a `fetch`
 * whose transport dies mid-flight never settles: the promise stays pending, so the caller's `finally` never
 * runs, the button stays disabled, no toast appears and no retry is possible. The user's only way out is to
 * kill the app — and on a form that is a patient's record typed twice. A browser on a LAN almost never shows
 * this; a phone changing cell, losing Wi-Fi or crossing a dead spot shows it routinely, which is exactly the
 * device this product is being taken onto.
 *
 * A timeout that fires is reported as the **network** error, not a new kind: it is indistinguishable to the
 * user from the server being unreachable, it is retryable, and `errors.ts` already words that case.
 */
const REQUEST_TIMEOUT_MS = 20_000

/**
 * Moving a **file** in either direction gets much longer. A panoramique over a slow uplink legitimately takes
 * minutes, and killing a transfer that is still making progress would be a worse failure than the one this guards
 * against.
 *
 * ⚠️ Downloads share it, not `REQUEST_TIMEOUT_MS`, and the difference is not cosmetic: a CBCT study or a
 * 3 000-row CSV export cannot finish inside 20 s on a clinic's uplink, so the tighter deadline would trade
 * « hangs for ever » for « always fails », which is the worse of the two.
 */
const TRANSFER_TIMEOUT_MS = 180_000

/**
 * The cabinet **archive**, in either direction, gets its own deadline again — and this one is not about the
 * uplink.
 *
 * ⚠️ A restore's own confirmation says « L'opération peut durer plusieurs minutes sur un cabinet complet », so
 * the UI was explicitly warning that the operation outlasts the deadline guarding it. Past three minutes the
 * client aborted while the server carried on committing table after table: the user got the *network* wording —
 * indistinguishable from « serveur injoignable » — the per-entity report was lost, and they had no way to tell
 * whether anything had been written. The download has the same shape, since the whole archive is built before a
 * single byte is sent.
 *
 * `TRANSFER_TIMEOUT_MS` was itself split out of `REQUEST_TIMEOUT_MS` for exactly this reason; this is the same
 * split one step further, rather than raising the ceiling for every upload in the product.
 */
export const ARCHIVE_TIMEOUT_MS = 900_000

/** `undefined` where the runtime lacks it, so an old renderer loses the deadline rather than every request. */
function deadline(ms: number): AbortSignal | undefined {
  return typeof AbortSignal !== 'undefined' && typeof AbortSignal.timeout === 'function'
    ? AbortSignal.timeout(ms)
    : undefined
}

/**
 * The header a step-up confirmation travels in (`hosted-security-hardening` FR-4.3).
 *
 * ⚠️ A header and **not** a query parameter: this app's URLs are logged, and FR-4.4 is about exactly that.
 */
export const STEP_UP_HEADER = 'X-Step-Up-Confirmation'

export function apiHeaders(
  accessToken?: string | null,
  contentType: ApiContentType = 'json',
  stepUpToken?: string | null,
): HeadersInit {
  const headers: Record<string, string> = {};

  if (contentType === 'json') {
    headers['Content-Type'] = 'application/json';
  }
  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`;
  }
  if (stepUpToken) {
    headers[STEP_UP_HEADER] = stepUpToken;
  }

  const shellVersion = typeof window !== 'undefined' ? window.__clinicShell?.version : undefined;
  if (shellVersion) {
    headers[CLIENT_VERSION_HEADER] = shellVersion;
  }

  return headers;
}

function buildUrl(endpoint: string, params?: Record<string, any>): string {
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
  return url.toString();
}

export async function apiGet<T>(endpoint: string, params?: Record<string, any>, accessToken?: string | null): Promise<T> {
  const url = buildUrl(endpoint, params);

  return handleRequest<T>(accessToken, (token) => fetch(url, {
    method: 'GET',
    headers: apiHeaders(token),
    credentials: 'include',
    signal: deadline(REQUEST_TIMEOUT_MS),
  }));
}

/**
 * A GET whose body is a file, not JSON — patient files, invoice PDFs, a practitioner's cachet.
 *
 * Exists because the download sites were raw `fetch`: they lost the deadline (so a dead transport left the
 * caller's promise pending for ever), the one-shot 401 retry (and a download is user-initiated after a period of
 * reading, i.e. the request most likely to be the first past a token's expiry), and — since they read
 * `errorData.message` — every French refusal the server sent.
 *
 * `'none'` on the headers because there is no request body to describe.
 */
export async function apiGetBlob(endpoint: string, params?: Record<string, any>, accessToken?: string | null): Promise<Blob> {
  const url = buildUrl(endpoint, params);

  return handleRequest<Blob>(accessToken, (token) => fetch(url, {
    method: 'GET',
    headers: apiHeaders(token, 'none'),
    credentials: 'include',
    signal: deadline(TRANSFER_TIMEOUT_MS),
  }), readBlob);
}

/**
 * ⚠️ `stepUpToken` exists for the same reason it does on `apiGetFile` and `apiPostFormData`: a step-up confirmation
 * travels in a **header** and never in the body or the query string, because this app's URLs are logged (FR-4.4).
 * A step-up confirmation is also **single-use**, which survives `handleRequest`'s one-shot 401 retry only because
 * that retry re-sends the *same* request rather than re-authenticating — a 403 is never retried.
 */
export async function apiPost<T>(
  endpoint: string, data: any, accessToken?: string | null, stepUpToken?: string | null
): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: apiHeaders(token, 'json', stepUpToken),
    body: JSON.stringify(data),
    credentials: 'include',
    signal: deadline(REQUEST_TIMEOUT_MS),
  }));
}

export async function apiPut<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: apiHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
    signal: deadline(REQUEST_TIMEOUT_MS),
  }));
}

export async function apiDelete<T>(endpoint: string, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
    headers: apiHeaders(token),
    credentials: 'include',
    signal: deadline(REQUEST_TIMEOUT_MS),
  }));
}

/**
 * `apiGetBlob` for a download whose filename the server dictates — the CSV exports, and the cabinet archive.
 *
 * `timeoutMs` exists for that last one: the archive is built in full before a byte is sent, so its deadline is a
 * property of the *operation*, not of the transfer.
 */
export async function apiGetFile(endpoint: string, params?: Record<string, any>, accessToken?: string | null, timeoutMs: number = TRANSFER_TIMEOUT_MS, stepUpToken?: string | null): Promise<DownloadedFile> {
  const url = buildUrl(endpoint, params);

  return handleRequest<DownloadedFile>(accessToken, (token) => fetch(url, {
    method: 'GET',
    headers: apiHeaders(token, 'none', stepUpToken),
    credentials: 'include',
    signal: deadline(timeoutMs),
  }), readDownloadedFile);
}

/**
 * A POST whose response is a file — the one download in the app that has to send a body.
 *
 * `medical-documents`' inline PDF render takes the whole document as its request, so it cannot be a GET: the
 * server re-resolves the practitioner snapshot from what is posted. Everything else about it is a download, so it
 * wants `readBlob`, not `handleResponse`.
 */
export async function apiPostBlob(endpoint: string, data: any, accessToken?: string | null): Promise<Blob> {
  return handleRequest<Blob>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: apiHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
    signal: deadline(TRANSFER_TIMEOUT_MS),
  }), readBlob);
}

export async function apiPostFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null, timeoutMs: number = TRANSFER_TIMEOUT_MS, stepUpToken?: string | null): Promise<T> {
  // Headers are built INSIDE the callback so a 401 retry rebuilds them with the renewed token. Uploads are
  // exactly where a stale token bites — they are user-initiated after a period of reading, so they are the
  // most likely request to be the first one past the access token's expiry.
  //
  // ⚠️ A step-up confirmation is SINGLE-USE, so it survives that retry only because the retry re-sends the same
  // request rather than re-authenticating: a 401 refresh does not spend the confirmation, and a 403 is not
  // retried at all.
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: apiHeaders(token, 'none', stepUpToken),
    body: formData,
    credentials: 'include',
    signal: deadline(timeoutMs),
  }));
}

export async function apiPutFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: apiHeaders(token, 'none'),
    body: formData,
    credentials: 'include',
    signal: deadline(TRANSFER_TIMEOUT_MS),
  }));
}



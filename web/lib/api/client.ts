const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public originalError?: unknown
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/** French text for statuses that can reach the client with an empty body. */
const STATUS_FALLBACK_FR: Record<number, string> = {
  401: "Votre session a expiré. Reconnectez-vous.",
  403: "Vous n'avez pas les droits nécessaires pour cette action.",
  404: "Élément introuvable.",
  409: "Cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie. "
    + "Rechargez pour voir la version à jour, puis appliquez à nouveau votre modification.",
  500: "Une erreur est survenue lors du traitement de votre demande.",
};

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
    try {
      const errorData = await response.json();
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
        if (errorData.error || errorData.title || errorData.message) {
          errorMessage = errorData.error || errorData.title || errorData.message;
        }
        if (errorData.errors) {
          const validationErrors = Object.entries(errorData.errors)
            .map(([key, value]) => `${key}: ${Array.isArray(value) ? value.join(', ') : value}`)
            .join('; ');
          errorMessage = `${errorMessage} - ${validationErrors}`;
        }
      }
    } catch {
      // If response is not JSON, use status text
    }

    // Rate-limit refusals carry a French `{ error }` body from the API, so the branch above normally
    // surfaces it. This is the safety net for a 429 whose body is missing or unparseable (e.g. refused by
    // an intermediary): "HTTP 429: Too Many Requests" is not something to show a clinic
    // (security-hardening AC-4.5).
    if (response.status === 429 && errorMessage.startsWith('HTTP 429')) {
      const retryAfter = Number(response.headers.get('retry-after'));
      errorMessage = Number.isFinite(retryAfter) && retryAfter > 0
        ? `Trop de tentatives. Veuillez réessayer dans ${Math.ceil(retryAfter / 60)} minute(s).`
        : 'Trop de tentatives. Veuillez réessayer dans quelques minutes.';
    }

    // Some statuses arrive with no body at all — most importantly the 403 that ASP.NET's authorization
    // pipeline returns before any handler runs, which short-circuits the `{ error }` contract. Falling back
    // to the raw status line put « HTTP 403: Forbidden » in front of a French-speaking dentist.
    if (errorMessage === `HTTP ${response.status}: ${response.statusText}`) {
      const french = STATUS_FALLBACK_FR[response.status];
      if (french) errorMessage = french;    }

    throw new ApiError(response.status, errorMessage);
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
        token = await getAccessToken();
        continue;
      }

      if (err instanceof TypeError && err.message.includes('fetch')) {
        throw new ApiError(0, 'Network error: Unable to connect to the API. Please check if the API is running and CORS is configured correctly.', err);
      }
      if (err instanceof ApiError) {
        throw err;
      }
      throw new ApiError(0, err instanceof Error ? err.message : 'An unexpected error occurred', err);
    }
  }
}

/**
 * The single place the app acquires an API access token (mode-aware: an Auth0 token in Cloud, the local
 * JWT in Local).
 *
 * **Exported deliberately, and this must stay the only implementation.** Seven per-resource modules used to
 * carry their own private copy of this fetch. That was harmless while the token lived 12 hours, but it
 * becomes a real defect once tokens are short-lived and renewed: any copy that bypasses this helper keeps
 * using an expired token and fails silently, surfacing to the user as a random unexplained error
 * (security-hardening plan risk R-4). Renewal logic can only live in one place if acquisition does.
 *
 * If you need a token in a new module, import this — do not re-implement the fetch.
 */
export async function getAccessToken(): Promise<string | null> {
  try {
    const response = await fetch('/bff/auth/token', {
      credentials: 'include', // Include cookies for session
    });
    if (response.ok) {
      const data = await response.json();
      return data.accessToken || null;
    }
  } catch {
    // Token endpoint not available or error
  }
  return null;
}


// Auth-only headers for multipart uploads: Content-Type must be left unset so the browser adds the boundary.
function formDataHeaders(accessToken: string | null): HeadersInit {
  return accessToken ? { Authorization: `Bearer ${accessToken}` } : {};
}

// Create headers with optional auth token
function createHeaders(accessToken?: string | null): HeadersInit {
  const headers: HeadersInit = {
    'Content-Type': 'application/json',
  };
  
  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`;
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
    headers: createHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPost<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: createHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiPut<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: createHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiDelete<T>(endpoint: string, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
    headers: createHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPostFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  // Headers are built INSIDE the callback so a 401 retry rebuilds them with the renewed token. Uploads are
  // exactly where a stale token bites — they are user-initiated after a period of reading, so they are the
  // most likely request to be the first one past the access token's expiry.
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: formDataHeaders(token),
    body: formData,
    credentials: 'include',
  }));
}

export async function apiPutFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  return handleRequest<T>(accessToken, (token) => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: formDataHeaders(token),
    body: formData,
    credentials: 'include',
  }));
}



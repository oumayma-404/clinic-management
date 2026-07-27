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

    throw new ApiError(response.status, errorMessage);
  }

  const contentType = response.headers.get('content-type');
  if (contentType && contentType.includes('application/json')) {
    return response.json();
  }
  return response.text() as unknown as T;
}

async function handleRequest<T>(requestFn: () => Promise<Response>): Promise<T> {
  try {
    const response = await requestFn();
    return handleResponse<T>(response);
  } catch (err) {
    if (err instanceof TypeError && err.message.includes('fetch')) {
      throw new ApiError(0, 'Network error: Unable to connect to the API. Please check if the API is running and CORS is configured correctly.', err);
    }
    if (err instanceof ApiError) {
      throw err;
    }
    throw new ApiError(0, err instanceof Error ? err.message : 'An unexpected error occurred', err);
  }
}

// Get Auth0 access token from client-side
async function getAccessToken(): Promise<string | null> {
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

  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  return handleRequest<T>(() => fetch(url.toString(), {
    method: 'GET',
    headers: createHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPost<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: createHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiPut<T>(endpoint: string, data: any, accessToken?: string | null): Promise<T> {
  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: createHeaders(token),
    body: JSON.stringify(data),
    credentials: 'include',
  }));
}

export async function apiDelete<T>(endpoint: string, accessToken?: string | null): Promise<T> {
  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
    headers: createHeaders(token),
    credentials: 'include',
  }));
}

export async function apiPostFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  const headers: HeadersInit = {};
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  // Don't set Content-Type for FormData, browser will set it with boundary

  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers,
    body: formData,
    credentials: 'include',
  }));
}

export async function apiPutFormData<T>(endpoint: string, formData: FormData, accessToken?: string | null): Promise<T> {
  // If no token provided, try to get it automatically
  const token = accessToken !== undefined ? accessToken : await getAccessToken();

  const headers: HeadersInit = {};
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  // Don't set Content-Type for FormData, browser will set it with boundary

  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers,
    body: formData,
    credentials: 'include',
  }));
}



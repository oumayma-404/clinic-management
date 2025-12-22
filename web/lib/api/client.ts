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
      if (errorData.title || errorData.message) {
        errorMessage = errorData.title || errorData.message;
      }
      if (errorData.errors) {
        const validationErrors = Object.entries(errorData.errors)
          .map(([key, value]) => `${key}: ${Array.isArray(value) ? value.join(', ') : value}`)
          .join('; ');
        errorMessage = `${errorMessage} - ${validationErrors}`;
      }
    } catch {
      // If response is not JSON, use status text
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

export async function apiGet<T>(endpoint: string, params?: Record<string, any>): Promise<T> {
  const url = new URL(`${API_BASE_URL}${endpoint}`);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        url.searchParams.append(key, String(value));
      }
    });
  }

  return handleRequest<T>(() => fetch(url.toString(), {
    method: 'GET',
    headers: {
      'Content-Type': 'application/json',
    },
  }));
}

export async function apiPost<T>(endpoint: string, data: any): Promise<T> {
  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(data),
  }));
}

export async function apiPut<T>(endpoint: string, data: any): Promise<T> {
  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(data),
  }));
}

export async function apiDelete<T>(endpoint: string): Promise<T> {
  return handleRequest<T>(() => fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'DELETE',
    headers: {
      'Content-Type': 'application/json',
    },
  }));
}


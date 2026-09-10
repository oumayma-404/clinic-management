// Server-only: the single place a BFF route turns the session cookie's durable credential into a
// short-lived API access token.
//
// It was inline in `/bff/auth/token`, which was fine while that route was the only server-side caller of the
// API. `/bff/pdf/…` is the second — it streams a rendered PDF so the browser's own viewer has a URL with a
// name — and a second copy of the exchange is a second place to get the refresh contract, the forwarded
// address or the failure mapping wrong. The token route keeps every one of its own branches (it also rotates
// the cookie); this only owns the round trip and how its outcome is read.

import type { NextRequest } from 'next/server';
import { forwardedForHeader } from './forwarded-for';

/**
 * Server-side handlers must reach the .NET API with an ABSOLUTE URL: the browser-facing
 * `NEXT_PUBLIC_API_URL` is the relative `/api` behind the same-origin front door and has no origin here.
 */
export const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

/**
 * What the refresh endpoint answered.
 *
 * ⚠️ `status` is the API's own, and callers must keep the distinction it carries: only a **401** means the
 * credential itself is dead. A 429 or a 5xx is a refusal to answer *right now* and must leave the session
 * alone — flattening them all to 401 is what once turned a rate-limit blip into a destroyed session and an
 * endless bounce to a login page that was itself rate-limited.
 */
export interface RefreshExchange {
  /** Set only when the API returned a usable access token. */
  accessToken: string | null;
  status: number;
  /** The parsed body, or null when there was none / it was not JSON. */
  data: {
    isSuccess?: boolean;
    error?: string;
    value?: {
      accessToken?: string;
      expiresAt?: string;
      refreshToken?: string;
      refreshExpiresAt?: string;
      mustChangePassword?: boolean;
    };
  } | null;
  /** True when the API could not be reached at all — distinct from any refusal it made. */
  unreachable: boolean;
  /** Passed through so a caller can honour the API's own back-off. */
  retryAfter: string | null;
}

/** Exchanges the cookie's refresh token for an access token. Never throws. */
export async function exchangeRefreshToken(
  credential: string,
  request: NextRequest,
): Promise<RefreshExchange> {
  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/refresh`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...forwardedForHeader(request),
      },
      body: JSON.stringify({ refreshToken: credential }),
    });

    const data = await res.json().catch(() => null);
    const accessToken =
      res.ok && data?.isSuccess && data?.value?.accessToken ? data.value.accessToken : null;

    return { accessToken, status: res.status, data, unreachable: false, retryAfter: res.headers.get('retry-after') };
  } catch {
    return { accessToken: null, status: 0, data: null, unreachable: true, retryAfter: null };
  }
}

import type { NextRequest } from 'next/server';

/**
 * Passes the browser's address through to the .NET API on a server-side BFF call.
 *
 * The BFF route handlers post to the API themselves, over loopback (`API_INTERNAL_URL`). This handler also
 * runs on the Node server that sits *behind* the Kestrel front door, so its own peer address is `127.0.0.1`
 * too — `request.ip` here is the front door, never the client. Without forwarding, the API sees every login
 * as loopback and buckets the whole clinic as a single source, which turns the per-IP rate limit into the
 * clinic-wide lockout it exists to prevent (security-hardening US-4).
 *
 * So read the `X-Forwarded-For` the front door already added and pass it on **verbatim**. Do NOT substitute
 * anything derived from this request — that is the wrong leg of the chain, and it fails silently: the limiter
 * would look correct while keying every request identically.
 *
 * The API only honours this header when its immediate peer is loopback (see `ClientIp`), so a LAN client
 * cannot send one directly and impersonate another device.
 */
export function forwardedForHeader(request: NextRequest): Record<string, string> {
  const forwardedFor = request.headers.get('x-forwarded-for');
  return forwardedFor ? { 'X-Forwarded-For': forwardedFor } : {};
}

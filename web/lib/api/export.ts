import { apiHeaders, getAccessToken, ApiError } from './client';

// `client.ts` keeps its base URL private, and every module that drops to raw `fetch` for a blob repeats this
// line. Kept identical to theirs rather than exported from `client.ts`, so this file adds no coupling the
// existing blob modules do not already have.
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

/**
 * CSV export (L5) — one authenticated blob fetch for every « Exporter » in the product.
 *
 * <p><b>Why one function and not one per resource.</b> Eight lists export, and the per-resource modules already
 * drop to raw `fetch` for their blob endpoints — eight copies of the token plumbing, the relative-base resolution
 * and the error handling is how one of them ends up without `credentials: 'include'` and fails only in Local mode.
 * The <i>route</i> and the <i>filters</i> are the caller's business; everything else is the same every time.</p>
 *
 * <p>⚠️ <b>The filename comes from the server</b>, read out of `Content-Disposition`. The server already dates it
 * with the clinic's own day (`patients-2026-08-03.csv`), and re-deriving a name here would be a second authority
 * on it — including a second chance to use the browser's UTC date, which for the first hour of every Tunisian day
 * would name the file after yesterday.</p>
 */
export async function fetchExportCsv(
  path: string,
  params: Record<string, string | number | boolean | undefined | null> = {},
): Promise<{ blob: Blob; filename: string }> {
  const token = await getAccessToken();
  const headers = apiHeaders(token, 'none');

  // Resolved against the origin so a *relative* NEXT_PUBLIC_API_URL=/api (the Local same-origin front-door
  // build) parses — the same guard `client.ts` documents.
  const base = typeof window !== 'undefined' ? window.location.origin : undefined;
  const url = new URL(`${API_BASE_URL}${path}`, base);
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      url.searchParams.set(key, String(value));
    }
  }

  const response = await fetch(url.toString(), { method: 'GET', headers, credentials: 'include' });

  if (!response.ok) {
    // The canonical `{ error }` body, when there is one. A 403 here is a real answer (« money exports are
    // AdminOrDoctor »), so it must reach the user as its French message rather than as « HTTP 403 ».
    let message = `L'export a échoué (HTTP ${response.status}).`;
    try {
      const body = await response.json();
      if (body?.error) message = body.error;
    } catch {
      // Not JSON — keep the status message.
    }
    throw new ApiError(response.status, message);
  }

  return {
    blob: await response.blob(),
    filename: filenameFrom(response.headers.get('content-disposition')),
  };
}

/**
 * Reads the filename out of `Content-Disposition`, preferring the RFC 5987 `filename*` form — which is the one
 * that survives the accents every French filename in this product could carry.
 */
function filenameFrom(header: string | null): string {
  const fallback = 'export.csv';
  if (!header) return fallback;

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      // A malformed value must not lose the download — fall through to the plain form.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

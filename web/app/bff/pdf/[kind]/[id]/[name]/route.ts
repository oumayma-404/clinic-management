import { NextRequest, NextResponse } from 'next/server';
import { readSessionCookie, writeSessionCookies } from '@/lib/auth/session-cookie';
import { API_INTERNAL_URL, exchangeRefreshToken } from '@/lib/auth/api-token';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';
import { PDF_KINDS, isPdfKind } from '@/lib/pdf-sources';

/**
 * A rendered PDF, served **same-origin over HTTP** so the browser's own viewer has a real response to name it
 * from.
 *
 * <h4>Why this route exists</h4>
 * <p>Every PDF this product frames used to be painted from a `blob:` URL. Chrome renders those with its own
 * viewer, whose toolbar carries a download button we do not own — and with no response headers to read, it
 * names the save after the URL's last path segment, which for a blob is a bare UUID. Reported as
 * « download from the pdf tool, downloads without an extension ».</p>
 *
 * <p>⚠️ <b>Two cheaper fixes were measured and do not work; do not re-try them.</b> Creating the object URL
 * from a named `File` rather than a `Blob` produces the identical `blob:&lt;origin&gt;/&lt;uuid&gt;` and the
 * name reaches nothing (measured 2026-09-10, Chrome). And `#toolbar=0` would remove the mis-naming button
 * along with zoom, page navigation and rotate — a capability removed by a naming decision
 * (`frontend-web.md` § 0) — besides being Chromium-only and rendering the frame <b>blank</b> in an Android
 * WebView, which is why `patient-file-pdf-preview.tsx` took it out.</p>
 *
 * <h4>⚠️ It is a CLOSED proxy, and that is the whole of its safety</h4>
 * <p>`kind` is looked up in {@link PDF_KINDS}, a literal map, and `id` must parse as a GUID — so the only
 * upstream paths reachable are the three this product renders itself. A `?src=` parameter, or an unvalidated
 * id interpolated into the API path, would be an open proxy carrying the caller's own credentials.</p>
 *
 * <h4>⚠️ Only PDFs WE RENDER are served inline, and patient files are deliberately not here</h4>
 * <p>These three are composed by our own PDF renderer from our own rows: there is no uploaded byte in them,
 * so rendering one inline in the app's origin introduces nothing. A **patient file** is the opposite — it is
 * whatever somebody uploaded — and `PatientFilesController` serves it `attachment` + `nosniff` on purpose
 * (AC-11.6) so nothing renders in this origin. Flipping that to `inline` to tidy a filename would trade a
 * security control for a cosmetic one. Patient files keep the blob path.</p>
 *
 * <h4>⚠️ The name is in the PATH, and `Content-Disposition` is NOT what does it</h4>
 * <p>This was measured, because it is the obvious thing to get wrong. Served with
 * `Content-Disposition: inline; filename=note-d-honoraires-karim-hamdi.pdf`, Chrome's viewer still titled the
 * document <b>`1ccc6c76-dd60-436c-99d6-f7800baf7e5d`</b> — the id from the URL. The viewer reads the
 * <b>last path segment</b> and ignores the header, which is exactly why a blob URL yields a UUID: it is not
 * that a blob has no headers, it is that the viewer was never going to read them.</p>
 *
 * <p>So the trailing `[name]` segment is the whole fix, and it is <b>load-bearing rather than decorative</b>:
 * remove it and this route is an elaborate way to serve the same badly-named document. The header is kept
 * anyway — `inline` is what stops the frame downloading instead of rendering, and the filename in it is what
 * every non-Chromium viewer and every `curl -OJ` will use.</p>
 *
 * <p>⚠️ The segment is <b>ignored for the lookup</b>: the `id` is authoritative and the name is cosmetic,
 * so a stale or hand-edited name cannot reach another cabinet's document. It is not validated for the same
 * reason — there is nothing to validate it against.</p>
 */
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function GET(
  request: NextRequest,
  // `name` is deliberately unread — see the note above on why it exists and why it is not validated.
  { params }: { params: Promise<{ kind: string; id: string; name: string }> },
) {
  const { kind, id } = await params;

  if (!isPdfKind(kind) || !GUID.test(id)) {
    return NextResponse.json({ error: 'Document introuvable.' }, { status: 404 });
  }

  const sessionCredential = readSessionCookie((name) => request.cookies.get(name)?.value);
  if (!sessionCredential) {
    return NextResponse.json({ error: 'Session absente. Reconnectez-vous.' }, { status: 401 });
  }

  const exchange = await exchangeRefreshToken(sessionCredential, request);
  if (exchange.unreachable) {
    return NextResponse.json({ error: 'Serveur injoignable. Veuillez réessayer.' }, { status: 503 });
  }
  if (!exchange.accessToken) {
    // The credential's own status, passed through: only a 401 means « reconnectez-vous ». This route does
    // NOT clear the cookie the way `/bff/auth/token` does — that route is the session's owner and runs on
    // every request, so a frame failing to load must not be what ends somebody's session.
    const status = exchange.status === 401 ? 401 : 503;
    return NextResponse.json(
      { error: exchange.data?.error || (status === 401 ? 'Session expirée. Veuillez vous reconnecter.' : 'Serveur indisponible. Veuillez réessayer.') },
      { status },
    );
  }

  let upstream: Response;
  try {
    upstream = await fetch(`${API_INTERNAL_URL}${PDF_KINDS[kind].path(id)}`, {
      headers: {
        Authorization: `Bearer ${exchange.accessToken}`,
        ...forwardedForHeader(request),
      },
    });
  } catch {
    return NextResponse.json({ error: 'Serveur injoignable. Veuillez réessayer.' }, { status: 503 });
  }

  if (!upstream.ok) {
    // The API answers 404 for a document this cabinet cannot see — « introuvable, not refused » is its own
    // rule, and re-wording it here would make two surfaces disagree about the same refusal.
    const body = await upstream.text().catch(() => '');
    return new NextResponse(body || JSON.stringify({ error: 'Le document n’a pas pu être rendu.' }), {
      status: upstream.status,
      headers: { 'Content-Type': upstream.headers.get('content-type') || 'application/json' },
    });
  }

  const headers = new Headers({
    'Content-Type': 'application/pdf',
    // ⚠️ `inline`, from the API's own `attachment` — the filename is forwarded verbatim, never rebuilt.
    'Content-Disposition': inlineDisposition(upstream.headers.get('content-disposition')),
    // A patient's document must not sit in a shared cache, or in the browser's disk cache on a clinic PC
    // after the user signs out. The frame re-fetches; these are tens of kilobytes.
    'Cache-Control': 'no-store, private',
    'X-Content-Type-Options': 'nosniff',
  });
  const length = upstream.headers.get('content-length');
  if (length) headers.set('Content-Length', length);

  // Streamed, never buffered: `IFileStorage.DownloadAsync` was made streaming precisely so a large document
  // is not held whole in a server's memory, and re-buffering it one hop later would give that back.
  const response = new NextResponse(upstream.body, { status: 200, headers });

  /*
   * ⚠️ **STORE THE ROTATED CREDENTIAL, or framing a PDF ends the session.** The refresh exchange above
   * is not a read: the API mints a new refresh token and retires the one presented. `/bff/auth/token` has
   * always written the new one back (« storing the freshly-minted credential is what makes the session
   * slide », AC-35); a second exchanger that does not is spending the browser's credential and handing back
   * nothing, so the *next* request 401s and the user is signed out by having looked at a document.
   *
   * Measured while building this: without these lines the very next call answered 401.
   *
   * Same writer as every other route (`session-cookie.ts`), so `Secure` cannot be decided differently here
   * and replace a stored cookie with one the browser drops.
   */
  const rotated = exchange.data?.value;
  if (rotated?.refreshToken) {
    writeSessionCookies(response, request, {
      credential: rotated.refreshToken,
      expiresAt: rotated.refreshExpiresAt,
      mustChangePassword: Boolean(rotated.mustChangePassword),
    });
  }

  return response;
}

/**
 * The upstream disposition with `attachment` turned into `inline`, keeping its filename parameters exactly as
 * the API wrote them (both `filename=` and the RFC 5987 `filename*=`, which is what carries the accents).
 *
 * With no upstream header at all this returns a bare `inline`: the viewer then falls back to the URL, which
 * is the behaviour we started from rather than a new failure.
 */
function inlineDisposition(upstream: string | null): string {
  if (!upstream) return 'inline';
  return upstream.replace(/^\s*(attachment|inline)\s*/i, 'inline');
}

/**
 * The console's API client — server-side only.
 *
 * ⚠️ **Every call goes through here and every call runs on the server**, because the session token is in an
 * HttpOnly cookie that browser JavaScript cannot read (see `lib/session.ts`). A client component that wanted to
 * call the API directly would need the token in the page, which is the one thing this arrangement exists to
 * avoid; it calls a route handler under `app/bff/` instead.
 */

/** The private address the console's own Caddy site proxies `/api/platform/*` from. */
const API_BASE = process.env.CONSOLE_API_URL ?? "http://api:5443/api";

/**
 * A refusal the API returned, carrying the canonical `{ error }` body **and its `code` where there is one**.
 *
 * The code is what the sign-in screen branches on — « enrol your factor » and « wrong password » are different
 * destinations, and recovering that from the French sentence would break the moment somebody rewords it. That
 * is the same contract `PlatformAuthRefusals` states on the server.
 */
export class ConsoleApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
  ) {
    super(message);
    this.name = "ConsoleApiError";
  }
}

const NETWORK_MESSAGE =
  "Impossible de joindre le serveur. Vérifiez que le tunnel vers la console est ouvert, puis réessayez.";

/**
 * Parses the canonical `{ error, code }` shape. ⚠️ Reads `error`, never `message` — the backend's
 * `ApiControllerBase` emits `error`, and the clinic app shipped a bug for two features by reading the other,
 * which replaced every French explanation with « HTTP 400: Bad Request ».
 */
async function refusalFrom(response: Response): Promise<ConsoleApiError> {
  let message = "";
  let code: string | undefined;

  try {
    const body = (await response.json()) as { error?: string; code?: string };
    message = body?.error ?? "";
    code = body?.code;
  } catch {
    // A body that is not JSON is not a reason to lose the status — see the fallback below.
  }

  return new ConsoleApiError(
    message || `Le serveur a refusé la requête (${response.status}).`,
    response.status,
    code,
  );
}

export async function consoleFetch<T>(
  path: string,
  init: { method?: string; body?: unknown; token?: string | null } = {},
): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: init.method ?? "GET",
      headers: {
        "Content-Type": "application/json",
        ...(init.token ? { Authorization: `Bearer ${init.token}` } : {}),
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      cache: "no-store",
    });
  } catch {
    // A connection failure is its own state, distinct from a business refusal — and it is the likeliest one
    // here, because reaching the console at all depends on a tunnel the operator opened by hand.
    throw new ConsoleApiError(NETWORK_MESSAGE, 0);
  }

  if (!response.ok) {
    throw await refusalFrom(response);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

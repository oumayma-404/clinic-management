import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Cancelling one subscription period (`platform-console` AC-5.1) — the console's second write.
 *
 * ⚠️ **It exists for `/bff/paiements`' reason**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so a write coming from a form needs a route handler on the same origin to attach it.
 *
 * ⚠️ **Its own route rather than a mode flag on `/bff/paiements`.** Recording money and striking an entry through
 * are different actions with different refusals, and one handler branching on a body field is how the second one
 * ends up inheriting the first's idempotency semantics — which here would replay a correction the vendor may have
 * meant to repeat against a different entry.
 *
 * ⚠️ **The refusal travels verbatim, `code` included.** The dialog branches on `period_already_cancelled` and shows
 * every other refusal's own French sentence — the server is the only participant that knows why.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; entryId?: string; reason?: string };

  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  const { clinicId, entryId, reason } = body;

  if (!clinicId || !entryId) {
    return NextResponse.json({ error: "Période non précisée." }, { status: 400 });
  }

  try {
    const cancelled = await consoleFetch(
      `/platform/clinics/${clinicId}/subscription-periods/${entryId}/cancellation`,
      { method: "POST", token, body: { reason } },
    );

    return NextResponse.json(cancelled);
  } catch (error) {
    if (error instanceof ConsoleApiError) {
      return NextResponse.json(
        { error: error.message, code: error.code },
        { status: error.status === 0 ? 503 : error.status },
      );
    }

    return NextResponse.json({ error: "Une erreur inattendue est survenue." }, { status: 500 });
  }
}

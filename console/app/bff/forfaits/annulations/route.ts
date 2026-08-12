import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Cancelling one forfait allocation (`vendor-whatsapp-messaging-quota` AC-7.1).
 *
 * ⚠️ **It exists for `/bff/forfaits`' reason**: the session token is in an HttpOnly cookie browser JavaScript cannot
 * read, so a write coming from a form needs a route handler on the same origin to attach it.
 *
 * ⚠️ **Its own route rather than a mode flag on `/bff/forfaits`**, the decision `/bff/annulations` already made:
 * recording an allocation and striking one through are different actions with different refusals, and one handler
 * branching on a body field is how the second inherits the first's idempotency semantics — which here would replay a
 * correction the vendor may have meant to repeat against a *different* allocation.
 *
 * ⚠️ **The refusal travels verbatim, `code` included.** The dialog branches on
 * `messaging_allowance_entry_already_cancelled` — a state of the world, not a rejected request — and shows every other
 * refusal's own French sentence.
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
    return NextResponse.json({ error: "Allocation non précisée." }, { status: 400 });
  }

  try {
    const cancelled = await consoleFetch(
      `/platform/clinics/${clinicId}/messaging-allowances/${entryId}/cancellation`,
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

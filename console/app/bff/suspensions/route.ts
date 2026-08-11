import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Suspending a cabinet, and lifting that suspension (`platform-console` US-6) — the console's third write.
 *
 * ⚠️ **It exists for `/bff/paiements`' reason**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so a write coming from a form needs a route handler on the same origin to attach it.
 *
 * ⚠️ **One route with a direction, unlike `/bff/paiements` and `/bff/annulations` which are two.** Those are
 * different actions with different refusals and different idempotency semantics — the reason they were split. These
 * two are one decision with a sign, served by one command on the server, and the API keeps them as **two routes** so
 * a truncated body cannot flip the direction. Here the direction is a required boolean and a body that omits it is
 * refused rather than defaulted, which is the same guarantee one layer up.
 *
 * ⚠️ **The refusal travels verbatim, `code` included** — `clinic_already_suspended` and `clinic_not_suspended` are
 * states of the world the dialog re-reads the fiche on, and the server is the only participant that knows which.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; suspend?: boolean; reason?: string };

  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  const { clinicId, suspend, reason } = body;

  if (!clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  // Never defaulted: « suspendre » and « lever » are opposite actions, and a missing flag is an unreadable request
  // rather than one of them.
  if (typeof suspend !== "boolean") {
    return NextResponse.json({ error: "Action non précisée." }, { status: 400 });
  }

  try {
    const changed = await consoleFetch(
      suspend
        ? `/platform/clinics/${clinicId}/suspension`
        : `/platform/clinics/${clinicId}/suspension/lifting`,
      // Lifting carries no body at all, matching the endpoint: there is nothing to say about it.
      { method: "POST", token, body: suspend ? { reason } : {} },
    );

    return NextResponse.json(changed);
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

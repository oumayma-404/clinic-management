import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Recording a cabinet's forfait de rappels WhatsApp (`vendor-whatsapp-messaging-quota` AC-6.1).
 *
 * ⚠️ **It exists because the session token is in an HttpOnly cookie browser JavaScript cannot read.** Every console read
 * happens in a server component; a write comes from a form, so it needs a route handler on the same origin to attach the
 * token — the arrangement Part 1 of `platform-console` built the whole application around.
 *
 * ⚠️ **The refusal travels verbatim, `code` included.** The sheet branches on `clinic_not_found` and on
 * `messaging_allowance_past_month` (which points at the month field rather than the form) and shows every other
 * refusal's own French sentence. Recovering an outcome by matching prose is the defect this codebase has already paid
 * for once, and the server is the only place that knows why an allocation was refused.
 *
 * ⚠️ **The body is passed through unchanged apart from the cabinet**, deliberately: which month a *standing* forfait
 * takes effect in is the server's decision (AC-6.4a), so there is nothing here to compute and nothing to normalise. A
 * hop that helpfully filled a month in would be the second answer that AC exists to prevent.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; [key: string]: unknown };

  try {
    body = (await request.json()) as { clinicId?: string };
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  const { clinicId, ...payload } = body;

  if (!clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  try {
    const recorded = await consoleFetch(`/platform/clinics/${clinicId}/messaging-allowances`, {
      method: "POST",
      token,
      body: payload,
    });

    return NextResponse.json(recorded);
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

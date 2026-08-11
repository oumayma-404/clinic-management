import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Recording a payment (`platform-console` AC-4.1) — the console's **only** write.
 *
 * ⚠️ **It exists because the session token is in an HttpOnly cookie browser JavaScript cannot read.** Every other
 * console read happens in a server component; a write comes from a form, so it needs a route handler on the same
 * origin to attach the token. That is the arrangement Part 1 built the whole application around, not a detour.
 *
 * ⚠️ **The refusal travels verbatim, `code` included.** The sheet branches on `clinic_not_found` and shows every
 * other refusal's own French sentence — recovering an outcome by matching prose is the defect this codebase has
 * already paid for once, and the server is the only place that knows why a payment was refused.
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
    const recorded = await consoleFetch(`/platform/clinics/${clinicId}/subscription-periods`, {
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

import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Resetting one clinic account's second factor (`hosted-security-hardening` FR-1.4) — the console's fifth write.
 *
 * ⚠️ **It exists for `/bff/paiements`' reason**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so a write coming from a form needs a route handler on the same origin to attach it.
 *
 * ⚠️ **Nothing is defaulted and nothing is repaired here.** The address and the motif are both required by the API
 * in French, and this hop passes them through untouched — trimming or filling either would put a second set of
 * rules in front of the one place that states them. The only checks here are « was anything sent at all », which is
 * a broken request rather than a refused one.
 *
 * ⚠️ **The refusal travels verbatim, `code` included.** `clinic_account_not_found` and
 * `second_factor_not_enrolled` are different situations for the vendor on the telephone — the first means « check
 * the address », the second « the factor is not what is blocking them » — and only the server knows which.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; email?: string; reason?: string };

  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  const { clinicId, email, reason } = body;

  if (!clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  try {
    const reset = await consoleFetch(`/platform/clinics/${clinicId}/second-factor/reset`, {
      method: "POST",
      token,
      body: { email, reason },
    });

    return NextResponse.json(reset);
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

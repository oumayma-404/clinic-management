import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Resetting one clinic account's **password** — the sibling of `/bff/second-facteur`.
 *
 * ⚠️ **It exists for `/bff/paiements`' reason**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so a write coming from a form needs a route handler on the same origin to attach it.
 *
 * ⚠️ **Named `mot-de-passe-cabinet`, not `mot-de-passe`** — that route already exists in this console and changes
 * the signed-in vendor's *own* password. Two writes one letter apart, one acting on the operator and one on a
 * practice's staff, is exactly the pair a hurried edit conflates.
 *
 * ⚠️ **Nothing is defaulted and nothing is repaired here.** The address and the motif are both required by the API
 * in French, and this hop passes them through untouched — trimming or filling either would put a second set of rules
 * in front of the one place that states them.
 *
 * ⚠️ **The response is forwarded verbatim, and it carries a live credential.** No logging of any kind in this
 * handler: a route log is read by more people, and kept far longer, than the screen that shows the password once.
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
    const reset = await consoleFetch(`/platform/clinics/${clinicId}/password/reset`, {
      method: "POST",
      token,
      body: { email, reason },
    });

    return NextResponse.json(reset);
  } catch (error) {
    // ⚠️ The refusal travels verbatim, `code` included. `clinic_account_not_found` and `account_has_no_password`
    // are different situations for the vendor on the telephone — the first means « check the address », the second
    // « a password is not what is blocking them » — and only the server knows which.
    if (error instanceof ConsoleApiError) {
      return NextResponse.json(
        { error: error.message, code: error.code },
        { status: error.status === 0 ? 503 : error.status },
      );
    }

    return NextResponse.json({ error: "Une erreur inattendue est survenue." }, { status: 500 });
  }
}

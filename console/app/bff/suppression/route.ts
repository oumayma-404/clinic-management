import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Deleting a cabinet, and reading first what that would remove (`clinic-account-removal`) — the console's fourth
 * write, and the only irreversible one.
 *
 * ⚠️ **It exists for `/bff/suspensions`' reason**: the session token is in an HttpOnly cookie browser JavaScript
 * cannot read, so a panel that reads or writes needs a route handler on the same origin to attach it.
 *
 * ⚠️ **The preview is a GET here and not a figure rendered with the fiche**, deliberately. Rendered with the page
 * it would be a ~60-table census paid on every open of every cabinet, and — worse — it would be as old as the page:
 * a vendor who left the tab open for an hour would type a cabinet's name against a sentence describing what it held
 * an hour ago. It is read when the panel opens.
 *
 * ⚠️ **The refusals travel verbatim, `code` included.** `clinic_name_mismatch` and
 * `clinic_deletion_reason_required` are the two the panel acts on differently, and the server is the only
 * participant that can decide the first — it compares against the cabinet the id resolved to, where the browser
 * would only be comparing two of its own strings.
 */
export async function GET(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  const clinicId = new URL(request.url).searchParams.get("clinicId");

  if (!clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  try {
    const preview = await consoleFetch(`/platform/clinics/${clinicId}/deletion-preview`, { token });
    return NextResponse.json(preview);
  } catch (error) {
    return refusal(error);
  }
}

export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; confirmation?: string; reason?: string };

  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  const { clinicId, confirmation, reason } = body;

  if (!clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  try {
    const deleted = await consoleFetch(`/platform/clinics/${clinicId}/delete`, {
      method: "POST",
      token,
      // Neither is defaulted or trimmed into existence here: both are mandatory one layer up, and a blank one has
      // to come back as that layer's own French refusal rather than as a 400 this file invented.
      body: { confirmation, reason },
    });

    return NextResponse.json(deleted);
  } catch (error) {
    return refusal(error);
  }
}

function refusal(error: unknown) {
  if (error instanceof ConsoleApiError) {
    return NextResponse.json({ error: error.message, code: error.code }, { status: error.status || 502 });
  }

  return NextResponse.json({ error: "Erreur inattendue de la console." }, { status: 500 });
}

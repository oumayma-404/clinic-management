import { NextResponse } from "next/server";
import { ConsoleApiError } from "@/lib/api/client";
import { setStorageQuota } from "@/lib/api/platform";
import { readSessionToken } from "@/lib/session";

/**
 * A cabinet's storage ceiling (`features/camera-photo-import`, phase 3) — `/bff/suspensions`' shape and its reason: the
 * session token is in an HttpOnly cookie browser JavaScript cannot read, so a form's write needs a same-origin route
 * to attach it. `quotaBytes: null` returns the cabinet to the deployment's default. The refusal travels verbatim.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { clinicId?: string; quotaBytes?: number | null; reason?: string };

  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  if (!body.clinicId) {
    return NextResponse.json({ error: "Cabinet non précisé." }, { status: 400 });
  }

  // Never defaulted: « 25 Go » and « le quota par défaut » are different answers, and a missing key is neither.
  if (body.quotaBytes !== null && typeof body.quotaBytes !== "number") {
    return NextResponse.json({ error: "Quota non précisé." }, { status: 400 });
  }

  try {
    const changed = await setStorageQuota(token, body.clinicId, body.quotaBytes, body.reason ?? "");
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

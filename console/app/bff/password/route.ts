import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { clearSessionToken, readSessionToken } from "@/lib/session";

/**
 * Changing one's own console password (AC-8.6) — the only account action reachable over the web.
 *
 * ⚠️ **The session is cleared on success, deliberately.** `PlatformAccount.SetPassword` bumps `TokenVersion`,
 * so the token in the cookie is dead the moment this returns; leaving it in place would give the operator an
 * application that looks signed in and 401s on the next click, with nothing saying why.
 */
export async function POST(request: Request) {
  const token = await readSessionToken();

  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  let body: { currentPassword?: string; newPassword?: string };

  try {
    body = (await request.json()) as { currentPassword?: string; newPassword?: string };
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  try {
    await consoleFetch("/platform/auth/password", {
      method: "POST",
      token,
      body: { currentPassword: body.currentPassword ?? "", newPassword: body.newPassword ?? "" },
    });

    await clearSessionToken();
    return new NextResponse(null, { status: 204 });
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

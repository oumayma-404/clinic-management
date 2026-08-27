import { NextResponse } from "next/server";
import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { clearSessionToken, writeSessionToken } from "@/lib/session";

/**
 * The console's sign-in hop: the browser posts credentials here, this exchanges them with the API and puts the
 * resulting token in an **HttpOnly** cookie the page can never read (`lib/session.ts`).
 *
 * ⚠️ **Three actions, one route**, discriminated by `action`, because all three end in the same place — a
 * session cookie — and splitting them would mean three copies of the cookie write. `lib/session.ts` is the
 * single writer for exactly that reason.
 *
 * ⚠️ **The refusal is relayed verbatim, with its `code`.** The sign-in screen branches on `code` to send an
 * unenrolled account to the enrolment form rather than showing it a password error it cannot act on, so
 * flattening the body here would remove the one thing that makes AC-1.3a reachable.
 */

type SessionRequest =
  | { action: "login"; email: string; password: string; totpCode: string }
  | { action: "enrol"; email: string; password: string; totpCode: string }
  | { action: "recovery"; email: string; password: string; recoveryCode: string };

type SessionResponse = { token: string; expiresAt: string; recoveryCodesRemaining?: number };
type EnrolmentResponse = { recoveryCodes: string[] };

export async function POST(request: Request) {
  let body: SessionRequest;

  try {
    body = (await request.json()) as SessionRequest;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  try {
    // Enrolment returns the recovery codes and NO session: the account still has to sign in afterwards with a
    // fresh code, which is what proves the authenticator it just bound actually works.
    if (body.action === "enrol") {
      const enrolment = await consoleFetch<EnrolmentResponse>("/platform/auth/totp/enrol", {
        method: "POST",
        body: { email: body.email, password: body.password, totpCode: body.totpCode },
      });

      return NextResponse.json(enrolment);
    }

    const session = await consoleFetch<SessionResponse>(
      body.action === "recovery" ? "/platform/auth/recovery" : "/platform/auth/login",
      {
        method: "POST",
        body:
          body.action === "recovery"
            ? { email: body.email, password: body.password, recoveryCode: body.recoveryCode }
            : { email: body.email, password: body.password, totpCode: body.totpCode },
      },
    );

    await writeSessionToken(session.token, session.expiresAt);

    return NextResponse.json({ recoveryCodesRemaining: session.recoveryCodesRemaining ?? null });
  } catch (error) {
    if (error instanceof ConsoleApiError) {
      return NextResponse.json(
        { error: error.message, code: error.code },
        // A 0 means the API was unreachable, which is not a status a browser should be handed as itself.
        { status: error.status === 0 ? 503 : error.status },
      );
    }

    return NextResponse.json({ error: "Une erreur inattendue est survenue." }, { status: 500 });
  }
}

/** Signing out is local: the token is stateless, so clearing the cookie is the whole of it. */
export async function DELETE() {
  await clearSessionToken();
  return new NextResponse(null, { status: 204 });
}

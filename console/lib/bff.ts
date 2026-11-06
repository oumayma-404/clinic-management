import { NextResponse } from "next/server";

import { ConsoleApiError, consoleFetch } from "@/lib/api/client";
import { readSessionToken } from "@/lib/session";

/**
 * Calls one console API route with the session token the browser cannot read. The refusal travels verbatim,
 * `code` included, so a screen branches on the code and never on the sentence.
 */
async function forward(path: string, method: "GET" | "POST", body?: unknown): Promise<NextResponse> {
  const token = await readSessionToken();
  if (!token) {
    return NextResponse.json({ error: "Session de console requise." }, { status: 401 });
  }

  try {
    return NextResponse.json(await consoleFetch(path, { method, token, body }));
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

/** Forwards a form's JSON body as a POST. */
export async function forwardPost(request: Request, path: string): Promise<NextResponse> {
  let body: unknown;
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  return forward(path, "POST", body);
}

/** Forwards a read. */
export async function forwardGet(path: string): Promise<NextResponse> {
  return forward(path, "GET");
}

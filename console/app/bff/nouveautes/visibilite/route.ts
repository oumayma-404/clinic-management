import { NextResponse } from "next/server";

import { forwardAction } from "@/lib/bff";

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * « Masquer dans l'app » / « Afficher dans l'app » on a sent news. The API keeps two routes so a truncated body cannot
 * flip the direction; here the direction is a required boolean, never defaulted (`/bff/suspensions`' rule).
 */
export async function POST(request: Request) {
  let body: { broadcastId?: unknown; visible?: unknown };
  try {
    body = (await request.json()) as typeof body;
  } catch {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }
  // A literal `null` (or a bare number) parses fine and is not an object.
  if (body === null || typeof body !== "object") {
    return NextResponse.json({ error: "Requête illisible." }, { status: 400 });
  }

  if (typeof body.broadcastId !== "string" || !GUID.test(body.broadcastId)) {
    return NextResponse.json({ error: "Nouveauté non précisée." }, { status: 400 });
  }
  if (typeof body.visible !== "boolean") {
    return NextResponse.json({ error: "Action non précisée." }, { status: 400 });
  }

  return forwardAction(`/platform/news/${body.broadcastId}/${body.visible ? "show" : "hide"}`);
}

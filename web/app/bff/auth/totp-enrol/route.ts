import { NextRequest, NextResponse } from 'next/server';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

/**
 * Enrolling a second factor from the login screen (`hosted-security-hardening` FR-1.3).
 *
 * ⚠️ **It writes no cookie, and that is the contract, not an omission.** Enrolling is not signing in: the server
 * issues no session either, and the screen stops on the recovery codes so the user then signs in with their new
 * code — which is also what proves the authenticator works before they depend on it.
 *
 * ⚠️ **The refusal's `code` is relayed**, unlike `local-login`'s flattened bad-credentials answer: every code
 * this endpoint can produce is a destination for the screen (« déjà enrôlé » ends the flow, « code invalide »
 * stays on the field), and recovering that from French prose is what this repository deleted elsewhere.
 */
export async function POST(request: NextRequest) {
  let body: { email?: string; password?: string; totpCode?: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: 'Requête invalide.' }, { status: 400 });
  }

  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/totp/enrol`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...forwardedForHeader(request),
      },
      body: JSON.stringify({
        email: body.email ?? '',
        password: body.password ?? '',
        // Absent on step one — « give me a secret » — and present on step two.
        ...(body.totpCode ? { totpCode: body.totpCode } : {}),
      }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess) {
      return NextResponse.json(
        {
          error: data?.error || "Impossible d'enrôler le second facteur.",
          ...(typeof data?.code === 'string' ? { code: data.code } : {}),
        },
        { status: res.status === 200 ? 400 : res.status }
      );
    }

    // `secretUri` + `secret` on step one, `recoveryCodes` on step two. Never both.
    return NextResponse.json(data.value ?? {});
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre le serveur du cabinet. Veuillez réessayer.' },
      { status: 502 }
    );
  }
}

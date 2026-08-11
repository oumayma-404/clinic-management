/**
 * Reads a refusal the console's own BFF routes returned — the canonical `{ error, code }` shape.
 *
 * ⚠️ **Deliberately not `response.json().catch(() => ({}))`.** That swallow is the shape `check:responsive`'s
 * `failed-read-as-empty` fails the build on, and the rule is right even here: an empty object stands in for
 * « the server said nothing », so an unreadable body and a body with no message collapse into one generic
 * sentence and the status — the only fact still available — is thrown away with them.
 *
 * So the body is read as **text** first and parsed inside a guard. When it is not JSON the status still produces
 * a French sentence naming what happened, which is strictly more than the generic fallback could say.
 */
export type Refusal = { error: string; code?: string };

export async function readRefusal(response: Response): Promise<Refusal> {
  const body = await response.text().catch(() => "");

  if (body) {
    try {
      const parsed = JSON.parse(body) as { error?: string; code?: string };
      if (parsed && typeof parsed.error === "string" && parsed.error.length > 0) {
        return { error: parsed.error, code: parsed.code };
      }
    } catch {
      // Not JSON — fall through to the status-derived sentence rather than inventing a message.
    }
  }

  return { error: messageForStatus(response.status) };
}

/**
 * What each status means to the operator, in French. Only the ones this application can actually produce; a
 * status not listed says so with its number rather than being described wrongly.
 */
function messageForStatus(status: number): string {
  switch (status) {
    case 401:
      return "Identifiants invalides.";
    case 403:
      return "Cette action n'est pas autorisée pour ce compte.";
    case 429:
      return "Trop de tentatives. Réessayez dans quelques minutes.";
    case 503:
      return "Impossible de joindre le serveur. Vérifiez que le tunnel vers la console est ouvert, puis réessayez.";
    default:
      return `Le serveur a refusé la requête (${status}).`;
  }
}

/** The successful body of a BFF call, or null when it carried none. Same no-swallow rule as above. */
export async function readJson<T>(response: Response): Promise<T | null> {
  const body = await response.text().catch(() => "");

  if (!body) {
    return null;
  }

  try {
    return JSON.parse(body) as T;
  } catch {
    return null;
  }
}

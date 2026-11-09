import Link from "next/link";
import { redirect } from "next/navigation";

import { NewsWorkspace } from "@/components/news-workspace";
import { ConsoleApiError } from "@/lib/api/client";
import {
  fetchNewsAudience,
  fetchNewsHistory,
  redirectIfPasswordChangeRequired,
  type PlatformNewsAudience,
  type PlatformNewsBroadcast,
} from "@/lib/api/platform";
import { readSessionToken } from "@/lib/session";

/** « Nouveautés » — e-mail the app's news to the admin of every active or trialling cabinet. */
export const dynamic = "force-dynamic";

export default async function NouveautesPage() {
  const token = await readSessionToken();
  if (!token) {
    redirect("/login");
  }

  let audience: PlatformNewsAudience;
  try {
    audience = await fetchNewsAudience(token);
  } catch (error) {
    redirectIfPasswordChangeRequired(error);
    return <ReadFailure error={error} />;
  }

  // A failed history read degrades only its own section; the form still works.
  let history: PlatformNewsBroadcast[] | null = null;
  let historyError: string | null = null;
  try {
    history = await fetchNewsHistory(token);
  } catch (error) {
    redirectIfPasswordChangeRequired(error);
    historyError = messageOf(error);
  }

  return (
    <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:py-8">
      <Link
        href="/cabinets"
        className="inline-flex min-h-11 items-center text-sm text-muted-foreground underline underline-offset-4"
      >
        ← Retour au portefeuille
      </Link>

      <header className="mt-4 space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Nouveautés</h1>
        <p className="text-sm text-muted-foreground">
          Un e-mail aux administrateurs des cabinets actifs ou en essai.
        </p>
      </header>

      <div className="mt-6">
        <NewsWorkspace audience={audience} history={history} historyError={historyError} />
      </div>
    </main>
  );
}

function messageOf(error: unknown): string {
  return error instanceof ConsoleApiError ? error.message : "Une erreur inattendue est survenue pendant la lecture.";
}

/** The audience could not be read, so nothing can be sent — said, never shown as an empty form. */
function ReadFailure({ error }: { error: unknown }) {
  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <h1 className="text-lg font-semibold">Nouveautés illisibles</h1>
        <p className="mt-2 text-sm text-muted-foreground">{messageOf(error)}</p>
      </div>
    </main>
  );
}

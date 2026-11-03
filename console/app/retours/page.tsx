import Link from "next/link";
import { redirect } from "next/navigation";

import { FeedbackList } from "@/components/feedback-list";
import { Pager } from "@/components/ui/pager";
import { ConsoleApiError } from "@/lib/api/client";
import {
  FEEDBACK_KIND_FILTERS,
  feedbackSearchParams,
  fetchFeedback,
  redirectIfPasswordChangeRequired,
  type FeedbackQuery,
} from "@/lib/api/platform";
import { readSessionToken } from "@/lib/session";

/**
 * « Retours » — what the cabinets' people told the vendor, newest first (`guided-first-days` E, S14). Each message
 * was also e-mailed to every console account the moment it was sent; this page is where they stay.
 *
 * ⚠️ **Read-only, and a pure server component**: the kind filter is links, like the journal's, so a filtered list is
 * shareable and there is no client state to drift from the URL.
 */
export const dynamic = "force-dynamic";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function RetoursPage({ searchParams }: PageProps) {
  const token = await readSessionToken();
  if (!token) {
    redirect("/login");
  }

  const params = await searchParams;
  const query: FeedbackQuery = {
    kind: single(params.kind),
    page: toPage(single(params.page)),
  };

  let page: Awaited<ReturnType<typeof fetchFeedback>>;
  try {
    page = await fetchFeedback(token, query);
  } catch (error) {
    redirectIfPasswordChangeRequired(error);
    return <ReadFailure error={error} />;
  }

  const href = (next: FeedbackQuery) => {
    const suffix = feedbackSearchParams(next).toString();
    return suffix ? `/retours?${suffix}` : "/retours";
  };
  const chip = "inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm";
  const active = "bg-primary text-primary-foreground";

  return (
    <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:py-8">
      <Link
        href="/cabinets"
        className="inline-flex min-h-11 items-center text-sm text-muted-foreground underline underline-offset-4"
      >
        ← Retour au portefeuille
      </Link>

      <header className="mt-4 space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Retours</h1>
        <p className="text-sm text-muted-foreground">
          Les avis, idées, problèmes et questions envoyés depuis l&apos;application. Chacun arrive aussi par e-mail.
        </p>
      </header>

      <div className="mt-6 space-y-6">
        {/* Page is dropped on every change: « page 3 » of one kind means nothing under another. */}
        <ul className="flex flex-wrap gap-2" aria-label="Filtrer par type">
          <li>
            <Link href={href({})} className={`${chip} ${!query.kind ? active : ""}`}>
              Tous
            </Link>
          </li>
          {FEEDBACK_KIND_FILTERS.map((kind) => (
            <li key={kind.key}>
              <Link href={href({ kind: kind.key })} className={`${chip} ${query.kind === kind.key ? active : ""}`}>
                {kind.label}
              </Link>
            </li>
          ))}
        </ul>

        <FeedbackList page={page} />
        <Pager
          page={page.page}
          totalPages={page.totalPages}
          totalCount={page.totalCount}
          hasPreviousPage={page.hasPreviousPage}
          hasNextPage={page.hasNextPage}
          href={(target) => href({ ...query, page: target })}
          label="Pagination des retours"
          noun="retour"
          nounPlural="retours"
        />
      </div>
    </main>
  );
}

/** A failed read says so — « aucun retour » would be a claim that nobody has written. */
function ReadFailure({ error }: { error: unknown }) {
  const message =
    error instanceof ConsoleApiError ? error.message : "Une erreur inattendue est survenue pendant la lecture.";

  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <h1 className="text-lg font-semibold">Retours illisibles</h1>
        <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        <p className="mt-2 text-sm text-muted-foreground">
          Ceci n&apos;est <strong>pas</strong> une liste vide : elle n&apos;a pas pu être lue.
        </p>
      </div>
    </main>
  );
}

function single(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

function toPage(value: string | undefined): number | undefined {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 1 ? parsed : undefined;
}

import Link from "next/link";
import { redirect } from "next/navigation";

import { AccessLogFilters } from "@/components/access-log-filters";
import { AccessLogList } from "@/components/access-log-list";
import { Pager } from "@/components/ui/pager";
import { ConsoleApiError } from "@/lib/api/client";
import { accessLogSearchParams, fetchAccessLog, type AccessLogQuery } from "@/lib/api/platform";
import { readSessionToken } from "@/lib/session";

/**
 * « Journal » — the console's own access ledger, read back (`platform-console` FR-5, AC-7.3).
 *
 * ⚠️ **A ledger nobody can read is a promise nobody can check.** Recording what a surface with cross-cabinet reach
 * does is only worth anything if somebody can look at it afterwards, so this screen is part of the guarantee rather
 * than a convenience on top of it.
 *
 * ⚠️ **Read-only, and visibly so.** Nothing on this page edits or deletes a row and there is no control that looks
 * as though it might — the endpoint has no write action at all.
 *
 * ⚠️ **A console screen, not a clinic one.** Showing a practice which vendor account opened its file is named out
 * of scope by the spec; there is no counterpart of this page in the clinic app.
 */
export const dynamic = "force-dynamic";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function JournalPage({ searchParams }: PageProps) {
  const token = await readSessionToken();
  if (!token) {
    redirect("/login");
  }

  const params = await searchParams;
  const query: AccessLogQuery = {
    accountId: single(params.accountId),
    clinicId: single(params.clinicId),
    page: toPage(single(params.page)),
  };

  let page: Awaited<ReturnType<typeof fetchAccessLog>>;
  try {
    page = await fetchAccessLog(token, query);
  } catch (error) {
    return <ReadFailure error={error} />;
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
        <h1 className="text-2xl font-semibold tracking-tight">Journal des accès</h1>
        <p className="text-sm text-muted-foreground">
          Ce que les comptes console ont fait, et à quel cabinet. Ouvrir la fiche d&apos;un cabinet crée une entrée ;
          afficher la liste des cabinets n&apos;en crée pas — une lecture de la liste touche tous les cabinets à la
          fois, et une entrée par cabinet et par page noierait celles qui comptent.
        </p>
      </header>

      <div className="mt-6 space-y-6">
        <AccessLogFilters page={page} query={query} />
        <AccessLogList page={page} />
        <Pager
          page={page.page}
          totalPages={page.totalPages}
          totalCount={page.totalCount}
          hasPreviousPage={page.hasPreviousPage}
          hasNextPage={page.hasNextPage}
          href={(target) => {
            const suffix = accessLogSearchParams({ ...query, page: target }).toString();
            return suffix ? `/journal?${suffix}` : "/journal";
          }}
          label="Pagination du journal"
          noun="accès enregistré"
          nounPlural="accès enregistrés"
        />
      </div>
    </main>
  );
}

/** EC-12 again: a failed read says so. « Aucun accès » would be a claim that nobody has ever opened a cabinet. */
function ReadFailure({ error }: { error: unknown }) {
  const message =
    error instanceof ConsoleApiError
      ? error.message
      : "Une erreur inattendue est survenue pendant la lecture du journal.";

  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <h1 className="text-lg font-semibold">Journal illisible</h1>
        <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        <p className="mt-2 text-sm text-muted-foreground">
          Ceci n&apos;est <strong>pas</strong> un journal vide : il n&apos;a pas pu être lu.
        </p>
      </div>
    </main>
  );
}

function single(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** A bad or absent page number is page 1, never a French error — the tolerance `PageRequest` applies server-side. */
function toPage(value: string | undefined): number | undefined {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 1 ? parsed : undefined;
}

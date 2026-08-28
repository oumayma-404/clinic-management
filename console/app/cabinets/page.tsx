import Link from "next/link";
import { redirect } from "next/navigation";

import { ClinicPortfolio } from "@/components/clinic-portfolio";
import { PortfolioFilters } from "@/components/portfolio-filters";
import { PortfolioPager } from "@/components/portfolio-pager";
import { PortfolioSummary } from "@/components/portfolio-summary";
import { ConsoleApiError } from "@/lib/api/client";
import {
  fetchPortfolio,
  fetchSummary,
  redirectIfPasswordChangeRequired,
  type PortfolioQuery,
} from "@/lib/api/platform";
import { formatDateTime, formatFreshness } from "@/lib/format";
import { readSessionToken } from "@/lib/session";
import { SignOutButton } from "./sign-out-button";

/**
 * The portfolio (`platform-console` US-2).
 *
 * ⚠️ **A server component, and every read happens here.** The session token lives in an HttpOnly cookie that
 * browser JavaScript cannot see, so a client-side fetch would need the token in the page — the one thing Part 1's
 * whole arrangement exists to avoid. Filters are URL state for the same reason.
 *
 * ⚠️ **A failed read says « je n'ai pas pu lire », never « aucun cabinet »** (EC-12). Those two are the same
 * picture and opposite facts: an empty table after a failure would have the vendor conclude the deployment is
 * empty, and a portfolio genuinely full of idle cabinets is a real and useful answer (EC-8) — which is exactly
 * what makes the silent version dangerous.
 */
export const dynamic = "force-dynamic";

interface PageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

export default async function CabinetsPage({ searchParams }: PageProps) {
  const token = await readSessionToken();
  if (!token) {
    redirect("/login");
  }

  const params = await searchParams;
  const query: PortfolioQuery = {
    q: single(params.q),
    dormant: single(params.dormant) === "true",
    state: single(params.state),
    messaging: single(params.messaging),
    sort: single(params.sort),
    page: toPage(single(params.page)),
  };

  let page: Awaited<ReturnType<typeof fetchPortfolio>>;
  let summary: Awaited<ReturnType<typeof fetchSummary>>;

  try {
    // Sequential rather than Promise.all: two calls over one tunnel, and the failure message must name the read
    // that failed rather than whichever of a pair rejected first.
    summary = await fetchSummary(token);
    page = await fetchPortfolio(token, query);
  } catch (error) {
    // A bootstrapped account is refused every read until it replaces the one-time password, and that refusal is
    // a destination rather than a message — see `redirectIfPasswordChangeRequired`. Without this, the first
    // account created on a deployment lands here and is told the portfolio is unreadable.
    redirectIfPasswordChangeRequired(error);
    return <ReadFailure error={error} />;
  }

  return (
    <main className="mx-auto w-full max-w-[100rem] px-4 py-6 sm:py-8">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Cabinets</h1>
          <p className="text-sm text-muted-foreground">
            Activité réelle de chaque cabinet. Aucun dossier patient n&apos;est accessible depuis cette console.
          </p>
        </div>
        {/* The console's whole navigation, deliberately two links rather than a rail (FR-7: no clinic chrome). It
            wraps rather than collapsing behind a hamburger, because two items fit at 320 px. */}
        <div className="flex flex-wrap items-center gap-2">
          <Link
            href="/journal"
            className="inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm hover:bg-muted/50"
          >
            Journal des accès
          </Link>
          <SignOutButton />
        </div>
      </header>

      <div className="mt-6 space-y-6">
        <PortfolioSummary summary={summary} />

        {/* ⚠️ The « presque épuisé » threshold comes from the PAGE the server just returned, not from a constant here:
            the filter's SQL predicate and the words on its button are then one figure. */}
        <PortfolioFilters query={query} messagingNearThresholdPercent={page.messagingNearThresholdPercent} />

        {/* AC-2.8: stated beside the figures, on every width — not tucked into a tooltip or a desktop-only
            caption. A stale figure presented as live is how a cabinet that started working yesterday gets a
            churn call today. */}
        <p className="text-sm text-muted-foreground" role="note">
          {page.countersAsOf
            ? `Compteurs d'activité mesurés ${formatFreshness(page.countersAsOf)} (le ${formatDateTime(page.countersAsOf)}).`
            : "Les compteurs d'activité n'ont jamais été calculés sur ce déploiement : les chiffres d'activité sont indisponibles, ce qui n'est pas la même chose que « aucune activité »."}
        </p>

        {/* Which month the reminder figures describe, stated beside them: a page rendered at 23:59 on the 31st and
            read a minute later would otherwise be labelled with the wrong month by whoever is looking at it. */}
        <p className="text-sm text-muted-foreground" role="note">
          Les chiffres « Rappels » portent sur {page.messagingMonthLabel}.
        </p>

        <ClinicPortfolio page={page} />

        <PortfolioPager page={page} query={query} />
      </div>
    </main>
  );
}

/**
 * The read failed. It says so, in French, with the server's own sentence where there is one — and it is
 * deliberately not an empty table.
 */
function ReadFailure({ error }: { error: unknown }) {
  const message =
    error instanceof ConsoleApiError
      ? error.message
      : "Une erreur inattendue est survenue pendant la lecture du portefeuille.";

  return (
    <main className="mx-auto w-full max-w-2xl px-4 py-8">
      <div className="rounded-lg border border-destructive/40 bg-card p-6" role="alert">
        <h1 className="text-lg font-semibold">Portefeuille illisible</h1>
        <p className="mt-2 text-sm text-muted-foreground">{message}</p>
        <p className="mt-2 text-sm text-muted-foreground">
          Ceci n&apos;est <strong>pas</strong> un portefeuille vide : la liste n&apos;a pas pu être lue.
        </p>
      </div>
    </main>
  );
}

function single(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/** A bad or absent page number is page 1, never a French error — the same tolerance `PageRequest` applies. */
function toPage(value: string | undefined): number | undefined {
  const parsed = Number.parseInt(value ?? "", 10);
  return Number.isFinite(parsed) && parsed > 1 ? parsed : undefined;
}

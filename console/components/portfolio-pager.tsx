import Link from "next/link";

import { portfolioSearchParams, type PlatformClinicPage, type PortfolioQuery } from "@/lib/api/platform";
import { formatCount } from "@/lib/format";

/**
 * « N résultats » and the two page steps.
 *
 * ⚠️ **Links, not buttons.** The whole screen is URL state, so paging is navigation: the back button works, a
 * page is shareable, and this component needs no client JavaScript at all. A disabled step is rendered as text
 * rather than as a link that goes nowhere — a dead control is worse than an absent one.
 *
 * The total is stated beside the steps because « page 2 sur 3 » alone does not answer the question the vendor
 * actually has, which is how large the portfolio is.
 */
export function PortfolioPager({ page, query }: { page: PlatformClinicPage; query: PortfolioQuery }) {
  function href(target: number): string {
    const params = portfolioSearchParams({ ...query, page: target });
    const suffix = params.toString();
    return suffix ? `/cabinets?${suffix}` : "/cabinets";
  }

  const step = "rounded-md border border-border px-3 py-2 text-sm";

  return (
    <nav className="flex flex-wrap items-center justify-between gap-3" aria-label="Pagination du portefeuille">
      <p className="text-sm text-muted-foreground" aria-live="polite">
        {formatCount(page.totalCount)} cabinet{page.totalCount > 1 ? "s" : ""} · page {page.page} sur {page.totalPages}
      </p>

      <div className="flex items-center gap-2">
        {page.hasPreviousPage ? (
          <Link href={href(page.page - 1)} className={`${step} hover:bg-muted/50`} rel="prev">
            Précédent
          </Link>
        ) : (
          <span className={`${step} text-muted-foreground opacity-60`} aria-hidden="true">
            Précédent
          </span>
        )}

        {page.hasNextPage ? (
          <Link href={href(page.page + 1)} className={`${step} hover:bg-muted/50`} rel="next">
            Suivant
          </Link>
        ) : (
          <span className={`${step} text-muted-foreground opacity-60`} aria-hidden="true">
            Suivant
          </span>
        )}
      </div>
    </nav>
  );
}

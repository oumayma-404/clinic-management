import { Pager } from "@/components/ui/pager";
import { portfolioSearchParams, type PlatformClinicPage, type PortfolioQuery } from "@/lib/api/platform";

/**
 * The portfolio's pager — the shared `ui/pager.tsx` with this screen's hrefs and wording.
 *
 * It became a wrapper in Part 3, when « Journal » needed the same control: the mechanics (links not buttons, a
 * disabled step as text, the total beside the steps) live in one place so a fix cannot land in only one of them.
 */
export function PortfolioPager({ page, query }: { page: PlatformClinicPage; query: PortfolioQuery }) {
  function href(target: number): string {
    const suffix = portfolioSearchParams({ ...query, page: target }).toString();
    return suffix ? `/cabinets?${suffix}` : "/cabinets";
  }

  return (
    <Pager
      page={page.page}
      totalPages={page.totalPages}
      totalCount={page.totalCount}
      hasPreviousPage={page.hasPreviousPage}
      hasNextPage={page.hasNextPage}
      href={href}
      label="Pagination du portefeuille"
      noun="cabinet"
      nounPlural="cabinets"
    />
  );
}

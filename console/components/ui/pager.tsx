import Link from "next/link";

import { formatCount } from "@/lib/format";

/**
 * « N résultats » and the two page steps, for any paged console list.
 *
 * ⚠️ **Links, not buttons.** Every console list is URL state, so paging is navigation: the back button works, a
 * page is shareable, and this needs no client JavaScript at all. A disabled step is rendered as **text** rather
 * than as a link that goes nowhere — a dead control is worse than an absent one, and the device contract forbids
 * it in the same breath as hover-only affordances.
 *
 * ⚠️ **It is shared rather than written per list, and that is the point.** Part 3 adds a second paged screen
 * (« Journal »), and two pagers with independently written disabled-step handling is this repository's dominant
 * defect shape — a fix landing in one of them. The caller supplies only how to build a href for a page number.
 */
export function Pager({
  page,
  totalPages,
  totalCount,
  hasPreviousPage,
  hasNextPage,
  href,
  label,
  noun,
  nounPlural,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  href: (target: number) => string;
  /** The nav's accessible name — two pagers on one page would otherwise be indistinguishable. */
  label: string;
  noun: string;
  nounPlural: string;
}) {
  const step = "rounded-md border border-border px-3 py-2 text-sm";

  return (
    <nav className="flex flex-wrap items-center justify-between gap-3" aria-label={label}>
      {/* The total is stated beside the steps because « page 2 sur 3 » alone does not answer the question the
          reader actually has, which is how large the list is. */}
      <p className="text-sm text-muted-foreground" aria-live="polite">
        {formatCount(totalCount)} {totalCount === 1 ? noun : nounPlural} · page {page} sur {Math.max(totalPages, 1)}
      </p>

      <div className="flex items-center gap-2">
        {hasPreviousPage ? (
          <Link href={href(page - 1)} className={`${step} hover:bg-muted/50`} rel="prev">
            Précédent
          </Link>
        ) : (
          <span className={`${step} text-muted-foreground opacity-60`} aria-hidden="true">
            Précédent
          </span>
        )}

        {hasNextPage ? (
          <Link href={href(page + 1)} className={`${step} hover:bg-muted/50`} rel="next">
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

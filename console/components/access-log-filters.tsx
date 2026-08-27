import Link from "next/link";

import { accessLogSearchParams, type AccessLogQuery, type PlatformAccessLogPage } from "@/lib/api/platform";

/**
 * The journal's two filters (`platform-console` step 29: « filterable by account and by cabinet »).
 *
 * ⚠️ **Links, and therefore no client JavaScript at all.** Unlike the portfolio's filters — a search box, a toggle
 * and three sort orders, which genuinely need a form and a sheet at 320 px — these are a handful of console
 * accounts and one cabinet arrived at from its own fiche. Rendering them as links keeps this page a pure server
 * component, makes a filtered journal shareable, and means there is no state to get out of step with the URL.
 *
 * ⚠️ **The account options come from the rows** (`page.actors`), not from the account table: an account that has
 * opened nothing would be a filter matching nothing, and a deactivated one that did must stay filterable.
 *
 * ⚠️ **The cabinet filter is a removable chip rather than a picker.** There is no cabinet list here to choose
 * from — you arrive with one — so a select would be a control with nothing in it. Removing it is one tap, and the
 * chip is what stops a journal narrowed to one cabinet reading as a journal with almost nothing in it.
 */
export function AccessLogFilters({ page, query }: { page: PlatformAccessLogPage; query: AccessLogQuery }) {
  function href(next: AccessLogQuery): string {
    // Page is dropped on every change: « page 3 » of the old filter is meaningless under the new one, and landing
    // past the end reads as « aucun accès » rather than as « you were on page 3 ».
    const suffix = accessLogSearchParams({ ...query, ...next, page: undefined }).toString();
    return suffix ? `/journal?${suffix}` : "/journal";
  }

  const chip = "inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm";
  const active = "bg-primary text-primary-foreground";

  // The cabinet's name is not on this page unless a row carries it — which it does whenever the filter matched
  // anything. With no matching row we show the id rather than inventing a name we have not read.
  const clinicName = page.items.find((entry) => entry.clinicId === query.clinicId)?.clinicName;

  return (
    <div className="space-y-3">
      <div>
        <p className="mb-2 text-sm font-medium">Compte console</p>
        {page.actors.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Aucun accès n&apos;a encore été enregistré, donc aucun compte à filtrer.
          </p>
        ) : (
          <ul className="flex flex-wrap gap-2">
            <li>
              <Link href={href({ accountId: undefined })} className={`${chip} ${!query.accountId ? active : ""}`}>
                Tous les comptes
              </Link>
            </li>
            {page.actors.map((actor) => (
              <li key={actor.platformAccountId}>
                <Link
                  href={href({ accountId: actor.platformAccountId })}
                  className={`${chip} ${query.accountId === actor.platformAccountId ? active : ""}`}
                >
                  {actor.accountEmail}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      {query.clinicId ? (
        <div>
          <p className="mb-2 text-sm font-medium">Cabinet</p>
          <Link
            href={href({ clinicId: undefined })}
            className={`${chip} gap-2`}
            aria-label={`Retirer le filtre sur le cabinet ${clinicName ?? query.clinicId}`}
          >
            {clinicName ?? query.clinicId}
            <span aria-hidden="true">×</span>
          </Link>
        </div>
      ) : null}
    </div>
  );
}

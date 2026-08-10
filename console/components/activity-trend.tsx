import type { PlatformActivityMonth } from "@/lib/api/platform";
import { EM_DASH, formatCount } from "@/lib/format";

/**
 * A cabinet's activity over six months (`platform-console` AC-3.1).
 *
 * ⚠️ **Every value is given as text, not only as a bar.** The plan requires it, and the reason is not only
 * accessibility: a bar chart answers « is this cabinet busier than it was? » and the vendor's next question is
 * always « by how much? ». Reading a figure off a 40 px column is guessing, so each month states its own number
 * beside the bar and the whole thing degrades to a legible list at 320 px with no chart library involved.
 *
 * ⚠️ **A month the counter pass never covered is « pas encore mesuré », never a zero bar** (EC-15). The pass
 * writes a rolling 30-day window (progress.md DEV-5), so on a young deployment five of these six have no data at
 * all — and drawing them flat would show every cabinet in the portfolio collapsing the further back you look,
 * which is a false story about the product's own history.
 *
 * ⚠️ **It scrolls in its own container**, so the months stay legible at 320 px instead of being squeezed. No
 * `flex justify-center` inside it: centring overflowing content pushes half of it outside the scrollable region,
 * which is what `check:responsive`'s `arch-clipping` rule exists to catch.
 */
export function ActivityTrend({ trend }: { trend: PlatformActivityMonth[] }) {
  // The scale is the busiest measured month, so the columns are comparable with each other. A portfolio-wide
  // scale would make a small practice's chart permanently flat and unreadable.
  const peak = Math.max(...trend.filter((m) => m.daysMeasured > 0).map((m) => m.writes), 0);
  const measuredMonths = trend.filter((m) => m.daysMeasured > 0).length;

  return (
    <section aria-labelledby="trend-heading" className="rounded-lg border border-border bg-card p-4">
      <h2 id="trend-heading" className="text-base font-semibold">
        Activité sur six mois
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Enregistrements par mois, faits par les personnes du cabinet.
        {measuredMonths < trend.length ? (
          <>
            {" "}
            {measuredMonths === 0
              ? "Aucun de ces mois n'a encore été mesuré."
              : `${trend.length - measuredMonths} de ces mois n'ont pas encore été mesurés : les compteurs ne remontent que sur 30 jours, et un mois non mesuré n'est pas un mois sans activité.`}
          </>
        ) : null}
      </p>

      {/* One scroller, one row of columns. `overflow-x-auto` on the wrapper and a min width on the track, so the
          months keep their labels at 320 px rather than being compressed into illegibility. */}
      <div className="mt-4 overflow-x-auto">
        <ol className="flex min-w-[20rem] items-end gap-3">
          {trend.map((month) => (
            <li key={`${month.year}-${month.month}`} className="flex min-w-[4.5rem] flex-1 flex-col items-center gap-2">
              {/* The figure first in the DOM: it is the answer, and the bar is the illustration. */}
              <p className="text-sm font-medium tabular-nums">
                {month.daysMeasured > 0 ? formatCount(month.writes) : EM_DASH}
              </p>

              <div
                className="flex h-24 w-full items-end rounded-sm bg-muted/40"
                // Not a `<progress>` and not an image: it is a decorative rendering of the number stated above it,
                // so it carries no accessible name of its own and the list item reads as « août 2026 — 128 ».
                aria-hidden="true"
              >
                {month.daysMeasured > 0 ? (
                  <div
                    className="w-full rounded-sm bg-primary/70"
                    style={{ height: `${peak > 0 ? Math.max((month.writes / peak) * 100, month.writes > 0 ? 6 : 0) : 0}%` }}
                  />
                ) : (
                  // A measured zero and an unmeasured month must not look alike: the second gets hatching rather
                  // than an empty column, which is the same distinction the list's « — » makes.
                  <div className="h-full w-full rounded-sm border border-dashed border-border" />
                )}
              </div>

              <p className="text-center text-xs text-muted-foreground">{month.monthLabel}</p>
              {month.daysMeasured === 0 ? (
                <p className="text-center text-xs text-muted-foreground">non mesuré</p>
              ) : null}
            </li>
          ))}
        </ol>
      </div>

      {/* The whole series as text, so nothing on this screen is reachable only by reading a column's height. It
          is a real list rather than a `<table>`, which is what keeps it out of `card-fallback`'s scope honestly:
          there is no grid of columns here to convert. */}
      <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-2 min-[380px]:grid-cols-2 lg:grid-cols-3">
        {trend.map((month) => (
          <div key={`text-${month.year}-${month.month}`} className="min-w-0">
            <dt className="text-xs text-muted-foreground">{month.monthLabel}</dt>
            <dd className="text-sm">
              {month.daysMeasured > 0 ? (
                <>
                  {formatCount(month.writes)} enreg. · {formatCount(month.appointments)} RDV ·{" "}
                  {formatCount(month.patientsCreated)} patients
                </>
              ) : (
                "Pas encore mesuré"
              )}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

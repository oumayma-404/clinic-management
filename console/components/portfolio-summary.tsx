import Link from "next/link";

import type { PlatformSummary } from "@/lib/api/platform";
import { formatCount, formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * The strip above the portfolio (`platform-console` AC-2.7).
 *
 * ⚠️ **Every figure is a link to the list it counted**, the same rule the clinic dashboard follows: a number
 * nobody can drill into is a number nobody can check, and the vendor's next action after reading « 4 dormants »
 * is always « lesquels ? ». A figure with no destination is rendered as plain text rather than as a dead link —
 * a control that does nothing is worse than no control (device contract § 0).
 *
 * ⚠️ **The five state counts are mutually exclusive and sum to « Cabinets ».** « Expire sous 14 j » is deliberately
 * NOT a sixth bucket — it is a subset of the covered cabinets, which is the whole point of showing it — so it is
 * labelled apart and placed after them. Lines that do not add up to the total above them is what makes a strip
 * unreadable at a glance.
 *
 * ⚠️ **The vendor's revenue is its own figure with its own label** (AC-2.7). It is never a sum of the cabinets'
 * « Encaissé (cabinet) », which is their turnover; the two names carry that distinction wherever they appear.
 */
export function PortfolioSummary({ summary }: { summary: PlatformSummary }) {
  const figures: Array<{ label: string; value: number; href?: string; tone?: "warning" }> = [
    { label: "Cabinets", value: summary.clinics, href: "/cabinets" },
    { label: "En essai", value: summary.inTrial, href: "/cabinets?state=trial" },
    { label: "Actifs", value: summary.active, href: "/cabinets?state=active" },
    {
      label: "Expire sous 14 j",
      value: summary.expiringWithin14Days,
      href: "/cabinets?state=expiringSoon",
      tone: "warning",
    },
    { label: "Expirés", value: summary.expired, href: "/cabinets?state=expired", tone: "warning" },
    { label: "Suspendus", value: summary.suspended, href: "/cabinets?state=suspended", tone: "warning" },
    { label: "Dormants (30 j)", value: summary.dormant, href: "/cabinets?dormant=true", tone: "warning" },
  ];

  // FR-13's failure state: a cabinet that somehow has no entitlement at all. Shown only when there are any — on a
  // healthy deployment this is 0 for ever, and a permanent zero teaches the reader to skip the strip.
  if (summary.noEntitlement > 0) {
    figures.push({
      label: "Sans abonnement",
      value: summary.noEntitlement,
      href: "/cabinets?state=missing",
      tone: "warning",
    });
  }

  // Only when there are any: on a healthy deployment this figure is 0 for ever, and a permanent « 0 jamais
  // mesuré » teaches the reader to ignore the strip.
  if (summary.neverMeasured > 0) {
    figures.push({ label: "Jamais mesurés", value: summary.neverMeasured, tone: "warning" });
  }

  return (
    <section aria-labelledby="portfolio-summary-heading" className="space-y-3">
      <h2 id="portfolio-summary-heading" className="sr-only">
        Résumé du portefeuille
      </h2>

      {/* 2 columns at 320 px, 3 from `sm:`, 6 from `xl:` — the figure count is small, so the grid opens up
          rather than stretching two cards across a desktop. */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-6">
        {figures.map((figure) => {
          const body = (
            <>
              <span className="text-xs text-muted-foreground">{figure.label}</span>
              <span
                className={cn(
                  "text-2xl font-semibold tabular-nums",
                  figure.tone === "warning" && figure.value > 0 && "text-destructive",
                )}
              >
                {formatCount(figure.value)}
              </span>
            </>
          );

          const className =
            "flex min-h-[4.5rem] flex-col justify-between rounded-lg border border-border bg-card p-3";

          return figure.href ? (
            <Link
              key={figure.label}
              href={figure.href}
              className={cn(className, "transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-hidden")}
            >
              {body}
            </Link>
          ) : (
            <div key={figure.label} className={className}>
              {body}
            </div>
          );
        })}
      </div>

      <p className="text-sm text-muted-foreground">
        Encaissé par l&apos;éditeur ce mois-ci :{" "}
        <span className="font-medium tabular-nums text-foreground">
          {formatMoney(summary.vendorCollectedThisMonthDt)}
        </span>{" "}
        — les abonnements que les cabinets nous ont réglés, jamais le chiffre d&apos;affaires des cabinets
        eux-mêmes.
      </p>
    </section>
  );
}

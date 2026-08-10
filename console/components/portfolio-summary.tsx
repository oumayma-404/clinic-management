import Link from "next/link";

import type { PlatformSummary } from "@/lib/api/platform";
import { formatCount } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * The strip above the portfolio (`platform-console` AC-2.7).
 *
 * ⚠️ **Every figure is a link to the list it counted**, the same rule the clinic dashboard follows: a number
 * nobody can drill into is a number nobody can check, and the vendor's next action after reading « 4 dormants »
 * is always « lesquels ? ». A figure with no destination is rendered as plain text rather than as a dead link —
 * a control that does nothing is worse than no control (device contract § 0).
 *
 * ⚠️ **The five subscription counts are absent, not zero.** Until `features/clinic-subscription/` ships there is
 * nothing behind them, and « Expirés 0 » is a claim that no cabinet has lapsed — which the console has no way of
 * knowing. The strip states that gap once, in words, instead.
 */
export function PortfolioSummary({ summary }: { summary: PlatformSummary }) {
  const figures: Array<{ label: string; value: number; href?: string; tone?: "warning" }> = [
    { label: "Cabinets", value: summary.clinics, href: "/cabinets" },
    { label: "Dormants (30 j)", value: summary.dormant, href: "/cabinets?dormant=true", tone: "warning" },
  ];

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

      {!summary.subscriptionDataAvailable ? (
        <p className="text-sm text-muted-foreground" role="note">
          Abonnements, revenus de l&apos;éditeur et états (essai, expiré, suspendu) ne sont pas encore disponibles
          ici. Les chiffres ci-dessus portent uniquement sur l&apos;activité réelle des cabinets.
        </p>
      ) : null}
    </section>
  );
}

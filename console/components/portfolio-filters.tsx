"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { SlidersHorizontal, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { DEFAULT_PORTFOLIO_SORT, portfolioSearchParams, type PortfolioQuery } from "@/lib/api/platform";

/** « Création » first, because it is the default — the order the list actually arrives in with nothing chosen. */
const SORTS: Array<{ value: string; label: string }> = [
  { value: "createdAt", label: "Création" },
  { value: "activity", label: "Activité" },
  { value: "endsOn", label: "Date de fin" },
  { value: "name", label: "Nom" },
];

/** AC-2.3's entitlement filters. Every one is a SQL predicate over the whole portfolio, never over the page. */
const STATES: Array<{ value: string; label: string }> = [
  { value: "trial", label: "En essai" },
  { value: "expiringSoon", label: "Expire sous 14 j" },
  { value: "expired", label: "Expirés" },
  { value: "suspended", label: "Suspendus" },
];

/**
 * The portfolio's filters (`platform-console` AC-2.3–AC-2.5).
 *
 * ⚠️ **Every filter is URL state**, which is what lets the page stay a server component with the session token
 * never reaching the browser — the arrangement Part 1 built the whole app around. It also makes a filtered view
 * shareable and the back button meaningful, and it is why the active-filter chips below can be honest: they read
 * the same query the server did.
 *
 * ⚠️ **One control set, two presentations.** Above `lg:` the controls sit inline; below it they move into a
 * bottom sheet behind a « Filtres » button, because at 320 px a search box, a toggle and four sort options in a
 * row are either unreadable or push the table off screen. The sheet is a real dialog (focus trapped, `Escape`
 * closes) rather than a disclosure, and the active filters stay visible **outside** it as removable chips — so a
 * narrowed list can never look like an empty portfolio, which is the EC-12 confusion in miniature.
 *
 * ⚠️ **Every state filter narrows the whole portfolio, not the page.** That is a property of the endpoint (AC-2.4a)
 * and the reason it is stated in the sheet's own description: « 4 expirés » in the strip and the list this opens
 * are the same set, counted by the same predicate.
 */
export function PortfolioFilters({
  query,
  messagingNearThresholdPercent,
}: {
  query: PortfolioQuery;
  /**
   * The server's own « presque épuisé » threshold, as a percentage consumed.
   *
   * ⚠️ **Passed in, never retyped.** The chip's label is `100 - this`, so the filter's SQL predicate and the words on
   * the button are one figure — two spellings of a threshold is how a filter and its own label come to disagree with
   * neither looking wrong on its own, and the vendor simply learns not to trust the number.
   */
  messagingNearThresholdPercent: number;
}) {
  const router = useRouter();
  const [open, setOpen] = useState(false);

  // AC-8.2's two forfait filters. Built here rather than as a module constant because the « presque » label is derived
  // from the server's threshold, which arrives per request.
  const messagingFilters: Array<{ value: string; label: string }> = [
    { value: "exhausted", label: "Forfait épuisé" },
    { value: "near", label: `Forfait à moins de ${100 - messagingNearThresholdPercent} %` },
  ];

  function apply(next: PortfolioQuery) {
    // Page is deliberately dropped on every change: « page 4 » of the old filter is meaningless under the new
    // one, and landing on an empty page reads as « aucun cabinet » rather than as « you were on page 4 ».
    const params = portfolioSearchParams({ ...query, ...next, page: undefined });
    const suffix = params.toString();
    router.push(suffix ? `/cabinets?${suffix}` : "/cabinets");
    setOpen(false);
  }

  const controls = (
    <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:gap-3">
      <form
        className="flex flex-col gap-1.5 lg:w-72"
        onSubmit={(event) => {
          event.preventDefault();
          const value = new FormData(event.currentTarget).get("q");
          apply({ q: typeof value === "string" ? value.trim() : "" });
        }}
      >
        <Label htmlFor="portfolio-search">Rechercher</Label>
        {/* `type="search"` and not `text`: it gets the on-screen keyboard's search key and a native clear
            control on iOS, both of which this box would otherwise have to reinvent. */}
        <Input
          id="portfolio-search"
          name="q"
          type="search"
          defaultValue={query.q ?? ""}
          placeholder="Nom, ville ou e-mail de l'administrateur"
          autoComplete="off"
        />
      </form>

      <fieldset className="flex flex-col gap-1.5">
        <legend className="mb-1.5 text-sm font-medium">Trier par</legend>
        <div className="flex flex-wrap gap-2">
          {SORTS.map((sort) => {
            const active = (query.sort ?? DEFAULT_PORTFOLIO_SORT) === sort.value;
            return (
              <Button
                key={sort.value}
                type="button"
                variant={active ? "default" : "outline"}
                aria-pressed={active}
                onClick={() => apply({ sort: sort.value })}
              >
                {sort.label}
              </Button>
            );
          })}
        </div>
      </fieldset>

      <fieldset className="flex flex-col gap-1.5">
        <legend className="mb-1.5 text-sm font-medium">Abonnement</legend>
        <div className="flex flex-wrap gap-2">
          {STATES.map((state) => {
            const active = query.state === state.value;
            return (
              <Button
                key={state.value}
                type="button"
                variant={active ? "default" : "outline"}
                aria-pressed={active}
                // Tapping the active one clears it: without that the only way out of a state filter is the chip,
                // which is off screen while the sheet is open on a phone.
                onClick={() => apply({ state: active ? "" : state.value })}
              >
                {state.label}
              </Button>
            );
          })}
        </div>
      </fieldset>

      <fieldset className="flex flex-col gap-1.5">
        <legend className="mb-1.5 text-sm font-medium">Rappels WhatsApp</legend>
        <div className="flex flex-wrap gap-2">
          {messagingFilters.map((filter) => {
            const active = query.messaging === filter.value;
            return (
              <Button
                key={filter.value}
                type="button"
                variant={active ? "default" : "outline"}
                aria-pressed={active}
                // Tapping the active one clears it, as the state filters do: on a phone the chip that would otherwise
                // clear it is off screen while this sheet is open.
                onClick={() => apply({ messaging: active ? "" : filter.value })}
              >
                {filter.label}
              </Button>
            );
          })}
        </div>
        {/* AC-8.3 said where the choice is made: a cabinet nothing is counting for matches neither term, and a vendor
            who expected it in « épuisé » would otherwise conclude the filter is broken. */}
        <p className="text-xs text-muted-foreground">
          Un cabinet dont le mois n&apos;est pas mesuré n&apos;apparaît dans aucun des deux : ce n&apos;est pas une
          limite atteinte, c&apos;est notre comptage qui manque.
        </p>
      </fieldset>

      <Button
        type="button"
        variant={query.dormant ? "default" : "outline"}
        aria-pressed={Boolean(query.dormant)}
        onClick={() => apply({ dormant: !query.dormant })}
      >
        Dormants (30 j)
      </Button>
    </div>
  );

  const activeChips = [
    query.q ? { key: "q", label: `« ${query.q} »`, clear: { q: "" } as PortfolioQuery } : null,
    query.state
      ? {
          key: "state",
          label: STATES.find((s) => s.value === query.state)?.label ?? query.state,
          clear: { state: "" } as PortfolioQuery,
        }
      : null,
    query.messaging
      ? {
          key: "messaging",
          label: messagingFilters.find((f) => f.value === query.messaging)?.label ?? query.messaging,
          clear: { messaging: "" } as PortfolioQuery,
        }
      : null,
    query.dormant ? { key: "dormant", label: "Dormants (30 j)", clear: { dormant: false } as PortfolioQuery } : null,
  ].filter(Boolean) as Array<{ key: string; label: string; clear: PortfolioQuery }>;

  return (
    <div className="space-y-3">
      <div className="hidden lg:block">{controls}</div>

      <div className="lg:hidden">
        <Sheet open={open} onOpenChange={setOpen}>
          <SheetTrigger asChild>
            <Button type="button" variant="outline" className="w-full justify-center gap-2">
              <SlidersHorizontal className="size-4" aria-hidden="true" />
              Filtres et tri
            </Button>
          </SheetTrigger>
          <SheetContent side="bottom">
            <SheetHeader>
              <SheetTitle>Filtres et tri</SheetTitle>
              <SheetDescription>Le portefeuille entier est filtré, pas seulement la page affichée.</SheetDescription>
            </SheetHeader>
            <div className="px-4">{controls}</div>
            <SheetFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() =>
                  apply({ q: "", dormant: false, state: "", messaging: "", sort: DEFAULT_PORTFOLIO_SORT })
                }
              >
                Tout réinitialiser
              </Button>
            </SheetFooter>
          </SheetContent>
        </Sheet>
      </div>

      {activeChips.length > 0 ? (
        // Outside the sheet on purpose: a filter whose only trace is inside a closed panel is how a narrowed
        // list gets mistaken for an empty portfolio.
        <ul className="flex flex-wrap gap-2" aria-label="Filtres actifs">
          {activeChips.map((chip) => (
            <li key={chip.key}>
              <Button
                type="button"
                variant="secondary"
                className="gap-1.5"
                onClick={() => apply(chip.clear)}
                aria-label={`Retirer le filtre ${chip.label}`}
              >
                {chip.label}
                <X className="size-3.5" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

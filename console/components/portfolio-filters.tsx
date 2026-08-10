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
import { portfolioSearchParams, type PortfolioQuery } from "@/lib/api/platform";

const SORTS: Array<{ value: string; label: string }> = [
  { value: "name", label: "Nom" },
  { value: "activity", label: "Activité" },
  { value: "createdAt", label: "Création" },
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
 * bottom sheet behind a « Filtres » button, because at 320 px a search box, a toggle and three sort options in a
 * row are either unreadable or push the table off screen. The sheet is a real dialog (focus trapped, `Escape`
 * closes) rather than a disclosure, and the active filters stay visible **outside** it as removable chips — so a
 * narrowed list can never look like an empty portfolio, which is the EC-12 confusion in miniature.
 *
 * ⚠️ **« Par date de fin » is not offered.** AC-2.4 asks for it, and it is a property of the subscription, which
 * this console cannot see yet — an option that silently sorted by something else would be a screen quietly
 * answering a different question. It arrives with the data behind it.
 */
export function PortfolioFilters({ query }: { query: PortfolioQuery }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);

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
            const active = (query.sort ?? "name") === sort.value;
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
              <Button type="button" variant="outline" onClick={() => apply({ q: "", dormant: false, sort: "name" })}>
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

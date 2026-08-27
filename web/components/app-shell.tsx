import type React from "react"
import { cn } from "@/lib/utils"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
import { BottomNav } from "@/components/bottom-nav"
import { SubscriptionBanner } from "@/components/subscription/subscription-banner"

/**
 * The app's one page shell: rail + header + `<main>`.
 *
 * It replaces 28 hand-copied instances of the same four-element scaffold across 24 page files. Those copies had
 * drifted into three different gutters (`p-4 md:p-6`, a flat `p-4`, and none at all on /settings and /users),
 * four overflow variants, and six content widths — differences nobody chose, which is what made "make the app
 * work on a phone" a 28-file edit instead of a one-file edit.
 *
 * ⚠️ Deliberately NOT a client component and deliberately NOT rendering `ClinicGuard`.
 *
 *   - No `"use client"`: `app/documents/[type]/page.tsx` is an async server component (`await params`), and a
 *     client boundary here would force it to stop being one. `DashboardSidebar`/`DashboardHeader` carry their
 *     own `"use client"`, so the boundary starts where it actually needs to.
 *   - No `ClinicGuard`: four shells render *outside* the guard on purpose, so a patient's loading skeleton and
 *     its « introuvable » state are visible instead of the guard's own spinner. Pages keep the guard exactly
 *     where they put it (`app/treatment-plans/[id]/page.tsx` documents the reasoning).
 */

/** The content widths that survived consolidation. Each is a real decision, not an accident. */
const WIDTHS = {
  /** The default: every list and detail page. */
  "7xl": "max-w-7xl",
  /** /creances — a four-column table that looks abandoned at full width. */
  "5xl": "max-w-5xl",
  /** /mon-profil — a single form column. */
  "3xl": "max-w-3xl",
  /** /appointments — the calendar earns more than 7xl and reads as cramped below it. */
  wide: "max-w-[1400px]",
  /** No wrapper at all: the page owns its own layout end to end. */
  none: "",
} as const

export type AppShellWidth = keyof typeof WIDTHS

interface AppShellProps {
  children: React.ReactNode
  /** Content width. Defaults to `7xl`, which is what 15 of the pages already used. */
  width?: AppShellWidth
  /** Page gutter. `false` only for a page that paints its own edge-to-edge surface. */
  gutter?: boolean
  /**
   * Replaces `<main>`'s default `overflow-y-auto` (it does not merge with it — a page that scrolls its own
   * inner region must be able to turn the page scroller off, and `overflow-hidden` alongside `overflow-y-auto`
   * leaves the y-axis still scrolling). Two legitimate users: /appointments, whose calendar scrolls its own
   * grid and must not also scroll the page, and /documents/[type], which needs `<main>` to be a flex column
   * so its `flex-1` branches stretch.
   */
  mainClassName?: string
  /** Extra classes on the content wrapper — spacing that belongs to the page, not the shell. */
  contentClassName?: string
}

export function AppShell({
  children,
  width = "7xl",
  gutter = true,
  mainClassName,
  contentClassName,
}: AppShellProps) {
  const widthClass = WIDTHS[width]

  return (
    /*
     * `h-dvh`, not `h-screen`. `100vh` on iOS Safari is the *large* viewport — the one that assumes the URL bar
     * is hidden — so the last ~60px of every page sat under it and could not be scrolled to. `dvh` tracks the
     * viewport that is actually visible. The rail's own height changed with it, in the same commit: the two
     * disagreeing is what grows a second scrollbar (see the note in `dashboard-sidebar.tsx`).
     */
    <div className="flex h-dvh bg-background">
      {/*
        A skip-link is the only way a keyboard user reaches page content without tabbing all 15-19 nav rows.
        `sr-only focus:not-sr-only` keeps it invisible until focused, which is the first Tab stop on every page.
      */}
      <a
        href="#contenu-principal"
        className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-primary focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:text-primary-foreground"
      >
        Aller au contenu
      </a>

      <DashboardSidebar />

      <div className="flex flex-1 flex-col overflow-hidden">
        {/*
          `clinic-subscription` AC-3.1 — the one strip that appears on every screen of the app. A flex sibling of
          `<main>`, exactly like `BottomNav` below, so `<main className="flex-1 …">` shrinks around it and no page
          needs to know it exists. It renders `null` for every clinic that is up to date, and on every deployment
          that does not work by subscription.

          ⚠️ Here rather than in `app/layout.tsx`: this div's parent is `h-dvh`, so a strip *above* the shell would
          push the document past the viewport — the page would scroll as a whole and the bottom bar would go with
          it. Mounting it here also makes « absent on /login and /signup » structural, since those six routes are
          precisely the ones that render no shell.
        */}
        <SubscriptionBanner />
        <DashboardHeader />
        {/*
          `animate-page-in` — one short fade per navigation.

          The shell remounts on every route change, so this runs once per navigation and makes the page
          visibly *arrive* rather than being swapped. It is 200 ms and opacity-only, and both of those are
          constraints rather than preferences:

            • **200 ms**, because a navigation happens dozens of times a day. At that frequency an animation
              has to be nearly subliminal or absent — a longer, showier entrance would be felt as latency by
              the third time a dentist opened the agenda.

            • ⚠️ **Opacity only, never a transform.** A `transform` on an ancestor makes it the containing
              block for every `position: fixed` descendant, and `<main>` has several — the agenda's « Nouveau
              rendez-vous » action bar and the AI launcher among them. A 4 px rise here would drag those
              fixed elements along for the duration and settle them with a visible jump. `opacity` creates a
              stacking context but *not* a containing block, so it is the one property that is safe here.

          `prefers-reduced-motion` collapses this through the base layer, so it needs no guard.
        */}
        <main
          id="contenu-principal"
          className={cn(
            // ⚠️ `relative` is load-bearing, not decoration: `overflow-y-auto` does NOT clip an `absolute`
            // descendant whose containing block is outside it, and Tailwind's `sr-only` IS `absolute`.
            // `page-canvas` is the page ground's grain — a fine achromatic dot lattice, see its note in
            // `globals.css`. It lives on `<main>` rather than on `<body>` so the rail keeps its own `--sidebar`
            // surface and the header stays flat: the grain marks the CONTENT canvas, which is the thing the
            // cards need to lift off.
            "animate-page-in page-canvas relative flex-1",
            mainClassName ?? "overflow-y-auto",
            /*
             * ⚠️ **No `pb-20` runway any more, because nothing floats over `<main>` at any width.** It was
             * scroll clearance for a `fixed` FAB, which permanently occupies the last ~72 px of the content
             * once a page is scrolled to the end — the last table row's actions and the pager sit under it
             * and cannot be tapped. Two deletions removed every such element: the AI assistant's launcher
             * (the only one that rendered at every width) and then `/appointments`' « Nouveau RDV », which
             * moved into the agenda header's own date row. On a phone that runway was 64 px past `p-4` on
             * every page in the app, spent on a collision that can no longer happen.
             *
             * Re-add it — `coarse:pb-20` for a phone-only FAB, unprefixed for one that renders at every
             * width — the day something floats over `<main>` again.
             */
            gutter && "p-4 md:p-6",
          )}
        >
          {widthClass || contentClassName ? (
            <div className={cn("mx-auto", widthClass, contentClassName)}>{children}</div>
          ) : (
            children
          )}
        </main>

        {/*
          A flex sibling of `<main>`, deliberately — see the note in `bottom-nav.tsx`. Because it participates
          in the column's layout, `<main className="flex-1 …">` shrinks around it automatically and no page
          needs to know the bar exists. It renders itself away at `md:` and up.
        */}
        <BottomNav />
      </div>
    </div>
  )
}

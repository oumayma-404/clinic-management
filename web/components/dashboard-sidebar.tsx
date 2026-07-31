"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { cn } from "@/lib/utils"
// `Stethoscope` is the brand mark in `brandHeader`, not a nav icon — it stays here after the nav data moved out.
import { ChevronLeft, ChevronRight, Stethoscope } from "lucide-react"
import { buildNavSections, type NavItem, type NavSection } from "@/lib/nav"
import { useSidebar } from "@/contexts/sidebar-context"
import { useSession } from "@/lib/auth/session"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { PRODUCT_NAME } from "@/lib/brand"
import { Button } from "@/components/ui/button"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet"

// The nav model (sections, role gating, HIDDEN_PATHS) lives in `lib/nav.ts` — P2's bottom bar renders the same
// destinations, and a second copy is how the two drift apart on exactly the device the bar exists for.

export function DashboardSidebar() {
  const pathname = usePathname()
  const { isCollapsed, toggleSidebar, isMobileOpen, setMobileOpen } = useSidebar()
  const { user } = useSession()
  /*
   * The clinic's saved name for the brand line; no redirect (ClinicGuard owns that).
   *
   * The « Horaires d'ouverture » block that used to sit in the footer is gone. It cost ~110px of a 100vh column
   * permanently, and with nineteen destinations the nav was already overflowing — so reference information nobody
   * navigates by was pushing navigation off the screen. The hours are still shown, and editable, in
   * Paramètres → Horaires d'ouverture, which is also where they are changed.
   */
  const { status } = useClinicAccess(false)
  // Chrome brand: show the clinic's own saved name; fall back to the product name so the header is
  // never blank/"undefined" on first-run/setup before a clinic name exists (spec Edge Cases).
  const brandName = status?.clinic?.name?.trim() || PRODUCT_NAME

  const isAdmin = user?.role === "admin"

  const sections: NavSection[] = buildNavSections(isAdmin)

  // `collapsed` is passed rather than read from context: inside the mobile drawer the rail is always
  // expanded (there is room, and a phone has no hover for the collapsed tooltips), while the desktop rail
  // honours the persisted preference. One renderer, two callers — AC-P3.18.
  const renderItem = (item: NavItem, collapsed: boolean) => {
    const isActive = pathname === item.href
    const linkContent = (
      <Link
        href={item.href}
        className={cn(
          // py-1.5 → a 32px row (the same height as a `size="sm"` control), not py-2.5's 40px. With nineteen
          // destinations that four pixels each is 76px of nav, and the nav is what was overflowing.
          //
          // On a coarse pointer the row grows to clear 44px (AC-10) — a stacked list, so it grows its own
          // height rather than overlaying a hit area that would overlap the row above. The density argument
          // above is a DESKTOP one: it was about a 19-item nav overflowing a laptop's 100vh, and the drawer a
          // finger uses scrolls anyway.
          "flex items-center gap-3 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors coarse:py-3",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-card",
          isActive
            ? "bg-accent text-accent-foreground"
            : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
          collapsed && "justify-center"
        )}
        aria-current={isActive ? "page" : undefined}
      >
        <item.icon className="h-5 w-5 shrink-0" />
        {collapsed ? <span className="sr-only">{item.name}</span> : <span className="truncate">{item.name}</span>}
      </Link>
    )

    if (collapsed) {
      return (
        <Tooltip key={item.href} delayDuration={0}>
          <TooltipTrigger asChild>{linkContent}</TooltipTrigger>
          <TooltipContent side="right">
            <p>{item.name}</p>
          </TooltipContent>
        </Tooltip>
      )
    }

    return <div key={item.href}>{linkContent}</div>
  }

  const brandHeader = (collapsed: boolean) => (
    <div className="flex h-16 items-center border-b border-border px-4">
      <div className="flex items-center gap-2 flex-1 min-w-0">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary shrink-0">
          <Stethoscope className="h-5 w-5 text-primary-foreground" />
        </div>
        {!collapsed && (
          <span className="text-lg font-semibold text-foreground truncate">{brandName}</span>
        )}
      </div>
    </div>
  )

  // Navigation — grouped sections. Section titles hide when collapsed (icon-only rail).
  //
  // `min-h-0` is load-bearing, not tidying. A flex item defaults to `min-height: auto`, which refuses to shrink
  // below its content — so `flex-1 overflow-y-auto` alone never scrolls: the nav just grows to fit all ~21 items
  // and pushes its container past the viewport instead. Both the desktop rail and the mobile drawer rely on it.
  // `label` distinguishes the two landmarks this renders. A screen-reader rotator lists every <nav> on the page,
  // and two both called « Navigation principale » are indistinguishable in that list — the user has to enter one
  // to find out which it is. Only one is exposed at a time today (the rail is `display:none` below `md:`, the
  // drawer unmounted above it), but P2 adds a third nav that IS concurrent with the rail.
  const navigation = (collapsed: boolean, label: string) => (
    /*
     * `scrollbar-thin` matters on a short viewport. The nav is bounded, so on a 13" laptop nineteen destinations
     * still overflow and it still scrolls — but a full-width scrollbar sitting beside `<main>`'s reads as a second
     * *page* scrollbar, which is what made the app look broken. A thin overlay track reads as "this panel has
     * more", which is what it means.
     */
    <nav className="scrollbar-thin min-h-0 flex-1 overflow-y-auto px-3 py-3" aria-label={label}>
      <TooltipProvider>
        {sections.map((section) => (
          <div key={section.title} className="space-y-0.5 pb-1">
            {!collapsed && (
              <p className="px-3 pb-1 pt-2 text-xs font-semibold uppercase tracking-wider text-muted-foreground/70">
                {section.title}
              </p>
            )}
            {section.items.map((item) => renderItem(item, collapsed))}
          </div>
        ))}
      </TooltipProvider>
    </nav>
  )

  return (
    <>
      {/* Desktop rail — unchanged at `md:` and above, hidden below it (AC-P3.12). */}
      {/*
        `h-screen overflow-hidden` is what keeps the whole app from growing a second scrollbar.

        The rail is a flex item of the page shell (`flex h-screen`), and stretch alone does NOT bound it: a flex
        item's automatic minimum size means it still refuses to shrink below its content. With ~21 nav entries plus
        the hours footer and the collapse toggle that content exceeds 100 vh on a laptop, so the rail overflowed the
        shell — which carries no `overflow-hidden` — and stretched the document instead. The result was an outer
        scrollbar on every page and a tall dead band below the content, on top of `<main>`'s own inner scrollbar.
        Bounding the rail here is what finally lets the nav's `overflow-y-auto` do its job.
      */}
      <aside
        className={cn(
          // `h-dvh` tracks the page shell's own height (`AppShell`); the two MUST agree or the rail overflows
          // the shell and the document grows a second scrollbar — which is the whole reason for the note above.
          "hidden md:flex h-dvh flex-col overflow-hidden border-r border-border bg-card transition-all duration-300 relative",
          isCollapsed ? "w-16" : "w-64"
        )}
      >
        {brandHeader(isCollapsed)}
        {navigation(isCollapsed, "Navigation principale")}

        {/* Toggle Button */}
        <div className="border-t border-border p-2">
          <Button
            variant="ghost"
            size="sm"
            onClick={toggleSidebar}
            className="w-full justify-center"
            aria-label={isCollapsed ? "Développer la barre latérale" : "Réduire la barre latérale"}
          >
            {isCollapsed ? (
              <ChevronRight className="h-4 w-4" />
            ) : (
              <ChevronLeft className="h-4 w-4" />
            )}
          </Button>
        </div>
      </aside>

      {/* Mobile drawer — a shadcn Sheet over the already-installed Radix dialog, so Escape, the overlay
          click, focus trapping and focus restore are the primitive's, not hand-rolled (AC-P3.13/3.44).
          Opened from the header control; closed on navigation by the provider. */}
      <Sheet open={isMobileOpen} onOpenChange={setMobileOpen}>
        {/* No `aria-label` here: it would override `SheetTitle` as the dialog's accessible name AND repeat the
            name of the <nav> inside it, so the drawer and its own contents announced identically. */}
        <SheetContent side="left" className="w-72 max-w-[85vw] p-0 md:hidden">
          {/* Radix requires a title/description for the dialog's accessible name; the rail shows its own
              brand header, so these are screen-reader only. */}
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <SheetDescription className="sr-only">
            Accédez aux différentes sections de l&apos;application.
          </SheetDescription>
          {/* Same bargain as the rail: bound the column so the nav's `overflow-y-auto` (with its `min-h-0`) is what
              absorbs a long list, rather than the brand header being pushed out of the drawer. */}
          <div className="flex h-full flex-col overflow-hidden">
            {brandHeader(false)}
            {navigation(false, "Navigation du menu")}
          </div>
        </SheetContent>
      </Sheet>
    </>
  )
}

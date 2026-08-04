"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { MoreHorizontal } from "lucide-react"
import { cn } from "@/lib/utils"
import { useSidebar } from "@/contexts/sidebar-context"
import { baseSections, isNavItemVisible, type NavItem } from "@/lib/nav"
import { useSession } from "@/lib/auth/session"

/**
 * The phone's primary navigation (AC-7).
 *
 * Below `md:` the rail is a drawer, and the drawer's only opener used to be a hamburger in the top-LEFT corner —
 * the hardest point on a phone to reach with the thumb holding it. Every navigation was two taps, and the first
 * of them was the awkward one. This puts the four screens the clinic actually opens all day on the thumb, and
 * leaves everything else one tap behind « Plus ».
 *
 * ⚠️ It is a FLEX SIBLING of `<main>`, not `fixed`. `<main className="flex-1 …">` then shrinks around it for
 * free, which is what a landscape phone needs — ~250px of content height once the header and the home indicator
 * are taken, and a fixed bar would overlay a quarter of that (EC-3). It also means the bar needs no z-index and
 * no scroll compensation; only genuinely-fixed things (the AI panel, toasts, sheets) consume `--bottom-inset`.
 */

/**
 * Four destinations, read from the same source the rail and the drawer read (`lib/nav.ts`) — a hand-written
 * second list is how the bar ends up missing a screen someone added to the rail.
 *
 * These four are « Quotidien » minus « RDV récurrents »: the dashboard, the agenda, the waiting room and the
 * patient list are what a clinic opens all day. Recurring series is a scheduling task done occasionally at a
 * desk, so it lives behind « Plus » with everything else.
 */
const BAR_HREFS = ["/", "/appointments", "/waiting-list", "/patients"] as const

const barItems: NavItem[] = BAR_HREFS.map((href) => {
  const item = baseSections.flatMap((s) => s.items).find((i) => i.href === href)
  // A throw, not a filter: silently rendering three tabs because a href was renamed is precisely the kind of
  // quiet degradation this bar exists to prevent, and it would only ever be noticed on a phone.
  if (!item) throw new Error(`bottom-nav: no nav item for ${href}`)
  return item
})

/** Short labels — « Tableau de bord » does not fit a fifth of a 390px screen. */
const SHORT_LABEL: Record<string, string> = {
  "/": "Accueil",
  "/appointments": "Agenda",
  "/waiting-list": "Salle",
  "/patients": "Patients",
}

export function BottomNav() {
  const pathname = usePathname()
  /*
   * I3: a secretary's bar drops « Accueil ».
   *
   * `GET /api/dashboard` is `AdminOrDoctor` since I1, so for reception that first tab was a permanent 403 — and
   * on a phone it is the *leftmost, thumb-nearest* control on every screen, which makes it the most-tapped dead
   * end in the product. Filtered through the same `isNavItemVisible` the rail and the drawer use, so the three
   * navigations cannot disagree about what a role can reach.
   *
   * The lookup below still spans the FULL `baseSections`, deliberately: its throw is a guard against a renamed
   * href, and narrowing the source first would turn a real breakage into a silently shorter bar.
   */
  const { user } = useSession()
  const items = barItems.filter((item) => isNavItemVisible(item.href, user?.role))
  // Reuses the drawer's own state — no third piece of sidebar state, so AC-P3.18 still holds: nothing a phone
  // session does can overwrite the persisted desktop rail preference.
  const { setMobileOpen } = useSidebar()

  const isActive = (href: string) => (href === "/" ? pathname === "/" : pathname.startsWith(href))

  return (
    <nav
      aria-label="Navigation rapide"
      className={cn(
        "flex shrink-0 items-stretch border-t border-border bg-card md:hidden",
        // The home indicator sits inside the browser's viewport on iOS, so the bar pads itself rather than
        // letting the OS draw over its last row of pixels.
        "pb-[env(safe-area-inset-bottom,0px)]",
        // Hidden while a full-screen sheet is open (AC-8): the sheet owns the screen, and a nav bar showing
        // through under it would sit over the sheet's own primary action.
        "[body[data-sheet-open]_&]:hidden"
      )}
    >
      {items.map((item) => {
        const active = isActive(item.href)
        return (
          <Link
            key={item.href}
            href={item.href}
            // `aria-current` is on the bar, not the drawer: below `md:` this is the navigation on screen, and
            // marking both would announce the same destination as current twice (AC-5).
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex flex-1 flex-col items-center justify-center gap-0.5 text-2xs font-medium transition-colors",
              "h-[var(--bottom-bar-h)]",
              active ? "text-primary" : "text-muted-foreground"
            )}
          >
            <item.icon className={cn("h-5 w-5", active && "text-primary")} aria-hidden="true" />
            <span className="leading-none">{SHORT_LABEL[item.href] ?? item.name}</span>
          </Link>
        )
      })}

      <button
        type="button"
        onClick={() => setMobileOpen(true)}
        aria-label="Ouvrir la navigation"
        className={cn(
          "flex flex-1 flex-col items-center justify-center gap-0.5 text-2xs font-medium text-muted-foreground",
          "h-[var(--bottom-bar-h)] transition-colors"
        )}
      >
        <MoreHorizontal className="h-5 w-5" aria-hidden="true" />
        <span className="leading-none">Plus</span>
      </button>
    </nav>
  )
}

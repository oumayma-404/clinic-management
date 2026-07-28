"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { cn } from "@/lib/utils"
import { Calendar, CalendarClock, Users, Settings, LayoutDashboard, Stethoscope, Package, FileCheck, ChevronLeft, ChevronRight, UserCog, Receipt, ClipboardList, Pill, ClipboardCheck, ScrollText, HandCoins, PhoneCall, Clock, FlaskConical, Wallet } from "lucide-react"
import type { LucideIcon } from "lucide-react"
import { useSidebar } from "@/contexts/sidebar-context"
import { useSession } from "@/lib/auth/session"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { DEFAULT_WORKING_HOURS, summarizeWorkingHours } from "@/lib/working-hours"
import { PRODUCT_NAME } from "@/lib/brand"
import { Button } from "@/components/ui/button"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { Sheet, SheetContent, SheetDescription, SheetTitle } from "@/components/ui/sheet"

type NavItem = { name: string; href: string; icon: LucideIcon }
type NavSection = { title: string; items: NavItem[] }

// Daily-use sections. Config/catalog screens live in a separate "Configuration" group (built below with
// role gating) so the everyday rail stays short. Mon profil moved to the header user menu; the read-only
// /records and global /files shortcuts were removed (the patient page owns that data).
const baseSections: NavSection[] = [
  {
    title: "Quotidien",
    items: [
      { name: "Tableau de bord", href: "/", icon: LayoutDashboard },
      { name: "Rendez-vous", href: "/appointments", icon: Calendar },
      { name: "RDV récurrents", href: "/recurring-series", icon: CalendarClock },
      { name: "Salle d'attente", href: "/waiting-list", icon: Clock },
      { name: "Patients", href: "/patients", icon: Users },
    ],
  },
  {
    title: "Clinique",
    items: [
      { name: "Documents", href: "/documents", icon: FileCheck },
      { name: "Plans / Devis", href: "/treatment-plans", icon: ClipboardCheck },
      { name: "Laboratoire", href: "/lab-orders", icon: FlaskConical },
    ],
  },
  {
    title: "Finances",
    items: [
      { name: "Factures", href: "/factures", icon: Receipt },
      { name: "Caisse", href: "/caisse", icon: Wallet },
      { name: "Créances", href: "/creances", icon: HandCoins },
    ],
  },
  {
    title: "Gestion",
    items: [
      { name: "Stock", href: "/stock", icon: Package },
      { name: "Relances", href: "/recalls", icon: PhoneCall },
    ],
  },
]

export function DashboardSidebar() {
  const pathname = usePathname()
  const { isCollapsed, toggleSidebar, isMobileOpen, setMobileOpen } = useSidebar()
  const { user } = useSession()
  // Working hours shown in the footer come from the clinic's saved settings (AC-7); no redirect (ClinicGuard
  // owns that). Falls back to the shared default when nothing is saved.
  const { status } = useClinicAccess(false)
  const workingHours = status?.clinic?.workingHours && status.clinic.workingHours.length > 0
    ? status.clinic.workingHours
    : DEFAULT_WORKING_HOURS
  const hoursSummary = summarizeWorkingHours(workingHours)
  // Chrome brand: show the clinic's own saved name; fall back to the product name so the header is
  // never blank/"undefined" on first-run/setup before a clinic name exists (spec Edge Cases).
  const brandName = status?.clinic?.name?.trim() || PRODUCT_NAME

  const isAdmin = user?.role === "admin"

  // Configuration group: procedure catalog + admin-only reference catalogs + clinic settings. CNAM /
  // médicaments / actes dentaires and Utilisateurs are all any-admin, in both modes.
  //
  // « Utilisateurs » used to carry an extra `mode === "local" &&` (AC-P2.28). Nothing else was mode-gated:
  // the page itself only checks `role === "admin"`, and `UsersController` (list / status / role) works
  // identically in Cloud — so a Cloud admin had no way to see who could reach their clinic's patient data, or
  // to revoke a departed colleague. The one genuinely Local-only action inside, « Réinitialiser le mot de
  // passe », is gated in `user-management.tsx` where it lives (AC-P2.29).
  const configItems: NavItem[] = [
    { name: "Types de procédures", href: "/procedure-types", icon: Stethoscope },
    ...(isAdmin
      ? [
          { name: "Nomenclature CNAM", href: "/cnam-nomenclature", icon: ClipboardList },
          { name: "Médicaments", href: "/medications", icon: Pill },
          { name: "Actes dentaires", href: "/dental-acts", icon: ScrollText },
        ]
      : []),
    ...(isAdmin ? [{ name: "Utilisateurs", href: "/users", icon: UserCog }] : []),
    { name: "Paramètres", href: "/settings", icon: Settings },
  ]

  const sections: NavSection[] = [...baseSections, { title: "Configuration", items: configItems }]

  // `collapsed` is passed rather than read from context: inside the mobile drawer the rail is always
  // expanded (there is room, and a phone has no hover for the collapsed tooltips), while the desktop rail
  // honours the persisted preference. One renderer, two callers — AC-P3.18.
  const renderItem = (item: NavItem, collapsed: boolean) => {
    const isActive = pathname === item.href
    const linkContent = (
      <Link
        href={item.href}
        className={cn(
          "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
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
  const navigation = (collapsed: boolean) => (
    <nav className="flex-1 overflow-y-auto p-4" aria-label="Navigation principale">
      <TooltipProvider>
        {sections.map((section) => (
          <div key={section.title} className="space-y-1 pb-2">
            {!collapsed && (
              <p className="px-3 pt-2 pb-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground/70">
                {section.title}
              </p>
            )}
            {section.items.map((item) => renderItem(item, collapsed))}
          </div>
        ))}
      </TooltipProvider>
    </nav>
  )

  // Footer — clinic hours from the saved settings (single source, AC-7).
  const hoursFooter = (
    <div className="border-t border-border p-4">
      <div className="text-xs text-muted-foreground">
        <p className="font-medium">Horaires d&apos;ouverture</p>
        {hoursSummary.map((line, i) => (
          <p key={i} className={i === 0 ? "mt-1" : ""}>
            {line}
          </p>
        ))}
      </div>
    </div>
  )

  return (
    <>
      {/* Desktop rail — unchanged at `md:` and above, hidden below it (AC-P3.12). */}
      <aside
        className={cn(
          "hidden md:flex flex-col border-r border-border bg-card transition-all duration-300 relative",
          isCollapsed ? "w-16" : "w-64"
        )}
      >
        {brandHeader(isCollapsed)}
        {navigation(isCollapsed)}
        {!isCollapsed && hoursFooter}

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
        <SheetContent
          side="left"
          className="w-72 max-w-[85vw] p-0 md:hidden"
          aria-label="Navigation principale"
        >
          {/* Radix requires a title/description for the dialog's accessible name; the rail shows its own
              brand header, so these are screen-reader only. */}
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <SheetDescription className="sr-only">
            Accédez aux différentes sections de l&apos;application.
          </SheetDescription>
          <div className="flex h-full flex-col">
            {brandHeader(false)}
            {navigation(false)}
            {hoursFooter}
          </div>
        </SheetContent>
      </Sheet>
    </>
  )
}

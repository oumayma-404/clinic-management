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
  const { isCollapsed, toggleSidebar } = useSidebar()
  const { user, mode } = useSession()
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
  // médicaments / actes dentaires are any-admin; Utilisateurs is local-mode admin only.
  const configItems: NavItem[] = [
    { name: "Types de procédures", href: "/procedure-types", icon: Stethoscope },
    ...(isAdmin
      ? [
          { name: "Nomenclature CNAM", href: "/cnam-nomenclature", icon: ClipboardList },
          { name: "Médicaments", href: "/medications", icon: Pill },
          { name: "Actes dentaires", href: "/dental-acts", icon: ScrollText },
        ]
      : []),
    ...(mode === "local" && isAdmin ? [{ name: "Utilisateurs", href: "/users", icon: UserCog }] : []),
    { name: "Paramètres", href: "/settings", icon: Settings },
  ]

  const sections: NavSection[] = [...baseSections, { title: "Configuration", items: configItems }]

  const renderItem = (item: NavItem) => {
    const isActive = pathname === item.href
    const linkContent = (
      <Link
        href={item.href}
        className={cn(
          "flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
          isActive
            ? "bg-accent text-accent-foreground"
            : "text-muted-foreground hover:bg-accent/50 hover:text-foreground",
          isCollapsed && "justify-center"
        )}
      >
        <item.icon className="h-5 w-5 shrink-0" />
        {!isCollapsed && <span className="truncate">{item.name}</span>}
      </Link>
    )

    if (isCollapsed) {
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

  return (
    <aside
      className={cn(
        "flex flex-col border-r border-border bg-card transition-all duration-300 relative",
        isCollapsed ? "w-16" : "w-64"
      )}
    >
      {/* Header */}
      <div className="flex h-16 items-center border-b border-border px-4">
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary shrink-0">
            <Stethoscope className="h-5 w-5 text-primary-foreground" />
          </div>
          {!isCollapsed && (
            <span className="text-lg font-semibold text-foreground truncate">{brandName}</span>
          )}
        </div>
      </div>

      {/* Navigation — grouped sections. Section titles hide when collapsed (icon-only rail). */}
      <nav className="flex-1 overflow-y-auto p-4">
        <TooltipProvider>
          {sections.map((section) => (
            <div key={section.title} className="space-y-1 pb-2">
              {!isCollapsed && (
                <p className="px-3 pt-2 pb-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground/70">
                  {section.title}
                </p>
              )}
              {section.items.map((item) => renderItem(item))}
            </div>
          ))}
        </TooltipProvider>
      </nav>

      {/* Footer — clinic hours from the saved settings (single source, AC-7). */}
      {!isCollapsed && (
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
      )}

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
  )
}

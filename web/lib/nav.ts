import {
  BellRing,
  Calendar,
  CalendarClock,
  ClipboardCheck,
  ClipboardList,
  Clock,
  FileCheck,
  FlaskConical,
  HandCoins,
  LayoutDashboard,
  Package,
  Pill,
  Receipt,
  ScrollText,
  Settings,
  Stethoscope,
  UserCog,
  Users,
  Wallet,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"

/**
 * The app's navigation model, in one place.
 *
 * This used to be module-private inside `dashboard-sidebar.tsx`, which was fine while the rail and its drawer
 * were the only two things that rendered it. P2 adds a third — the phone's bottom bar — and a nav list copied
 * into a second component is a nav list that drifts: a destination added to one and not the other is invisible
 * on exactly the device the bar exists for. `HIDDEN_PATHS` moved here for the same reason (it was private to
 * `ai-chat.tsx`, and the bottom bar needs the same answer to "is this a chrome-less route?").
 */

export type NavItem = { name: string; href: string; icon: LucideIcon }
export type NavSection = { title: string; items: NavItem[] }

// Daily-use sections. Config/catalog screens live in a separate "Configuration" group (built below with
// role gating) so the everyday rail stays short. Mon profil moved to the header user menu; the read-only
// /records and global /files shortcuts were removed (the patient page owns that data).
export const baseSections: NavSection[] = [
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
      // « Relances » (/recalls) is gone — the page was removed. The recall BACKEND is deliberately intact
      // (RecallController, Features/Recall, the due-list query), so the worklist can be given a new home later
      // without rebuilding it. Nothing here links to a route that no longer exists.
      { name: "Rappels", href: "/rappels", icon: BellRing },
    ],
  },
]

/**
 * Configuration group: procedure catalog + admin-only reference catalogs + clinic settings. CNAM /
 * médicaments / actes dentaires and Utilisateurs are all any-admin, in both modes.
 *
 * « Utilisateurs » used to carry an extra `mode === "local" &&` (AC-P2.28). Nothing else was mode-gated:
 * the page itself only checks `role === "admin"`, and `UsersController` (list / status / role) works
 * identically in Cloud — so a Cloud admin had no way to see who could reach their clinic's patient data, or
 * to revoke a departed colleague. The one genuinely Local-only action inside, « Réinitialiser le mot de
 * passe », is gated in `user-management.tsx` where it lives (AC-P2.29).
 */
export function buildConfigItems(isAdmin: boolean): NavItem[] {
  return [
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
}

/** Every destination, grouped — 15 for a practitioner, 19 for an admin. */
export function buildNavSections(isAdmin: boolean): NavSection[] {
  return [...baseSections, { title: "Configuration", items: buildConfigItems(isAdmin) }]
}

/**
 * Auth / onboarding routes that render no chrome. A path check is needed on top of the session check: on
 * /setup and /join a Cloud user IS authenticated (Auth0 session, no clinic yet), and /change-password is a
 * forced interstitial.
 */
export const HIDDEN_PATHS = ["/login", "/setup", "/join", "/change-password"]

/** True on a route that deliberately renders without the app chrome. */
export function isChromeLessPath(pathname: string | null): boolean {
  if (!pathname) return false
  return HIDDEN_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`))
}

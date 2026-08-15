import {
  ShieldCheck,
  BellRing,
  Calendar,
  ClipboardCheck,
  ClipboardList,
  Clock,
  CreditCard,
  FileCheck,
  FlaskConical,
  LayoutDashboard,
  Package,
  Pill,
  Receipt,
  ReceiptText,
  ScrollText,
  Settings,
  Stethoscope,
  Truck,
  UserCog,
  FileClock,
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
 * on exactly the device the bar exists for. `HIDDEN_PATHS` lives here for the same reason — several surfaces
 * need the same answer to "is this a chrome-less route?" (the bottom bar, and the subscription banner).
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
      // Deliberately NOT in SECRETARY_HIDDEN_HREFS: reception is who knows whether the patient came.
      { name: "À clôturer", href: "/a-cloturer", icon: ClipboardCheck },
      // « RDV récurrents » (/recurring-series) is gone — the screen was withdrawn. Its BACKEND and the screen
      // itself are deliberately intact (`app/recurring-series/page.tsx` keeps the component, unrouted).
      { name: "Salle d'attente", href: "/waiting-list", icon: Clock },
      { name: "Patients", href: "/patients", icon: Users },
    ],
  },
  {
    title: "Clinique",
    items: [
      { name: "Documents", href: "/documents", icon: FileCheck },
      { name: "Plans de traitement", href: "/treatment-plans", icon: ClipboardCheck },
      { name: "Laboratoire", href: "/lab-orders", icon: FlaskConical },
    ],
  },
  {
    title: "Finances",
    items: [
      { name: "Factures", href: "/factures", icon: Receipt },
      { name: "Caisse", href: "/caisse", icon: Wallet },
      { name: "Chèques", href: "/cheques", icon: ReceiptText },
      // « Créances » (/creances) is gone, on the same terms: the read, `ReceivablesTable` and the unrouted screen
      // all stay. The dashboard's « Créances » figure went with it, so nothing in the app links there.
    ],
  },
  {
    title: "Gestion",
    items: [
      { name: "Stock", href: "/stock", icon: Package },
      // Beside « Stock » because that is where a fournisseur is reached for — but it covers laboratoires de
      // prothèse and d'analyses too, which is why the label is not « Fournisseurs de stock ».
      { name: "Fournisseurs", href: "/fournisseurs", icon: Truck },
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
export function buildConfigItems(isAdmin: boolean, showSubscription = true): NavItem[] {
  return [
    { name: "Types de procédures", href: "/procedure-types", icon: Stethoscope },
    ...(isAdmin
      ? [
          { name: "Nomenclature CNAM", href: "/cnam-nomenclature", icon: ClipboardList },
          { name: "Médicaments", href: "/medications", icon: Pill },
          { name: "Actes dentaires", href: "/dental-acts", icon: ScrollText },
        ]
      : []),
    ...(isAdmin
      ? [
          { name: "Utilisateurs", href: "/users", icon: UserCog },
          // AC-19. The endpoint behind it is `AdminOnly` and the page carries its own `role === "admin"` gate;
          // this only stops reception being shown a door that does not open.
          { name: "Journal d'activité", href: "/journal", icon: FileClock },
        ]
      : []),
    { name: "Paramètres", href: "/settings", icon: Settings },
    // `clinic-subscription` AC-2.2 — **outside the `isAdmin` branch above, deliberately.** The person who meets
    // « Votre abonnement a expiré … » on a save is usually reception, not whoever pays, and she has to be able to
    // read why; hiding this row would point the refusal at a screen she cannot see (EC-10). It sits with the other
    // clinic-administration destinations but outside their admin-only grouping, exactly as the spec words it.
    // What stays admin-only is the payment *history* inside the page, not the page.
    //
    // ⚠️ Gated on the **deployment**, not on a role (AC-7.1/7.2, Part D): where nothing expires there is no
    // subscription to show, and the page itself says so. `showSubscription` defaults to `true` because the one
    // caller that must never lose the row is `lib/zones.ts`, which builds the route→icon map and needs every
    // destination that can render — including this one, on the deployments where it does.
    ...(showSubscription ? [{ name: "Abonnement", href: "/abonnement", icon: CreditCard }] : []),
    // `hosted-security-hardening` FR-1.5 — **outside the `isAdmin` branch, and unconditional across
    // deployments.** A doctor or a secretary may enrol a second factor voluntarily anywhere, and this is the
    // only screen where they can; gating it on the role would leave the two people most likely to want it with
    // no way to reach it, and gating it on the deployment would do the same on the two profiles that require
    // nothing of administrators. What the *deployment* decides is whether an admin may switch theirs off,
    // which the page states in words.
    { name: "Sécurité", href: "/securite", icon: ShieldCheck },
  ]
}

/**
 * Destinations a **secretary** does not get, because the API refuses them (feature
 * `adoption-qa-i-access-control-and-audit`, I1): « Tableau de bord » is gated `AdminOrDoctor` — its Argent
 * section *is* the clinic's revenue — and so are all three « Finances » screens.
 *
 * <p><b>This is presentation, not security.</b> The server is authoritative: `GET /api/dashboard`,
 * `GET /api/billing/caisse`, `/caisse/ledger`, `/billing/receivables` and `GET /api/invoices/revenue` all carry
 * `AdminOrDoctor`, and a secretary who hand-types the URL gets a 403 whatever this list says. Hiding the rail
 * entries exists so reception is not shown four doors that do not open — which was the state of the product
 * before I1: the rail shipped « Tableau de bord » and the whole « Finances » group to every role, and the pages
 * behind them contained no `role` reference at all.</p>
 *
 * <p>Matched on `href`, so a renamed *label* cannot silently un-gate a screen.</p>
 */
const SECRETARY_HIDDEN_HREFS: ReadonlySet<string> = new Set([
  "/",
  "/factures",
  "/caisse",
  // L8 slice B — the clinic's uncashed cheques are the same clinic-wide money read as la caisse's totals, and
  // `GET /api/billing/cheques` is `AdminOrDoctor`. A secretary recording a cheque payment on a patient's invoice
  // is unaffected: that endpoint stays deliberately open.
  "/cheques",
  // No "/creances": that row no longer exists, and an href this set can never be asked about is dead weight that
  // reads as a live gate.
])

/** True when this role must not see the clinic-wide money screens. The one place the comparison is written. */
export function hidesClinicWideMoney(role: string | null | undefined): boolean {
  return role === "secretary"
}

/**
 * True when this role satisfies the server's `AdminOrDoctor` policy — the one place that comparison is written
 * client-side, for the **actions** gated on it rather than the routes.
 *
 * <p>⚠️ A <b>positive</b> test, not `!hidesClinicWideMoney(role)`. The two are the same partition today, but they
 * answer different questions and would diverge the moment a fourth role existed — and the safe direction differs
 * too: an unknown or not-yet-loaded role must <i>hide</i> a bulk-write affordance (« Importer des patients » creates
 * records that cannot be merged afterwards), where the money list's job is to hide four doors that do not open.</p>
 *
 * <p><b>Presentation, not security</b>, like everything in this file: `POST /api/patients/import` carries
 * `AdminOrDoctor` and refuses a secretary whatever this returns.</p>
 */
export function isAdminOrDoctor(role: string | null | undefined): boolean {
  return role === "admin" || role === "doctor"
}

/** Is this destination reachable by that role? Shared by the rail, the drawer and the phone's bottom bar. */
export function isNavItemVisible(href: string, role: string | null | undefined): boolean {
  return !hidesClinicWideMoney(role) || !SECRETARY_HIDDEN_HREFS.has(href)
}

/**
 * Every destination this role can reach, grouped — 13 for a practitioner, 17 for an admin, 10 for a secretary.
 *
 * <p>Takes the **role**, not an `isAdmin` boolean: the admin/not-admin split alone cannot express « a secretary
 * sees less than a doctor », which is the whole distinction I1 turns on. A section whose every item is hidden is
 * dropped rather than rendered empty — « Finances » with no rows under it advertises exactly the capability the
 * gate exists to withhold.</p>
 */
export function buildNavSections(
  role: string | null | undefined,
  /** Whether this deployment works by subscription — see {@link buildConfigItems}. Defaults to showing the row. */
  showSubscription = true,
): NavSection[] {
  const visible = baseSections
    .map((section) => ({ ...section, items: section.items.filter((i) => isNavItemVisible(i.href, role)) }))
    .filter((section) => section.items.length > 0)

  return [
    ...visible,
    { title: "Configuration", items: buildConfigItems(role === "admin", showSubscription) },
  ]
}

/**
 * Auth / onboarding routes that render no chrome. A path check is needed on top of the session check: on
 * /setup and /join a Cloud user IS authenticated (Auth0 session, no clinic yet), and /change-password is a
 * forced interstitial.
 */
export const HIDDEN_PATHS = [
  "/login",
  "/setup",
  "/join",
  "/change-password",
  // `clinic-self-signup`'s two public routes, added by `clinic-subscription` Part D. They belong on this list on
  // their own merits — a visitor with no clinic and no session must be offered no app chrome at all — and the
  // subscription banner reads the same answer, so « no banner on /signup » needs no second list.
  // ⚠️ One entry, not two: `isChromeLessPath` matches a prefix, so `/signup` already covers `/signup/verifier`.
  "/signup",
]

/** True on a route that deliberately renders without the app chrome. */
export function isChromeLessPath(pathname: string | null): boolean {
  if (!pathname) return false
  return HIDDEN_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`))
}

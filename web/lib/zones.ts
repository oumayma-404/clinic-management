import {
  ShieldCheck, UserRound, Users, type LucideIcon } from "lucide-react"
import { buildNavSections } from "@/lib/nav"

/**
 * The app's five **zones**, and the one place their colour is decided.
 *
 * <p>The rail has always been grouped — « Quotidien », « Clinique », « Finances », « Gestion »,
 * « Configuration » — and `PageHeader` has always taken a `zone` string. Neither did anything visual: nineteen
 * identically-grey icons in a column, and a grey eyebrow above every page title. The grouping existed in the
 * markup and nowhere in the perception, which is most of why the app reads as one long grey list.</p>
 *
 * <p>A zone hue is <b>orientation, not decoration</b>, and that is the whole justification for adding colour to a
 * product whose design notes deliberately reserved the accent. It answers « where am I? » before a word is read —
 * the money screens are amber, the clinical screens violet, the everyday screens azure — and it answers it in the
 * rail and on the page with the <i>same</i> colour, so the two are visibly the same place.</p>
 *
 * <p><b>Where a zone hue is allowed to appear</b>, exhaustively — this list is the restraint:</p>
 * <ol>
 *   <li>the nav icon of the active row, and a 12 % wash behind it;</li>
 *   <li>the `PageHeader` eyebrow and its 6 px dot;</li>
 *   <li>the icon chip of a zone-scoped empty state.</li>
 * </ol>
 *
 * <p>It is never a background for content, never a button fill, and never a status. Status has its own family
 * (`ui/status-tone.ts`) on purpose: a zone says <i>where</i>, a status says <i>how it is going</i>, and a screen
 * where those two share a palette can express neither.</p>
 *
 * <p>« Configuration » is deliberately near-neutral. It is the one zone a clinic visits rarely, and giving it a
 * fifth competing hue would make the rail read as a paint chart rather than as four working areas plus settings.</p>
 */
export type ZoneKey = "daily" | "clinical" | "money" | "ops" | "config"

export interface Zone {
  key: ZoneKey
  /** The French section title. This is also the rail's group heading and `PageHeader`'s eyebrow text. */
  label: string
  /**
   * The Tailwind text colour for this zone's ink.
   *
   * <p>Written as complete class strings rather than composed from `text-zone-${key}`: Tailwind scans source for
   * literal class names, and an interpolated one is not generated at all — it silently renders as no colour,
   * which is the single most common way a themed system like this ships broken.</p>
   */
  text: string
  /** The 12 % wash, for an icon chip. Same literal-class rule as `text`. */
  wash: string
  /** Border at 25 %, for a chip that needs an edge against a tinted ground. */
  border: string
  /** Full-strength fill. The rail's 3 px active-row indicator, and nothing wider — this is ink, not a surface. */
  bg: string
}

export const ZONES: Record<ZoneKey, Zone> = {
  daily: {
    key: "daily",
    label: "Quotidien",
    text: "text-zone-daily",
    wash: "bg-zone-daily/12",
    border: "border-zone-daily/25",
    bg: "bg-zone-daily",
  },
  clinical: {
    key: "clinical",
    label: "Clinique",
    text: "text-zone-clinical",
    wash: "bg-zone-clinical/12",
    border: "border-zone-clinical/25",
    bg: "bg-zone-clinical",
  },
  money: {
    key: "money",
    label: "Finances",
    text: "text-zone-money",
    wash: "bg-zone-money/12",
    border: "border-zone-money/25",
    bg: "bg-zone-money",
  },
  ops: {
    key: "ops",
    label: "Gestion",
    text: "text-zone-ops",
    wash: "bg-zone-ops/12",
    border: "border-zone-ops/25",
    bg: "bg-zone-ops",
  },
  config: {
    key: "config",
    label: "Configuration",
    text: "text-zone-config",
    wash: "bg-zone-config/12",
    border: "border-zone-config/25",
    bg: "bg-zone-config",
  },
}

/**
 * Every route → its zone.
 *
 * <p>Keyed by the route's own prefix rather than derived from `nav.ts`, because the mapping must answer for routes
 * the rail does not list — `/patients/[id]`, `/treatment-plans/[id]`, `/documents/[type]` — and those are exactly
 * the pages a user is deepest inside and most needs orienting on.</p>
 *
 * <p>Ordered longest-prefix-first at lookup time, so `/patients/12/files` resolves through `/patients`.</p>
 */
const ROUTE_ZONES: Array<[string, ZoneKey]> = [
  ["/appointments", "daily"],
  // « À clôturer » is the agenda's other half — finishing the séances the agenda booked — so it shares its zone.
  ["/a-cloturer", "daily"],
  ["/recurring-series", "daily"],
  ["/waiting-list", "daily"],
  ["/patients", "daily"],
  ["/documents", "clinical"],
  ["/treatment-plans", "clinical"],
  ["/lab-orders", "clinical"],
  ["/factures", "money"],
  ["/caisse", "money"],
  ["/creances", "money"],
  ["/stock", "ops"],
  ["/rappels", "ops"],
  ["/procedure-types", "config"],
  ["/cnam-nomenclature", "config"],
  ["/medications", "config"],
  ["/dental-acts", "config"],
  ["/users", "config"],
  ["/settings", "config"],
  ["/mon-profil", "config"],
  // « Abonnement » is what the practice pays its *software vendor*, so it is deliberately `config` and not `money`:
  // the money zone is the clinic's own till, and FR-2 keeps the two apart everywhere else in the product too.
  ["/abonnement", "config"],
  // This account's own second factor — administration of oneself, not of the clinic's money or its records.
  ["/securite", "config"],
]

/**
 * The zone a path belongs to, or `daily` for the dashboard and anything unmapped.
 *
 * <p>Falling back to `daily` rather than to `undefined` is deliberate: an unmapped route still gets a coherent
 * rail, and a missing hue would read as a rendering fault rather than as "this page has no zone".</p>
 */
export function zoneForPath(pathname: string | null | undefined): Zone {
  if (!pathname) return ZONES.daily
  const match = ROUTE_ZONES.filter(([prefix]) => pathname === prefix || pathname.startsWith(`${prefix}/`)).sort(
    (a, b) => b[0].length - a[0].length,
  )[0]
  return match ? ZONES[match[1]] : ZONES.daily
}

/** The zone whose `label` matches a rail section title — the seam between `nav.ts`'s sections and this palette. */
export function zoneForSectionTitle(title: string): Zone {
  return Object.values(ZONES).find((z) => z.label === title) ?? ZONES.config
}

/** Convenience for a component that has a `ZoneKey` and wants an icon chip's full class string. */
export function zoneChipClass(zone: Zone): string {
  return `${zone.wash} ${zone.text}`
}

/**
 * A route's icon — **the same glyph the rail draws for it**.
 *
 * <p>Built from `buildNavSections(true)` rather than hand-listed, so a page header can never show one icon while
 * the rail shows another for the same destination. That pairing is the point: a non-technical user recognises a
 * shape long before they finish reading a French heading, and the recognition only pays if the shape they tapped
 * in the rail is the shape now at the top of the page.</p>
 *
 * <p>`"admin"` is passed to get the *widest* set (the parameter became the role in I1, so a secretary's narrower
 * nav does not silently drop icons from this map). This is a display lookup over a path that has already been
 * rendered — it grants nothing, and role gating stays where it is enforced (`nav.ts`, the pages, the API).</p>
 */
const NAV_ICONS: Array<[string, LucideIcon]> = buildNavSections("admin").flatMap((s) =>
  s.items.map((i) => [i.href, i.icon] as [string, LucideIcon]),
)

/**
 * Routes with no rail entry of their own. Each is a page a user genuinely lands on, and each would otherwise
 * fall through to its parent's icon or to none at all.
 */
const EXTRA_ICONS: Array<[string, LucideIcon]> = [
  ["/mon-profil", UserRound],
  ["/securite", ShieldCheck],
  ["/patients", Users],
]

export function navIconForPath(pathname: string | null | undefined): LucideIcon | undefined {
  if (!pathname) return undefined
  // Longest prefix wins, so `/patients/12/files` resolves through `/patients` and never through `/`.
  const match = [...NAV_ICONS, ...EXTRA_ICONS]
    .filter(([href]) => href !== "/" && (pathname === href || pathname.startsWith(`${href}/`)))
    .sort((a, b) => b[0].length - a[0].length)[0]
  return match?.[1]
}

export type { LucideIcon }

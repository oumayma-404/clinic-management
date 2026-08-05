# web/ — Clinic Management Frontend

Next.js 15 (App Router) frontend for the dental/medical clinic management system. Talks to a separate .NET API. **Auth is pluggable** (`AUTH_MODE`): **cloud** = Auth0; **local** = email+password backed by an HttpOnly session cookie (offline LAN installs). Consumers read a unified `useSession()` seam, not Auth0 directly. **French UI** (`<html lang="fr">`), Tunisia-targeted.

## Tech Stack

- **Next.js 15.5** App Router (`app/`), React 19, **TypeScript** (strict).
- **Tailwind CSS v4** (`app/globals.css`, oklch design tokens, `@tailwindcss/postcss`). No `tailwind.config` file — config is CSS-based. **next-themes** for light/dark.
- **shadcn/ui** (style "new-york", RSC enabled) on top of Radix UI primitives. See `components/ui/`.
- **Auth0** via `@auth0/nextjs-auth0` v4 (`Auth0Provider`, middleware, `/auth/*` routes) — cloud mode only.
- **@microsoft/signalr** v8 — realtime client (`lib/realtime/`, hub at `/hub/clinic` on the API host root).
- **sonner** toasts, **lucide-react** icons, **date-fns** dates (fr locale), **react-hook-form** + **zod** forms, **recharts** charts (first real usage is the dashboard's `collected-trend-chart.tsx`), **docx** + **file-saver** for client-side document export, **@vercel/analytics** (mounted in layout).
- Data layer is plain `fetch` wrapped in `lib/api/` — **no React Query / SWR / Redux**. State is local `useState` + custom hooks + React Contexts (session, connectivity, sidebar) + the SignalR realtime seam.

## Run

From `web/` (scripts in `package.json`):
- `npm run dev` — dev server (Next.js)
- `npm run build` — production build (`output: 'standalone'`, ESLint disabled during build in `next.config.ts`)
- `npm start` — serve production build
- `npm run check:responsive` — **the device gate** (`scripts/check-responsive.mjs`): **13 checks**, one grep per class of layout defect that no type can catch and no eye sees at the width you happen to be developing at. Run it with `npx tsc --noEmit` + `npm run build` on any frontend change. ⚠️ **Every check is enforced** — the old `PENDING_PARTS` staging set is gone; it still listed `P7`/`P8` long after no check declared either, so it read as the source of truth for what was enforced while being inert.
- `node scripts/generate-icons.mjs` — regenerates the **seven** icon assets in `public/` from the single master `branding/icon.svg` (via `sharp`, already a Next dependency; PIL was not an option — it cannot read SVG). **Never hand-edit a PNG in `public/`**: replace the master and re-run. Output is byte-identical across runs, so an unrelated re-run does not churn the diff. All seven used to **404** while `app/layout.tsx` and `app/manifest.ts` declared them, so an installed app got a blank tile.
- `npm run lint` — ⚠️ **cannot run**: `eslint` is named in the script but is **not in `devDependencies`**, and `next.config.ts` sets `eslint.ignoreDuringBuilds`. With no test runner and no CI either, `check:responsive` + `tsc` + `build` + an eye pass at 320/390/820/1180/1440 px *is* the whole gate.

Dockerized via `web/Dockerfile`.

## Environment (`.env.local`)

| Var | Purpose |
|-----|---------|
| `NEXT_PUBLIC_API_URL` | Base URL of the .NET API (default fallback `http://localhost:5000/api`). Read in `lib/api/client.ts`. **In the Local same-origin front-door build (Phase 5) set this to the relative `/api`** — the browser hits the Kestrel front door on whatever server IP it loaded from; `client.ts` resolves the relative base against `window.location.origin`. The SignalR hub URL derives from this too (`lib/realtime/clinic-hub.ts`), targeting the API host root, not the `/api` base. |
| `AUTH_MODE` | `cloud` (default) or `local`. Read server-side (`lib/auth/local-auth.ts`); selects the session provider and gates the Local-only **`/bff/auth/*`** routes and middleware behavior. Delivered to the browser via SSR (`useSession().mode`). |
| `API_INTERNAL_URL` | **Local, server-only (Phase 5).** Absolute API URL the `local-login` / `change-password` BFF route handlers call server-side (a relative `/api` has no origin server-side). Default `http://localhost:5000/api` (the API's loopback HTTP hop). |
| `AUTH_COOKIE_SECURE` | **Local, server-only (Phase 5).** `true` forces the `Secure` flag on the auth session cookie. Needed because the Node server sits behind the HTTPS front door on a plain-HTTP loopback hop, so the BFF handler would otherwise derive a non-Secure scheme and drop `Secure` even though the browser transport is HTTPS. |
| `AUTH0_SECRET`, `AUTH0_DOMAIN`, `AUTH0_ISSUER_BASE_URL`, `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET`, `AUTH0_AUDIENCE`, `AUTH0_BASE_URL`, `APP_BASE_URL` | Auth0 config (server-side, cloud mode). `AUTH0_AUDIENCE` enables API access tokens. |

## How the frontend talks to the .NET API

- All requests go through helpers in **`lib/api/client.ts`** (`apiGet/apiPost/apiPut/apiDelete`, plus `apiPostFormData/apiPutFormData` for uploads). Base URL = `NEXT_PUBLIC_API_URL`.
- **Auth token**: client.ts auto-fetches the token from the local route **`app/bff/auth/token/route.ts`** (Phase 5 relocated `/api/auth/*` → `/bff/auth/*` so the front-door proxy forwards them to Next instead of colliding with the API's `/api/*`; mode-aware: Auth0 access token in cloud, the local JWT from the cookie in local) and attaches it as `Authorization: Bearer <token>`. Requests also send `credentials: 'include'` for the session cookie.
- **Errors**: non-OK responses throw `ApiError(status, message)`; validation errors from the .NET ProblemDetails (`title`/`errors`) are flattened into the message. Network failures throw `ApiError(0, ...)`.
- **Realtime**: `lib/realtime/clinic-hub.ts` opens a SignalR connection to `/hub/clinic` (auth via `/bff/auth/token`) and listens for the `entityChanged` event carrying a `RealtimeResource` key (appointments, patients, proceduretypes, documents, files, clinics, users, stock, notifications, invoices, cnamnomenclature, medications, dentalacts, treatmentplans, expenses, **doctors, laborders, recall, waitinglist**). Pages subscribe via `useClinicRealtime(RealtimeResource.X, refetch)` to refetch only their own views on a peer's mutation. **The map is not free-form**: `RealtimeResourceResolverTests` reflects over every backend `IRequest`, parses this file, and asserts the two key sets are **equal in both directions** — a key added on one side alone fails the build, which is how the five orphans of audit § 9.1 went unnoticed for so long. Its two allow-lists (emit-only / listen-only) are asserted empty on purpose.
- Per-resource API modules wrap these helpers (see `web/lib/CLAUDE.md`). DTO types live in `lib/api/types.ts`.

## Auth & route protection

- **`middleware.ts`** is mode-branched (`resolveAuthMode()`):
  - **cloud**: routes `/auth/*` to Auth0; allows public `/login`, `/setup`, `/join`; else requires an Auth0 session or redirects to `/auth/login?returnTo=...`.
  - **local**: gates protected routes on the `local_session` HttpOnly cookie (redirect to `/login`), skips Auth0 entirely, and forces users with the `local_must_change_password` cookie onto `/change-password`. Redirects go through `frontDoorRedirect()` (Phase 5): behind the YARP proxy Next's own request host is the internal `localhost:<WebPort>` (HTTP), so it builds an **absolute** URL from the `x-forwarded-host`/`x-forwarded-proto` headers YARP adds — sending the browser to the HTTPS front door, not the internal port. Public/skip paths include `/_next/*` and `/bff/auth/*`.
- **The session seam** (`lib/auth/session.tsx`): a single `useSession()` context — `{ user, mode, isLoading, logout }` — backed by `CloudSessionProvider` or `LocalSessionProvider`. SSR-tolerant (returns a loading default when no provider is in scope).
  - **Cloud**: `CloudSessionProvider` → `Auth0Provider` + `CloudBridge` (bridges Auth0's `useUser`). Auth0's profile carries no clinic role, so `CloudBridge` additionally calls `clinicsApi.getUserStatus()` and folds the DB-resolved `role` into the session — without this the admin-gated UI (reminder settings, CNAM nomenclature, medication & dental-act catalogs) is unreachable in Cloud.
  - **Local**: `LocalSessionProvider` reads `/bff/auth/session` from the cookie; 30-min inactivity auto-logout; a 401 clears the stale cookie.
- **Clinic-membership** is enforced client-side, not in middleware: pages wrap content in **`<ClinicGuard>`** (`components/clinic-guard.tsx`), which uses `useClinicAccess` to verify the user belongs to a clinic (else shows `unauthorized-page` / redirects to `/setup`).
- **Role gating is client-side and presentation-only — the server is authoritative.** Two shapes:
  - **Admin**: the catalog pages (`/cnam-nomenclature`, `/dental-acts`, `/medications`) and `/users` check
    `useSession().user?.role === "admin"` and render a "Lock" card + back link for non-admins.
  - **Secretary** (`adoption-qa-i-access-control-and-audit` I3): `lib/nav.ts` owns the one comparison
    (**`hidesClinicWideMoney(role)`** / `isNavItemVisible(href, role)`), and `buildNavSections` takes the **role**
    rather than an `isAdmin` boolean — an admin/not-admin split cannot express « a secretary sees less than a
    doctor », which is the whole distinction I1 turns on. « Tableau de bord » and the entire « Finances » group
    (`/factures`, `/caisse`, `/creances`) disappear from the rail, the drawer **and the phone's bottom bar** (all
    three read `isNavItemVisible`, so they cannot disagree), the three pages themselves render
    **`ui/access-denied-card.tsx`**, and `/` **redirects a secretary to `/appointments`** — login lands on `/`, so
    without that redirect reception would open the app every morning onto the one screen they cannot read.
    ⚠️ The three money pages gate with a **wrapper component**, not a branch inside the page: their bodies fetch on
    mount, so a branch would still fire every request and stack three 403 toasts on top of the refusal card. A
    section whose every item is hidden is dropped rather than rendered empty — « Finances » with no rows under it
    advertises exactly the capability the gate withholds.
    ⚠️ **The secretary gate is about money, and nothing else.** The patient page is fully open to reception — its
    only role gate is the delete affordance on a fiche de soins and on a document (`app/patients/[id]/page.tsx`),
    because the five clinical controllers behind those tabs moved to `AnyClinicRole` server-side: reception reads
    and records the patient file, and only destruction is refused. There is **no** client-side gate to add here for
    the clinical record, and the « a lot of things say you are not allowed » symptom that prompted the change was
    never client-side at all — it was `client.ts`'s 403 fallback (« Vous n'avez pas les droits nécessaires pour
    cette action. ») surfacing an ASP.NET refusal that fired *before* any handler ran. If a clinical read refuses
    again, the fix is a policy on the controller, not a branch in the page.
- `app/layout.tsx` (a server component) reads `AUTH_MODE` and mounts either `CloudSessionProvider` or `LocalSessionProvider`, then `ConnectivityProvider` (Phase 3 — polls `/api/connectivity` in Local mode, static online default in Cloud), `SidebarProvider`, children, the floating `<AIChat>` widget (inside connectivity so it can gate on reachability), the global `<Toaster>`, and Vercel `<Analytics>`. French `metadata` (title via `PRODUCT_NAME` from `lib/brand.ts`) + theme-aware favicons.

## Folder Structure

```
web/
  app/                 App Router pages, layouts, route handlers
    bff/auth/          token/ (mode-aware JWT for the client), session/ (decode local cookie → {email,role}),
                       local-login/ + local-logout/ (set/clear session + must-change cookies), change-password/ (proxy).
                       Under /bff/* (Phase 5) so the front-door proxy routes them to Next, not the API's /api/*.
    api/               empty (legacy; route handlers relocated to bff/auth/)
    layout.tsx         root layout: session/connectivity/sidebar providers, Toaster, AIChat, Analytics, FR metadata
    error.tsx          segment-level error boundary (French "Réessayer")
    global-error.tsx   root error boundary (self-contained <html>, for layout-level throws)
    globals.css        Tailwind v4 + oklch design tokens
  components/          Feature components (+ components/ui = shadcn primitives)  -> see components/CLAUDE.md
  lib/
    api/               fetch wrapper (client.ts) + per-resource API modules + types.ts
    auth/              session.tsx (useSession seam + Cloud/Local providers), local-auth.ts (AUTH_MODE + cookie names)
    connectivity/      connectivity.tsx (ConnectivityProvider + useConnectivity, Phase 3)
    realtime/          clinic-hub.ts (SignalR /hub/clinic + RealtimeResource) + use-clinic-realtime.ts
    hooks/             data-fetching / auth hooks
    auth0.ts           Auth0Client (server) config
    brand.ts           PRODUCT_NAME + branding constants
    format.ts          fr-TN locale date/number/currency formatting (formatDT, formatDate, ...)
    download.ts, errors.ts, phone.ts, working-hours.ts, utils.ts (cn())
    -> see lib/CLAUDE.md for the full data layer
  contexts/            sidebar-context.tsx (desktop collapse — persisted to localStorage; plus the
                       below-`md:` drawer state, which is deliberately NOT persisted so a phone
                       session never overwrites the desktop rail preference. Closes on navigation.)
  types/               ambient .d.ts (speech-recognition)
  middleware.ts        dual-mode (Auth0 / local cookie) route gate
```

## Routing / Pages

All app pages are client components (`"use client"`) that render `DashboardSidebar` + `DashboardHeader` inside `<ClinicGuard>` (except auth/onboarding pages). Data is fetched in `useEffect` via `lib/api` modules; many pages also subscribe to realtime via `useClinicRealtime`.

| Route | File | Renders |
|-------|------|---------|
| `/` | `app/page.tsx` | **Redirects a secretary to `/appointments`** (I3 — `GET /api/dashboard` is `AdminOrDoctor`). Otherwise the dashboard (`useDashboard` → `GET /api/dashboard`): a period selector (Aujourd'hui / Cette semaine / Ce mois, held in `?period=`) above four sections — **Activité** and **Argent** (every figure with its delta vs. the previous period), **À traiter**, and the **Tendance** sparkline — then the kept `AppointmentList`. **Every figure is a `Link`**; the KPI→route mapping lives in one place, `lib/dashboard-links.ts` (an exhaustive `Record<DashboardKpiKey, …>`, so adding a KPI without a destination fails `tsc`). |
| `/appointments` | `app/appointments/page.tsx` | Day/week/month calendar, create/edit dialogs, Google Calendar sync controls (Local: gated on internet reachability + per-appointment "non synchronisé"/Push-to-Google via `useConnectivity()`). ⚠️ **The page renders no toolbar of its own any more — only the active-filter chips.** The view switch, « Nouveau rendez-vous », the praticien filter and the Google controls are props on `<AppointmentCalendar>` and render inside the one agenda bar that component owns (see `components/CLAUDE.md`); four rows across two files is what put an administrative Google row between the date and the view switch. The chips stay here because they are a statement about *this page's* URL state (`?status=` from two `lib/dashboard-links.ts` entries) and § 13 requires an unrequested filter to be visible and removable at every width — so they must not be folded into the popover holding the switches themselves. |
| `/recurring-series` | `app/recurring-series/page.tsx` | Recurring appointment series ("Rendez-vous récurrents") — create/list via `appointmentsApi` |
| `/waiting-list` | `app/waiting-list/page.tsx` | "Salle d'attente / Liste d'attente" (`waitingListApi`) — queue + promote to appointment |
| `/patients` | `app/patients/page.tsx` | Patients table + search/flag filter, create patient dialog. (`patients/loading.tsx` exists but `return null` — it is a no-op, **not** a skeleton.) |
| `/patients/[id]` | `app/patients/[id]/page.tsx` | Patient detail (tabbed): info, dental records/odontogram, history, documents, treatment plans/devis. *(No « AI summary » tab — the button was removed long ago and I4 deleted the orphaned endpoint behind it.)* |
| `/patients/[id]/files` | `app/patients/[id]/files/page.tsx` | Per-patient file/folder manager |
| `/procedure-types` | `app/procedure-types/page.tsx` | Procedure types CRUD table + form modal |
| `/documents` | `app/documents/page.tsx` | Document template gallery (ordonnance, lettre de liaison, note d'honoraires, etc. — FR) + honoraires launcher |
| `/documents/[type]` | `app/documents/[type]/page.tsx` | Document editor (`DocumentEditorContent`, Suspense-wrapped) |
| `/stock` | `app/stock/page.tsx` | Stock/inventory table + item form modal (`stockApi`) |
| `/factures` | `app/factures/page.tsx` | "Factures & Recettes": invoices table (`invoicesApi`) + revenue KPI, date/status filters |
| `/treatment-plans` | `app/treatment-plans/page.tsx` | « Plans de traitement » (the title no longer says « & devis » — the rail agrees): `TreatmentPlansTable` (a **list** — rows link to the workspace) + a statut filter and a **période preset** (`Cette semaine` by default, then `Ce mois` / `Toutes les dates` / `Personnalisé`, which is the only state that shows Du/Au). ⚠️ Two things the default window forces: a dashboard drill-through (`?status=`, `?acceptedFrom/?acceptedTo`) switches the période to `Toutes les dates`, because both those KPIs count by another date (or none) and a *creation*-week window would hide the very devis the card counted; and the table is told `filtered`, so a quiet week renders « Aucun devis pour ces filtres » instead of the first-run « Aucun plan de traitement » invite. No « Filtrer » button, for the reason `/factures` documents. |
| `/treatment-plans/[id]` | `app/treatment-plans/[id]/page.tsx` | The devis **workspace** (`PlanWorkspace`): header (statut, progress, Total/Encaissé/Reste, prochaine séance, the plan's actions), actes with one primary action per état, échéancier, and a « Parcours » feed. The plan area's only dynamic route. Loading / « Plan introuvable » render *outside* `ClinicGuard`, following `patients/[id]`. |
| `/creances` | `app/creances/page.tsx` | "Créances": per-patient balances due (`ReceivablesTable`) |
| `/caisse` | `app/caisse/page.tsx` | « Caisse »: **four** figures (Encaissements gross · Avoirs remboursés · Dépenses · Net) over the **« Extrait de caisse »** — every movement behind them, oldest first, with a running period balance and struck-through voided rows (`CaisseLedgerTable`) — then the expenses table, kept for its edit/delete actions. Money-in was previously an opaque total with only expenses itemised |
| `/lab-orders` | `app/lab-orders/page.tsx` | "Laboratoire — bons de prothèse" (`labOrdersApi`) |
| `/rappels` | `app/rappels/page.tsx` | « Rappels » — the SMS/WhatsApp **delivery log** (`reminderSettingsApi.reminderStatus`), plus the channel settings in a Sheet. ⚠️ Not a worklist: `app/recalls/` **does not exist** and `recallsApi` has **zero callers**, so « quels patients ne sont pas revenus depuis 6 mois ? » — the question the recall subsystem exists to answer — currently has no UI. The backend (`RecallController`, the due-list query) is deliberately intact so the worklist can be given a home without rebuilding it; `components/recalls/recall-labels.ts` was deleted as dead code carrying a fifth status palette. |
| `/cnam-nomenclature` | `app/cnam-nomenclature/page.tsx` | **admin-only**: CNAM nomenclature table + letter values (`CnamNomenclatureTable`, `CnamLetterValuesCard`) |
| `/dental-acts` | `app/dental-acts/page.tsx` | **admin-only**: CNAM dental act codes (`DentalActsTable`) |
| `/medications` | `app/medications/page.tsx` | **admin-only**: medication catalog for ordonnances (`MedicationCatalogTable`) |
| `/mon-profil` | `app/mon-profil/page.tsx` | "Mon profil": practitioner info + cachet/signature (`MonProfilContent`) |
| `/settings` | `app/settings/page.tsx` | Clinic settings (`ClinicSettings` — billing, reminders, backup, etc.) |
| `/users` | `app/users/page.tsx` | **Local, admin-only**: user management + clinic-code regenerate (`UserManagement`) |
| `/login` | `app/login/page.tsx` | Mode-aware: Auth0 sign-in landing (cloud) **or** a local email+password form (local) |
| `/setup` | `app/setup/page.tsx` | First-run wizard: create a clinic (`SetupWizard`); local mode also collects the admin account |
| `/join` | `app/join/page.tsx` | Join existing clinic via code (`JoinWizard`); local mode skips the session gate (self-registration) |
| `/change-password` | `app/change-password/page.tsx` | **Local**: forced/voluntary password change (`ChangePasswordForm`) |

## Conventions

- Pages are client-rendered; data is fetched in `useEffect` via `lib/api` modules, with loading/error local state and `toast` on failure. `app/error.tsx` / `app/global-error.tsx` catch render throws with a French fallback.
- Lists refresh by bumping a `refreshKey` state passed to child tables; peer mutations refresh live via `useClinicRealtime(RealtimeResource.X, refetch)`.
- Admin-only screens gate client-side on `useSession().user?.role === "admin"` (Lock card + back link otherwise).
- Import alias `@/*` -> project root (`tsconfig.json`). UI alias `@/components/ui`, utils `@/lib/utils`.
- FR labels throughout; dates/currency/file sizes via `lib/format.ts` (fr-TN — `formatDT`, `formatDate`, `formatDateTime`, **`formatFileSize`** which renders « o / Ko / Mo »). Branding strings via `lib/brand.ts`. **Never hand-format a dinar amount** — `toFixed(2)` drops the millime and `toFixed(3)` prints a period where the rest of the product prints a comma (audit § 8.5).
- **A date input's default is `todayLocalIso()`, never `toISOString().slice(0, 10)`** (`lib/format.ts`, AC-P6.5). `toISOString` converts to UTC first, so for the first hour of every Tunisian day it pre-fills *yesterday* — and on the 1st, the previous month. Three of the five call sites that carried it were money dates.
- **English storage key + French display map** is the standing convention for closed value sets whose keys are persisted or snapshotted: `lib/specialties.ts` (`specialtyLabel`), `lib/working-hours.ts`' weekday keys, `components/appointment-labels.ts`, `components/factures/invoice-labels.ts`, `components/treatment-plans/treatment-plan-labels.ts`. Map at display time; never rename a key, and always pass unknown values through so historical rows keep rendering.
- **⚠️ Writing frontend code? `.claude/rules/frontend-web.md` is the directive form of everything below** — the device contract (320 px · 380 px height · 200 % zoom), the `coarse:` touch floor, table→cards, dialog→sheet, the `dvh`/`--bottom-inset` rules, the UX floor, and the gate. This file describes *what each screen is*; that file states *what you must write*. One fact, one home: it points here for rationale rather than restating it.
- **Responsive shell** — below `md:` the rail becomes a `Sheet` drawer (`useSidebar().isMobileOpen`) reached from the phone's bottom bar, the header reflows, page gutters drop to `p-4`, and wide content scrolls **inside its own container** (`ui/table.tsx` wraps every table in `overflow-x-auto`, now with a right-edge fade so an off-screen column is visible as one). `AppShell` owns the gutter, the width and the `pb-20` runway that keeps the AI launcher off the last table row.
  ⚠️ **`md:` is the phone hinge; `lg:` is the tablet one, and the widest tables need the second.** An iPad portrait is 820 px and therefore already `md:`, so it gets the desktop table *and* the 256 px rail — 532 px for a 9- or 10-column grid whose every cell is `whitespace-nowrap`. `ui/card-list.tsx` exports both pairs: `CARDS_ONLY`/`TABLE_ONLY` (`md:`) for ordinary lists and **`CARDS_ONLY_LG`/`TABLE_ONLY_LG`** for surfaces with roughly eight or more columns.
  ⚠️ **Growing a control and overlaying it are different fixes, and picking the wrong one causes wrong-action bugs.** `.touch-target` overlays a 44 px hit area without repainting, which is right for an *isolated* small control. For anything in a **stack or a row** — menu items, tooth cells, the pager's four 32 px buttons, the MODVL faces — the overlay overhangs its neighbours and, since the later sibling paints last, steals their taps. Those grow their own padding (`coarse:py-3`) or their own size (`coarse:size-10`) instead. `.touch-target` is also inert inside an `overflow-hidden` box (the agenda's appointment block), where the pseudo-element is simply clipped.
- **A field under 16 px zooms iOS and never zooms back.** `ui/input.tsx`, `ui/textarea.tsx`, `ui/time-field.tsx`, `ui/select.tsx` and `ui/command.tsx` all carry `text-base md:text-sm` for this. ⚠️ A call site passing an **unprefixed** `text-sm` *removes* the primitive's `text-base` under tailwind-merge and silently defeats the guard — always write `md:text-sm`.
- **Zone colour (`lib/zones.ts`)** — the app's five nav groups (Quotidien · Clinique · Finances · Gestion · Configuration) each own a hue, and it appears in exactly three places: the rail's group heading and active row, `PageHeader`'s eyebrow + dot, and a zone-scoped `EmptyState` chip. It is **orientation, not decoration** — the rail and the page agree on where you are — and it is never a status (`ui/status-tone.ts` owns that family, deliberately separate: a zone says *where*, a status says *how it is going*). `PageHeader` derives both the zone and the page icon from the **route**, so the eyebrow can no longer drift from the rail the way « Argent » vs « Finances » and « Dossiers » vs « Quotidien » had. Every zone ink sits at L 0.46–0.49 because it is used as 11 px text on a white card.
- **`ui/empty-state.tsx`** — icon chip + title + description + action, and the one place the three empty *kinds* are kept apart: **nothing yet** (invite, with the action that creates the first record), **nothing matching the filter** (offer « Effacer les filtres », and *never* an « Ajouter » — the record may exist and the user mistyped), and **failed to load**, which is not an empty state at all and gets a « Réessayer » banner instead. An empty list is the most common first experience of every screen in a new clinic.
- **Feedback and accessibility floor** for any new interactive surface: disabled while in flight with a single effect on double-submit; success via a French `sonner` toast (sonner's container is the app's live region, so toasts announce); failure via `showErrorToast` with the dialog left open and its input intact; a real `<Label htmlFor>` rather than a placeholder standing in for one; an `aria-label` on every icon-only control; `role="button"` + `tabIndex={0}` + Enter/Space on any clickable `Card`; and `role="status"` on an inline async result. `:focus-visible` in `globals.css` gives every keyboard-reachable element a ring floor.
- The in-app **notification center** is API-wired: the header bell + `notification-panel.tsx` in `dashboard-header.tsx`, driven by `useNotifications()` over `notificationsApi`, refreshed on the `"notifications"` realtime key. Dashboard stats, `appointment-list.tsx`, and `stock-table.tsx` are also API-wired. Header search is a live patient lookup.

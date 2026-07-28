# web/ — Clinic Management Frontend

Next.js 15 (App Router) frontend for the dental/medical clinic management system. Talks to a separate .NET API. **Auth is pluggable** (`AUTH_MODE`): **cloud** = Auth0; **local** = email+password backed by an HttpOnly session cookie (offline LAN installs). Consumers read a unified `useSession()` seam, not Auth0 directly. **French UI** (`<html lang="fr">`), Tunisia-targeted.

## Tech Stack

- **Next.js 15.5** App Router (`app/`), React 19, **TypeScript** (strict).
- **Tailwind CSS v4** (`app/globals.css`, oklch design tokens, `@tailwindcss/postcss`). No `tailwind.config` file — config is CSS-based. **next-themes** for light/dark.
- **shadcn/ui** (style "new-york", RSC enabled) on top of Radix UI primitives. See `components/ui/`.
- **Auth0** via `@auth0/nextjs-auth0` v4 (`Auth0Provider`, middleware, `/auth/*` routes) — cloud mode only.
- **@microsoft/signalr** v8 — realtime client (`lib/realtime/`, hub at `/hub/clinic` on the API host root).
- **sonner** toasts, **lucide-react** icons, **date-fns** dates (fr locale), **react-hook-form** + **zod** forms, **recharts** charts, **docx** + **file-saver** for client-side document export, **@vercel/analytics** (mounted in layout).
- Data layer is plain `fetch` wrapped in `lib/api/` — **no React Query / SWR / Redux**. State is local `useState` + custom hooks + React Contexts (session, connectivity, sidebar) + the SignalR realtime seam.

## Run

From `web/` (scripts in `package.json`):
- `npm run dev` — dev server (Next.js)
- `npm run build` — production build (`output: 'standalone'`, ESLint disabled during build in `next.config.ts`)
- `npm start` — serve production build
- `npm run lint` — ESLint (`eslint.config.mjs`, extends next core-web-vitals + TS)

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
- **Admin gating** is client-side too: the catalog pages (`/cnam-nomenclature`, `/dental-acts`, `/medications`) and `/users` check `useSession().user?.role === "admin"` and render a "Lock" card + back link for non-admins.
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
| `/` | `app/page.tsx` | Dashboard "Tableau de bord": stats cards (`dashboardApi`) + appointment list (`useAppointments`) |
| `/appointments` | `app/appointments/page.tsx` | Day/week calendar, create/edit dialogs, Google Calendar sync controls (Local: gated on internet reachability + per-appointment "non synchronisé"/Push-to-Google via `useConnectivity()`) |
| `/recurring-series` | `app/recurring-series/page.tsx` | Recurring appointment series ("Rendez-vous récurrents") — create/list via `appointmentsApi` |
| `/waiting-list` | `app/waiting-list/page.tsx` | "Salle d'attente / Liste d'attente" (`waitingListApi`) — queue + promote to appointment |
| `/patients` | `app/patients/page.tsx` | Patients table + search/flag filter, create patient dialog. (`patients/loading.tsx` exists but `return null` — it is a no-op, **not** a skeleton.) |
| `/patients/[id]` | `app/patients/[id]/page.tsx` | Patient detail (tabbed): info, dental records/odontogram, history, AI summary, documents, treatment plans/devis |
| `/patients/[id]/files` | `app/patients/[id]/files/page.tsx` | Per-patient file/folder manager |
| `/procedure-types` | `app/procedure-types/page.tsx` | Procedure types CRUD table + form modal |
| `/documents` | `app/documents/page.tsx` | Document template gallery (ordonnance, lettre de liaison, note d'honoraires, etc. — FR) + honoraires launcher |
| `/documents/[type]` | `app/documents/[type]/page.tsx` | Document editor (`DocumentEditorContent`, Suspense-wrapped) |
| `/stock` | `app/stock/page.tsx` | Stock/inventory table + item form modal (`stockApi`) |
| `/factures` | `app/factures/page.tsx` | "Factures & Recettes": invoices table (`invoicesApi`) + revenue KPI, date/status filters |
| `/treatment-plans` | `app/treatment-plans/page.tsx` | "Plans de traitement & Devis": `TreatmentPlansTable` (a **list** — rows link to the workspace) + status filter |
| `/treatment-plans/[id]` | `app/treatment-plans/[id]/page.tsx` | The devis **workspace** (`PlanWorkspace`): header (statut, progress, Total/Encaissé/Reste, prochaine séance, the plan's actions), actes with one primary action per état, échéancier, and a « Parcours » feed. The plan area's only dynamic route. Loading / « Plan introuvable » render *outside* `ClinicGuard`, following `patients/[id]`. |
| `/creances` | `app/creances/page.tsx` | "Créances": per-patient balances due (`ReceivablesTable`) |
| `/caisse` | `app/caisse/page.tsx` | "Caisse": cash register / expenses (`expensesApi`) |
| `/lab-orders` | `app/lab-orders/page.tsx` | "Laboratoire — bons de prothèse" (`labOrdersApi`) |
| `/recalls` | `app/recalls/page.tsx` | "Patients à relancer": recall list + recall settings (`recallsApi`) |
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
- FR labels throughout; dates/currency/file sizes via `lib/format.ts` (fr-TN — `formatDT`, `formatDate`, `formatDateTime`, **`formatFileSize`** which renders « o / Ko / Mo »). Branding strings via `lib/brand.ts`.
- **English storage key + French display map** is the standing convention for closed value sets whose keys are persisted or snapshotted: `lib/specialties.ts` (`specialtyLabel`), `lib/working-hours.ts`' weekday keys, `components/appointment-labels.ts`, `components/factures/invoice-labels.ts`, `components/treatment-plans/treatment-plan-labels.ts`. Map at display time; never rename a key, and always pass unknown values through so historical rows keep rendering.
- **Responsive shell** — the chrome, not every screen, is responsive. Below `md:` the rail becomes a `Sheet` drawer (`useSidebar().isMobileOpen`), the header reflows, page gutters drop to `p-4`, and the two fixed-width offenders (the AI panel, the document editor's 420 px form column) go viewport-relative. Wide content scrolls **inside its own container** — `ui/table.tsx` already wraps every table in `overflow-x-auto`, so the body never scrolls horizontally. A full responsive pass over the calendar grid and the wide data tables is explicitly *not* done.
- **Feedback and accessibility floor** for any new interactive surface: disabled while in flight with a single effect on double-submit; success via a French `sonner` toast (sonner's container is the app's live region, so toasts announce); failure via `showErrorToast` with the dialog left open and its input intact; a real `<Label htmlFor>` rather than a placeholder standing in for one; an `aria-label` on every icon-only control; `role="button"` + `tabIndex={0}` + Enter/Space on any clickable `Card`; and `role="status"` on an inline async result. `:focus-visible` in `globals.css` gives every keyboard-reachable element a ring floor.
- The in-app **notification center** is API-wired: the header bell + `notification-panel.tsx` in `dashboard-header.tsx`, driven by `useNotifications()` over `notificationsApi`, refreshed on the `"notifications"` realtime key. Dashboard stats, `appointment-list.tsx`, and `stock-table.tsx` are also API-wired. Header search is a live patient lookup.

# web/ — Clinic Management Frontend

Next.js 15 (App Router) frontend for the dental/medical clinic management system. Talks to a separate .NET API. **Auth is pluggable** (`AUTH_MODE`): **cloud** = Auth0; **local** = email+password backed by an HttpOnly session cookie (offline LAN installs). Consumers read a unified `useSession()` seam, not Auth0 directly.

## Tech Stack

- **Next.js 15.5** App Router (`app/`), React 19, **TypeScript** (strict).
- **Tailwind CSS v4** (`app/globals.css`, oklch design tokens, `@tailwindcss/postcss`). No `tailwind.config` file — config is CSS-based.
- **shadcn/ui** (style "new-york", RSC enabled) on top of Radix UI primitives. See `components/ui/`.
- **Auth0** via `@auth0/nextjs-auth0` v4 (`Auth0Provider`, middleware, `/auth/*` routes).
- **sonner** for toasts, **lucide-react** icons, **date-fns** dates, **react-hook-form** + **zod** for forms, **recharts** charts, **docx** + **file-saver** for client-side document export.
- Data layer is plain `fetch` wrapped in `lib/api/` — **no React Query / SWR / Redux**. State is local `useState` + custom hooks + one React Context (sidebar).

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
| `NEXT_PUBLIC_API_URL` | Base URL of the .NET API (default fallback `http://localhost:5000/api`). Read in `lib/api/client.ts`. **In the Local same-origin front-door build (Phase 5) set this to the relative `/api`** — the browser hits the Kestrel front door on whatever server IP it loaded from; `client.ts` resolves the relative base against `window.location.origin`. |
| `AUTH_MODE` | `cloud` (default) or `local`. Read server-side (`lib/auth/local-auth.ts`); selects the session provider and gates the Local-only **`/bff/auth/*`** routes and middleware behavior. Delivered to the browser via SSR (`useSession().mode`). |
| `API_INTERNAL_URL` | **Local, server-only (Phase 5).** Absolute API URL the `local-login` / `change-password` BFF route handlers call server-side (a relative `/api` has no origin server-side). Default `http://localhost:5000/api` (the API's loopback HTTP hop). |
| `AUTH_COOKIE_SECURE` | **Local, server-only (Phase 5).** `true` forces the `Secure` flag on the auth session cookie. Needed because the Node server sits behind the HTTPS front door on a plain-HTTP loopback hop, so the BFF handler would otherwise derive a non-Secure scheme and drop `Secure` even though the browser transport is HTTPS. |
| `AUTH0_SECRET`, `AUTH0_DOMAIN`, `AUTH0_ISSUER_BASE_URL`, `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET`, `AUTH0_AUDIENCE`, `AUTH0_BASE_URL`, `APP_BASE_URL` | Auth0 config (server-side, cloud mode). `AUTH0_AUDIENCE` enables API access tokens. |

## How the frontend talks to the .NET API

- All requests go through helpers in **`lib/api/client.ts`** (`apiGet/apiPost/apiPut/apiDelete`, plus `apiPostFormData/apiPutFormData` for uploads). Base URL = `NEXT_PUBLIC_API_URL`.
- **Auth token**: client.ts auto-fetches the token from the local route **`app/bff/auth/token/route.ts`** (Phase 5 relocated `/api/auth/*` → `/bff/auth/*` so the front-door proxy forwards them to Next instead of colliding with the API's `/api/*`; mode-aware: Auth0 access token in cloud, the local JWT from the cookie in local) and attaches it as `Authorization: Bearer <token>`. Requests also send `credentials: 'include'` for the session cookie.
- **Errors**: non-OK responses throw `ApiError(status, message)`; validation errors from the .NET ProblemDetails (`title`/`errors`) are flattened into the message. Network failures throw `ApiError(0, ...)`.
- Per-resource API modules wrap these helpers (see `web/lib/CLAUDE.md`). DTO types live in `lib/api/types.ts`.

## Auth & route protection

- **`middleware.ts`** is mode-branched (`resolveAuthMode()`):
  - **cloud**: routes `/auth/*` to Auth0; allows public `/login`, `/setup`, `/join`; else requires an Auth0 session or redirects to `/auth/login?returnTo=...`.
  - **local**: gates protected routes on the `local_session` HttpOnly cookie (redirect to `/login`), skips Auth0 entirely, and forces users with the `local_must_change_password` cookie onto `/change-password`. Redirects go through `frontDoorRedirect()` (Phase 5): behind the YARP proxy Next's own request host is the internal `localhost:<WebPort>` (HTTP), so it builds an **absolute** URL from the `x-forwarded-host`/`x-forwarded-proto` headers YARP adds — sending the browser to the HTTPS front door, not the internal port. Public/skip paths include `/_next/*` and `/bff/auth/*`.
- **The session seam** (`lib/auth/session.tsx`): a single `useSession()` context — `{ user, mode, isLoading, logout }` — backed by `CloudSessionProvider` (bridges Auth0 `useUser`) or `LocalSessionProvider` (reads `/api/auth/session` from the cookie; 30-min inactivity auto-logout). All ~5 former `useUser` consumers read this instead. SSR-tolerant (returns a loading default when no provider is in scope).
- **Clinic-membership** is enforced client-side, not in middleware: pages wrap content in **`<ClinicGuard>`** (`components/clinic-guard.tsx`), which uses `useClinicAccess` to verify the user belongs to a clinic (else shows `unauthorized-page` / redirects to `/setup`).
- `app/layout.tsx` (a server component) reads `AUTH_MODE` and mounts either `CloudSessionProvider` (with `Auth0Provider` inside) or `LocalSessionProvider`, plus the `ConnectivityProvider` (Phase 3 — polls `/api/connectivity` in Local mode, static online default in Cloud), `SidebarProvider`, the global `<Toaster>`, and the floating `<AIChat>` widget (inside the providers so it can read connectivity).

## Folder Structure

```
web/
  app/                 App Router pages, layouts, route handlers
    bff/auth/          token/ (mode-aware JWT for the client), session/ (decode local cookie → {email,role}),
                       local-login/ + local-logout/ (set/clear session + must-change cookies), change-password/ (proxy).
                       Under /bff/* (Phase 5) so the front-door proxy routes them to Next, not the API's /api/*.
  components/          Feature components (+ components/ui = shadcn primitives)  -> see components/CLAUDE.md
  lib/
    api/               fetch wrapper (client.ts) + per-resource API modules + types.ts
    auth/              session.tsx (useSession seam + Cloud/Local providers), local-auth.ts (AUTH_MODE + cookie names)
    hooks/             data-fetching / auth hooks
    auth0.ts           Auth0Client (server) config
    utils.ts           cn() classname helper
  contexts/            sidebar-context.tsx (collapse state, persisted to localStorage)
  types/               ambient .d.ts (speech-recognition)
  middleware.ts        Auth0 session gate
```

## Routing / Pages

All app pages are client components (`"use client"`) that render `DashboardSidebar` + `DashboardHeader` inside `<ClinicGuard>` (except auth/onboarding pages).

| Route | File | Renders |
|-------|------|---------|
| `/` | `app/page.tsx` | Dashboard: stats cards, appointment list, notifications (currently **static placeholder data**) |
| `/appointments` | `app/appointments/page.tsx` | Day/week calendar, create/edit appointment dialogs, Google Calendar sync controls (Local: gated on internet reachability + per-appointment "not synced"/Push-to-Google via `useConnectivity()`) |
| `/patients` | `app/patients/page.tsx` | Patients table + search/flag filter, create patient dialog |
| `/patients/[id]` | `app/patients/[id]/page.tsx` | Patient detail: info, dental records, history, AI summary, documents |
| `/patients/[id]/files` | `app/patients/[id]/files/page.tsx` | Per-patient file/folder manager |
| `/procedure-types` | `app/procedure-types/page.tsx` | Procedure types CRUD table + form modal |
| `/records` | `app/records/page.tsx` | Medical records browser; opens patient summary modal |
| `/documents` | `app/documents/page.tsx` | Document template gallery (ordonnance, lettre de liaison, note d'honoraires, etc. — FR labels) + saved docs list |
| `/documents/[type]` | `app/documents/[type]/page.tsx` | Document editor (`DocumentEditorContent`, Suspense-wrapped) |
| `/files` | `app/files/page.tsx` | Global file browser across patients (folders, upload, preview, download) |
| `/stock` | `app/stock/page.tsx` | Stock/inventory table + item form modal (**sample data, no API yet**) |
| `/settings` | `app/settings/page.tsx` | Clinic settings (`ClinicSettings`) |
| `/login` | `app/login/page.tsx` | Mode-aware: Auth0 sign-in landing (cloud) **or** a local email+password form (local) |
| `/setup` | `app/setup/page.tsx` | First-run wizard: create a clinic (`SetupWizard`); local mode also collects the admin account |
| `/join` | `app/join/page.tsx` | Join existing clinic via code (`JoinWizard`); local mode skips the session gate (self-registration) |
| `/users` | `app/users/page.tsx` | **Local, admin-only**: user management + clinic-code regenerate (`UserManagement`) |
| `/change-password` | `app/change-password/page.tsx` | **Local**: forced/voluntary password change (`ChangePasswordForm`) |

## Conventions

- Pages are client-rendered; data is fetched in `useEffect` via `lib/api` modules, with loading/error local state and `toast` on failure.
- Lists are refreshed by bumping a `refreshKey` state passed to child tables.
- Import alias `@/*` -> project root (`tsconfig.json`). UI alias `@/components/ui`, utils `@/lib/utils`.
- Some screens use French labels (documents, setup) — this is a Tunisia-targeted clinic app.
- `appointment-list.tsx`, `notifications-list.tsx`, dashboard stats, and `stock-table.tsx` still render hardcoded sample data (not wired to the API).

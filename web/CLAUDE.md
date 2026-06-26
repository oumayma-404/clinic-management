# web/ — Clinic Management Frontend

Next.js 15 (App Router) frontend for the dental/medical clinic management system. Talks to a separate .NET API. Auth via Auth0.

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
| `NEXT_PUBLIC_API_URL` | Base URL of the .NET API (default fallback `http://localhost:5000/api`). Read in `lib/api/client.ts`. |
| `AUTH0_SECRET`, `AUTH0_DOMAIN`, `AUTH0_ISSUER_BASE_URL`, `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET`, `AUTH0_AUDIENCE`, `AUTH0_BASE_URL`, `APP_BASE_URL` | Auth0 config (server-side). `AUTH0_AUDIENCE` enables API access tokens. |

## How the frontend talks to the .NET API

- All requests go through helpers in **`lib/api/client.ts`** (`apiGet/apiPost/apiPut/apiDelete`, plus `apiPostFormData/apiPutFormData` for uploads). Base URL = `NEXT_PUBLIC_API_URL`.
- **Auth token**: client.ts auto-fetches the Auth0 access token from the local route **`app/api/auth/token/route.ts`** (which calls `auth0.getAccessToken()` server-side) and attaches it as `Authorization: Bearer <token>`. Requests also send `credentials: 'include'` for the session cookie.
- **Errors**: non-OK responses throw `ApiError(status, message)`; validation errors from the .NET ProblemDetails (`title`/`errors`) are flattened into the message. Network failures throw `ApiError(0, ...)`.
- Per-resource API modules wrap these helpers (see `web/lib/CLAUDE.md`). DTO types live in `lib/api/types.ts`.

## Auth & route protection

- **`middleware.ts`**: routes `/auth/*` to Auth0; allows public routes `/login`, `/setup`, `/join`; otherwise requires an Auth0 session or redirects to `/auth/login?returnTo=...`.
- **Clinic-membership** is enforced client-side, not in middleware: pages wrap content in **`<ClinicGuard>`** (`components/clinic-guard.tsx`), which uses `useClinicAccess` to verify the user belongs to a clinic (else shows `unauthorized-page` / redirects to `/setup`).
- `app/layout.tsx` wraps the app in `Auth0Provider` + `SidebarProvider`, mounts the global `<Toaster>` and the floating `<AIChat>` widget.

## Folder Structure

```
web/
  app/                 App Router pages, layouts, route handlers
    api/auth/token/    Route handler exposing Auth0 access token to the client
  components/          Feature components (+ components/ui = shadcn primitives)  -> see components/CLAUDE.md
  lib/
    api/               fetch wrapper (client.ts) + per-resource API modules + types.ts
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
| `/appointments` | `app/appointments/page.tsx` | Day/week calendar, create/edit appointment dialogs, Google Calendar sync controls |
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
| `/login` | `app/login/page.tsx` | Auth0 sign-in landing (redirects authed users to `/setup`) |
| `/setup` | `app/setup/page.tsx` | First-run wizard: create a clinic (`SetupWizard`) |
| `/join` | `app/join/page.tsx` | Join existing clinic via code (`JoinWizard`) |

## Conventions

- Pages are client-rendered; data is fetched in `useEffect` via `lib/api` modules, with loading/error local state and `toast` on failure.
- Lists are refreshed by bumping a `refreshKey` state passed to child tables.
- Import alias `@/*` -> project root (`tsconfig.json`). UI alias `@/components/ui`, utils `@/lib/utils`.
- Some screens use French labels (documents, setup) — this is a Tunisia-targeted clinic app.
- `appointment-list.tsx`, `notifications-list.tsx`, dashboard stats, and `stock-table.tsx` still render hardcoded sample data (not wired to the API).

# Clinic Management — Repo Guide

Full-stack **dental/medical clinic management** system (Tunisia-targeted: French UI labels, Tunisian governorates). Multi-tenant by clinic, with patient records, appointments + Google Calendar sync, medical/dental documents, file storage, and an AI assistant.

> **Read this first.** Each major folder has its own `CLAUDE.md` with details. This file is the map — jump to the right sub-guide instead of re-reading source.

## Stack at a glance

| Layer | Tech | Location |
|-------|------|----------|
| Frontend | Next.js 15 (App Router), React 19, TypeScript, Tailwind v4, shadcn/ui, Auth0 | `web/` |
| Backend API | .NET 8, Clean Architecture, ASP.NET Core, MediatR (CQRS), Hangfire | `api/` |
| Database | PostgreSQL 16 (EF Core) | docker-compose `postgres` |
| Object storage | MinIO (S3-compatible) | docker-compose `minio` |
| Auth | **Pluggable** by `Auth:Mode`: **Cloud** = Auth0 (JWT bearer); **Local** = self-issued email+password accounts for offline LAN installs. Clinic membership resolved server-side. | both |
| External | Google Calendar (two-way sync), HuggingFace (AI chat) | `api/...Infrastructure/Services` |

## Layout

```
clinic-management/
├── api/                          .NET 8 Clean Architecture solution (ClinicManagement.sln)
│   ├── ClinicManagement.Domain/         → CLAUDE.md  (entities, value objects, domain events, repo interfaces)
│   ├── ClinicManagement.Application/     → CLAUDE.md  (CQRS features, MediatR pipeline, DTOs, Result<T>)
│   ├── ClinicManagement.Infrastructure/  → CLAUDE.md  (EF Core, repos, external services, DI)
│   └── ClinicManagement.API/             → CLAUDE.md  (controllers, background jobs, Program.cs startup)
├── web/                          Next.js frontend
│   ├── (root)                            → CLAUDE.md  (stack, routing, API/auth integration)
│   ├── components/                       → CLAUDE.md  (feature components + shadcn/ui primitives)
│   └── lib/                              → CLAUDE.md  (API client layer, hooks, utils)
├── backend/                      EMPTY (only .idea/) — ignore
├── docker-compose.yml            postgres (5432) + minio (9000 API / 9001 console)
└── *.md (root setup docs, see below)
```

The **dependency direction** in `api/` is strict Clean Architecture: `API → Application → Domain`, with `Infrastructure` implementing Application's outbound interfaces. Domain has no infrastructure dependencies.

## Where things live (quick index)

- **A REST endpoint / route** → `api/ClinicManagement.API/Controllers/` (12 controllers). Controllers are thin MediatR pass-throughs.
- **Business logic / a use case** → `api/ClinicManagement.Application/Features/<Area>/{Commands,Queries}/` (handlers).
- **An entity / business rule / domain event** → `api/ClinicManagement.Domain/Entities/`.
- **DB schema / a query implementation / EF config** → `api/ClinicManagement.Infrastructure/Persistence/` + `Repositories/`.
- **An external integration** (Google Calendar, AI, files, notifications) → `api/ClinicManagement.Infrastructure/Services/`.
- **A page / screen** → `web/app/<route>/page.tsx` (App Router).
- **A UI component** → `web/components/` (feature) or `web/components/ui/` (shadcn primitives).
- **Frontend → backend calls** → `web/lib/api/` (per-resource modules over `client.ts`).

## Running locally

```bash
docker compose up -d              # postgres + minio
cd api/ClinicManagement.API && dotnet run    # API (default http://localhost:5000)
cd web && npm install && npm run dev          # frontend (http://localhost:3000)
```
Frontend talks to the API via `NEXT_PUBLIC_API_URL` (default `http://localhost:5000/api`). EF migrations live in `Infrastructure/Migrations`.

## Key architectural notes (verified, may surprise you)

- **Multi-tenancy**: every request is scoped to a clinic. The clinic is resolved per-request (`IClinicContext` → DB lookup of the Auth0 `sub`), not purely from the JWT claim.
- **Pluggable auth (`Auth:Mode` = `Cloud` | `Local`)**: Cloud is the original Auth0 path; **Local** (for offline Windows/LAN installs) issues its own HS256 JWTs against local email+password accounts. Backend seam: `ILocalAuthService`/`LocalAuthService` (+ per-install signing key via `LocalAuthConfig`), a mode-branched JWT setup in `Program.cs`, and `AuthController` (`login`/`setup`/`register`/`mode`/`change-password`). `CreateClinicCommand`/`JoinClinicCommand` branch to a Local path when a `Password` is present. Frontend seam: a single `useSession()` context (`web/lib/auth/session.tsx`) backed by either `CloudSessionProvider` (Auth0) or `LocalSessionProvider` (HttpOnly cookie), gated on `AUTH_MODE`. All Local behavior is additive; the Cloud path is unchanged. Offline admin lockout recovery is a console command (`dotnet run -- reset-admin-password`), not a web endpoint. *Phases 1–4 of a 5-phase repackaging are complete — see `features/windows-desktop-app/`.*
- **Local-disk file storage (Phase 2)**: the single `IFileStorage` seam is mode-branched — `LocalDiskFileStorage` (Local, blobs under `FileStorage:BasePath`) vs `MinioFileStorage` (Cloud). Additive; Cloud unchanged.
- **Connectivity awareness (Phase 3, Local mode)**: internet reachability is judged by the **server** (LAN clients may have no egress). `IInternetProbe`/`InternetProbe` (Singleton, cached) backs an anonymous, Local-only `GET /api/connectivity` (404 in Cloud) that the frontend polls via `ConnectivityProvider`/`useConnectivity()`. The two internet-dependent features — AI chat + Google Calendar — visibly disable offline and auto re-enable; appointments not yet pushed to Google show a "non synchronisé" badge + manual "Push to Google" (`AppointmentDto.IsSyncedToGoogle`). Cloud gets a static "online" default and behaves as before.
- **LAN hosting & security gates (Phase 4, Local mode)**: hardens the API for offline-LAN hosting, all additive/gated to Local (Cloud byte-for-byte unchanged). (a) Fail-closed authorization — `AuthorizationPolicies.ConfigurePolicies(options, isLocalMode)` installs a `FallbackPolicy = RequireAuthenticatedUser()` in Local, so anything without an explicit `[AllowAnonymous]` returns 401; the exact allow-list is pinned by `ControllerAuthorizationCoverageTests`. (b) Loopback-only `/hangfire` in Local (`HangfireAuthorizationFilter` + `LocalRequest.IsLoopback`, shared with the `setup` gate / AC-1.2a). (c) Config-driven CORS (`CorsOrigins` — LAN origins via `Cors:AllowedOrigins`), HTTPS cert binding + guarded redirect, and Kestrel bind (`Https:*`/`Hosting:*`) in `Program.cs`. (d) `IGoogleTokenStore`/`FileGoogleTokenStore` (Singleton, `.local/` file) replaces the OAuth-callback appsettings rewrite. Known residual gaps (feature-review, Local mode): OAuth `state` is still unvalidated on the anonymous `authorize`/`callback` (token-hijack risk), and a set-but-missing `Https:CertPath` silently downgrades to plain HTTP.
- **Google Calendar sync is asymmetric**: App→Google runs inline on appointment create/update. Google→App is implemented but **disabled** (recurring job removed in `Program.cs`); only runs via the manual `GoogleCalendarController` endpoint.
- **Background jobs mostly idle**: Hangfire is wired but `NotificationJob`/`AISummaryJob`/calendar-sync recurring registrations are commented out. Only on-demand `PdfGenerationJob` fires in practice.
- **Stubs/placeholders**: `NotificationService` logs instead of sending email/SMS; `PatientSummaryService` is a string template (no AI call); `GoogleAIService` exists but isn't registered (HuggingFace is the wired AI backend).
- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return `Result.Failure`.
- **Several frontend surfaces use hardcoded sample data** (dashboard stats, appointment-list, notifications-list, the whole stock feature) — not yet wired to the API.
- ⚠️ **Security debt**: `api/.../appsettings.json` still contains real-looking secrets. **Phase 4 retired several items in Local mode** — the OAuth callback no longer rewrites the refresh token into appsettings (now `IGoogleTokenStore` → `.local/`), the Hangfire filter is loopback-only in Local (not `return true;`), and anonymous-by-omission endpoints fail closed via the Local `FallbackPolicy`. **In Cloud these largely remain**: the fallback stays null (so a controller without `[Authorize]` is still anonymous), and the Hangfire filter is unchanged (authorizes everyone). Treat with care.

## Root-level setup / reference docs

| File | Topic |
|------|-------|
| `README.md` | (minimal) |
| `AUTH0_SETUP.md` | Auth0 tenant/app configuration |
| `GOOGLE_CALENDAR_SETUP.md` / `_FR.md` | Google Calendar OAuth setup (EN/FR) |
| `GOOGLE_CALENDAR_SYNC_ARCHITECTURE.md` | Calendar sync design |
| `SYNC_TESTING_GUIDE.md` | How to test calendar sync |
| `GOOGLE_AI_SETUP.md`, `HUGGING_FACE_SETUP.md` | AI provider setup |
| `api/CLINIC_MANAGEMENT_FLOW.md`, `MULTI_CLINIC_SETUP.md`, `ROLE_ASSIGNMENT_IMPLEMENTATION.md`, `ENTITY_UPDATE_GUIDE.md`, `IMPLEMENTATION_SUMMARY.md` | Backend feature/process docs |

> When code changes, update the nearest `CLAUDE.md` so this map stays accurate.

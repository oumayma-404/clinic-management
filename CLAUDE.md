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
| Auth | Auth0 (JWT bearer; clinic membership resolved server-side) | both |
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
- **Google Calendar sync is asymmetric**: App→Google runs inline on appointment create/update. Google→App is implemented but **disabled** (recurring job removed in `Program.cs`); only runs via the manual `GoogleCalendarController` endpoint.
- **Background jobs mostly idle**: Hangfire is wired but `NotificationJob`/`AISummaryJob`/calendar-sync recurring registrations are commented out. Only on-demand `PdfGenerationJob` fires in practice.
- **Stubs/placeholders**: `NotificationService` logs instead of sending email/SMS; `PatientSummaryService` is a string template (no AI call); `GoogleAIService` exists but isn't registered (HuggingFace is the wired AI backend).
- **`ValidationBehavior` is inert**: no FluentValidation validators exist; handlers validate inline and return `Result.Failure`.
- **Several frontend surfaces use hardcoded sample data** (dashboard stats, appointment-list, notifications-list, the whole stock feature) — not yet wired to the API.
- ⚠️ **Security debt**: `api/.../appsettings.json` contains real-looking secrets; the OAuth callback rewrites the refresh token back into appsettings at runtime; some controllers are `[AllowAnonymous]`; the Hangfire dashboard auth filter returns `true` for everyone. Treat with care.

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

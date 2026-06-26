---
name: start-clinic
description: Launch the clinic-management stack locally, end to end — Docker (Postgres + MinIO), the .NET API with auto-applied EF migrations, the Next.js frontend, and AI (HuggingFace). Brings services up in dependency order, waits for each to be healthy, and reports the URLs. Automatically use when the user requests to "start the app", "launch the project", "run everything", "start dev", "bring up the stack", "run the clinic app locally", or "get everything working".
---

# Start Clinic (Local End-to-End)

Bring the whole clinic-management project up locally so every part works: database, object storage, API, frontend, and the AI assistant.

## How to Use This Skill

**Input:** A request to run the project locally (optionally "reset the database" or "skip docker").
**Output:** All tiers running and verified healthy, with the URLs reported to the user.

## Architecture (why the order matters)

```
Docker (Postgres 5432 + MinIO 9000/9001)   ← must be up first
        ↓
.NET API  http://localhost:5000            ← runs EF migrations on startup; AI = HuggingFace key in appsettings
        ↓
Next.js   http://localhost:3000            ← reads NEXT_PUBLIC_API_URL from web/.env.local
```

- **AI works automatically** — the HuggingFace API key lives in `api/.../appsettings.json`; nothing extra to start.
- **Migrations are automatic** — `Program.cs` calls `context.Database.Migrate()` on boot; no manual `dotnet ef` step.
- **Auth0** is pre-configured in both `appsettings.json` and `web/.env.local`.

## Steps

1. **Run the launcher** (idempotent — it skips any tier already running):
   ```powershell
   .\.claude\skills\start-clinic\scripts\start.ps1
   ```
   Variants: `-SkipDocker` (Postgres/MinIO already up elsewhere) · `-Reset` (recreate Docker volumes, **drops the DB**).

2. **Watch the output.** The script waits for each tier and prints `[ ok ]` per tier, ending with a `STACK READY` summary. If a tier shows `[warn]`, read its log (paths printed) and consult [references/troubleshooting.md](references/troubleshooting.md).

3. **Verify** these respond before declaring success:
   | Tier | Check |
   |------|-------|
   | API | `http://localhost:5000/swagger` returns the Swagger UI |
   | Frontend | `http://localhost:3000` loads |
   | AI | In the app's chat widget, a prompt returns a reply (exercises the API → HuggingFace path) |

4. **Report** the URL table from the summary block to the user.

## Service URLs

| Service | URL | Notes |
|---------|-----|-------|
| Frontend | http://localhost:3000 | Next.js dev server |
| API / Swagger | http://localhost:5000/swagger | |
| Hangfire dashboard | http://localhost:5000/hangfire | background jobs |
| MinIO console | http://localhost:9001 | `minioadmin` / `minioadmin` |
| Postgres | localhost:5432 | `clinic_user` / `clinic_password`, db `clinic_management` |

## Stopping

```powershell
.\.claude\skills\start-clinic\scripts\stop.ps1
```
`-KeepDocker` leaves Postgres/MinIO up · `-Reset` also drops the DB volumes.

## When the script isn't appropriate

If the user wants to run a single tier, or a step fails and needs manual control, run the tiers by hand — see [references/troubleshooting.md](references/troubleshooting.md) for the exact per-tier commands, log locations, and common failures (port in use, Docker down, missing `.env.local`, .NET SDK/runtime mismatch).

## Notes

- Scripts are **PowerShell** (Windows is the dev environment). They self-locate the project root via `docker-compose.yml`, so they work regardless of where the repo lives.
- API + frontend run as **detached background processes**; their stdout/stderr go to `%TEMP%\clinic-management-run\`.
- Re-running `start.ps1` is safe — already-listening tiers are skipped.

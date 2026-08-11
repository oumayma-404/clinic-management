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
          http://localhost:5443            ← the VENDOR CONSOLE listener, a second Kestrel endpoint
        ↓
Next.js   http://localhost:3000            ← reads NEXT_PUBLIC_API_URL from web/.env.local
Next.js   http://localhost:3100            ← `console/`, the vendor back-office (CONSOLE_API_URL → :5443)
```

⚠️ **The console is a fourth tier, not a page of the third.** It is a separate Next app that contains none of the
clinic bundle (that separation *is* the requirement, not packaging), and it exists only on
`Deployment:Profile = HostedMultiTenant` — which local dev already uses. Its two keys (`Console:Port`,
`Console:SigningKey`) live in `appsettings.Development.json`: with them absent the console is **absent**, and every
`/api/platform/*` path 404s everywhere rather than refusing.

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
| Hangfire dashboard | http://localhost:5000/hangfire | background jobs — loopback-only in every profile, which is why it works here and not in a deployment |
| **Vendor console** | http://localhost:3100 | The editor's back-office. Sign-in needs a password **and** a TOTP code; see below |
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
- **The API runs `-c Release` by default.** Smart App Control on this machine intermittently refuses a freshly-built
  *Debug* assembly (`FileLoadException 0x800711C7`), which kills `dotnet run` before it binds anything; a Release
  build emits different bytes, so SAC judges a different file. `-ApiDebug` opts back in when you need a debugger.
- **The compose project name is pinned to `clinic-management`.** Run from a git *worktree* and the derived name would
  be the worktree's directory, so compose would invent a second stack with empty volumes and the script would wait on
  a database holding none of your data.

## The vendor console, first time

`-SkipConsole` opts out of all of this. Otherwise the script does three things the clinic tiers do not need:

1. **Creates the first console account** if `PlatformAccounts` is empty. There is no sign-up screen anywhere in the
   console — a verb is the only door — and it prints a one-time password **and** a TOTP enrolment secret, each shown
   **once**, unrecoverable afterwards. Put the secret into any RFC 6238 authenticator app; if you lose it,
   `platform-account --reset-totp --email …` issues a new one and invalidates the old one plus every recovery code.
2. **Triggers `count-clinic-activity` once.** It is a *daily* job (03:00 UTC), so without this every cabinet reads
   « jamais mesuré » — correct, and useless for testing. That is a real state, not a bug: a deployment whose counter
   pass has never run says so rather than presenting a portfolio of dormant practices.
3. **Starts `console/` on 3100** with `CONSOLE_API_URL` pointed at the console listener. That variable is read
   **server-side only** — every console read is a server component, because the session cookie is HttpOnly and
   browser JavaScript must never see the token.

Then sign in at http://localhost:3100/login with the printed password and a generated code. The first sign-in walks
through enrolment, returns eight recovery codes (also once), and then requires the one-time password to be changed
before any other route answers.

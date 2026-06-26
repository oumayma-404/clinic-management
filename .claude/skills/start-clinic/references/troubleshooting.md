# Troubleshooting & Manual Launch

Reference for when `start.ps1` warns/fails, or when you need to run one tier by hand.

## Logs

The launcher redirects background process output to `%TEMP%\clinic-management-run\`:

| File | Contents |
|------|----------|
| `api.out.log` / `api.err.log` | .NET API stdout / stderr |
| `web.out.log` / `web.err.log` | Next.js dev server stdout / stderr |

The API also writes Serilog files to `api/ClinicManagement.API/logs/clinic-management-*.log`.

## Manual per-tier launch

Run from the project root.

**1. Docker (Postgres + MinIO)**
```powershell
docker compose up -d
docker compose ps          # both should be "healthy"
```

**2. API** (auto-applies migrations on startup; serves on port 5000)
```powershell
cd api\ClinicManagement.API
dotnet run --launch-profile http
```
Ready when `http://localhost:5000/swagger` loads.

**3. Frontend**
```powershell
cd web
npm install      # only if node_modules is missing
npm run dev
```
Ready when `http://localhost:3000` loads.

## Common failures

| Symptom | Cause | Fix |
|---------|-------|-----|
| `port 5000/3000 already in use` | A previous run is still up | Run `stop.ps1`, or `start.ps1` again (it skips listening tiers). |
| API exits immediately, log shows Npgsql connection refused | Postgres not ready / not running | Ensure `docker compose ps` shows postgres `healthy`; re-run. |
| API exits: `framework 'Microsoft.NETCore.App' version '8.x' not found` | Only a newer .NET SDK installed; net8.0 runtime missing | Install the .NET 8 runtime, or add `<RollForward>LatestMajor</RollForward>` — the projects target `net8.0`. |
| Frontend loads but every API call fails (CORS / 401 / network) | `web/.env.local` missing or wrong `NEXT_PUBLIC_API_URL` | Ensure `web/.env.local` exists with `NEXT_PUBLIC_API_URL=http://localhost:5000/api` and the Auth0 vars (see `web/CLAUDE.md`). |
| AI chat returns an error | HuggingFace key missing/invalid in `appsettings.json`, or API not reachable | Confirm `HuggingFace:ApiKey` is set (see `HUGGING_FACE_SETUP.md`); confirm the API tier is healthy. |
| MinIO-backed file upload fails | MinIO container down / bucket missing | Ensure the `minio` container is up (`http://localhost:9001`); the API creates the bucket on demand. |
| Migrations error on startup | DB schema drift after a `-Reset` mid-state | `stop.ps1 -Reset` then `start.ps1` to recreate a clean database. |

## Tiers & ports (quick reference)

| Tier | Port | Health check |
|------|------|--------------|
| Postgres | 5432 | TCP / `docker compose ps` healthy |
| MinIO API | 9000 | TCP |
| MinIO console | 9001 | http |
| .NET API | 5000 | `GET /swagger/index.html` |
| Next.js | 3000 | `GET /` |

## Notes

- `start.ps1 -SkipDocker` assumes Postgres + MinIO are already provided.
- `-Reset` (on either script) removes Docker volumes — **all DB data is lost**. Migrations recreate the schema on next API start, but seeded/entered data is gone.
- Background processes survive the script exiting. Use `stop.ps1` to terminate them cleanly (it kills whatever is listening on 3000 and 5000).

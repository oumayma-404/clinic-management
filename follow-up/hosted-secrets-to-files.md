# The sidecars' secrets still reach them as environment variables

**From:** `features/hosted-security-hardening/` Part C (Custody), FR-3.10
**Raised:** 2026-08-12 · **Scope:** `deploy/` only — no application code
**Decided with the user** during Part C: move the application's own secrets now, the sidecars' with their own
mechanisms later.

## What landed

`API/Startup/FileBackedSecrets.cs` adds a `*_FILE` configuration layer to `AddInstallLayers()`, so **every**
secret the .NET application reads can be supplied as a file. It is applied by the host and by every console
verb through the one method they share, a `*_FILE` variable beats a literal of the same name, and a named file
that is missing or empty is a **startup refusal** rather than an empty value. Converted in both compose files:

| Secret | Where |
|---|---|
| `DataProtection__CertificatePassword` | hosted |
| `Console__SigningKey` | hosted |
| `Auth__Local__SigningKey` | hosted |
| `GoogleCalendar__ClientSecret` | hosted + prod |
| `HuggingFace__ApiKey` | hosted + prod |
| `Auth0__ManagementApi__ClientSecret` | prod |

## What is still in `environment:`, and why it was left

Two classes, and the second is the real reason this is a follow-up rather than an omission.

**1. Secrets the application shares with a container that is not .NET.** `POSTGRES_PASSWORD` (inside
`ConnectionStrings__DefaultConnection`) and `MINIO_ROOT_PASSWORD` (inside `MinIO__SecretKey`) are also read by
`postgres`, `minio`, `backup` and `pitr`. Moving only the API's copy would leave the same password in three
other containers' environments **while the compose file implied it had left** — which is worse than not moving
it, because it converts a visible gap into an invisible one.

**2. The sidecars themselves need per-image mechanisms, and none can be verified here.**

| Container | Secret | Mechanism |
|---|---|---|
| `postgres` | `POSTGRES_PASSWORD` | `POSTGRES_PASSWORD_FILE` — supported natively by the official image |
| `minio` | `MINIO_ROOT_PASSWORD` | `MINIO_ROOT_PASSWORD_FILE` — supported natively |
| `backup`, `pitr` | `PGPASSWORD` | `PGPASSFILE` (libpq's own convention) — a *different* shape: a `.pgpass` file of `host:port:db:user:password` lines at mode `0600`, not a bare value |
| `pitr`, `postgres` | `WALG_LIBSODIUM_KEY`, `AWS_SECRET_ACCESS_KEY` | ⚠️ **wal-g has no `_FILE` convention at all.** Either a wrapper entrypoint that reads the file and `export`s before `exec`ing, or accept the environment for these two and say so |

## Why it is not done blind

Every one of those is a change to how a container **authenticates at boot**, and none of them can be cold-started
in the development environment: the hosted stack needs a real domain for Let's Encrypt, and the failure mode is
not a build error — it is the nightly dump failing at 02:00, or PITR silently ceasing to archive, each
discovered by needing the backup. Part B's own findings list two defects of exactly that shape
(MinIO's healthcheck had never run; every `deploy/*.sh` was unbuildable on Windows), both invisible until
somebody brought the stack up.

## What doing it looks like

1. Add the four `secrets:` entries and switch `postgres`/`minio` to their native `*_FILE` variables.
2. Write a `.pgpass` secret and set `PGPASSFILE` on both sidecars; drop `PGPASSWORD`.
3. Decide wal-g: wrapper entrypoint, or a stated exception in `deploy/KEY-CUSTODY.md`.
4. Compose the API's connection string from a file too, once its password is no longer in the environment —
   the whole string, since the `*_FILE` layer maps a configuration **key**, not a substring.
5. **Cold-start both compose files from nothing** and confirm: postgres accepts the API's connection, the API
   reaches MinIO, one `backup` run completes and its archive decrypts, one `pitr` base backup completes.
6. `docker exec clinic-api-prod env | grep -Ei 'password|apikey|token|secret' | grep -v '_FILE='` returns
   nothing — the check `deploy/README.md` already documents.

## Related

- `deploy/KEY-CUSTODY.md` — the four keys and what losing each one costs
- `features/hosted-security-hardening/stories/progress.md` — Part C's own record, DEV-11

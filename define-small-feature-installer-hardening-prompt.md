/define-small-feature Make the offline-LAN Windows server installer reliable on both a fresh install and an in-place reinstall/upgrade. No new app features — this is packaging/installer robustness only, all Local-mode, Cloud unaffected. Group both fixes below into one small feature.

## Context — already fixed this session (do NOT redo; treat as baseline to build on)
The API Windows-service first-boot SCM-timeout root cause is already addressed and working:
- `api/ClinicManagement.API/Startup/DeferredStartupService.cs` (new) defers EF migrations off the service-start critical path in Local mode; `Program.cs` registers it (Local only) and keeps synchronous migrations for Cloud only.
- `api/ClinicManagement.API/ClinicManagement.API.csproj` sets `<PublishReadyToRun>true</PublishReadyToRun>`.
- `packaging/server/clinic-server.iss` readiness gate now polls `pg_isready` in a retry loop (~60s) instead of a single shot.
- The client installer was recompiled with the server CA embedded (`packaging/build-output/client/ca/ca.crt`).
These are applied (currently uncommitted). The spec must build ON them, not re-do or revert them.

## 1. Eliminate the first-boot service-start window on a truly fresh install (pre-provision cert + warm at install time)
Even with deferred migrations + R2R, a brand-new machine's FIRST service boot still pays one-time costs that approach the ~30s SCM start timeout: HTTPS cert generation (RSA keygen for the CA + server cert, ~5–15s, `CertificateProvisioner.EnsureServerCertificate()` at api/ClinicManagement.Infrastructure/Security/CertificateProvisioner.cs:45, invoked from Program.cs:~312) plus cold JIT/load of freshly-written assemblies. Move this one-time work to INSTALL time (no SCM limit) so the service's first start reuses an existing cert and warm binaries.
DECISION (pre-answered): add a one-shot CLI branch to the API — mirror the existing `reset-admin-password` pattern (intercepted at Program.cs:22; implemented in Maintenance/AdminPasswordResetCommand.cs, `CommandName` at :23) — e.g. `provision-cert`, which runs `CertificateProvisioner.EnsureServerCertificate()` and returns 0. Then in `packaging/server/clinic-server.iss` `SetupAppServices`, run `{app}\api\ClinicManagement.API.exe provision-cert` BEFORE `sc start {#ServiceApi}` (currently clinic-server.iss:347). This (a) generates the cert into `.local/` at install time and (b) JIT/loads + warms the core assemblies, so the first service boot is fast and inside the SCM window. `provision-cert` must be idempotent (reuse an existing cert, never regenerate — `EnsureServerCertificate()` already is).
Also fix the CA-export chicken-and-egg this exposes: the server installer's export of the CA to `%ProgramData%\ClinicManagement\ca.crt` currently no-ops because the cert doesn't exist until first boot (observed: that file was absent). With the cert provisioned at install time, wire/verify the installer exports the CA in the same run, so the client installer's embedded-CA step (and manual client trust) has a real source.

## 2. Idempotent DB credentials across reinstalls (support in-place upgrade, no data wipe)
`packaging/server/clinic-server.iss` regenerates BOTH DB passwords on every run: `DbPassword := NewRandomPassword` (clinic-server.iss:372) and `PgSuperPassword := NewRandomPassword` (:373). On a reinstall over an existing cluster this breaks the install:
- `pgdata` already exists → `initdb` is skipped → the cluster keeps the OLD superuser + clinic_user passwords.
- The role step only creates `IF NOT EXISTS` (clinic-server.iss:248) → clinic_user keeps its OLD password.
- But the pgpass + psql bootstrap (clinic-server.iss:241-242), run as `postgres` with the NEW superuser password, fails authentication → "Échec de la création du rôle/de la base" abort. And appsettings.Production.json is written with the NEW clinic_user password (ConnStr at clinic-server.iss:127) that the existing DB doesn't know → API can't connect even if the install "succeeds".
This is why every reinstall this session required wiping `pgdata`.
DECISION (pre-answered): persist the generated credentials on first install and REUSE them on subsequent installs. Store the superuser + clinic_user passwords in a per-install, gitignored file under the install dir (e.g. `.local/db-credentials`, matching the `.local/` secret-store convention), written on first install. On any run where that file AND a valid `pgdata` already exist, READ both passwords from it instead of calling `NewRandomPassword`, so pgpass, the role step, and appsettings all use the passwords the existing cluster actually has. First install (no file / fresh cluster) keeps generating fresh random passwords exactly as today. Result: an in-place reinstall/upgrade succeeds without wiping the PostgreSQL data.

## Scope boundaries (state these as decisions in the spec)
- Local/offline-LAN installer only. Cloud is unaffected (there is no installer in Cloud) and must stay byte-for-byte.
- Do NOT touch the already-applied fixes (deferred migrations, R2R, pg_isready retry loop, client-CA embed) except to build on them.
- No app/runtime feature changes and no DB schema changes. This is packaging (`clinic-server.iss`) plus one thin, idempotent API CLI branch (`provision-cert`) only.
- Packaging is operator-verified (R-1) — not CI-runnable. Acceptance = the installer recipe + the new CLI branch compile clean (`dotnet build`), plus the documented operator verification steps. Update `packaging/README.md` (fresh-install + in-place-reinstall/upgrade sections) accordingly.

Goal: after this pass, a fresh server install comes up on first boot with no manual `Start-Service` and no client cert warning (CA provisioned + exported for the client installer), and a reinstall over an existing install upgrades in place without wiping the PostgreSQL database.

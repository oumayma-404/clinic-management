# Context — Hosted Security Hardening

**Written:** 2026-08-12 at `cbccbe0` (tip of Part B) · **Last verified:** 2026-08-12, end of **Part D** (all four parts landed)

Durable **codebase pointers** for this four-part story. Status and deviations live in `progress.md`; this file
only says *where things are*. It caches **paths, commands and which file owns which rule** — never a signature,
never a snippet, never another feature's status. Open the file; the part shipped last session is the most likely
thing to have changed it.

## Staleness check — run this first

```bash
git diff --stat <last-verified-sha>..HEAD -- \
  api/ClinicManagement.Infrastructure/Security/ \
  api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs \
  api/ClinicManagement.Application/Common/Interfaces/ISchemaVerificationReader.cs \
  api/ClinicManagement.Infrastructure/Persistence/SchemaVerificationReader.cs \
  api/ClinicManagement.API/Program.cs \
  api/ClinicManagement.API/Startup/InstallConfiguration.cs \
  api/ClinicManagement.API/Maintenance/ \
  api/ClinicManagement.Domain/Services/AuditChain.cs \
  api/ClinicManagement.Infrastructure/Persistence/AuditChainAppender.cs \
  api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs \
  api/ClinicManagement.API/Middleware/SecurityHeadersMiddleware.cs \
  deploy/
```

(Use the SHA of the commit named in « Last verified ».) Empty ⇒ every pointer below holds; re-stamp and skip to the gate table. Non-empty ⇒ re-read only the listed
files that moved, correct the rows **in place**, re-stamp. Re-check **⚠️ Volatile** regardless.

## Where the work happens

| | |
|---|---|
| Worktree | `.claude/worktrees/hosted-security-hardening/` — **open a session with this as the cwd** |
| Branch | `feature/hosted-security-hardening`, based on `9a90d54` (tip of `feature/windows-desktop-app`), **not `main`** |
| Main checkout | stays on `feature/windows-desktop-app` with 40+ uncommitted files — never `git switch` this branch there |

## Verified gate commands

| Gate | Command | Notes |
|---|---|---|
| Backend suite | `dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release -p:BaseOutputPath=<temp-outside-repo>` | **`-c Release`** and an out-of-repo `BaseOutputPath` are both load-bearing: Smart App Control refuses freshly-built Debug test assemblies (`0x800711C7`), and the running API locks in-repo `bin/`. ⚠️ In PowerShell never end the path with `\` inside double quotes — the trailing `\"` escapes the quote and MSBuild silently builds to `bin/` and reports success. Never `--no-build`. A block is **transient — retry** |
| Backend warnings | `dotnet build ... --no-incremental` | An incremental build reports « 0 Warning(s) » by recompiling nothing. Baseline is ~55 standing `CS8618`/`CS8602`/`CS8981`/`CS0618`; the gate is **no new ones in files this part touched** |
| `web/` | `npm run check:responsive && npx tsc --noEmit && npm run build` | No test runner, no working ESLint, no CI — this *is* the gate |
| `console/` | same three | second Next app, its own `check:responsive` |
| Schema | `cd api/ClinicManagement.API && dotnet run -- verify-schema` | **Exists** (`Maintenance/VerifySchemaCommand.cs`). Run **before and after** a migration and **diff**. Exit 0 clean / 1 couldn't run / 2 drift |
| Compose parse | PyYAML `yaml.safe_load` **and** `docker compose config` with a filled `.env` | both were used in Part B |

**Gates that do NOT exist in this repo:** no backend integration/E2E suite, no frontend test runner, no working
`web/` ESLint, no `console/` lint, no Newman/Postman. Nothing in `UnitTests` touches a database — which is why
`verify-schema` is the only gate a migration has. Do not write « not applicable » for a gate without first
confirming it exists.

## Reference implementations — imitate these

| Need | Read |
|---|---|
| A console verb (wiring, config, tenant scope, audit actor, exit codes) | `api/ClinicManagement.API/Maintenance/ResetUserTotpConsoleCommand.cs` — Part A's, the closest shape. Dispatch branch is in `Program.cs`; the reachability guard is `UnitTests`' `SubscriptionVendorCommandReachabilityTests` |
| A read-only report verb (container from `AddInfrastructure` **only**, `UseSystemWide`, exit 2 on drift) | `Maintenance/VerifySchemaCommand.cs` + `Maintenance/ReconcileMoneyCommand.cs` |
| Adding a `verify-schema` check | fact on `Application/Common/Interfaces/ISchemaVerificationReader.cs` → read it in `Infrastructure/Persistence/SchemaVerificationReader.cs` → assert it in `Application/Common/Maintenance/SchemaVerificationService.cs`. `Info` vs `Drift` vs « not applicable » via `NotApplicableIn` |
| A **non-schema** side on `SchemaFacts` (something the database does not hold) | Part B's `InternalCertificateFact` — the reader takes an **optional** dependency and returns `null` ⇒ « not applicable » |
| Data Protection configuration (the one definition both the host and the verbs use) | `Infrastructure/Security/LocalDataProtection.cs` |
| The secret-protection seam to extend | `Application/Common/Interfaces/IPlatformSecretProtector.cs` — **returns `bool`, deliberately not a nullable somebody could `??` past**; impl `Infrastructure/Security/PlatformSecretProtector.cs`. Sibling: `Infrastructure/Services/ReminderSecretProtector.cs` (throws instead) |
| « Refuse, never degrade » on a failed unprotect | `Application/Features/Platform/Auth/PlatformLoginCommand.cs` `VerifyTotp` |
| Config layers (host **and** every console verb) | `API/Startup/InstallConfiguration.cs` → `AddInstallLayers()` / `BuildForConsoleVerb()` |
| A derived guard test (docstring criterion · reflected candidate set · `Assert.NotEmpty` · both-direction exception map · executed red proof) | `exploration.md` § 5.1 names the house style; live examples are `ClinicStorageKeyTests`, `TenantScopeFilterTests`, `PlatformReadShapeTests` |
| Fail-loud startup refusal naming the setting **and** the file | `API/Startup/TransportAssurance.cs` (Part B) |
| Adding a row to the audit ledger from OUTSIDE the interceptor | `Application/Features/Backup/ArchiveAccessLedger.cs` — stage through `IAuditEntryRepository`, then save. The chain is assigned by `ApplicationDbContext.SaveChangesAsync`, so **no caller ever touches `AuditChainAppender`** (progress.md DEV-14) |
| The one CSP string, and what holds its three copies together | `API/Middleware/SecurityHeadersMiddleware.ContentSecurityPolicy` is the authority; `deploy/Caddyfile` (**two** sites) and `console/next.config.ts` copy it, and `UnitTests/Common/ContentSecurityPolicyAgreementTests` fails the build on drift |
| Masking a value that must not reach a log file | `Infrastructure/Services/LogMask.cs` (names, file names) beside `ReminderPhone.Mask` (phones); `UnitTests/Common/LogTemplateCoverageTests` is the derived guard, and its exemption map is **empty** |
| A « required, except in Development » gate | `Infrastructure/Storage/MinioCredentials.TolerateUnconfigured` — the repo's precedent, followed by `LocalDataProtection.TolerateUnprotectedKeyRing` (Part C) |
| A secret supplied as a **file** rather than an env var | `API/Startup/FileBackedSecrets.cs`; the layer is added inside `AddInstallLayers()` so the host and every verb share it |
| A **derived** guard with a decision map + reasons + both directions + an in-test red proof | `UnitTests/Common/SecretProtectionCoverageTests.cs` and `UnitTests/Api/ConsoleVerbDispatchTests.cs` (Part C) |

## Where the six protected column families live

| Family | Owner | Protector |
|---|---|---|
| SMS API key · WhatsApp token · SMTP password | `Domain/Entities/ClinicReminderSettings.cs` (`*Encrypted`) | `IReminderSecretProtector` |
| Console second factor | `Domain/Entities/PlatformAccount.cs` | `IPlatformSecretProtector` |
| Clinic second factor (Part A) | `Domain/Entities/User.cs` | `IPlatformSecretProtector`'s clinic sibling |
| Google refresh token (Part C) | `Domain/Entities/Clinic.cs`, `GoogleRefreshTokenProtected` beside the legacy plaintext column | `IGoogleTokenProtector` |

⚠️ **Four protectors, one shape.** Each family has its **own purpose string** — that is what keeps the
ciphertexts from being interchangeable, and it is enforced by the framework's key derivation, not by convention.
Collapsing them into one seam taking a purpose parameter is worth doing and was deliberately **not** done in
Part C (progress.md DEV-13): it touches the sign-in path.

⚠️ **Which key-ring generation a ciphertext is under** is `Infrastructure/Security/DataProtectionKeyGeneration`
— one authority for the `reprotect-secrets` verb, `verify-schema`'s coverage figure and the FR-3.9 dump stamp.
Its `IdOf` is the only place that knows the key id's byte order; rendering one as a canonical GUID silently
breaks every FR-3.9 comparison.

## Deploy layout

`deploy/` — `docker-compose.prod.yml` (CloudBrowser; holds the shared infra definitions) ·
`docker-compose.hosted.yml` (`extends` it; ⚠️ `extends` drops `depends_on`, so every one is restated) ·
`docker-compose.selfhosted-lan.yml` · `Caddyfile` · `README.md` (operator guide) · `backup/{Dockerfile,backup.sh,entrypoint.sh}` ·
`postgres/{Dockerfile,pg_hba.conf,pitr-backup.sh,pitr-entrypoint.sh}` · `certs/{Dockerfile,issue.sh}` (Part B) ·
`.gitattributes` (Part B — `*.sh`/`*.conf`/`Dockerfile`/`Caddyfile` are `eol=lf`; **CRLF breaks every image built on Windows**).

## ⚠️ Volatile — re-check every session regardless of the diff

| Fact | Checked |
|---|---|
| Part status (which parts have landed) | read `progress.md`, never this file |
| Whether a symbol a later part depends on exists yet | grep for it. `reprotect-secrets`, `AuditChain`, `AuditChainAppender`, `ArchiveAccessLedger`, `LogMask` and `CspReportController` **all now exist** |
| A scratch container left behind by a verification run (`hshb-c-pg`, Part C — removed; `hshb` project, Part B) | `docker compose -p hshb -f deploy/docker-compose.hosted.yml down -v`. It holds an **empty** database; the dev one is the main checkout's `clinic-postgres` on 5432 |
| Working-tree cleanliness in the worktree | `git status` — it should be clean between parts |

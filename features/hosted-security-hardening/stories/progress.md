# Progress — Hosted Security Hardening

**Story:** [story-1-full-hosted-security-hardening.md](./story-1-full-hosted-security-hardening.md)
**Worktree:** `.claude/worktrees/hosted-security-hardening/` · **Branch:** `feature/hosted-security-hardening`
**Base:** `9a90d54` (tip of `feature/windows-desktop-app`)

## Part status

| Part | Name | Plan part | Status |
|------|------|-----------|--------|
| A | Identity | Part 1 | **implemented** (A.1–A.4 landed; eye pass owed) |
| B | Transit | Part 2 | **implemented** (steps 1–11; two walks owed, both named below) |
| C | Custody | Part 3 | **implemented** (C.1–C.5; the host-level items are owed, all named below) |
| D | Evidence & surface | Part 0 + Part 4 | **implemented** (D.0–D.4; the eye pass is owed and named below) |

### Part A sub-parts

| Sub-part | Covers | Steps | Status |
|----------|--------|-------|--------|
| A.1 | The capability and the served password floor | 1–7 | **implemented** · committed `3c8d2fe` |
| A.2 | The factor itself, and the login screen that enrols it | 8–19 | **implemented** · `07d40d8` + `1aef203` |
| A.3 | « Sécurité », step-up, and the three ways back | 20–26 | **implemented** · `3b7b6c8` |
| A.4 | Session replay, cookie hardening, the guards | 27–32 | **implemented** · `03d0ea5` |

## Part A gate — final run

| Gate | Result |
|------|--------|
| Backend suite (Release, `BaseOutputPath` outside the repo) | **2825 passed, 0 failed** (baseline 2800 + 25 new) |
| `web/` `check:responsive` · `tsc --noEmit` · `build` | **15/15**, clean, compiled |
| `console/` `check:responsive` · `typecheck` · `build` | **14/14**, clean, compiled |
| `verify-schema` before → after the migration | **263 → 269 ok, 0 drift, exit 0** — the diff is exactly the 4 new indexes + 2 FKs |
| `verify-schema` with A.4's three checks | all three live and green against the running database |
| Backend warnings | no new ones; the pre-existing `CS8618`/`CS8602` baseline is untouched |

**Owed:** the eye pass at 320 / 390 / 820 / 1180 / 1440 + landscape + keyboard. No browser was driven in that
session, so it is recorded as **not done** rather than claimed — the surfaces needing it are `/login`'s four
modes, `/securite`, and the step-up sheet.

**Also owed (verification, not code):** the two flow walks of step 30 could not be executed here (the Google
OAuth round trip needs real credentials). The `SameSite` decision was taken on the defined behaviour of
`Strict` rather than on an observation — see DEV-3.

## Resuming — read this first

**All four parts are landed and the tree is green.** What remains is not implementation: the **eye pass**, the
**flow walks** and the **host-level items** listed under each part below, plus **one PR for the whole story**.
Nothing is half-applied.

⚠️ **Read `context.md` first** (written during Part C): the gate commands that work here, the reference
implementation for each shape, and the ⚠️ Volatile rows to re-check every session. Run its staleness diff rather
than trusting it.

⚠️ **Part C did not mint a fresh key ring** (R-2). `ProtectKeysWithCertificate` protects only what the ring
writes from then on; the existing keys stay as decryptors and the ciphertext is migrated by the new
`reprotect-secrets` verb. Nothing deletes a key file — that is the operator's step, gated on
`verify-schema` reading zero.

⚠️ **A local Docker stack from Part B's verification may still be running** under project name `hshb`
(`docker compose -p hshb -f deploy/docker-compose.hosted.yml down -v`). It holds an empty database and is not the
dev database; the dev one is the main checkout's `clinic-postgres` on 5432.

`exploration.md` § 3.1 records a **contradiction Part C owns**: `deploy/README.md` says to back the
`dataprotection_keys` volume up *alongside* `postgres_data` while the compose file and `.env.hosted.example` say
*separately, never in the same archive*. Part B did not touch it (it is Custody's) but did state the rule the
correct way for its own new `internal_certs` volume, so the README now contains both wordings — fix the older one.

Two items are owed from Part A and are verification, not code — see *Part A gate* above.

---

# Part B — Transit

**Status:** implemented, gate green. **One commit**, check and configuration together (R-6).

## What landed, step by step

| Step | Delivered |
|------|-----------|
| 1 | `deploy/certs/{Dockerfile,issue.sh}` — a one-shot alpine+openssl container minting a **ten-year** internal CA and two SAN leaves (`postgres`, `minio`) into `internal_certs`. Idempotent: a set that still *chains* is reused, and a half-set is re-minted whole |
| 2 | `depends_on: { certs: { condition: service_completed_successfully } }` on `postgres`, `minio`, `api`, `backup`, `pitr` — **restated** in `docker-compose.hosted.yml`, since `extends` drops it |
| 3 | `deploy/postgres/pg_hba.conf` (**`hostssl` only**, no `host` line at all) baked into the image and reached through `-c hba_file=`, plus `ssl=on` + the leaf in the `command:` |
| 4 | `SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt` on both compose files' connection strings · `MinIO__UseSSL: "true"` + `MinIO__RootCertificate` · `Infrastructure/Security/InternalRootTrust` gives the Minio client the internal root |
| 5 | `backup.sh` + `pitr-backup.sh` connect `verify-full` **and ask PostgreSQL whether their own connection is encrypted** before dumping — a run that cannot negotiate fails with exit 1 |
| 6 | `Infrastructure/OriginalPeer` + `API/Middleware/OriginalPeerCapture`, registered **first** in the pipeline; `LocalRequest.IsLoopback` reads the capture. `ClientIp` still reads the substituted address — the two answer differently on purpose |
| 7 | `UseForwardedHeaders` bounded by `Security:TrustedProxies`' **own parsed set** (`TrustedProxies.Networks`, one parser), `!SelfHostsFrontDoor` only; empty/unparseable ⇒ **not registered at all** + a warning naming the key |
| 8 | `API/Startup/TransportAssurance` — refuses startup on either hosted kind, reports **every** problem, names the setting *and* the file, before Hangfire is handed the connection string |
| 9 | `verify-schema`'s **`internal-certificate-days-remaining`**, fed by a third "side" on `SchemaFacts` |
| 10 | `UnitTests/Deploy/TransportConfigurationTests` — the compose files' own values through the **real** check |
| 11 | FR-2.7 confirmed (below) · `deploy/README.md` § *Transit inside the perimeter* · `.env.hosted.example` |

## Part B gate

| Gate | Result |
|------|--------|
| Backend suite (Release, `BaseOutputPath` outside the repo) | **2893 passed, 0 failed** (baseline 2825 + 68 new) |
| Backend build, `--no-incremental` | 0 errors · 55 warnings, **none in a file this part added or changed** (the standing `CS8618`/`CS8602`/`CS8981` baseline; `Program.cs`'s `CS0618` is the pre-existing Hangfire call, moved by 13 lines) |
| `web/` `check:responsive` · `tsc --noEmit` · `build` | **15/15**, clean, compiled — Part B changes **no** frontend file (`git diff -- web/ console/` is empty), so this confirms the tree rather than the part |
| `console/` `check:responsive` · `typecheck` · `build` | **14/14**, clean, compiled |
| `verify-schema` | **exit 0, 0 drift**, twice — with no internal CA (« not applicable ») and with the real one (« 3649 day(s) remaining »). Captures in `../verification/verify-schema-partB-*.txt` |
| Migration | **none** — Part B adds no schema change, and `git status api/**/Migrations/` is empty |
| Compose files parse | both, through PyYAML **and** `docker compose config` with a filled `.env` |

## Executed verification — what was actually run

Docker was available in this session, so most of Part B's verification list was **executed** rather than owed.
A scratch stack under project `hshb` brought `certs` + `postgres` + `minio` up from cold.

| Item | Result |
|------|--------|
| Every hop negotiates TLS | `\conninfo` from a second container: **`SSL connection (protocol: TLSv1.3, cipher: TLS_AES_256_GCM_SHA384)`**, with `sslmode=verify-full` against the internal CA. MinIO's own log: `API: https://…:9000`. `openssl s_client -CAfile ca.crt` against `minio:9000` → **`Verification: OK`**, `subject=CN=minio` |
| **A cleartext connection is refused by the server** | `psql "host=postgres sslmode=disable"` → **`FATAL: no pg_hba.conf entry for host "172.20.0.4", … no encryption`**. `show ssl` → `on` |
| The sidecar fails the run when it cannot negotiate | The real `backup.sh` in its real image: correct config → dump written; **wrong root** → `SSL error: certificate verify failed`, exit 1, nothing dumped; `sslmode=disable` → the server's own refusal, exit 1; a **typo'd** `verify-fully` → libpq refuses the value, exit 1 |
| A deliberately-wrong transit configuration refuses to start, naming the setting | **The real API process**, `Deployment__Profile=HostedMultiTenant`, cleartext connection string → **exit 1** with all four French problems on console *and* Serilog |
| A correct configuration is **not** refused | Same process with `SSL Mode=VerifyFull` + the real `ca.crt` → passed the check and ran on to the migration step (then failed on the *host's* non-TLS dev database, with Npgsql's own `SSL connection requested. No SSL enabled connection from this host is configured` — which independently confirms the connection-string form is honoured, not merely parsed) |
| `Security:TrustedProxies` empty ⇒ headers ignored, stated in the log | Same run: **`Forwarded headers are IGNORED because Security:TrustedProxies is not set. …`** |
| Absent / unreadable / not-yet-valid certificates each refuse and say which | `InternalCertificateTests` + `TransportAssuranceTests` over generated certificates, five verdicts distinguished |
| Exactly one HSTS header | **See the finding below — the answer was two, and it is fixed** |
| `verify-schema` clean, certificate days reported | exit 0 both ways, captures committed |
| `TransportConfigurationTests` proven red | Removing verified TLS from the hosted compose file → **3 red**, the message quoting the operator-facing refusal |
| `SelfHostedLan` unchanged | **Zero deleted lines in `Program.cs`** (purely additive), 5 `ConfigureKestrel` mentions unchanged, the hosted `else` branch still binds both ports in **one** call, `ConsolePortGate` intact, `docker-compose.selfhosted-lan.yml` untouched |

### Still owed (verification, not code)

- **The full cold start of both compose files** (`up -d --build` of every service, including `api`, `web`,
  `console`, `caddy`). This session brought up `certs` + `postgres` + `minio` and ran the API as a host process
  against them; it did not build the API image or obtain a Let's Encrypt certificate, which needs a real domain.
- **The forged-header walk through Caddy** — `curl -H 'X-Forwarded-For: 127.0.0.1' https://<domain>/hangfire`.
  The logic and the ordering it depends on are held by `OriginalPeerTests` (including the substituted-loopback
  case and the source-level ordering guard, both proven red), but the end-to-end hop was not walked.
- **The PITR sidecar's own pre-flight** was not run against a live WAL-G target; its check is byte-identical to
  the logical sidecar's, which *was* run in all four states.

## Findings — things that were wrong before this part, or wrong in it

### F-1: `minio/minio` ships no `wget`, so its healthcheck has never run

The healthcheck in `docker-compose.prod.yml` was `wget --spider http://localhost:9000/minio/health/live`. The
image has **no `wget`** (it has `curl` and `mc`), so the check has exited 1 for the life of both hosted
profiles: `/bin/sh: wget: command not found`. It stayed invisible because nothing waits on
`minio: service_healthy` — the API deliberately uses `service_started` — so the container simply sat at
`health: starting` and no operator had a reason to look.

Found while moving that line to HTTPS, and **fixed** rather than left: `curl -fsS --cacert /certs/ca.crt`, which
now goes `healthy` (verified: exit 0). `--cacert` rather than `--insecure` on purpose — a probe that skips
verification proves the port answers and nothing about the chain, so a leaf that had stopped matching would read
as healthy right up to the moment the API refused to talk to it.

### F-2: Caddy does **not** replace an upstream's header, so Part B nearly shipped two HSTS headers

`deploy/Caddyfile` sets HSTS at the site level and its comment said the API never emits one — true only because
`Request.IsHttps` was false for every proxied request and nothing consumed `X-Forwarded-Proto`. Step 7 changes
exactly that, so `SecurityHeadersMiddleware` would have started emitting its own on `/api/*`.

The Caddyfile's own comment (and mine, briefly) asserted Caddy would replace it. **It does not.** Reproducing
the shipped directive over an upstream that sets its own gave the client **both**:

```
Strict-Transport-Security: max-age=31536000; includeSubDomains   ← Caddy
Strict-Transport-Security: max-age=31536000                      ← the upstream
```

RFC 6797 § 8.1 has the browser honour only the **first**, so it was not a downgrade — it was a malformed
response carrying a value nothing in the deployment could predict. Two fixes were available: strip the
duplicate at each proxied block (`header_down -Strict-Transport-Security`, also verified working), or make the
API emit HSTS only where it is itself the edge. **The second was chosen**, because the Caddyfile already states
the rule — « HSTS belongs HERE, not in the API » — and one condition keeps that true where three `header_down`
lines would have to be remembered on every route added later.

`SecurityHeadersMiddleware._hstsEnabled` is now `SelfHostsFrontDoor && Security:EnableHsts`. ⚠️ **The
observable behaviour is unchanged in every profile**: hosted deployments never emitted it (IsHttps was false)
and still do not; `SelfHostedLan` keeps its opt-in. What changed is that the reason is now stated instead of
incidental. Two new cases hold it, proven red against the old expression — and only the two hosted rows went
red, so the change did not widen into « never emit HSTS ».

### F-3: `exploration.md` § 2.3 / plan R-5 describe a defect that had already been fixed

Both say `LocalRequest.IsLoopback` « already returns `true` on a **null** address — a gate that defaults to
allow », citing `LEARNINGS:97`. It does not: it returns `false`, and its own comment says
*(Previously returned true.)* `LEARNINGS` records the historical defect; the risk register copied it as present
tense. R-5's substantive half is unaffected — `UseForwardedHeaders` *would* have made the gate forgeable, which
is why `OriginalPeer` exists — and the fail-closed null case is now pinned by a test of its own.

### F-4: every `deploy/*.sh` is CRLF in a Windows working tree, so the images cannot be built there

`core.autocrlf=true` with no `.gitattributes` anywhere: git stores LF and a Linux checkout is fine, so an
operator deploying from a clone is unaffected — but building these images **on Windows** yields
`exec /usr/local/bin/backup.sh: no such file or directory`, because the shebang ends `#!/bin/sh\r`. The message
names a file that plainly exists, which is why it costs a cycle every time. `pg_hba.conf` is the sharper case:
PostgreSQL parses it token by token, so a trailing `\r` corrupts the authentication **method** on every line —
and that file *is* FR-2.3.

Closed with **`deploy/.gitattributes`** (`*.sh`, `*.conf`, `Dockerfile`, `Caddyfile` → `eol=lf`), scoped to
`deploy/` because `packaging/` ships PowerShell and an Inno Setup file that needs a BOM. The working-tree copies
were normalised; `git diff --numstat` confirmed **zero** content change, i.e. the index already held LF.

### F-5: two of my own comments were wrong, and the probes caught them

- The sidecars' comment said libpq *silently ignores* an unrecognised `sslmode`. It does not — it refuses the
  value outright (`invalid sslmode value: "verify-fully"`). The real reason to ask `pg_stat_ssl` is that
  `require` and libpq's default `prefer` **encrypt while verifying nothing**, so an env-var check passes on
  exactly the configuration FR-2.1 rules out. Corrected in both scripts.
- `TransportConfigurationTests`' first reader swallowed a trailing YAML comment into the value, which surfaced as
  `Every_Consumer_Mounts_The_Certificates_Read_Only` failing on a mount that was perfectly correct. A reader that
  quietly returns the wrong string makes every assertion above it meaningless *in the direction that passes*.

## Red proofs executed

| Guard | Proof |
|-------|-------|
| `OriginalPeerTests.A_Substituted_Loopback_Address_…` | `LocalRequest` reverted to `connection.RemoteIpAddress` → **exactly that test red**; restored |
| `OriginalPeerTests.The_Peer_Is_Captured_Before_Forwarded_Headers_…` | The capture moved *after* `UseForwardedHeaders` in `Program.cs` → **that test red**, the other seven green; restored |
| `InternalRootTrustTests` | `IsTrusted` replaced with `=> true` → **the three refusal cases red**, the two acceptance cases green (so the check is not merely inverted); restored |
| `TransportConfigurationTests` | Verified TLS removed from `docker-compose.hosted.yml` → **3 red**, the failure quoting the operator-facing French refusal; restored |
| `SecurityHeadersMiddlewareTests.Hsts_Is_Left_To_The_Reverse_Proxy_…` | The pre-Part-B `_hstsEnabled` expression restored → **both hosted rows red**, `SelfHostedLan` green; restored |
| The sidecar pre-flight | Not a unit test — the real script in its real image, in four states (see the table above) |
| `certs/issue.sh` idempotency | Run twice: « existing internal CA reused — expires Aug 9 2036 ». A third run through `docker compose up -d minio` reused it again |

## Deviations

### DEV-4: the connection string uses `SSL Mode=VerifyFull`, not the plan's literal `sslmode=verify-full`
**Date:** 2026-08-12 · **Story:** Part B, step 4 · **Category:** Technical
**Original plan:** *« connection string gains `sslmode=verify-full` »*.
**Actual implementation:** `SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt`.
**Justification:** `sslmode=verify-full` is **libpq's** spelling and Npgsql **rejects it** — the driver parses
`SslMode` as an enum, so the hyphenated form is not a value it has. The plan's own note says `sslmode` has zero
hits in the repo and *« state the chosen form in `deploy/README.md` »*, i.e. it anticipated that the form had to
be decided; this is that decision. The README now states both forms and which layer uses which — the **sidecars**
are libpq and genuinely do use `verify-full`. `TransportAssurance` parses with `NpgsqlConnectionStringBuilder`
rather than matching text, so the two cannot drift, and
`TransportAssuranceTests.Libpqs_Own_Spelling_Is_Refused_Rather_Than_Silently_Accepted` pins it.
**Impact:** none on behaviour; the plan's literal was unimplementable.
**Approved:** auto (trivial — the named value does not exist in the driver, and the plan asked for the form to be
chosen and documented)

### DEV-5: `OriginalPeer` splits across two files, not the plan's single `API/Middleware/OriginalPeer.cs`
**Date:** 2026-08-12 · **Story:** Part B, step 6 · **Category:** Technical
**Original plan:** create `api/ClinicManagement.API/Middleware/OriginalPeer.cs`.
**Actual implementation:** `Infrastructure/OriginalPeer.cs` (the key, the capture, the read) +
`API/Middleware/OriginalPeerCapture.cs` (the middleware and its `UseOriginalPeerCapture()` extension).
**Justification:** **forced by the dependency direction.** `LocalRequest` — the consumer — lives in
Infrastructure, which cannot reference the API project, so the item key and the reader must be there. It is the
same reason `LocalRequest` itself was extracted to Infrastructure (its docstring: *« so it can be exercised by
`ClinicManagement.UnitTests`, which references Infrastructure but not the API »*). The middleware stays in
`API/Middleware/` per convention.
**Impact:** one extra file. The ordering obligation the plan cared about is asserted against `Program.cs`'s own
source and proven red.
**Approved:** auto (the plan's single-file layout is structurally impossible)

### DEV-6: `internal-certificate-days-remaining` is Info when usable and **Drift** when unusable
**Date:** 2026-08-12 · **Story:** Part B, step 9 · **Category:** Technical
**Original plan:** *« `verify-schema` gains `internal-certificate-days-remaining` (**Info**, with the count) »*.
**Actual implementation:** Info with the count while the certificate is usable; **Drift** when it is configured
and expired, unreadable or not yet valid.
**Justification:** the Info half is kept for the case the story is about — FR-2.6 wants a ten-year expiry visible
years ahead, and a certificate with 3 649 days left must not turn the verb to exit 2, since *« an alarm that is
always on is one nobody reads »*. But the same line rendering an **expired** root as `[  ok ]` is the exact
failure shape this report exists to prevent. Absent stays « not applicable » — `SelfHostedLan` and a developer
machine correctly have no internal CA — and where there *should* be one the API refuses to start without it, so
a deployment that can run this verb has already passed that gate.
**Impact:** a deployment whose internal CA has expired now exits 2 instead of 0. Three test cases cover the
split.
**Approved:** auto (the story's letter is preserved for the case it names; the reversal applies only to a state
the story does not discuss and that the repo's own conventions say must not read as ok)

### DEV-7: `SecurityHeadersMiddleware`'s HSTS condition changed — see finding F-2
**Date:** 2026-08-12 · **Story:** Part B, step 7 · **Category:** Technical
**Original plan:** *« Confirm `SecurityHeadersMiddleware` does not now emit a second HSTS header alongside
Caddy's. »*
**Actual implementation:** it **would** have, so the condition became
`SelfHostsFrontDoor && Security:EnableHsts` (was `!SelfSignsCertificate || Security:EnableHsts`).
**Justification:** the step asked for a confirmation; the confirmation came back negative, verified over the wire
(two headers). Fixed at the API rather than with three `header_down` lines because `deploy/Caddyfile` already
states « HSTS belongs HERE, not in the API » and one condition keeps that true for every route added later.
**Impact:** **observably none** — hosted kinds never emitted HSTS (`IsHttps` was false) and still do not;
`SelfHostedLan` keeps its opt-in. It is a global file, so it is worth knowing that the *reason* changed even
though the behaviour did not.
**Approved:** auto (step 7 asked the question and this is the answer; the alternative was leaving a malformed
response the story's own verification list forbids)

### DEV-8: two adjacent pre-existing defects fixed in this part — findings F-1 and F-4
**Date:** 2026-08-12 · **Story:** Part B · **Category:** Scope
**Original plan:** neither is in it.
**Actual implementation:** MinIO's healthcheck now uses `curl` (it has never worked — the image has no `wget`),
and `deploy/.gitattributes` pins LF on the files Linux containers read.
**Justification:** both were found *because* Part B touches those exact lines, both are one-line fixes, and both
mask Part B's own work — a healthcheck that cannot run cannot report the TLS the part just added, and a CRLF
`pg_hba.conf` silently corrupts the cleartext refusal that *is* FR-2.3. Leaving them would have meant shipping
transit whose two most load-bearing files cannot be verified on the machine this work happens on.
**Impact:** `deploy/` only. The healthcheck now genuinely gates, which nothing depends on today
(`service_started` is unchanged, deliberately: MinIO down is `Degraded`, not a reason to refuse to start).
**Approved:** auto (adjacent, one line each, and each one masks a Part B guarantee)

---

# Part C — Custody

**Status:** implemented, gate green. **One commit.** Everything reachable from a development machine was
executed; every host-level item is owed and named below rather than claimed.

## What landed, step by step

| Step | Delivered |
|------|-----------|
| 1–2 | `Infrastructure/Security/KeyRingProtectionCertificates` + `LocalDataProtection.ApplyCertificateProtection` — `ProtectKeysWithCertificate` **and** `UnprotectKeysWithAnyCertificate`, the Windows DPAPI branch untouched. Required in `HostedMultiTenant`, **Development exempt** (DEV-9). Retained generations = **2**, stated in `KEY-CUSTODY.md` (FR-3.2) |
| 3–4 | **`reprotect-secrets [--rotate]`** (`API/Maintenance/`) over all **six** families, idempotent, per-family counts, **naming** any row it cannot decrypt and exiting **2**. `--rotate` mints the new active key (DEV-10) |
| 5 | Deleting the superseded key files is the **operator's** step, gated on the check below — `KEY-CUSTODY.md` § 1 carries the four-command order |
| 6 | `verify-schema` **`secrets-protected-under-current-ring`** + **`key-ring-protection`**, fed by a fourth "side" on `SchemaFacts`. Absent ⇒ « not applicable », **never zero** |
| 7 | Every `TryUnprotect` caller audited; `GoogleCalendarSyncService.ResolveConnectionAsync` **throws** rather than returning null (the four TOTP callers already matched `PlatformLoginCommand`'s model) |
| 8 | `Clinic.GoogleRefreshTokenProtected` + `IGoogleTokenProtector`/`GoogleTokenProtector`, migration **`AddProtectedGoogleToken`**, the **startup backfill** (`Startup/GoogleTokenProtectionBackfill`), and `verify-schema`'s **`google-token-protected`** |
| 9 | LUKS — **documented**, not applied: `KEY-CUSTODY.md` § 4, with the « stolen, snapshotted or decommissioned disk » wording verbatim and the unattended-reboot `crypttab`/`fstab` lines |
| 10 | `deploy/backup/Dockerfile` adds **`age`**; `backup.sh` **refuses without a recipient**, encrypts before rclone, and — where an identity is mounted — **decrypts what it just wrote and runs `pg_restore --list`** |
| 11 | `WALG_LIBSODIUM_KEY` on **both** `postgres` (whose `archive_command` pushes the WAL) and `pitr`; `pitr-entrypoint.sh` **refuses to start** without it |
| 12 | `Startup/KeyRingGenerationMarker` + `deploy/backup/check-keyring.sh`. ⚠️ **Staleness rule chosen and stated**: the marker lists **every** generation the ring can read, rewritten at startup and by `--rotate` — see DEV-12 |
| 13 | The **`*_FILE` layer** (`Startup/FileBackedSecrets`) inside `AddInstallLayers`, plus `secrets:` blocks in **both** compose files. Scope decided with the user — see DEV-11 |
| 14 | FR-3.11 resolved in **one voice** across `deploy/README.md`, `.env.hosted.example` and `KEY-CUSTODY.md`: the ring may travel with `postgres_data`; the **certificate** is what travels apart |
| 15 | **`deploy/KEY-CUSTODY.md`** and **`deploy/RESTORE-DRILL.md`** |
| 16 | `UnitTests/Common/SecretProtectionCoverageTests` + the shared `SecretShapedNames`, and `Api/ConsoleVerbDispatchTests` (the verb-reachability guard, widened from the five subscription verbs to **all** of them) |

## Part C gate

| Gate | Result |
|------|--------|
| Backend suite (Release, `BaseOutputPath` outside the repo) | **2943 passed, 0 failed** (baseline 2893 + 50 new) |
| Backend build, `--no-incremental` | 0 errors · the standing warning baseline only. The three warnings in files this part touched were checked line by line and are pre-existing: `Clinic.cs(120)` / `User.cs(70)` are the `private X() { } // For EF Core` ctors (`CS8618`, every entity has one) and `Program.cs(496)` is the Hangfire `UsePostgreSqlStorage` `CS0618` Part B already recorded. **0 warnings in any file this part added** |
| `web/` `check:responsive` · `tsc --noEmit` · `build` | **15/15**, clean, compiled — Part C changes **no** frontend file (`git diff -- web/ console/` is empty), so this confirms the tree rather than the part |
| `console/` `check:responsive` · `tsc --noEmit` · `build` | **14/14**, clean, compiled |
| `verify-schema` before → after the migration | **exit 0 → exit 0, 32 checks, 0 drift both sides**; the diff is **exactly one line** (`google-token-protected`: « not applicable » → « 0 cabinet(s) still hold a Google Agenda token in the clear »). Captures in `../verification/verify-schema-{before,after}-C.txt` |
| Compose files parse | both, through PyYAML **and** `docker compose config` with a filled `.env` and real secret files |
| Committed scripts parse on their runtime | `sh -n` clean on `backup.sh`, `check-keyring.sh`, `pitr-entrypoint.sh`; all four container-read files confirmed **LF** (Part B's `.gitattributes` covers the new one) |

## Executed verification — what was actually run

A throwaway PostgreSQL 16 container plus a real self-signed PKCS#12 made most of C.1/C.2 executable end to end.

| Item | Result |
|------|--------|
| The ring is **encrypted** and the check says so | `key-ring-protection: the key ring is encrypted by the deployment's certificate (3649 day(s) remaining on it)` |
| `verify-schema` before/after the migration, **diffed** | one line, exactly the column the migration adds (above) |
| **`reprotect-secrets` runs** and reports every family | all six listed, exit **0** on a clean database, marker path honoured |
| **A row it cannot decrypt is NAMED and left alone** | seeded `Clinics.GoogleRefreshTokenProtected = 'not-real-ciphertext'` → « 1 illisible(s) », the cabinet named, **exit 2**, and `SELECT` afterwards shows the value **unchanged** |
| The coverage figure then reports it | `[DRIFT] secrets-protected-under-current-ring: 1 stored secret(s) are still under a superseded generation — Clinics.GoogleRefreshTokenProtected 0/1 — run « reprotect-secrets » and do NOT delete any key file until this reads zero`, exit 2 |
| A configured-but-broken certificate refuses | `KeyRingProtectionTests` over generated certificates — missing, unreadable, no private key, retained generation with no path |
| An expiring certificate **warns** rather than refusing | same class; and more than two retained generations warns |
| Both compose files resolve with the new `secrets:` blocks | `docker compose config` exit 0 for each, with `WALG_LIBSODIUM_KEY` present on **both** `postgres` and `pitr` |

### Still owed (verification and host changes, not code)

Everything here needs a real hosted deployment. None of it is half-applied; each is a step an operator runs.

- **The LUKS change itself, and the cold unattended reboot.** Documented in `KEY-CUSTODY.md` § 4; not applied.
- **A real backup run**: that the archive decrypts, that `pg_restore --list` is non-empty, and that a
  **deliberately-corrupted** upload fails the run. The script is committed and parses; it has not been executed
  in its image (no `age` binary and no MinIO volume here).
- **The PITR sidecar's refusal** was not run against a live WAL-G target; the check is a three-line guard at the
  top of an entrypoint that `sh -n` parses.
- **One manual restore drill**, end to end. ⚠️ `RESTORE-DRILL.md` says out loud that **no drill has been
  performed** and that the restore path is unproven until its log has a first row — deliberately, rather than
  shipping an empty table that reads as « nothing to report ».
- **A mismatched key-ring generation refused end to end.** `check-keyring.sh` is written and parses; the pair of
  real files (a stamp from a backup, a marker from a live API) needs the stack up.
- **`docker exec … env | grep -Ei 'password|apikey|token|secret'` returning nothing** — true for the six secrets
  moved to files, and **not yet true overall**; see DEV-11 and `follow-up/hosted-secrets-to-files.md`.

## Findings — things that were wrong before this part, or wrong in it

### F-6: the FR-3.11 contradiction was **three** documents, and the correct answer is the opposite of both sides

`exploration.md` § 3.1 records `deploy/README.md` (« alongside ») against the compose file and
`.env.hosted.example` (« separately, never in the same archive »). Both were written when the ring was
**cleartext**, which is what made « separately » right. FR-3.1 changes the fact underneath: the ring is now
encrypted, so it may travel with `postgres_data`, and what must travel apart is the **certificate**. Resolving
it as a wording fix — picking one of the two existing sentences — would have shipped a rule that was correct
yesterday and wrong today. All three now say the same thing and point at `KEY-CUSTODY.md`.

### F-7: my own `verify-schema` addition broke the « before » half of the before/after run

The first version of `ReadSecretProtectionAsync` read the six families through EF unconditionally, so against
any database predating the migration the **entire verb** died with
`42703: column c.GoogleRefreshTokenProtected does not exist` — i.e. the run that establishes the baseline, which
is the whole point of the prescribed workflow. Every existing count in that reader is guarded with
`requiredTable`/`requiredColumn` and mine was not. Found by **running the gate**, not by the suite: nothing in
`UnitTests` touches a database, so this is exactly the class of defect `verify-schema` exists for — and it
surfaced the first time the prescribed order was followed. Each family is now guarded on its own column, and an
absent one is **omitted** rather than reported as zero outstanding.

### F-8: two ephemeral Data Protection providers share a key id, so one new test asserted something false

`Ciphertext_From_Another_Ring_Is_Not_Covered` failed against perfectly correct code:
`UseEphemeralDataProtectionProvider` leaves the ring's default key id at `Guid.Empty`, so two ephemeral
providers write byte-identical payload headers. The **test setup** was wrong, not `Covers`. It uses two
persisted rings now — which is also the arrangement every deployment running this check actually has. Worth
knowing before writing another test over the payload format.

### F-9: the verb-dispatch guard's first form reported three correctly-dispatched verbs as missing

Written as « `Program.cs` contains `{Verb}.CommandName` **and** `{Verb}.RunAsync(args)` », it went red on
`CountActivityCommand` (`RunAsync()`), `ProvisionCertCommand` and `HardenPermissionsCommand` (`Run(args)`) —
the entry points legitimately differ. Loosened to `.Run`, which is the trap in the *other* direction (a
loosened check can silently stop matching anything), so the class carries an **executed red proof** running the
real check against a `Program.cs` with `reprotect-secrets`' branch renamed away.

### F-10: `SecretProtectionCoverageTests` found seven columns nobody had ruled on — and one mistake of my own

It went red on first run with `ClinicSignup.PasswordHash`, `ClinicSignup.TokenHash`, `DeviceRegistration.Token`,
`PlatformAccount.MustChangePassword`, `User.MustChangePassword`, both `SessionFamily.*CredentialHash` and both
`TokenVersion` counters. Every one is legitimately plaintext and now says **why** — which is the guard's whole
purpose: none of them had ever been argued about anywhere. Its **both-directions** half then caught a
speculative entry of mine for `Appointment.GoogleCalendarEventId`, which does not match the heuristic at all —
a pre-approved hole, removed. And `Every_Decision_Gives_A_Reason` went red on two entries I had written as
« The clinic twin of the flag above. »

## Red proofs executed

| Guard | Proof |
|-------|-------|
| `SecretProtectionCoverageTests` | `The_Guard_Rejects_A_Credential_Column_Whose_Decision_Is_Removed` runs the **real** classifier over the **real** model with the Google-token decision removed. Plus the seven genuine findings above, each a red run |
| `ConsoleVerbDispatchTests` | `The_Guard_Rejects_A_Verb_Whose_Dispatch_Branch_Is_Removed`, over a `Program.cs` with `ReprotectSecretsCommand.` renamed away; plus F-9's three real red rows |
| `reprotect-secrets`' refusal | **The real verb against a real database**: an undecryptable row → named, untouched, exit 2 |
| `secrets-protected-under-current-ring` | **The real verb**: DRIFT + exit 2 with that row present; `ok` + exit 0 without it |
| `google-token-protected` | The before/after diff — « not applicable » → « 0 cabinet(s) » across the migration |
| `KeyRingProtectionCertificates` refusals | Four cases over generated certificates (missing · unreadable · no private key · retained generation with no path) |
| `FileBackedSecrets` refusals | Missing file · empty file · `*_FILE` with no path — each refusing by name and by path |
| Non-vacuity | Both new derived guards assert a **non-zero candidate count** first, `SystemWideCallerCoverageTests`' lesson |

## Deviations

### DEV-9: the protecting certificate is required in `HostedMultiTenant` **except in Development**
**Date:** 2026-08-12 · **Story:** Part C, step 1 · **Category:** Technical
**Original plan / spec:** Part 3's edge-case table — « The protecting certificate is missing at startup → Refuse
to start. »
**Actual implementation:** refuses in `HostedMultiTenant` **unless `ASPNETCORE_ENVIRONMENT=Development`**, where
it warns and continues with an unencrypted ring.
**Justification:** discovered by running `dotnet ef` — `appsettings.Development.json` selects
`HostedMultiTenant` **deliberately** (it is the only profile where the public signup door is open), and no
developer has a PKCS#12, so the literal rule broke `dotnet run` and `dotnet ef migrations add` on a fresh clone
for everyone. The exemption is not invented: `MinioCredentials.TolerateUnconfigured` decides the identical
question for object-store credentials, one file away, in the same startup path — « Acceptable in Development
only — a non-Development environment will refuse to start ». The refusal is unchanged wherever a real
deployment runs.
**Impact:** none on any deployment. Pinned by `An_Unprotected_Ring_Is_Tolerated_In_Development_Only`.
**Approved:** auto (the repo's own precedent for this exact question, and the literal rule is unshippable)

### DEV-10: forcing a new active key is `reprotect-secrets --rotate`, not an unconditional startup action
**Date:** 2026-08-12 · **Story:** Part C, step 3 · **Category:** Technical
**Original plan:** step 3 « **Force a new active key**, so every subsequent write is protected », standing alone
between « configure certificate protection » and « add the verb ».
**Actual implementation:** an explicit `--rotate` flag on the verb, which mints the key through
`IKeyManager.CreateNewKey` and then re-protects.
**Justification:** minting a key is the one **non-idempotent** thing in this part, and step 4 requires the verb
to be idempotent. Doing it at startup mints a key on every container restart — an unbounded ring, and a
generation the FR-3.9 marker names differently after every deploy. Behind a flag, the ordinary run is idempotent
(a second run reports « 0 rechiffrée, N déjà à jour ») and `KEY-CUSTODY.md` § 1's four-command sequence makes
the rotation an explicit, once-per-migration act.
**Impact:** the operator types `--rotate` once. Nothing else changes.
**Approved:** auto (the plan's own step 4 requires idempotence, which an unconditional rotate contradicts)

### DEV-11: FR-3.10 covers the application's own secrets; the sidecars' are a follow-up
**Date:** 2026-08-12 · **Story:** Part C, step 13 · **Category:** Scope
**Original plan:** « a `secrets:` block in **both** compose files and `*_FILE` indirection for **every** `${VAR}` ».
**Actual implementation:** the `*_FILE` **layer** is built in full and applied by the host and every console
verb; six secrets are moved. Two classes are left and named in place: those **shared** with a non-.NET container
(the DB password inside the connection string, the MinIO key) and the sidecars' own
(`POSTGRES_PASSWORD_FILE`, `MINIO_ROOT_PASSWORD_FILE`, `PGPASSFILE`, and wal-g, which has **no** file
convention at all).
**Justification:** **asked and decided with the user**, against the spec's own stated contingency (« may be
dropped if the part is running long — say so rather than half-doing it »). Two reasons. Moving only the API's
copy of a **shared** secret leaves it in three other containers' environments while the compose file implies it
has left — a visible gap converted into an invisible one. And each sidecar's mechanism changes how a container
authenticates **at boot**, unverifiable here (the hosted stack needs a real domain), whose failure is the
nightly dump stopping at 02:00 — Part B found two defects of exactly that shape.
**Impact:** `deploy/README.md`'s « no secret remains in `environment:` » check is not yet clean, and says so.
Remedy chosen and written up: `follow-up/hosted-secrets-to-files.md`.
**Approved:** **yes — asked explicitly**

### DEV-12: the FR-3.9 marker lists **every** readable generation, not only the active one
**Date:** 2026-08-12 · **Story:** Part C, step 12 · **Category:** Technical
**Original plan:** « writes the ring's active key id … the restore procedure compares and **refuses a
mismatch** », with the note « **Known staleness:** … State the refresh rule chosen (re-write on rotation, or
re-read before each stamp) rather than leaving it implicit. »
**Actual implementation:** the marker carries `active=` plus one `readable=` line per key the ring holds; the
restore check asks whether the backup's generation is **among** the readable ones. The refresh rule is
**rewritten at every startup and by `reprotect-secrets --rotate`**.
**Justification:** « re-read before each stamp » is unavailable — the sidecar has no key ring by design, which
is § 3.1's whole point — so the staleness had to be absorbed rather than eliminated. Equality on the *active*
key is the wrong question anyway: the framework rolls keys on its own, so a ring that has rolled since the dump
was taken can still read it perfectly, and equality would refuse a restore that was never in danger. Refusing
correct restores is how a safety check gets switched off. A list makes the residual staleness **narrow the
readable set**, so the check errs toward refusing — the safe direction — and says so.
⚠️ It forced a second decision: `IKey.KeyId` and the id inside a payload must render to the **same text**
(`DataProtectionKeyGeneration.IdOf`, `Guid.ToByteArray()` order). Rendering one as a canonical GUID byte-swaps
three fields, so every restore would be refused as a mismatch that is not real — pinned by
`A_Key_Id_Renders_Identically_However_It_Was_Obtained`.
**Impact:** a marker of a few short lines instead of one. Nothing else changes.
**Approved:** auto (the plan asked for the rule to be chosen and stated; this is that choice, with its reason)

### DEV-13: a fourth secret protector rather than collapsing the three that exist
**Date:** 2026-08-12 · **Story:** Part C, step 8 · **Category:** Technical
**Original plan:** silent on how the Google token is encrypted.
**Actual implementation:** `IGoogleTokenProtector`/`GoogleTokenProtector`, a fourth type on
`UserSecretProtector`'s pattern with its own purpose string.
**Justification:** each family **must** have its own purpose or the ciphertexts become interchangeable, and the
purpose is what the framework derives the key from. Collapsing the four into one seam taking a purpose parameter
is a refactor of Part A's work in the middle of Part C, on the code path that decides whether anybody can sign
in. Following the established pattern is the smaller risk.
**Impact:** one more small class. Noted as worth collapsing later, deliberately not here.
**Approved:** auto (matches the three siblings exactly; the alternative touches Part A's sign-in path)

---

# Part D — Evidence & surface

**Status:** implemented, gate green. **One commit**, D.0 through D.4 together — the log scrub and the durable-log
volume cannot be split (FR-4.4 says so in as many words), and the enforcing policy cannot land without the
analytics removal that unblocks it.

## What landed, step by step

| Step | Delivered |
|------|-----------|
| D.0 | **Verification, not a fix.** `ClinicArchiveRestorer` holds exactly one `ForgetRestoredRows()`, **after** the save; `git diff HEAD` on that file was empty. `UnitTests/Features/Backup/ClinicArchiveRestorerTests` now holds the property — asserting **what reached the store**, never `outcome.Restored` — and `exploration.md` § 4.2's « LIVE DEFECT » block is rewritten to say the surviving call *is* the guard |
| 1 | `Domain/Services/AuditChain.cs` — `Hash(previousHash, entry, key)` (HMAC-SHA256, length-prefixed fields, **microsecond-canonical** timestamp) + `Walk`. One arithmetic, called by the appender and by `verify-schema`, never re-expressed in SQL |
| 2 | `AuditEntry` gains `ChainKey` · `Sequence` · `PreviousHash` · `EntryHash` · `IsDeclaredGap`, two declaration factories and `ToChainEntry()`; `AuditEntryConfiguration` adds a **partial-unique** `(ChainKey, Sequence)` index filtered on `Sequence > 0`. Migration **`AddAuditChain`**: all DDL first, three backfill statements last |
| 3 | `Infrastructure/Security/AuditChainKey.cs` — **throws** where `!SelfHostsFrontDoor`, self-generates and persists on `SelfHostedLan` and in Development (DEV-9's precedent). Resolved once in `Program.cs`, so a missing key is a **startup** refusal naming the setting |
| 4 | `Infrastructure/Persistence/AuditChainAppender.cs` — per-chain `pg_advisory_xact_lock(5314, hash(ChainKey))`, keys locked in **ascending order**, tip read, sequences and hashes assigned. Wired into `ApplicationDbContext.SaveChangesAsync`, which opens a transaction when the caller has none — see **DEV-14** |
| 4b | `AuditSaveChangesInterceptor` records a **declared gap** when its write fails, on a fresh scope; if that fails too the chain is genuinely broken, which is the honest outcome |
| 5 | `ClinicArchiveRestorer` stages a **declared boundary** before anything else, so a restore's discontinuity does not read as tampering |
| 6 | `verify-schema` gains **`audit-chain-intact`** (drift) and **`audit-declared-gaps`** (Info, reported apart) over a fifth "side" on `SchemaFacts`. ⚠️ The walk happens in the **reader**, streaming per chain — see DEV-15 |
| 7 | `Application/Features/Backup/ArchiveAccessLedger.cs` — the request row is written **before** the archive is built and is **not** best-effort (refusal carries `archive_not_recorded`); a second row records **delivered vs interrupted** from `Response.OnCompleted` + `RequestAborted`, in its own scope, declaring `UseClinic`. Plus `NotificationCategory.ClinicArchiveExported` (DEV-16) |
| 8 | `RateLimiting.ArchivePolicy` — 3 per 10 min per user. It fell to the API window: **600 full-practice exports a minute** |
| 9 | Step-up on **both** archive doors, with **different** action names, in an `X-Step-Up-Confirmation` header (never the query string — FR-4.4). `apiHeaders` stays the single writer. The archive card wires `StepUpDialog`, which had **no caller at all** until now, and states the phone limitation in French rather than refusing |
| 10 | The eleven PHI templates scrubbed — three in `PdfGenerationService` (→ the document's own number), eight in `GoogleCalendarSyncService` (→ ids where one is in hand, `LogMask.Name` where none is) — plus `HuggingFaceAIService`'s raw payload (→ property names) and `SmtpDocumentEmailSender`'s name-composed `{FileName}` (→ `LogMask.FileName`) |
| 11 | `api_logs:/app/logs` + `retainedFileCountLimit: 30` on **both** hosted compose files, in the same commit as the scrub |
| 12 | `UnitTests/Common/LogTemplateCoverageTests` — derived source scan, both a red proof and a masked-value proof, and **an empty exemption map** |
| 13 | `Security__EnforceCsp: "true"` on both hosted files; **`'unsafe-eval'` dropped**; **`@vercel/analytics` removed** from `web/app/layout.tsx`, `package.json` and the lockfile |
| 14 | `API/Controllers/CspReportController.cs` — anonymous, its own rate-limit policy, body capped **before** it is read, both report shapes parsed, and the `document-uri` **stripped to its route pattern** |
| 15 | `Permissions-Policy` (empty allow-list), `Reporting-Endpoints`, COOP/CORP; `deploy/Caddyfile`'s page block updated and **the console site given its first policy at all**; `console/next.config.ts` gains `headers()` |
| 16 | `UnitTests/Common/ContentSecurityPolicyAgreementTests` — parses the real `Caddyfile` (both sites) and the real console config and asserts byte-identity with the middleware's constant, with two red proofs |
| 17 | **FR-4.6 — `UseHttpsRedirection` removed**, with the reasoning left in its place. It had no port configured on either hosted kind and silently did nothing; Caddy already redirects at the edge (confirmed by `caddy validate`'s own « enabling automatic HTTP->HTTPS redirects ») |

## Part D gate

| Gate | Result |
|------|--------|
| Backend suite (Release, `BaseOutputPath` outside the repo) | **2974 passed, 0 failed** (baseline 2943 + 31 new) |
| Backend build, `--no-incremental` | 0 errors · **110 warnings, none in a file this part added or changed**. One genuinely new `CS8604` in `BuildClinicArchiveQuery` was found by this census and fixed; the only remaining hit on a touched file is the standing Hangfire `CS0618` in `Program.cs`, moved 17 lines by an insertion |
| `web/` `check:responsive` · `tsc --noEmit` · `build` | **15/15**, clean, compiled |
| `console/` `check:responsive` · `tsc --noEmit` · `build` | **14/14**, clean, compiled |
| `verify-schema` before → after the migration | captures in `../verification/verify-schema-{before,after}-D.txt`; the diff is below |
| The chain walk turns **red** on a hand-edited entry | **executed** — see below |
| Compose files parse | all three through PyYAML (9 / 8 / 3 services) |
| `deploy/Caddyfile` | **`caddy validate` → « Valid configuration »** |

## Executed verification — what was actually run

| Item | Result |
|---|---|
| The D.0 test **red with a pre-save `ForgetRestoredRows()`** | **executed**: exactly 2 of its 4 cases went red (`…Persists_Them`, `Each_Table_Is_Persisted_Before…`), the other two stayed green; reverted, all 4 green |
| The migration applies | `AddAuditChain` applied to the dev database. Backfill outcome: **5 chains, 1104 pre-chain entries, 5 declared boundaries** — one per chain, exactly as designed |
| Entries written after the migration are **chained** | `provision-clinic` created a cabinet through the real write path → `audit-chain-intact: 6 chaîne(s) intactes — 179 entrée(s) vérifiées, 1104 antérieure(s) au chaînage` |
| **A hand-edited entry turns the walk red** | `UPDATE "AuditEntries" SET "ChangedFields" = '…' WHERE "Id" = '33ae153b-…'` → **`[DRIFT] audit-chain-intact: 1 chaîne(s) rompue(s). Première rupture : cabinet e1bc853b-… n° 179 (33ae153b-…) — cette entrée a été modifiée après son écriture`**. Value restored → intact again |
| `audit-declared-gaps` is reported **apart from** breaks and is never drift | both runs above: 5 declared gaps, `[ ok ]`, while the break was `[DRIFT]` |
| `LogTemplateCoverageTests` proven red | its own `The_Guard_Rejects_A_Template_That_Names_A_Patient` runs the real scanner over `{PatientName}`/`{Phone}`; the masked twin proves the accept side |
| `ContentSecurityPolicyAgreementTests` proven red | two cases run the real parsers over a Caddyfile with `frame-ancestors` changed and a console config with `object-src` widened |
| The suite catches the constructor cascade | `ClinicArchiveEndpointTests` failed to **compile** on the handler's new dependencies and was updated in lock-step |

### verify-schema, before → after

⚠️ **The before-baseline was taken on a dev database that had not had Part C's migration applied either**, so the
diff carries two migrations rather than one. Both are accounted for:

| Line | Before | After |
|---|---|---|
| `AuditEntries(ChainKey, Sequence)` | absent | `present (unique)` |
| `audit-chain-intact` | *(the check did not exist)* | `6 chaîne(s) intactes — 179 vérifiées, 1104 antérieures` |
| `audit-declared-gaps` | *(the check did not exist)* | `5 interruption(s) déclarée(s)` |
| `google-token-protected` | « not applicable » | **`1 cabinet(s) … en clair`** — Part C's column now exists and its backfill runs at **startup**, which this dev machine's own transit check refuses. Not a Part D regression |
| `key-ring-protection`, `secrets-protected-under-current-ring` | DRIFT | DRIFT — unchanged, and expected on a developer machine with no PKCS#12 (DEV-9's Development exemption) |

## Findings — things that were wrong before this part, or wrong in it

### F-11: `StepUpDialog` shipped in Part A with no caller at all

`web/components/security/step-up-dialog.tsx` is complete — sheet below `md:`, `dvh`-sized, focus on the field,
both proofs, the « votre session reste ouverte » sentence — and `grep -rn "StepUpDialog" web/` matched **only its
own definition**. Part A built FR-1.8's mechanism and Part D is its first consumer, which is what the story's own
split intended (« the step-up itself ships here; Part D applies it to the archive ») — but it means the component
was **unexercised** for two parts, and `UsersController.ResetTotp`'s step-up gate had no client path either. The
archive now wires it; **the admin factor reset still has none**, and that is a real gap in Part A recorded here
rather than quietly fixed under Part D's heading.

### F-12: the enforcing-CSP guard Part B *reserved* was written permissively, and would have stayed green

`TransportConfigurationTests.The_Enforcing_Csp_Key_Is_Never_Present_And_Off` asserted « absent **or** true », with
a docstring promising it would start asserting the full requirement « with no edit » once Part D landed. It would
not have: absent passes. Renamed to `The_Content_Security_Policy_Is_Enforced` and made mandatory, with two
siblings for the chain key and the durable log volume. Worth generalising — a placeholder assertion that tolerates
the absent case cannot become load-bearing on its own.

### F-13: the compose file still claimed the key ring was cleartext

`deploy/docker-compose.hosted.yml`'s `dataprotection_keys` comment said « back it up SEPARATELY from
`postgres_data` … the ring is stored in cleartext », which Part C's F-6 made false — the ring is now encrypted and
what must travel apart is the **certificate**. Part C fixed `README.md`, `.env.hosted.example` and
`KEY-CUSTODY.md` and missed this one. Corrected while adding the logs volume beside it.

### F-14: eager construction turned a startup guard into a container-build failure

The chain-key provider was first registered as an **instance** (`AddSingleton(new AuditChainKeyProvider(…))`), so
`AddInfrastructure` threw for any hosted-profile caller without a key — three test fixtures and every console
verb. The refusal is correct; its *timing* was not, and it surfaced as an unrelated resolution error instead of
the operator sentence it carries. Registered as a factory, with `Program.cs` resolving it once at startup beside
`TransportAssurance`. Found by running the full suite, not by reading the code.

## Still owed (verification, not code)

- **The eye pass at 320 / 390 / 820 / 1180 / 1440 plus a landscape phone and the on-screen keyboard.** No browser
  was driven in this session, so it is recorded as **not done** rather than claimed. The surfaces are the archive
  card (« Paramètres » → Sauvegarde) and the step-up sheet it now opens. Owed for Part A too.
- **Walking the whole app under the enforcing policy with zero violations**, including a PDF preview, a document
  print, a CSV export and a patient-file download — the four `blob:` paths. The policy is enforcing in the compose
  files and the analytics script is gone; what has not happened is somebody loading the pages with it on.
- **« No patient name in any log file after a full day of use. »** The static half is held by
  `LogTemplateCoverageTests`; a day of real traffic is not something this session can produce.
- **The archive refused in French when the ledger cannot be written**, and **an aborted download recorded as not
  delivered** — both implemented and covered at the handler, neither walked over the wire.
- **Lock contention measured on a seeded clinic (R-7).** The appender takes one advisory lock per chain per save;
  no measurement was taken.
- **A test cabinet was left on the dev database** — « Cabinet Chaîne Test », created by `provision-clinic` so the
  chain had genuinely chained rows to tamper with. Harmless local test data; named so nobody wonders.

## Deviations

### DEV-14: the chain is assigned in `ApplicationDbContext.SaveChangesAsync`, not in `AuditSaveChangesInterceptor.FlushAsync`
**Date:** 2026-08-12 · **Story:** Part D, step 4 · **Category:** Technical
**Original plan:** *« In `AuditSaveChangesInterceptor.FlushAsync`, open an explicit transaction on the audit
context and inside it … `pg_advisory_xact_lock(chainKey)` → read the tip → assign → insert → commit. »*
**Actual implementation:** the lock, the tip read and the assignment happen in
`ApplicationDbContext.SaveChangesAsync`, which opens a transaction when the caller has none. `FlushAsync` is
unchanged apart from the declared-gap fallback.
**Justification:** **the interceptor is not the only writer of the ledger.** `ClinicArchiveRestorer` stages a
summary row per table through `IAuditEntryRepository`, into its caller's own transaction, deliberately — and Part
D adds two more direct writers (the archive ledger's request and delivery rows). Chaining at the point of
*collection* would leave every one of those **unchained after chained rows**, which is precisely the signature
`AuditChain.Walk` reports as tampering: the feature would manufacture its own alarm. Chaining where the rows are
**saved** catches every writer, present and future, by construction — the interceptor's own argument (« every
write funnels through `SaveChangesAsync`, so this sees them all ») applied one level down. The plan's requirement
that the transaction span the whole append is **met**, and is what the override exists to guarantee.
**Impact:** synchronous `SaveChanges` now throws if audit rows are pending — unreachable (the product saves
asynchronously) and refusing beats chaining without a lock. `ApplicationDbContext` takes a third optional
constructor parameter.
**Approved:** auto (the plan's literal placement makes three of the feature's own writers unchainable)

### DEV-15: the chain walk runs in the reader, not in `SchemaVerificationService`
**Date:** 2026-08-12 · **Story:** Part D, step 6 · **Category:** Technical
**Original plan:** the two checks « both call the real `AuditChain`, never SQL ».
**Actual implementation:** they do — but `SchemaVerificationReader` calls `AuditChain.Walk` as it streams, and
`SchemaFacts` carries the **verdicts** rather than the entries.
**Justification:** every other fact on `SchemaFacts` is a count or a small projection bounded by the number of
cabinets or accounts. This one is bounded by a practice's **whole history**: carrying it into Application so the
service could re-derive it would put every audit row a deployment has ever written in memory at once. The property
the plan protects — one arithmetic, never re-expressed in SQL — is untouched, and the split matches the project's
own testing seam (Domain tests the walk, the service tests the rendering).
**Impact:** `AuditChainFacts` holds `IReadOnlyList<AuditChainWalkResult>`. The reader takes an optional
`IAuditChainKeyProvider`; absent ⇒ « not applicable », never « intact ».
**Approved:** auto (the plan's property is preserved; only where the loop runs changed)

### DEV-16: the export notification is clinic-wide, not addressed to administrators
**Date:** 2026-08-12 · **Story:** Part D, step 7 · **Category:** Technical
**Original plan / spec:** Stated Assumption 9 — « Notify administrators (for the export). »
**Actual implementation:** one clinic-wide `StaffNotification` with the actor excluded.
**Justification:** the feed carries **one shared row per event with at most one target user**, so « les
administrateurs » is not expressible without a fan-out mechanism the model does not have — and building one for a
single category is a schema change in the last step of the last part. A clinic-wide row reaches every
administrator (a superset) and is the right side to err on: an export is not a private fact about one colleague,
it is every patient of the practice leaving the building in an unencrypted file. The actor is excluded as
everywhere, so nobody is told about their own action.
**Impact:** doctors and secretaries see it too. Recorded on the enum member, where a reader finds it.
**Approved:** auto (the literal is unbuildable without a schema change; the substitute is strictly wider)

### DEV-17: FR-4.6 is resolved by REMOVING the redirect, not configuring it
**Date:** 2026-08-12 · **Story:** Part D, step 17 · **Category:** Technical
**Original plan / spec:** « It is either configured or removed. »
**Actual implementation:** removed, with the reasoning left in its place in `Program.cs`.
**Justification:** the spec offers both and this is the one that is *correct* rather than merely allowed. Behind
Caddy the API receives plain HTTP by design and, since Part 2's `UseForwardedHeaders`, reports `IsHttps` true — so
a configured redirect would either fire on nothing or, on a misread header, bounce the proxy's own hop. Caddy
already redirects at the edge, which `caddy validate` states in its own output. `SelfHostedLan` never registered
it, so no profile loses a behaviour it had.
**Impact:** none observable. One registration deleted.
**Approved:** auto (the spec offers the choice; this is the choice with a reason)

# Progress — Hosted Security Hardening

**Story:** [story-1-full-hosted-security-hardening.md](./story-1-full-hosted-security-hardening.md)
**Worktree:** `.claude/worktrees/hosted-security-hardening/` · **Branch:** `feature/hosted-security-hardening`
**Base:** `9a90d54` (tip of `feature/windows-desktop-app`)

## Part status

| Part | Name | Plan part | Status |
|------|------|-----------|--------|
| A | Identity | Part 1 | **implemented** (A.1–A.4 landed; eye pass owed) |
| B | Transit | Part 2 | **implemented** (steps 1–11; two walks owed, both named below) |
| C | Custody | Part 3 | not-started |
| D | Evidence & surface | Part 0 + Part 4 | not-started |

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

**Parts A and B are landed in full and the tree is green.** Start at **Part C** (Custody), whose entry section is
`exploration.md` § 3. Nothing is half-applied.

⚠️ **Part C must not mint a fresh Data Protection key ring** (R-2) — Part A's second factors live on it.

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

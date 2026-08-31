# Security & Tunisian-compliance remediation — what is still outstanding

> **Type:** incomplete
> **Priority:** high
> **Created:** 2026-08-31
> **Feature:** general (cross-cutting — arose from a whole-codebase security + data-protection audit)

## Summary

A whole-codebase security and Tunisian data-protection audit produced 15 commits on
`feature/security-remediation`. This records what was **not** finished, split by who can act: four items only the
owner can do (§ 1), a code backlog (§ 2), and three of the repository's own derived guards that have been red
since before this work started (§ 3).

**Source audit:** <https://claude.ai/code/artifact/fa9cb21c-21c0-44db-a733-48ee1e782258>
**What was done:** `follow-up/security-remediation-progress.md` (batch by batch, with reasoning)

---

## 1. Owner-only — nothing in the codebase can do these

Ordered by exposure. **Item 1.1 outranks everything else in this file.**

### 1.1 🔴 Verify where the production data physically sits

`desktop/ClinicManagement.DesktopShell/ServerConfig.cs:47` pins every installed client to
`vps-dc7e4229.vps.ovh.net`. **OVH publishes no Tunisian datacentre.** Under *loi organique 2004-63* art. 51–52 a
transfer of health data abroad needs prior INPDP authorisation, and the art. 90 penalty falls on the **cabinet**,
not on the vendor — so every practice using the product is exposed, not just you.

One console lookup settles it. If it is not Tunisia, plan the move: `deploy/README.md` § « Résidence des
données » already carries a provider shortlist (EO Data Center, DataXion).

⚠️ Two sidecars ship a full copy off-server independently of where the app runs — `WALG_S3_ENDPOINT`
(continuous) and `BACKUP_REMOTE` (nightly, and **unverifiable from the application** by construction).

### 1.2 🔴 Revoke the leaked credentials — they are still live

Commit `0e4d343c` shipped a Google OAuth `ClientSecret`, a live `RefreshToken` with Calendar scope, and a
HuggingFace key. Removing them from HEAD is not rotation, and **`api/ClinicManagement.API/appsettings.json:113`
still carries the same `ClientId`** — verified byte-for-byte against the leaked commit — so the leaked secret is
valid for the live configuration. **Burn the OAuth client, not just the secret.**

Mitigating: the repository is private, so exposure is collaborators / forks / CI rather than the open internet.

### 1.3 🟠 Run the restore drill; fill in the key custody table

`deploy/RESTORE-DRILL.md` states the restore path is unproven. `deploy/KEY-CUSTODY.md:26-30` is still
`_(name, role)_` on all five rows — while `SECURITE-DOSSIER-PATIENT.md` used to tell cabinets the keys are under
« une garde formalisée avec un dépositaire nommé ». That sentence was corrected in `531561aa` to describe what is
actually promised; **filling the table is what makes the original claim true again.**

### 1.4 🟠 The compliance filings and the documents only counsel can finish

- Nothing is filed with the INPDP (declaration, health-data authorisation, transfer authorisation)
- No DPAs with OVH or the SMTP/SMS providers
- No breach-notification procedure
- `site/src/pages/confidentialite.html` carries `[bracketed]` gaps — legal identity, hosting location, contact
  address — and must be read by Tunisian counsel before it is published
- Budget the annual ANCS audit (`Décret-loi 2023-17`); confirm with counsel whether it reaches a product this size

---

## 2. Code backlog

### 2.1 🔴 Consent capture + per-patient reminder opt-out — **the last blocked item, now unblocked**

Recording a phone number **auto-enrols** a patient into SMS/WhatsApp reminders. Neither the patient nor the
cabinet can exempt anyone. This is a live compliance gap, not a missing nicety.

It needed a migration, and the EF snapshot was dirty with another session's uncommitted work — that landed in
`413b3891`, so **it is unblocked now**.

| File | Lines | Purpose |
|---|---|---|
| `api/ClinicManagement.Infrastructure/Services/ReminderScheduler.cs` | 98, 172 | The two enqueue sites, gated on `HasDeliverablePhoneAsync` alone — the consent check goes beside it |
| `api/ClinicManagement.Domain/Entities/Patient.cs` | — | Needs the flag + a `SetReminderConsent` mutator |

**Approach:** a column on `Patient`, the check at both enqueue sites, the field on the patient form, tests.
Consider recording *who* set it and *when* — a consent nobody can date is hard to defend.

### 2.2 🟠 Thumbnails lost their picture for every pre-existing file

`736332cd` stopped thumbnails fetching the **original** to paint a 40 px tile — it was pulling full-size
radiographs, and once downloads were audited it wrote one journal row per visible tile. Tiles now use the
downscaled stand-in only.

Every file uploaded **before** the preview feature has `hasPreview: false`, so those files now show their icon.
**A backfill of previews for existing files restores them.** That belongs to the coffre/preview feature
(`413b3891`), not to this one.

| File | Lines | Purpose |
|---|---|---|
| `web/components/patients/files/file-thumbnail.tsx` | ~78-100 | The eligibility rule and the reasoning |
| `api/ClinicManagement.Domain/Entities/PatientFile.cs` | — | `PreviewStorageKey` — null on every legacy row |

### 2.3 🟠 The archive-grant token is still a full clinic-admin token

`POST /api/backup/archive-grants/token` exchanges a device secret for an **ordinary 30-minute clinic-admin
access token with the whole API surface** — not one scoped to the archive. `ccc6608a` gave the grant a 90-day
idle expiry, which bounds the exposure; it does not remove the over-grant.

**Approach:** mint a token with its own audience or claim that only `GET /api/backup/archive` accepts. Touches
`Program.cs`'s JWT validation, so it is not a drive-by. **Fixing this also clears two of § 3's red guards.**

### 2.4 🟠 Truncating a chain at its tip is still undetectable

`198ed577` put `ClinicId` and `UserEmail` into the hash (scheme `v2:`) and `f3bd8c0e` added
`rehash-audit-chain` for the older rows. What remains: nothing persists an **expected tip** out of band, so
deleting the newest *k* rows returns `Break.None` and the next append re-links from whatever tip it finds.

**Approach:** store the tip where the chain key lives (configuration / an operator-held file), not in the
database the tip protects. Needs a decision about where, hence not done.

### 2.5 🟠 SSRF — a hostname is never resolved before it is used

`api/ClinicManagement.Domain/Common/OutboundEndpoint.cs:94-116` blocks a literal suffix list and IP literals, but
`IPAddress.TryParse` returns false for any hostname, so hostnames pass unconditionally. **The file's own docstring
(`:19-24`) names the missing half — « that half is owed ».** On `HostedMultiTenant` public signup creates an
admin, and that admin can point `smtpHost` at e.g. `127.0.0.1.nip.io`.

**Approach:** a `SocketsHttpHandler.ConnectCallback` re-running `IsPublic` against the **resolved**
`IPEndPoint`, and resolve-then-check before the SMTP connect. The HTTP channels are protected today only by the
accident that `https` is forced; SMTP has no such protection.

### 2.6 🟡 83 handlers return the raw exception message to the client

Pattern: `catch (Exception ex) when (ex is not ConflictException)` → `Result.Failure($"…{ex.Message}")` →
`ApiControllerBase.HandleFailure` → `{ error }` verbatim. Npgsql SQLSTATE and table names, S3 endpoints and
server file paths reach an authenticated browser.

**Approach:** in the generic-`Exception` branch only, log `ex` and return `ErrorMessages.Generic`. Leave the
typed `InvalidOperationException`/`ArgumentException` catches alone — those carry deliberate French domain text.

### 2.7 🟡 The at-rest gaps, each a deliberate decision to revisit rather than a bug

- **No PHI column encryption** — rejected with reasons in `features/hosted-security-hardening/spec.md:338`
  (breaks SQL search, duplicate detection, ordering). Volume encryption is the compensating control.
- **No MinIO server-side encryption.** Deliberately not done here: SSE-S3 fails closed unless MinIO has a KMS
  key, so enabling it blindly breaks *every* upload — and with the key on the host LUKS already protects, it buys
  little. Doing it properly means `MINIO_KMS_SECRET_KEY` + a config-gated `.WithServerSideEncryption`.
- **On-premise installs have no disk-encryption requirement.** `DirectoryAclHardener` protects the Postgres data
  directory with NTFS ACLs, which a removed disk defeats entirely. BitLocker appears in exactly one place
  (`desktop/…/ArchiveCopyWindow.xaml.cs:61-68`, for the *archive copy* destination) and is « stated, never
  enforced ». **A stolen clinic PC is the most likely real breach for a Tunisian cabinet.**

### 2.8 🟡 Supply chain and backups

- **`vpk pack` runs with no `--signParams`** (`.github/workflows/client-installer.yml:182-189`) and clients pull
  from an anonymous feed — unsigned code installed silently on every clinic PC
- **No object-lock or immutability on the backup store**, and the server holds full read-write S3 credentials
  plus a `wal-g delete` path — ransomware deletes the backups with the keys it already found

### 2.9 🟡 Smaller, each contained

- CSP carries `script-src 'unsafe-inline'` — constrains origins, does not stop XSS. Needs nonces +
  `strict-dynamic` and its own page walk
- Rate-limit / TOTP-replay / step-up stores are **per-process** — correct on one instance, silently weaker on the
  first `--scale api=2`
- `deploy/docker-compose.selfhosted-lan.yml:34` hardcodes `POSTGRES_PASSWORD: clinic_password`
- No known-default rejection for `Console:SigningKey` (MinIO has one; the console key does not)
- `appsettings.Development.json` ships in the published image — add `<Content Remove="…" />`
- Password policy is **length only** (12) — no breach-list check, no reuse prevention

---

## 3. Red before this work started — deliberately not papered over

`dotnet test` on this branch is **3599 passed, 3 failed**. All three name `Backup.ExchangeArchiveGrant` /
`ClinicArchiveGrant.SecretHash`:

- `ControllerAuthorizationCoverageTests.No_unexpected_anonymous_endpoints_exist`
- `SubscriptionExemptionCoverageTests.No_Unreviewed_Write_Is_Exempt_From_The_Subscription_Gate`
- `SecretProtectionCoverageTests.Every_Credential_Shaped_Column_Is_Encrypted_Or_A_Named_Decision`

Both symbols exist in the fork point and neither file was touched here. **Three of the repository's own derived
guards have been red since the archive feature shipped**, which is how an anonymous endpoint minting a full
clinic-admin token from a never-expiring grant reached production unreviewed.

They are **not** fixed by adding allow-list entries. § 2.3 is the real fix, after which two should go green on
their own merits.

### Also flagged by `verify-schema`, not investigated here

- `audit-chain-intact`: **all 7 chains broken on the dev database**, breaks dated 2026-08-14 (likely the
  `restore-backup`, and there are 5 declared gaps). Confirmed **not** caused by the scheme-v2 change — the one
  chain carrying v2 rows breaks at sequence **1**, not at 1685 where its v2 rows begin. Worth confirming
  production is clean.
- `key-ring-protection`: key ring not encrypted at rest (expected in dev; **must not** be true in production)
- `messaging-month-covers-every-clinic`: 1 of 7 cabinets has no counting row for 2026-08
- `overlapping-appointment-pairs`: 1 pair blocks the exclusion constraint

---

## Before deploying to `main`

1. **`WALG_LIBSODIUM_KEY` must be set in production.** `deploy/postgres/wal-push-guard.sh` now makes
   `archive_command` **fail** without it — fail-closed by design, but if it is unset today WAL archiving stops
   and `pg_wal` grows.
2. **Reception will meet a new identity prompt** when exporting the patient list. Tell them before they meet it.
3. A **dormant clinic PC's unattended archive copy stops after 90 days idle** (renews on any use).
4. **Audit write volume rises materially** — the clinical record now produces rows where it produced none.
5. This branch forked from `feature/clinic-archive-auto-copy`, **not `main`** — merging brings that too.

## Acceptance criteria

- [ ] The OVH region is confirmed in writing, and a move is planned if it is not Tunisia
- [ ] The Google OAuth client, its refresh token and the HuggingFace key are revoked at the provider
- [ ] A restore drill row exists in `deploy/RESTORE-DRILL.md` with a date and a name
- [ ] `deploy/KEY-CUSTODY.md` names a real holder and a real location for all five keys
- [ ] A patient can be exempted from reminders, and no reminder is enqueued for them
- [ ] The archive-grant exchange yields a token that `GET /api/backup/archive` accepts and other endpoints refuse
- [ ] `dotnet test` is **3602 passed, 0 failed**
- [ ] `verify-schema` on production exits 0

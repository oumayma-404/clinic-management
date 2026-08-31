# Security & compliance remediation — progress

**Branch:** `feature/security-remediation` (forked from `feature/clinic-archive-auto-copy`)
**Started:** 2026-08-31 · **Last updated:** 2026-08-31
**Source audit:** whole-codebase security + Tunisian data-protection review, published at
<https://claude.ai/code/artifact/fa9cb21c-21c0-44db-a733-48ee1e782258>

The audit ranked everything. This file records what has been *fixed*, what is *half-built*, what is *blocked*
and why, and what is not mine to do at all. Read § 5 before writing any migration on this branch.

---

## 1. Batch 1 — DONE, committed as `b819da22`

22 files, +573 / −82. Ten findings, none of which produced an error before.

| # | What | Where |
|---|---|---|
| 1 | `.gitignore` now covers the seven operator-created secret paths, incl. **`deploy/backup-identity.txt`** — the age private key that decrypts every off-site backup of every cabinet. `web/.auth/state.json` untracked. | `.gitignore` |
| 2 | **CSV formula injection** on all ten exports. A leading `-` is only neutralised when it is not a number, so negative dinars still sum. | `CsvTable.Escape`/`Neutralise` |
| 3 | **Patient names in the durable log.** Seven statements in the Google Calendar services, two at Information/Warning. | `GoogleCalendarSyncService`, `GoogleCalendarService` |
| 4 | **The log guard now reads argument expressions, not just placeholder names** — the reason it missed all seven. It found two more sites the review agents had not. | `LogTemplateCoverageTests` |
| 5 | **Log retention was 7 days, not the documented 30.** The whole `Serilog` section of `appsettings.json` was read by nothing. Deleted rather than wired — wiring it would let a config file re-enable EF command logging, which writes SQL with its parameters. | `Program.cs`, `appsettings.json`, `docker-compose.hosted.yml` |
| 6 | **TOTP replay guard reached 1 of 6 verification sites.** Now on console login, step-up and `ManageTotpCommands` too. | `PlatformLoginCommand`, `StepUpCommand`, `ManageTotpCommands` |
| 7 | **`TotpReplayCoverageTests`** — derived guard: every `VerifyCode` call site must also spend the code. Two enrolment sites are exempt *with reasons*, asserted in both directions. | new |
| 8 | **`POST /api/auth/totp/enrol` was an unauthenticated password oracle** — no lockout tier, no recorded failure. | `EnrolTotpCommand` |
| 9 | **WAL could ship in cleartext.** The refusal existed only in the *sidecar* entrypoint; the WAL is pushed by `postgres`'s own `archive_command`. | `deploy/postgres/wal-push-guard.sh` (new), `Dockerfile`, `docker-compose.prod.yml` |
| 10 | PBKDF2 100 000 → **210 000** (OWASP). Existing accounts migrate on next sign-in via the rehash path already in place. | `LocalAuthService` |

**Deliberately NOT done: MinIO server-side encryption.** The review agent called it "one line". It is not:
SSE-S3 fails closed unless MinIO has a KMS key configured, so enabling it blindly breaks *every* upload — and
with the key on the same host LUKS already protects, it buys very little. `features/hosted-security-hardening/
spec.md:338` already rejects application-level encryption with reasons. If you want it, it is a deliberate piece
of work (MinIO `MINIO_KMS_SECRET_KEY` + a config-gated `.WithServerSideEncryption`), not a drive-by.

---

## 2. Batch 2 — MOSTLY DONE

Commits `08b2d765` and `ccc6608a`.

| Done | What |
|---|---|
| ✅ | **Clinical-record audit coverage.** `IAuditable` + the interceptor accepting either marker; ten entities marked; `ClinicalRecordAuditCoverageTests` derives the rule from the model (carries a `PatientId` ⇒ must be auditable). **No migration was needed** — `AggregateRoot<T>` adds nothing to `Entity<T>`, it is a pure marker. |
| ✅ | **Export controls on `/api/patients/export`** — step-up (server + the shared `ExportButton` opt-in + the patients page), a `ListExport` rate-limit policy, and a non-best-effort audit row that refuses the export if it cannot be written. |
| ✅ | **Export controls on `/api/appointments/export`** — rate limit + audit row, deliberately **no** step-up (a date range printed daily is not the identified medical dataset; see `ExportAppointmentsQuery`). |
| ✅ | **Server-side logout.** `EndSessionCommand` + `POST /api/auth/logout` + the BFF actually calling it. A captured refresh credential used to stay valid 12 h after sign-out. |
| ✅ | **Archive-grant expiry** — 90 days idle, sliding from last use, **no migration** (`CreatedAtUtc`/`LastUsedAtUtc` already existed). A grant used to be a permanent admin credential. |
| ✅ | **`ClinicId` + `UserEmail` into the audit chain hash** — done **without** a migration, by versioning the canonical form (`AuditChain.SchemeV2Prefix`). New rows are marked `v2:` and cover the tenancy; pre-change rows keep verifying under `LegacyHash`. The scheme is **read off the stored value, never guessed** — a « try v2, else v1 » fallback would have let a nulled `ClinicId` fall through to v1 and verify clean, re-opening the hole. Old rows stay as protected as they always were, and `A_Legacy_Entry_Is_Only_As_Protected_As_It_Ever_Was` records that in an executable test; a rehash migration is still what closes them. |
| ✅ | **Read-auditing** — `AuditAction.Read` + `PatientRecordAccessLedger`, on both doors onto a patient's bytes (download **and** preview), with `PatientFileAccessCoverageTests` deriving the rule from the call sites so a third door is covered on the day it is written. Content leaving, not screens opened. |
| ⛔ | Scoping the archive-grant token (it still mints a full clinic-admin token; expiry bounds it, scoping would remove the over-grant). Needs new audience/claim handling in auth validation. |

### 2.1 The two that stay blocked, in detail

**The old rows now have a route: `rehash-audit-chain`.** A console verb rather than a data migration, because it
needs the **chain key**, which lives in configuration and deliberately not in the database — a migration has no
access to it, and giving it access would put the key on the restore path it exists to stay independent of. Dry
run by default; `--apply` writes under one transaction; and it **refuses any chain that does not already verify**,
because rehashing a tampered row would launder it into a valid v2 hash and destroy the only evidence there was.

**Still open on the chain: the tip.** Truncating the newest entries of a chain remains
undetectable — nothing persists an expected tip out of band, so deleting the last *k* rows returns `Break.None`
and the next append re-links from whatever tip it finds. And rows written before scheme v2 are still hashed
without their tenancy; a rehash migration (verify each under `AuditChain.LegacyHash`, rewrite under `Hash`) is
what closes them, and it is **blocked** by § 5.

**Read-auditing on downloads.** `AuditAction` has only `Insert`/`Update`/`Delete`, so nothing records that a
radiograph was downloaded. Adding the enum member needs no migration, but every call site
(`DownloadPatientFileQuery`, `PatientFilesController`, the medical-document path) is inside the concurrent
session's edit set — see § 5.1.

## 3. Batch 3 — PARTLY DONE

Commits `0b683ce4` and `531561aa`.

Two are feature-sized with frontend work and a device-contract pass; the scope is the owner's call.

- ✅ **The clinic-facing dossier corrected — `531561aa`.** `SECURITE-DOSSIER-PATIENT.md` is written to be handed
  to a cabinet's legal counsel, and three of its claims did not survive checking: the key-custody promise (the
  custody table is still placeholders), the residency claim (which explicitly named « services de messagerie »,
  the one category the guard does *not* inspect), and « les exports complets sont nominatifs » (true of the ZIP
  only). Now v1.1. Two other claims became true rather than being withdrawn — the clinical-record journal and
  no-PHI-in-logs — because Batch 1 and 2 fixed them.
- ⛔ **Consent capture + per-patient reminder opt-out — THE LAST ITEM, and the only one still blocked.**
  Everything else in this file is done. This needs a persisted per-patient flag, i.e. a column on `Patient`, i.e.
  a migration — and § 5.0's block is still in force. It is not a design problem: the two enqueue sites are
  `ReminderScheduler.cs:98` and `:172`, both gated on `HasDeliverablePhoneAsync` alone, and the check goes beside
  it. **Unblock it by committing (or stashing) the other session's work**, then it is one focused change:
  a column + `Patient.SetReminderConsent`, the check at both enqueue sites, the field on the patient form, and
  tests. Was: **Consent capture + per-patient reminder opt-out.** There is *no* consent field in any of the 66 domain
  entities, and recording a phone number **auto-enrols** the patient into SMS/WhatsApp — the only gate on
  enqueuing is `HasDeliverablePhoneAsync`. Neither the patient nor the cabinet can exempt one person.
- ✅ **Per-patient dossier export — DONE.** `PatientDossierPackager` + `ExportPatientDossierQuery` +
  `GET /api/patients/{id}/dossier` (step-up + rate-limited + recorded against that patient) + a « Dossier » button
  on the patient page. A file whose original is at the cabinet is **listed** with its date rather than silently
  dropped. Was: ⛔ **Per-patient dossier export.** Every export is list-scoped; nothing assembles one patient's complete record.
  This is the right-of-access mechanism, and it is also what a patient changing dentist asks for constantly.
- ✅ **Privacy notice — DONE.** `site/src/pages/confidentialite.html`, built and live at `dist/confidentialite.html`,
  which also fixes the footer link that had pointed at a non-existent file since the site shipped. Written from the
  actual field-by-field inventory, not a template. ⚠️ **It carries `[bracketed]` gaps that only you can fill** —
  the vendor's legal identity, the hosting location, the contact address — and it must be read by Tunisian counsel
  before it is published.
- ✅ **Retention position — DONE.** `RETENTION-ET-CONSERVATION.md`: what the code already bounds (with the class
  names), what is unbounded and the position to take on each, what the deletion path really does, and § 4's honest
  list of what is missing to hold the policy. `GO-LIVE.md:286` can be ticked for the vendor half; the cabinet half
  (how long a dossier is kept) is a decision only the practice and its counsel can make.

---

## 4. Batch 4 — OWNER ONLY (I cannot do these)

1. **Confirm the OVH region for `vps-dc7e4229.vps.ovh.net`** (`desktop/…/ServerConfig.cs:47`). OVH publishes no
   Tunisian datacentre. If it is not Tunisia, every cabinet using the product is exposed under *loi organique
   2004-63* art. 51–52, and the art. 90 penalty falls on the **cabinet**, not on you. This is the single most
   consequential open question in the whole audit.
2. **Revoke the Google OAuth client, its refresh token, and the HuggingFace key.** Commit `0e4d343c` shipped
   them; the `ClientId` still in `appsettings.json:113` is byte-identical to the leaked one, so rotating the
   secret alone is not enough — burn the client.
3. **Run the restore drill** (`deploy/RESTORE-DRILL.md`) and **fill in `deploy/KEY-CUSTODY.md`**, which is still
   `_(name, role)_` placeholders — while `SECURITE-DOSSIER-PATIENT.md` tells cabinets the keys are under
   « garde formalisée avec un dépositaire nommé ».
4. **INPDP filing, the ANCS audit, DPAs** with OVH and the SMTP/SMS providers, and a breach-notification
   procedure. Nothing is filed today.

---

## 5. ⚠️ Two hazards before you resume

### 5.0 Current state of the block (as of the last update)

The other session went quiet around 11:10 and left **~50 files uncommitted**, including
`ApplicationDbContextModelSnapshot.cs`. The snapshot now *does* carry their `FileResidency` property, so
`dotnet ef migrations add` would emit only this branch's own columns — but committing the resulting snapshot
would carry **their** model change into a security commit, and committing the migration without the snapshot
leaves the two drifting. So migrations stay blocked until their work lands, and everything in this file marked ⛔
waits on that. Nothing here is blocked on a technical unknown; it is blocked on a working tree with two authors
in it.

### 5.1 A concurrent session was writing to this repo

While Batch 1 was being written, a **`FileResidency`** feature appeared in the working tree —
`UploadPatientFileCommand.cs` modified 11:10, migration `20260831101341_AddPatientFileResidency` touched 11:13.
None of it is committed on this branch and none of it was staged.

One courtesy edit was made and **left uncommitted**: `UploadPatientFileAtomicityTests.cs` gained the missing
`IFileResidencyPolicy` mock (returning `FileResidency.Hosted`, since the enum has no `0` member) purely so the
test project would compile. Its owner should replace it.

### 5.2 Do NOT run `dotnet ef migrations add` until that lands

`ApplicationDbContextModelSnapshot.cs` is **not** updated for their migration. So EF would diff the live model
(which has their `FileResidency` property) against the committed snapshot (which does not) and emit **their**
column into **your** migration. This is the hazard already recorded in the project memory. Every § 2.3 item needs
a migration, which is why they are blocked rather than merely unstarted.

---

## 6. Three pre-existing test failures — not caused here, and deliberately not papered over

`dotnet test` on this branch is **3 568 passed, 3 failed**. All three name `Backup.ExchangeArchiveGrant` /
`ClinicArchiveGrant.SecretHash`:

- `ControllerAuthorizationCoverageTests.No_unexpected_anonymous_endpoints_exist`
- `SubscriptionExemptionCoverageTests.No_Unreviewed_Write_Is_Exempt_From_The_Subscription_Gate`
- `SecretProtectionCoverageTests.Every_Credential_Shaped_Column_Is_Encrypted_Or_A_Named_Decision`

Both symbols exist in `HEAD` and neither file was touched here — verified. **Three of the repository's own
derived guards have been red since the archive feature shipped**, which is what let an anonymous endpoint that
mints a full clinic-admin token from a never-expiring grant reach production unreviewed.

They are **not** fixed by adding allow-list entries. The real fix is § 2.3's archive-grant work — scope the
token, expire the grant — after which two of the three should go green on their own merits.

---

## 7. How to verify

```bash
# Build and test OUTSIDE the repo — Smart App Control refuses freshly-built in-repo assemblies.
dotnet test api/ClinicManagement.UnitTests -c Release -p:BaseOutputPath=<temp>/
# Expect: 3568 passed, 3 failed (the three in § 6).

# Before and after ANY migration batch, and diff the two:
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema
```

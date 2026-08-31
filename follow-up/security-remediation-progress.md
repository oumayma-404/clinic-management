# Security & compliance remediation — progress

**Branch:** `feature/security-remediation` (forked from `feature/clinic-archive-auto-copy`)
**Started:** 2026-08-31 · **Paused:** 2026-08-31
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

## 2. Batch 2 — IN PROGRESS

### 2.1 Half-built and committed as WIP: export controls on `/api/patients/export`

**The finding.** That endpoint returns twenty columns per patient — *Nom, Prénom, Date de naissance, Adresse,
Identifiant CNAM, Antécédents médicaux, Allergies, …* — i.e. the cabinet's whole identified medical dataset, and
it carried **none** of the four controls the whole-clinic ZIP archive carries: no step-up, no rate limit, no
audit row, open to every clinic role. The archive is guarded as « the practice on a laptop »; this is the same
data through a different door.

**Written so far (compiles, tested by nothing yet, wired to nothing yet):**

- `Features/Patients/PatientExportLedger.cs` — appends a **non-best-effort** `AuditEntry`; if the row cannot be
  written the export is refused. Sibling of `Backup/ArchiveAccessLedger.cs`, with the reason for being a sibling
  rather than a shared abstraction written down. Records the row count and *which* filters were applied, never
  the filter **values** — a search term here is a patient's name.
- `Features/Patients/Queries/ExportPatientsQuery.cs` — a Query that writes (`BuildClinicArchiveQuery`'s
  precedent and its exact reason: a Command would broadcast `patients` on every export). Reuses
  `GetPatientsQuery` unpaged rather than repeating the filter logic, so only the export path audits — auditing
  the shared query would put a ledger row on every page turn of the patients screen.

**Still to do for this item:**

1. Point `PatientsController.ExportPatients` at `ExportPatientsQuery` instead of `GetPatientsQuery`.
2. Add a rate-limit policy beside `RateLimiting.ArchivePolicy` and put `[EnableRateLimiting(...)]` on it.
3. Add step-up: `[FromHeader(Name = BackupController.StepUpHeader)]` + a `RequireStepUp(...)` guard, with a new
   action constant (the archive's is `download-clinic-archive`). **This needs the frontend too** — the flow
   already exists (`web/components/security/step-up-dialog.tsx`, and `apiGetFile` already threads a
   `stepUpToken`), so it is wiring, not new UI.
4. Tests: the ledger row is written; the export is **refused** when the ledger throws; the filter summary never
   contains the search term.
5. Same treatment for `GET /api/appointments/export`, whose CSV carries patient name + acts + notes.

### 2.2 Not started, unblocked

- **Server-side logout / session revocation.** There is *no* revoke endpoint on the API at all
  (`web/app/bff/auth/local-logout/route.ts` only clears cookies), so a captured refresh token stays valid for
  its full 12 h and rotates itself. `SessionFamily.End` exists and is called from exactly one place.
- **Read-auditing write path.** Adding an `AuditAction` member is an enum value, not a schema change, so the
  write path can be built now; only the chain rehash (§ 2.3) needs a migration.

### 2.3 Not started, **BLOCKED on a migration** — see § 5

- **Clinical-entity audit coverage.** `IsAuditable` walks for `AggregateRoot<>`; `DentalRecord`,
  `MedicalDocument`, `PatientFile`, `ToothState`, `PatientMedicalHistory`, `PatientFamilyHistory` and `Payment`
  are all `Entity<Guid>` and produce **zero** audit rows. Deleting a patient's prescriptions and x-rays leaves
  no trace. This is the single largest compliance gap in the product.
- **`ClinicId` (and `UserEmail`) into the audit chain hash.** `AuditEntry.ToChainEntry()` hashes twelve fields
  and `ClinicId` is not one — while every read of the journal filters on it. `UPDATE "AuditEntries" SET
  "ClinicId"=NULL` therefore hides a row *and the chain still verifies as intact*. Needs a rehash migration.
  Also: persist an expected chain tip out of band, or truncating the newest rows stays undetectable.
- **Archive-grant expiry.** `ClinicArchiveGrant.IsUsable` has no `ExpiresAtUtc`, so a secret on a clinic laptop
  is a permanent credential that exchanges into a full 30-minute clinic-admin token with the whole API surface.
  Needs a column, and the token should be scoped to the archive rather than being an ordinary admin token.

---

## 3. Batch 3 — NOT STARTED

Two are feature-sized with frontend work and a device-contract pass; the scope is the owner's call.

- **Consent capture + per-patient reminder opt-out.** There is *no* consent field in any of the 66 domain
  entities, and recording a phone number **auto-enrols** the patient into SMS/WhatsApp — the only gate on
  enqueuing is `HasDeliverablePhoneAsync`. Neither the patient nor the cabinet can exempt one person.
- **Per-patient dossier export.** Every export is list-scoped; nothing assembles one patient's complete record.
  This is the right-of-access mechanism, and it is also what a patient changing dentist asks for constantly.
- **Privacy notice.** No page in the app, none on the marketing site — where the « Confidentialité » footer link
  (`site/src/partials/footer.html:56`) points at a file that was never created.
- **Retention policy.** Patient records, audit rows and `DocumentEmail` bodies are kept for ever. `GO-LIVE.md:286`
  already lists this, unticked.

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

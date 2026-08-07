# Progress — patient-file-uploads

**Story:** [story-1-patient-file-uploads.md](./story-1-patient-file-uploads.md) — one story, five parts.
**Branch:** `feature/audit-sections-3-to-10` (user decision, 2026-08-07)

## Status

| Part | Covers | Status |
|---|---|---|
| P1 — server refusal reaches the user | AC-1 | in progress |
| P2 — extension-keyed catalog, six call sites | AC-2, AC-3 | pending |
| P3 — policy served, not mirrored | AC-5.1 | pending |
| P4 — rename, describe, move | AC-4 | pending |
| P5 — manager UX | AC-5.2 … AC-5.10 | pending |

## Working tree note (start of session, 2026-08-07)

`feature/audit-sections-3-to-10` was **not clean** when this story started, contrary to the session's own
snapshot. It carries **33 uncommitted files** implementing a different feature, `clinic-self-signup`:

- new: `Domain/Entities/ClinicSignup.cs`, `IClinicSignupRepository`, `ClinicSignupConfiguration`,
  `ClinicSignupRepository`, `Features/Auth/Commands/{SignUpClinic,VerifyClinicSignUp}Command.cs`,
  `ITransactionalEmailSender` + `SmtpTransactionalEmailSender`, `IPublicAppUrlProvider` +
  `PublicAppUrlProvider`, `Models/ClinicSignUpRequest.cs`, `features/clinic-self-signup/`, `web/app/signup/`
- new migration: `20260807102000_AddClinicSignups.{cs,Designer.cs}` — **and a modified
  `ApplicationDbContextModelSnapshot.cs` (+76 lines)**
- modified: `AuthController.cs` (+75), `DeploymentProfile.cs`, `Extensions.cs`, `ApplicationDbContext.cs`,
  `SchemaVerification{Service,Reader}.cs`, `ISchemaVerificationReader.cs`, three test classes, six `CLAUDE.md`s,
  `web/lib/api/auth.ts`, `web/middleware.ts`

**Excluded from every commit in this story. Staged by explicit path only — never `git add -A`.** A pre-existing
build or test failure in those files is not this story's.

This is also the direct reason `Clinic.LogoContentType` was dropped from scope: scaffolding a second migration
over an uncommitted model snapshot is how two migrations come to duplicate each other's operations.

## Deviations

### DEV-1: Feature folder created by `/implement-story`, not by the pipeline
**Date:** 2026-08-07
**Category:** Scope
**Original plan:** `/implement-story`'s Step 0 requires an APPROVED `plan.md` and a `stories/` folder produced by
`/define-feature` → `/plan-feature` → `/break-plan`.
**Actual implementation:** none of those existed. The approach came from `/think-solution`, where the user
selected Option 1 plus the format breadth and the large-file behaviour from challenged options. `spec.md`,
`plan.md`, the story file and this tracker were written from that blueprint, and the approvals are attributed to
those answers rather than to a challenge pass.
**Justification:** the user chose "minimal scaffold, then implement" when the prerequisite gap was surfaced. The
design decisions were genuinely made and recorded; what is missing is the challenge step, which is stated as
missing in both documents' headers rather than implied.
**Impact:** neither spec nor plan has been through `/challenge-spec` / `/challenge-plan`. `/review-story` should
weigh that.
**Approved:** Yes

### DEV-2: `Clinic.LogoContentType` dropped, story is migration-free
**Date:** 2026-08-07
**Category:** Scope
**Original plan:** the `/think-solution` blueprint listed it as pitfall #3 — store the validated logo content
type so `GetClinicLogoQuery.cs:74` stops hardcoding `"image/png"`.
**Actual implementation:** out of scope; captured as a follow-up. The logo still gains real *validation*.
**Justification:** the working-tree note above — an uncommitted migration plus a modified model snapshot.
**Impact:** a validated JPEG logo is still served as `image/png`. Behaviour is unchanged from today, not worse.
**Approved:** Yes — user chose this option explicitly.

### DEV-3: P1 widened from one module to all ten blob-transfer sites
**Date:** 2026-08-07
**Story:** 1, part P1
**Category:** Scope
**Original plan:** AC-1 scopes the fix to `web/lib/api/patient-files.ts`.
**Actual implementation:** `apiGetBlob`, plus two siblings the other sites needed — `apiGetFile` (keeps the
server-chosen filename out of `Content-Disposition`, which a bare `Blob` discards) and `apiPostBlob` (the one
download that must send a body) — and **all ten** raw-`fetch` transfer sites moved onto them: `billing`,
`clinics` (logo), `doctors` (cachet), `export` (CSV), `invoices` (×3), `treatment-plans` (×2),
`medical-documents` (POST), `patient-files` (×4). `lib/api/` now contains **no raw `fetch` outside `client.ts`**.
**Justification:** the user was asked and chose it. `patient-files.ts` was the worst of the ten but not the only
one: `clinics.getLogo` threw « Failed to get clinic logo » and `medical-documents.generatePdfForDownload` threw
« Failed to generate PDF: … » — both English, both reaching a French UI verbatim through `lib/errors.ts`; and
`invoices.downloadPdf` surfaced a raw `{"error":"…"}` JSON string. None of the ten had a deadline or the 401
retry, and a download is the request *most* likely to be the first past a token's expiry. Leaving nine of ten is
the `fixes-dont-propagate` shape this repo has recorded twelve instances of.
**Impact:** every clinic-API call now goes through `client.ts`, so `onClientTooOld` and `onMustChangePassword`
fire for PDF and CSV downloads too — they previously could not. Three stale comments that asserted the opposite
were corrected (see the learning below). Nothing about P2–P5 changes.
**Approved:** Yes

### DEV-4: `UPLOAD_TIMEOUT_MS` renamed to `TRANSFER_TIMEOUT_MS`, and downloads use it
**Date:** 2026-08-07
**Story:** 1, part P1
**Category:** Technical
**Original plan:** unstated — the blueprint said "route the downloads through `client.ts`" and said nothing about
which deadline they should carry.
**Actual implementation:** the first cut gave the three new blob helpers `REQUEST_TIMEOUT_MS` (20 s). That is a
**defect**, caught before commit: a CBCT study (150 MB once P2 lands), an invoice PDF or a 3 000-row CSV export
cannot finish inside 20 s on a clinic's uplink, so it would have traded « hangs for ever » for « always fails »,
which is worse — the first is at least intermittent. The 180 s constant was renamed to `TRANSFER_TIMEOUT_MS`
with the reasoning written into its docstring, and the five file-transfer helpers share it.
**Justification:** one number, one name, one reason. An `UPLOAD_TIMEOUT_MS` used by downloads is a comment that
lies; a second constant with the same value is two authorities.
**Impact:** `apiGet`/`apiPost`/`apiPut`/`apiDelete` and the token exchange keep 20 s — unchanged.
**Approved:** trivial-by-classification (internal constant, no API or behaviour change to any caller), logged
here because it corrects a defect rather than a style point.

## Auto-approved deviations

| Deviation | Classification | Reason |
|---|---|---|
| `export.ts`'s `filenameFrom` moved into `client.ts` as `filenameFromDisposition` | Trivial | Both were private; the CSV exports are no longer the only download whose name the server owns, and a second parser would be a second answer to "what is this file called". `export.ts` keeps its own `'export.csv'` fallback, which is genuinely export-specific. |
| `export.ts`'s empty-string filter kept at the call site | Trivial | `buildUrl` skips `null`/`undefined` only; `fetchExportCsv` also dropped `''`, and silently widening `buildUrl` would change every other caller's query strings. |

## Learnings

- **The session's own git snapshot said "(clean)"; it was wrong.** `git status` at the start of the work showed
  33 dirty files. The `check-file-is-clean-before-staging` memory covers exactly this and it earned its keep
  again — the snapshot is taken once, at session start, and work arrives after it.
- **A correct refusal and a reported refusal are different features.** The txt→pdf 400 was the signature check
  working as designed; what made it read as a bug is that `patient-files.ts` read `errorData.message` while the
  backend sends `{ error }`, so the French explanation was replaced by an English `HTTP 400: Bad Request`. Worth
  remembering when a user reports "it fails with 400" — check the client's error path before the server's rule.
- **A comment asserting a limitation outlives the limitation, and then it argues for repeating the defect.**
  Three separate comments claimed the blob routes *could not* go through `client.ts`: `invoices.ts:43`
  (« the PDF/artifact routes can't go through `client.ts` »), `export.ts:3-5` (« `client.ts` keeps its base URL
  private, and every module that drops to raw `fetch` for a blob repeats this line … so this file adds no
  coupling the existing blob modules do not already have ») and `client.ts`'s own `onClientTooOld` docstring
  (« the dozen raw-`fetch` blob/upload sites deliberately keep their own response handling … and so do not
  notify »). Each was true when written and false by the time it was read; together they read as a settled
  design decision rather than as accumulated debt, and `export.ts`'s explicitly reasons *from* the duplication to
  justify more of it. All three were corrected in the same change. **When a helper gains a capability, grep for
  the comments that said it lacked one** — they are the strongest force keeping the next author on the old path.
- **The device gate's `api-headers` check passed throughout, and could not have caught any of this.** It fails on
  an `Authorization: … Bearer` literal outside `client.ts`, and all ten sites politely called `apiHeaders()`.
  A check on the *header* was blind to a duplicated *response* path. Worth knowing before trusting a green gate
  as coverage of a class it was never written for.

## Gate results

| Part | Backend build | Backend tests | `tsc` | `check:responsive` | `build` | Eye pass |
|---|---|---|---|---|---|---|
| P1 | n/a (web only) | n/a | ✓ 0 errors | ✓ 15/15 | **deferred** — see below | n/a (no rendering change) |

⚠️ **`npm run build` is deferred to the end of the story, by user decision.** The user's own Next server (PID
47448, `next dist/server/lib/start-server.js`) is serving from `web/.next`, which `next build` overwrites — so
running it would both fail confusingly and break the live app mid-serve. `distDir` is config-only, so there is no
build-elsewhere option without editing `next.config.ts`. `tsc` + `check:responsive` carried P1; the build is owed
before the story closes and matters most for P5's UI work.

⚠️ Also noted for P2: **`ClinicManagement.API.exe` (PID 55232) is running out of
`api/ClinicManagement.API/bin/Debug/net8.0`**, so `dotnet build` will fail with MSB3021/MSB3027 file locks. That
one has a clean workaround — `-p:BaseOutputPath=` to a scratch directory — and needs no change to the user's
running API.

## Session log

- **2026-08-07** — Prerequisite gap surfaced and resolved with the user (branch / scaffolding / logo column).
  Feature folder scaffolded. **P1 complete**, widened per DEV-3 to all ten blob-transfer sites; DEV-4 caught a
  20 s deadline that would have broken large downloads. `lib/api/` now has no raw `fetch` outside `client.ts`.

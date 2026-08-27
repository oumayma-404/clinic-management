# Progress: Lettre de liaison aux normes + envoi de tout document par email

**Started:** 2026-07-31
**Type:** Small
**Branch:** feature/audit-sections-3-to-10 (user chose to reuse the current branch, not a new one)

## Status
- [x] Implementation
- [x] Quality checks (backend build, frontend typecheck)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
Unrelated in-progress work present at session start — **excluded from this feature's commits**:
- `web/components/treatment-plans/plan-act-row.tsx`
- `web/components/ui/card-list.tsx`
- `web/scripts/check-responsive.mjs`
- `features/landing-website/` (untracked)

These belong to the `mobile-tablet-responsive` feature (P3 at 17/19 per the last commit).
⚠️ **Two files are touched by both features** and their diffs are now mixed:
`web/app/patients/[id]/page.tsx` (responsive only — this feature did not edit it in the end) and
**`web/components/treatment-plans/plan-workspace.tsx`** (responsive work **plus** this feature's email
actions). Committing the responsive work first is the only way to separate them.

## Environment notes
- The dev API is running (ports 5000/5001) and locks `api/**/bin/Debug`, so every build used
  `-p:BaseOutputPath=<scratch>/` per [[ef-migration-scaffolding-hazards]].
- `dotnet ef` worked (main working dir, `--configuration Release`); the generated `Up()` was checked against
  trap #3 and contains **only** this feature's operations, and EF emitted the `.Designer.cs` (trap #4).
- **ESLint is not installed** in `web/` (`npm run lint` → "'eslint' is not recognized"), and `next.config.ts`
  disables it during build. The FE gate used was `npx tsc --noEmit`.
- A `next build` was **not** run: it writes to the same `.next` the running dev server owns, which is exactly
  the stale-cache breakage repaired at the start of this session. `tsc --noEmit` is clean.

## Files Changed

### Part A — lettre de liaison (3 files)
- `api/…Infrastructure/Services/LiaisonContent.cs` — rewritten: free text is a first-class ordered section,
  the "only when no guided field" condition removed, 4 new norm sections.
- `api/…Infrastructure/Services/PdfGenerationService.cs` — « Médecin traitant / praticien adresseur » in the
  identity block; liaison comment updated.
- `web/components/document-editor-content.tsx` — 5 new fields, reordered editor (motif → free-text body →
  collapsed « Sections complémentaires »), `liaisonSections()` mirrors the server order, preview + save +
  hydrate + reset paths, recipient email field, « Envoyer par email » button + dialog mount.

### Part B — document email (backend, 20 files)
- Domain: `Entities/DocumentEmail.cs` (new), `Enums/DocumentEmailStatus.cs` (new),
  `Repositories/IDocumentEmailRepository.cs` (new), `Entities/ClinicReminderSettings.cs` (+7 SMTP fields,
  `ApplySmtpSettings`, `SetSmtpPasswordEncrypted`).
- Infrastructure: `Persistence/Configurations/DocumentEmailConfiguration.cs` (new),
  `Repositories/DocumentEmailRepository.cs` (new), `Services/IDocumentEmailSender.cs` (new),
  `Services/SmtpDocumentEmailSender.cs` (new), `Services/SmtpConfig.cs` (new),
  `Services/ReminderSettingsProvider.cs`, `Persistence/Configurations/ClinicReminderSettingsConfiguration.cs`,
  `Persistence/ApplicationDbContext.cs` (DbSet + query filter), `Extensions.cs` (DI ×2).
- Application: `Features/DocumentEmails/DocumentEmailAttachment.cs` (new),
  `Features/DocumentEmails/Commands/QueueDocumentEmailCommand.cs` (new),
  `Features/DocumentEmails/Queries/GetDocumentEmailsQuery.cs` (new),
  `Features/DocumentEmails/DocumentEmailMappingExtensions.cs` (new), `DTOs/DocumentEmailDto.cs` (new),
  `Features/Documents/MedicalDocumentPdfMapping.cs` (new — extracted, see DEV-2),
  `DTOs/ReminderSettingsDto.cs`, `DTOs/ReminderSettingsMappings.cs`,
  `Features/Clinics/Commands/UpdateClinicReminderSettingsCommand.cs`,
  `Features/Clinics/Queries/GetClinicReminderSettingsQuery.cs`, `Extensions.cs` (DI).
- API: `Controllers/DocumentEmailsController.cs` (new), `BackgroundJobs/DocumentEmailJob.cs` (new),
  `BackgroundJobs/PdfGenerationJob.cs` (now uses the extracted mapping), `Program.cs` (recurring job).
- Migration: `20260731113345_AddDocumentEmailsAndSmtpSettings{.cs,.Designer.cs}` +
  `ApplicationDbContextModelSnapshot.cs`.

### Part B — document email (frontend, 6 files)
- `web/lib/api/document-emails.ts` (new), `web/components/send-document-email-dialog.tsx` (new),
  `web/lib/realtime/clinic-hub.ts` (`documentemails` key), `web/lib/api/reminder-settings.ts` (SMTP fields),
  `web/components/reminder-settings.tsx` (SMTP section).
- Surfaces (6 kinds across 4 files): `factures/invoices-table.tsx` (facture),
  `factures/invoice-detail-modal.tsx` (avoir + reçu de paiement),
  `treatment-plans/plan-workspace.tsx` (devis + reçu d'échéance),
  `document-editor-content.tsx` (ordonnance / liaison / certificat / bulletin CNAM).

**Total: ~35 files.** Far past the small-feature envelope; the user explicitly chose to keep both halves in one
pass with nothing deferred, and reaffirmed it when the size was surfaced again at implementation time.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Liaison PDF heading « Motif » → « Motif de la liaison » | Matches the mockup the user approved in the spec interview, and the norms name it that way. Cosmetic; affects existing letters' heading only. |
| `System.Net.Mail.SmtpClient` instead of MailKit | Zero new dependency. STARTTLS + auth + one PDF attachment is all this needs, and the offline installer would otherwise have to carry a package that buys nothing. Not `[Obsolete]` in .NET 8 — no warning. |
| SMTP batch/retry read from `Notification:Smtp:DispatchBatchSize`/`MaxAttempts` with in-code defaults | Mirrors `RemindersConfig`/`TtnConfig`'s bounded-batch pattern rather than adding a third config class for two integers. |
| `DocumentEmailDto` omits the attachment storage key | A key on the wire is a handle to a stored PHI blob; the UI needs recipient/moment/outcome only. |

## Significant Deviations

### DEV-1 — Attachment rendered at queue time into file storage (approved)
**Spec said:** AC-9, the dispatcher job "re-renders the PDF at send time"; AC-7, the row holds no attachment bytes.
**Problem found:** all five money-document PDF queries (`GetInvoicePdfQuery`, `GetCreditNotePdfQuery`,
`GetDevisPdfQuery`, `GetPaymentReceiptPdfQuery`, `GetInstallmentReceiptPdfQuery`) resolve the clinic from the
caller's JWT via `ICurrentClinicResolver`. A Hangfire job has no `HttpContext`, so it **cannot** call them —
AC-7 and AC-9 could not both hold while reusing the existing renderers.
**Implemented:** the queue command renders through the document's own query (tenant check, French filenames and
business refusals all reused unchanged), stores the PDF via the existing `IFileStorage` seam, and the row keeps
only `AttachmentStorageKey`. The job downloads, sends, then deletes the blob. AC-7 still holds literally (no
bytes in the DB); AC-9 becomes "rendered at queue time".
**Why it is better:** an unrenderable document is refused **at the click** with the renderer's own French
message, instead of failing in a job a minute after a success toast; and the emailed PDF is what the
practitioner was looking at. Cost: a document edited between queueing and sending emails the queued version.
**Approved:** Yes (user picked this option over refactoring the six queries to take an explicit `clinicId`).

### DEV-2 — `MedicalDocumentPdfMapping` extracted from `PdfGenerationJob`
**Why:** a medical document has no by-id PDF query (the download endpoint takes a body), so rendering one for an
email needed the `MedicalDocumentDto` → `MedicalDocumentPdfData` flattening that lived inline in
`PdfGenerationJob`. Copying it would have created a second answer to "what does this ordonnance look like",
which is the § 5.10 defect class this repo keeps closing. Extracted to Application and **both** callers now use
it; `PdfGenerationJob` lost ~25 lines and two now-unused usings.
**External scope:** yes (touches an existing job), hence logged rather than auto-approved. No behavior change —
the mapping is byte-for-byte the same, plus a `JsonException` guard so an unreadable `ContentJson` renders the
header/identity/signature instead of throwing.

### DEV-3 — `DocumentEmail` is its own table, not rows on `Notification`
**Spec said:** "a row in the existing Notification outbox (NotificationType.Email already exists)".
**Implemented:** a separate `DocumentEmail` aggregate + `DocumentEmailJob`.
**Why:** `Notification` rows carry appointment/recall semantics its dispatcher branches on (re-checking the
appointment is still active, `ClearRecallSnooze` on terminal failure) and a reminder-retention purge, none of
which apply to a document — teaching one dispatcher two meanings makes both subtly wrong. The *pattern*
(connectivity gate, per-row commit, bounded retry, batch cap) and the settings/secret infrastructure are reused.
**Flagged in the spec before implementation**, and the user approved the spec containing it.

### DEV-4 — No `SmtpEnabled` toggle
The two message channels have tri-state on/off/inherit toggles. Email deliberately has none: a host plus a
from-address **are** the enable, and a channel switched "on" with no server configured would be a promise the
dispatcher cannot keep. `EmailConfigured` is the single sendability authority (and it does **not** require
credentials — an unauthenticated LAN relay is a real deployment).

## Observed but NOT fixed (out of this spec's scope)
`document-editor-content.tsx` seeds its document date with `new Date().toISOString().split("T")[0]` in two
places (the initial state and `resetForm`). That is the exact anti-pattern `web/CLAUDE.md` forbids — for the
first hour of every Tunisian day it pre-fills **yesterday**, and on the 1st the previous month. The repo's
`todayLocalIso()` is the fix. Left alone because it changes behaviour and belongs with the other date-default
work, not with a liaison/email change. Worth a `/quick-fix`.

## What /test-small-feature must cover first
- **`RealtimeResourceResolverTests`** — it reflects over every `IRequest` and parses `clinic-hub.ts`, asserting
  the two key sets are equal in both directions. `Features/DocumentEmails/Commands` now emits
  `documentemails` and the key was added to `clinic-hub.ts`; if the derivation differs from my reading, that
  test is what fails, and it fails the build.
- `verify-schema` picks up the new table, its two indexes and the FK **for free** (it diffs the EF model
  against PostgreSQL's catalog), so no expectation list needed editing — but it should be run against the
  migrated DB.
- Unit scope worth having: `LiaisonContent` ordering + free-text-with-guided-fields + legacy round-trip;
  `DocumentEmail.NormalizeKind` / retry-vs-terminal transitions; `QueueDocumentEmailCommand`'s
  not-configured refusal and its orphan-blob cleanup; `DocumentEmailJob`'s offline no-op (consumes no attempt),
  missing-blob terminal failure, and blob release on success.

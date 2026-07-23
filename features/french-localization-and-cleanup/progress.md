# Progress: French Localization, Branding & Dead-Code Cleanup

**Started:** 2026-07-23
**Type:** Small
**Branch:** feature/french-localization-and-cleanup (git worktree at `.claude/worktrees/french-localization-and-cleanup`)
**Base branch:** feature/windows-desktop-app (== current HEAD a27ba23)

## Status
- [x] Implementation
- [x] Quality checks (backend build 0/0; frontend `npx tsc --noEmit` 0 + `npm run build` 0)
- [ ] Tests (handled by /test-small-feature)

## Quality gate results (final)
- **Backend** `dotnet build ClinicManagement.sln`: **0 errors / 0 new warnings** (the ~56 solution-wide warnings are the pre-existing CS8618/CS8981/CS8602/CS0618 baseline — none in changed files).
- **Frontend** `npx tsc --noEmit`: **0 errors**. `npm run build`: **0 errors**, all 26 routes compiled.
- **Frontend lint**: `npm run lint` is NOT runnable — the repo has an ESLint config + `lint` script but no installed `eslint` binary (`next build` runs with `eslint.ignoreDuringBuilds`). Per the skill this is the documented case; the real FE gate is `tsc --noEmit` + `next build`, both green. No new unused imports were introduced (the only removed imports — `date-fns` `format`/`parseISO` in `patients/[id]` and `patient-summary-modal` — had no other consumers).
- Docs updated: root `CLAUDE.md`, `api/.../Domain/CLAUDE.md`, `api/.../API/CLAUDE.md` (billing/CNAM/treatment-plans/e-invoicing documented; active jobs = `NotificationJob` + `EInvoiceOutboxJob`; domain-events + calendar-job removals noted).

## Additional files localized (found via AC-1 "no English on any screen" sweep, beyond the initial list)
patient-summary-modal, procedure-types-table, procedure-type-form-modal, stock-table, stock-item-form-modal, user-management, change-password-form, clinic-guard, unauthorized-page, users page, files page, patient-files-manager, patients/[id]/files page, appointment-list, join page, clinic-settings, ui/dialog (sr-only), + loading-hook/BFF-route user-facing fallbacks. Deep billing/CNAM screens (document-editor, join-wizard, reminder-settings, cnam/medications/dental-acts admin pages) were already French.

## Working tree note (start of session)
- Worktree created from `feature/windows-desktop-app` (same commit as the current `features/cloud-security-and-tenant-isolation` branch). Main repo working dir stays on `features/cloud-security-and-tenant-isolation`; the new branch is NOT checked out there (per request).
- The three untracked `features/*` spec folders were NOT copied into the worktree except this feature's own `features/french-localization-and-cleanup/` (spec + this progress file), which was copied in so tracking lives alongside the spec.
- Scope note: this is a Type: Small, Scope: Full coherence pass. It touches many files (~15-18) but each change is mechanical (string translation, formatting, deletion). Proceeding without escalation per explicit `/implement-small-feature` invocation.

## Files Changed
**Backend (dead-code removal, problem D):**
- `api/.../Domain/Common/AggregateRoot.cs` — dropped `_domainEvents`/`DomainEvents`/`AddDomainEvent`/`ClearDomainEvents`
- `api/.../Domain/Common/IDomainEvent.cs` — DELETED
- `api/.../Domain/Events/*` — DELETED (4 event classes + folder)
- `api/.../Domain/Entities/Appointment.cs` — removed Events using + 3 raise calls (dropped now-unused `oldDateTime`)
- `api/.../Domain/Entities/Patient.cs` — removed Events using + 1 raise call
- `api/.../Domain/ClinicManagement.Domain.csproj` — removed now-unused `MediatR.Contracts` ref (DEV: auto-approved)
- `api/.../API/BackgroundJobs/GoogleCalendarSyncJob.cs` — DELETED
- `api/.../API/Program.cs` — removed commented `sync-google-calendar` AddOrUpdate; kept the defensive `RemoveIfExists`
- Backend build: **0 errors / 0 new warnings** (verified).

**Frontend (localization + formatting + branding + UX):**
- `web/lib/brand.ts` — NEW: `PRODUCT_NAME = "Gestion Clinique"`
- `web/lib/format.ts` — added `formatDate` (dd/MM/yyyy) + `formatDateTime` (dd/MM/yyyy HH:mm), fallback "Non renseigné"
- `web/app/layout.tsx` — title from PRODUCT_NAME (FR), removed `generator: "v0.app"`, FR description
- `web/components/dashboard-sidebar.tsx` — brand from `clinic.name` ?? PRODUCT_NAME; FR aria-labels
- `web/app/login/page.tsx` — full FR, branding, reworded CTA (join vs. create)
- `web/app/page.tsx` — dashboard FR + KPI grid reflow (cols-2/3/4)
- `web/app/patients/page.tsx`, `web/app/stock/page.tsx`, `web/app/appointments/page.tsx` — FR
- `web/components/dashboard-header.tsx` — account menu FR
- `web/app/records/page.tsx` — FR + fr-FR DOB search + shared formatDate
- `web/app/patients/[id]/page.tsx` — FR (whole page) + formatting via shared helper + "Non renseigné"
- `web/components/edit-patient-dialog.tsx` — FR validation + labels/headers/options/instructional placeholders
- `web/components/create-appointment-dialog.tsx` — FR validation + labels/placeholders + dd/MM/yyyy date button
- `web/components/edit-appointment-dialog.tsx` — FR validation + labels + status options + cancel dialog
- `web/components/setup-wizard.tsx`, `web/components/clinic-settings.tsx` — clinic@example.com → obvious FR placeholder (+ nearby label)
- (in progress) patients-table, files page, procedure-types-table, clinic-settings, patient-summary-modal, patient-files-manager, user-management, change-password-form, clinic-guard, users page

## Scope note
This Type: Small / Scope: Full coherence pass spans ~22 files — beyond the ~10-file small envelope — but every change is mechanical (string translation, locale formatting, dead-code deletion, docs). Proceeding as the explicit `/implement-small-feature` request directed ("start implementing"); no architectural decisions involved. Frontend typecheck (`npx tsc --noEmit`) is green after each major chunk.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Removed `MediatR.Contracts` PackageReference from `ClinicManagement.Domain.csproj` | It existed solely for `IDomainEvent : INotification`; deleting the domain-events subsystem (spec problem D) makes it dead weight. Internal build-config change, no behavior/API change. Build stays 0/0. |
| Extended existing shared `web/lib/format.ts` (rather than a new file) with `formatDate`/`formatDateTime` | Spec asked for "a single shared date/number formatting helper"; the file already existed (`formatDT`/`formatDateFr`). Reused it. |
| Product brand `"Gestion Clinique"` placed in new `web/lib/brand.ts` constant | Spec left the product name unpinned (asked user → "Gestion Clinique"). Constant avoids re-hardcoding across layout/login/sidebar. |
| Translated appointment-status dropdown option labels + gender option labels (display only; `value=` unchanged) | Visible form-control text under AC-1; stored/enum values untouched (no behavior change). |
| Also localized files-page / patients-table / shared components not named in the spec's problem-A list | AC-1 ("no English on any screen") + spec overview ("surfaces a dentist touches daily") govern; the spec file list is non-exhaustive. |

## Known residual (noted, not a defect)
- Raw backend enum VALUES displayed as data (e.g. appointment `status` badge on patient detail, stored `gender` value) remain English where shown as data rather than through a translated control. Translating backend enum values app-wide is out of scope per the spec ("backend business-message translation ... out of scope"). Dropdowns/controls that render these were localized.

## Significant Deviations
(none — no public API / behavior / schema changes; branding product name confirmed via AskUserQuestion)

# Progress: Reminder Reliability & Product Polish

**Started:** 2026-07-22
**Type:** Small (forced full-slice, one pass — see DEV-0)
**Branch:** feature/reliability-and-polish (worktree off feature/windows-desktop-app)

## Status
- [x] Implementation (all 10 ACs)
- [x] Quality checks — backend `dotnet build` 0 errors/0 new warnings; FE `tsc --noEmit` clean; FE `npm run build` OK
- [ ] Tests (handled by /test-small-feature)

## Quality gate results
- Backend: `dotnet build ClinicManagement.sln` → **0 errors, 56 warnings (all pre-existing baseline: CS8618/CS8602/CS8604/CS8600/CS8981/CS0618)**; scoped filter shows no new warning in changed files.
- Frontend gate (no ESLint/test runner in repo — LEARNINGS): `npx tsc --noEmit` clean + `npm run build` succeeded.
- EF migrations generated with the tool (`--no-build`, reusing freshly-built DLLs) — WDAC did **not** block it this run. Snapshot auto-updated + compiles.

## Working tree note (start of session)
- Worktree created off `feature/windows-desktop-app` @ a512b80.
- The untracked spec dir `features/reliability-and-polish/` was copied into the worktree (was not committed on HEAD).
- Deps restored: `dotnet restore` (ok), `npm install` (running/ok).

## Files Changed
### Area 2 — Reminders (AC-1/2/3) ✅
- Domain: `ClinicReminderSettings.cs` (+SmsApiUrl/WhatsAppApiUrl/LeadTimeHours(CSV)/MessageTemplateBody, extended `ApplyNonSecretSettings`, Parse/FormatLeadTimeHours)
- Infra: `ClinicReminderSettingsConfiguration.cs` (4 cols), `ReminderSettingsProvider.cs` (per-clinic ?? per-install for URLs/lead-times/body), `HttpSmsSender.cs`/`WhatsAppSender.cs` (use shared Configured props), `ReminderScheduler.cs` (resolved lead-times + template), `NotificationRepository.cs` (+GetRecentByClinicIdAsync)
- App: `ResolvedReminderSettings.cs` (+LeadTimeHours/MessageTemplateBody + [MemberNotNullWhen] SmsConfigured/WhatsAppConfigured), `ReminderSettingsDto.cs` (+URLs/lead/body/effectiveStatus + ReminderEffectiveStatus consts), `ReminderStatusDto.cs` (new), `ReminderSettingsMappings.cs`, `GetClinicReminderSettingsQuery.cs` (+provider→effectiveStatus), `UpdateClinicReminderSettingsCommand.cs` (+new fields+effectiveStatus), `GetClinicReminderStatusQuery.cs` (new), `INotificationRepository.cs`
- API: `ClinicsController.cs` (+GET reminder-status)
- Migration: `20260722182751_AddPerClinicReminderOverrides` (EF tool, 4 additive nullable cols) + snapshot
- Web: `lib/api/reminder-settings.ts` (types + status()), `components/reminder-settings.tsx` (URL/lead/body fields, readiness badges, Connecté downgrade, delivery-status surface)
- Tests fixed (build-required): `ClinicReminderSettingsTests`, `ReminderSettingsProviderTests`, `GetClinicReminderSettingsQueryHandlerTests`, `UpdateClinicReminderSettingsCommandHandlerTests`

### Area 3 — Phones (AC-4/5) ✅
- Domain: `PhoneNumber.cs` (static `ToE164`/`IsDeliverable` — the single Tunisian-phone rule)
- Infra: `ReminderPhone.cs` (`ToE164` delegates to `PhoneNumber.ToE164`)
- App: `CreatePatientCommand.cs` + `UpdatePatientCommand.cs` (reject non-deliverable phone at entry, French message)
- Web: `lib/phone.ts` (new — FE mirror), `create-appointment-dialog.tsx` (inline new-patient captures + validates a phone; passes `phoneNumber`), `edit-patient-dialog.tsx` (unified validation, removed old regex validator)

### Area 4 — Header search + working hours (AC-6/7) ✅
- Domain: `Clinic.cs` (+`WorkingHoursJson` + `SetWorkingHours`)
- Infra: `ClinicConfiguration.cs` (+col), Migration `20260722185719_AddClinicWorkingHours` (EF tool) + snapshot
- App: `WorkingHoursDto.cs` (new — `WorkingDayDto` + `WorkingHoursSerializer`), `ClinicDto.cs` (+`WorkingHours`), `UpdateClinicCommand.cs` (validate+store+project), `GetUserStatusQuery.cs` (project)
- API: `Models/UpdateClinicRequest.cs` (+`WorkingHoursJson` form field), `ClinicsController.cs` (pass-through)
- Web: `lib/working-hours.ts` (new — shared default + summary), `lib/api/clinics.ts` (+workingHours/workingHoursJson), `clinic-settings.tsx` (load + real save), `dashboard-sidebar.tsx` (footer from saved hours — single source; nav labels FR), `dashboard-header.tsx` (live patient search → navigate)

### Area 5 — French localization + alert/confirm (AC-8/9) ✅
- `app/layout.tsx` (`<html lang="fr">`)
- `components/ai-chat.tsx` (greeting/header/placeholders/toasts/titles FR; speech recognition + synthesis `lang="fr-FR"`)
- `components/setup-wizard.tsx` + `components/join-wizard.tsx` (full chrome FR; day-name display map; specialty/role option **values** kept — see deviation)
- alert()→toast: `app/appointments/page.tsx` (Google-sync, +offline `ApiError(0)` branch), `components/procedure-types-table.tsx`
- confirm()→`AlertDialog`: `components/patient-files-manager.tsx`, `app/files/page.tsx`

### Area 6 — Dead code + docs (AC-10) ✅
- Deleted (backend, zero live consumers verified): `IGoogleAIService.cs`, `GoogleAIService.cs`, `IPatientSummaryService.cs`, `PatientSummaryService.cs`, `AISummaryJob.cs`, `INotificationService.cs`, `NotificationService.cs`; removed their DI regs (`Extensions.cs`) + the commented job block (`Program.cs`)
- Deleted (frontend): `notifications-list.tsx` (orphan), `dental-chart.tsx` (redundant 4th chart)
- `patient-summary-modal.tsx` migrated off `dental-chart` → reuses `record-tooth-chart` (read-only worked-teeth map)
- `patients/[id]/page.tsx` — removed the duplicate static "Patient Summary" button + its modal mount/state/import (kept the real AI summary)
- Deleted `GOOGLE_AI_SETUP.md`; refreshed root `CLAUDE.md` + Domain/Application/Infrastructure/API/web-components sub-guides

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `LeadTimeHours` stored as CSV `text` column (not `int[]`) | DTO contract stays `int[]` as pinned; internal storage choice, simpler/safe migration. Entity Parse/Format helpers own the conversion. |
| Refactored SMS/WhatsApp senders to read shared `ResolvedReminderSettings.SmsConfigured`/`WhatsAppConfigured` | Same behavior; single source of truth for "sendable" (also feeds effectiveStatus). `[MemberNotNullWhen]` keeps 0 new nullable warnings without suppression. |
| Extended `ApplyNonSecretSettings` signature (+4 params) → fixed 4 test call sites + 2 handler-test ctors (mock `IReminderSettingsProvider`) | Build-required compile fix; original assertions preserved. New effectiveStatus/override scenarios deferred to /test-small-feature. |
| Moved the Tunisian-phone `ToE164` rule into Domain `PhoneNumber`; `ReminderPhone.ToE164` now delegates | Same behavior (`ReminderPhoneTests` still green); one rule shared by patient-entry validation + reminder dispatch. |
| `Clinic.WorkingHours` stored as a JSON `text` column (`WorkingHoursJson`) | Spec allowed "JSON column or a small owned type"; DTO exposes the structured `WorkingHours` list. Single additive migration. |
| `patient-summary-modal` migrated to `record-tooth-chart` with a fixed blue "worked" fill + procedure count | Spec-mandated consolidation onto the real record chart; per-procedure detail remains in the table below. Visual differs slightly from the removed `dental-chart` (calibration note — verify in-app). |
| Setup/join wizard **specialty** and **role** option *values* kept as-is (labels/display translated) | These strings double as stored/back-end values across multiple files; translating them is translation-infrastructure work beyond AC-8's "targeted string cleanup". Visible labels are French; role labels translated (values are lowercase keys). |

## Significant Deviations
- **DEV-0:** Spec is `Status: DRAFT` and `Scope: Full` (~40 files → 65 files, 2 migrations) but labeled `Type: Small`. User explicitly forced the small pipeline and chose "Full slice, one pass" via AskUserQuestion. Implemented all 10 ACs; treated spec as approved for this pass.
- **DEV-1 (removal safety, per spec edge case):** `NotificationService`/`INotificationService`, `PatientSummaryService`/`IPatientSummaryService`+`AISummaryJob`, and `GoogleAIService`/`IGoogleAIService` were removed only after a repo-wide grep (incl. `UnitTests`) confirmed **zero injection/callsites** — `NotificationService` had only its DI registration; `AISummaryJob` was commented-out; `GoogleAIService` was never registered. Live SMS/WhatsApp reminders + the HuggingFace patient AI summary are untouched.

## Deferred to /test-small-feature
- Reminder settings: per-clinic URL/lead-time/message-body override resolution (per-clinic ?? per-install); `effectiveStatus` = configured/not_configured; the "Connecté→warning" downgrade; the `GET /api/clinics/reminder-status` masking/mapping (sent/pending/failed).
- Phones: `PhoneNumber.ToE164`/`IsDeliverable` unit cases; `CreatePatientCommand`/`UpdatePatientCommand` reject a bad phone (and accept a valid TN one); inline appointment path captures phone.
- Working hours: `WorkingHoursSerializer.Parse/Normalize` (valid/blank/invalid); `UpdateClinicCommand` persist + reject invalid JSON; `GetUserStatusQuery`/`ClinicDto` projection.
- (FE behavior — header search, French strings, AlertDialog delete flows — no FE test runner in repo; manual verification.)

# Progress: Calendar import creates reviewable patients

**Started:** 2026-08-31
**Type:** Small
**Branch:** feature/security-remediation (pre-existing, shared with another session — see the note below)

## Status
- [x] Implementation
- [x] Quality checks (build, tsc, check:responsive)
- [x] End-to-end browser verification (full pass — see below)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
The branch is shared with **another active session** working on `AuditChain` (`AuditChain.cs`,
`SealAuditChainCommand.cs`, `AuditChainSealStore.cs`) plus a large pre-existing set of untracked
`landing-v2/**` and `follow-up/**` files. **Nothing was committed by this skill** and every file below was
touched deliberately — stage by path, never `git add -A`.

Note also that earlier in this session that other session committed the whole dirty tree in five
`fix(sécurité)` / `feat(conformité)` commits, sweeping two unrelated rename lines of mine into
`5546f572`. Watch for the same when committing this feature.

## Files Changed

**Domain (5)**
- `Domain/Enums/NotificationCategory.cs` — `PatientImportedNeedsReview = 17`
- `Domain/Enums/NotificationTargetKind.cs` — `Patient = 8`
- `Domain/Entities/StaffNotification.cs` — `PatientId` + `ForPatientImportReview`
- `Domain/Entities/Patient.cs` — `CalendarImportPendingReviewSince`, `MarkImportedFromCalendar`,
  `ConfirmCalendarImport`, and the clear inside `UpdatePersonalInfo`
- `Domain/Entities/Clinic.cs` — `GoogleCalendarHoldsOnlyAppointments` + setter + reset on disconnect

**Application (9)**
- `Common/Services/StaffNotificationRules.cs` — classified `false` (never a lock-screen push)
- `Common/Interfaces/INotificationGenerator.cs` + `Common/Services/NotificationGenerator.cs`
- `DTOs/NotificationDto.cs`, `DTOs/PatientDto.cs`, `Features/Patients/PatientMappingExtensions.cs`
- `Features/Patients/Queries/GetPatientsQuery.cs` — `PendingCalendarReviewOnly`
- `Features/Patients/Commands/ConfirmCalendarImportCommand.cs` *(new)*
- `Features/Clinics/Commands/SetGoogleCalendarImportSettingsCommand.cs` *(new)*

**Infrastructure (7)**
- `Services/GoogleCalendarSyncService.cs` — the gate, `LooksLikeAPersonName`, `MatchesName`, null birth date,
  the review stamp, the post-commit notification
- `Services/PushNotificationGeneratorDecorator.cs` — pass-through
- `Repositories/PatientRepository.cs` + `Domain/Repositories/IPatientRepository.cs` — the SQL filter
- `Persistence/Configurations/{Patient,StaffNotification,Clinic}Configuration.cs`
- `Migrations/20260831202247_AddCalendarImportReview.cs` *(generated)*

**API (2)** — `Controllers/PatientsController.cs`, `Controllers/GoogleCalendarController.cs`

**Frontend (8)** — `lib/api/types.ts`, `lib/api/patients.ts`, `lib/api/google-calendar.ts`,
`components/notification-panel.tsx`, `components/dashboard-header.tsx`, `components/patients-table.tsx`,
`app/patients/page.tsx`, `app/patients/[id]/page.tsx`, `components/appointment-calendar.tsx`,
`app/appointments/page.tsx`

**Test infrastructure (3, compile-required)** — `RecallQueryBoundsTests.cs`, `CreditNoteReadTests.cs`,
`PatientContactOptionalTests.cs`

## Quality checks
- `dotnet build ClinicManagement.sln -c Release` — **0 errors**, 13 warnings, all pre-existing (CS8618 on the
  EF private ctors of `Patient`/`Clinic`, CS8602 in `PatientsController`; line numbers shifted by my inserts).
- `npx tsc --noEmit` — clean. `npm run check:responsive` — **23/23**.
- `dotnet run -- verify-schema` — 4 drifts, **all pre-existing and unrelated** (`audit-chain-intact` is the
  other session's live work; `overlapping-appointment-pairs`, `messaging-month-covers-every-clinic` and
  `key-ring-protection` are dev data/env). No drift on Patients/Clinics/StaffNotifications.
- ⚠️ `npm run build` **not run**: a live `next dev` holds `.next` and would disrupt the running session.

## Device pass
**Actually viewed** (screenshot opened and read), touch emulation on so `pointer: coarse` is true:
banner at **320 · 390 · 820 · 1440**; `/patients` filtered state at **820**; bell panel at 1440; and from the
earlier work in this session, the action row at 320/820/1180 and the tab strip at 390/820.
No document or `<main>` horizontal scroll at any width; both banner actions **44 px** at all four.

**One defect the numbers passed and the eye caught.** At 820 px the banner's action group sat beside the text
from `sm:` onward, leaving the description a ~250 px column wrapping to **five lines** on a tablet — the
measurements (44 px targets, no overflow) were all green. Hinge moved `sm:` → `lg:`, so the two actions take
their own row until there is genuinely room for both; re-viewed at 820 and 1440 after the change.
This is the second time in one session that a green mechanical gate hid a layout defect — see
`verification-proportional-to-fix-size` in memory.

## End-to-end verification (browser, real API + DB)
The Google→App import's *output* was staged in SQL exactly as the new code writes it (patient with
`DateOfBirth` NULL, `Gender` `Unknown`, stamp set; notification category 17 / target kind 8), then every
downstream surface was driven for real:

| AC | Result |
|----|--------|
| AC-7 | Fiche renders with **no age** and « Aucun téléphone » — no fabricated birth date |
| AC-8 | Banner renders with the right copy; « C'est correct » clears it + toast, stamp NULL in DB |
| AC-9 | Bell row renders (UserPlus, teal chip, unread dot); click → `/patients/<id>` |
| AC-12 | Saved a real birth date via « Compléter la fiche » → banner gone, « 38 ans », stamp NULL in DB |
| AC-13 | Chip: 13 rows → 1; `?pendingCalendarReview=1` written **and honoured on reload** |
| AC-15 | 44 px at 320/390/820/1440 on a coarse pointer, no horizontal scroll |
| opt-in | `PUT /import-settings` with no Google connection → French refusal; `status` exposes the flag |

⚠️ **Not verifiable here: the import TRIGGER itself.** `SyncFromGoogleCalendarAsync` needs a real OAuth'd
Google calendar, which cannot be created in this environment. The gate (`IsClinicAppointment`),
`LooksLikeAPersonName`, `MatchesName` and the ambiguous-match refusal are therefore **unexercised at
runtime** — they are the priority for `/test-small-feature`.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| `DateTime.UtcNow` for the review stamp, not `ClinicClock` (spec AC-7 said `ClinicClock`) | `ClinicClock` has no "now as UTC" accessor — it owns clinic-*local dates* and day boundaries. Every `CreatedAt` in the codebase uses `DateTime.UtcNow`; the spec line was over-strict and would not have compiled against any existing helper. |
| Opt-in refusal returns **400**, not the spec's 409 | `HandleFailure(result)`'s default in this repo is 400, and the repo's convention wins. Body is the canonical `{ error }` with the French sentence. |
| `ExtractPatientNameFromSummary`'s fallback now calls `LooksLikeAPersonName` | It carried its own copy of the word-count test with a weaker blocklist, so the gate could admit an event this then refused to name — the `fixes-dont-propagate` shape. |
| Moq `It.IsAny<bool>()` inserted at 4 call sites in 3 test files | Build-required: the new repository parameter broke positional callers. Assertions preserved verbatim. |
| Banner action-group hinge `sm:` → `lg:` | Eye-pass finding at 820 px — see the Device pass section. |
| Filtered-empty branch added to `patients-table.tsx` | Found in the browser pass: the new chip rendered « Aucun patient enregistré » + an « Ajouter » button, i.e. the first-run invite on a filtered list — the exact confusion `ui/empty-state.tsx` forbids. Now « Aucune fiche à compléter » with « Effacer les filtres ». |

## Significant Deviations
**DEV-1 — Full scope kept under `Type: Small` (user decision).** The blueprint was ~25 files, above the
skill's « one coherent change, < ~10 files » gate. I surfaced this and offered a trimmed scope or
`/define-feature`; the user chose full scope as one small-feature pass. Recorded because the stated cost is
real: there are **no story boundaries**, so `NotificationCategory.PatientImportedNeedsReview` and its
`ReachesALockedPhone` classification must land together — that method *throws* on an unclassified category,
so shipping the enum alone would break **every** notification in the app, not just the new one.

**DEV-2 — Action-row button alignment fixed on two pages (user request, mid-implementation).** `ExportButton`
carries `coarse:h-11` (painting 44 px) while `buttonVariants` deliberately keeps every size « exactly as
drawn » and relies on the `.touch-target` overlay (documented as AC-10). On a coarse pointer that left
`/patients` with a 44 / 32 / 36 row and the patient page with 44 beside four 32s. Fixed **at those two rows**
by giving the siblings `coarse:h-11`.
⚠️ **The other 9 `<ExportButton>` call sites still have the mismatch** (`caisse` ×2, `factures`, `lab-orders`,
`treatment-plans`, `appointment-calendar`, `cheques-table`, `receivables-table`, `stock-table`). The one-home
fix is a `coarse:` min-height on `buttonVariants`' `sm`/`default` sizes (excluding `icon*`, so the 22 table
row-action grids do not reflow) — that contradicts a documented primitive decision, so it was **not** taken
unilaterally. Worth a follow-up.

## Deferred to /test-small-feature
Highest value first, because none of these ran at runtime here:
1. Gate off → event selection byte-identical to today; gate on → « Ahmed Ben Ali » imports.
2. `LooksLikeAPersonName`: « Réunion CNAM », one word, 5+ words, « Déjeuner Sarah » all refused.
3. `MatchesName`: exact full name, first+last split; « Ali » does **not** match « Ali Ben Salah ».
4. Two patients of the same name → skipped, nothing created.
5. Created patient has `DateOfBirth == null` and a non-null stamp; one-word title creates nothing.
6. Notification written once, post-commit, only for a created patient; a throwing generator loses nothing.
7. `Patient.UpdatePersonalInfo` clears the stamp; `ClearGoogleCalendarConnection` resets the opt-in.
8. `ReachesALockedPhone` classifies the new category (the throw is the guard).

# Story 2: Full - A save is refused kindly, and the visitor always knows it is a demo

**Status:** APPROVED
**Story Status:** done
**Layer:** Full (vertical slice — see README)
**Depends On:** 1
**Blocks:** 3

## Objective

Every write a visitor attempts is refused by the server with the demo sentence, shown inside the dialog that made it
(neutral tone, « Créer mon cabinet » link, typed input kept) or as one toast when no dialog is open; GET exports are
refused too; nothing writes an idempotency claim. A permanent band on every screen says it is a demo.

## Acceptance Criteria

_From spec:_

- [ ] AC-3.1 one message + « Créer mon cabinet » (new tab) for every save / create / edit / delete / payment / settings change
- [ ] AC-3.2 dialog stays open with input
- [ ] AC-3.3 never looks like an error; AC-3.3b inside a dialog the notice is in the dialog; AC-3.3d one message at a time
- [ ] AC-3.4 opening forms allowed; AC-3.5 nothing a visitor does changes another's view
- [ ] AC-4.1–4.4 band text, short form at phone width, not closable, not printed
- [ ] FR-2 per-action classification, refusal before any write; FR-5 band

_Story-specific:_

- [ ] Gate sits after `LocalAuthEnforcementMiddleware`, before `IdempotencyMiddleware` (pinned by an `IndexOf` test)
- [ ] `DemoExemptionCoverageTests` fail on any unclassified action, both directions
- [ ] Production dialogs and toasts unchanged (the new branch keys on `demo_read_only` only)

## Entry Criteria

- [ ] Story 1 implemented (demo boots, visitor enters)
- [ ] Announce to busy peers before editing shared primitives (`FormErrorBanner`, `client.ts`, `errors.ts`, `app-shell.tsx`) — `ListAgents`

## Steps

1. **Attributes**
   - `AllowedInDemoAttribute`, `RefusedInDemoAttribute` (mandatory reason, copy `AllowsWithoutSubscriptionAttribute`).
2. **Gate**
   - `DemoReadOnlyMiddleware`: `RefusesWrites` + `/api` path: non-GET/HEAD/OPTIONS refused unless `[AllowedInDemo]`;
     GET refused when `[RefusedInDemo]`; unroutable passes; writes 403 `{ error, code: "demo_read_only" }`.
   - `Program.cs`: insert after ≈1092, before `IdempotencyMiddleware`.
3. **Classify**
   - `[AllowedInDemo]`: `Auth.DemoSession`, `Auth.Refresh`, `Auth.Logout`, `CspReport.Receive`.
   - `[RefusedInDemo]`: patients export, dossier export, appointments export, backup archive, Google callback.
4. **Tests**
   - `DemoReadOnlyMiddlewareTests` (shape of `SubscriptionGateMiddlewareTests`) + order test.
   - `DemoExemptionCoverageTests`: reviewed sets, both directions; source scan — every GET writing an export /
     archive / dossier ledger row is `[RefusedInDemo]`.
5. **Web refusal primitives**
   - `client.ts`: `ApiErrorCode.DemoReadOnly`; keep off the token-refresh 401/403 path.
   - `use-conflict.ts`: `capture` keeps the `ApiError` (code); `form-error-banner.tsx`: neutral notice + link for a
     demo refusal; `errors.ts` `showErrorToast`: demo toast `id: "demo-read-only"` + action.
   - `check-responsive.mjs`: `demo-refusal-one-owner` (only the primitives compare the code).
6. **Band**
   - `web/components/demo/demo-band.tsx` in `app-shell.tsx` above `DashboardHeader`: `shrink-0`, `print:hidden`,
     short text `< sm`, CTA ≥ 44 px on coarse, top safe-area inset on phones, `role="status"` once.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Common/Authorization/AllowedInDemoAttribute.cs` | allow-list |
| `api/ClinicManagement.Application/Common/Authorization/RefusedInDemoAttribute.cs` | GET refusals |
| `api/ClinicManagement.API/Middleware/DemoReadOnlyMiddleware.cs` | the gate |
| `api/ClinicManagement.UnitTests/Api/DemoReadOnlyMiddlewareTests.cs` | gate tests |
| `api/ClinicManagement.UnitTests/Api/DemoExemptionCoverageTests.cs` | coverage |
| `web/components/demo/demo-band.tsx` | band |

### Files to Modify

| File | Changes |
|------|---------|
| `Program.cs` | gate position |
| `AuthController.cs`, `CspReportController.cs`, `PatientsController.cs`, `AppointmentsController.cs`, `BackupController.cs`, `GoogleCalendarController.cs` | attributes |
| `web/lib/api/client.ts`, `web/lib/hooks/use-conflict.ts`, `web/components/ui/form-error-banner.tsx`, `web/lib/errors.ts` | refusal UX |
| `web/components/app-shell.tsx`, `web/scripts/check-responsive.mjs` | band, guard |

## Verification Steps

- [ ] Full API suite green, unfiltered; web gate green
- [ ] Browser (isolated stack): new patient, appointment edit, fiche save, payment, settings → refused in-dialog, input kept; a refusal outside a dialog → one toast
- [ ] Band at 320 / 390 / 820 / 1180 / 1440 and 730 px tall never covers a control; coarse emulation: CTA ≥ 44 px
- [ ] SQL: no `IdempotencyRecords` row from the walk
- [ ] `HostedMultiTenant` boot: one ordinary save still works, its error banner unchanged

## Exit Criteria

- [ ] All ACs checked; gates green; committed; README status → `implemented`

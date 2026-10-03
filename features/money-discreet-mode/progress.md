# Progress: Mode discret

**Started:** 2026-10-03
**Type:** Small
**Branch:** feature/security-remediation (kept: two peer sessions were busy in this working tree, a checkout would move their files)

## Status
- [x] Implementation (resumed 2026-10-03 after an IDE crash: layout import + settings card were missing)
- [x] Quality checks — `dotnet test` 4877 passed / 0 failed · `tsc` 0 · `check:responsive` 72/72 · `npm run build` ok
- [x] Blast radius closed (rows 1–16; 16 = docs, done)
- [x] Tests — `Api/MoneyMaskGateMiddlewareTests.cs`, `Features/Clinics/ClinicMoneyMaskTests.cs` (22 cases)
- [x] Browser QA — `qa/run-1.md` GREEN, 26 rows, widths 320/390/1440/1536×730; `verify-schema` unchanged by the migration

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Clinic.IsMoneyHidden` + migration | new column | `ClinicConfiguration`, model snapshot | must change — config + migration (empty-`xmin` check) |
| 2 | `ClinicDto.IsMoneyHidden` | wire field | 7 C# builders (UserStatus, UpdateClinic, Regenerate, Create ×2, Join ×2) + TS `ClinicDto` | must change — every builder that holds the entity |
| 3 | `DashboardDto.Money` / `Receivables` → nullable | wire shape | C#: `GetDashboardQuery` only (tests use the reader directly) · TS: `types.ts`, `dashboard-page.tsx` (10 reads) | must change TS; C# unaffected |
| 4 | `StepUpCommand` (code-only actions) | shared step-up | archive download/restore, patient export, user TOTP reset, `ExportButton` | must re-test — a password still confirms an export |
| 5 | `ClinicsController` ctor | +2 deps | `MessagingCapabilityRegistrationTests` (2 `new`) | must change — compile fix |
| 6 | `[AllowsWithoutSubscription]` ×2 | reviewed list | `SubscriptionExemptionCoverageTests.ExpectedExemptWrites` | must change — 2 rows |
| 7 | New actions' policy | explicit `AdminOnly` | `ControllerAuthorizationCoverageTests` | unaffected — explicit policy on both |
| 8 | `AuditSaveChangesInterceptor.ValuedProperties` | shared name set | every aggregate | unaffected — no other property is named `IsMoneyHidden` |
| 9 | `MoneyMaskGateMiddleware` in `Program.cs` | new middleware after the subscription gate | every `/api` request | must re-test — caisse/factures/cheques load when shown; only attributed endpoints are read |
| 10 | `[HiddenWhenMoneyMasked]` on 13 reads | endpoint metadata | caisse (+ expense dialogs), factures, cheques, unrouted creances; patient file `GET invoices?patientId` | must re-test — patient file invoices tab while hidden |
| 11 | `nav.ts` `isNavItemVisible` / `buildNavSections` | shared predicate | sidebar (rail + drawer), bottom-nav, `zones.ts` `NAV_ICONS` | must change sidebar + bottom-nav; zones unaffected (new arg defaults to shown) |
| 12 | `client.ts` 403 `money_hidden` listener | new branch | every API error | unaffected — keyed on the code only |
| 13 | `MoneyVisibilityProvider` in `app/layout.tsx` | root provider | every page incl. `/login` | must re-test — reads only when signed in |
| 14 | caisse / factures / cheques pages, dashboard « L'argent » | gate | — | must change |
| 15 | `clinic-settings.tsx` | new card at the top | its own `Clinics` realtime reload | must re-test — a hide while a card is being edited shows the existing « Recharger » banner |
| 16 | `web/CLAUDE.md`, `api/.../CLAUDE.md` | docs | — | must change — nav + middleware notes |

16 rows: over the ~12 signal. Kept in the small pipeline because the owner approved option 1's full scope explicitly.

## Working tree note (start of session)
~150 dirty/untracked files already present (QA shots, fixtures, rules, design/, eyepass/, site/, console/ …). None are touched or staged by this feature. Stage by explicit path only.

## Files Changed
- api: `Clinic.cs`, `ClinicConfiguration.cs`, migration `20261003162234_AddClinicMoneyHidden` + snapshot, `IClinicRepository`/`ClinicRepository`,
  `ClinicMoneyMask.cs`, `HideMoneyCommand.cs`, `ShowMoneyCommand.cs`, `StepUpCommand.cs`, `ClinicDto` + 7 builders, `DashboardDto`,
  `GetDashboardQuery.cs`, `AuditSaveChangesInterceptor.cs`, `HiddenWhenMoneyMaskedAttribute.cs`, `MoneyMaskGateMiddleware.cs`, `Program.cs`,
  `ClinicsController` (+2 actions), marker on Billing/Expenses/Invoices controllers
- tests: 2 new files + 3 compile fixes (`MessagingCapabilityRegistrationTests`, `SubscriptionExemptionCoverageTests`, `DashboardTenantIsolationTests`)
- web: `lib/money-visibility/money-visibility-context.tsx`, `components/money-discreet-card.tsx`, `components/money-hidden-card.tsx`,
  `app/layout.tsx`, `clinic-settings.tsx`, `nav.ts`, `dashboard-sidebar.tsx`, `bottom-nav.tsx`, `caisse`/`factures`/`cheques` pages,
  `dashboard-page.tsx`, `client.ts`, `clinics.ts`, `types.ts`
- docs: root `CLAUDE.md`, `api/ClinicManagement.API/CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md`, `notes.md`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| No `totpOnly` prop on `StepUpDialog` | `hasTotp={true}` already renders only the code field; the server is what refuses a password |

## Significant Deviations

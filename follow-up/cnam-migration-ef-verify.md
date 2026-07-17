# Verify/regenerate the CNAM EF migration with the EF tool

> **Type:** debt
> **Priority:** high
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
The `AddCnamBulletinFields` migration (+ its `.Designer.cs` and the model snapshot edits) was **hand-authored** because `dotnet ef` is blocked by Windows WDAC on this machine (`0x800711C7` on freshly-built DLLs). It compiles and applies cleanly, but must be regenerated/diffed with the real EF tool in an unrestricted environment before merge to guarantee it matches what EF would emit.

## Current State
- `api/ClinicManagement.Infrastructure/Migrations/20260717120000_AddCnamBulletinFields.cs` — hand-written `Up`/`Down` (additive nullable columns on Patients/Clinics/Doctors).
- `...20260717120000_AddCnamBulletinFields.Designer.cs` — derived mechanically by copying `ApplicationDbContextModelSnapshot.cs`, renaming the class, adding `[Migration]`, renaming `BuildModel`→`BuildTargetModel`.
- `ApplicationDbContextModelSnapshot.cs` — hand-edited to add the new owned `CnamInfo` block + `MatriculeFiscal` + `CodeProfessionnelSante`.

## Key Files
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Infrastructure/Migrations/20260717120000_AddCnamBulletinFields.cs` | Up/Down |
| `api/ClinicManagement.Infrastructure/Migrations/20260717120000_AddCnamBulletinFields.Designer.cs` | Target model |
| `api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` | Model snapshot |

## Why Deferred
Environmental blocker (WDAC), not a code fault — cannot run `dotnet ef` from the shell here. See the "EF migrations when `dotnet ef` is blocked by Windows WDAC" note in the implement-small-feature skill and `features/cnam-bulletin-soins/progress.md`.

## Suggested Approach
1. On a machine without the WDAC block, `git stash` the three hand-authored files (or drop the migration) and run:
   `dotnet ef migrations add AddCnamBulletinFields --project ClinicManagement.Infrastructure --startup-project ClinicManagement.API`
2. Diff the generated files against the hand-authored ones; reconcile any difference (column types, snapshot ordering).
3. Confirm `dotnet ef database update` applies cleanly against a fresh DB and an existing (already-migrated) DB.

## Acceptance Criteria
- [ ] EF-generated migration matches the hand-authored intent (additive nullable columns only).
- [ ] `database update` applies on both a fresh and a pre-existing DB with no data loss.
- [ ] Snapshot has no drift when running `dotnet ef migrations add` a second time (no phantom migration).

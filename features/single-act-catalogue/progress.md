# Progress: One act catalogue

**Started:** 2026-08-31
**Type:** Small (forced — real surface ~48 files, user chose full implementation in one pass)
**Branch:** feature/security-remediation (user chose to stay on it)

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0 errors / 0 new warnings; tsc 0; check:responsive 23/23; next build OK)
- [ ] Tests (handled by /test-small-feature)

## Working tree note (start of session)
The branch carries unrelated in-flight security work: **14 modified + 97 untracked** files at session start.
None of this feature's target files were dirty, and all of them were byte-identical between `main` and `HEAD`.
Stage only the files listed under « Files Changed » — never `git add -A`.

## Scope decisions (asked, not assumed)
- Coefficients: **not sourced**. The NGAP arrêté's cotation annex is not published (convention art. 56 defers to
  it; `RBactes.pdf` carries all 100 codes and no coefficient column). The feature makes the gap visible.
- Consultations: kept as lettres clés `Cd`/`Cds` — confirmed official (NGAP arrêté 01/06/2026 art. 4) and priced
  as such by the convention's own table (Cd 30,000 · Cds 45,000 dès 01/01/2021, unchanged Dec-2022).
- Existing `CnamNomenclatureEntries` rows: **dropped outright**, no archive.

## Files Changed
**Deleted (10 backend + 6 frontend + 2 tests):** `CnamNomenclatureEntry` + its EF config + DTO, the 4 entry
commands + `CnamEntryMapper` + `GetCnamNomenclatureQuery`, `CnamNomenclatureController`,
`web/app/cnam-nomenclature/*`, `cnam-nomenclature-table.tsx`, `cnam-entry-form-modal.tsx`,
`lib/api/cnam-nomenclature.ts`, `CnamNomenclatureCrudTests`, `GetCnamNomenclatureQueryHandlerTests`.

**Moved (git mv, history kept):** `CnamReimbursementCalculator`, `GetCnamLetterValuesQuery`,
`UpdateCnamLetterValueCommand`, both estimate queries → `Features/DentalActs/**`; the 3 surviving test classes →
`UnitTests/Features/DentalActs/`; `CnamNomenclatureRequests.cs` → `CnamLetterValueRequests.cs`;
`CnamControllerAuthorizationTests` → `DentalActCatalogAuthorizationTests`.

**Edited:** `CnamCatalogSeed` (VLC-only, VD dropped), `DentalActCatalogSeed` (+`ConsultationActs`),
`ClinicCatalogSeeder` (+by-code consultation top-up), `ICnamCatalogRepository` + impl (VLC-only),
`ApplicationDbContext`, `AuditLabels`, `ConfirmDentalActsCommand` (+VLC), `ReimbursementEstimateDto`
(+`UnavailableReason`), `CnamReimbursementCalculator` (+`UnavailableReason`), `DentalActsController` (+4 routes),
`CnamBillingCalculator` (using), `20260721120611_AddCnamCatalog` (dead seed loops), 5 guard/coverage tests,
`dental-acts.ts`, `dental-acts-table.tsx` (+`Cotation`), `cnam-letter-values-card.tsx`, `dental-acts/page.tsx`,
`document-editor-content.tsx`, `nav.ts`, `zones.ts`, `clinic-hub.ts`, `types.ts`, `lib/cnam.ts`, 7 `CLAUDE.md`.

**New:** `Domain/Enums/ReimbursementUnavailability.cs`, `Features/DentalActs/CnamLetterValueMapper.cs`,
`Migrations/20260831215702_DropCnamNomenclatureCatalog.{cs,Designer.cs}` + snapshot.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|

## Significant Deviations
**DEV-1 — forced small pipeline on a ~48-file change.** Discovered surface: ~25 backend, ~10 test, ~10 frontend,
3 migration. User asked for full implementation in one pass rather than escalating to `/plan-feature`. Approved.

**DEV-2 — ~~the 26 CNAM entry seed rows are frozen inline in `20260721120611_AddCnamCatalog.cs`~~ SUPERSEDED BY DEV-4.** The spec said
"drop the table"; it did not say what to do about the shipped migration that iterates `CnamCatalogSeed.Entries`
at compile time (`AddCnamCatalog.cs:68`). Deleting the list breaks that migration's build. The data is moved into
the migration file itself (same GUIDs, same values — a historical script becomes self-contained) so the seed class
can lose it. **Not what shipped** — see DEV-4, which found the loops were already dead and simply removed them.

**DEV-3 — `ConfirmDentalActsCommand` absorbed the VLC half of the deleted `ConfirmCnamDataCommand`.** Not in the
spec, and required by it: that command cleared « à vérifier » on the entries **and** the letter values, and it was
the only writer of `CnamLetterValue.Confirm()`. Deleting it while AC-5 removes the `api/cnam-nomenclature` routes
would have left a VLC row permanently unconfirmable — a capability removed, which § 0 of the device contract and
AC-2 both forbid. The clinic filter is applied to the VLC loop too (stricter than the original). Approved: pending.

**DEV-4 — the dead seed loops were removed from `20260721120611_AddCnamCatalog` rather than the 26 rows being
frozen inline** (revises DEV-2's plan). `20260723110500_AddPerClinicCatalogs:22-24` deletes **every** row of
`CnamNomenclatureEntries`, `CnamLetterValues` and `DentalActCodes` when the catalogues became per-clinic, so both
loops were provably unreachable state for any database that reaches HEAD — `ClinicCatalogSeeder` is the real
seeding authority. Behaviour-neutral and a far smaller diff than inlining 26 rows. Approved: pending.

**DEV-5 — the consultation rows are seeded by `ClinicCatalogSeeder`, not by the migration.** The spec's
« Data / Schema Changes » says "insert 2 rows". A migration cannot: the ids are per-clinic MD5 hashes the app
computes, so SQL would have to invent GUIDs, and the seeder's existing guard (`skip if the clinic has any act`)
would never reach an existing cabinet. A by-code top-up in the seeder runs for every clinic on every startup, keeps
the ids deterministic, and is the pattern already used for the superseded-defaults correction. The migration says
so in a comment so nobody adds a duplicating `InsertData`. Approved: pending.

## Migration
`20260831215702_DropCnamNomenclatureCatalog` — generated with the **EF tool** (`BaseOutputPath` as an env var, so
the running API's locked `bin` was bypassed; args after `--` go to the app, not MSBuild). Two hand-fixes after
scaffolding:
1. **`Down()` scaffolded `xmin = table.Column<uint>(type: "xid", …)`** in its `CreateTable`. PostgreSQL refuses that
   name (« column name "xmin" conflicts with a system column name ») — the repo's documented trap, mirrored into the
   rollback path. Removed.
2. Added `DELETE FROM "CnamLetterValues" WHERE upper("LettreCle") = 'VD'` to `Up()`.
`Up()` is otherwise a single `DropTable`. Snapshot + Designer verified free of the entity.
⚠️ **Not run against a database** — `verify-schema` before/after is still owed (nothing in `UnitTests` touches one).

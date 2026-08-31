# Progress: One act catalogue

**Started:** 2026-08-31
**Type:** Small (forced — real surface ~48 files, user chose full implementation in one pass)
**Branch:** feature/security-remediation (user chose to stay on it)

## Status
- [x] Implementation
- [x] Quality checks (dotnet build 0 errors / 0 new warnings; tsc 0; check:responsive 23/23; next build OK)
- [x] Tests (added — see Test Plan)

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

## Test Plan
| AC | Action | Target | Notes |
|----|--------|--------|-------|
| AC-1 | Covered by an existing derived guard | `RealtimeResourceResolverTests` | Reflects every MediatR request against `clinic-hub.ts` and asserts the two key sets are equal **in both directions** — the `cnamnomenclature` removal is exactly what it exists to catch. Passes. |
| AC-2 | Modify existing | `DentalActCatalogAuthorizationTests` | Retargeted from the deleted controller; `UpdateLetterValue` is in the AdminOnly list, `GetLetterValues` in the any-authenticated list. |
| AC-3 | Add scenarios | `DentalActCatalogSeedTests` | `Consultations_Are_Coted_At_One`, `Consultation_Codes_Are_Their_Own_Lettre_Cle`, `Consultations_Use_A_Valued_Lettre_Cle`. The arithmetic itself is `CnamReimbursementEstimateTests`' existing coefficient × VLC × rate coverage. |
| AC-4 | Add scenarios | `CnamReimbursementEstimateTests`, `ReimbursementEstimatesQueryTests` | 4 facts on `UnavailableReason` (null / MissingCoefficient ×2 / NoLetterValue / precedence when both absent), plus per-index reasons through the batch handler and agreement between the batch and single-act reads. |
| AC-5 | Coverage note | — | Route moves have no unit surface: the controller is a thin MediatR pass-through. `DentalActCatalogAuthorizationTests` pins each action by `nameof`, so a rename fails the build. Verified live: authenticated, the three retired routes answer 404 (identical to a never-existent path) and both new routes 200. |
| AC-6 | Add scenarios | `CnamCatalogSeedTests` | `Seed_Holds_Only_Lettres_Cles_The_Nomenclature_Defines` pins the set to CD/CDS/D/RD; the `VD` inline case was dropped from `CnamVlcTests`' unsettled-clé theory (`ZZ` already covers "a clé we do not hold"). |
| AC-7 | Coverage note | — | `web/` has no test runner (documented in `.claude/rules/frontend-web.md` § 14). Covered by `check:responsive` 23/23 + the eye pass at 320/390/820/1440, both recorded below. |
| DEV-3 | Add scenarios | `CnamVlcTests` | The confirm's new VLC half: clears the provisional flag and stages the row, and leaves a row somebody already vouched for untouched. Cross-clinic refusal is in `CatalogTenantIsolationTests`. |

**Coverage note — `ClinicCatalogSeeder`'s consultation top-up has no unit surface.** It takes
`ApplicationDbContext`, and nothing in `UnitTests` touches a database (root `CLAUDE.md`). The seed *list* it reads
is unit-tested above; the top-up itself was verified live against the dev database: 7 cabinets × `Cd` + `Cds`,
coefficient 1, category « Consultation », `IsProvisional = false`, 700 → 714 acts.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `CnamReimbursementEstimateTests` + `ReimbursementEstimatesQueryTests` + `CnamVlcTests` | **37 passed**, 0 failed |
| Unit | `CnamCatalogSeedTests` + `DentalActCatalogSeedTests` + `CatalogTenantIsolationTests` + `DentalActCatalogAuthorizationTests` | **54 passed**, 0 failed |
| Unit | the derived guards — `RealtimeResourceResolver` · `AdminSurfaceCoverage` · `SubscriptionExemptionCoverage` · `ControllerAuthorizationCoverage` · `LogTemplateCoverage` | **40 passed**, 0 failed |
| Unit | **whole suite** (deliberate: the change deletes an entity and edits shared seeds + `ApplicationDbContext`, so the blast radius is wider than a typical small feature) | **3702 passed**, 0 failed |

Run with the isolated-`OutDir` + `dotnet vstest` recipe, which dodges both Smart App Control's `0x800711C7`
load block and the running API's lock on `bin`.

## Still owed
- **`verify-schema` before/after** — the migration was applied to the dev database without capturing a before run.
  Nothing in `UnitTests` can cover a migration, so this is the only gate for it:
  `docker exec` / `dotnet run -- verify-schema` before and after, diffed.

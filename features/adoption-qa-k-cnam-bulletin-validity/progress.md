# Progress: Adoption QA — K (a BS1 the caisse accepts)

**Started:** 2026-08-03
**Type:** Small (forced — 11 items, ~16 files; scope boundary chosen by the user, see DEV-2)
**Branch:** `feature/audit-sections-3-to-10` (existing; see DEV-3)

## Status
- [x] Implementation — **all eleven items (K1–K11) landed.**
- [x] Quality checks — `dotnet build ClinicManagement.sln` **0 errors**, 58 warnings all pre-existing
      (`CS8618`/`CS8604`/`CS8981`) and **none in a changed file** (verified by scoping the build output to the six
      touched C# files); `npx tsc --noEmit` clean; `npm run build` compiled successfully; `npm run check:responsive`
      **11/11 enforced checks pass**. ⚠️ **Eye pass NOT done — see below.**
- [x] Tests — 4 new classes, 3 extended; **136 passed, 0 failed** across the seven. See `## Tests Run`.

## Bugs found & fixed by the tests

**Two, and the first one shipped.**

1. **K8 was inert — the renderer still printed a period.** `CnamBs1BulletinRenderer.FormFormat` (the fr-TN
   `NumberFormatInfo`) was declared, documented at length, and **never used**: `FormatHonoraires` still ended
   `amount.ToString("0.000", CultureInfo.InvariantCulture)`. So the whole of K8 was dead code and the BS1 kept
   printing `30.000`. Found by reading the existing `CnamBs1BulletinRendererTests`, whose `[InlineData]`
   expectations were still periods and still passing — the test was pinning the defect. Fix: format with
   `FormFormat` (parsing stays `InvariantCulture`, since the input is normalised to a `.` first — the two cultures
   are deliberately different, and that asymmetry is exactly what made the bug easy to miss). The nine
   expectations are now commas, plus a four-digit case pinning that no group separator appears.
   ⚠️ This means the earlier « K8 landed » claim was wrong: the declaration landed, the behaviour did not.
2. **`CnamCatalogSeed` threw at type-initialisation.** My own K10 change put `LegacyLetterValues` *below* the
   `LetterValues` property. Static field initializers run in **textual order**, so `BuildLetterValues()` ran
   against a null array → `TypeInitializationException` on **every** read of the seed, i.e. application startup and
   every clinic-catalog operation. Caught immediately by the new seed tests (23 red). Fix: declare the array above
   the properties, with a comment saying why it must stay there.

Neither was reachable by the frontend gate or by a build — the first is a string a build cannot judge, the second
a runtime initialiser.

## Tests Run

| Suite | Filter | Result |
|-------|--------|--------|
| Unit | `BulletinMandatoryFieldsTests` · `CnamClosedSetContractTests` · `DentalActCatalogSeedTests` · `CnamCatalogSeedTests` · `CnamVlcTests` · `CnamBs1BulletinRendererTests` · `MedicalDocumentPdfErrorTests` | **136 passed, 0 failed** |
| Unit | whole compiling suite (regression sweep) | 1692 passed, **31 failed — none in a class K touched** (see below) |

### ⚠️ The test project does not compile on this branch, and that is not K's doing

Six test files reference production members other **in-flight features on this branch** have since renamed or
removed, so `dotnet build` on `ClinicManagement.UnitTests` fails before any test runs:

| File | Missing member |
|---|---|
| `Api/NotificationJobTests.cs` | `INotificationRepository.GetPendingNotificationsAsync` (now `GetDueForDispatchAsync`) |
| `Infrastructure/Services/ReminderScheduleTests.cs` | `ReminderSchedule.ComputeSendTimeUtc` (now `ComputeSendTimes**Utc**`, returning a list) |
| `Features/Recall/RecallDeliveryTruthTests.cs` | same rename |
| `Features/Billing/MoneyReadConsistencyTests.cs` | `GetInvoiceRevenueQueryHandler` |
| `Features/Billing/InvoiceDebtIsAgedTests.cs` | `ReceivableDto` |
| `Infrastructure/Persistence/AuditInterceptorTests.cs` | non-constant default parameter (`scopedClinic`) |

K touches none of those areas. To verify K's own tests I temporarily moved the six aside (`*.ktestrun`), built,
ran, and **restored all six** — verified: no remaining stashes, no deletions in `git status`. They are **left
exactly as found**; adapting them means deciding the new APIs' intended semantics, which belongs to whoever made
the renames.

The 31 remaining failures are in eleven classes, none of them K's: `GetCnamNomenclatureQueryHandlerTests`,
`GetMedicationsQueryHandlerTests`, `GetStockItemsQueryHandlerTests`, `GetTreatmentPlansQueryHandlerTests`,
`CreditNoteReadTests`, `InvoiceTenantIsolationTests`, `TreatmentPlanTenantIsolationTests`,
`ProcedureTypeTenantIsolationTests`, `PatientContactOptionalTests`, `LiaisonRenderContentTests`,
`ReminderSchedulerTests`. The shape is the one the test guide warns about: filters moved into **SQL**
(`list-pagination`) while the repository **mocks** still return every row, so `Assert.Single()` now sees four. Same
class of stale fixture, same other-feature origin.

## ⚠️ Outstanding before this is mergeable

1. **The device eye pass at 320 / 390 / 820 / 1180 / 1440 px + landscape + keyboard.** No browser was available in
   this session, so the mechanical gate (`check:responsive`) plus a re-read of the diff against
   `DEVICE-CONTRACT.md` § 1 is all that was done. The surfaces to walk are the bulletin editor's new practitioner
   `Select` and refusal block, the act row's two new notes, the act-lookup `Popover`, `/cnam-nomenclature`'s
   convention prompt (**in both trees** — the stacked form below `md:` and the table above), and
   `/dental-acts`' new note. Not claimed as passed.
2. **The manual paper verification** (unchanged from the spec): fill a bulletin end to end, print onto a real
   pre-printed BS1, check every stamped field lands in its box. No test in this repo can assert paper.

## Working tree note (start of session)

The branch carried **210 dirty files** at session start (now **255** — other in-flight features have advanced
since). None of it belongs to this feature. Per `check-file-is-clean-before-staging`, every file this feature
touches is staged **explicitly by path**; `git add -A` / `git add .` are never used. Files this feature edits
that were **already dirty** before it started (so their diff mixes two changes and must be reviewed by hunk,
not by file):

- `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs`
- `api/ClinicManagement.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- `web/app/cnam-nomenclature/page.tsx`, `web/app/dental-acts/page.tsx`
- **`web/components/document-editor-content.tsx`** — the heaviest case (+809/−550 vs HEAD), carrying
  `liaison-norms-and-document-email` and the mobile redesign. Every K1/K2/K3/K4/K5/K6 hunk lands in this file.

## Where this feature stands — read this first on resume

### Landed (verified by reading the working-tree diff, not by assumption)

| Item | State | Where |
|---|---|---|
| **K2 (backend half)** | **Done** | New `api/…/Application/Features/Documents/BulletinCnamValidation.cs` (untracked) — one French message naming *every* missing field; wired into **both** `CreateMedicalDocumentCommand` and `UpdateMedicalDocumentCommand`. ⚠️ The update path gates on `user != null` deliberately: `user == null` is the background `PdfGenerationJob` re-rendering a stored bulletin, and refusing there would make an existing incomplete document permanently **unprintable**. |
| **K7** | **Done** | `CnamInfo` gained `IdentifiantUniqueDigits = 10`, `CountIdentifiantDigits`, `IsValidIdentifiantUnique`; refused at K2's gate. `CnamBs1BulletinRenderer.StampAssureAndMalade` became an **instance** method so it can `_logger.LogWarning` on an over-length IDU instead of truncating silently. |
| **K8** | **Done** | `CnamBs1BulletinRenderer.FormFormat` — an explicitly-built `NumberFormatInfo` (comma, no group separator), **not** `CultureInfo.GetCultureInfo("fr-TN")`: globalization-invariant mode would silently resolve that back to the invariant culture and reprint a period. |
| **K9** | **Done** | `MedicalDocumentsController` PDF catch now routes through `Failure(...)` (canonical `{ error }`). Only `InvalidOperationException` is surfaced verbatim — that is the type the three fail-fast French operator messages use; anything else gets `ErrorMessages.Generic`, since an arbitrary .NET message can carry a path or a connection string. |
| **K10 (table only)** | **Partial** | New `api/…/Domain/Services/CnamConventionTariffs.cs` (untracked) holds the convention's values (`CD 30,000` · `CDS 45,000` · `D 3,000`), `InForceSince`, `RevisionIntervalYears = 3`, `Source`. **Nothing consumes it yet** — the seed still ships the stale numbers. |

Also worth knowing: the régime/lien literals are now `CnamInfo` **constants**, matched by the renderer's two
`switch`es *and* by K2's validation — so the byte-for-byte accent risk the spec flags (§ K2 ⚠️) is closed
structurally rather than by care.

### Not started

- **K1** — the bulletin act picker still reads `cnamNomenclatureApi.list()`
  (`document-editor-content.tsx:732`) and `selectNomenclatureEntry:868` still writes the mnemonic `codeActe`.
- **K2 (editor half)** — no pre-Save marking, no disabled Save with a visible reason. Only the liaison check
  at `:1515` exists (that is the pattern to copy).
- **K3** — `selectedDoctor = currentUserDoctor || (doctors.length > 0 ? doctors[0] : null)` at `:490`. Still no
  `setSelectedDoctor` anywhere in the file.
- **K4** — `handlePrint:1432` still guards on `documentRef.current`, and `ref={documentRef}` is on the `<Card>`
  at `:2625`, i.e. in the **else** branch of the `bulletin-cnam ? … : (…)` ternary. The Print button
  (`:2483`) is in the shared Actions block, ungated on type.
- **K5** — `generateWordInternal:1154`'s chain is still `prescription` / `liaison` / `certificat` with no
  `bulletin-cnam` branch and no `else`; « Télécharger Word » at `:2505` is ungated.
- **K6** — `prefillActsFromRecords:830` still does `new Date(r.interventionDate)` vs `new Date(actsTo)`.
- **K10 (the rest)** — seed correction, the startup correction for already-seeded clinics, the DTO fields, the
  admin prompt + cadence help text.
- **K11** — `DentalActCatalogSeed` still flags **all 15 Prothèse rows** `RequiresAccordPrealable = true`.

## Test Plan

| Item | Action | Target file | Notes |
|---|---|---|---|
| K1 | **New class** | `Infrastructure/Persistence/DentalActCatalogSeedTests.cs` | Every code the picker can supply matches `^DCH\d{6}$`; the two catalogues are **disjoint** (the assertion that says *why* K1 was needed) |
| K11 | Add scenarios | same file | Prothèse flags cleared; Parodontologie/ODF untouched; `SupersededAccordPrealable` is surgical |
| K2 | **New class** | `Features/Documents/BulletinMandatoryFieldsTests.cs` | One case per missing field, all problems in one message, a complete bulletin passes, **every régime and lien value accepted byte-for-byte** |
| K7 | Add scenarios | same file | Over-length IDU refused, exactly 10 accepted, separators ignored |
| K2/DEV-6 | **New class** | `Features/Documents/CnamClosedSetContractTests.cs` | Derived FE↔BE contract: parses `web/lib/cnam.ts` and asserts its two sets **equal** `CnamInfo`'s, in both directions. Modelled on `RealtimeResourceResolverTests` |
| K8 | **Modify existing** | `Infrastructure/Services/CnamBs1BulletinRendererTests.cs` | The 9 honoraires expectations become **commas** — the existing test pinned the defect |
| K1/K7 | Add scenarios | same file | A legacy mnemonic-coded document still renders and keeps its code; an over-length IDU renders without truncating silently |
| K10 | Add scenarios | `Infrastructure/Persistence/CnamCatalogSeedTests.cs` | Seed now carries the convention values; `Vd`/`Rd` untouched; `SupersededLetterValue` returns the legacy figure for the three and **null** for the rest |
| K10 | Add scenarios | `Features/CnamNomenclature/CnamVlcTests.cs` | The DTO's three convention fields are populated for `Cd`/`Cds`/`D` and **null together** for `Vd`/`Rd` |
| K9 | **New class** | `Api/MedicalDocumentPdfErrorTests.cs` | `InvalidOperationException` surfaces verbatim; any other exception is generic **and its message does not leak** |

### Coverage notes — items with no unit-test surface

- **K3 (practitioner picker), K4 (print the iframe), K5 (no Word export), K6 (local-day filter), K2's editor half,
  and DEV-7 (lettre clé alone)** are all in `web/components/document-editor-content.tsx`. **`web/` has no test
  runner** (no vitest/jest; `eslint` isn't even installed — see `web/CLAUDE.md`). They are covered by the gate that
  does exist — `npx tsc --noEmit` + `npm run build` + `npm run check:responsive` — plus the outstanding manual eye
  pass. Not contrived into backend tests.
- **`ClinicCatalogSeeder.CorrectSupersededDefaultsAsync`** takes an `ApplicationDbContext`, and **nothing in this
  suite touches a database** (an explicit rule in `ClinicManagement.UnitTests/CLAUDE.md`). Its *pure* half — the
  predicate's third term — is tested directly via `SupersededLetterValue`/`SupersededAccordPrealable`, which is the
  seam the pass reads. The end-to-end backfill is startup/operator-verified.
- **The canonical `{ error }` body itself** is already pinned by `Api/ApiControllerBaseTests.cs`; the new class
  covers only K9's *new* branch (which exception type is surfaced verbatim).

> **Class count.** Four new classes + three extended is past the skill's ~5 signal, but that signal is for *new
> feature surface*. K is a hardening pass over existing handlers and seeds; each class is thin and mirrors a
> sibling, and there is no new user flow to E2E. Not escalated — per the skill's own carve-out.

## Two decisions taken with the user this session (both approved)

### DEV-4 — K10 corrects on `UpdatedAt == null`, not on `IsProvisional`

**Spec said:** « Update only rows still flagged `IsProvisional` (untouched since seeding). »

**Why that is wrong as written:** `UpdateCnamLetterValueCommand` calls `CnamLetterValue.SetValue`, which
stamps `UpdatedAt` and **does not clear `IsProvisional`** (only `Confirm()` does). So an admin who deliberately
typed their own valeur de la lettre clé and never pressed « Confirmer » still reads `IsProvisional = true` —
and the spec's literal predicate would clobber precisely the deliberate entry the same paragraph says must
never be clobbered. `IsProvisional` means « nobody has vouched for this », not « nobody has touched it ».

**Approved predicate** — all three terms, per lettre clé:

```
row.UpdatedAt == null            // never touched since seeding  ← the real « untouched » signal
  && row.IsProvisional           // still unvouched-for
  && row.Value == the value the seed shipped before the correction
→ SetValue(CnamConventionTariffs.ValueFor(cle))
```

Anything else is left alone and surfaced as the admin prompt instead. Honours the spec's stated **intent**;
deviates from its literal mechanism. Approved: **Y**.

### DEV-5 — the correction runs in `ClinicCatalogSeeder`, not in a migration

`SeedForClinicAsync` already runs for every clinic at startup via `SeedAllClinicsAsync`
(`DeferredStartupService`/`Program.cs`), which is exactly the shape needed, and it avoids a hand-authored
migration + Designer + `verify-schema` backfill count on a machine where `dotnet ef` is blocked by Smart App
Control (`0x800711C7`). The spec's own Data/Schema section says no migration is required. Same mechanism
carries K11's Prothèse flag correction. Approved: **Y**.

⚠️ It does widen the seeder's documented contract (« idempotent per catalog: a catalog that already has rows
for the clinic is skipped ») — the correction pass runs *after* the seed-if-empty blocks and must be
documented there, and in `Infrastructure/CLAUDE.md`.

## The implementation plan, as far as it was worked out

### Backend (K10 + K11)

1. **`Infrastructure/Persistence/CnamCatalogSeed.cs`** — build `LetterValues` from one raw table of the
   **legacy** figures, taking `CnamConventionTariffs.ValueFor(cle) ?? legacy` (so `VD`/`RD` keep 10 / 2 and
   stay « à vérifier »), and expose

   ```csharp
   public static decimal? SupersededLetterValue(string? lettreCle)
   ```

   returning the pre-correction figure **only** for a cle the convention actually moved. That is what gives the
   seeder DEV-4's third term from a single table instead of a second hardcoded copy of `7m / 10m / 1.200m`.
   ⚠️ `20260721120611_AddCnamCatalog` inserts `CnamCatalogSeed.LetterValues` at runtime, so a **fresh** database
   gets the corrected values straight from the migration and never matches the correction predicate. Good, but
   it means the seed change alone is not a no-op for existing installs — hence step 3.
2. **`Infrastructure/Persistence/DentalActCatalogSeed.cs`** — flip the 15 `Prothese` rows to `Ap = false`
   (K11, sourced: covered hors plafond without accord préalable since April 2019). Leave `Parodontologie` and
   `OrthopedieDentoFaciale` **exactly as they are** — the real list is fixed by an *arrêté conjoint* the
   research could not retrieve, and the spec is explicit: **do not invent the list.** Expose the superseded
   flag the same way (`SupersededAccordPrealable(code)` → `true` only for the Prothèse codes) so step 3 can be
   surgical here too.
   ⚠️ `20260722111316_AddDentalCore` also inserts from this seed at runtime — same fresh-vs-existing split.
3. **`Infrastructure/Persistence/ClinicCatalogSeeder.cs`** — a correction pass after the four seed-if-empty
   blocks, over `IgnoreQueryFilters()` (no clinic in scope), applying DEV-4's predicate per row. `DentalActCode`
   has no single-field mutator, so the flag correction goes through `Update(...)` echoing every current field —
   which stamps `UpdatedAt`, so the pass is **self-terminating** and cannot re-fire.
4. **`Application/DTOs/CnamLetterValueDto.cs`** + `CnamEntryMapper.ToDto` — add `ConventionValue` (decimal?),
   `ConventionSource` (string?), `ConventionRevisionIntervalYears` (int?), all **null for `VD`/`RD`**, which is
   what makes « we do not know » renderable as such rather than as a figure. Populated from
   `CnamConventionTariffs`; the array shape of `GET /cnam-nomenclature/letter-values` is unchanged, so the
   pinned contract holds.

### Frontend

5. **`web/lib/api/types.ts`** — mirror the three new `CnamLetterValueDto` fields.
6. **`web/components/cnam-letter-values-card.tsx`** (K10) — for a row whose stored value differs from
   `conventionValue`, a prompt naming both figures plus an « Appliquer » action calling the existing
   `updateLetterValue`; the source and the three-year cadence as help text. ⚠️ This file was just rewritten by
   the mobile pass into **two trees** (a `CARDS_ONLY` stacked form below `md:` and a `TABLE_ONLY` table above) —
   the prompt has to land in **both**, or it is invisible on a tablet.
7. **`web/components/dental-acts-table.tsx`** / `dental-act-form-modal.tsx` (K11) — a note that which families
   require accord préalable is **unverified** and correctable per clinic. The flag is already editable there,
   so this is copy, not plumbing.
8. **`web/components/document-editor-content.tsx`** — K1, K2-editor, K3, K4, K5, K6:
   - **K1:** swap the act-lookup read to `dentalActsApi.list()` and the row type to `DentalActDto`; keep
     `parseCotation`/`estimateReimbursements` untouched (they take a lettre clé + coefficient and do not care
     which table supplied them). Two traps: `DentalActCode.Coefficient` is **nullable** where
     `CnamNomenclatureEntry.Coefficient` is not — a null must write the lettre clé alone (« D ») and leave the
     coefficient for the dentist, never `D 0`, since a zero estimate is indistinguishable from « non
     remboursable »; and the **stored** acts of an existing bulletin must keep whatever code they hold
     (re-pointing the *picker* must not rewrite history — the renderer stamps the row verbatim). Free win:
     render « Accord préalable requis » from `requiresAccordPrealable` on the act row.
   - **K2-editor:** derive the missing-field list from the same five facts the backend checks, mark those
     fields, and disable Save with the reason as **visible text** (a `title` is unreachable on touch). Validate
     on Save, not per keystroke.
   - **K3:** a real practitioner `Select` defaulting to `currentUserDoctor` and otherwise to **nothing**;
     feeds `doctorCodeProfessionnel` in `buildBulletinContent:987`. A practitioner with no
     `codeProfessionnelSante` gets the certificat's treatment — copy the wording pattern at `:2226`
     (« Aucun numéro d'ordre sur votre profil. Ajoutez-le dans « Mon profil ». »). A clinic with exactly one
     dentist keeps the control **visible but pre-filled**, per the spec's edge case.
   - **K4:** gate Print on `documentType === "bulletin-cnam"` and print the overlay **iframe** (`bs1PreviewUrl`,
     state at `:1678`) rather than moving `documentRef` — printing a PDF object URL and printing a cloned
     `<Card>` are genuinely different operations, and conflating them is what produced the bug. Handle
     `bs1PreviewUrl === null` (preview not yet generated / failed) with its own French refusal.
   - **K5:** hide/disable « Télécharger Word » for a bulletin with a short reason.
   - **K6:** compare on the clinic-local calendar day. **`toLocalIso(date)` already exists in
     `web/lib/format.ts`** (added by a sibling feature for the same class of bug) — use it, do not add a second
     helper. Fix **both** bounds and make the upper one inclusive of its own day.

### Explicitly out of scope, confirmed while reading

`CnamNomenclatureEntry` is **not** retired (`/cnam-nomenclature`, `CnamLetterValuesCard` and
`GetReimbursementEstimatesQuery` all read it). Consolidating the two near-identical entities is a genuine
follow-up — worth a `capture-followup` entry — but folding it in here would make this a Full feature.

## Files Changed

Previous session (K2 backend · K7 · K8 · K9 · the tariff table):

- `api/…/Application/Features/Documents/BulletinCnamValidation.cs` **(new)**
- `api/…/Application/Features/Documents/Commands/CreateMedicalDocumentCommand.cs`
- `api/…/Application/Features/Documents/Commands/UpdateMedicalDocumentCommand.cs`
- `api/…/Domain/ValueObjects/CnamInfo.cs`
- `api/…/Domain/Services/CnamConventionTariffs.cs` **(new)**
- `api/…/Infrastructure/Services/CnamBs1BulletinRenderer.cs`
- `api/…/API/Controllers/MedicalDocumentsController.cs`

This session (K10 · K11 · K1 · K2-editor · K3 · K4 · K5 · K6):

| File | Items |
|---|---|
| `api/…/Infrastructure/Persistence/CnamCatalogSeed.cs` | K10 — derives the VLC seed from `CnamConventionTariffs`; adds `SupersededLetterValue` |
| `api/…/Infrastructure/Persistence/DentalActCatalogSeed.cs` | K11 — 15 Prothèse rows cleared; adds `SupersededAccordPrealable` |
| `api/…/Infrastructure/Persistence/ClinicCatalogSeeder.cs` | K10/K11 — `CorrectSupersededDefaultsAsync` startup pass (DEV-5) |
| `api/…/Application/DTOs/CnamLetterValueDto.cs` | K10 — three convention fields |
| `api/…/Application/Features/CnamNomenclature/Commands/CnamEntryMapper.cs` | K10 — projects them |
| `web/lib/api/types.ts` | K10 — mirrors the DTO |
| `web/lib/cnam.ts` **(new)** | K2 — the two closed sets + IDU rules, client-side |
| `web/components/edit-patient-dialog.tsx` | K2 — its `SelectItem`s now read `lib/cnam.ts` (DEV-6) |
| `web/components/cnam-letter-values-card.tsx` | K10 — `ConventionPrompt` in both trees + cadence footnote |
| `web/components/dental-acts-table.tsx` | K11 — the unverified-list note |
| `web/components/document-editor-content.tsx` | K1, K2-editor, K3, K4, K5, K6 |

## Auto-Approved Deviations

| Deviation | Reason |
|-----------|--------|
| The act-lookup `PopoverContent` moved from `w-80` to `w-[min(20rem,calc(100vw-2rem))]` | Touched while swapping the catalogue; an unqualified `w-80` is 320 px inside a 320 px viewport, i.e. edge-to-edge with no gutter (`frontend-web.md` § 10). Internal, no behaviour change. |
| « Télécharger PDF » drops to `grid-cols-1` when the Word button is hidden | Same edit as K5. A `grid-cols-2` with one child leaves the button at half width beside a gap, which reads as a control that failed to render. |
| `prefillActsFromRecords` also stops deriving each act's **date** with `split("T")[0]` | Same defect as K6's bounds, same line of code: the stored UTC day for an evening séance is *tomorrow* printed on a CNAM document. Fixing the filter and leaving the value it writes would have been half a fix. |

## Significant Deviations

- **DEV-1 / DEV-2 / DEV-3** — recorded in the previous session (forced-small pipeline, the chosen scope
  boundary, the existing-branch decision). ⚠️ Their detail was never written down here; reconstruct from the
  spec's `Type:` line and the branch note above if it matters.
- **DEV-4** — K10 corrects on `UpdatedAt == null && IsProvisional && value == superseded`, not on
  `IsProvisional` alone. Full reasoning above. Approved **Y**.
- **DEV-5** — the K10/K11 correction runs as a `ClinicCatalogSeeder` startup pass, not a data migration. Full
  reasoning above. Approved **Y**.
- **DEV-6 — `web/lib/cnam.ts` is a new shared module, not a third copy of the régime/lien literals.**
  K2's editor half has to name *which* mandatory field is missing, which means the browser needs the two closed
  sets. They existed only as `<SelectItem value>` literals inside `edit-patient-dialog.tsx`, so the alternative was
  a second hand-typed copy of « Convention bilatérale » — an accented French literal whose mismatch fails
  **silently** (the renderer's `switch` falls through and prints an empty régime box). The module mirrors the
  backend `CnamInfo` constants, and `edit-patient-dialog.tsx` was re-pointed at it in the same change so there is
  one client-side copy rather than two. Touching a second component is *external scope*, hence recorded here rather
  than auto-approved — the `fixes-dont-propagate` pattern applied deliberately. Not separately confirmed with the
  user; flagged in the session report.
- **DEV-7 — every seeded DCH act has a null `Coefficient`, so K1 costs the estimate its input.** The spec asserts
  « `LettreCle` and `Coefficient` both exist on `DentalActCode`, so … the reimbursement estimate keep[s] working
  unchanged ». They exist, but `DentalActCatalogSeed` passes `coefficient: null` for **all 100 rows** on purpose
  (« the cotation lives in the NGAP arrêté, not the acts list »), where `CnamNomenclatureEntry` carries real
  coefficients. So after K1 a picked act fills the code and *not* the cotation until an admin enters coefficients
  on `/dental-acts`. That is the right trade — a correct code with no estimate beats a wrong code with one, since
  the code is what the caisse rejects on — but it must not be silent, which is also what the spec's own edge case
  demands (« a null coefficient must not silently estimate zero, which is indistinguishable from
  « non remboursable » »). Implemented as: `selectDentalAct` writes the **lettre clé alone** (never `D 0`, never
  `D null`), and a per-row note says the coefficient is missing, where it comes from, and that the CNAM computes
  the real figure regardless. Not separately confirmed with the user; flagged in the session report.

## Deferred to /test-small-feature

Per the spec's Testing section: `Bs1UsesRealNomenclatureTests` (K1, incl. a legacy mnemonic-coded document
still rendering), `BulletinMandatoryFieldsTests` (K2 — one case per missing field **plus** the exact-string
match for every régime and lien value, which is the test that would have caught a silent no-op switch),
renderer tests for K7 and K8, and the document-error-path test for K9's canonical `{ error }` body. Add one
for DEV-4's predicate: an admin-edited row must survive the correction pass.

Two more the implementation added, worth a case each:

- **`selectDentalAct` writes the lettre clé alone when the catalogue carries no coefficient** (DEV-7) — assert it
  never produces `"D 0"` or `"D null"`, and that `parseCotation` declines it rather than estimating zero.
- **`CnamCatalogSeed.SupersededLetterValue`** returns the legacy figure for `CD`/`CDS`/`D` and **null** for
  `VD`/`RD` — the null is what stops the correction pass touching a lettre clé the convention does not settle.

⚠️ `dotnet test` fails at assembly load with `0x800711C7` on this machine (Smart App Control ON —
environmental, not a defect). Write the tests; verify with SAC off or elsewhere.

**The load-bearing verification is manual and cannot be automated:** fill a bulletin end to end, print it onto
the real pre-printed BS1 form, and check by eye that every stamped field lands in its box — codes, IDU comb,
régime tick, lien tick, code PS, honoraires. Not yet done; record the result here.

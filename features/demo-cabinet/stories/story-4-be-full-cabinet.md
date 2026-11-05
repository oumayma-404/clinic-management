# Story 4: BE - The cabinet is full of believable work

**Status:** APPROVED
**Story Status:** reviewed (2026-11-03, 100/100)
**Layer:** BE
**Depends On:** 1
**Blocks:** 5, 6

## Objective

The demo API builds « Cabinet Dentaire Démo » with the full spec FR-6 scenario through the application's own
commands, so every screen has content, every figure agrees, and `verify-schema` + `reconcile-money` exit 0.
Business dates follow the scenario around today; creation stamps read the rebuild day (owner, 2026-11-03).

## Acceptance Criteria

_From spec:_

- [x] AC-2.1 every sidebar screen has content (Rappels keeps « Bientôt disponible »)
- [x] AC-2.2 −14 … +14 working days, yesterday full, today ~3 visits per dentist
- [x] AC-2.4 coherent on any day of the year; AC-2.5 solde / À clôturer / caisse / dashboard agree
- [x] FR-6 table, every row; banned claims respected (no CNAM screen, no working reminders)

_Story-specific:_

- [x] Past visits are created Completed with their fiche (only 1–2 deliberately left for « À clôturer »)
- [x] No `DateTime.UtcNow` / `DateTime.Today` in the builder — `ClinicClock` only (source-scan test)
- [x] Journal authors are the acting team member
- [~] Locator tightened (not done — see progress.md, decided deviation) to « active admin **and** ≥ 1 patient »

## Entry Criteria

- [ ] Story 1 implemented (rebuilder + seed context exist)
- [ ] Letterhead / cachet assets copied from `C:\dev\clinic-film\public\tuto\assets\`; `site/src/img/pano.jpg` available

## Steps

1. **Assets** — embed letterhead, cachet, panoramique, 3 photos under `api/ClinicManagement.Application/Features/Demo/Assets/`.
2. **Builder parts** (`DemoCabinetBuilder` + `DemoScenario*.cs`, each sending real commands as the right member):
   team & settings → patients (~40 Friends names, Ross Geller as in the videos, a « denture mixte » child, no
   real-looking phones) → agenda (both dentists, congé for Dr Bing, recurring series, waiting list) → fiches with acts
   (bridge piliers + pontique, implant, caries/obturations, absents, « à traiter ») → treatments at every state →
   money (cash/cheque/card, cheque to deposit, notes d'honoraires, expenses, one owing, one avoir) → documents
   (ordonnance + demande d'examens, certificat, arrêt de travail) → files → stock + fournisseurs + lab (one late) →
   worklists.
3. **Guards respected** — no future payment (`PaymentDateRules`), no slot overlap, multi-séance money on the
   treatment (root CLAUDE.md traps), followed treatments via the same commands the booking dialogs use.
4. **Wire in** — `DemoCabinetRebuilder` runs the whole builder; locator tightened.
5. **Tests** — `DemoCabinetBuilderTests` (mocked `ISender`): every area sends ≥ 1 command; visit dates on working
   days inside hours; scenario anchor overridable for Sunday / 1 January; raw-clock source scan.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `api/ClinicManagement.Application/Features/Demo/DemoCabinetBuilder.cs` + `DemoScenario*.cs` | scenario |
| `api/ClinicManagement.Application/Features/Demo/Assets/*` | images |
| `api/ClinicManagement.UnitTests/Features/Demo/DemoCabinetBuilderTests.cs` | shape tests |

### Files to Modify

| File | Changes |
|------|---------|
| `api/ClinicManagement.API/BackgroundJobs/DemoCabinetRebuilder.cs` | run full builder |
| `api/ClinicManagement.Application/Features/Demo/DemoCabinetLocator.cs` | ≥ 1 patient |
| `ClinicManagement.Application.csproj` | embed assets |

## Verification Steps

- [ ] API suite green, unfiltered
- [ ] Demo API boots on `clinic_demo_local` and builds; `dotnet run -- verify-schema` → 0; `reconcile-money` → 0
- [ ] Browser: every sidebar screen has content; one patient's solde matches « À clôturer », la caisse and the dashboard
- [ ] Rebuild with anchor = a Sunday and = 1 January → still coherent

## Exit Criteria

- [ ] All ACs checked; gates green; committed; README status → `implemented`

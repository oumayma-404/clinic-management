# Story 4 Review Report

**Story:** 4 - The cabinet is full of believable work (BE)
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-2 « a cabinet that looks lived-in »: every FR-6 row built through the product's own commands |
| Acceptance criteria met | 15/15 | AC-2.1 (22-screen sweep GREEN), AC-2.2, AC-2.3 (job skips only during the build), AC-2.4 (four anchor days in tests), AC-2.5 (`reconcile-money` 0) |
| Functional requirements | 10/10 | FR-6 table complete after the review added a second clinical photo; banned claims respected (no CNAM screen, Rappels « Bientôt disponible », no phones) |
| Edge cases handled | 5/5 | Sunday / 1 January / Saturday anchors; the agenda-job race; withdrawn acts in `verify-schema` |
| No scope creep | 5/5 | `DemoCabinetBuilds` and the `verify-schema` fix are both masking defects found by the story's own gates |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | real MediatR commands per member, one DI scope each; `ClinicClock` only; skip hook beside the existing fence skip |
| No dead code | 5/5 | — |
| No duplication (DRY) | 10/10 | doctor name / specialty literals folded into `DemoScenario` + `DoctorSpecialtyLabels` (review fix) |
| Clean solutions (no hacks) | 10/10 | — |
| Unit tests | 8/8 | `DemoCabinetBuilderTests` (15 cases incl. 3 new anchor theories), `AppointmentProgressJobTests` +1 |
| Integration tests | 7/7 | N/A as a project here; the real build on `clinic_demo_local` + both verbs + three browser walks stand in |

## Test Traceability Matrix

Spec uses numbered ACs for US-2 and a narrative FR-6 table; rows below map each to its proof.

| AC / FR-6 row | Unit Test | Real build / verbs | Browser | Status |
|---|---|---|---|---|
| AC-2.1 every sidebar screen has content | `Every_Area_Is_Written_By_The_Right_Member`, `Records_Are_Written` | SQL row counts | `walk-4c` S1–S12 + C2–C10, `walk-4b`, `walk-4a` | ✓ |
| AC-2.2 agenda −14…+14, today ~3 each | `Every_Visit_Is_Inside_The_Cabinets_Hours`, `No_Chair_Is_Double_Booked…`, `Today_Is_Three_Visits_Each…` | 6 today ended + 2 planned « séance passée » | `walk-4a` | ✓ |
| AC-2.3 today's visits move with the clock | `A_Demo_Cabinet_Being_Built_Is_Skipped_Until_It_Is_Built` | job wrote 8 rows right after the build | — | ✓ |
| AC-2.4 any day of the year | anchors Tue / Sun / 1 Jan / Sat on hours, treatments, records, no-future | — | — | ✓ |
| AC-2.5 every figure agrees | `Treatments_And_Money_Are_Written` | `reconcile-money` 0 | `walk-4b` M1–M4 | ✓ |
| FR-6 Cabinet / Team | `The_Hours_Are_Valid_With_Sunday_Closed`, roles test | — | S10, S12 | ✓ |
| FR-6 Patients | `The_Videos_Patients_Are_There` | — | S5 | ✓ |
| FR-6 Clinical | roles test (diagnoses), bridge pontique assert | `verify-schema` | `walk-4a` | ✓ |
| FR-6 Treatments | `Treatments_And_Money_Are_Written` | plan statuses from SQL | `walk-4b` T1 | ✓ |
| FR-6 Money | `Treatments_And_Money_Are_Written`, `Nothing_Is_Paid_In_The_Future` | `reconcile-money` 0 | `walk-4b` | ✓ |
| FR-6 Documents | `Records_Are_Written` | 4 documents | C7–C9 | ✓ |
| FR-6 Files | `Records_Are_Written` (4 uploads) | 4 files | C2, C10 | ✓ |
| FR-6 Stock and lab | `Records_Are_Written` | low · expiring · 2 suppliers · 1 late | C3–C5 | ✓ |
| FR-6 Worklists | `Records_Are_Written`, `Today_Is_Three_Visits…` | — | C6, S3, `walk-4b` M1 | ✓ |
| FR-6 Journal authors | roles test | audit: Rachel 722 · Monica 341 · Chandler 234 | S11 | ✓ |
| Story: no raw clock | `The_Scenario_Never_Reads_The_Clock` | — | — | ✓ |

**Coverage:** 16/16 (100 %)

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| Implant waiting = « Implant dentaire » done + « Couronne sur implant » pending | catalogue implant is 3 séances | Accepted |
| `verify-schema` plan-step check excludes Withdrawn acts | false drift would fail hosted deploys | Accepted |
| `AppointmentProgressJob` skips a cabinet being built | each nightly build left one paid fiche with no note | Accepted |
| Part A items (see progress.md) | trivial | Accepted |

**Total:** recorded per part in progress.md; none flagged.

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| — | Locator « ≥ 1 patient » not implemented | Yes — the entry request is anonymous, so the tenant filter reads 0 patients and every cabinet would be refused; « admin activated last » already guarantees completeness | Decided (reversible), recorded | 0 |

## Scope Creep Review

No scope creep detected.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 0 |
| Minor | 2 |
| Suggestions | 1 |
| **Total** | 3 |

## Fixed Issues

1. **Minor — FR-6 « a few intra-oral photos » had one photo.** Added `photo-sourire-detail.jpg` (a close-up crop of the same smile) as Jill's second file; tests now expect 4 uploads.
2. **Minor — doctor name and specialty literals duplicated** (« Chandler Bing » composed 3×, `"Dentist"` 2×, « Médecin dentiste » 2×). Now `DemoScenario.SecondDentistFullName` / `DentistSpecialty` and `DoctorSpecialtyLabels.Label(...)`.
3. **Suggestion — AC-2.4 checked on one day only for treatments, records and the no-future rule.** All three are theories over the four anchor days.

## Skipped Issues

1. « non synchronisé » badge on every visit of a cabinet with no Google link — product-wide agenda behaviour with a recorded design choice; filed as `follow-up/agenda-sync-badge-without-google.md`.

## Learnings & Observations

- A seed that books past visits races every job that advances past visits; only a real build shows it.
- The journal labels an account by its e-mail, so the demo shows `rachel.green+<stamp>@demo.apexa.tn` — the stamp exists because yesterday's cabinet still holds the plain address during a rebuild (story 6 territory).
- Probe traps met this story: `/documents` is a template page; patient tabs are nested (address the panel by `-content-<value>`); a field's value is not `innerText`.
- Build baseline: 8 × NU1903 (`System.Security.Cryptography.Xml` advisory) — pre-existing, not this story's.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (none for api/) |
| Type checking | Pass (`dotnet build` 0 errors, no story warnings) |
| Unit tests | Pass — 6 139 passed, 0 failed, unfiltered |
| Integration tests | N/A (no project); real build + `verify-schema` (key-ring only, expected locally) + `reconcile-money` 0 |
| E2E tests | `qa/walk-4c.mjs` GREEN 22/22 (plus 4a, 4b earlier) |
| Device check (mechanical) | N/A (backend-only story) |
| Device review (static, 12a) | N/A (backend-only story) |
| Widths verified in browser | 1440 every screen · 390 on Fichiers |

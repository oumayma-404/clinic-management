# Story 5 Review Report

**Story:** 5 - Documents open, print, and say « SPÉCIMEN » (BE)
**Review Date:** 2026-11-03
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-5: every demo PDF opens, downloads and prints on the letterhead with the mark |
| Acceptance criteria met | 15/15 | AC-5.1, 5.2, 5.5, 5.6 proven here; 5.3 / 5.4 untouched paths, proven by stories 4 / 2 |
| Functional requirements | 10/10 | « SPÉCIMEN — DÉMONSTRATION » + « Document de démonstration, sans valeur » on every page, every renderer incl. the CNAM P 061 overlay |
| Edge cases handled | 5/5 | two visitors behind one address; a forged token; multi-page documents; the limited visit still reads |
| No scope creep | 5/5 | DEV-6 (global window per visit) and the address ceiling are the masking defect and its security follow-on |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | named policy + `[EnableRateLimiting]` like `ArchivePolicy`; capability-chosen registration like the other demo seams; hand-parsed config via `Read` |
| No dead code | 5/5 | — |
| No duplication (DRY) | 10/10 | one `Wait()` behind both French 429 sentences |
| Clean solutions (no hacks) | 10/10 | bearer read without a JWT library (the library read nothing on real tokens) |
| Unit tests | 8/8 | `SpecimenPdfTests` (13, derived over the interface), `DemoPdfRateLimitTests` (14, incl. the real configured limiter), each red-proofed |
| Integration tests | 7/7 | N/A as a project; `qa/walk-5.mjs` on a real demo build stands in (run 2 + review run GREEN) |

## Test Traceability Matrix

| AC ID | Description | Unit Test | Real stack (`walk-5`) | Status |
|-------|-------------|-----------|-----------------------|--------|
| AC-5.1 | preview / Télécharger / Imprimer give the real PDF, letterhead + cachet | `A_Real_Document_Is_Stamped` | P1–P8, P10, E1 | ✓ |
| AC-5.2 | ordonnance aperçu in a fiche works | — | P11 | ✓ |
| AC-5.3 | files open full size | — (path untouched) | story 4 `walk-4c` C10 | ✓ |
| AC-5.4 | « Modifier » opens, save refused | — (path untouched) | story 2 refusal walk | ✓ |
| AC-5.5 | mark + footer on every demo PDF | `Every_Renderer_Is_Stamped_On_Every_Page` (all 6 methods), `A_Real_Document_Is_Stamped` (QuestPDF + P 061) | P1–P11, E1 | ✓ |
| AC-5.5 | no mark elsewhere | `Only_The_Demo_Stamps_Its_Pdfs` (every `DeploymentKind`), `A_Production_Document_Carries_No_Mark` | — | ✓ |
| AC-5.6 | limited per session | `Two_Visitors_Of_The_Same_Dentist_Get_Two_Budgets`, `Before_Authentication_The_Visit_Is_Read_Off_The_Bearer`, `Every_Pdf_Action_Carries_The_Demo_Limit` | R1, R2, R3 | ✓ |

**Coverage:** 6/6 spec ACs (100 %)

## Auto-Approved Deviations

None.

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-5 | Stamp on the finished PDF (wrap `IPdfGenerationService`), not in `DocumentPage` | Yes — the P 061 / BS1 overlays bypass `DocumentPage` and the demo holds an arrêt de travail | Owner told before coding, « go ahead » | 0 |
| DEV-6 | Demo's global API window per visit | Yes — every visitor is one user, so all shared 600/min | Decided (reversible) | 0 |

## Scope Creep Review

No scope creep detected (DEV-6 and the review's address ceiling are defects found by the story's own gates).

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 2 |

## Fixed Issues

1. **Major — a forged token bypassed the demo's global limit.** The visit key is read off the bearer *unvalidated* (the limiter runs before authentication), so each forged token was a fresh 600/min bucket. A forged token never gets a PDF (authentication refuses it after), but it could flood the demo API. The demo's global limiter is now chained with a per-address ceiling (`RateLimiting:Api:DemoAddressPermitLimit`, 3000 per window); `Forged_Visits_From_One_Address_Meet_A_Ceiling` drives the real configured limiter and is red without the chain.
2. **Minor — a 5-line doc comment on `SpecimenStamp`.** Cut to one line + one comment line (comment budget).

## Skipped Issues

None.

## Learnings & Observations

- The rate limiter runs before `UseAuthentication`: every « per user » partition in `RateLimiting.cs` is really per address. Anything keyed on identity at that layer must read the raw credential and then needs an address ceiling, because that read is unvalidated.
- `JwtSecurityTokenHandler.ReadJwtToken` read no claims from this app's real tokens while a hand-built unsigned token worked — test credential readers with a token from the real issuer.
- `/Keywords` is the cheap decisive check that a PDF is stamped; drawn text is glyph-encoded.
- Build baseline: 8 × NU1903 (pre-existing), no warning in a story file.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (none for api/) |
| Type checking | Pass (0 errors, no story warnings) |
| Unit tests | Pass — 6 166 passed, 0 failed, unfiltered |
| Integration tests | N/A (no project); real demo build + `walk-5` |
| E2E tests | `qa/run-5.md` GREEN; review re-run below |
| Device check (mechanical) | N/A (backend-only story) |
| Device review (static, 12a) | N/A (backend-only story) |
| Widths verified in browser | N/A (no rendering change); rendered PDFs read at page size |

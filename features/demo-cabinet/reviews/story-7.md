# Story 7 Review Report

**Story:** 7 — demo.apexa.tn prepared beside production
**Review Date:** 2026-11-04
**Reviewer:** Claude

## Implementation Quality Score: 100/100

### Spec Coherence: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| User stories implemented | 15/15 | US-7 written and rehearsed locally; server steps wait for the owner (by design) |
| Acceptance criteria met | 15/15 | AC-7.1–7.4, 7.6, 7.7 + EC-3 + FR-4 (install) |
| Functional requirements | 10/10 | own DB/role/keys/bucket, caps, same image tag, warnings-only checks |
| Edge cases handled | 5/5 | demo down no longer fails a production deploy (fixed here); manual rebuild on production's tag (fixed here) |
| No scope creep | 5/5 | none |

### Code Quality: 50/50

| Criteria | Score | Notes |
|----------|-------|-------|
| Follows codebase patterns | 10/10 | overlay mirrors `docker-compose.hosted.yml`; workflow keeps its `$COMPOSE` / heredoc style |
| No dead code | 5/5 | |
| No duplication (DRY) | 10/10 | demo detection is one `grep` per remote block (each block is a separate ssh shell) |
| Clean solutions (no hacks) | 10/10 | |
| Unit tests | 8/8 | `DemoDeployWiringTests` 9 cases, read the shipped files |
| Integration tests | 7/7 | N/A in this repo; the local `demo-rehearsal` (20/20) stands in |

## Test Traceability Matrix

| AC ID | Description | Unit Test | Rehearsal | Status |
|-------|-------------|-----------|-----------|--------|
| AC-7.1 | nothing leaves the demo | `DemoDeployWiringTests.The_Demo_Carries_No_Outbound_Integration` | no outbound env | ✓ |
| AC-7.2 | demo session refused by production | `…The_Demo_Has_Its_Own_Identity_Data_And_Files` (issuer, key) | token 401 on prod | ✓ |
| AC-7.3 | never reads prod DB / bucket | `…The_Demo_Has_Its_Own_Identity_Data_And_Files` | role + MinIO user refused | ✓ |
| AC-7.4 | same version as production | `…The_Demo_Runs_Production_Images` (added) | — | ✓ |
| AC-7.6 | failed rebuild visible | `SelfRegistrationGateTests` (`demoBuiltOn`), `DemoCabinetRebuildTests` | `demoBuiltOn` today | ✓ |
| AC-7.7 | not indexed | `…The_Demo_Site_Is_Not_Indexed_And_Offers_No_Install` | noindex · robots · manifest 404 | ✓ |
| EC-3 | cannot starve production | `…Both_Demo_Services_Are_Capped` | caps read back | ✓ |
| — | demo never fails a prod deploy | `…The_Deploy_Never_Waits_On_The_Demo` (added) | — | ✓ |

**Coverage:** 8/8 (100 %)

## Auto-Approved Deviations

| Deviation | Reason | User Feedback |
|-----------|--------|---------------|
| No separate `api-demo`/`web-demo` pull | same image refs `pull api web` fetches | Accepted |
| `DEMO_REBUILD_ON_START` env | runbook « rebuild now » without editing the overlay | Accepted |

## Significant Deviations

| ID | Title | Justified | Approved | Score Impact |
|----|-------|-----------|----------|--------------|
| DEV-10 | demo Caddy site in its own file, mounted only by the overlay | Yes | decided | 0 |
| DEV-11 | demo checks warn, never fail | Yes | decided | 0 |

## Scope Creep Review

None.

## Findings Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Major | 2 |
| Minor | 1 |
| Suggestions | 0 |
| **Total** | 3 |

## Fixed Issues

1. **Major — a broken demo failed the production deploy.** `up -d --wait` waited on every service, demo included,
   so a demo stuck unhealthy (e.g. a secret file with the wrong owner) turned the deploy red and skipped production's
   `verify-schema` and smoke — against DEV-11. Now, with `DEMO_DOMAIN`, `--wait` names production's services only
   (read from the files without the overlay) and the demo starts after with a warning on failure; without
   `DEMO_DOMAIN` the command is unchanged. Guarded by `The_Deploy_Never_Waits_On_The_Demo`.
2. **Major — the runbook's « rebuild now » ran the demo on `:latest`.** With no `CLINIC_IMAGE_PREFIX/TAG` the image
   lines fall back to `latest`, which differs from production after a rollback (AC-7.4). The runbook now reads both
   from `clinic-api-prod` and adds `--no-build`.
3. **Minor — AC-7.4 had no test.** Added `The_Demo_Runs_Production_Images` (derives the expected image lines from
   `docker-compose.registry.yml`).

Owner request handled in the same pass: « Créer mon cabinet (gratuitement) » now opens `https://apexa.tn/#telecharger`
(server default, browser default, overlay `DEMO_SIGNUP_URL`, test, spec).

## Skipped Issues

None.

## Learnings & Observations

- `docker compose up --wait` with an overlay waits on the overlay's services too — anything « optional » beside
  production must be started outside the waited command.
- Backend warning baseline unchanged; none in a story file. Pre-existing and worth its own fix: `NU1903` on
  `System.Security.Cryptography.Xml` 8.0.4 (high-severity advisories) in Infrastructure.

## Quality Check Results

| Check | Result |
|-------|--------|
| Linting | N/A (no linter in `web/`) |
| Type checking | Pass (`tsc --noEmit`) |
| Unit tests | Pass — 6 218 passed, 0 failed, 6 skipped (unfiltered) |
| Integration tests | N/A |
| E2E tests | Skipped (story review) |
| Device check (mechanical) | Pass — `check:responsive` 85/85 |
| Device review (static, 12a) | N/A — no rendering file in story 7 |
| Widths verified in browser | not needed; signup link checked on the local demo (entry + band → `https://apexa.tn/#telecharger`, new tab) |
| `bash -n` on the edited deploy block | Pass; `config --services` lists production's 9 services without the overlay, 11 with it |

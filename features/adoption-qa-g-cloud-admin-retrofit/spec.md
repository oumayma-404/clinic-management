# Spec: Adoption QA — G (Cloud admin retrofit)

**Status:** APPROVED
**Type:** Small (forced follow-up — deferred from Batch E)
**Created:** 2026-07-24
**Scope:** BE
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Give **existing** Cloud clinics an admin. Batch E fixed *new* clinic creation (creator → admin); clinics created before that fix are still stuck with zero admins, so their admin-gated features remain unreachable.

## Overview
Cloud onboarding used to stamp the creator as `doctor`/`secretary`, never `admin`, so any Cloud clinic created before Batch E has no admin and its user-management / catalog-write / reminder / backup surfaces are dead. This repairs those clinics by promoting their earliest (creator) user to `admin` — idempotently, touching only clinics that currently have **no** admin.

## Design decision (recommended)
**Idempotent startup backfill** (`IClinicAdminBackfill.BackfillAsync`) invoked from the Cloud startup path right after the existing `IClinicCatalogSeeder.SeedAllClinicsAsync()` — self-healing, no operator step, mirrors the seeder pattern. Rejected alternative: a manual console verb (`promote-clinic-admin`) — kept as a possible add-on but not the primary. **No web endpoint** that lets a non-admin self-promote (would defeat the gate). Confirm at approval.

## What Changes
- New `IClinicAdminBackfill`/`ClinicAdminBackfill` (Infrastructure): for each clinic with zero `IsActive` admin users, promote the earliest-`CreatedAt` user to `Role = "admin"` (DB) and push the role to Auth0 `app_metadata` (`IAuth0ManagementService`, best-effort/swallow like elsewhere). Runs with no clinic in scope (`IgnoreQueryFilters` / unfiltered `User`), Cloud-only.
- Wired into `Program.cs` Cloud startup (after catalog seeding). Local unaffected (already mints an admin).
- Structured log of how many clinics were repaired (or none).

## Out of Scope
- A UI/endpoint to reassign admin among members (separate role-management feature).
- Any change to `CreateClinicCommand` / `JoinClinicCommand` (Batch E already covers new clinics).
- Local mode (no-op; every Local clinic already has an admin).

## Edge Cases (Critical only)
- Idempotent: a second run finds every clinic already has an admin → promotes nobody.
- A clinic with **no** users at all (orphan) → skip, log, don't crash.
- Auth0 push failure must not fail startup or the DB promotion (best-effort, logged) — matches the existing metadata-update convention.

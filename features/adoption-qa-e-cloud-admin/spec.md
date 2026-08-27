# Spec: Adoption QA — Batch E (Cloud admin gap)

**Status:** APPROVED
**Type:** Small (forced multi-item pass — user fed the full adoption-QA blueprint)
**Created:** 2026-07-24
**Scope:** BE
**Branch:** feature/windows-desktop-app (existing)
**Feature:** Give Cloud clinics a real admin. The docs-vs-reality gap from the report: `CreateClinicCommand` stamps the Cloud creator as `"doctor"`, so every admin-gated feature (user management, catalog/reminder writes, backup, WhatsApp connect) is unreachable in Cloud.

## Overview
In Cloud mode the clinic creator is assigned their selected role (`doctor`/`secretary`) and never `admin` (`CreateClinicCommand.cs:166-168`), so no Cloud clinic ever has an admin — the entire admin surface is dead. Local is unaffected (first-run mints an `admin` at `:319`). This batch makes the first user of a new Cloud clinic an admin, matching the Local pattern. P2: the offline-Windows target (Local) already works, so this only matters if Cloud is in scope.

## What Changes
- **E1 — First Cloud clinic user = admin.** In the Cloud branch of `CreateClinicCommand` (`:166-178`), the creating user of a **new** clinic gets role `"admin"` (keeping their practitioner identity where applicable), mirroring `CreateLocalFirstRunAsync` (`:319`). The `app_metadata` role pushed to Auth0 (`:236`) follows.

## Acceptance Criteria
- **AC-1:** Creating a new clinic in Cloud mode assigns the creator the `"admin"` role in the DB user record.
- **AC-2:** That creator can reach admin-gated endpoints (users, catalog writes, reminder settings, backup) without a 403, and the FE admin surfaces render.
- **AC-3:** Subsequent users joining an existing clinic keep their selected non-admin role (only the first/creator becomes admin).
- **AC-4:** Local first-run behavior is unchanged (still mints `admin`).

## Out of Scope
- Retrofitting existing Cloud clinics that were created without an admin (data migration / admin-promotion tool — separate).
- A general role-management/promotion UI beyond what already exists.

## Edge Cases (Critical only)
- A creator who selected "secretary" as their practitioner role still becomes `admin` for the clinic (admin is an authorization role, not a clinical one) — confirm this matches the Local rationale comment (`:322-325`).

# Feature Specification: Single-Dentist Practitioner Identity

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** Full
**Feature:** Make the admin-is-also-the-practitioner (single-dentist) setup resolve its own doctor everywhere, and stop persisting nameless doctors at setup.

## Overview
The new setup flow creates the cabinet's practitioner as a user with role `admin` plus a linked `Doctor`. But the frontend only resolves "the current user's doctor" when `role === 'doctor'`, so on every default single-dentist install `currentUserDoctor` is permanently `null` — the certificat ordre field stays blank with a false "no ordre on your profile" message, the practitioner isn't pre-selected on appointments, and AI chat sends no `doctorId`. Separately, the setup branch validates only `Specialty`, so it can persist a `Doctor` with an empty name.

## What Changes
- `useDoctors` resolves `currentUserDoctor` for any current user who has a linked `Doctor` record — matched by the doctor's linked user id first, email as fallback — regardless of role (so an `admin` practitioner resolves).
- `CreateClinicCommand`'s single-dentist branch requires non-empty first and last name (in addition to `Specialty`), consistent with the Cloud `CreateClinic` and `JoinClinic` paths; it never persists a `Doctor` with an empty `FullName`.

## Acceptance Criteria
- **AC-1:** On a single-dentist install (admin role, linked `Doctor`), `currentUserDoctor` resolves to that doctor; the Certificat CNOMDT ordre field pre-fills from the profile and the "Aucun numéro d'ordre sur votre profil" message is not shown when an ordre exists.
- **AC-2:** The admin-practitioner is pre-selected as the default doctor in the create-appointment dialog, and AI chat sends their `doctorId`.
- **AC-3:** A `/auth/setup` request whose `doctorInfo` lacks a first or last name does not persist a nameless `Doctor` (rejected with a clear failure).
- **AC-4:** A non-practitioner admin (no linked doctor) still resolves `currentUserDoctor = null` with no regression, and a real `doctor`-role user resolves as before.

## Out of Scope
- Persisting an issuing-doctor FK on documents/appointments (multi-doctor cabinets).
- Any change to the Cloud/Auth0 identity path.

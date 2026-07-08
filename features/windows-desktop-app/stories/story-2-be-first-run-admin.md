# Story 2 (BE): First-run clinic + admin creation (localhost-only)

**Status:** implemented
**Layer:** BE
**Depends On:** 1

## Objective
On a fresh Local install, allow the person at the **server PC (localhost only)** to create the clinic and the first user as **admin** with an email+password — the bootstrap that makes login (Story 1) usable.

_From spec:_ AC-1.2, AC-1.2a; FR-B3.

## Entry criteria
- Story 1 done (local auth + `User` credential fields exist).
- Fresh Local DB (no admin yet).

## Steps
1. Extend `CreateClinicCommand` (Local mode): accept a password, hash it via `LocalAuthService`, set the creator's role to **admin**, and mint the `local|{guid}` user id.
2. Add a **first-run gate**: the create/setup endpoint is allowed only when (a) the request originates from `localhost` AND (b) no admin/user exists yet; otherwise reject (403).
3. Ensure the generated clinic `Code` is persisted for later staff self-registration.
4. Keep Cloud-mode `CreateClinicCommand` behavior unchanged (no password, existing Auth0 flow).

## Files to create/modify
- `api/.../Application/Features/Clinics/Commands/CreateClinicCommand.cs` — Local admin+password path.
- `api/.../API/Controllers/ClinicsController.cs` (or a dedicated setup endpoint) — localhost + no-admin-exists gate.
- Small helper to detect localhost + "no users exist".

## Verification steps
- Fresh DB, request from localhost → clinic + admin created; admin can then log in (Story 1).
- Same request from a non-localhost origin → 403.
- After an admin exists, the first-run endpoint is closed → 403.
- Cloud-mode clinic creation unchanged.

## Exit criteria
- A fresh Local install can be provisioned with a clinic + admin only from the server PC, and that admin can log in offline.

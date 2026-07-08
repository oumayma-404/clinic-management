# Story 4 (BE): Staff self-registration API (clinic code)

**Status:** implemented
**Layer:** BE
**Depends On:** 1

## Objective
Let a staff member create their own local account via the API by presenting the clinic code + credentials, so additional users can join offline without an admin pre-creating them.

_From spec:_ AC-4.2, AC-4.3, AC-4.4; FR-B4.

## Entry criteria
- Story 1 done (local auth + credential fields).
- A clinic with a `Code` exists (Story 2).

## Steps
1. Extend `JoinClinicCommand` (Local mode): accept email, password, full name, role (doctor/secretary), optional doctor info, and the clinic code.
2. Validate: clinic code exists (else reject); email not already used in this install (partial unique index + handler check); role ∈ {doctor, secretary} — **admin not self-assignable**.
3. Create the local `User` (hashed password, `local|{guid}` id, active) + optional linked `Doctor`.
4. Keep Cloud-mode join behavior unchanged.

## Files to create/modify
- `api/.../Application/Features/Clinics/Commands/JoinClinicCommand.cs` — Local credentials path + validations.
- `api/.../API/Controllers/ClinicsController.cs` — expose join for Local mode (unauthenticated pre-account).
- Reuse `LocalAuthService` for hashing.

## Verification steps
- Valid code + new email → account created; user can log in (Story 1).
- Invalid code → rejected; duplicate email → rejected; role=admin attempt → rejected/ignored.
- Cloud-mode join unchanged.

## Exit criteria
- Staff can self-register a working local account via the API using the clinic code.

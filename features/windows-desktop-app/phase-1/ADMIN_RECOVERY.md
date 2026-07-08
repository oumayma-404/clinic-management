# Admin Lockout Recovery (Local / Offline mode)

> Offline recovery path for a clinic running the app in **Local mode** (`Auth:Mode=Local`).
> Use this when **no administrator can log in** — forgotten admin password, or the admin account
> got locked out — and there is no email/cloud reset (the app runs entirely on the clinic LAN).

This is the Local-mode counterpart of a cloud "forgot password" e-mail. It resets the administrator's
password to a fresh, one-time temporary password and forces the admin to choose a new one at next login.

## Prerequisites

- Run it **on the SERVER PC** — the machine that hosts the API and PostgreSQL. The utility connects
  directly to the local database using the server's `appsettings.json` connection string, so it must
  run where that database is reachable. It is not exposed over the network and there is no web endpoint
  for it.
- The server must be configured for Local mode (`Auth:Mode=Local`). In Cloud mode the utility refuses
  to run — cloud deployments reset passwords through Auth0.

## Procedure

1. Open a terminal on the server PC, in the API project folder
   (`api/ClinicManagement.API`, the same folder you start the server from).

2. Run the recovery command:

   ```bash
   # Reset the sole administrator (works when there is exactly one admin account):
   dotnet run --project ClinicManagement.API -- reset-admin-password

   # Or target a specific administrator by email (required if more than one admin exists):
   dotnet run --project ClinicManagement.API -- reset-admin-password admin@clinic.com
   ```

   > If you run the packaged/published server (`ClinicManagement.API.exe`), pass the same arguments:
   > `ClinicManagement.API.exe reset-admin-password [admin-email]`.

3. On success the utility prints the account and a **temporary password**, for example:

   ```
   Administrator password reset successfully.
     Account:            admin@clinic.com
     Temporary password: Kd7mRq2xTb9n

   Give this password to the administrator. They will be required to
   choose a new one the next time they log in.
   ```

4. Give the temporary password to the administrator. They log in with it on any clinic PC, and the app
   immediately forces them to set a new password (the forced-change screen). The reset also clears any
   active lockout, so a locked-out admin can log in straight away.

## Failure messages

The command prints a clear error and exits without changing anything when:

- **Cloud mode** — `Auth:Mode` is not `Local`.
- **No admin found** — no administrator account exists, or the given email doesn't match a local account.
- **Not an administrator** — the given email belongs to a doctor/secretary (this utility only recovers admins).
- **Multiple admins, no email** — more than one admin exists; re-run with the target admin's email.

## How it works (for maintainers)

- CLI entry: `api/ClinicManagement.API/Maintenance/AdminPasswordResetCommand.cs` — intercepted at the top
  of `Program.cs` before the web host starts, so the process runs one-shot and exits with code `0`
  (success) or `1` (failure).
- Core logic: `api/ClinicManagement.Application/Common/Maintenance/AdminPasswordRecoveryService.cs` —
  reuses `ILocalAuthService` to generate + hash the temporary password and `User.SetPassword(hash,
  mustChangePassword: true)` (which also clears failed-attempt count and lockout). It is intentionally
  **not** registered in DI, so it can never be injected into an HTTP handler and reset an admin without
  authentication.

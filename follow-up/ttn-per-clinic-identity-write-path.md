# A write path for the per-clinic TTN identity (`set-clinic-ttn-identity` verb)

> **Type:** incomplete
> **Priority:** high
> **Created:** 2026-08-06
> **Feature:** multi-tenant-cloud (US-4 / Part D — review finding 7)

## Summary
Part D moved the El Fatoora signing identity from per-install to per-clinic and **deliberately shipped no way to
populate it**. Combined with DEV-19 — which removed the per-install fall-back from `CloudBrowser` as well as
`HostedMultiTenant` — that means e-invoicing in an **already-shipped `CloudBrowser` deployment stops permanently the
moment this branch lands**, with no in-product remedy: the only way to install an identity is direct SQL against
`Clinics` plus a hand-placed blob in object storage.

## Current State
- `Clinic` carries `TtnUsername`, `TtnApiSecretEncrypted`, `TtnCertificateKey`, `TtnCertificatePasswordEncrypted`,
  set through `Clinic.SetTtnIdentity` (which refuses half an identity).
- **`SetTtnIdentity` has no caller.** No endpoint, no console verb, no admin screen.
- `TtnIdentityProvider.ResolveAsync` falls back to the per-install certificate **only** where
  `DeploymentProfile.SharesInstallWideTtnIdentity` holds, i.e. `SelfHostedLan` alone.
- `EInvoiceService` now *parks* the invoice on an identity refusal instead of burning its five attempts (finding 6
  fix, applied), so the note stays `Queued` and visible in `GET /api/outbox` indefinitely — recoverable, but only
  once an identity exists.
- `verify-schema`'s `ttn-identity-is-complete` is the only guard a hand-populated row has.

## Expected State
An operator can install a clinic's identity without touching the database.

## Chosen approach — a console verb, not an endpoint
Add **`set-clinic-ttn-identity`** to `api/ClinicManagement.API/Maintenance/`, on `ProvisionClinicCommand`'s template:

```
dotnet ClinicManagement.API.dll set-clinic-ttn-identity \
    --clinic-id <guid> --pfx <path> --pfx-password <secret> \
    [--ttn-username <matricule>] [--ttn-secret <secret>]
```

1. `InstallConfiguration.BuildForConsoleVerb()`, then `MaintenanceDatabase.HasConnectionString` (the M3 gate — this
   verb needs a connection string, not `pg_dump`, so it must **not** gate on `HasLocalDbTooling`).
2. Container from `AddInfrastructure` alone; `IAuditActorProvider.RunAs(CommandName)`; `ITenantScope.UseClinic(id)`
   **before** any write.
3. Upload the PFX through `IFileStorage.UploadAsync(stream, contentType, clinicId, path)` so the key is composed by
   `ClinicStorageKey` like every other blob.
4. Encrypt both secrets through `ITtnSecretProtector` — this layer must never see plaintext reach the row.
5. `Clinic.SetTtnIdentity(...)` + `IUnitOfWork.SaveChangesAsync`.
6. Re-resolve through `ITtnIdentityProvider` and open the certificate once, so a wrong password is reported **at
   provisioning time** rather than on the first invoice. (`XadesEInvoiceSigner.LoadCertificate` now raises a French
   `TtnIdentityUnavailableException` for exactly this, so the message is already written.)

### Why this and not the alternatives
- **An admin endpoint + settings screen** (`AdminOnly`, multipart upload) is the better long-term surface but is a
  *feature*: a new controller action, a file-upload UI with the device contract's gate, and per-clinic secret fields
  on a settings screen. Part D's own note puts the certificate **upload** with Part E's storage work for that reason.
  The verb is what makes the deployment recoverable now; the screen can replace it later without changing the domain.
- **Restoring `CloudBrowser`'s per-install fall-back** would fix the regression in one line and is **spec-violating**:
  DEV-19 removed it deliberately and was « asked and approved », because `CloudBrowser` is multi-clinic and a TEIF
  signature attests *who issued* the invoice — sharing one qualified identity across clinics is a false legal
  declaration that TTN validation makes irreversible. **Do not do this.**

## Key Files
| File | Purpose |
|------|---------|
| `api/ClinicManagement.API/Maintenance/ProvisionClinicCommand.cs` | the template to copy (gates, scope, audit actor) |
| `api/ClinicManagement.API/Maintenance/MaintenanceDatabase.cs` | the connection-string gate |
| `api/ClinicManagement.API/Program.cs` | verb interception, before the web host boots |
| `api/ClinicManagement.Domain/Entities/Clinic.cs` | `SetTtnIdentity` (refuses half an identity) |
| `api/ClinicManagement.Infrastructure/Services/TtnIdentityProvider.cs` | precedence + the refusals |
| `api/ClinicManagement.Infrastructure/Security/TtnSecretProtector.cs` | `ITtnSecretProtector` |
| `deploy/README.md` | the hosted operator view — needs the migration note below |

## Why Deferred
It is **new operator-facing functionality**, not a fix to written code, and Part D's scope excluded the write path by
an approved decision. It also cannot be verified in this repo: the feature's operator gate has never run, and a
verb that uploads a real PFX and signs with it needs a live database, live object storage and a genuine qualified
certificate.

## Still needs validating
- A real PFX round-trip: upload → `IFileStorage.DownloadAsync` → `X509Certificate2` → a signed TEIF that TTN's
  sandbox accepts. Nothing in the unit-test project can touch any of those.
- **The `CloudBrowser` migration note in `deploy/README.md`**: an existing Cloud deployment must run this verb for
  every clinic *before* upgrading, or its e-invoice outbox parks on the first dispatch after the deploy. That note is
  owed whether or not the verb ships first, and `GET /api/outbox` is where the backlog will show.

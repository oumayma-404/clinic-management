# The hosted security layer is running reduced on Render's free tier — restore it on a real VM

**From:** `features/hosted-security-hardening/` Parts 2 (Transit) and 3 (Custody)
**Raised:** 2026-08-12 · **Scope:** configuration + two narrow code seams
**Decided with the user:** ship on the free tier now, bring the full layer back when the deployment moves to
paid virtual-machine hosting.

## Why this exists

The hardening was designed against `deploy/docker-compose.hosted.yml`, where the operator controls the host and
can **mount files**: an internal CA at `/certs/ca.crt`, a PKCS#12 for the key ring, a durable volume for the ring
itself. Render's free tier offers none of those — no persistent disk, no file mount, and
[no published CA certificate for its managed PostgreSQL](https://render.com/docs/postgresql-creating-connecting)
(its docs say external connections use "Render-managed TLS certificates" and say nothing about internal ones).

So three of Part 2/3's guarantees could not be met as written. Each was handled differently, and **only one is a
genuine reduction**. The other two are permanent improvements that happen to have been forced by this host.

## 1. ⚠️ THE REDUCTION — restore this first

`Security:AllowUnverifiedInternalTls=true` makes `TransportAssurance` accept `SSL Mode=Require` for the database
instead of `VerifyFull`.

| | |
|---|---|
| **Kept** | The hop is still **encrypted**. A passive tap on the network reads nothing. |
| **Given up** | **Identity.** `Require` accepts whatever certificate it is handed, so an impostor between the application and the database would not be detected. |
| **Never given up** | `Disable`, `Allow` and `Prefer` are still refused *with the flag set* — « rien en clair sur le réseau interne » is not the promise being traded. |

It is opt-in, non-default, and `Program.cs` logs a French warning naming the key on **every** boot, so it cannot
become the forgotten default the way `Security:EnforceCsp` did for a whole release.

**To restore, on a host that can mount a file:**

1. Obtain the database's CA certificate and mount it (on a VM: the same `internal_certs` volume the compose files
   already use; on a managed database: the provider's published CA).
2. Change the connection string to `…;SSL Mode=VerifyFull;Root Certificate=/certs/ca.crt`.
3. **Delete `Security__AllowUnverifiedInternalTls`.** The startup warning disappears; that is the signal.
4. Same for the object store if one is configured: `MinIO__RootCertificate=/certs/ca.crt`.

⚠️ **Do not simply set `VerifyFull` without the CA file.** The startup check would pass and the *connection*
would fail instead — moving a clear refusal at boot into an obscure failure on the first query.

## 2. The key ring moved into PostgreSQL — a real improvement, keep it

`DataProtection:PersistToDatabase=true` puts the Data Protection key ring in the database rather than on a
volume, because on a free tier the database is the only durable thing there is.

**This is not a reduction.** The rows are still encrypted by the deployment's certificate
(`DataProtection:CertificateBase64`), exactly as they were on the volume — a database dump does not disclose the
ring. What it fixes is worse than what it costs: with an ephemeral ring, `RequiresAdminSecondFactor` being true
on `HostedMultiTenant` means **every administrator's TOTP secret dies on redeploy**, locking them out of their
own cabinet.

**On a VM you may keep it or move back to a volume.** If you move back, it is a real migration, not a config
flip: run `reprotect-secrets --rotate` under the new arrangement *before* deleting anything, and confirm
`verify-schema`'s `secrets-protected-under-current-ring` reads zero. Naming both a database and a directory is
refused at startup, deliberately.

## 3. Two things that were plainly wrong and are now fixed — keep both

- **`DataProtection:CertificateBase64`** — the key-ring certificate could previously arrive *only* as a file
  path, which no managed platform can provide. Both routes now converge on one parse with identical checks.
- **The object-store transit check is skipped when no object store is configured.** It used to refuse startup
  over the transit of a connection the deployment never opens, while `AddInfrastructure` already registered a
  storage stub for exactly that state.

## What is still owed regardless of host

- **The key ring's certificate is self-signed and generated on a developer laptop** (`clinic-keys/`, 2036
  expiry). On a real deployment, decide where it is *custodied* — `deploy/KEY-CUSTODY.md` § 1 — and back it up
  apart from the database. Losing it makes every 2FA secret and every clinic's reminder credentials unreadable.
- **No restore drill has been performed.** `deploy/RESTORE-DRILL.md` says so in place of an empty table, and
  that remains true.
- **The two `verify-schema` checks that read the key ring** (`key-ring-protection`,
  `secrets-protected-under-current-ring`) have not been run against this deployment. They are the only thing
  that says the certificate protection is actually in force rather than merely configured.

## Checklist for the move to a VM

- [ ] Mount the database CA; connection string to `SSL Mode=VerifyFull;Root Certificate=…`
- [ ] Delete `Security__AllowUnverifiedInternalTls`; confirm the boot warning is gone
- [ ] `MinIO__UseSSL=true` + `MinIO__RootCertificate` if an object store is configured
- [ ] Decide the key ring's home (database is fine); if moving, `reprotect-secrets --rotate` first
- [ ] Custody the PKCS#12 properly and remove it from any developer machine
- [ ] Run `verify-schema` and diff against the last capture
- [ ] Perform the first restore drill and write it down

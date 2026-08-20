# clinic-self-signup — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A clinic can let itself in, and nothing exists until the email is answered (`clinic-self-signup`)

a hosted
clinic used to exist only because an operator ran `provision-clinic`. `POST /api/auth/signup` (anonymous) writes a
pending **`ClinicSignup`** and emails a link; `POST /api/auth/signup/verify` consumes it and provisions the clinic +
admin through the **existing** `LocalClinicProvisioning.ProvisionAsync` — its third caller, and it needed no change.
Gated on the 15th capability, **`DeploymentProfile.AllowsPublicClinicSignup`** (`HostedMultiTenant` ✓ only), reported
to the browser as `publicSignupEnabled` on `GET /api/auth/mode`; pages `/signup` + `/signup/verifier`.
⚠️ **It does not reopen the door US-3 closed, and the two capabilities are opposite questions.**
`AllowsSelfRegistration` is « may a stranger *join an existing clinic* with its six-character code? » — a shared
password everyone who ever worked there knows — and stays **`false`**. This one hands out no shared secret: the gate
is a fresh 32-byte CSPRNG token (`RandomNumberGenerator`), SHA-256 in the row and plaintext **only in the email**,
single-use and 24 h. Reading either flag as the other is a security decision made by accident.
⚠️ **`ClinicSignup` carries no `ClinicId`** — a signup exists precisely because its clinic does not — so it is outside
the EF tenant filter *by construction* and needs no `TenantScopeFilterTests` entry, whose clinic-owned set is derived
from that very column. It is also the one table with **no FK and nothing that cascades it away**, which is why the
purge is opportunistic on the signup path (no new job) and why `verify-schema` gained
**`clinic-signup-has-no-orphans`**.
⚠️ **The response is byte-identical whether the address is free, already an account, or already pending** — one
neutral French sentence, so the endpoint is not an enumeration oracle; only the password-length rule refuses
differently, and a length rule is a fact about what was typed. Verification's four failure causes (expired, unknown,
malformed, **now-taken**) share **one** refusal, and the now-taken case still *spends* the row.
⚠️ **Verification issues no session** — no token, no cookie: receiving an email is not knowing the password, and the
password is the credential the visitor already chose. The admin is created `IsActive` with `MustChangePassword`
**false**, unlike `provision-clinic`'s printed one-time password.
⚠️ **`ITransactionalEmailSender`/`SmtpTransactionalEmailSender` is the first email path bound to no clinic**, and it
reads the per-install `Notification:Smtp:*` (`SmtpConfig`) rather than `ResolvedReminderSettings` — those resolve
*per clinic*, and there is none. Routing it through `IReminderSettingsProvider` would compile and stop working. It is
deliberately **not** an outbox either (every queue keys on `ClinicId`, and the visitor is waiting): an unconfigured
host is a French refusal **before** anything is written, never a 202 over an email nobody can send. The link comes
from `FrontendUrl` via `IPublicAppUrlProvider` — an Application-side seam because that project references no
configuration package at all. No new config key.

# multi-tenant-cloud — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Three deployment topologies, one capability per question (`multi-tenant-cloud` US-1 / Part A)

`Deployment:Profile`
resolves to a **`DeploymentProfile`** — a `DeploymentKind` plus **15** named capabilities — and every mode branch in
the solution asks one of them. It replaced `LocalAuthConfig.IsLocalMode`, a single boolean answering a dozen
unrelated questions at ~30 call sites; two profiles happened to agree on all of them, so one flag sufficed, and a
third does not. Absent ⇒ derived from `Auth:Mode` (`Local` → `SelfHostedLan`, else `CloudBrowser`); an
**unrecognised value fails startup loud**, because falling back would hand a hosted deployment Auth0 login on a typo.

| Kind | What it is | Status |
|---|---|---|
| `SelfHostedLan` | the clinic's own Windows PC serving its LAN: its data, its disk, its self-signed certificate, local accounts | **built** (`windows-desktop-app`, 5 phases) |
| `HostedMultiTenant` | **one hosted backend serving many clinics**, each reached over the internet, on the product's own accounts — no Auth0 | **built** (`multi-tenant-cloud`, Parts A–C + F; D/E outstanding) |
| `CloudBrowser` | one hosted backend reached by a browser, with Auth0 as the identity provider | **built** (the original path) |

⚠️ **`HostedMultiTenant` runs with `AUTH_MODE=local`** — it owns its accounts — so anything that asked « is this
Local? » to mean « is this a clinic's own PC? » was already wrong there. That is the whole reason the profile exists,
and `DeploymentProfileTests` asserts the two shipped kinds reproduce the old `IsLocalMode` truth table exactly.
Deploy assets: `deploy/docker-compose.hosted.yml` + `deploy/.env.hosted.example`; operator guide in
[`deploy/README.md`](deploy/README.md).

## The hosted runtime can be watched, and it cannot race itself (`multi-tenant-cloud` US-6 / Part F)

five hardenings
that share one premise — **in a datacentre nobody is looking at the console.**
- **`GET /health`** (anonymous, un-rate-limited, every profile) answers what a TCP probe cannot: the database round
  trips, and the file storage is reachable through the new `IFileStorage.ProbeAsync`. ⚠️ **Storage down is `Degraded`
  (200), not `Unhealthy`** — a clinic with no object storage still books, records and collects; grading it unhealthy
  would pull every instance out of rotation and turn a partial outage into a total one, and restarting the API does
  not bring MinIO back. The body carries check **names and statuses only**; reasons go to the log, never to an
  anonymous caller.
- **`GET /api/outbox`** (`AdminOnly`) reports the depth of the three background queues — reminders (pending / **due**
  / blocked / failed) and document emails — each with **the age of its oldest waiting row**. ⚠️ That age is
  the diagnosis, not the count: « 40 pending » is meaningless when a reminder for next Tuesday is *supposed* to be
  waiting, while a row due three hours ago says the dispatcher is not running. It exists because `/hangfire` is
  loopback-only in **both** modes and behind a reverse proxy every request arrives from the proxy container — correct
  as security, total as blindness — and because US-2's stated risk (R-1) is that a job with no tenant scope reads
  **nothing** and logs a clean run. Each queue's predicates are **copies of its own dispatcher's**, clause for
  clause, or the read would report a backlog nothing will ever drain.
- **The login limiter is keyed on the submitted account**, with the address as a second and looser ceiling. Per
  address alone is a lockout waiting to happen once a deployment is reached over the internet: a whole practice
  arrives through **one** public NAT address, so one colleague mistyping ten times spent everybody's budget.
  ⚠️ It could not be a compound `account+address` key — that hands one attacker a fresh budget per address — so the
  named policy partitions on the account while the global limiter partitions the same request on its address
  (`RateLimiting.IsAnonymousAuthPath`). The email is lifted out of the body by `AuthAttemptAccount` **before** the
  limiter, since the partitioner is synchronous and runs long before model binding; **anything unreadable falls back
  to the address**, so `auth/refresh` and a malformed body are bounded exactly as they were before.
- **`Security:EnforceCsp`** promotes the CSP from report-only to enforcing. Default **false in every profile** and
  deliberately *not* derived from the kind: what makes enforcing safe is that somebody walked these pages in this
  deployment. (Checked against Next's own policy first — `web/next.config.ts` emits **no** CSP in either branch, so
  there is nothing to intersect with.)
- **`MigrationLock`** wraps the startup migrate-and-backfill block in a PostgreSQL **session-level advisory lock**:
  EF Core 8 takes none, so two containers starting together both apply the same migrations and the loser fails
  part-way, leaving a schema that is neither old nor new. Advisory rather than a lock table (a table would need the
  migration it protects, and a crashed holder would wedge the next deploy for ever); ⚠️ **`pg_advisory_lock`, never
  the `xact` variant**, which would release at the first commit *inside* the migration.
- **`DataProtection:KeyRingPath` is required in `HostedMultiTenant` and fails startup without it.** The framework
  fallback is per-instance and ephemeral: it works, and then the first redeploy replaces the ring, so every clinic's
  encrypted reminder credentials become undecryptable and each channel reports « non configuré » with
  nothing in any log tying that to a deployment. A path with **no durable volume** behind it produces the identical
  symptom, which no code can detect — that half is stated beside the volume in the compose file.
- **The three read-only/recovery verbs gate on the connection string, not the profile** (amendment M3):
  `verify-schema`, `reconcile-money` and `reset-admin-password` run no PostgreSQL binary, so `HasLocalDbTooling` was
  the wrong question. It mattered twice — `verify-schema` is the **only** gate a schema change has anywhere in this
  product, and a hosted clinic's locked-out admin had no recovery once `provision-clinic` could create one.
  ⚠️ **`restore-backup` keeps its profile gate**, because its safety interlock (« refuse while the app is
  listening ») looks for a listener on *this* machine and in a container the API listens in a sibling — so the check
  would pass silently while `pg_restore --clean` drops tables under a live application.

## Every blob knows whose it is (`multi-tenant-cloud` US-5 / Part E)

new storage keys are
**`clinics/{clinicId}/…`**, composed in exactly one place — `Infrastructure/Storage/ClinicStorageKey` — for
**both** backends. The defect was not only the flat keys: « which clinic owns this blob » had **two** answers.
Four upload sites prefixed a path of their own with a bare `{clinicId}/` (the logo, a doctor's cachet, and the
two artifacts of the electronic-invoicing subsystem of the day) while four wrote `{guid}-{timestamp}` with no
clinic in it at all — the patient files and
the three PDF paths — so on a hosted backend most of the object store was one undifferentiated pile, and a
third convention was one new upload away.
⚠️ **The enforcement is the signature, not a convention**: both `IFileStorage.UploadAsync` overloads now
**require** a `Guid clinicId`, so an unprefixed key is not something a caller can write, and the second
overload's path is **relative to the clinic** (adding a clinic segment of your own yields
`clinics/{id}/{id}/logo`). `ClinicStorageKeyTests` derives that off the interface rather than listing today's
overloads — a third overload with no clinic id fails it, which was checked by adding one.
⚠️ **The clinic is a parameter rather than something the storage reads off `ITenantScope`.** The tempting
version works for every HTTP path and fails silently for the one that matters: an outbox job uploads under
**`UseSystemWide`**, where there is no clinic in scope at all.
⚠️ **Reading is deliberately asymmetrical — there is no backfill (amendment M2).** `DownloadAsync`/`DeleteAsync`
take the stored key **verbatim**, so every pre-Part-E row keeps resolving; composing on the read side would
strand all of them. That is also why the plan's pitfall was a *verification* task rather than a code change:
`PdfGenerationService` loads a practitioner's cachet from the `doctorCachetKey` snapshotted into the document's
`ContentJson`, and a key-format assumption there fails **silently** — the renderer falls back to a plain
signature line. It reads the stored value and was left alone.
⚠️ One consequence worth knowing: the logo and cachet keys were **deterministic**, so a re-upload used to
overwrite in place. It now lands on a new key, which is why `UpdateDoctorProfileCommand` gained a post-commit
best-effort delete of the superseded blob (the logo path already deleted the old key before uploading).

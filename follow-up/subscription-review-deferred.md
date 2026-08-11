# Deferred from the `clinic-subscription` review pass

Captured 2026-08-11 while applying `features/clinic-subscription/reviews/feature-review.md`. Forty-eight of its
fifty-two findings were fixed in that pass; these four were not, each for a stated reason, and each with the
remedy already chosen so the next reader does not have to re-do the analysis.

---

## 1. Warning-row change detection compares two French sentences (review finding 41, Suggestion)

`NotificationGenerator.EnsureSubscriptionWarningAsync` decides whether a cabinet's end date has moved by comparing
`existing.Message` against the message it would write. It works today and is documented as deliberate — the message
carries the end date and no countdown, so it is stable day to day and differs exactly when a grant moved the date.

**Why deferred, not applied.** Recovering an outcome by matching prose is a defect class this repo deleted once
(`Contains("déjà facturée")`), so the concern is real — but the remedy needs a **schema change**: a nullable
`StaffNotifications.EndsOn`, a migration, and a `verify-schema` line, which is well outside a review-fix pass.
The *live* symptom the finding describes (rows naming two different dates at once) was fixed in this pass by
finding 16's `WithdrawStaleSubscriptionWarningsAsync`, so what remains is hardening against a future rewording.

**Chosen remedy.** Add `DateTime? EndsOn` to `StaffNotification`, written by `ForSubscription` beside
`SubscriptionThresholdDays`, and make the restate condition `existing.EndsOn != endsOn`. Reject the alternatives:
a message *prefix* comparison is the same defect with a shorter string, and hashing the date into the row's title
hides a real value behind an opaque one. Trade-off to accept: one more nullable column on a table that is mostly
about appointments and stock — justified because the column that makes the dedupe work (`SubscriptionThresholdDays`)
is already there for exactly the same reason, and the pair reads as one mechanism.

---

## 2. `subscription-report` has no notion of a repeat trial (review finding 42, Suggestion)

A cabinet can reset its 30 free days by exporting its patients (a GET, always allowed), signing up again with a
fresh e-mail address, and importing the CSV into the new cabinet while it is inside its trial. Nothing links a new
signup to an existing or expired cabinet, and nothing caps trials per phone or clinic name.

**Why deferred, not applied.** `features/clinic-subscription/spec.md`'s Out of Scope excludes it explicitly —
*"Changes to public self-signup itself, beyond stating the trial (AC-1.3)"* — so adding signup caps here would be
implementing a spec amendment under cover of a review fix. It is also self-limiting: the practice loses every
appointment, invoice and document, with no merge and no migration path.

**Chosen remedy** for the half that *is* in this feature's scope: a fifth group in `SubscriptionReportService` —
« Repeat signup (same phone or clinic name as an existing cabinet) » — listed like `Suspended`, i.e. **informational
and not a finding**, so a scheduled report does not sit permanently at exit 2 over what is usually two practices
sharing a landline. Reject the alternatives: refusing the signup outright needs the spec amendment, and a hard
per-address cap is defeated by construction (a new address is free). Still needs validating: whether clinic name
plus phone is a usable signal in Tunisia, or whether it fires constantly on group practices.

---

## 3. The four mutating vendor verbs repeat one six-step scaffold (review finding 46, Suggestion)

`subscription-grant` / `-cancel` / `-suspend` / `-unsuspend` each run `BuildForConsoleVerb` → `HasConnectionString`
→ `BuildProvider` → `CreateScope` → `DeclareActor` → `ResolveCabinetAsync` → `UseClinic` → construct a handler from
the same `GetRequiredService` calls. Suspend and unsuspend construct the *identical*
`SetSubscriptionSuspensionCommandHandler`.

**Why deferred, not applied.** It is a four-file restructure with no behavioural component, well outside the line
the review flagged, and it has a constraint that makes the obvious version wrong: `SystemWideCallerCoverageTests`
reads each verb's scope declaration out of its **own source file**, so the `UseClinic(id)` call must stay textually
in each `Maintenance/*Command.cs`. A helper that swallowed it would make all five verbs look silent to the one
guard that exists to catch a path reading nothing and reporting success.

**Chosen remedy.** `SubscriptionVerbs.RunForCabinetAsync(args, commandName, purpose, (scope, clinicId, actor) => …)`
taking the body as a callback, with the `UseClinic(clinicId)` line **left in each caller's lambda** — visible to the
source scan, one line per verb, everything else shared — plus a `NewSuspensionHandler(scope)` factory for the pair
that build the same handler. Reject moving the declaration into the helper (breaks the guard) and reject a base
class (these are `static class`es by convention here, and `SystemWideCallerCoverageTests` already had to learn that
a static class is abstract-and-sealed in metadata).

---

## 4. `POST /api/backup` writes a cross-tenant dump to a caller-supplied path (review's own "do not lose it" note)

`BackupNowCommand` → `PgDumpBackupService` runs `pg_dump` over `ConnectionStrings:DefaultConnection` — on
`HostedMultiTenant` the single database holding **every** clinic — and writes the dump plus a recursive copy of the
file-storage tree to a **caller-supplied folder used verbatim**: no allow-list, no canonicalisation, no root check,
and no deployment-profile gate on the controller or the command. Any one clinic's admin can therefore cause a
cross-tenant dump to be written to any path the API process can write.

**Why it is here rather than in the review.** Every line of it is **unchanged** code; the `clinic-subscription`
branch only adds `[AllowsWithoutSubscription]`, and that attribute is spec-aligned (FR-8 keeps the scheduled backup
running, so refusing the manual one would be incoherent). It is recorded because the exemption is the moment
somebody last looked at this endpoint.

**Chosen remedy.** Two changes, and they are complementary rather than alternatives: (a) gate the controller on a
capability — `HasLocalDbTooling` / `UsesDiskStorage` — so it **404s** on `HostedMultiTenant`, where a per-clinic
backup is not a thing this service can produce at all; and (b) restrict the destination to the resolved default
root, refusing anything that escapes it after canonicalisation, so the `SelfHostedLan` install it exists for keeps
working. Reject "validate the path only": on the hosted profile the dump would still be cross-tenant, which is the
larger half. Trade-off to accept: a hosted admin loses a button that never did what its label implies.

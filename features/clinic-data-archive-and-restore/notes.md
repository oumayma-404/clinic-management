# clinic-data-archive-and-restore — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A cabinet takes its whole record out, and can put it back (`clinic-data-archive-and-restore`)

`GET /api/backup/archive` streams a **per-clinic `.zip`** — a manifest, one JSON file per table, and the blobs —
and `POST /api/backup/archive/restore` puts the missing rows back. `POST /api/platform/clinics/restore` is the
vendor's door, re-creating a cabinet that no longer exists (its own accounts included).
⚠️ **This is what `pg_dump` structurally cannot be on a shared database**: that tool takes `--dbname` and has no
tenant predicate, which is exactly why `BacksUpItsOwnData` turns it off on the two hosted kinds. The archive goes
through the same tenant filter as every CSV export, so it is offered on **all three** profiles — and on
`SelfHostedLan` it is additionally a *portable* copy the machine-level dump beside it is not.
⚠️ **The restore is additive and keyed on the original ids**, which is what makes total loss and partial loss the
**same operation** — total loss is the case where every row is a gap. A row still present is left alone and
counted « déjà présent »; a row that **differs** is **skipped**, never overwritten, and counted apart, so work
done after the archive survives putting the archive back; a row that is gone is re-inserted **with its own id and
its own document number**. Money documents are safe for that last reason: the gapless `AAAA-NNNN` sequences break
only when a new number is minted, and nothing here mints one. A second restore is a no-op.
⚠️ **A 45-finding review then corrected the feature in place, and five of its fixes were data integrity rather
than tidying.** (a) `ArchivedProperties` excluded `ValueGenerated.OnAdd`, which EF sets for **every**
`HasDefaultValue` column *and* for a `Guid` key whose configuration omits `ValueGeneratedNever()` — so a **voided
payment restored as live money**, archived patients un-archived, every devis lost its act ordering, and the three
configurations without that call lost their **primary key**, making every ordonnance, certificat and antécédent
médical unrestorable while the manifest declared their row counts. Fixing the predicate was half: EF also omits a
store-generated column equal to the property's *sentinel*, so `ApplicationDbContext` now aligns each sentinel
with the column's own default — model-only, no migration, and it closes the same trap for every insert in the
product. (b) **Only `Child` tables were ordered**; the directly-owned ones were appended in the model's
enumeration order, so on the total-loss case the feature exists for, `DentalRecord` reached the database before
`Patient` and the restore died part way. (c) **Neither door had a transaction**, so that death committed tables
1..*n*−1 and surfaced as a generic 500 — while the spec's own « Aucune donnée n'a été modifiée » implies it
cannot happen. (d) **The console path validated the archive's `Clinic` row *after* the commit**, and the
live-cabinet guard keys on the manifest's *claim* rather than on the row that lands: a hand-edited manifest left
a practice's records committed under an id nothing points at, with no administrator, no entitlement and every
retry answered `409 clinic_exists`. (e) **Presence was decided on the primary key alone**, so a re-minted invoice
number met the unique index as an unhandled `DbUpdateException` instead of AC-3's « sans gap ni collision ».
⚠️ **Four tenancy holes closed with them**, each needing no knowledge of another tenant beyond a GUID: a crafted
`data/Clinic.json` inserted **unbounded phantom cabinets** (`Clinic`'s identity is `Id`, which `StageInsert` does
not re-stamp); a `Child` row's parent FK was written verbatim, and twelve archived tables carry no `ClinicId` of
their own, so a `Payment` could be inserted against **another practice's invoice**; blob keys were taken from
every row the file named rather than from the rows actually inserted, so `clinics/<victim>/…` created objects
inside that tenant's prefix; and `RowsMatch` over an `IgnoreQueryFilters` probe was a **confirm-or-deny oracle**
on arbitrary column values platform-wide. `ClinicArchiveLimits` closes the decompression bomb, and the console
door — which had no size gate at all — shares the cabinet door's.
⚠️ **`restore|` was a write-only marker.** `AsRestore()` *prepends*, so it matched neither `AuditLabels.Actor`'s
`job|` test (a restore rendered as the named admin's own e-mail — verbatim the outcome its docstring says it
prevents, and on the console path an outside address shown to the practice as a colleague) nor
`PlatformCounterPass`'s `job|`/`console|` exclusions, so the vendor restoring a dead cabinet made it the
portfolio's **most active practice** the next morning. Both read the prefix now. And restored **children** left
no ledger row at all — the interceptor writes one per aggregate *root* — so four thousand re-inserted `Payment`
rows put money back in la caisse with nothing in « Journal d'activité »; the restorer emits a summary row per
table, inside the same transaction. The archive's own uploaded warnings no longer reach the report either: they
were attacker-authored prose rendered on the vendor's console as the server's own.
⚠️ **What is still owed is a real database.** Six of the guards above are SQL, and nothing in `UnitTests` touches
a database — `follow-up/archive-restore-real-database-checks.md` chooses a `restore-dry-run` console verb on
`verify-schema`'s precedent and says why not Testcontainers.
⚠️ **Nothing goes through a domain constructor, and it cannot.** Every PK is a GUID minted *inside* the
constructor and half the timestamps are stamped there from `DateTime.UtcNow`, so building entities the ordinary
way would give every restored row a new identity and today's date. `Infrastructure/Persistence/ClinicArchiveStore`
materialises rows as **property bags driven by the EF model** instead — which also means a **new column is
archived the day it is written**, where ~35 hand-written DTOs would be ~35 second definitions of what a row is.
⚠️ **The entity set is derived, not listed** (`ClinicArchiveScope`): every non-owned table with a path to a clinic,
minus a declared exclusion set, ordered **parents before children** by walking the required FKs to a fixpoint. The
three scopes are `Self` (the `Clinic` row, matched on its own PK — it has no `ClinicId` because it *is* the
clinic), `Direct` (its own `ClinicId`) and `Child` (through the parent's ids). A table with none is **reported**,
never silently dropped.
⚠️ **What is excluded is each a decision**: the vendor's entitlement (a cabinet could otherwise restore its own
cover from a file it controls — FR-2), the three outboxes (a re-inserted due row would **send SMS reminders about
visits that already happened**), the feed, the push registry, the backup ledger, the audit ledger, `User`
(credentials do not travel in a file on a laptop), `ClinicReminderSettings` (its secrets decrypt under a key ring
the archive does not carry, so they would restore as silently « non configuré ») and the console's own tables.
`Clinic.GoogleRefreshToken`/`GoogleCalendarId` are **nulled** rather than the row excluded.
⚠️ **`IFileStorage` gained `RestoreAtKeyAsync`, deliberately not a third `UploadAsync` overload.** US-5's
guarantee — « an unprefixed key is not something a caller can write » — is held by `ClinicStorageKeyTests`
reflecting over every `UploadAsync` for a `Guid`, so a third overload without one would restore that defect in
silence. This mints no key: it names the key a row **already** holds, which may legitimately be a flat pre-US-5
one (EC-4) that composing would move out from under its own row. Existing bytes are left alone, the blob half of
the additive rule.
⚠️ **AC-9's actor is a decoration, not a fourth kind.** `AuditActor.AsRestore()` wraps whoever is in scope as
`restore|{id}`, so « qui a restauré ? » stays answerable while « ces trois mille fiches ont-elles été saisies ? »
answers no. It cannot be `RunAs`, which is deliberately ignored while a real user is in scope — and a restore
always has one.
⚠️ **The console path restores the `Clinic` row rather than provisioning a cabinet.** Provisioning first would
make the archive's own row « présent mais différent », i.e. skipped — the practice back with its patients and its
money but a blank name, no billing settings and no working hours. Only what an archive deliberately does not
carry is created after: the admin (one-time password, shown once) and the entitlement, through the companion's own
`LocalClinicProvisioning.StageEntitlementAsync`. A cabinet that is **still live** is a **409 `clinic_exists`** —
its own admin can restore it themselves, and the vendor minting a second administrator into a working practice is
the wrong move whatever the archive says.
⚠️ Both cabinet endpoints carry **`[AllowsWithoutSubscription]`** (AC-8, the AC-4.2 argument), and the download is
a GET the gate never inspects anyway. **The archive is not encrypted**, and the card says so in French in the same
box as the button — it is a complete copy of the practice's medical records, and that is a separate decision with
its own key-management question rather than a password box that protects nothing.

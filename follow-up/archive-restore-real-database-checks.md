# Follow-up — the archive restore's query path has no automated gate

**From:** `features/clinic-data-archive-and-restore`, review finding 25 (Major), captured while applying the
review's 42 valid findings.
**Status:** open. Nothing is broken by it today; what is missing is the ability to *notice* if it breaks.

## What is uncovered

Six behaviours of `ClinicArchiveStore.RestoreTableAsync` / `ReadEntitiesTypedAsync` — listed in
`features/clinic-data-archive-and-restore/progress.md` under « Coverage notes ». They are the tenancy and
collision guards the review's findings 1, 3, 6, 9, 11 and 19 added, and every one of them is SQL.

`progress.md` used to mark AC-1 and AC-3 as covered by `ClinicArchiveScopeTests` and
`ClinicArchiveEndpointTests`; neither can reach them. The scope class tests the **plan**, and every endpoint test
runs against `FakeArchiveStore`, which returns whatever it was seeded with. Those claims are corrected; this is
what replaces them.

## The chosen remedy, and why not the others

**Extend `verify-schema` with a `restore-dry-run` sibling verb** — a read-only console verb that seeds two
cabinets into a throwaway database, exports one, and asserts the six properties against the real queries. Exit
codes `0` / `1` / `2` as its two siblings use, so it scripts identically and can be run before and after a
release.

That is the shape this repository already has for « a class of change the unit suite structurally cannot see »:
nothing in `UnitTests` touches a database, and `verify-schema` exists precisely because a migration is invisible
to it. A restore is the same kind of change — an index can be missing, a predicate can be unscoped, a backfill
can cover zero rows, and the whole suite stays green.

Rejected, with reasons:

- **A test-only relational provider (Sqlite in-memory) in `ClinicManagement.UnitTests`.** It would break the
  project's own stated rule that nothing there touches a database — a rule `UnitTests/CLAUDE.md` argues for at
  length — and it would test a *different* provider: the partial unique indexes, `unaccent` and the `xmin`
  concurrency token are all PostgreSQL-specific, and the tenancy predicates would be exercised against a schema
  that is not the one that ships.
- **Testcontainers / a real PostgreSQL in CI.** The right answer for a project that already has that
  infrastructure. This one does not, `ci.yml`'s api job runs `dotnet build` plus the unit suite and nothing
  else, and introducing a container dependency into the only backend gate is a larger decision than a review
  fix should make on its own.
- **Leaving it as a manual checklist item.** That is what the AC rows claimed and it is what failed: a checklist
  nobody runs is indistinguishable from coverage until the day it matters.

## Trade-offs to accept

- A console verb is run by an operator, not by CI, so it catches a regression at release time rather than at
  commit time. That is strictly better than the current position (nothing catches it) and matches what
  `verify-schema` and `reconcile-money` already deliver.
- It needs a throwaway database to seed into, which means the verb has to refuse to run against one holding
  real data. `restore-backup`'s « refuse while the app is listening » interlock is the precedent for that kind
  of guard, and its limitation in containers is documented there.

## Still needs validation

The six properties above have been reasoned through and reviewed but **not one of them has been executed against
PostgreSQL**. Until the verb exists, an end-to-end restore on a real database — the export, the refusals, and
the document-number continuation — is owed before this feature is relied on for a recovery.

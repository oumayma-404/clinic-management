# backup-works-everywhere — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Backup works out of the box, or says whose job it is (`backup-works-everywhere`)

the tools are **discovered**,
not configured — `Infrastructure/Services/PostgresToolLocator` searches explicit config → beside the app → **`PATH`**
→ the well-known per-version roots (newest first), and `api/Dockerfile` now installs `postgresql-client-16`.
⚠️ **The defect this closes was invisible at every layer**: both tools were reached through `Backup:PgDumpPath`,
written by exactly **one** of the four ways this product is deployed (the Windows installer, which bundles its own
PostgreSQL). Everywhere else it is the `""` that ships in `appsettings.json`, so « Sauvegarder maintenant », the
hourly `BackupJob`, the pre-migration safety dump **and** the `restore-backup` verb all answered « L'outil pg_dump
est introuvable » — for the life of the product, on every Docker deployment. `RestoreBackupCommand` held a second
**copy** of the resolution rule under a docstring claiming to be « one rule »: the `fixes-dont-propagate` shape
again, and both copies were broken the same way.
⚠️ **On the two hosted kinds the answer is not « make it work », it is « say whose job it is »**: the 17th
capability **`BacksUpItsOwnData`** (`SelfHostedLan` only) stops the hourly job being registered and 404s the two
write endpoints, because `deploy/`'s `backup` sidecar already dumps the database *and* the object store
**off-server** on a schedule, and — the load-bearing half — `pg_dump` takes `--dbname` and has **no tenant
predicate**, so on a shared database « Dr X clicks Sauvegarder » would dump every other practice's patients.
Nothing today could exfiltrate it (no download endpoint), which is exactly why it is a capability and not a
comment: the day somebody adds « télécharger la sauvegarde », the leak arrives with it. **A clinic still takes its
own data out** through the per-clinic CSV exports and PDFs, which go through the tenant filter.
⚠️ `GET /api/backup/history` **still answers there**, reporting `managedByHost`, and the card says « Sauvegardes
gérées par l'hébergeur » — it is the read that *explains* the absent button, and it deliberately quotes **no date**
(the sidecar runs in another container and this application cannot observe it, so a « dernière sauvegarde » here
would be invented).
⚠️ **And `DirectoryAclHardener` was locking the backup out of its own folder.** It granted three well-known SIDs
then ran `/inheritance:r` — which is *also* where the running account's access comes from unless it is one of those
three, so under an unelevated or de-privileged account a **successful** hardening broke the dump written
immediately afterwards, **and** the AC-14.4 cleanup was refused for the same reason, leaving an unreadable,
undeletable `clinic-backup-*` folder with only a logged warning. Three were found in a real destination — where,
being `clinic-backup-*`-named and oldest, they permanently consumed `PruneOldBackupsAsync`'s deletion budget, so
**retention had silently stopped pruning anything**. The account the process runs as is now granted too;
`Users`/`Everyone` are excluded from that grant explicitly, so the policy is true by construction.

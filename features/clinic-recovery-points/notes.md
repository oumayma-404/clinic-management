# clinic-recovery-points — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Something now PRODUCES an archive, and the practice is told when none has left the building (`clinic-recovery-points`)

the archive/restore feature above was a complete recovery mechanism with **nothing
creating an archive** — no schedule, no reminder, and no record anywhere of when one was last taken — so with every
delete in this product being a hard delete (zero hits for `IsDeleted`/`DeletedAt`/`SoftDelete`; `Patient.IsArchived`
is a list-visibility flag and says so), « j'ai supprimé la fiche » had no answer unless somebody had happened to
press a button. A daily **`ClinicRecoveryPointJob`** now builds a **rows-only** archive per cabinet, stores it under
`clinics/{id}/recovery-points/`, records it in **`ClinicRecoveryPoints`** and prunes to seven;
`GET /api/backup/recovery-points` lists them and `POST /api/backup/recovery-points/{id}/restore` puts one back
behind the **same** step-up action as the upload restore.
⚠️ **It is the THIRD caller of `ClinicArchiveRestorer.ApplyAsync` and adds no restore semantics of its own** — the
additive rule, the original ids, the « présent mais différent » skip are all unchanged. What is new is only *where
the bytes come from*: a storage key the cabinet already owns instead of a file somebody uploaded.
⚠️ **Rows only, and that is a cost decision with a stated consequence.** A cabinet's rows are megabytes of JSON;
its radiographs are gigabytes, so seven daily full copies would be seven copies of the object store per practice.
A row deleted by mistake is the case this exists for; a blob's durability is the object store's own problem and is
not improved by copying it into the same store. So `ClinicArchiveContents` is recorded **in the manifest and on the
row**, the list says « lignes seulement », and the confirmation warns before the click — because an unreadable blob
is a *warning* in the packager, so a rows-only archive and a full archive whose every blob failed both report
`BlobCount = 0`, and « cette archive ne contient pas les fichiers » and « les fichiers n'ont pas pu être lus » are
opposite facts with the same picture. `RowsAndFiles` is the enum's **0** so every archive already on a laptop reads
as carrying its files, which is why **no `SchemaVersion` bump** was needed.
⚠️ **`ClinicRecoveryPoint` is on `ClinicArchiveScope.Excluded`**, beside `BackupRun` but for a sharper reason than
transience: these rows name **storage keys**, so an archive carrying them would restore a list of recovery points
whose objects retention pruned months ago — offering a recovery that cannot be performed, which is worse than
offering none.
⚠️ **The staleness alert is about the copy that LEFT the building, not about these rows.** Recovery points live
inside the deployment and die with it, so the fact worth nagging about is the practice's own off-server copy —
`NotificationCategory.ArchiveStale`, an ensure/clear pair on `BackupStale`'s shape (one restating row, not four
thresholds like the subscription warnings), fired from `Clinic.LastArchiveDownloadedAtUtc`. A cabinet whose seven
points are perfectly healthy still gets it, and the card says why in the same box.
⚠️ **That column exists because the audit ledger cannot answer the question.** `ArchiveAccessLedger` already records
every export twice, but « livrée » and « NON livrée » are *both* `AuditAction.Update` and differ only in French
prose — deriving « la dernière archive réussie » from it would mean matching a sentence, the
`Contains("déjà facturée")` defect this repo deleted. Only a **delivered** download stamps it, and it never moves
backwards (the delivery row is written post-response and best-effort, so two downloads started together can finish
in either order).
⚠️ **Registered on every deployment kind and asking no capability**, unlike `BackupJob`: that one runs `pg_dump`,
which has no tenant predicate, and is `BacksUpItsOwnData`-only. On `SelfHostedLan` this is additionally the only
**granular, online** recovery that exists — `restore-backup` stops the app and restores the whole database to undo
one deleted fiche.
⚠️ **The prune deletes the OBJECT before the ROW**: the other order leaks an object nothing points at, invisibly,
for the life of the deployment, while this order can at worst leave a row whose object is gone — which the restore
names as a refusal rather than meeting as a crash. `verify-schema` gained
**`recovery-point-success-names-its-archive`**, the one failure invisible everywhere else: a row claiming success
while naming no archive is listed as restorable and refuses on the click, at the moment a practice has already lost
data.
⚠️ **What this does NOT solve, stated rather than implied**: a recovery point inside the deployment dies with the
deployment, so **total loss still needs infrastructure backup**, and the vendor console remains the only path back
from a lost `Clinic` row. On the Render deployment the premise `BacksUpItsOwnData = false` rests on — «`deploy/`'s
`backup` sidecar already dumps this off-server » — is **false**, because Render is not running that compose file at
all; see `follow-up/render-free-tier-transit-relaxation.md` and `deploy/RESTORE-DRILL.md`, which still says no drill
has ever been performed.

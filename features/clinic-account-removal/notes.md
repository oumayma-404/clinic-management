# clinic-account-removal — what shipped

> The vendor can delete a cabinet for good from the console, and the deletion frees the e-mail addresses of its
> accounts. One screen, one endpoint pair, and a delete plan nobody maintains by hand.

## Why it exists

`Users.Email` carries a **unique index filtered on `PasswordHash IS NOT NULL`**, per installation. So a *verified*
account owns its address for ever, and nothing in the product could release it: a cabinet created to try the
product out held one of the owner's real addresses permanently. The second half is the portfolio — a hosted
deployment that accumulates abandoned trials has no way to say which of its rows are real practices.

⚠️ **A pending signup never blocked an address** (`SignUpClinicCommand` re-arms an existing row), so the thing to
remove was always the *cabinet*, not a stray row.

## The two decisions that were asked

| Question | Answer | Consequence |
|---|---|---|
| What does « remove » mean? | **The whole cabinet** — every row, every account, every file | there is no soft-delete and no « libérer l'adresse » half-measure to fall back on |
| Refuse a cabinet holding money? | **No** — always allowed | the only barriers are human: the typed name, the motif, the journal. A rule keyed on « has encaissements » would have made the test cabinets this exists for undeletable the moment somebody recorded a payment in one, and pushed real deletions back to SSH where nothing is recorded |

## The plan is derived, and that is the whole engineering content

`ClinicPurge.BuildPlan(IModel)` reads the EF model: every non-owned entity with a table, its predicate from its
own `ClinicId` column or from a single-column foreign-key path to one, and the order from
`GetReferencingForeignKeys()` — dependents before principals, the cabinet's own row last. **65 steps today.**

⚠️ **A hand-written list was the alternative and it is this repository's dominant defect shape with the highest
possible stake**: the table somebody forgets is the table whose patient rows survive a deletion the vendor was
told had succeeded. Proof it was the right call — the generated plan covers eight tables a hand list would have
missed: `CreditNotes`, `SessionFamilies`, `UserDashboardPreferences`, `PatientFileAnnotations`,
`FileUploadSessions`, `RecurringExpenses`, `StockMovements`, `DocumentEmails`.

⚠️ **It verifies itself.** After the pass every covered table is counted again; one surviving row throws, which
rolls the caller's transaction back. A half-emptied cabinet — reads as deleted, cannot be signed into, still holds
clinical rows — is the one outcome worse than a refusal.

⚠️ **Two tables belong to a cabinet and no clinic id can reach them**: `ClinicSignup` and `PasswordResetRequest`
carry the practice's name, an administrator's address and a PBKDF2 hash of their password, with no `ClinicId` and
no FK to `Users`. They are swept **by address**, and the rule is derived too (clinic-less · not exempted ·
carrying an `Email` column), so a third table of that shape is covered the day it is configured. The addresses are
read **before** the rows go; afterwards the answer is structurally unavailable.

⚠️ `ClinicPurge.SurvivesByDesign` is the one hand-written list — four entries, each with a reason:
`PlatformAccessEntry` (the vendor's own ledger), `PlatformAccount` + `PlatformRecoveryCode` (console identity),
`DataProtectionKey` (**the install's** key ring — it protects every cabinet's encrypted reminder secrets).
`ClinicPurgePlanTests` asserts it against the model in both directions.

## The order is the design

1. every refusal — blank motif, wrong name, unknown cabinet — **before** the transaction opens;
2. the freed addresses read **while the accounts still exist**;
3. rows + the journal row in **one** transaction, the row staged before the save (a cabinet gone with no record of
   who removed it must be unreachable);
4. the blob sweep **after** the commit — an object store has no rollback, so a sweep inside a transaction that
   then aborts is how a cabinet keeps its rows and loses its radiographs. Best effort by contract: the rows are
   gone, so an unremovable blob is wasted bytes and not a wrong record.

`IFileStorage.DeleteByClinicAsync` is a **prefix sweep** (`clinics/{clinicId}/`), not a pass over the six columns
that hold a key: those would miss a logo whose row is already gone, the staged parts of an unfinished upload, and
every blob a column added later holds. ⚠️ A flat pre-US-5 key is outside the prefix and survives — correct, since
those rows exist only on a LAN install, where there is no console.

## The journal row is what remains

`PlatformAccessAction.DeletedClinic` = 10. `PlatformAccessEntryConfiguration` already declared **no FK to
`Clinics`** and carried `ClinicName`/`AccountEmail` denormalised, for exactly this case (« who opened the file of
the practice that has since been closed? ») — so the feature needed no schema change at all beyond the enum
member, which persists as an `int`. The **motif is mandatory** and is the only thing that will ever separate
« cabinet de test » from « le cabinet a demandé la suppression de ses données ».

## What the screen does

« Zone dangereuse », last on the cabinet's fiche, on a destructive border, **beside nothing**: filed next to
« Suspendre » it would read as a stronger version of the same lever, and the section states the alternative
(suspension is reversible and costs no paid day) above the button.

⚠️ **The preview is read when the panel opens**, not rendered with the fiche: with the page it would be a ~60-table
census paid on every open of every cabinet, and — worse — as old as the tab, so a vendor would type a cabinet's
name against a sentence describing what it held an hour ago.

⚠️ **The field and the button are absent, not disabled, until the preview lands**, and a **failed** preview offers
no deletion at all: « ce cabinet ne contient rien » is the one sentence that would make this button look safe and
be wrong.

⚠️ **A typed name, never a « je comprends » tick**: the failure being caught is a *wrong row* (several cabinets are
called « Cabinet Test N »), which a checkbox cannot catch. It is checked again server-side against the cabinet the
id resolved to — the browser comparing two of its own strings would agree with itself. Trimmed, inner whitespace
collapsed, case-insensitive: the check is « did you read the right row », not « can you reproduce the capitals ».

⚠️ **The panel does not close on success.** It shows what was removed and which addresses are free — that list is
the whole reason the action was taken and there is nowhere left to read it afterwards.

## What is NOT in this feature

- No archive is taken or required. The dialog names the alternative in prose; `clinic-data-archive-and-restore`
  already provides the download and the console already has the restore.
- No bulk deletion, and no deletion from the portfolio list — the action is only reachable from one cabinet's own
  fiche, which is what makes « you read the row you are destroying » true.
- No console verb. The five `subscription-*` verbs exist because they predate the console; this has one home.

## Verification

- `ClinicPurgePlanTests` — 11 cases, derived from the model. The highest-value one is
  `Every_Step_Names_What_It_Is_Keyed_On`: a predicate that lost its parameter is a `DELETE` for every cabinet on
  the deployment, and a functional test of « this cabinet is gone » would pass.
- `DeleteClinicFromConsoleTests` — 19 cases, most of them about what must **not** happen (nothing touched on a
  refusal; a failed purge rolls back and leaves the files alone; an unremovable blob does not un-delete the
  cabinet).
- Whole suite green: **4638 passed, 0 failed**. `console`: `tsc --noEmit` + `npm run build` clean.
- ⚠️ **Not exercised end to end against a database.** Nothing in `UnitTests` touches one, so the generated SQL was
  read by eye (all 65 statements, dumped from the model) and the plan's shape is guarded — but the first real
  deletion will be the first execution. Do it on a cabinet you created for the purpose.
- ⚠️ **It is not on the hosted deployment until `deploy-hosted.yml` runs**: `console`, `web` and `api` ship only
  through that workflow.

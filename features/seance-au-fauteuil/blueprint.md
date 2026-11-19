# Séance au fauteuil — implementation blueprint

> **Decided 2026-11-18** (`/think-solution`, after a dentist demo): **option A, « fiche vivante »**, and **« Payé »
> starts empty with a « Tout payé » button**. Options page with the diagrams and mock-ups:
> https://claude.ai/artifact/M4zzy1JiGD67JVYF8E7BBY — B (draft fiche), C (per-cabinet switch) and D (separate
> ordonnance button) were rejected there, with reasons.
>
> **Build from `origin/main`**, never from `feature/security-remediation` (≈ 250 commits behind: it lacks
> `Appointment.ExpectedEnd` / the retard, `useCarriedDraft`, `StaffNotification.TargetRole`). Every finding below
> was re-checked on `origin/main` (a3249a87).

## Summary

The fiche de soins becomes the séance's working document: opened from the agenda while the patient is in the chair,
saved as the dentist goes, closed with « Terminer la séance ». The ordonnance prints from the fiche, after a save, so
there is still one writer and one document. Money gets two ways in that end in the same note: « Payé » in the fiche
(now empty by default) and a new « À encaisser » list on the agenda for the secretary. Both go through the existing
`BillDentalRecordCommand`. No new entity, **no migration** in either wave.

## What the code does today (verified on main)

| # | Fact | Where |
|---|------|-------|
| 1 | « Payé » mirrors the total on a new fiche, mode « Espèces » → an untouched save bills a fully-paid cash note | `patient-record-modal.tsx` mirror effect (`if (record \|\| paidDirty \|\| isInvoiced) return`) |
| 2 | The modal closes on every successful save; a second save from the same window would create a 2nd fiche or 409 (record and document versions not adopted) | `handleSave` → `onOpenChange(false)` |
| 3 | First fiche **create** linked to a visit → `MarkVisitCompleted` immediately, no time check; an **update** never completes | `CreateDentalRecordCommand` post-commit block |
| 4 | No fiche door on the agenda block, its « ⋯ », or the RDV window; « Au fauteuil » is labelled « Ouvrir la fiche » and opens the RDV | `appointment-quick-actions.tsx`, `edit-appointment-dialog.tsx`, `now-next-cards.tsx` |
| 5 | The fiche offers only « Aperçu » (no print) for its ordonnance, even once saved | `prescription-section.tsx` |
| 6 | Preview « Imprimer » prints the iframe without waiting for it to load (blank page possible); `lib/print.ts` calls itself « the one way a PDF reaches a printer » and is bypassed | `document-preview-dialog.tsx` `print()` |
| 7 | « Corriger la note » voids **every** live payment on the note, desk ones included, then re-bills the fiche's figure only | `UpdateDentalRecordCommand.RetireForCorrectionAsync` |
| 8 | « Encaissé sur le traitement » compares only installment payments tagged with the fiche; desk payments are untagged → double count possible | `CollectOnTreatmentCommand`, `TreatmentPlan.CollectedOnRecord` |
| 9 | RDV window « Facturer cette consultation » creates a **draft** note; drafts settle the closure money step (`InvoiceLinkChoice.ByKey` excludes only Cancelled), aren't debt, and the fiche billing can't see them (line links only) → a 2nd note later | `edit-appointment-dialog.tsx`, `InvoiceFormModal`, `InvoiceLinkChoice` |
| 10 | `BillDentalRecordDialog` has no cheque fields | `bill-dental-record-dialog.tsx` |
| 11 | Nothing on the agenda says a séance waits to be paid; the agenda listens to `appointments` only, which the fiche broadcasts before the note exists | `appointment-calendar.tsx`, `app/appointments/page.tsx` |
| 12 | `PUT dental-records/{id}` is `AnyClinicRole` and accepts `CorrectionReason` (voids payments) with no role check; the invoice corrections are `AdminOrDoctor` | `DentalRecordsController`, `UpdateDentalRecordCommand` |

Already safe to save repeatedly (re-used, not rebuilt): stock (only added acts), the ordonnance (updated in place,
never deleted), devis linking (idempotent), treatment collection (running total), billing top-up (difference only).

---

## Wave 1 — the fiche in the chair

### 1.1 Doors to the fiche during the séance

- **One rule, one link composer.** `web/lib/visits/fiche-door.ts` (new):
  - `canRecordFiche(visit, todayIso)` returns true when the visit has a patient, is not Annulé/Absent, and its day is
    today or earlier (Tunis day via `todayLocalIso()`). It is **not** gated on the slot's start, because a patient
    who arrives early is real.
  - `ficheHref(patientId, appointmentId)` is the **only** composer of `?addRecord=1&appointmentId=`. The patient page
    already switches to edit mode when a fiche carries that appointment.
- **Doors that use it:**
  - the agenda block's « ⋯ » (`appointment-quick-actions.tsx`), as « Fiche de soins »;
  - the RDV window header beside the patient link (`edit-appointment-dialog.tsx`);
  - « Au fauteuil » (`now-next-cards.tsx`), whose `href` becomes `ficheHref`, which makes its label true;
  - the patient page « Rendez-vous » tab row (today gated on the slot **end**);
  - the plan rows (`plan-next-action.ts`, `seance-strip.tsx`, gated on the slot **start**).
- **Unchanged on purpose:** the post-visit popup and « À clôturer » are reminders and stay gated on `ExpectedEnd`.
- Patient page « Nouvelle fiche de soins »: when the patient has a visit today that `canRecordFiche` admits and that
  has no fiche, pass its `appointmentId`. The server's inference refuses a day with two visits, and an unlinked fiche
  can never be linked later. PR #115 relinks orphans at booking time and is complementary.

### 1.2 « Enregistrer » keeps the fiche open · « Terminer la séance »

- **Footer.** When the modal's `appointment` is still running (status Planifié · Confirmé · En cours), it shows a
  status line « Enregistré à 10:32 » (`role="status"`), then « Enregistrer » (stays open) and « Terminer la séance »
  (primary: save + complete + close). Otherwise it keeps today's single « Enregistrer » (save + close). The variant
  is derived from the status, never from the clock in the browser.
- **In-place save (`mode: "stay"`).** The modal adopts what it just saved instead of closing:
  - The modal holds `savedRecord` internally and aliases `const record = savedRecord ?? recordProp`, so every
    existing `record` read (create vs update, the fiche version, `isInvoiced`, the merge baseline, the document ids)
    follows it. The page's prop stays untouched until close; then `refreshKey` bumps as today.
  - `baseRecord` becomes the **saved** DTO, not the screen. Edits typed while the request was in flight stay
    « mine » for the next three-way merge (`fiche-merge.ts`).
  - Document versions come from the response (see 1.3). `guard.markClean()` runs after an in-place save, so closing
    doesn't prompt.
- **`useCarriedDraft`.** Its key moves from `new:{patient}:{appointment}` to `record:{id}` after the first save.
  Clear the `new:` entry, or a later open restores a stale draft.

### 1.3 Server — « Terminé » follows the séance, not the first save

- `CreateDentalRecordCommand` and `UpdateDentalRecordCommand` gain `bool FinishSeance` (default false = « decide by
  the clock »).
- **New `Features/Patients/FicheVisitCompletion.cs`.** It extracts Create's post-commit block (`MarkVisitCompleted`,
  save if changed, delete the review, broadcast) so **both** commands call it. It never throws: a failure is logged,
  as today.
  - Rule: `finishSeance || visit.HasEndedBy(now)`, using `Appointment.HasEndedBy` / `ExpectedEnd`, which include the
    retard. Never `Start + Duration`.
  - On **create**, the review is still deleted even when the visit stays running: a fiche exists, so there is
    nothing left to prompt for.
- **`AppointmentProgressJob.CloseClinicAsync`.** A patient visit whose séance ended goes to `Completed` when a fiche
  is linked to it, else to `AwaitingClosure` as today.
  - One batched read per pass: `IDentalRecordRepository.GetAppointmentIdsWithRecordAsync(ids)`.
  - Same write the job already makes at that minute, through `AutomaticWriteInterceptor` (job actor).
  - `SkipsAsync` (PC de secours, demo build) still applies.
- **`DentalRecordDto`** gains `PrescriptionDocumentVersion` and `ExamensDocumentVersion` (`uint`, 0 = no document),
  read **after** the commit so the browser can save again without a 409 against its own last save.
- `VisitClosure`, the popup and `chairClaim` need no change. A fiche-bearing visit becomes « Terminé » at
  « Terminer » or at `ExpectedEnd`, and « Au fauteuil » keeps the patient until then.

### 1.4 Print the ordonnance where it is written

- **In « Prescription »**, one button per sheet actually typed: « Imprimer l'ordonnance » / « Imprimer la demande
  d'examens », beside « Aperçu ».
  - Press: when the form is dirty or has no document yet, run the in-place save first. Then
    `GET /medical-documents/{id}/pdf` → `printPdfBlob(blob, fileName)`.
  - The toast is worded from its return value (`'printed'` vs `'delivered'`, the tablet case).
  - A failed save prints nothing; its error shows as today. « Aperçu » keeps its rule: nothing printable without a
    saved fiche.
- **After save + close** (the after-séance flow), the save toast carries an « Imprimer » action, on the precedent of
  `payment-modal`'s « Télécharger le reçu ». It replaces the sentence « imprimable depuis la séance ».
- **`document-preview-dialog.tsx`**: « Imprimer » calls `printPdfBlob(blobRef.current, fileName)` (it already holds
  the blob). This fixes the blank-frame race and gives `lib/print.ts` back its « one way » claim.

### 1.5 « Payé » starts empty

- Delete the mirror effect. « Payé » starts `""`. Mode « Espèces » stays the default method for when an amount is
  typed.
- « Tout payé » sits beside the field, shown while `grandTotal > 0` and the amount ≠ total. It sets the total and
  `paidDirty`.
- Reopening a saved fiche is unchanged in wave 1 (it shows the stored amount).

### Wave 1 files

| Create | Purpose |
|---|---|
| `api/…/Application/Features/Patients/FicheVisitCompletion.cs` | the one completion step + rule |
| `web/lib/visits/fiche-door.ts` | `canRecordFiche`, `ficheHref` |

| Modify | Change |
|---|---|
| `CreateDentalRecordCommand.cs`, `UpdateDentalRecordCommand.cs` | `FinishSeance`; call `FicheVisitCompletion`; map document versions |
| `DentalRecordDto.cs` (+ `web/lib/api/types.ts`) | two document versions |
| `IDentalRecordRepository` + `DentalRecordRepository` | `GetAppointmentIdsWithRecordAsync` |
| `AppointmentProgressJob.cs` | end pass: fiche → `Completed` |
| `patient-record-modal.tsx` | in-place save, footer variants, adopt, mirror removed, « Tout payé », print wiring, toast action |
| `record/prescription-section.tsx` | print buttons (`onPrint(kind)`) |
| `documents/document-preview-dialog.tsx` | print through `printPdfBlob` |
| `appointment-quick-actions.tsx`, `edit-appointment-dialog.tsx`, `dashboard/day/now-next-cards.tsx`, `app/patients/[id]/page.tsx`, `treatment-plans/plan-next-action.ts`, `treatment-plans/seance-strip.tsx`, `post-visit-review-popup.tsx` (`ficheHref` only), `visits/visit-closure-list.tsx` (`ficheHref` only) | doors through `fiche-door.ts` |
| `web/scripts/check-responsive.mjs` | 2 derived checks (below); N49 classifies `finishSeance` |
| `web/components/CLAUDE.md`, `features/prescription-fiche-de-soins/notes.md` | the mirror and « no print in the fiche » lines are no longer true |

---

## Wave 2 — the secretary collects

### 2.1 The read: « À encaisser »

- `GetSeancesToCollectQuery` stays in **`Features/Appointments`**: a new folder would emit an undeclared realtime key,
  which `RealtimeResourceResolverTests` fails on.
  - Endpoint: `GET /appointments/to-collect?day=YYYY-MM-DD` (default: the Tunis day).
  - Attributes: `AnyClinicRole`, `[HiddenWhenMoneyMasked]`. Add it to `MoneyMaskGateMiddlewareTests`' exact
    expected list.
- **Rows** are the day's visits (not Annulé/Absent) that carry a fiche and still owe money for the séance:
  - `Unbilled`: fiche cost > 0, no **issued** note, not covered by a debt-bearing devis, no « Rien à facturer ».
  - `NoteBalance`: the séance's issued note has an outstanding balance.
  - `Treatment`: a devis-carried séance whose plan has money due now, read from `PatientDebtLines`.
- **Reuse, never re-derive.** The batched reads come from `VisitClosureReader` (add a « fiche present, this day »
  scope beside the worklist's « séance ended » scope). Amounts come from the note and from `PatientDebtLines`, so a
  row always equals what the patient file says.
- **Order:** by `ExpectedEnd`, earliest first (who left the chair first).

### 2.2 Agenda, RDV window, one collection resolver

- **`web/components/visits/use-seance-collection.tsx`** (new) is the one « Encaisser » for a séance. It opens:
  - `Unbilled` → `BillDentalRecordDialog`;
  - `NoteBalance` / `Treatment` → `useDebtLineCollection` (which already re-reads the document and picks the
    échéance).

  Used by the sheet, the RDV window and « À clôturer »'s « Encaisser ». The latter opens `BillDentalRecordDialog`
  directly today, which is wrong for a note issued with a balance.
- **Agenda bar:** an amber « À encaisser · N » pill when N > 0 and `moneyShown`. It opens
  `visits/to-collect-sheet.tsx` (a sheet; full screen below `md:`) with one row per séance and « Encaisser ».
- **Agenda block:** an amount badge on séances in that list (lookup by `appointmentId`), withheld when `!moneyShown`.
- **Realtime:** the pill/sheet read refetches on `appointments`, `invoices`, `patients` and `treatmentplans`,
  debounced.
- **RDV window money row:**
  - With a fiche: « À encaisser : 120,000 DT » + « Encaisser » (resolver), **instead of** « Facturer cette
    consultation ».
  - Without a fiche: unchanged, but see 2.4 (d).
- **No bell notification per séance**, by decision: the live pill is the channel, and a bell row is a second thing
  for reception to clear. `StaffNotification.TargetRole` exists on main if that changes.

### 2.3 Cheque fields in « Facturer cette intervention »

- Mount `ChequeFields` when the method is Chèque, and send through `chequePaymentFields()` (the single builder).
- Check that `POST invoices/from-dental-record/{id}` carries cheque fields through `DentalRecordPaymentRequest`; the
  auto-billing path already sends them.

### 2.4 Money hardening

- **(a) The fiche reads money from the money.** `DentalRecordDto.CollectedOnNote` is the sum of live payments on the
  séance's note, desk included.
  - When the fiche has a note, reopening it shows « Payé » = that figure, not the stale `AmountPaid`.
  - « Encaissé sur le traitement » likewise reads `CollectedOnRecord`.
  - Re-saving unchanged stays `AlreadyBilled` (top-up = typed − collected).
- **(b) « Corriger la note » carries desk payments.**
  - `RetireForCorrectionAsync` voids as today, but first captures payments not made by this fiche
    (`Payment.DentalRecordId != record.Id`).
  - After the replacement note is issued, it re-records them with the same date, method, cheque fields and a « reporté
    de la note n° … » mention. Net zero in la caisse.
  - Refuse before the transaction when one of them is a cheque already deposited: `DentalRecordDeletionReversal`'s
    precedent and wording.
  - The correction preview names what moves.
- **(c) No double on a devis.** A desk installment payment made through the resolver for a séance carries that
  fiche's id (`InstallmentPayment.DentalRecordId`, the existing idempotency key of `CollectedOnRecord`). Add the
  field to `RecordInstallmentPaymentCommand` if absent.
- **(d) One note per séance.**
  - The closure money step counts an **issued** note only; a draft no longer settles it. Grep every
    `InvoiceLinkChoice.ByKey` consumer and decide each one.
  - `BillDentalRecordCommand` refuses with a named code (`seance_has_note`) when a draft or live note already exists
    for the fiche's `AppointmentId` that its line links don't see.
- **(e) Correction needs a doctor or admin.** `UpdateDentalRecordCommand` refuses a `CorrectionReason` from a
  secretary (`correction_requires_doctor`). The modal hides « Corriger la note » for that role and shows the named
  refusal.

### Wave 2 files

| Create | Purpose |
|---|---|
| `api/…/Features/Appointments/Queries/GetSeancesToCollectQuery.cs` (+ DTO) | the day's « à encaisser » |
| `web/components/visits/use-seance-collection.tsx` | the one « Encaisser » for a séance |
| `web/components/visits/to-collect-sheet.tsx` | the agenda's list |

| Modify | Change |
|---|---|
| `AppointmentsController.cs` | `GET to-collect` |
| `VisitClosureReader.cs` / `VisitClosure.cs` | day scope; issued-only billing term |
| `BillDentalRecordCommand.cs` | one note per séance |
| `UpdateDentalRecordCommand.cs` | carry desk payments; correction role |
| `RecordInstallmentPaymentCommand.cs` | optional `DentalRecordId` |
| `DentalRecordDto.cs` + handlers | `CollectedOnNote` |
| `bill-dental-record-dialog.tsx` | cheque fields |
| `appointment-calendar.tsx`, `app/appointments/page.tsx` | pill, badges, realtime keys |
| `edit-appointment-dialog.tsx` | money row via the resolver |
| `visits/visit-closure-list.tsx` | « Encaisser » via the resolver |
| `patient-record-modal.tsx` | « Payé » / treatment figures from the server; « Corriger la note » by role |
| `MoneyMaskGateMiddlewareTests` | the new GET in the expected list |

---

## Key signatures (sketch, not code)

```csharp
// Features/Patients/FicheVisitCompletion.cs — registered like FicheOrdonnanceEmitter
public sealed class FicheVisitCompletion
{
    public Task<FicheVisitCompletionOutcome> ApplyAsync(
        DentalRecord record, bool finishSeance, bool deleteReview, CancellationToken ct);
    public static bool ShouldComplete(Appointment visit, bool finishSeance, DateTime nowUtc)
        => finishSeance || visit.HasEndedBy(nowUtc);
}
public enum FicheVisitCompletionOutcome { NoVisit, LeftRunning, Completed, AlreadyClosed, Contradicted }

// IDentalRecordRepository
Task<HashSet<Guid>> GetAppointmentIdsWithRecordAsync(IReadOnlyCollection<Guid> appointmentIds, CancellationToken ct = default);

// Features/Appointments/Queries
public sealed record GetSeancesToCollectQuery(DateOnly? Day) : IRequest<Result<IReadOnlyList<SeanceToCollectDto>>>;
public sealed record SeanceToCollectDto(
    Guid AppointmentId, Guid PatientId, string PatientName, DateTime StartUtc, string? PractitionerName,
    Guid DentalRecordId, IReadOnlyList<string> ActLabels, SeanceCollectionKind Kind, decimal AmountDue,
    Guid? InvoiceId, string? InvoiceNumber, Guid? TreatmentPlanId);
public enum SeanceCollectionKind { Unbilled, NoteBalance, Treatment }
```

```ts
// web/lib/visits/fiche-door.ts
export function canRecordFiche(visit: Pick<AppointmentDto, "patientId" | "status" | "startDateTime">, todayIso: string): boolean
export function ficheHref(patientId: string, appointmentId: string): string

// patient-record-modal.tsx
type SaveMode = "stay" | "close" | "finish"          // « Enregistrer » in place · today's save · « Terminer la séance »
async function save(mode: SaveMode): Promise<DentalRecordDto | null>

// web/components/visits/use-seance-collection.tsx
export function useSeanceCollection(onSettled: () => void): { collect(row: SeanceToCollectDto): void; dialogs: ReactNode }

// web/lib/api/appointments.ts
toCollect(day?: string): Promise<SeanceToCollectDto[]>
```

## Wiring

- `FicheVisitCompletion` registers next to `FicheOrdonnanceEmitter` in the Application DI.
- The job needs `IDentalRecordRepository` (already in Infrastructure DI).
- No new realtime key, no new notification category, no new feature folder.
- `check:responsive` gains two derived checks, red-proofed first:
  - `fiche-door-has-one-composer`: the literal `addRecord=1` appears only in `lib/visits/fiche-door.ts`.
  - `pdf-print-has-one-way`: `contentWindow.print(` appears only in `lib/print.ts`.
- N49 must classify `finishSeance` as an action flag, or it fails as an unclassified payload key.

## Pitfalls

- **Re-hydration on adopt.** The modal's open effects key on `record`. When `savedRecord` appears, they must **not**
  re-hydrate the form (that would reset the armed card and drop typing done during the request). Guard them with an
  « adopted id » ref and adopt only base + versions.
- **Typing during an in-flight save.** The request sends a snapshot. The base becomes that snapshot, never the live
  screen.
- **Retard.** Completion and the job use `HasEndedBy` / `ExpectedEnd`. A visit with a 15-minute retard is not over
  at its booked end.
- **Footer variant from status, not the clock.** A visit already « Séance passée » or « Terminé » shows the single
  « Enregistrer ».
- **Printing on a tablet does not print.** It delivers the PDF to the system viewer, so word the toast from
  `printPdfBlob`'s return value.
- **Removing the « Payé » mirror breaks e2e money specs** that relied on it. Update the specs and
  `features/e2e-hot-paths/scenarios.md`, then run `node e2e/scripts/check-coverage.mjs`.
- **Mode discret.** The new GET is 403 when money is masked, so the pill must read `moneyShown` and render nothing
  rather than an error.
- **Agenda realtime.** The fiche's `appointments` broadcast precedes the note, so refetch on `invoices` /
  `treatmentplans` too, or the pill lags one save.
- **Correction carry.** La caisse shows a void plus a new payment on the same day. `reconcile-money` must stay
  clean, and a deposited cheque is a refusal, never a silent move.
- **Overlaps.**
  - PR #116 (cabinet de groupe) edits `appointment-calendar.tsx` and `edit-appointment-dialog.tsx`: land after it,
    or rebase.
  - The peer « Enregistrer la séance » (audio → AI notes) design uses « Enregistrer » for recording. Agree its label
    (« Dicter » or similar) so the fiche's save button keeps one meaning.
- **`DentalRecordPostVisitCompletionTests.cs.deferred`** is not compiled. Revive it: today nothing tests that a fiche
  completes its visit.

## Test strategy

**Unit (xUnit + Moq), unfiltered suite as the gate.**

- `FicheVisitCompletionTests`:
  - create mid-séance → stays En cours, review deleted;
  - `FinishSeance` → Terminé;
  - create after `ExpectedEnd` → Terminé;
  - a retard pushes the end;
  - an update with `FinishSeance` → Terminé;
  - Annulé/Absent → Contradicted.
- `AppointmentProgressJobTests`: end pass with a fiche → Completed; without one → AwaitingClosure; one batched fiche
  read per pass.
- Handler maps document versions after commit; a second in-place save sends them and passes.
- `GetSeancesToCollectQueryTests`:
  - unbilled → row;
  - issued note with balance → row;
  - paid → none;
  - cost 0 → none;
  - « Rien à facturer » → none;
  - draft note → still a row;
  - other day / Annulé → none;
  - devis séance → `Treatment` row equal to `PatientDebtLines`.
- `VisitClosureRulesTests`: a draft no longer settles the money step.
- Correction carry:
  - desk payment moved with its date, method and cheque;
  - the fiche's own payment re-billed;
  - deposited cheque → refusal;
  - `CaisseLedgerTests` net unchanged.
- `CollectOnTreatment`: a desk installment tagged with the fiche + a fiche re-save → no double.
- `BillDentalRecordCommand`: an existing draft/live note on the appointment → `seance_has_note`.
- Correction by a secretary → `correction_requires_doctor`.
- `MoneyReadConsistencyTests`, `MoneyMaskGateMiddlewareTests`, `RealtimeResourceResolverTests` green.

**Frontend gate:** `check:responsive` (with the two new checks, red-proofed) + `tsc --noEmit` + `npm run build`, then
the eye pass at 320 / 390 / 820 / 1180 / 1440 and at 730 px tall (the fiche footer and the sheet).

**Browser (e2e hot paths + `/test-in-browser`).** These rules live in `.tsx`, so a wire test cannot prove them
(verification § 4b).

- Wave 1:
  - fiche opened from the agenda on a running visit;
  - « Enregistrer » twice → one fiche, RDV still En cours;
  - « Imprimer l'ordonnance » → one save, then one PDF GET;
  - « Terminer la séance » → Terminé;
  - « Payé » empty → no note;
  - « Tout payé » → note paid.
- Wave 2:
  - the séance appears in « À encaisser » without a reload;
  - « Encaisser » by cheque → note issued, cheque in « Chèques »;
  - doctor re-saves afterwards → no second payment, « Payé » shows the desk money;
  - « Corriger la note » keeps the desk payment;
  - Mode discret → no pill.
- New rows in `features/e2e-hot-paths/scenarios.md` (money rows tier 0).

## Blast radius (draft — finish it before the first edit, per `regression-safety.md`)

| Touching | Other consumers | Verdict |
|---|---|---|
| Fiche create → completion rule | popup, « À clôturer », « Au fauteuil », status chart, job | must re-test |
| Job end pass (fiche → Completed) | agenda statuses, closure worklist, taux d'absence | must re-test |
| « Payé » mirror removed | e2e money specs, `components/CLAUDE.md`, `fiche-merge` paiement section | must change (specs, docs) |
| `DentalRecordDto` additions | modal, patient page, fiche-merge, e2e | unaffected — additive fields |
| `finishSeance` payload key | N49 | must change |
| `InvoiceLinkChoice` / issued-only billing term | VisitClosure, RDV window « Facturé » badge, other `ByKey` callers | must re-test each caller |
| `RetireForCorrectionAsync` carry | caisse ledger, « Chèques », reconcile-money | must re-test + run `reconcile-money` |
| `BillDentalRecordDialog` cheque fields | « À clôturer », patient page, resolver | must re-test |
| Preview print → `printPdfBlob` | séance row, Documents tab, fiche | must re-test |
| `appointment-calendar.tsx`, `edit-appointment-dialog.tsx` | PR #116 | coordinate |

## Decided against (and why, in one line each)

- **Draft fiche state:** no print before the end, and every fiche reader must learn to hide drafts.
- **Per-cabinet switch:** every flow twice, and the money traps stay live in the default mode.
- **Separate ordonnance button:** two writers of one legal paper.
- **Printing the aperçu unsaved:** a prescription with no record behind it.
- **Autosave:** silent saves would hit the billing guards and 409s in the background. Explicit « Enregistrer » plus the
  existing carried draft is enough.
- **Bell row per séance for reception:** the live pill already says it, without a second list to clear.

## Next step

`/break-plan` this file into stories, wave 1 first, in a worktree from `origin/main`. Neither wave adds a migration.

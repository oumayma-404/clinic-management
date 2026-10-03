# Progress: Reste à payer worklist

**Started:** 2026-10-03
**Type:** Small
**Branch:** feature/security-remediation (kept: 3 peer sessions live in this working tree — a checkout would move their files)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build` 0 errors / 0 new warnings in changed files · `dotnet test` **4877 passed / 0 failed** (unfiltered) · `tsc` 0 · `check:responsive` 72/72 · ⏳ `npm run build` NOT run: :3000 is a peer's Next server and a build rewrites `web/.next` under it
- [x] Blast radius closed (10 rows: 6 changed, 3 to re-test, 1 unaffected)
- [x] Tests — 37 added, 0 modified (see Test Plan) · unfiltered suite **4914 passed / 0 failed** / 6 skipped (pre-existing)
- [x] Browser QA — 3 cycles: run-1 RED (2) → run-2 RED (1, tab strip at 820) → run-3 GREEN (28 ✅, 1 ⏭). Widths 320/390/820/1440/1536×730. `npm run build` still not run (peer's `next dev` on `web/.next`)

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PatientDebtLineDto` (+`DueNow`, `DueReason`, `DueSince`, `RunningReason`, `NextInstallmentDue`, `Acts`) | wire record, positional | `PatientDebtLines.Project` (2 ctor sites), `patient-outstanding-strip.tsx`, patient page (3 fns), `types.ts`, `PatientDebtLinesTests` (reads fields, never constructs) | must change ctor sites + TS type; strip/page unaffected — additive fields |
| 2 | `PatientDebtLines.Project` | the « Solde dû » decomposition | « Solde patient » query, new query, `PatientDebtLinesTests`, `MoneyReadConsistencyTests` | must re-test — existing fields unchanged |
| 3 | Selection of live notes + debt devis (extracted to `PatientDebtSelection`) | shared filter | « Solde patient » query + new query | must change both — one owner, so AC-3 holds by construction |
| 4 | `IInvoiceRepository` / `ITreatmentPlanRepository` / `IAppointmentRepository` (+1 batched read each) | interfaces | one implementer each; tests use Moq only | unaffected — no hand-written fakes |
| 5 | `BillingController` (+`GET billing/reste-a-payer`) | new endpoint, `AnyClinicRole` | `MoneyMaskGateMiddlewareTests` (derived « every billing/ GET is marked » + `ExpectedMarked`), `ControllerAuthorizationCoverageTests` | must change — `[HiddenWhenMoneyMasked]` + `ExpectedMarked` row; explicit policy |
| 6 | Realtime keys | query only, folder `Features/Billing` exists | `RealtimeResourceResolverTests` | unaffected — no new area, no command |
| 7 | Payment-dialog openers (re-read + target échéance) | patient page inline logic | patient page + new panel | must change — extracted to one hook used by both |
| 8 | `/a-cloturer` tabs | page | the 3 existing tabs, URL tab key | must re-test — 4th tab wraps (the page already uses `flex-wrap` + basis) |
| 9 | Mode discret (peer feature) | money visibility | Factures/Caisse/Chèques gate | must change — the tab is withheld while money is hidden |
| 10 | `web/lib/api/types.ts` + billing api module | additive types + 1 fetcher | every importer | unaffected — additive |

## Working tree note (start of session)
~205 dirty/untracked files from peer sessions (Mode discret, post-visit presence, offline drafts, QA shots, site/…). Not touched or staged by this feature except the shared files named above (targeted edits, peers notified). Stage by explicit path only.

## Files Changed
- api: `PatientBillingSummaryDto.cs` (+6 line fields, `PatientDebtActDto`), `PatientDebtLines.cs` (due-now split, reasons, acts), **new** `PatientDebtSelection.cs`, `GetPatientBillingSummaryQuery.cs` (uses it), **new** `GetResteAPayerQuery.cs` + `ResteAPayerPageDto.cs`, `BillingController.cs` (+1 GET), `I/InvoiceRepository` + `I/TreatmentPlanRepository` (+`GetByPatientIdsAsync`), `I/AppointmentRepository` (+`GetNextBookingByPatientAsync`)
- tests: `MoneyMaskGateMiddlewareTests.ExpectedMarked` (+1 row — mirrored set)
- web: `lib/api/types.ts`, `lib/api/billing.ts`, `app/a-cloturer/page.tsx`, `app/patients/[id]/page.tsx` (onto the hook), **new** `components/patient/use-debt-line-collection.tsx`, `components/visits/reste-a-payer-list.tsx`, `reste-a-payer-patient-sheet.tsx`, `contact-actions.tsx`
- docs: root `CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md`, Application + API `CLAUDE.md`, `notes.md`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Row DTO carries `dueReason`/`runningReason`, `dueDocument`/`runningDocument`, `nextVisit`/`nextInstallmentDue` instead of the spec's single `reason`/`since`/`nextDate` | a patient can be in both lists, so each list needs its own reason; new endpoint, no other consumer |
| Line DTO fields named `dueReason`/`runningReason`/`dueSince`/`nextInstallmentDue` (spec: `reason`) | same reason — a split devis has one reason per part |
| Table patient name links to the fiche (`PatientNameLink`); « Détail » / card tap open the panel | repo guard N13: a name beside its id is the link to the fiche |
| Panel title is sr-only; the visible name is the fiche link | same guard, and the dialog keeps a real accessible name |

## Deferred to /test-small-feature
All four done — see Test Plan below.

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | Add scenarios | `GetResteAPayerQueryTests` | badge source = `DueCount` (`Both_Cards_Count_…`); policy held by existing `ControllerAuthorizationCoverageTests` + `MoneyMaskGateMiddlewareTests` row |
| AC-2 | Add scenarios | `PatientDebtLinesTests` (+9) | note due in full · only late agreed échéance due · partly-paid late échéance · auto-raised: unfinished = running, finished = due, stopped = due · agreed future échéance on finished work = running · « Reste hors échéancier » |
| AC-2 | New test class | `PatientDebtSelectionTests` (4) | live notes only · Draft devis not debt · bridged devis on the note · draft/cancelled bridge keeps the devis |
| AC-2 | Add scenarios | `GetResteAPayerQueryTests` | Draft devis adds nothing · bridged devis counts once on the note |
| AC-3 | Add scenarios | `MoneyReadConsistencyTests` | `SoldePatientAsync` now also runs « Reste à payer » → every fixture asserts due + running == Solde dû and `0 ≤ DueNow ≤ Outstanding`; +2 fixtures (split note + devis · échéancier outgrew the devis) |
| AC-4 | New test class | `GetResteAPayerQueryTests` (16) | cards over every patient at `pageSize: 1` · search narrows list + both cards |
| AC-5 | New test class | `GetResteAPayerQueryTests` | due oldest first · running next séance/échéance first · both by amount · ties end on `PatientId` · past-the-end page empty, totals kept |
| AC-6 | Add scenarios | `GetResteAPayerQueryTests` | row carries `PhoneE164`, null when no phone |
| AC-7 | Add scenarios | `PatientDebtLinesTests` | devis acts dated oldest first, undated in devis order, teeth/price/done/« 1 étape faite sur 2 » · note acts = its lines, done, issue day |
| AC-8 | Add scenarios | `GetResteAPayerQueryTests` | 200 paid on 450 → row + card 250 · settled note → patient leaves the list |

### Coverage notes (no unit surface)
- AC-1 tab visible to all roles, AC-6 buttons absent (not disabled), AC-7 panel = row, AC-8 no-reload refresh, AC-9 320 px cards + full-screen panel + 44 px, AC-10 empty state: `.tsx` only, no FE test runner — covered by browser QA `qa/run-3.md` (GREEN, 28 ✅) + `tsc` / `check:responsive`.

## Bug found & fixed by the tests
- **What:** a devis with every échéance paid and an act added afterwards owed money on « Solde dû » but was absent from « Reste à payer » (AC-3 broken for that patient).
- **Cause:** candidates came from « Créances »' échéance-row sum (Σ Amount − AmountPaid), which is 0 there.
- **Fix:** new `ITreatmentPlanRepository.GetPatientIdsWithPlanOutstandingAsync` (`TotalPlanned > Σ AmountPaid`, debt-bearing, non-bridged — the projector's own test) replaces that read in `GetResteAPayerQuery`. « Créances » unchanged (out of scope).
- **Tests:** `GetResteAPayerQueryTests.A_Devis_Owing_An_Act_Added_After_…` · `MoneyReadConsistencyTests.A_Devis_Owing_Outside_Its_Echeancier_Still_Adds_Up` · new `PlanOutstandingQueryTranslationTests` (2 — the read compiles to SQL, exclusion included).
- **Dev DB check (read-only SQL):** old and new reads find the same 607 patients — nobody lost, none in the gap today.
- ⚠️ The running API serves the old code until restarted — not restarted here (shared stack).

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit (whole suite, `-c Release`, scratch `BaseOutputPath`) | none | 4914 passed, 0 failed, 6 skipped (pre-existing) |
| New-warning scan on changed files | — | 0 |

## Significant Deviations

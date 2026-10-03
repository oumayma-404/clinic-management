# Progress: Reste à payer worklist

**Started:** 2026-10-03
**Type:** Small
**Branch:** feature/security-remediation (kept: 3 peer sessions live in this working tree — a checkout would move their files)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build` 0 errors / 0 new warnings in changed files · `dotnet test` **4877 passed / 0 failed** (unfiltered) · `tsc` 0 · `check:responsive` 72/72 · ⏳ `npm run build` NOT run: :3000 is a peer's Next server and a build rewrites `web/.next` under it
- [x] Blast radius closed (10 rows: 6 changed, 3 to re-test, 1 unaffected)
- [ ] Tests (handled by /test-small-feature)
- [ ] Browser QA

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
- `PatientDebtLinesTests`: `DueNow` per case — unpaid note = whole; devis agreed late échéance = that part only; auto-raised on finished / stopped / ongoing work; drift cap; reasons + acts order
- `MoneyReadConsistencyTests`: Σ row (due + running) == « Solde patient » total over every fixture (spec AC-3)
- `GetResteAPayerQuery`: list split, both totals over the whole set, search, the four sort branches end on `PatientId`, past-the-end paging
- `PatientDebtSelection`: equals the summary handler's old inline filter

## Significant Deviations

# Blast radius — review fixes, critical findings 1–8 (2026-10-13)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `ClinicWriteLease.PcMustConfirmNumbers` | D16 predicate | `NumberPromiseCoordinator.Plan`, `ClinicWriteLeaseTests`, `NumberPromiseTests` | must change — always false now; tests move to the new rule |
| 2 | `ApplicationDbContext.SaveChangesAsync` | every save | all writers | must re-test — new branch acts only on a PC de secours that holds the saves |
| 3 | `NumberedDocuments` (+ provisional numbers) | numbered types | `IssueInvoiceCommand`, `BillDentalRecordCommand`, `CreateCreditNoteCommand`, `DevisNumbering` (5 callers) | unaffected — rewritten at save, no handler touched (D16's own pattern) |
| 4 | `ClinicChange.Automatic` (new column) | change log row | `ClinicChangeCapture`, `ApplyReturnAsync` raw INSERT (column defaulted), prune, feed | unaffected — additive, NOT NULL DEFAULT false |
| 5 | `RelayCloudChange` (+ `Automatic`) | record | `CloudChangesAfterAsync`, `RelayReturnTests` | must change — trailing optional parameter |
| 6 | `RelayHandbackPlanner.Plan` | the return's decisions | `HandBackRelayCommand`, `RelayReturnTests` | must change — optional owner/cloud rows; old calls keep row-level behaviour |
| 7 | `IRelayHandbackStore.ApplyReturnAsync` | return / gap apply | `HandBackRelayCommand`, `ReturnRelayGapCommand`, test fakes in `RelayReturnTests`, `RelayGapTests`, `RelayFollowerTests` | must change — returns the rows it could not apply |
| 8 | `ListOverruledCutCommandHandler.Lines` | « À reprendre » | `RelayReEnterTests` | must change — every row of the cut, not only roots |
| 9 | `EX_Appointments_NoDoubleBooking` (migration) | DB constraint | appointment create/update (catch 23P01), verify-schema « exists and is partial » | must re-test — `DEFERRABLE INITIALLY IMMEDIATE` checks at statement end, same as today |
| 10 | `ApplyBatchAsync` / `ReplaceAsync` | PC copy apply | follower | unaffected — `SET CONSTRAINTS ALL DEFERRED` only touches deferrable constraints |
| 11 | `RelayCloudClient` downloads/uploads | PC → cloud HTTP | follower, gap, overruled listing, uninstall verb | must re-test — stall timeout; long transfers on a client with no 100 s cap |
| 12 | `RelayHandback.StepAsync` | PC return driver | follower | must change — files sent before saves freeze |
| 13 | `Program.cs` RunsCutJobs | PC's cut jobs | `RelayFenceTests` (derived list) | must change — MonthlyExpenseJob leaves the PC's list |
| 14 | `InstallConfiguration.AddInstallLayers` | config layers | host + 6 console verbs | unaffected — one optional layer added, same order for all |
| 15 | `clinic-setup.iss` WriteInstallConfig | installer | server + relay roles | must re-test — lint + ISCC compile |

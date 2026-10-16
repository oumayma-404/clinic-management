# Staff chat — blast radius

Written before the first edit (2026-10-15, on `feature/staff-chat` @ 8c2b24de). Greps re-run at each part; a row
gains its evidence when its part lands.

| # | Touching | What it is | Other consumers (grep) | Verdict | Part |
|---|---|---|---|---|---|
| 1 | `IRealtimeNotifier` (+`NotifyStaffChatAsync`) | interface | one implementation (`SignalRRealtimeNotifier`); ~12 production callers of the existing method; tests use loose `Mock`s | unaffected — additive member | 1 |
| 2 | `ClinicHub` ctor + `OnConnectedAsync` | every tab's one connection | `ClinicHubTests.cs:41,60` (`new ClinicHub(users.Object)`), `ClinicHubTenantScopeTests`, every realtime screen | must change (2 tests) · must re-test: an `entityChanged` refresh still lands on the agenda | 1 |
| 3 | `RealtimeResourceResolver.ExcludedAreas` | mirrored set (C# ↔ `clinic-hub.ts`) | `RealtimeResourceResolverTests` (both ways + marker test); `Dashboard`, `Backup` precedents | must change — `StaffChat` excluded, no `IDoesNotBroadcast` in the area | 1 |
| 4 | `clinic-realtime-provider.tsx` | the one connection (N45) | every `useClinicRealtime` caller | must change (second `.on`) · must re-test: ordinary refresh + reconnect catch-up | 1 |
| 5 | `ClinicArchiveScope.Excluded` / `ClinicRelayScope.Added` | mirrored scopes | archive/restore, PC de secours capture, `Nothing_Is_Both_Added_And_Excluded` | must change (both, same commit) | 1 |
| 6 | `ApplicationDbContext` filters + DbSets | tenant layer | `TenantScopeFilterTests`, `ClinicPurgePlanTests` | must change (new types only) | 1, 2 |
| 7 | `SubscriptionExemptionCoverageTests` | two-way list | — | must change (`conversations/read`) | 1 |
| 8 | `dashboard-header.tsx` right cluster | every page | bell (`coarse:size-11`), connectivity, avatar | must re-test at 768 / 820 / 1440 px | 1 |
| 9 | `bottom-nav.tsx` | shell bar | every phone screen; admin 5 → 6 items, secretary 4 → 5 | must re-test at 320 / 390 px | 1 |
| 10 | `SessionState` (+`locked`) | session seam | every `useSession()` reader | unaffected — additive field | 5 |
| 11 | `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx` outside-interaction | shared primitives | callers passing their own handler: `backup/clinic-archive-card.tsx`, `letterhead/letterhead-import-dialog.tsx`, `user-management.tsx` | must change · must re-test: those three still behave; outside tap still closes an ordinary dialog | 5 |
| 12 | `appointment-quick-actions.tsx`, `edit-appointment-dialog.tsx` | agenda surfaces | status actions, « Supprimer », footer « ⋯ » | must re-test: status change + delete unchanged | 4 |
| 13 | `VaultBridge.Script`, `WebView_WebMessageReceived` | shell bridge | `confirmIdentity`, `saveFile`, relay methods, `carryDraft` | must change · must re-test: `confirmIdentity`, `saveFile` | 6 |
| 14 | `e2e/scripts/check-coverage.mjs` id regex | e2e guard | every scenarios row | unaffected — one prefix added | 4 |

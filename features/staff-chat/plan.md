# Implementation Plan: Messagerie interne du cabinet

**Status:** APPROVED
**Challenged:** No
**Created:** 2026-10-15
**Spec:** [spec.md](./spec.md) · context: [blueprint.md](./blueprint.md), [exploration.md](./exploration.md)
**Branch / worktree:** `feature/staff-chat` at `.claude/worktrees/staff-chat`, from `origin/main` 8c2b24de. Every
path below is relative to that worktree. ⚠️ Not `feature/security-remediation` — it is 148 commits behind and has
no PC de secours.

## Overview

A new backend area `Features/StaffChat` with three tables, one REST controller, and **targeted** realtime: the hub
gains a per-user group, so a message wakes only its recipients' tabs instead of every colleague's. Chat traffic has
**its own hub event, `staffChatChanged`**, on the tab's one connection — never `entityChanged`: `StaffChat` goes
into `RealtimeResourceResolver.ExcludedAreas`, so no chat command broadcasts clinic-wide through the behaviour and
`RealtimeResourceResolverTests` (which reflects over commands only, and fails on a key declared but not emitted) is
untouched. The event carries **no data**; the client re-reads, exactly as the bell does. Its senders: a message →
its members' user groups; a read → the reader's own group; a quick-message edit or a presence change → the
clinic group.

The browser side is a `StaffChatProvider` mounted in `layout.tsx` (the header remounts on every page, so panel
state, drafts and alerts cannot live there), a non-modal panel that is **not** a `Sheet` (a `Sheet` is modal and
hides the bottom bar), and **one alert owner** (`lib/staff-chat/alerts.ts`) — the only code allowed to play a
sound, draw a notice, call `__clinicShell.notify` or the browser `Notification` API, held by a new
`check:responsive` guard. The Windows app gains one bridge method, `notify`, raising a Windows toast + taskbar
flash.

Key decisions (made, reversible):

| Decision | Choice | Why |
|---|---|---|
| Message entity | `StaffMessage : Entity<Guid>` — **not** an `AggregateRoot` | An aggregate is journaled by `AuditSaveChangesInterceptor`; chat must not be (FR-8). No exclusion-list edit needed |
| Quick-message list | `StaffQuickMessageList : AggregateRoot<Guid>`, one row per clinic, items as an **owned collection** | One `xmin` for the whole list → the PUT's 409 « Recharger » (AC-6.5) is free; audited, as FR-8 asks; no row = defaults, so nothing is seeded or backfilled |
| Read state | `StaffChatReadMarker` — plain class, key `(ClinicId, UserId, ConversationKey)`, `LastReadAt` | `NotificationRead` shape **plus a `ClinicId`**: without one, `ClinicPurgePlanTests` fails (no path to a clinic) |
| Conversation identity | A derived string key, **no conversation table**: `dm:{a}:{b}` · `desk:{owner}` · `clinic`, each id `Uri.EscapeDataString`-encoded | User ids contain `|` (`local|guid`), so a raw separator would collide. Derived keys mean no get-or-create race and nothing to backfill |
| Retry safety | The existing `Idempotency-Key` header (`client.ts` reuses it for an unknown-outcome write; `IdempotencyMiddleware` replays 48 h) | Already covers every write; no `clientMessageId` column (spec API updated) |
| Targeting | `ClinicGroups.User(clinicId, userId)` joined in `ClinicHub.OnConnectedAsync`, and `IRealtimeNotifier.NotifyStaffChatAsync(clinicId, userIds?)` (null = the clinic group) | Deterministic — `Clients.User` would rely on the default `IUserIdProvider` and an inbound claim mapping nobody has tested |
| Realtime event | A dedicated `staffChatChanged` event; `StaffChat` in `ExcludedAreas`; the browser maps it onto the internal key `"staffchat"` **outside** the `RealtimeResource` block | Declaring `staffchat` in `RealtimeResource` needs a command that broadcasts it clinic-wide — the opposite of what a private message must do — and Part 1 has none |
| Presence | `StaffPresence` singleton counting connections per (clinic, user), from `OnConnected/OnDisconnected` | One API process per deployment (no replicas, no backplane); a PC de secours has its own hub and its own count |
| In-app notice | Own component, **not sonner** | It must work over an open dialog (AC-4.2), sit at the top on touch, stay until opened when urgent, and offer three buttons; sonner's action API holds two and nothing in the app has tested a toast over a Radix modal |
| Notice over a dialog | `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx` ignore a pointer-down inside `[data-staff-chat-notice]` | Otherwise Radix reads a press on the notice as « outside » and **closes the fiche** with what was typed in it |
| Phone launcher | A « Messages » button in `bottom-nav.tsx` (shown < 768 px); the header icon is `hidden md:inline-flex` | The header has no room at 320 px; the bar shows exactly where the header icon is hidden |
| Windows toast | `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 (`ToastNotificationManagerCompat`) | Works for an unpackaged WPF app on `net8.0-windows10.0.19041.0`, registers its own AUMID; no shortcut needed |

## Files to Modify/Create

### Files to Create
| File | Purpose |
|------|---------|
| `api/ClinicManagement.Domain/Entities/StaffMessage.cs` | The message: `Create(...)`, `Claim`/`ReleaseClaim` |
| `api/ClinicManagement.Domain/Entities/StaffChatReadMarker.cs` | Per-user, per-conversation « lu jusqu'à » |
| `api/ClinicManagement.Domain/Entities/StaffQuickMessageList.cs` | The cabinet's list + owned `StaffQuickMessage` items; `Replace(items)` validates |
| `api/ClinicManagement.Domain/ValueObjects/StaffConversation.cs` | **The one owner** of keys, membership and who is alerted (FR-1, FR-2) |
| `api/ClinicManagement.Domain/Enums/StaffQuickMessageAudience.cs` | `Dentist = 0, Desk = 1, Reply = 2` — appended only |
| `api/ClinicManagement.Domain/Constants/StaffQuickMessageDefaults.cs` | The 17 defaults; the three built-ins' keys |
| `api/ClinicManagement.Domain/Repositories/IStaffMessageRepository.cs`, `IStaffQuickMessageListRepository.cs` | Repository contracts |
| `api/ClinicManagement.Application/Features/StaffChat/StaffChatRefusals.cs` | Codes + French sentences, one place |
| `api/ClinicManagement.Application/Features/StaffChat/StaffChatRoster.cs` | Loads the caller + colleagues once: who is a dentist (`Doctor.UserId`), who is a secretary, who is active |
| `api/ClinicManagement.Application/Features/StaffChat/StaffChatNotifier.cs` | After commit: `NotifyStaffChatAsync(clinicId, members)`, log-only catch |
| `api/ClinicManagement.Application/Features/StaffChat/Commands/SendStaffMessageCommand.cs` | US-1/2/4 |
| `api/ClinicManagement.Application/Features/StaffChat/Commands/MarkStaffConversationReadCommand.cs` | US-1 |
| `api/ClinicManagement.Application/Features/StaffChat/Commands/ClaimStaffMessageCommand.cs`, `ReleaseStaffMessageClaimCommand.cs` | US-3 |
| `api/ClinicManagement.Application/Features/StaffChat/Commands/SendArrivalNoticeCommand.cs` | US-4 |
| `api/ClinicManagement.Application/Features/StaffChat/Commands/SaveStaffQuickMessagesCommand.cs`, `ResetStaffQuickMessagesCommand.cs` | US-2 — the only clinic-wide broadcasts |
| `api/ClinicManagement.Application/Features/StaffChat/Queries/GetStaffConversationsQuery.cs`, `GetStaffMessagesQuery.cs`, `GetStaffChatUnreadQuery.cs`, `GetStaffColleaguesQuery.cs`, `GetStaffQuickMessagesQuery.cs`, `GetTodayArrivalsQuery.cs` | Reads |
| `api/ClinicManagement.Application/DTOs/StaffChatDtos.cs` | Wire shapes (spec § API) |
| `api/ClinicManagement.Application/Common/Interfaces/IStaffPresence.cs` | `IsOnline(clinicId, userId)` |
| `api/ClinicManagement.Infrastructure/Persistence/Configurations/StaffMessageConfiguration.cs`, `StaffChatReadMarkerConfiguration.cs`, `StaffQuickMessageListConfiguration.cs` | EF mapping + indexes |
| `api/ClinicManagement.Infrastructure/Repositories/StaffMessageRepository.cs`, `StaffQuickMessageListRepository.cs` | Data access |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddStaffChat.cs`, `<ts>_AddStaffQuickMessages.cs` | ⚠️ delete scaffolded `xmin` lines; short names (relay build-column limit) |
| `api/ClinicManagement.API/Controllers/StaffChatController.cs` | `api/staff-chat`, `AnyClinicRole` |
| `api/ClinicManagement.API/Hubs/StaffPresence.cs` | Singleton connection counter |
| `api/ClinicManagement.UnitTests/Domain/StaffConversationTests.cs`, `StaffMessageTests.cs`, `StaffQuickMessageListTests.cs` | Domain rules |
| `api/ClinicManagement.UnitTests/Features/StaffChat/*Tests.cs` (+ `StaffChatTenantIsolationTests.cs`) | Handlers |
| `api/ClinicManagement.UnitTests/Hubs/StaffPresenceTests.cs` | Counting, multi-tab, disconnect |
| `web/lib/api/staff-chat.ts` | `staffChatApi` + its types |
| `web/lib/hooks/use-staff-chat.ts` | Queries (`meta: realtimeMeta.staffChat`), mutations |
| `web/lib/staff-chat/alerts.ts` | **The one alert owner** (sound · notice · shell · browser), cross-tab dedupe |
| `web/lib/staff-chat/chime.ts` | Web Audio tones (normal / urgent), no asset |
| `web/lib/staff-chat/drafts.ts` | One draft per conversation in `sessionStorage`, keyed by user (FR-7) |
| `web/lib/staff-chat/format.ts` | « 14:32 » · « Hier 14:32 » · « 12/10 14:32 » |
| `web/components/staff-chat/staff-chat-provider.tsx` | Context: open/close, active conversation, `openConversation(key)`; mounts panel + notices |
| `web/components/staff-chat/staff-chat-launcher.tsx` | Header icon + badge (`md:` up) |
| `web/components/staff-chat/staff-chat-panel.tsx` | The non-modal panel |
| `web/components/staff-chat/conversation-list.tsx`, `message-thread.tsx`, `message-bubble.tsx`, `message-composer.tsx`, `quick-message-bar.tsx` | Panel parts |
| `web/components/staff-chat/patient-link-picker.tsx`, `patient-link-chip.tsx` | US-4 |
| `web/components/staff-chat/quick-messages-editor-dialog.tsx` | US-2 |
| `web/components/staff-chat/staff-chat-notices.tsx` | The in-app notice stack (US-5) |
| `web/components/staff-chat/alert-settings.tsx` | Son / notifications navigateur / app Windows à mettre à jour |
| `web/components/staff-chat/arrival-action.tsx` + `web/lib/hooks/use-arrival-notice.ts` | « Signaler l'arrivée » for both appointment surfaces |
| `desktop/ClinicManagement.DesktopShell/ShellNotifyMessage.cs` | Pure parser for `notify:{json}` (testable) |
| `desktop/ClinicManagement.DesktopShell/ShellNotifications.cs` | Toast + `FlashWindowEx` + click → activate + open conversation |
| `desktop/ClinicManagement.DesktopShell.Tests/ShellNotifyMessageTests.cs` | Parser tests |
| `e2e/tests/staff-chat.spec.ts` | Hot-path rows CHAT-* |
| `features/staff-chat/notes.md`, `features/staff-chat/blast-radius.md` | What shipped and why; the table below, kept current |

### Files to Modify
| File | Changes |
|------|---------|
| `api/ClinicManagement.API/Hubs/ClinicHub.cs` | Join `ClinicGroups.User(...)`; report connect/disconnect to `StaffPresence`; on a presence change send `staffChatChanged` to the clinic group; `public const string StaffChatChanged = "staffChatChanged"` |
| `api/ClinicManagement.API/Hubs/ClinicGroups.cs` | `User(Guid clinicId, string userId)` |
| `api/ClinicManagement.API/Hubs/SignalRRealtimeNotifier.cs` | `NotifyStaffChatAsync` over `Clients.Groups(...)` / the clinic group, never throws |
| `api/ClinicManagement.Application/Common/Interfaces/IRealtimeNotifier.cs` | + `NotifyStaffChatAsync(Guid clinicId, IReadOnlyCollection<string>? userIds, CancellationToken)` |
| `api/ClinicManagement.Application/Common/Behaviors/RealtimeResourceResolver.cs` | `ExcludedAreas` += `"StaffChat"`, with its reason |
| `web/lib/realtime/clinic-realtime-provider.tsx` | `.on(STAFF_CHAT_EVENT, () => emit("staffchat"))` on the existing connection |
| `api/ClinicManagement.API/Program.cs` | `StaffPresence` singleton as `IStaffPresence` |
| `api/ClinicManagement.Infrastructure/Extensions.cs` | Two repositories (scoped), next to `IStaffNotificationRepository` |
| `api/ClinicManagement.Infrastructure/Persistence/ApplicationDbContext.cs` | DbSets; clinic filters on all three; marker keyed as above |
| `api/ClinicManagement.Infrastructure/Persistence/ClinicArchiveScope.cs` | `Excluded` += `StaffMessage`, `StaffChatReadMarker` (with reasons) |
| `api/ClinicManagement.Infrastructure/Persistence/ClinicRelayScope.cs` | `Added` += the same two, « the clinic's chat (FR-8) » |
| `api/ClinicManagement.UnitTests/Hubs/ClinicHubTenantScopeTests.cs` | `UnfilteredDependencies` += `IStaffPresence` with its reason |
| `api/ClinicManagement.UnitTests/Hubs/ClinicHubTests.cs`, `SignalRRealtimeNotifierTests.cs` | New ctor; user group joined; `NotifyStaffChatAsync` sends `staffChatChanged` to the named user groups only |
| `api/ClinicManagement.UnitTests/Api/SubscriptionExemptionCoverageTests.cs` | Expected exempt list += `conversations/read` |
| `web/lib/realtime/clinic-hub.ts` | `export const STAFF_CHAT_EVENT = "staffChatChanged"` and `STAFF_CHAT_KEY = "staffchat"` — **outside** the `RealtimeResource` block the resolver test parses |
| `web/lib/query/keys.ts` | `queryKeys.staffChat*`, `realtimeMeta.staffChat` |
| `web/app/layout.tsx` | `StaffChatProvider` inside `SidebarProvider`, around `{children}` |
| `web/components/dashboard-header.tsx` | `StaffChatLauncher` in the right cluster, before the bell |
| `web/components/bottom-nav.tsx` | « Messages » button + badge, the « Plus » button's shape |
| `web/lib/auth/session.tsx` | `locked` on `SessionState` (+ `DEFAULT_SESSION`) — the lock is invisible to React today |
| `web/components/ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx` | `onPointerDownOutside`/`onInteractOutside`: ignore `[data-staff-chat-notice]`, then call the caller's handler |
| `web/components/appointment-quick-actions.tsx` | `<ArrivalAction>` between the separator and « Supprimer » |
| `web/components/edit-appointment-dialog.tsx` | `<ArrivalAction>` beside the statut buttons |
| `web/types/clinic-shell.d.ts` | `notify?(n: { title: string; body: string; tag: string; urgent: boolean }): void` |
| `web/scripts/check-responsive.mjs` | **N52 `staff-chat-has-one-alert-owner`** |
| `desktop/ClinicManagement.DesktopShell/VaultBridge.cs` | `notify(n)` on the frozen `__clinicShell`, posting `notify:{json}` |
| `desktop/ClinicManagement.DesktopShell/MainWindow.xaml.cs` | `notify:` branch in `WebView_WebMessageReceived` (clinic-origin gated) |
| `desktop/ClinicManagement.DesktopShell/ClinicManagement.DesktopShell.csproj` | + `Microsoft.Toolkit.Uwp.Notifications` |
| `mobile/shared/bridge.md` | `notify` row (Windows ✅ · Android — · iOS —) + version history |
| `e2e/scripts/check-coverage.mjs` | Id prefix `CHAT` |
| `features/e2e-hot-paths/scenarios.md` | `## HP-n Messagerie interne` rows CHAT-01…08 |
| `CLAUDE.md`, `api/ClinicManagement.API/CLAUDE.md`, `api/ClinicManagement.Infrastructure/CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md`, `web/lib/CLAUDE.md`, `desktop/CLAUDE.md` | One map line each; reasoning in `notes.md` |

## Blast radius (written before the first edit — `regression-safety.md` § 1)

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 1 | `IRealtimeNotifier` | interface, +1 method | 1 implementation; every test double is a loose `Mock` | unaffected — additive |
| 2 | `ClinicHub` ctor + `OnConnectedAsync` | every tab's one connection | `ClinicHubTests` (`new ClinicHub(users)`), `ClinicHubTenantScopeTests`, all realtime screens | must change (2 tests) · must re-test: a realtime refresh on the agenda still lands |
| 3 | `RealtimeResourceResolver.ExcludedAreas` + `clinic-realtime-provider.tsx` | mirrored set (C# ↔ TS) + the one connection | `RealtimeResourceResolverTests` (both ways, `A_Marker_Is_Only_Worn_Where_It_Changes_Something`), N45, every realtime screen | must change — `StaffChat` excluded, **no** `IDoesNotBroadcast` in the area, no `RealtimeResource` entry · must re-test: an ordinary `entityChanged` refresh still lands |
| 4 | `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx` | shared primitives (shape 6) | every dialog/sheet; callers passing their own `onPointerDownOutside`/`onInteractOutside` (grep before editing) | must change · must re-test: outside-tap still closes a dialog; a caller's own handler still runs |
| 5 | `SessionState` + `DEFAULT_SESSION` | session seam | every `useSession()` reader | unaffected — additive field |
| 6 | `bottom-nav.tsx` | shell bar | every phone screen; secretary vs admin item counts | must re-test at 320 px: 6 items (admin), 5 (secretary) |
| 7 | `dashboard-header.tsx` right cluster | every page | the bell (`coarse:size-11`), connectivity, avatar | must re-test at 768 / 820 px: header fits, bell still tappable |
| 8 | `appointment-quick-actions.tsx`, `edit-appointment-dialog.tsx` | agenda surfaces | the status actions, « Supprimer », the footer « ⋯ » | must re-test: status change and delete unchanged |
| 9 | `ClinicArchiveScope.Excluded` / `ClinicRelayScope.Added` | mirrored scopes | archive, restore, PC de secours change capture, `Nothing_Is_Both_Added_And_Excluded` | must change (both, same commit) |
| 10 | `SubscriptionExemptionCoverageTests` | two-way list | — | must change |
| 11 | `VaultBridge.Script` / `WebView_WebMessageReceived` | shell bridge | every shell method; `bridge.md` rule (method set ⇒ doc + version) | must change · must re-test: `confirmIdentity`, `saveFile` unaffected |
| 12 | `check-coverage.mjs` id regex | e2e guard | every scenarios row | unaffected — one prefix added |

## Implementation Stories

### US-1: Write to a colleague and read the answer

**Goal:** From any screen, open « Messages », pick a colleague or « Tout le cabinet », send a text, and see the
badge count what is unread — on desktop, tablet and phone. (Spec US-1, AC-2.3, AC-2.6, FR-1 (two-person +
cabinet), FR-2 (except the secretariat rows), FR-3, FR-7, FR-8, FR-9, FR-10.)
**Blocked by:** None
**Layers:** DB, Domain, Application, API, Hub, UI
**⚠️ Oversized by design** — implement and commit in the four parts below; the part boundary is the split point
(R-6).

**Steps:**

*Part A — model and storage*
1. `Domain/ValueObjects/StaffConversation.cs`: `Direct(a, b)` (sorted, refuses `a == b`), `Clinic`, `Desk(owner)`
   (declared now, used from US-3); `Key` with each id `Uri.EscapeDataString`-encoded; `Parse(key)` →
   `Result<StaffConversation>`; `IsMember(userId, roster)`; `AlertsFor(senderId, roster)` — returns the user ids to
   alert, the sender never included.
2. `Domain/Entities/StaffMessage.cs` (`Entity<Guid>`, private ctor + `static Create`, private setters): `ClinicId`,
   `ConversationKey` (≤ 300), `SenderUserId` (≤ 255, plain string, no FK — `Users` is not archived),
   `Body` (≤ 1000, nullable), `QuickMessageKey`/`QuickMessageLabel`/`IsUrgent` (snapshot, AC-6.4),
   `PatientId?`, `AppointmentId?`, `SentAt`, `ClaimedByUserId?`, `ClaimedAt?`. `Create` trims, refuses blank-and-no-
   quick-message, refuses > 1000.
3. `Domain/Entities/StaffChatReadMarker.cs`: `ClinicId`, `UserId`, `ConversationKey`, `LastReadAt`;
   `Advance(at)` never moves backwards.
4. Configurations: `HasOne<Clinic>().WithMany().HasForeignKey(ClinicId).OnDelete(Cascade)` on both; indexes
   `(ClinicId, ConversationKey, SentAt)`, `(ClinicId, SenderUserId, SentAt)`, `(ClinicId, AppointmentId)`; marker
   key `(ClinicId, UserId, ConversationKey)`.
5. `ApplicationDbContext`: DbSets beside `NotificationReads`; `HasQueryFilter(x => IsSystemWide || x.ClinicId ==
   ScopedClinicId)` on both.
6. `ClinicArchiveScope.Excluded` += both (reason: « keyed on accounts the archive does not carry »);
   `ClinicRelayScope.Added` += both (reason: « the cabinet's chat keeps working during a cut (FR-8) »).
7. Migration: from `api/ClinicManagement.Infrastructure`,
   `BaseOutputPath=<scratch>/build-ef/ dotnet ef migrations add AddStaffChat`; delete every scaffolded `xmin`
   column; `verify-schema` before/after.

*Part B — server*
8. `StaffChatRoster` (Application): from `IUserRepository.GetByClinicIdAsync(clinicId, paging: null)` +
   `IDoctorRepository.GetByClinicIdAsync` → members with `isDentist` (`Doctor.UserId` match, active doctor),
   `isSecretary`, `isActive`, `createdAt`, display name (`FullName ?? Email`).
9. `IStaffMessageRepository` + impl: `AddAsync`, `GetPageAsync(clinicId, key, beforeSentAt, beforeId, take)`
   ordered `SentAt desc, Id desc`, `GetUnreadSummaryAsync(clinicId, userId, keys, sinceFloor)` (one grouped query,
   left-joined to markers), `GetMarkersAsync`, `UpsertMarkerAsync`, `GetLastMessagesAsync(keys)`.
10. `IRealtimeNotifier.NotifyStaffChatAsync` + `SignalRRealtimeNotifier` (user groups, or the clinic group when
    `userIds` is null; swallow + warn); `ClinicGroups.User`; `ClinicHub` joins the user group and reports to
    `IStaffPresence`; `StaffPresence` singleton (`ConcurrentDictionary<(Guid,string), int>`); presence change →
    `staffChatChanged` to the clinic group. `RealtimeResourceResolver.ExcludedAreas` += `"StaffChat"`.
11. `SendStaffMessageCommand`: caller via `IClinicContext.GetUserId()` → `GetByAuth0SubAsync`; parse key;
    membership; recipient active (`staffchat-recipient-inactive`); save; after commit
    `StaffChatNotifier.NotifyAsync(members)` (all members incl. sender's other tabs, log-only catch).
12. `MarkStaffConversationReadCommand` — advances the marker, notifies the **caller's own** user group only (badge
    on their other devices). Controller action `[AllowsWithoutSubscription(...)]`.
13. Queries: `GetStaffColleaguesQuery`, `GetStaffConversationsQuery` (DM keys for every colleague ever written to,
    « Tout le cabinet » when ≥ 3 accounts; last message; unread; `readOnly` when the peer is inactive),
    `GetStaffMessagesQuery` (cursor = `sentAt|id`), `GetStaffChatUnreadQuery` (total + per conversation + 5 most
    recent alerting messages). The account-age floor: unread counts only messages after `max(marker,
    user.CreatedAt)`.
14. `StaffChatController` (`[Route("api/staff-chat")]`, `AnyClinicRole`): the US-1 endpoints of spec § API;
    status picked from `Result.Code`, never from the sentence.

*Part C — browser*
15. `clinic-hub.ts` += `STAFF_CHAT_EVENT` / `STAFF_CHAT_KEY` (outside `RealtimeResource`);
    `clinic-realtime-provider.tsx` maps the event onto `emit(STAFF_CHAT_KEY)`; `keys.ts` += `staffChatUnread`,
    `staffChatConversations`, `realtimeMeta.staffChat = { realtime: [STAFF_CHAT_KEY] }` (widen the meta type if it
    is typed to `RealtimeResource` values — N48 must still pass).
16. `lib/api/staff-chat.ts`, `lib/hooks/use-staff-chat.ts` (unread = shared cached query, the bell's pattern;
    conversations + thread only while the panel is open; refetch on `useClinicRealtime(StaffChat)` and on
    `undefined` = reconnect).
17. `staff-chat-provider.tsx` in `layout.tsx` (inside `SidebarProvider`; `null` on `isChromeLessPath` or no user);
    user id from `useUserStatus().data?.user?.id`.
18. `staff-chat-launcher.tsx` (`hidden md:inline-flex`, `coarse:size-11`, badge like the bell's, accessible name
    « Messages, N non lus ») in `dashboard-header.tsx`; « Messages » button in `bottom-nav.tsx`.
19. `staff-chat-panel.tsx`: fixed `aside`, `inset-y-0 end-0 w-[min(24rem,100vw)]`, full screen below `md:` and at
    200 % zoom, `h-dvh`, no overlay, `print:hidden`, Escape + 44 px ✕, focus to the title, focus back to the
    trigger; list ↔ thread with « ← ».
20. `message-thread.tsx` (newest at the bottom, older on scroll-up, 50 per page, `role="log"`), `message-bubble.tsx`
    (time always visible), `message-composer.tsx` (`text-base md:text-sm`, Enter sends on a fine pointer,
    Shift+Enter newline, button on touch, « Envoi… » / « Non envoyé · Réessayer », draft kept on failure),
    `drafts.ts`, `format.ts`.
21. Mark read when the thread is visible **and** `document.visibilityState === "visible"` and the window has focus.
22. Three empty kinds: « Aucun collègue pour l'instant » (+ « Créer un compte » for an admin), « Aucun message »,
    `LoadFailureNotice` with « Réessayer ».

**Validation:**
- [ ] Unit: `StaffConversationTests`, `StaffMessageTests`, `SendStaffMessageTests`, `MarkReadTests`,
      `StaffChatQueriesTests`, `StaffChatTenantIsolationTests`, `StaffPresenceTests`, updated hub tests — full suite
      unfiltered, green.
- [ ] `verify-schema` clean after the migration; archive + relay scope tests green.
- [ ] `check:responsive` + `tsc --noEmit` + `build` green.
- [ ] Two browsers (admin + a secretary created for the run): a message appears on the other side within ~1 s;
      badge counts, clears on read on both devices; a third colleague's tab does **not** refetch (network panel).
- [ ] 320 / 390 / 820 / 1180 / 1440 + 730 px tall: launcher placement, panel, composer with the phone keyboard.

### US-2: Send a quick message in one click

**Goal:** The 17 dental quick messages appear under the conversation, filtered by recipient and ordered by what
each person sends most; anyone can edit the cabinet's list. (Spec US-2 AC-2.1/2.2/2.5, US-6, FR-4.)
**Blocked by:** US-1
**Layers:** DB, Domain, Application, API, UI

**Steps:**
1. `StaffQuickMessageAudience` enum; `StaffQuickMessageDefaults` (17 items; keys `patient-arrived`,
   `emergency-waiting`, `chair-help` are built-in).
2. `StaffQuickMessageList : AggregateRoot<Guid>` (`ClinicId` unique) with owned `StaffQuickMessage` items (`Key`,
   `Label` 1–40, `Audience`, `AsksForPatient`, `IsUrgent`, `IsBuiltIn`, `SortOrder`); `Replace(items)` enforces:
   unique labels, ≤ 30 items, built-ins present, built-in urgency unchanged.
3. Configuration (`OwnsMany`, the `Patient.AdditionalPhoneNumbers` precedent), DbSet, filter; migration
   `AddStaffQuickMessages` (xmin check). Archived and carried by default — no scope edit.
4. `GetStaffQuickMessagesQuery`: the clinic's list or the defaults; ordered by the caller's own sends of each key
   over the last 60 days (`ClinicClock`), ties by `SortOrder`; returns `version` (0 = defaults).
5. `SaveStaffQuickMessagesCommand` (after commit `NotifyStaffChatAsync(clinicId, null)` — the clinic group, since
   the list is shared), takes `version` → `SetExpectedVersion` → 409 on a stale save;
   `ResetStaffQuickMessagesCommand` deletes the row and notifies the same way.
6. `SendStaffMessageCommand`: resolve `quickMessageKey` against the clinic's list; snapshot label + urgency; refuse
   an unknown key (`staffchat-unknown-quick-message`).
7. Web: `quick-message-bar.tsx` (chips per FR-4's « which buttons » rule — recipient dentist / secretary /
   secretariat / cabinet; `flex-wrap`, `coarse:min-h-11`, one scrolling row on a phone above the composer);
   `quick-messages-editor-dialog.tsx` (copy `procedure-type-steps-dialog.tsx`: `mobile="sheet"`, ↑/↓, audience,
   « demande un patient », urgent locked on built-ins, « Rétablir les messages par défaut », `useConflict` →
   « Recharger »).

**Validation:**
- [ ] Unit: `StaffQuickMessageListTests` (every `Replace` refusal), `QuickMessageOrderingTests` (defaults, usage
      order, ties, 60-day window), `SaveQuickMessagesTests` (stale version → conflict), send-with-quick-key tests.
- [ ] A list edit in one tab refreshes the chips in a colleague's open panel (`staffChatChanged` to the clinic).
- [ ] Browser: send « Patient arrivé » 3× → it moves first for that person only; edit list in one tab, stale save
      in another → « Recharger »; built-in cannot be deleted.

### US-3: Share the desk in a group practice

**Goal:** Each dentist has a « Secrétariat » thread with every secretary; the right people are alerted;
« Je m'en occupe » and « Vu par » stop double work. (Spec US-5, FR-1 secretariat, FR-2 table, FR-6.)
**Blocked by:** US-1
**Layers:** Domain, Application, API, UI

**Steps:**
1. `StaffConversation.Desk(owner)` membership (owner + current secretaries; hidden with no secretary) and the full
   FR-2 `AlertsFor` table; « silent unread » (a secretary's message in a desk thread for the other secretaries) is
   counted in that thread and excluded from `total` (AC-5.3).
2. `GetStaffConversationsQuery`: desk threads per FR-1 (a secretary sees every dentist's; a non-secretary their
   own), always titled « Secrétariat ».
3. `StaffMessage.Claim(userId, at)` / `ReleaseClaim(userId)`; `ClaimStaffMessageCommand` — on
   `ConflictException` from a concurrent claim, re-read and return `staffchat-already-claimed` with the taker's
   name in the French sentence (`ApiError` drops extra body fields); `ReleaseStaffMessageClaimCommand`
   (`staffchat-not-your-claim`). Both notify the thread's members.
4. `seenBy` on the last message: members other than the sender whose marker ≥ `SentAt`.
5. Web: « Je m'en occupe » / « Pris par Salma » / « Je ne peux plus » on the bubble (always visible); « Vu » /
   « Vu par Salma, Amel » under the last message.

**Validation:**
- [ ] Unit: `StaffConversationTests` (every FR-2 row, role change both ways, no-secretary), `ClaimTests`
      (first wins, concurrent → named refusal, release by taker only, non-desk refused), `SeenByTests`.
- [ ] Browser, 3 sessions (dentist + 2 secretaries): « Faites entrer » rings both secretaries; one takes it, the
      other sees « Pris par … »; a secretary's reply rings the dentist only.

### US-4: Link a patient, and announce an arrival from the agenda

**Goal:** A message can carry a patient/appointment shown as a live link; « Signaler l'arrivée » sends « Patient
arrivé » to that appointment's dentist in one press. (Spec AC-2.4, AC-2.5, US-3, FR-3 links, FR-5.)
**Blocked by:** US-2, US-3
**Layers:** Application, API, UI

**Steps:**
1. `SendStaffMessageCommand`: validate `patientId` (`patient.ClinicId == clinicId`) and `appointmentId` (same
   clinic, same patient).
2. Message DTO projection: patient name + `archived`; appointment `startsAt`, dentist name, `cancelled`; a missing
   row → `null` + `missing: true` (« introuvable »).
3. `SendArrivalNoticeCommand`: today (`ClinicClock.TodayRangeUtc`), not cancelled (absent allowed),
   `StaffNotificationRules.ResolveDoctorUserIdAsync` → active user, not the caller; secretary → `desk:{dentist}`,
   anyone else → `dm:{caller}:{dentist}`; already announced (any conversation, `(ClinicId, AppointmentId)` index +
   `patient-arrived` key) and `resend == false` → `staffchat-arrival-already-sent`; counts toward ordering.
4. `GetTodayArrivalsQuery` → `GET /arrivals/today`.
5. Web: `patient-link-picker.tsx` (debounced `patientsApi.list({ searchTerm, limit: 8 })`, then that patient's
   appointments today; failed read ≠ empty), `patient-link-chip.tsx` (real `<a>`, so `plan-workspace`'s
   unsaved-edits guard intercepts it; « Patient archivé » / « RDV annulé » / « introuvable »);
   `use-arrival-notice.ts` (today's arrivals as a cached query, `meta: realtimeMeta.staffChat`) +
   `arrival-action.tsx` (enabled / « Signalé à 09:32 par Salma » + confirm / disabled with the AC-3.4 reason —
   never hidden); wired into `appointment-quick-actions.tsx` and `edit-appointment-dialog.tsx` (the only path on a
   phone and on blocks < 36 px).

**Validation:**
- [ ] Unit: `SendArrivalNoticeTests` (each refusal code, secretary→desk, dentist→DM, resend, absent allowed,
      other-clinic RDV = not found), link-validation tests.
- [ ] e2e CHAT-01/02/05 (below).
- [ ] Browser: arrival from the « ⋯ » menu and from the dialog at 390 px; link opens the RDV; dentist without an
      account → disabled with the reason.

### US-5: Be alerted wherever I am

**Goal:** A message meant for me rings and shows a notice with one-tap replies, even over the fiche de soins;
urgent ones insist; nothing leaks on the lock screen; one sound per device. (Spec US-4 AC-4.1/4.2/4.4–4.8, EC-9,
EC-12, EC-13.)
**Blocked by:** US-2 (replies and urgency are quick messages)
**Layers:** UI (+ session seam, dialog primitives)

**Steps:**
1. `session.tsx`: expose `locked`.
2. `lib/staff-chat/alerts.ts` — the one owner. On a fresh unread summary: diff against the last seen ids; for each
   new alerting message not in the visible open conversation: claim it across tabs (`localStorage`
   `staffchat:alerted:{id}`, try/catch), then sound (urgent tone ignores mute), notice (skipped while `locked`),
   browser `Notification` (tab hidden + permission granted; title/body = sender + label or « Nouveau message »,
   never a patient name or free text). On a reconnect / first load: badge only, at most one sound (AC-4.8).
3. `chime.ts` (Web Audio, unlocked on the first user gesture; « Son bloqué — activer » when suspended).
4. `staff-chat-notices.tsx`: `data-staff-chat-notice`, `pointer-events-auto`, top-end on a fine pointer, top
   full-width on touch, z-index above dialogs and **below** `session-lock-gate`; sender + label (+ « RDV 09:30 »
   for an arrival); « Ouvrir » + the person's first two reply messages; 8 s, urgent stays; `role="status"` /
   urgent `role="alert"`; never takes focus; withdrawn when read anywhere or claimed (`staffchat` refetch).
5. `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx`: ignore outside-interaction from inside
   `[data-staff-chat-notice]`, then run the caller's own handler. « Ouvrir » while a dialog is open queues the
   panel until the dialog closes.
6. `alert-settings.tsx` in the panel: Son on/off (per device, `localStorage`), « Activer les notifications »
   (asks permission on press), the old-shell line (US-6).
7. `check-responsive.mjs` **N52 `staff-chat-has-one-alert-owner`**: `new Notification(`, `AudioContext`,
   `.notify(` on the shell, and `data-staff-chat-notice` rendering — only in `lib/staff-chat/alerts.ts` /
   `staff-chat-notices.tsx`. Prove red with a throwaway violation.

**Validation:**
- [ ] N52 red on a planted violation, green after.
- [ ] Browser: notice over an open fiche de soins → « J'arrive » sends, fiche stays open with its typing; urgent
      rings while muted; lock screen → sound + badge, no notice; two tabs → one sound; back after a disconnect →
      one sound, no burst; screen reader announces (polite / assertive).
- [ ] Regression (blast row 4): outside tap still closes an ordinary dialog; a caller's own outside handler runs.

### US-6: Windows notification and flashing taskbar

**Goal:** In the updated Windows app, a message meant for me raises a Windows notification and flashes the
taskbar when the window is not in front; clicking it brings the app forward on that conversation. (Spec AC-4.3,
EC-10.)
**Blocked by:** US-5
**Layers:** Desktop shell, UI

**Steps:**
1. `ShellNotifyMessage.TryParse(string)` — JSON `{ title ≤ 120, body ≤ 200, tag ≤ 300, urgent }`, refuses
   anything else; tests.
2. `ShellNotifications`: `ToastContentBuilder` (title, body, `tag` as argument; urgent → `Scenario.Reminder`),
   `FlashWindowEx(FLASHW_ALL | FLASHW_TIMERNOFG)` via `WindowInteropHelper`; `ToastNotificationManagerCompat
   .OnActivated` → UI thread → restore (`WindowState.Normal` if minimised) + `Activate()` + `ExecuteScriptAsync(
   "window.__clinicShellOpenStaffChat?.(<json tag>)")`.
3. `VaultBridge.Script`: `notify(n)` posting `notify:` + JSON; `MainWindow.WebView_WebMessageReceived`: `notify:`
   branch behind `IsClinicPage(e.Source)`.
4. `bridge.md`: member row, platform matrix (Android —, iOS —), version history (next `client-v*` patch — CI
   versions from the tag, not the csproj).
5. Web: `types/clinic-shell.d.ts` `notify?`; `alerts.ts` calls it only when `typeof
   window.__clinicShell?.notify === "function"` **and** `!document.hasFocus()`; the provider registers
   `window.__clinicShellOpenStaffChat`; `alert-settings.tsx` shows « Mettez à jour l'application Windows » when the
   bridge exists without `notify`.

**Validation:**
- [ ] `dotnet test desktop/ClinicManagement.DesktopShell.Tests` green (parser tests).
- [ ] On a built shell (isolated profile): window minimised → toast + flash; click → window forward on that
      conversation; window in front → in-app notice only; an old shell → browser path + the update line.

## Testing Strategy

### Unit Tests (`api/ClinicManagement.UnitTests`, unfiltered suite is the gate)
- `StaffConversation`: key round-trip with `|` in ids; `Direct(a,a)` refused; membership per kind; every FR-2 row;
  role change; no secretary.
- `StaffMessage` / `StaffQuickMessageList` / `StaffChatReadMarker`: every `Create`/`Replace`/`Claim` refusal; marker
  never moves back.
- Handlers: non-member → not found; other clinic → not found; inactive recipient; quick key unknown; links
  validated; arrival codes; claim race → named refusal; notifier called with exactly the members.
- Queries: unread floor = account creation; silent unread excluded from total; quick-message ordering; cursor
  paging stable on equal `SentAt` (`Id` tiebreak).
- Hub: user group joined; presence counts multi-tab and disconnect; `NotifyStaffChatAsync` sends
  `staffChatChanged` to the named user groups only (clinic group when null) and never throws.
- Guards that must stay green untouched: `TenantScopeFilterTests`, `ClinicPurgePlanTests`,
  `ControllerAuthorizationCoverageTests`, `AuditInterceptorTests`, `ClinicArchiveScopeTests`,
  `ClinicRelayScopeTests`, `RealtimeResourceResolverTests`.
- Desktop: `ShellNotifyMessageTests`.

### Integration Tests
- None in `UnitTests` (it touches no database). The migration is verified by `verify-schema` before/after; the
  relay/archive scopes by their own model-derived tests.

### E2E Tests (`e2e/tests/staff-chat.spec.ts`; arrange over the API, act in the browser)
Arrange creates a second **dentist** account and a **secretary** account through `POST /api/users` (they never sign
in), and an appointment today for that dentist.
- CHAT-01 (tier 1, browser): admin opens « Messages », writes to the secretary → bubble shows, own badge unchanged.
- CHAT-02 (tier 1, browser): admin « Signaler l'arrivée » on the dentist's RDV from the « ⋯ » menu → lands in the
  admin↔dentist conversation with the RDV link; menu then reads « Signalé à … ».
- CHAT-03 (tier 1, wire): a secretary-sent arrival goes to `desk:{dentist}`; a resend without `resend` → 409.
- CHAT-04 (tier 2, browser): quick message sent 3× moves first in the bar.
- CHAT-05 (tier 2, browser): RDV whose dentist has no account → action present, disabled, reason shown.
- CHAT-06 (tier 2, browser): 320 px — bottom-bar « Messages », panel full screen, composer + chips reachable.
- CHAT-07 (tier 2, browser): notice reply over an open fiche de soins keeps the fiche and its text.
- CHAT-08 (tier 2, wire): deactivated colleague → `staffchat-recipient-inactive`.
Multi-receiver behaviour (who rings, claim, « Vu par ») is covered by the domain tests and the `/test-in-browser`
pass with real separate sessions — e2e has one account and a second context would rotate its refresh cookie.

## Risk Register

| ID | Risk | Likelihood | Impact | Story | Mitigation |
|----|------|------------|--------|-------|------------|
| R-1 | A press on the notice over a Radix modal closes the fiche de soins | High | High | US-5 | Primitive-level ignore of `[data-staff-chat-notice]`; CHAT-07; blast row 4 re-test |
| R-2 | Realtime noise: presence + messages wake every tab | Med | Med | US-1 | Messages target user groups; presence is the only clinic-wide chat event; network-panel check in validation |
| R-3 | Scaffolded `xmin` column in either migration | High | High | US-1, US-2 | Delete by hand; `verify-schema` before/after |
| R-4 | Windows toast silently not shown (AUMID / Velopack path change on update) | Med | Med | US-6 | `ToastNotificationManagerCompat` registers per exe; verify on an installed build after one update; in-app notice still fires |
| R-5 | Ids with `|`/`:` break conversation keys | Med | High | US-1 | Escape-encode each id; round-trip tests with `local|…` and `google-oauth2|…` |
| R-6 | US-1 too big for one session | High | Med | US-1 | Four committed parts (A model · B server · C browser · validation); split at a part boundary |
| R-7 | Unread query slow for a large history (« never deleted ») | Low | Med | US-1 | `(ClinicId, ConversationKey, SentAt)` index; markers bound the scan; `Diagnostics:SlowRequestMs` log |
| R-8 | Two sounds per message (tabs) or none (claim stuck) | Med | Low | US-5 | `localStorage` claim with try/catch and a TTL; fall back to sounding |
| R-9 | Browser autoplay keeps the front-desk tab mute after a reload | High | Med | US-5 | « Son bloqué — activer » in the panel; in US-6 the Windows app passes WebView2 `--autoplay-policy=no-user-gesture-required` |
| R-10 | Merging the shell change auto-releases the Windows app | High | Med | US-6 | `client-installer.yml` ships on any `desktop/**` merge to `main` — merge only with the owner's OK |

### R-1: Notice over a modal dialog
- **Description:** Radix `DismissableLayer` treats a pointer-down outside the dialog as a dismiss; the notice lives
  outside every dialog.
- **Likelihood:** High · **Impact:** High (typed clinical data lost).
- **Mitigation:** the three primitives ignore it; `alert-dialog` too because a confirm can be open.
- **Contingency:** if a focus trap still swallows the press, render the notice's buttons as an `inert`-free portal
  inside the active dialog's container.

### R-4: Windows toast not shown
- **Description:** an unpackaged app's toast depends on an AUMID registration the Toolkit writes per exe path.
- **Mitigation:** test after a Velopack update, not only on a fresh install.
- **Contingency:** set `SetCurrentProcessExplicitAppUserModelID` + a Start-menu shortcut via `vpk --shortcuts`.

### R-6: US-1 is oversized
- **Description:** ~30 files across four layers.
- **Mitigation:** parts A–C each compile and test on their own; commit per part.
- **Contingency:** split at the A/B or B/C boundary into US-1a/US-1b.

## Breaking Changes

None for users — additive. Two internal contracts move and are listed in the blast radius: `IRealtimeNotifier`
(+1 method) and the `ClinicHub` constructor.

## Migrations

### Migration 1: `AddStaffChat` (US-1)
- **What:** `StaffMessages`, `StaffChatReadMarkers` (+ FK to `Clinics`, cascade; indexes above).
- **When:** applied at API start like every migration; `DefersMigrations` profiles at the deferred pass.
- **Rollback:** `dotnet ef database update <previous>` drops both tables (chat lost — acceptable only before
  release).
- **Steps:** scaffold with `BaseOutputPath`; delete `xmin`; `verify-schema` before/after.

### Migration 2: `AddStaffQuickMessages` (US-2)
- **What:** `StaffQuickMessageLists` (unique `ClinicId`) + owned `StaffQuickMessages`.
- **When / Rollback / Steps:** as above. No data: no row = the defaults.

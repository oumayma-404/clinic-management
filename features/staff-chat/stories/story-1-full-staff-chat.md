# Story 1: Full Messagerie interne du cabinet

**Status:** APPROVED
**Story Status:** implemented
**Layer:** Full (owner's choice — one story in six ordered parts; see README)
**Depends On:** None
**Blocks:** None

## Objective

Every member of a cabinet — admin, dentist, secretary — can open « Messages » from any screen without leaving it,
write to a colleague, the secretariat or the whole cabinet, send one of the dental quick messages in one press,
attach a patient or an appointment, announce a patient's arrival to that appointment's dentist from the agenda,
share a request between secretaries with « Je m'en occupe », and be alerted — sound, an in-app notice with one-tap
replies, a Windows notification and a flashing taskbar — even over an open fiche de soins or with the app
minimised.

Detail for every step lives in [plan.md](../plan.md) § « US-N » (Part N here = US-N there); this file is the
execution checklist.

## Acceptance Criteria

_From spec:_

- [x] **Part 1** — AC-1.1 · AC-1.2 · AC-1.3 · AC-1.4 · AC-1.5 · AC-1.6 · AC-2.3 · AC-2.6 · FR-1 (two-person,
      cabinet) · FR-2 (two-person, cabinet rows; account-age rule) · FR-3 · FR-7 · FR-8 · FR-9 · FR-10 · EC-1 · EC-3 ·
      EC-7 · EC-11
- [x] **Part 2** — AC-2.1 · AC-2.2 · AC-2.5 · AC-6.1 · AC-6.2 · AC-6.3 · AC-6.4 · AC-6.5 · FR-4
- [x] **Part 3** — AC-5.1 · AC-5.2 · AC-5.3 · AC-5.4 · FR-1 (secretariat) · FR-2 (secretariat rows) · FR-6 · EC-2 ·
      EC-4 · EC-14
- [x] **Part 4** — AC-2.4 · AC-3.1 · AC-3.2 · AC-3.3 · AC-3.4 · FR-3 (links) · FR-5 · EC-5 · EC-6 · EC-8 · EC-15
- [x] **Part 5** — AC-4.1 · AC-4.2 · AC-4.4 · AC-4.5 · AC-4.6 · AC-4.7 · AC-4.8 · EC-9 · EC-12 · EC-13
- [x] **Part 6** — AC-4.3 · EC-10
- [x] Device & Interface Behaviour table of the spec, at 320 / 390 / 820 / 1180 / 1440 px and 730 px tall

_Story-specific:_

- [x] A message wakes only its recipients' tabs (no clinic-wide refetch for a two-person message)
- [x] Chat traffic rides its own `staffChatChanged` event; `StaffChat` is in `ExcludedAreas`;
      `RealtimeResourceResolverTests` green with allow-lists still empty
- [x] N52 `staff-chat-has-one-alert-owner` proven red on a planted violation, then green
- [x] A press on the notice never closes an open dialog (plan R-1)
- [x] `features/staff-chat/blast-radius.md` exists before the first edit; every `must re-test` row is in the
      `/test-in-browser` plan

## Entry Criteria

Before starting, ensure:

- [ ] Working in `.claude/worktrees/staff-chat` on `feature/staff-chat` (from `origin/main` 8c2b24de) — never the
      main checkout's `feature/security-remediation`
- [ ] `spec.md` APPROVED + challenged, `plan.md` APPROVED
- [ ] `stack-lease.ps1 status` read before starting anything (`shared-stack.md`); a worktree shares the database
      and ports with every peer — use the isolated test stack (:5099 / :3099) when a peer owns the shared one
- [ ] `verify-schema` output captured **before** the first migration

## Steps

### Part 1 — Write to a colleague and read the answer (plan US-1)

1. **Blast radius first** — write `features/staff-chat/blast-radius.md` from plan § Blast radius; run the greps
   for each row (callers of `onPointerDownOutside`/`onInteractOutside`, `new ClinicHub(`, `IRealtimeNotifier`).
2. **Model + storage (Part 1A)** — `StaffConversation` (escape-encoded keys, `Direct`/`Clinic`/`Desk`, `IsMember`,
   `AlertsFor`), `StaffMessage` (`Entity<Guid>`, not an aggregate), `StaffChatReadMarker` (with `ClinicId`),
   configurations + indexes, DbSets + clinic filters, `ClinicArchiveScope.Excluded` and `ClinicRelayScope.Added`
   (same commit), migration `AddStaffChat` with the `xmin` lines deleted. **Commit.**
3. **Server (Part 1B)** — `StaffChatRoster`, `IStaffMessageRepository` + impl,
   `IRealtimeNotifier.NotifyStaffChatAsync` + `SignalRRealtimeNotifier`, `ClinicGroups.User`, `ClinicHub` user group
   + `StaffPresence`, `ExcludedAreas += "StaffChat"`, send / mark-read commands (targeted `staffChatChanged`, no
   `IDoesNotBroadcast` marker — the area is excluded), the four reads (cursor paging `SentAt desc, Id desc`; unread
   floor = account creation), `StaffChatController` (`AnyClinicRole`; read exempt from subscription). Unit tests
   for each. **Commit.**
4. **Browser (Part 1C)** — `clinic-hub.ts` + `keys.ts`, `staff-chat.ts` api, `use-staff-chat.ts`,
   `StaffChatProvider` in `layout.tsx`, header launcher (`md:` up) + bottom-bar « Messages », the non-modal panel,
   list ↔ thread, composer (« Envoi… » / « Non envoyé · Réessayer »), drafts in `sessionStorage`, the three empty
   kinds. Gate + eye pass. **Commit.**

### Part 2 — Quick messages in one click (plan US-2)

5. `StaffQuickMessageAudience`, `StaffQuickMessageDefaults` (17), `StaffQuickMessageList` aggregate with owned items
   and `Replace` rules, migration `AddStaffQuickMessages` (xmin check), quick-messages query (per-person 60-day
   order), save (clinic-wide broadcast, version → 409) and reset commands, send resolves + snapshots the quick
   key; web `quick-message-bar.tsx` + `quick-messages-editor-dialog.tsx` (`useConflict`). Tests. **Commit.**

### Part 3 — Share the desk in a group practice (plan US-3)

6. Desk membership + the full FR-2 alert table + silent unread; desk threads in the conversation list (« Secrétariat »);
   `Claim` / `ReleaseClaim` with the concurrent-claim → named refusal; `seenBy`; bubble actions « Je m'en occupe » /
   « Pris par … » / « Je ne peux plus » and « Vu par … ». Tests. **Commit.**

### Part 4 — Patient link and arrival from the agenda (plan US-4)

7. Link validation + live projection (archived / cancelled / missing); `SendArrivalNoticeCommand` (secretary →
   desk, anyone else → DM; already-sent → 409 unless `resend`); `GET /arrivals/today`; web patient picker, link chip
   (real `<a>`), `use-arrival-notice.ts` + `arrival-action.tsx` in `appointment-quick-actions.tsx` and
   `edit-appointment-dialog.tsx` (never hidden — disabled with the reason). Tests + e2e CHAT-01…05, CHAT-08.
   **Commit.**

### Part 5 — Be alerted wherever I am (plan US-5)

8. `locked` on the session seam; `lib/staff-chat/alerts.ts` (the one owner: cross-tab claim, sound, notice, browser
   notification, catch-up = one sound), `chime.ts`, `staff-chat-notices.tsx` (top, `data-staff-chat-notice`, replies,
   urgent stays, below the lock gate), dialog/alert-dialog/sheet ignore presses on the notice, `alert-settings.tsx`,
   check **N52** (prove red). Browser pass incl. CHAT-06/07 and the blast-radius re-tests. **Commit.**

### Part 6 — Windows notification and flashing taskbar (plan US-6)

9. `ShellNotifyMessage` parser + tests, `ShellNotifications` (toast, `FlashWindowEx`, click → restore + open the
   conversation), `notify` on the bridge script + `notify:` branch, `Microsoft.Toolkit.Uwp.Notifications`,
   `bridge.md` rows, web `notify?` type + feature-detect + `__clinicShellOpenStaffChat` + the old-shell line;
   WebView2 autoplay argument (R-9). Verify on an isolated shell build, after one update. **Commit — do not merge
   to `main` without the owner's OK (the merge auto-releases the Windows app, R-10).**

### Close

10. `features/staff-chat/notes.md` (what shipped and why — the traps: notice over a dialog, `|` in user ids, the
    one alert owner, never-purged history); one map line in each `CLAUDE.md` listed in the plan; scenarios rows +
    `CHAT` prefix in `check-coverage.mjs`.

## Files to Create/Modify

See [plan.md](../plan.md) § « Files to Modify/Create » — the authoritative list, grouped there by layer. By part:

| Part | Creates | Modifies |
|---|---|---|
| 1 | `StaffConversation`, `StaffMessage`, `StaffChatReadMarker`, 2 configurations, `IStaffMessageRepository` + impl, `StaffChatRoster`, `StaffChatNotifier`, `StaffChatRefusals`, send + read commands, 4 queries, `StaffChatDtos`, `IStaffPresence`, `StaffPresence`, `StaffChatController`, migration 1, web api/hook/provider/launcher/panel/list/thread/bubble/composer/drafts/format, tests | `ClinicHub`, `ClinicGroups`, `SignalRRealtimeNotifier`, `IRealtimeNotifier`, `Program.cs`, `Infrastructure/Extensions.cs`, `ApplicationDbContext`, `ClinicArchiveScope`, `ClinicRelayScope`, hub tests, `SubscriptionExemptionCoverageTests`, `clinic-hub.ts`, `keys.ts`, `layout.tsx`, `dashboard-header.tsx`, `bottom-nav.tsx` |
| 2 | audience enum, defaults, `StaffQuickMessageList` + config + repo, save/reset commands, quick-messages query, migration 2, `quick-message-bar.tsx`, `quick-messages-editor-dialog.tsx`, tests | `SendStaffMessageCommand`, `ApplicationDbContext`, `Extensions.cs` |
| 3 | claim/release commands, tests | `StaffConversation`, `StaffMessage`, conversations query, message DTO, `message-bubble.tsx`, `message-thread.tsx` |
| 4 | `SendArrivalNoticeCommand`, `GetTodayArrivalsQuery`, `patient-link-picker.tsx`, `patient-link-chip.tsx`, `use-arrival-notice.ts`, `arrival-action.tsx`, `e2e/tests/staff-chat.spec.ts` | `SendStaffMessageCommand`, message projection, `appointment-quick-actions.tsx`, `edit-appointment-dialog.tsx`, `scenarios.md`, `check-coverage.mjs` |
| 5 | `alerts.ts`, `chime.ts`, `staff-chat-notices.tsx`, `alert-settings.tsx` | `session.tsx`, `ui/dialog.tsx`, `ui/alert-dialog.tsx`, `ui/sheet.tsx`, `check-responsive.mjs` (N52) |
| 6 | `ShellNotifyMessage.cs`, `ShellNotifications.cs`, `ShellNotifyMessageTests.cs` | `VaultBridge.cs`, `MainWindow.xaml.cs`, shell `.csproj`, `bridge.md`, `types/clinic-shell.d.ts`, `alerts.ts`, `alert-settings.tsx` |

## Verification Steps

After **each** part (before its commit):

- [ ] Full backend suite, unfiltered, green
- [ ] `check:responsive` + `tsc --noEmit` + `build` green (parts touching `web/`)
- [ ] `verify-schema` clean (parts 1 and 2)

At the end of the story:

- [ ] `/test-in-browser` plan run GREEN — happy paths of all six parts, the edge cases, two- and three-session
      walks (dentist + 2 secretaries), and one row per `must re-test` line of `blast-radius.md`
- [ ] `node e2e/scripts/check-coverage.mjs` green; `e2e/tests/staff-chat.spec.ts` green on a bootstrapped stack
- [ ] Desktop: `dotnet test desktop/ClinicManagement.DesktopShell.Tests` green; toast + flash checked on an isolated
      installed shell after one Velopack update

**Verification commands:**
```bash
# Backend — the whole suite, never --filter (Smart App Control: Release + temp BaseOutputPath)
BaseOutputPath=<scratch>/build-test/ dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release

# Schema (from api/ClinicManagement.API) — before and after each migration, diff the two
dotnet run -- verify-schema

# Frontend gate
cd web && npm run check:responsive && npx tsc --noEmit && npm run build

# e2e derived guard
node e2e/scripts/check-coverage.mjs

# Desktop shell
dotnet test desktop/ClinicManagement.DesktopShell.Tests -c Release
```

## Exit Criteria

This story is complete when:

- [ ] All six parts committed, each with its gate green at its own commit
- [ ] Every spec AC listed above checked off
- [ ] `/test-in-browser` latest run GREEN (after at most three `/fix-qa-findings` cycles)
- [ ] `notes.md` written; `CLAUDE.md` map lines added
- [ ] Nothing pushed or merged without the owner's OK

## Notes

- **The traps, in the order they bite:** the scaffolded `xmin` column (twice); `|` in user ids inside conversation
  keys; `ClinicHubTenantScopeTests` refusing the presence dependency until it is listed with a reason; and
  `staffchat` landing inside the `RealtimeResource` block — `RealtimeResourceResolverTests` parses that block and
  fails on a key no command emits, which is why chat rides its own `staffChatChanged` event and its constants
  live **outside** the block (decided 2026-10-15, plan § Overview). An `IDoesNotBroadcast` on a StaffChat command
  would fail `A_Marker_Is_Only_Worn_Where_It_Changes_Something`, since the area is excluded.
- `ApiError` drops extra body fields — every refusal's useful detail (« Déjà pris par Salma », « Signalé à 09:32 par
  Salma ») is in the French sentence, and the client branches on `code`, never on the sentence.
- Never log a message body (`LogTemplateCoverageTests` will not catch it).
- The shell's shipped version comes from the next `client-v*` tag, not the csproj — feature-detect `notify` by method.

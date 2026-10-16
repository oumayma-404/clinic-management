# Messagerie interne (staff chat) — implementation blueprint

> ⚠️ **Historical input.** `spec.md` (WHAT) and `plan.md` (HOW) supersede this file wherever they differ — notably
> the realtime event, retention, the arrival routing and the notice. Kept for the option analysis only.

> Status: **blueprint** (`/think-solution`, 2026-10-15). Option chosen: **1 · global panel + Windows alerts through
> the existing shell**. Rejected: a menu tab (leaves the fiche), a separate Windows app (second login/TOTP/update
> channel, nothing on phone/tablet), the bell (one-way). Next step: `/define-small-feature` or `/plan-feature`.

## Summary

A « Messages » panel opened from a 💬 button beside the bell, on every screen, never modal. Doctor ↔ secretary
conversations, a « Secrétariat » thread per dentist for group practices, and a « Tout le cabinet » thread. One-click
**messages rapides** ordered by what *each person* sends most. A message can carry a patient / RDV. Alerts: chime +
in-app toast, a Windows toast + taskbar flash through `window.__clinicShell`, a browser notification otherwise.

## What exists today (measured)

| Area | Fact | Consequence |
|---|---|---|
| Realtime | `ClinicHub` joins only `clinic-{id}` (`Hubs/ClinicGroups.cs:10`); payload is a key, never data | Needs a **per-user group**, or every DM refetches every colleague's tab |
| `IRealtimeNotifier` | One method, clinic-wide (`Application/Common/Interfaces/IRealtimeNotifier.cs`) | Add a targeted method |
| Roles | `admin` · `doctor` · `secretary` (`User.cs:23-36`); one clinic per user; `User.Id` is a string (`local|guid`) | Sender/recipient ids are `string` |
| Dentist ↔ user | `Doctor.UserId` nullable (`Doctor.cs:15`); `Appointment.DoctorId` nullable | « Patient arrivé » routing must handle « no linked user » |
| Directory | `ListUsersQuery` is admin-only (`ListUsersQuery.cs:56`) | A new colleague read, open to all roles |
| Waiting room | No « arrivé » status, no chair/room entity | The chat does **not** add one — a message is not a clinical state |
| Desktop shell | Bridge = `version`, `platform`, `confirmIdentity`; message switch at `MainWindow.xaml.cs:468-497`; no toast/flash/tray | New bridge method `notify`, `bridge.md` row, version bump |
| Phone push | Not wired on any device; impossible on `SelfHostedLan` (`DeploymentProfile.PermitsOsPush`) | Phones get in-app alerts only, while the app is open |
| Web | No `Notification` API, no service worker, no sound anywhere | Alert code is new, and must have **one owner** |
| `Sheet` | Modal: overlay + `body[data-sheet-open]` hides `BottomNav` | The panel is **not** a `Sheet` |
| `/settings` | Hidden from secretaries (`nav.ts:187`) | Quick-message editor lives **in the panel** |
| Bottom nav | 4 tabs + « Plus », no slot | Launcher stays in the header on every width |

## Decisions (made, reversible)

| Question | Decision | Why |
|---|---|---|
| Conversations | Three kinds, **derived keys, no conversation table**: `dm:{a}\|{b}` (sorted ids) · `desk:{userId}` (that person + all secretaries) · `clinic` | No get-or-create race, no unique index, nothing to backfill |
| Who is alerted | DM → the other person · `desk` from the dentist → every secretary · `desk` from a secretary → the dentist only (other secretaries: silent unread) · `clinic` → everyone but the sender | A secretary's reply must not ring her colleagues |
| « Je m'en occupe » | On a `desk` message from the dentist; first claim wins; the second gets « Déjà pris par Salma » | Group practice: nobody does the job twice |
| Quick messages | **Clinic list**, code defaults until the clinic edits it (no seeding, no backfill); each person's chips ordered by **their own** sends, last 60 days | « the ones sent the most » differs per person |
| Who edits them | Every role, from the panel; « Rétablir les messages par défaut » | Secretaries can't reach `/settings`; the edit is audited |
| Patient on a message | Optional `PatientId` / `AppointmentId`, shown as a chip deep-linking to the file / agenda (role-gated by `isNavItemVisible`) | « Patient arrivé — Sana B. 09:30 » is the #1 message |
| Toast text outside the app | **Sender + chip label only** (« Salma : Patient arrivé »), never a patient name or free text | Same rule as the phone lock screen (AC-47); a front-desk screen is seen by patients |
| Retention | **Never purged** (owner, 2026-10-15) — deleted only with the cabinet. No purge job | Owner's call; `spec.md` FR-8 is the authority |
| Audit journal | `StaffMessage` + read markers **excluded**; quick-message edits **audited** | A chat line is not « a person decided something » |
| Clinic archive | `StaffMessage`, read markers excluded; `StaffQuickMessage` **included** | Keyed on users the archive does not carry; the chip list is clinic config |
| PC de secours | `StaffMessage` + read markers added to `ClinicRelayScope.Added` (on `origin/main`), like `StaffNotification` | The chat must work during an internet cut (`spec.md` FR-8) |
| Notice text | Sender + quick label or « Nouveau message » everywhere outside the panel, in-app included | A front-desk screen is seen by patients; the toaster draws above the lock screen |
| Phone launcher | Bottom-bar « Messages » tab under 640 px | Header has no room at 320 px |
| Send idempotency | `clientMessageId` on send and arrival | A retry after a lost answer must not duplicate |
| Expired subscription | No exemption — history readable, sending refused with the usual 402 banner | Same rule as every other write |
| Presence (● online) | In-memory, from hub connect/disconnect; « connecté » only, not « disponible » | One API process in both deployments |
| Edit / delete a message | Not offered | Not asked; append-only keeps it simple |

## Default quick messages (code constant `StaffQuickMessageDefaults`)

| Key | Label (chip) | Shown in | Asks for a patient |
|---|---|---|---|
| `patient-arrived` | Patient arrivé | to a dentist | yes (RDV) |
| `patient-late` | Patient en retard (10 min) | to a dentist | yes |
| `patient-cancelled` | Le patient a annulé | to a dentist | yes |
| `lab-on-phone` | Le labo au téléphone | to a dentist | no |
| `call-waiting` | Appel en attente pour vous | to a dentist | no |
| `emergency-waiting` | Urgence en salle d'attente | to a dentist | no |
| `send-next` | Faites entrer le patient suivant | to the desk | no |
| `chair-help` | Besoin d'aide au fauteuil | to the desk | no |
| `prepare-checkout` | Préparez l'encaissement | to the desk | yes |
| `book-control` | RDV de contrôle dans 1 semaine | to the desk | yes |
| `call-lab` | Appelez le labo | to the desk | no |
| `bring-file` | Apportez-moi le dossier / la radio | to the desk | yes |
| `running-late` | J'ai 10 min de retard | to the desk | no |
| `ok` · `coming` · `two-min` · `not-now` | OK · J'arrive · 2 min · Pas maintenant | replies (everywhere) | no |

`Audience` = `ToDentist | ToDesk | Reply`. A DM picks chips by the *recipient's* role (dentist = has a linked
`Doctor`, not `role == doctor` — an admin is often the dentist).

## Files to create

### Domain (`api/ClinicManagement.Domain/`)
- `Entities/StaffMessage.cs` — `AggregateRoot<Guid>`: `ClinicId`, `ConversationKey`, `SenderUserId`, `Body`,
  `QuickMessageKey?`, `PatientId?`, `AppointmentId?`, `SentAt`, `ClaimedByUserId?`, `ClaimedAt?`.
  `static Create(...)`, `Claim(userId, at)` → refuses when already claimed or not a `desk` message.
- `Entities/StaffQuickMessage.cs` — `Entity<Guid>`: `ClinicId`, `Key`, `Label`, `Audience`, `AsksForPatient`,
  `SortOrder`.
- `Entities/StaffChatReadMarker.cs` — composite key `(UserId, ConversationKey)`, `ClinicId`, `LastReadAt`;
  **copy `NotificationRead`'s shape** (per-user marker, `SkipsConcurrencyToken`).
- `ValueObjects/StaffConversation.cs` — `Direct(a, b)` · `Desk(userId)` · `Clinic`; `Key`, `Parse(key)`,
  `IsMember(userId, secretaryIds)`, `AlertsFor(senderId, members, secretaryIds)`. **The one owner** of membership
  and alert rules.
- `ValueObjects/StaffQuickMessageAudience.cs` (enum, `HasConversion<int>`, appended-only).
- `Constants/StaffQuickMessageDefaults.cs` — the table above.
- `Repositories/IStaffMessageRepository.cs`, `IStaffQuickMessageRepository.cs`.

### Application (`api/ClinicManagement.Application/Features/StaffChat/`)
- `Commands/SendStaffMessageCommand.cs` — `IDoesNotBroadcast`; validates membership, quick key, patient/RDV
  belong to the clinic; after commit → `NotifyUsersAsync(members, "staffchat")`.
- `Commands/ClaimStaffMessageCommand.cs` — `IDoesNotBroadcast`; notify `desk` members.
- `Commands/MarkStaffConversationReadCommand.cs` — `IDoesNotBroadcast`; notify **the caller's own** other tabs.
- `Commands/SendArrivalNoticeCommand.cs` — from an appointment: resolves `Appointment.DoctorId → Doctor.UserId`;
  a secretary posts into `desk:{userId}`, anyone else into `dm:{sender}|{userId}` (`spec.md` AC-3.2); « already
  announced » is per **appointment**, across conversations; refusal codes per `spec.md` § API.
- `Commands/SaveStaffQuickMessagesCommand.cs` — replace-whole-list (dedicated endpoint, `[]` allowed);
  **broadcasts clinic-wide through the behaviour** — this is what makes `staffchat` an emitted key.
- `Commands/ResetStaffQuickMessagesCommand.cs` — deletes the clinic's rows → defaults again.
- `Queries/GetStaffConversationsQuery.cs` — list + last message + unread count + presence + members.
- `Queries/GetStaffMessagesQuery.cs` — cursor page `(SentAt desc, Id desc)`, 50.
- `Queries/GetStaffChatUnreadQuery.cs` — total + per conversation + the newest unread messages (drives badge,
  chime and toast).
- `Queries/GetStaffQuickMessagesQuery.cs` — clinic list or defaults, ordered by the caller's 60-day usage, ties by
  `SortOrder`.
- `Queries/GetStaffColleaguesQuery.cs` — name, role, « is a dentist », presence; open to every role.
- `DTOs/StaffChatDtos.cs`.
- `Common/Interfaces/IStaffPresence.cs` — `IsOnline(clinicId, userId)`.

### Infrastructure
- `Persistence/Configurations/StaffMessageConfiguration.cs` — indexes `(ClinicId, ConversationKey, SentAt)`,
  `(ClinicId, SenderUserId, SentAt)`; `Body` max 1 000.
- `Persistence/Configurations/StaffQuickMessageConfiguration.cs`, `StaffChatReadMarkerConfiguration.cs`.
- `Repositories/StaffMessageRepository.cs`, `StaffQuickMessageRepository.cs`.
- `Migrations/<ts>_AddStaffChat.cs` — **delete the scaffolded `xmin` lines**.
- ~~`StaffChatPurgeJob`~~ — dropped: messages are never purged (`spec.md` FR-8).

### API
- `Controllers/StaffChatController.cs` — `api/staff-chat`, any clinic role:
  `GET conversations` · `GET messages?conversation=&before=` · `POST messages` · `POST messages/{id}/claim` ·
  `POST conversations/read` · `POST arrival/{appointmentId}` · `GET|PUT|DELETE quick-messages` · `GET colleagues` ·
  `GET unread`. Conversation keys travel in query/body, never the path (`|` in ids).
- `Hubs/StaffPresence.cs` — singleton, connection counts per `(clinic, user)`; implements `IStaffPresence`.

### Web
- `lib/api/staff-chat.ts`, `lib/hooks/use-staff-chat.ts` (unread query with `meta: realtimeMeta.staffChat`;
  thread query only while open).
- `components/staff-chat/staff-chat-provider.tsx` — open state, active conversation, **the only alert owner**
  (chime, toast, `__clinicShell.notify`, `Notification`).
- `components/staff-chat/staff-chat-launcher.tsx` — 💬 + badge, beside the bell.
- `components/staff-chat/staff-chat-panel.tsx` — fixed `aside`, right, `w-[min(24rem,100vw)]`, `h-dvh`, no
  overlay; full screen under `md`; Escape + visible 44 px ✕; focus to title; `print:hidden`.
- `components/staff-chat/conversation-list.tsx`, `message-thread.tsx`, `quick-message-chips.tsx`,
  `message-composer.tsx`, `patient-ref-chip.tsx`, `quick-messages-editor-dialog.tsx` (copy
  `procedure-type-steps-dialog.tsx`: `mobile="sheet"`, up/down reorder).
- `lib/staff-chat/chime.ts` — Web Audio two-tone, no asset file.

### Desktop shell
- `desktop/ClinicManagement.DesktopShell/ShellNotifier.cs` — Windows toast + `FlashWindowEx` when not foreground;
  toast click → restore window + post `staffchat:open:{conversationKey}` back to the page.

## Files to modify

| File | Change |
|---|---|
| `api/.../Hubs/ClinicHub.cs` | On connect also join `ClinicGroups.User(clinicId, userId)`; report connect/disconnect to `StaffPresence` (no DB, no tenant scope needed) |
| `api/.../Hubs/ClinicGroups.cs` | `User(clinicId, userId)` |
| `Application/Common/Interfaces/IRealtimeNotifier.cs` | `NotifyUsersAsync(Guid clinicId, IReadOnlyCollection<string> userIds, string resource, CancellationToken)` — must not throw |
| `api/.../Hubs/SignalRRealtimeNotifier.cs` (+ any fake/no-op implementation) | Implement it over `Clients.Groups(...)` |
| `Infrastructure/Persistence/ApplicationDbContext.cs` | 3 `DbSet`s, clinic query filters, marker `SkipsConcurrencyToken` |
| `Infrastructure/Persistence/AuditSaveChangesInterceptor.cs` | Exclude `StaffMessage`, `StaffChatReadMarker` |
| `Infrastructure/Persistence/ClinicArchiveScope.cs` | Exclude `StaffMessage`, `StaffChatReadMarker` |
| `Infrastructure/Extensions.cs` | Register the two repositories (scoped) |
| `API/Program.cs` | `StaffPresence` singleton |
| `web/lib/realtime/clinic-hub.ts` | Declare `staffchat` |
| `web/lib/query/keys.ts` | `queryKeys.staffChat*`, `realtimeMeta.staffChat` |
| `web/app/layout.tsx` | Mount `StaffChatProvider` inside `QueryProvider` + `ClinicRealtimeProvider`; skip `isChromeLessPath` |
| `web/components/dashboard-header.tsx` | Launcher in the right cluster, `coarse:size-11` like the bell |
| `web/components/appointment-quick-actions.tsx` | « Signaler l'arrivée » → `POST arrival/{id}`; when no linked dentist, the item stays and says why |
| `web/lib/api/client.ts` (shell typing) | `notify?(...)` on the bridge type; feature-detect before calling |
| `desktop/.../MainWindow.xaml.cs` | Inject `notify` into the bridge script; `notify:` case in the message switch |
| `desktop/.../*.csproj` | Version 1.3.0 → **1.4.0** |
| `mobile/shared/bridge.md` | `notify` row (Windows ✅, Android/iOS ❌) |
| `web/scripts/check-responsive.mjs` | **N50** `staff-chat-has-one-alert-owner` |
| `features/e2e-hot-paths/scenarios.md` | `CHAT-*` rows (below) |
| `api/.../CLAUDE.md`, `web/components/CLAUDE.md`, `desktop/CLAUDE.md`, root `CLAUDE.md` | One-line map entries; reasoning goes to `features/staff-chat/notes.md` |

## Key signatures

```csharp
// Domain
public sealed record StaffConversation(StaffConversationKind Kind, string? UserA, string? UserB)
{
    public static StaffConversation Direct(string a, string b);   // sorts a/b; refuses a == b
    public static StaffConversation Desk(string ownerUserId);
    public static readonly StaffConversation Clinic;
    public string Key { get; }
    public static Result<StaffConversation> Parse(string key);
    public bool IsMember(string userId, IReadOnlySet<string> secretaryIds);
    public IReadOnlyCollection<string> AlertsFor(string senderId, IReadOnlyCollection<string> clinicUserIds,
        IReadOnlySet<string> secretaryIds);
}

public sealed class StaffMessage : AggregateRoot<Guid>
{
    public static StaffMessage Create(Guid clinicId, StaffConversation conversation, string senderUserId,
        string? body, string? quickMessageKey, Guid? patientId, Guid? appointmentId, DateTime sentAtUtc);
    public Result Claim(string userId, DateTime atUtc);   // code: "staffchat-already-claimed"
}

// Application
public sealed record SendStaffMessageCommand(string ConversationKey, string? Body, string? QuickMessageKey,
    Guid? PatientId, Guid? AppointmentId) : IRequest<Result<StaffMessageDto>>, IDoesNotBroadcast;

// Realtime
Task NotifyUsersAsync(Guid clinicId, IReadOnlyCollection<string> userIds, string resource,
    CancellationToken cancellationToken = default);
```

```ts
// web/lib/realtime + shell
type ClinicShell = { version: string; platform: string; confirmIdentity?: …; notify?: (n: { title: string; tag: string }) => void };
```

## Wiring

- Repositories → `Infrastructure/Extensions.cs` (scoped). `StaffPresence` → singleton, registered as `IStaffPresence`.
- ~~`staffchat` emitted through `RealtimeBroadcastBehavior` + `IDoesNotBroadcast`~~ — **superseded by `plan.md`**:
  chat rides its own `staffChatChanged` hub event, `StaffChat` is in `ExcludedAreas`, and
  `IRealtimeNotifier.NotifyStaffChatAsync(clinicId, userIds?)` sends it. `plan.md` is the authority from here on.
  Both directions of `RealtimeResourceResolverTests` stay satisfied with empty allow-lists.

## Potential pitfalls

- **Hub has no tenant scope.** `StaffPresence` reads nothing from the DB; never inject a filtered repository into
  `ClinicHub` (`ClinicHubTenantScopeTests`).
- **`IRealtimeNotifier` has more than one implementation** (test fakes, possibly a no-op) — grep before adding the
  method.
- **Two tabs, two chimes.** The provider claims each message id in `localStorage` (try/catch) before alerting; the
  visible tab wins.
- **Desk alert rule lives in `StaffConversation.AlertsFor` only.** A second copy in the handler or the browser is
  the repo's dominant defect shape.
- **« Is a dentist » ≠ `role == doctor`.** An admin with a linked `Doctor` is a dentist; read `Doctor.UserId`.
- **Arrival with no linked dentist** (`DoctorId` null, imported RDV, or `Doctor.UserId` null): the menu item stays
  and names the reason in French; never hidden.
- **Concurrent claims.** Both load unclaimed → the second save hits `xmin` → `ConflictException`. Catch-alls carry
  `when (ex is not ConflictException)`; the client re-reads and shows « Déjà pris par … ».
- **Paging.** `ORDER BY SentAt DESC, Id DESC` — never `SentAt` alone.
- **Time.** `ClinicClock` for « today » labels and the 60-day usage window; never `DateTime.UtcNow`.
- **Header width at 320 px.** One more icon in the right cluster: eye pass at 320/390 before anything else.
- **Panel is not a `Sheet`.** A `Sheet` sets `data-sheet-open`, hides `BottomNav` and holds the post-visit popup.
- **An incoming message never opens the panel by itself** (frontend-web § 5: an unrequested interruption never takes
  over the screen). Toast + badge only.
- **Composer**: `text-base md:text-sm` (iOS zoom); keep the draft on send failure; disable while sending.
- **Chips**: wrap, `coarse:min-h-11`, never `.touch-target`; long labels wrap — `Button` does not shrink by default.
- **No helper text** under any control.
- **Shell feature-detect.** An older shell (1.3.0) has no `notify`; the page falls back to the browser path. The
  owner's installed shell reports 1.0.9 — test the fallback on it.
- **Browser `Notification` permission** is asked from a button in the panel, never on page load.
- **Migration**: `verify-schema` before and after; check for scaffolded `xmin`.

## Test strategy

### Unit (`api/ClinicManagement.UnitTests/`, unfiltered suite is the gate)
| Class | Asserts |
|---|---|
| `Domain/StaffConversationTests` | key round-trip; `Direct(a,a)` refused; membership per kind; `AlertsFor` per kind and sender role |
| `Domain/StaffMessageTests` | `Claim` once; second claim refused with code; claim refused on non-desk |
| `Features/StaffChat/SendStaffMessageTests` | non-member refused; unknown quick key refused; patient/RDV of another clinic refused; notifies exactly `AlertsFor` + sender's other tabs; empty body + no quick key refused |
| `Features/StaffChat/SendArrivalNoticeTests` | routes to the RDV's dentist desk; `staffchat-no-linked-dentist` when unlinked |
| `Features/StaffChat/StaffQuickMessageOrderingTests` | defaults when none saved; usage order per user; ties by `SortOrder`; window uses `ClinicClock` |
| `Features/StaffChat/StaffChatTenantIsolationTests` | every query/command refuses another clinic's ids |
| `Hubs/SignalRRealtimeNotifierTests` (extend) | user groups targeted; never throws |
| Existing guards (no edit, must stay green) | `RealtimeResourceResolverTests`, `TenantScopeFilterTests`, `ControllerAuthorizationCoverageTests`, `SubscriptionExemptionCoverageTests`, archive-scope test, `ClinicHubTenantScopeTests` |

### Desktop shell (`ClinicManagement.DesktopShell.Tests`)
- `notify:` message parsed; malformed payload ignored; no toast text beyond title.

### Frontend gate
- `npm run check:responsive` (+ N50 proven red first) · `npx tsc --noEmit` · `npm run build` · eye pass 320/390/820/1180/1440.

### e2e (`features/e2e-hot-paths/scenarios.md`, browser rows)
| Id | Tier | Scenario |
|---|---|---|
| CHAT-01 | 1 | Secretary « Signaler l'arrivée » on an RDV → that dentist's badge +1, thread shows the patient chip, chip opens the RDV |
| CHAT-02 | 1 | Dentist « Faites entrer » to Secrétariat → two secretaries alerted; one « Je m'en occupe » → the other sees « Pris par … » |
| CHAT-03 | 1 | Secretary's reply in a desk thread → dentist alerted, other secretary not |
| CHAT-04 | 2 | Quick messages reorder by the sender's own usage after sends |
| CHAT-05 | 2 | RDV with no linked dentist → item present, French reason, no send |
| CHAT-06 | 2 | Panel at 320 px: chips wrap, composer visible, ✕ reachable, header not overflowing |

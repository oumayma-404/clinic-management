# Messagerie interne du cabinet — what shipped

The accounts of one cabinet write to each other from every screen: two-person conversations, « Secrétariat de
Dr X » for a group practice, and « Tout le cabinet ». One-click dental quick messages, a patient and an appointment
attached to a message, « Signaler l'arrivée » from the agenda, and alerts that reach the person on another screen,
over the fiche de soins, or in the minimised Windows app. Built in six parts on `feature/staff-chat`
(`8e7f024b` … `362e6c1d`); spec [`spec.md`](spec.md), plan [`plan.md`](plan.md), progress
[`stories/progress.md`](stories/progress.md).

## The pieces

| Layer | Piece |
|---|---|
| Domain | `StaffMessage` (an `Entity<Guid>`, **not** an aggregate), `StaffConversation` (value object: the derived key + the FR-2 alert table, `Alerts()`), `StaffChatReadMarker`, `StaffQuickMessageList` (aggregate, owned items), `StaffQuickMessageDefaults`, `StaffQuickMessageAudience` |
| Application | `Features/StaffChat`: send · mark read · claim / release · send arrival · save / reset quick messages; conversations · messages · unread · colleagues · quick messages · today's arrivals. `StaffChatRoster` (who is in the cabinet, who is a dentist), `StaffChatInbox` (which conversations a person has), `StaffMessageLinks` (a message's patient and RDV as they are now), `StaffChatRefusals` (codes + French). `ChangeUserRoleCommand` marks the desks read for a new secretary (EC-14) |
| Infrastructure | `StaffMessageRepository` (keyset paging, latest-per-thread `ROW_NUMBER`, unread `NOT EXISTS` against the marker, today's arrivals), `StaffQuickMessageListRepository`; migrations `AddStaffChat`, `AddStaffQuickMessages`. `ClinicArchiveScope` excludes messages and markers; `ClinicRelayScope` carries them to the PC de secours |
| API | `StaffChatController` (`/api/staff-chat/…`); `ClinicHub` joins `ClinicGroups.User(clinic, user)`; `IRealtimeNotifier.NotifyStaffChatAsync` → `staffChatChanged`; `StaffPresence` (« En ligne ») |
| Web | `StaffChatProvider` in `app/layout.tsx`: the panel, the header icon and the bottom-bar tab, the quick-message bar and editor, the patient picker and link chips, « Signaler l'arrivée » (RDV window button, agenda bell, agenda « ⋯ » row), the alert owner `lib/staff-chat/alerts.ts` + `chime.ts`, the notices, the panel's sound / notification settings |
| Desktop 1.7.0 | `notify` on the bridge → `ShellNotifications` (silent toast + taskbar flash, click → window forward on that conversation) |
| Phones (Part 7) | Server: `StaffChatPushQueue` (queued from `StaffMessagePosted`, audience `AlertsFor`), `NotificationCategory.StaffChatMessage`, `PushDelivery.StaffMessageId` (migration `AddPushDeliveryStaffMessage`), `PushDispatchJob`'s « déjà lu ? » check, `FcmAccessToken` + `FcmPushSender`'s channel/tag. Android 1.4.0: `ShellPush`, `PushTap`, the bridge's `onPushToken`/`notificationState`/`openNotificationSettings`. Web: `lib/push/phone-push.ts` (register at sign-in, release at sign-out), the phone line in `alert-settings.tsx` |
| iPhone & browsers (Part 8) | Server: `DevicePlatform.WebPush` + `DeviceRegistration.WebPushP256dh/Auth` (migration `AddWebPushSubscriptionKeys`), `WebPushSubscription` (the endpoint allow-list), `WebPushCrypto` (RFC 8291 aes128gcm + RFC 8292 VAPID), `WebPushSender`, `PushConfig`'s `Push:WebPush:*`, `POST /push-devices/release`, the `generate-vapid-keys` verb. Web: `public/sw.js`, `lib/push/web-push.ts`, the « home-screen » state and the `?chat=` deep link |
| Guards | `StaffArrivalTests`, `StaffDeskTests`, `StaffQuickMessageTests`, `StaffMessageQuerySqlTests`, the domain tests; e2e CHAT-01…06, 08; `check:responsive` **N52**; `ShellNotifyMessageTests`; `WebPushCryptoTests` (the RFC 8291 test vector), `WebPushSenderTests`, `WebPushSubscriptionTests` |

## Decisions that are easy to undo by accident

- **A message is not journaled, not archived and never purged** (FR-8). `StaffMessage` is an `Entity<Guid>` and not
  an `AggregateRoot` precisely because `AuditSaveChangesInterceptor` journals every aggregate; the quick-message list
  *is* an aggregate, so its edits are journaled. Messages go only with the cabinet itself, and they travel to the PC
  de secours like the bell.
- **There is no conversation table.** A conversation is a key derived from its members — `dm:{a}:{b}` (sorted),
  `desk:{owner}`, `clinic` — so there is no get-or-create race and nothing to backfill. ⚠️ User ids contain `|`
  (`local|guid`), so every id is `Uri.EscapeDataString`-encoded in the key; a raw separator would collide.
- **The chat has its own realtime event, `staffChatChanged`, and carries no data.** `StaffChat` is in
  `RealtimeResourceResolver.ExcludedAreas`: an `entityChanged` broadcast goes to the whole clinic group, which would
  wake every colleague for a private message. The event goes to the members' user groups and the client re-reads.
- **Who is alerted is one table** (`StaffConversation.Alerts`, FR-2): a secretary's reply on a dentist's desk rings
  the dentist only; the other secretaries see it unread and silent, without raising their count.
- **« Je m'en occupe » is decided by the message's `xmin`.** The loser re-reads and is told « Déjà pris par X. »
  (409) — a name, never a generic conflict.
- **Replacing the quick-message list changes a root column** (`UpdatedAt`): an owned-collection-only change moves
  neither `xmin` nor the journal, so a concurrent edit would not be refused.
- **A message's patient and RDV are read live** (archived · cancelled · « introuvable »), through
  `StaffMessageLinks`, by **every** read that returns a message — thread, unread, claim, release, send and the
  conversation list. The conversation list was the one missed and the e2e caught it.
- **« Signaler l'arrivée »**: a secretary's goes to the dentist's secretariat, anybody else's to a direct
  conversation; a second one is a 409 naming who and when unless resent on purpose. It is a **real button** in the
  RDV window and a **bell** on today's waiting agenda blocks (owner's call: the « ⋯ » entry was « really hidden », and
  a half-hour block is too short for the « ⋯ » at all). The agenda « ⋯ » keeps its row because it is the one place on
  the agenda that states why it cannot go.
- **A press on the notice is not « outside » a dialog.** Radix closes a modal on an outside press — the fiche de
  soins, with what was typed in it. `ui/dialog.tsx` and `ui/sheet.tsx` wrap the three outside handlers in
  `unlessOnFloatingNotice` (`ui/floating-notice.ts`); `ui/alert-dialog.tsx` needs nothing, Radix already refuses an
  outside press there. The notice also cancels `mousedown`, so focus stays in the field being typed.
- **« Ouvrir » under an open dialog waits** for it to close (a 400 ms poll on the open-dialog marker) — the panel
  under a modal overlay is unusable, and closing the dialog for the user would lose its typing.
- **One alert owner, `lib/staff-chat/alerts.ts`** — the only file that rings, draws the notice's marker, raises a
  browser notification or calls the Windows app (`check:responsive` N52; `chime.ts` holds the `AudioContext`).
  One sound per device: a `localStorage` claim per message, last writer wins after a 60 ms read-back; no storage →
  ring anyway.
- **A catch-up is one sound and no notices**: the first summary of a session, the one after a hub reconnect, the
  one after the browser's `online` event (it can land before the hub reconnects), or any refresh bringing three or
  more new messages at once.
- **No notice for the conversation open in front** (visible **and** focused), **none on the lock screen**, and a
  notice never names a patient or quotes free text — an arrival shows « RDV 09:30 », free text shows « Nouveau
  message ». With the panel open a notice moves out of its header (under it on a phone, beside it from `md:`).
- **The Windows toast is silent** — the page already rang; one toast per conversation (the tag is a hash: Windows
  caps it at 64 characters); an urgent one is a `Reminder` and stays. It is raised only when the shell is not
  active **and** the page has no focus. WebView2 runs with `--autoplay-policy=no-user-gesture-required` so the tone
  rings after a restart. The Velopack uninstall hook removes the notification registration.
- **Shell 1.7.0 must not raise the server's client-version floor**: an older app keeps the tone and the in-app
  notice, and the panel asks for the update (EC-10).

### Phones (Part 7)

- **The push follows the screen's rule.** Its audience is `StaffConversation.AlertsFor` — the people the message
  rings in the app, never the sender — so a phone and a screen cannot disagree about who is told.
- **It waits 30 s and asks « déjà lu ? » before leaving** (`StaffChatPushQueue.Grace`, re-checked by
  `PushDispatchJob` against the read marker): somebody at the PC who reads it there must not also have their phone
  buzz. The dispatcher is minutely, so a push lands 30–90 s after the message.
- **The lock screen learns nothing**: « Nouveau message », the P6 rule (AC-47). The text, the sender and the patient
  stay behind the app's sign-in.
- **The server was sending the Firebase key as the bearer**, which Google refuses: `FcmPushSender` could never have
  delivered with the value its configuration asked for. `FcmAccessToken` exchanges the key for a one-hour token.
- **Two channels** (« Messages de l'équipe », « Rendez-vous ») so a dentist can silence the agenda and keep the chat.
- ⚠️ **Turning push on also turns on the agenda's five P6 notifications** (new, cancelled, moved, reminder, post-visit
  review) — they were built, and waited for a phone that could register.

### iPhone and browsers (Part 8)

- **Web Push is the only way an iPhone gets a notification without an App Store app**, and only from the home-screen
  app (iOS 16.4+). Safari itself says « ajoutez APEXA à l'écran d'accueil » rather than offering a button that
  cannot work.
- **No library**: `WebPushCrypto` is ~150 lines over .NET's own ECDH, HKDF and AES-GCM, pinned to RFC 8291's test
  vector byte for byte.
- **The endpoint is an address the server will POST to**, so it is held to the push services' own hosts (FCM,
  Mozilla, Apple, Windows) at registration **and** again at send — anything else would let a browser aim the server
  at its internal network.
- **Nothing ever prompts on its own.** Permission is asked only behind « Activer les notifications » (iOS refuses a
  prompt with no tap); a browser that already allowed them re-subscribes silently at sign-in — which is how a shared
  phone moves to its next user — and sign-out releases it first.
- **The page's notice goes through the service worker, tagged by thread**, so the push arriving 30 s later keeps the
  page's richer « who wrote, what » instead of replacing it with « Nouveau message ».
- **A tap opens the thread** (`/?chat=…`, or a message to an open window); a message from the worker naming a URL is
  followed only if it is one of this app's own paths.
- **`sw.js` has no `fetch` handler**: it caches nothing, so a deploy can never be hidden behind a stale copy.
- ⚠️ **The hosted compose never passed `Push__*` to the API**, so Part 7's « set it in the API's environment » did
  nothing on the VPS. Both platforms now have empty-by-default slots in `docker-compose.hosted.yml`.

## Testing the Windows app locally

The shell talks HTTPS only and the dev stack is HTTP. What worked, with nothing installed on the machine:
`APEXA_SHELL_PROFILE=<scratch>` (its own `server.json` with `"PortIsExplicit": true`), a throwaway Node HTTPS proxy
in front of the web dev server, the API started with `Cors__AllowedOrigins__0=https://localhost:<port>`, and
`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--ignore-certificate-errors --autoplay-policy=no-user-gesture-required
--remote-debugging-port=9333"` for that run only — then drive the page with Playwright's `connectOverCDP`.
⚠️ While a script is attached over CDP, `document.hasFocus()` reads `true` (focus emulation), so the « window not
in front » path only fires once it has disconnected. ⚠️ Running the test copy with `--veloapp-uninstall 1.7.0`
removes its notification registration.

## Found during this feature, fixed on this branch (pre-existing, outside the chat)

- **After a full page load of the agenda or a patient file, closing any guarded dialog reloaded the whole page**
  — and lost a queued « Ouvrir » from a notice. 25 raw `history.replaceState({}, …)` / `pushState(marker, …)`
  calls dropped Next's own history marker. Every write now goes through `web/lib/history.ts`, held by N53. See
  [`features/dialog-reload/notes.md`](../dialog-reload/notes.md).

## Not exercised

- The lock screen live (needs the Windows app or an 8 h idle trusted session) — `locked` makes the notice stack
  render nothing; sound and badge are untouched by it.
- The urgent Windows toast staying on screen, and « app in front → in-app notice only » in the shell — both guarded
  in code; the owner was using the machine.
- Android notifications on a real phone: no Firebase project exists yet (`mobile/README.md` § Notifications Android
  has the three steps). Server, APK build and web are tested; the device walk is owed.
- Web Push on a real iPhone: no key pair is set on the VPS yet (`mobile/README.md` § Web Push). Verified end to
  end in Chrome on the demo stack — the server encrypted, Google's push service accepted, the browser decrypted and
  showed « Nouveau message » with no text, with the page closed. The iPhone walk is owed.
- ⚠️ Testing it with Playwright: a plain context is incognito (Chrome refuses the Push API there) — use
  `launchPersistentContext`; and drop `--disable-background-networking` (`ignoreDefaultArgs`), which switches off
  Chrome's push channel. The first push after subscribing can take a while to arrive.

## Release

Not merged. A `desktop/**` change landing on `main` **is** a Windows release (`client-installer.yml`): merging ships
shell 1.7.0 to every cabinet. It waits for the owner's OK.

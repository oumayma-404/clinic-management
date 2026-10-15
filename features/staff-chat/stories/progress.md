# Messagerie interne — Progress

**Feature:** staff-chat · **Branch:** `feature/staff-chat` (worktree `.claude/worktrees/staff-chat`, from `origin/main` 8c2b24de)

## Status

| Story | Status |
|---|---|
| 1 — Messagerie interne du cabinet (Full, 6 parts) | in-progress |

| Part | Status | Commit |
|---|---|---|
| 1A Model + storage | implemented | 8e7f024b |
| 1B Server | implemented | b61c571d |
| 1C Browser | implemented | 4d18510e |
| 2 Quick messages | implemented | 3025b348 |
| 3 Shared desk | implemented | 64aa31c3 |
| 4 Patient link + arrival | implemented | 62ad188d |
| 5 Alerts | implemented | (this commit) |
| 6 Windows notification | not-started | — |

## Working tree note (start of session, 2026-10-15)

- Only `features/staff-chat/` untracked (this feature's docs). Nothing else dirty.

## Environment note

- Docker daemon **not running** at session start; shared stack fully down (`stack-lease status`). The DB is needed
  only for `verify-schema` — use an isolated database on the shared postgres so peers on older branches are not
  migrated (memory `isolated-test-stack`).

## Blast Radius

See [`../blast-radius.md`](../blast-radius.md).

## Auto-Approved Deviations

| Deviation | Classification | Reason |
|---|---|---|
| `StaffConversation` holds the full FR-2 desk rules already in Part 1A (plan put desk use in Part 3) | Trivial | Pure domain, no API/UI; one owner for the rule from day one, tested now |
| `StaffConversation` extends the repo's `ValueObject` base | Trivial | House convention for value objects |
| Doc comments kept to one or two lines | Trivial | Comment budget |
| No `StaffChatNotifier` class: handlers call `IRealtimeNotifier.NotifyStaffChatAsync` after commit inside a log-only catch | Trivial | One call per handler; a wrapper would add a file and no behaviour |
| `StaffPresence` counting tested through `ClinicHubTests` (first tab / last close) instead of a separate `StaffPresenceTests` | Trivial | The behaviour that matters is the hub's signal, tested where it happens |
| `StaffMessageCursor` lives beside `GetStaffMessagesQuery` | Trivial | Its only reader and writer |

## Deviations

_(none yet)_

## Gate results

### Part 1A — model + storage (2026-10-15)

| Gate | Result |
|---|---|
| Backend build | 0 errors; 54 pre-existing warnings, **0 in files this part added or changed** |
| Unit suite (unfiltered) | **5 750 passed, 0 failed**, 6 skipped (pre-existing) |
| Migration `20261015182301_AddStaffChat` | scaffolded `xmin` removed by hand; snapshot adds only the two tables |
| `verify-schema` before (isolated DB at `RelayReturnPerRecord`) | exit 2 — exactly the 5 StaffChat items (3 indexes, 2 FKs) |
| `verify-schema` after | **exit 0 — schema matches the model**; no `xmin` column on `StaffMessages` |

Guards that reacted (both correct, both fixed in this part):
- `ClinicalRecordAuditCoverageTests` — `StaffMessage` carries `PatientId` and is not journaled → added to
  `NotClinicalByDesign` with its reason (FR-8).
- My own `StaffConversationTests` used a culture-sensitive `OrderBy` → ordinal.

### Part 1B — server (2026-10-15)

| Gate | Result |
|---|---|
| Unit suite (unfiltered) | **5 795 passed, 0 failed**, 6 skipped (pre-existing) |
| SQL translation (`StaffMessageQuerySqlTests`) | keyset page (`Id <` — Npgsql translates `Guid.CompareTo`), latest-per-thread (`ROW_NUMBER() … PARTITION BY`), unread (`NOT EXISTS` on markers + `GROUP BY`) |
| New warnings in this part's files | 0 (`--no-incremental`; the two matches are pre-existing: Hangfire obsolete API in `Program.cs`, `LocalDataProtection`) |
| Guards that stayed green with a deliberate edit | `RealtimeResourceResolverTests` (`StaffChat` excluded), `ClinicHubTenantScopeTests` (+`StaffPresence` with reason), `SubscriptionExemptionCoverageTests` (+`StaffChat.MarkRead`), `ControllerAuthorizationCoverageTests` (class-level `AnyClinicRole`) |

**Real defect found by a test written in this part:** a two-person key naming an account of **another clinic**
passed membership for the caller and was refused as « compte désactivé » — a confirmation that the id exists.
`StaffConversation.IsMember` now requires both people in the cabinet; pinned in `StaffConversationTests` and
`StaffChatTenantIsolationTests`.

## Learnings

- **A patient-bearing entity that is deliberately not an aggregate trips `ClinicalRecordAuditCoverageTests`** —
  the decision has to be written down there, which is the point.
- The Docker engine on this machine is **Rancher Desktop**, not Docker Desktop.

### Part 1C — browser (2026-10-15)

| Gate | Result |
|---|---|
| `npx tsc --noEmit` | 0 errors |
| `npm run check:responsive` | 79/79 |
| `npm run build` | green (run before the three eye-pass fixes; tsc + check re-run after them) |
| Unit suite (unfiltered, after the server fixes) | **5 795 passed, 0 failed**, 6 skipped |
| Eye pass | two driven Chromes on an isolated copy (`clinic_staffchat_demo`, API :5099, web :3099): dentist at 946 px (header icon, side panel), secretary at 562 px (bottom-bar tab, full-screen panel); a secretary's message reached the dentist live; no page scroll at either width |

**Three real defects found by the eye pass, fixed in this part:**

| Defect | Fix |
|---|---|
| A secretary saw her two-person thread with a dentist and that dentist's secrétariat under the **same title** | desk title to the desk is « Secrétariat · {owner} » (`StaffChatInbox.TitleOf`) |
| The « en ligne » dot read `members[0]`, which can be the viewer | reads the member who is not the viewer |
| An empty list put colleagues above « Tout le cabinet » / « Secrétariat » (A→Z only) | quiet threads rank shared first, then colleagues A→Z (AC-1.3) |

⚠️ **Running a copy of the dev DB with the branch's API fires catch-up Hangfire jobs**: `take-recovery-points`
ran at startup and pruned two dev recovery-point objects in the shared MinIO bucket (same clinic ids). Dev data only.

### Part 2 — quick messages (2026-10-15)

| Gate | Result |
|---|---|
| Unit suite (unfiltered) | **5 830 passed, 0 failed**, 6 skipped (+35: list rules, ordering, saves, reset, send-with-key, usage SQL) |
| Migration `20261015191527_AddStaffQuickMessages` | scaffolded `xmin` removed; `verify-schema` before = exactly the 3 new items (exit 2), after = **exit 0** |
| `tsc --noEmit` · `check:responsive` · `build` | 0 errors · 79/79 · green |
| Eye pass (isolated copy, two Chromes) | dentist → secretary offers the 7 « secrétariat » + 4 replies; secretary → dentist the 6 « dentiste » + 4 replies; « Patient arrivé » reached the dentist live; editor = 17 rows, 3 locked; no page scroll at 946 / 562 px |

Decisions taken in this part (all reversible):

| Decision | Why |
|---|---|
| The list row carries `UpdatedAt`, moved by every `Replace` | replacing only the owned rows leaves the list row untouched — no `xmin` check (AC-6.5) and no journal row (FR-4) |
| Version 0 = « opened on the defaults »; a mismatch either way is a 409; a unique-index race on the first save is a 409 too | a stale save is refused, never overwritten |
| A custom message may be marked urgent; a built-in's urgency is locked | AC-6.2 locks only the built-ins |
| Chip order frozen while a thread is open | a send re-sorts by usage, and a button must not move under the finger |
| Asks-for-patient chips send immediately for now | the patient picker is Part 4 (AC-2.5's « sent without one » half holds already) |
| Defaults live in `Domain/Services/StaffQuickMessageDefaults` | there is no `Domain/Constants` folder (plan wording) |

### Part 3 — shared secretariat (2026-10-15)

Already in place from Part 1: desk membership (owner + current secretaries, none without a secretary), the FR-2
alert table and the silent unread (AC-5.3) — this part adds the claim, « Vu par » and EC-14.

| Gate | Result |
|---|---|
| Unit suite (unfiltered) | **5 852 passed, 0 failed**, 6 skipped (+22: claim/release domain + handlers incl. the concurrent race, OffersClaim, role change both ways, « Vu par », read signals, EC-14 marks) |
| `tsc --noEmit` · `check:responsive` · `build` | 0 errors · 79/79 · green (built beside the running dev server — it uses `.next/dev`) |
| Eye pass, 3 sessions (dentist 626 px, two secretaries 502 px) | 12/12: both secretaries offered « Je m'en occupe »; dentist sees « Vu par Ines Gharbi »; Ines takes it → « Vous vous en occupez », dentist and the other secretary see « Pris par Ines Gharbi » and her button is gone; Ines' « OK » reaches the dentist and leaves the other secretary's badge unchanged; no page scroll |

**Blast radius added in this part**

| Touching | Other consumers | Verdict |
|---|---|---|
| `ChangeUserRoleCommandHandler` ctor (+ `IStaffMessageRepository`, `IClinicWriteFence`) | its tests; `UsersController` | must change (tests) · must re-test: role change still lands, incl. on a fenced cloud |
| `RelayFenceCoverageTests.ReviewedFr11Reads` | FR-11 doors | must change — `ChangeUserRoleCommand:StaffMessage` reviewed: markers written only when the fence lets the save through |
| `MarkStaffConversationReadCommand` signal | the reader's tabs | must change — two-person / secretariat reads now wake the thread's members (for « Vu »); the cabinet thread still wakes only the reader |

Decisions (reversible):

| Decision | Why |
|---|---|
| A concurrent claim is decided by the message's `xmin`; the loser re-reads untracked and is told « Déjà pris par X. » (409) | EC-4 — a name, never a generic conflict |
| Claiming twice is a no-op success; releasing an unclaimed message is a no-op success | a double press is not an error |
| EC-14 is written at the role change (read markers on every secretariat at that moment), skipped while a PC de secours holds the saves | the account-age floor cannot express « joined later »; on a fenced cloud chat tables are refused and the role must still change — that history then reads unread |
| « Vu » only under my own last message; « Vu par » lists names; the cabinet thread shows none | AC-5.4 |

### Part 4 — patient link + arrival (2026-10-15)

| Gate | Result |
|---|---|
| Unit suite (unfiltered) | **5 873 passed, 0 failed**, 6 skipped (+21: link validation, live projection archived / cancelled / gone, arrival target refusals, already-sent 409, resend, secretary → desk, own appointment, today's actions, the conversation list's last message, the arrivals SQL) |
| `tsc --noEmit` · `check:responsive` · `build` | 0 errors · 79/79 · green, re-run after the last edit |
| e2e `staff-chat.spec.ts` on the isolated stack | **6/6**: CHAT-01…05, CHAT-08. `check-coverage` counts all six; its pre-existing failures (64 untested tier-0, 18 wire-only) are unchanged |
| Eye pass, 2 sessions (secretary 900 px, dentist 640 px) + 390 px | dialog « ⋯ » offers it → toast « Arrivée signalée au Dr … » → dentist gets « Patient arrivé » with the patient and « RDV 15:00 · Dr … » links; menu then reads « Signalé à 21:14 par Ines Gharbi » and a second press asks first, « Non » sends nothing; a dentist with no account → present, disabled, « Ce praticien n'a pas de compte dans l'application. »; agenda block « ⋯ » shows the same three states; at 390 px the menu stays inside the screen, no page scroll |

Found and fixed during the pass:

| Defect | Fix |
|---|---|
| The RDV chip did nothing when the agenda was already open (a same-route link does not remount the page) | the chip hands the id to the agenda's `clinic:deeplink` listener, the bell's own path |
| The conversation list's last message came without its patient / RDV links | `GetStaffConversationsQuery` fills them like every other message read (+ test) |

**Blast radius added in this part**

| Touching | Other consumers | Verdict |
|---|---|---|
| `appointment-quick-actions.tsx` (agenda block « ⋯ ») | every agenda block | must re-test — statuts and « Supprimer » unchanged; the arrival row reads the shared query only while the menu is open |
| `edit-appointment-dialog.tsx` « ⋯ » menu, now controlled | « Annuler le rendez-vous », « Supprimer » | must re-test — both still open their confirmations |
| Every handler returning a `StaffMessageDto` | thread, unread, claim, release, send, conversations | must change — all fill the links through `StaffMessageLinks` |

Deviations (auto-approved, trivial):

| Deviation | Why |
|---|---|
| `GET /arrivals/today` returns every appointment of today with `unavailable` filled, not only the sent ones | the row needs the server's reason for each visit (AC-3.4) without a second read |
| Extra refusal `staffchat-no-patient` | a blocked slot has no patient to announce |
| e2e spec lives in `e2e/specs/` | the plan said `e2e/tests/`; the suite's folder is `specs` |
| `useArrivalAction` hook + two small rows instead of one `ArrivalAction` component | the confirmation must outlive the menu that closes before it opens |

### Part 5 — alerts (2026-10-15)

| Gate | Result |
|---|---|
| `tsc --noEmit` · `check:responsive` · `build` | 0 errors · **80/80** (new N52) · green, re-run after the last edit |
| N52 `staff-chat-has-one-alert-owner` | red on a planted file (notification, tone, shell call, notice marker — 4 hits), green once removed |
| e2e `staff-chat.spec.ts` | **7/7** (+ CHAT-06 at 320 px) |
| Backend suite | not re-run — no `api/` change in this part (last: 5 873 passed at Part 4) |
| Browser pass, 3 sessions (dentist, two secretaries) + 390 px tab | **43/43**: notice over an open fiche, « J'arrive » sent, fiche and its typing kept; « Ouvrir » under a dialog waits and opens on close; nothing for the conversation open in front; one chime, gone after 8 s, top-right; urgent: red, `role="alert"`, rings while muted, stays, other secretary alerted; muted: notice, no sound; two tabs → one chime; phone: top, full width; nav drawer and booking dialog still close on an outside press; arrival notice « Patient arrivé · RDV 15:00 », no patient name; browser notification « Nouveau message », never free text; back after a cut → one chime, no notices, badge counts them |
| Not exercised | the lock screen (needs the Windows app or an 8 h idle trusted session) — `locked` makes the notice stack render nothing; sound and badge are untouched by it |

Found and fixed during the pass:

| Defect | Fix |
|---|---|
| A notice sat over the open panel's header (back, ✕) on tablet and phone widths | with the panel open: under its header on a phone, beside it from `md:` |
| Back online, the browser's own refetch could land before the hub reconnected, so three missed messages drew three notices | the `online` event and any refresh bringing 3+ new messages count as a catch-up |

Found, **not** fixed (pre-existing, outside the chat):

| Defect | Where |
|---|---|
| After a full page load of the agenda or a patient file, closing **any** guarded dialog reloads the whole page | 20 `history.replaceState({}, …)` calls drop Next's own history marker; `useDirtyGuard`'s `history.back()` then lands on an entry Next does not know and it reloads. Moving inside the app first avoids it |

**Blast radius added in this part**

| Touching | Other consumers | Verdict |
|---|---|---|
| `ui/dialog.tsx`, `ui/sheet.tsx` outside handlers | 77 + 4 files; 3 callers pass their own `onInteractOutside` | must re-test — re-tested: outside press still closes / asks; callers' handlers run for every press not on the notice |
| `ui/alert-dialog.tsx` | 42 files | unaffected — Radix already refuses an outside press on an alert dialog |
| `SessionState.locked` | every `useSession` reader | unaffected — additive |

Decisions (reversible):

| Decision | Why |
|---|---|
| A refresh bringing 3+ new messages is a catch-up (one sound, no notices) | live, messages arrive one per signal; a batch was missed while away (AC-4.8) |
| A notice is drawn in every tab of the device; only the tab that wins the claim rings and raises the browser notification | one sound per device (EC-9), and the tab in front always shows it |
| Hovering or focusing an ordinary notice holds it | a reply being reached for must not vanish under the pointer |
| No sound or notice for the conversation open in a window in front | AC-4.7; « in front » = visible **and** focused |


### « Signaler l'arrivée » made visible (2026-10-15, owner's request: « it's reallyyyy hidden »)

| Change | Why |
|---|---|
| RDV window: a labelled button under the patient's name — « Signaler l'arrivée », « Signalé à HH:mm par … » (asks before resending) or disabled with the reason; the « ⋯ » entry is gone | one door, where reception looks |
| Agenda block (desktop): a bell beside the name on today's visits still waiting (Planifié, Confirmé, En cours set by the agenda at the start minute); one press sends, then a green check | a half-hour block is too short for the « ⋯ », so most visits had no arrival door on the agenda at all |
| Agenda « ⋯ » keeps its arrival row | the one place on the agenda that states why it cannot go |

| Gate | Result |
|---|---|
| `tsc` · `check:responsive` · `build` | 0 · 80/80 · green |
| e2e `staff-chat.spec.ts` | 7/7 (CHAT-02 / CHAT-05 now drive the button) |
| Browser pass | 13/13: bell in Jour and on the half-hour block in Semaine; one press sends without opening the block; bell turns to « signalé »; window shows « Signalé à 22:26 par Ines Gharbi » and asks first; no second door in « ⋯ »; no-account dentist → disabled + reason; 390 px: button on its own row, no page scroll |

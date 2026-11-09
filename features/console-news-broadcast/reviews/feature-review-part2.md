# Feature Review: console-news-broadcast — Part 2 (« Nouveautés » dans l'app)

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-07
**Challenged Date:** 2026-11-07
**Parent Branch:** main (0 commits ahead)
**Scope:** commit `42d0200c` only (`5c579b88..42d0200c`) — Part 1 was reviewed in `feature-review.md`
**Files Reviewed:** 38 files, +1 557 / −17 (excluded: migration `.Designer.cs`, model snapshot, `features/**`, PNGs, CLAUDE.md docs — the migration agent read the first two from the repo)
**Review method:** five parallel agents adapted to the stack — backend code quality (Result/CQRS, not ROP), business logic
(spec § Part 2), breaking changes + schema + middleware, frontend code (React 19 / Next 16), device & UX.
**Schema:** `verify-schema` before / after / after a rollback + re-apply — the only difference is the two new items
(unique index on `UserId`, FK to `Clinics`); the one remaining drift (`key-ring-protection`) is the dev environment.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings (after merging duplicates across agents) | 34 |
| Confirmed | 30 |
| Confirmed (adjusted) | 1 |
| Dismissed (false positive / deliberate) | 2 |
| Dismissed (pre-existing) | 1 |
| **Final findings** | 31 |

Dismissed:
- `ListVisibleInAppAsync` « loads whole aggregates » — it loads the row and its owned items, which the page prints;
  `Recipients` is a non-owned navigation and is never included. A projection would save nothing measurable.
- « A promoted admin sees every news since the account was created as unread » — deliberate: that person never
  received those e-mails (only admins do), so they are genuinely unseen. The fallback exists for a *new* account.
- « Plus » accessible name lacks the visible word (WCAG 2.5.3) — the base name « Ouvrir la navigation » predates this
  commit; only the unread suffix is new. Worth its own fix, not this feature's.

## Findings

### Finding 1
- **Severity:** Major · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 52 · **Anchor:** `NewsFeed.load` / `clearNewsBadge`
- **Comment:** The badge only clears when `feed.unread > 0` and only from `ready`. Read on the phone, or a news hidden
  by the vendor → the page shows nothing new and the rail keeps « 1 » until a reload; and a summary that resolves
  after the page marked (landing from the e-mail link: both start together) republishes the old count. Every
  successful feed load must publish the page's own answer, and a summary started before it must be discarded.

### Finding 2
- **Severity:** Minor · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/News/Commands/MarkNewsReadCommand.cs · **Line:** 60
- **Comment:** Read-then-insert with no guard: two first reads (two tabs, StrictMode) both insert → 23505 logged at
  Error and a 400; two concurrent posts can move the marker backwards. One upsert,
  `ON CONFLICT … DO UPDATE SET "ReadUpToUtc" = GREATEST(…)`.

### Finding 3
- **Severity:** Minor · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/SetPlatformNewsVisibilityCommand.cs · **Line:** 53
- **Comment:** A second writer of the broadcast row while `PlatformNewsDispatcher` keeps it tracked: « Masquer » during a
  send ages `xmin` and the dispatcher's `Complete` save throws `ConflictException` (job fails, « en cours » until a
  retry). Refuse while the send is running (`news_in_progress`) and say so in the console instead of offering the button.

### Finding 4
- **Severity:** Minor · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** console/components/news-history.tsx · **Line:** 151 · **Anchor:** `AppVisibility` + `HistorySize = 20`
- **Comment:** The toggle only exists on the 20 most recent sends while the app shows every visible news; past 20 an
  old news can no longer be hidden (or shown again). The console must list every send.

### Finding 5
- **Severity:** Minor · **Category:** Breaking Change · **Verdict:** Confirmed
- **File:** api/ClinicManagement.API/Controllers/NewsController.cs · **Line:** 50 · **Anchor:** `MarkRead`
- **Comment:** On a fenced cloud `RelayLeaseGateMiddleware` answers 423 to `POST api/news/read` (and fires the cut
  strip's re-read) for a row that is never carried. `[AllowedWhileCloudFenced]` (+ `[OnlineOnly]` for the PC).

### Finding 6
- **Severity:** Minor · **Category:** Frontend · **Verdict:** Confirmed
- **File:** web/lib/hooks/use-news-badge.ts · **Line:** 32 / 58
- **Comment:** A failed summary is never retried (`failed` locks), and `failed` shows the row — on an install with no
  news, one blip adds a row leading to « pas proposées ». Show the row only on a real answer; a failure goes back to
  « unknown » so the next screen asks again.

### Finding 7
- **Severity:** Minor · **Category:** Frontend · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 53 · **Anchor:** `NewsFeed.load`
- **Comment:** No cancel flag: StrictMode's double effect sends two marks; leaving before the answer still marks and
  sets state on an unmounted component. Guard with an `active` flag.

### Finding 8
- **Severity:** Minor · **Category:** Code Quality · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Repositories/PlatformNewsRepository.cs · **Line:** 95 · **Anchor:** `GetAsync`
- **Comment:** Returns the aggregate without `Recipients`, on which `IsSettled` is true and `Complete()` succeeds —
  the « unloaded collection is empty, not stale » trap behind an innocent name. Rename `GetWithoutRecipientsAsync`.

### Finding 9
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 272 · **Anchor:** `NewsIndex`
- **Comment:** At `xl:` with a finger (iPad Pro landscape) the index rows are ~34 px in a stack. `coarse:py-3`.

### Finding 10
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/dashboard-sidebar.tsx · **Line:** 418 · **Anchor:** drawer foot
- **Comment:** The drawer's foot row sits in the home-indicator band on an iPhone. Pad with `env(safe-area-inset-bottom)`.

### Finding 11
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 218 / 234 / 284
- **Comment:** An unbreakable token (a URL, a long word) is clipped by the card at 320/390 and scrolls the index
  sideways. `[overflow-wrap:anywhere]` on the title, the item text and the index subject.

### Finding 12
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 230 · **Anchor:** emoji tile
- **Comment:** The server accepts up to 16 characters; two glyphs overflow the fixed 36 px tile onto the text. Let it grow.

### Finding 13
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 89 · **Anchor:** loading skeleton
- **Comment:** `aria-label` on a role-less div is ignored. `role="status"`.

### Finding 14
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 75 · **Anchor:** header badge
- **Comment:** After the page marks, the header still says « 3 non lues » while the rail has cleared — a false
  statement beside a true one. Word it « 3 nouvelles », like the cards' « Nouveau ».

### Finding 15
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 66
- **Comment:** Picking a subject in the index scrolls but leaves focus in the index, and ignores reduced motion. Focus
  the article (`tabIndex={-1}`), `auto` scrolling under `prefers-reduced-motion`.

### Finding 16
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 154
- **Comment:** « Nouveautés plus anciennes » unmounts itself on the last page while focused; focus falls to `<body>`.
  Move focus to the first card revealed.

### Finding 17
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** console/components/news-history.tsx · **Line:** 140
- **Comment:** Below `lg:` the card list's `<dd class="truncate">` clips the « Masquer » button's focus ring and touch
  overlay. Render the control outside the truncating `dd`.

### Finding 18
- **Severity:** Minor · **Category:** Business Logic · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/News/Commands/MarkNewsReadCommand.cs · **Line:** 53
- **Comment:** The marker moves to the newest news at POST time, not to the newest the page showed; one sent (or shown
  again) in between is marked read unseen. The page sends what it showed; the server clamps.

### Finding 19
- **Severity:** Minor · **Category:** Frontend · **Verdict:** Confirmed
- **File:** console/components/news-history.tsx · **Line:** 43 · **Anchor:** `load` / `toggle`
- **Comment:** A poll that left before the toggle can land after it and put the old state back; loads are not
  sequenced. Number the reads and drop stale ones; re-read after a toggle.

### Finding 20
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed (adjusted — Suggestion → Minor; two agents)
- **File:** web/components/news/news-feed.tsx · **Line:** 172
- **Comment:** From `md:` a screen reader reads the date twice. `aria-hidden` on the visual date column.

### Finding 21
- **Severity:** Minor · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/dashboard-sidebar.tsx · **Line:** 249 / 418
- **Comment:** The « Nouveautés » icon sits 4 px left of every other row (foot `p-2` vs nav `px-3`). Line it up.

### Finding 22
- **Severity:** Suggestion · **Category:** Code Quality · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Repositories/PlatformNewsRepository.cs · **Line:** 102 / 119 / 124
- **Comment:** « Visible in the app » is written three times. One private query.

### Finding 23
- **Severity:** Suggestion · **Category:** Code Quality · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Repositories/PlatformNewsRepository.cs · **Line:** 127
- **Comment:** The two queries track a marker they never save. `AsNoTracking` (the upsert of F2 removes the tracked use).

### Finding 24
- **Severity:** Suggestion · **Category:** Code Quality · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/News/Queries/GetNewsQuery.cs · **Line:** 40
- **Comment:** The caller lookup is copied in the three handlers. One `NewsReading.ResolveCallerAsync`.

### Finding 25
- **Severity:** Suggestion · **Category:** Code Quality · **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/SetPlatformNewsVisibilityCommand.cs · **Line:** 53
- **Comment:** Nothing records which console account hid or showed a news. Log it with the account id.

### Finding 26
- **Severity:** Suggestion · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/dashboard-sidebar.tsx · **Line:** 179
- **Comment:** Collapsed rail: the tooltip says « Nouveautés » with no count. Put the count in the tooltip.

### Finding 27
- **Severity:** Suggestion · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** console/components/news-history.tsx · **Line:** 186
- **Comment:** Visible → Masquée changes silently for a screen reader. `role="status"` on the state.

### Finding 28
- **Severity:** Suggestion · **Category:** Device & UX · **Verdict:** Confirmed
- **File:** web/components/news/news-feed.tsx · **Line:** 204 · web/components/dashboard-sidebar.tsx:166
- **Comment:** « Nouveau » re-styles `Badge` by hand; the rail pill is the only unread count without the « 99+ » cap.

### Finding 29
- **Severity:** Suggestion · **Category:** Frontend · **Verdict:** Confirmed
- **File:** console/app/bff/nouveautes/visibilite/route.ts · **Line:** 14
- **Comment:** A literal `null` body throws → 500 instead of the French 400.

### Finding 30
- **Severity:** Suggestion · **Category:** Breaking Change · **Verdict:** Confirmed
- **File:** web/lib/feedback.ts · **Line:** 52 · **Anchor:** `screenLabel`
- **Comment:** Feedback written on `/nouveautes` reaches the vendor with no screen named. Add `NEWS_NAV_ITEM`.

### Finding 31
- **Severity:** Suggestion · **Category:** Business Logic · **Verdict:** Confirmed (document, no code)
- **File:** api/ClinicManagement.Domain/Entities/PlatformNewsBroadcast.cs · **Line:** 149 · **Anchor:** `ShowInApp`
- **Comment:** A news shown again keeps the read state of the watermark (it comes back without « Nouveau » if the admin
  read past it). Acceptable — write the rule down in the spec.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 1 |
| Minor | 20 |
| Suggestion | 10 |
| **Total** | 31 |

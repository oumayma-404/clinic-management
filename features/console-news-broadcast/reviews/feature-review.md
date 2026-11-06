# Feature Review: console-news-broadcast

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-11-06
**Challenged Date:** 2026-11-06
**Parent Branch:** main
**Merge Base:** 7000b0e0f6cb474cdb87cef3959af320ec0ec180 (`origin/main` has not moved since)
**Files Reviewed:** 50 files (+3 336 diff lines reviewed) — uncommitted worktree, new files folded in with
`git add -N`; the migration `.Designer.cs` and model snapshot excluded from the diff and read directly by the
regression agent.
**Review method:** 5 agents adapted to this stack (MediatR + `Result<T>`, no ROP): Code Quality · Error Handling
(replaces ROP) · Business Logic + Security · Breaking Change + cross-boundary wiring · Device & UX (required —
console `.tsx` in the diff). Review and challenge ran back to back on the owner's request; every finding below was
re-read against the source.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Raw agent findings | 40 |
| Merged duplicates (same defect, several agents) | 5 |
| Distinct findings challenged | 35 |
| Confirmed | 28 |
| Confirmed (adjusted) | 7 |
| Dismissed (false positive) | 0 |
| Dismissed (pre-existing) | 0 |
| **Final findings** | **35** (33 numbered + 2 listed at the end) |

Verified clean by the regression agent (not findings): migration ↔ config ↔ snapshot match, no `xmin` column,
Designer = snapshot; every console ↔ API field name matches (request and all DTOs); existing e-mails unchanged;
guard classifications consistent (recipients never carried to the PC de secours, survive purge, child rows never
audited); DI resolvable; `:where(.touch-target)` safe for every console consumer.

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Error Handling
- **Verdict:** Confirmed (merged: error-handling + business-logic agents)
- **File:** api/ClinicManagement.API/Controllers/Platform/PlatformNewsController.cs
- **Line:** 85
- **Anchor:** `PlatformNewsController.Send` / `PlatformNewsDispatchJob.Dispatch`
- **Comment:** A send can stay « en cours » for ever. The job is queued only from the HTTP request, after the save.
  If the process dies between save and `Enqueue`, or the job uses up its 3 retries (a few minutes of database
  trouble), every recipient stays Pending and nothing re-queues it — the console mints a new `requestId` after
  each success and on reload, so a replay never happens. History then reads « 0 / N · en cours… » for ever and
  `AutoRefresh` polls every 5 s while the page is open. Fix: a recurring (or startup) sweep that re-queues
  broadcasts with `CompletedAtUtc IS NULL` older than a few minutes — the dispatcher already resumes from Pending.

### Finding 2
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 223
- **Anchor:** `NewsComposer` (row actions Monter / Descendre / Supprimer)
- **Comment:** On a touch screen, a tap meant for « Descendre » can delete the row. The three `size-9` icon buttons
  sit `gap-1` (4 px) apart; each `touch-target::after` is 44 px wide on a 36 px button, so it sticks out 4 px each
  side and the later sibling takes the gap. « Supprimer » is last, so it steals the right edge of « Descendre » —
  and deletes up to 300 typed characters with no undo. `globals.css` itself says « never on siblings a few pixels
  apart ». QA measured each hit area, not which button wins. Fix: grow the boxes on a coarse pointer
  (`coarse:size-11`) with `gap-2`, and set the trash apart (`ms-2`).

### Finding 3
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Critical)
- **File:** console/components/news-history.tsx
- **Line:** 58
- **Anchor:** `NewsHistory` (CardList « Résultat ») / `Outcome`
- **Comment:** Below 1024 px the history is cards, and `CardList` renders each value in `<dd className="truncate">`
  (`ui/card-list.tsx:69`). `Outcome`'s lines inherit `nowrap` and are clipped: « Non reçu : Cabinet A, Cabinet B… »
  is cut after ~20–40 characters, and in two-column cards (380–639 px) even « 12 / 14 envoyés · 2 échecs » loses
  the failure count. That list is the only place naming the cabinets that missed the e-mail. Fix: `whitespace-normal`
  on `Outcome`'s root `<div>`.
- **Challenge note:** Critical → Major — information is hidden on phones and tablets, but no action is lost
  (the desk table shows it in full); the spec's history requirement (« which cabinets failed ») fails below 1024.

### Finding 4
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed (merged: device + error-handling agents)
- **File:** console/app/nouveautes/page.tsx
- **Line:** 32
- **Anchor:** `NouveautesPage`
- **Comment:** While a send is in progress, `AutoRefresh` calls `router.refresh()` every 5 s. One failed read
  (tunnel blip, API restart) makes the page return `<ReadFailure>`, which unmounts `NewsComposer` and loses the
  subject and rows being typed — and unmounts `AutoRefresh`, so the page never comes back by itself. Fix: render the
  composer whatever the history read does; a failed history read becomes an inline error in « Envois précédents »
  (and poll only the history).

### Finding 5
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed (merged: error-handling + business-logic + regression agents)
- **File:** api/ClinicManagement.API/BackgroundJobs/PlatformNewsDispatchJob.cs
- **Line:** 25
- **Anchor:** `PlatformNewsDispatchJob.Dispatch` (+ `PlatformNewsController.Send:85`)
- **Comment:** Two runs can mail the same admin twice once a run passes 10 minutes. `[DisableConcurrentExecution]`
  locks per method, and Hangfire.PostgreSql deletes a lock older than its 10-minute `DistributedLockTimeout`
  (a job still running at 30 minutes is also re-fetched). A replay with `InProgress = true` queues a second job
  every time. With a slow SMTP relay both runs walk the same Pending rows and both send. The job's comment
  « two runs can never e-mail the same address twice » is not true. Fix: claim each recipient before sending
  (conditional `UPDATE … Status = Sending WHERE Status = Pending`, act only on 1 row), queue only when
  `!AlreadyQueued`, and correct the comment.

### Finding 6
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformNewsDispatcher.cs
- **Line:** 85
- **Anchor:** `PlatformNewsDispatcher.DispatchAsync`
- **Comment:** The outcome of a send that already happened is saved with the job's cancellation token. A shutdown
  (every deploy) or a database error between `SendAsync` returning Sent and this save leaves the row Pending, and the
  retry mails that admin again. Fix: save with `CancellationToken.None`; the claim-before-send of Finding 5 closes
  the rest.

### Finding 7
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/SendPlatformNewsCommand.cs
- **Line:** 116
- **Anchor:** `SendPlatformNewsCommandHandler.Handle`
- **Comment:** `catch (DbUpdateException)` has no filter, while the repo's convention is
  `when (IsUniqueViolation(ex))` (`RequestPasswordResetCommand.cs:175`, `SignUpClinicCommand.cs:178`,
  `SaveStaffQuickMessagesCommand.cs:112`). Any other save failure is logged as « lost the race », and the re-read
  inside the catch can throw past the sibling catch-all as an unhandled 500. Fix: add the unique-violation filter.

### Finding 8
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 286
- **Anchor:** `NewsComposer` (« Envoyer à N cabinets » / confirm)
- **Comment:** AC-3 says the confirm's counts equal what is attempted. The button and the sheet use the audience
  read when the page loaded; the server recomputes at the click. A signup, an expiring trial or a page left open
  across midnight makes them differ. Fix: re-read the audience when the sheet opens (and optionally post the
  confirmed count, refused with a code if it changed).

### Finding 9
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** console/components/news-composer.tsx
- **Line:** 50
- **Anchor:** `NewsComposer` (subject / rows state)
- **Comment:** The draft lives only in React state: a back gesture, « ← Retour au portefeuille », a reload or a
  session redirect drops it silently. Fix: mirror `{ subject, rows }` to `sessionStorage` (try/catch), restore on
  mount, clear after a successful send.
- **Challenge note:** Major → Minor — these are ordinary page navigations, not a new dismissal channel on a dialog
  (DEVICE-CONTRACT #17's case); no console page persists drafts today.

### Finding 10
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (merged: device + error-handling agents)
- **File:** console/components/news-composer.tsx
- **Line:** 296
- **Anchor:** `NewsComposer` (Aperçu)
- **Comment:** A failed preview shows the error above the previous preview (stale, not what will go out), or above
  a pulsing skeleton on first load (« error » and « loading » at once), with no retry until the next keystroke; the
  `role="alert"` re-announces on every failure. Fix: on failure replace the iframe/skeleton with the error and a
  « Réessayer » button (a nonce in the effect deps), `role="status"`.

### Finding 11
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 286
- **Anchor:** `NewsComposer` (confirm Sheet trigger)
- **Comment:** The sheet opens from a plain `onClick`, not a `SheetTrigger`, so on close (Revenir, Escape, send)
  focus falls to `<body>`. The sibling dialogs use `<SheetTrigger asChild>` (`cancel-period-dialog.tsx:141`). Fix:
  same pattern; after a successful send focus the notice or the subject.

### Finding 12
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 248
- **Anchor:** `NewsComposer` (Supprimer / moveRow)
- **Comment:** Deleting a row removes the focused button; moving a row to the first/last position disables it. Focus
  drops to `<body>` and Tab restarts at the top. Fix: after delete focus the textarea now at that index; at an edge
  focus the opposite move button; after « Ajouter » focus the new textarea.

### Finding 13
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 306
- **Anchor:** `NewsComposer` (Aperçu iframe)
- **Comment:** `h-[36rem]` (576 px) is about 1.5× a landscape phone's height, so most scrolls land in the e-mail
  instead of the page; on the owner's ~730 px laptop the preview scrolls out of view while editing rows 6–12. Fix:
  `h-[min(36rem,70dvh)]` and `lg:sticky lg:top-4 lg:self-start` on the preview section.

### Finding 14
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 302
- **Anchor:** `NewsComposer` (Aperçu iframe)
- **Comment:** `sandbox=""` still lets the frame navigate itself. A tap on the e-mail's « Ouvrir Apexa » loads the
  app's /login inside the preview, which CSP / `X-Frame-Options` refuse, so the box shows a browser error page until
  the next keystroke. Fix: inject `<style>a{pointer-events:none;cursor:default}</style>` into the preview HTML.

### Finding 15
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-history.tsx
- **Line:** 54
- **Anchor:** `NewsHistory` (CardList title)
- **Comment:** `CardList`'s title is `truncate`; a 120-character subject is cut to ~30–40 characters at 320–390 px
  with no way to read it in full on a phone. Fix: render the title as
  `<span className="whitespace-normal break-words">`.

### Finding 16
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 286
- **Anchor:** `NewsComposer` (« Envoyer » when the audience is empty)
- **Comment:** With no eligible address the button reads « Envoyer à 0 cabinet », disabled, with no reason — the
  « sans adresse » count is only inside the sheet, which cannot open. A refusal stays visible. Fix: when
  `recipients === 0` show the server's `NoAudience` sentence (with the without-address count) beside the buttons.

### Finding 17
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 108
- **Anchor:** `NewsComposer.post`
- **Comment:** `post()` re-implements `readRefusal` / `readJson` from `console/lib/refusal.ts`, with a different
  fallback sentence, and returns an untyped record — hence `String(data.testSentTo)` (prints « undefined » if the
  field is missing) and the `as unknown as PlatformNewsQueued` cast. Fix: use the two helpers with typed results.

### Finding 18
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 23
- **Anchor:** `MAX_ITEMS` / `SUBJECT_MAX` / `TEXT_MAX`
- **Comment:** These copy `PlatformNewsBroadcast.MaxItems`, `SubjectMaxLength` and `PlatformNewsItem.TextMaxLength`
  (and `EMOJIS[0]` copies `DefaultEmoji`) with nothing holding them together — a mirrored set. A server change lets
  the form accept text the send refuses. Fix: a pair check in `check-responsive.mjs` reading the C# constants, or
  serve the limits on the audience read.

### Finding 19
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** console/lib/api/platform.ts
- **Line:** 801
- **Anchor:** `PlatformNewsItem`
- **Comment:** Exported, imported by nothing; the composer builds `items` as an anonymous `{ emoji, text }[]`. Fix:
  type the body with it, or delete it.

### Finding 20
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** console/components/news-composer.tsx
- **Line:** 38
- **Anchor:** `NewsComposer` (JSDoc)
- **Comment:** A 6-line comment block; the owner's rule is one line, two at most. Keep only
  `/** One requestId per form, kept across a failed press, so a retry or double-click sends once. */`.

### Finding 21
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** console/lib/bff.ts
- **Line:** 10
- **Anchor:** `forwardJson`
- **Comment:** A second way to write a BFF route beside nine hand-written ones that repeat the same token check and
  refusal mapping. Optionally export the refusal mapping and rename to `forwardPost` (it always POSTs).
- **Challenge note:** Minor → Suggestion — converting the nine existing routes is outside this feature; the helper
  itself is correct and reduces this feature's own copies from three to one.

### Finding 22
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.API/Controllers/Platform/PlatformNewsController.cs
- **Line:** 85
- **Anchor:** `PlatformNewsController.Send`
- **Comment:** Static `BackgroundJob.Enqueue` makes the enqueue / no-enqueue branch untestable; `IBackgroundJobClient`
  would allow a mock. The line-82 comment is unclear (« re-queues a harmless no-op guard »).
- **Challenge note:** Minor → Suggestion — the static form is the repo's precedent (`MedicalDocumentsController.cs:297`,
  `Program.cs:1290`).

### Finding 23
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformNews.cs
- **Line:** 111
- **Anchor:** `PlatformNewsRules` / `PlatformNewsAudiencePlan` / `PlatformNewsEmail`
- **Comment:** Four public types in a file named after none of them; split into `PlatformNewsRules.cs`,
  `PlatformNewsAudiencePlan.cs`, `PlatformNewsEmail.cs`.
- **Challenge note:** Minor → Suggestion — multi-type files exist in this repo (e.g. `StaffQuickMessageList.cs`);
  findability only.

### Finding 24
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformNews.cs
- **Line:** 176
- **Anchor:** `PlatformNewsEmail.PreviewHtml`
- **Comment:** Swapping `cid:` for a data URI by string replace depends on how `EmailLayout` writes the logo; moving
  it into `EmailLayout` (`BrowserHtml`) keeps the rule with the class that writes `cid:`.
- **Challenge note:** Minor → Suggestion — `LogoContentId` is a public constant and the layout writes it in one
  place; the coupling is real but narrow.

### Finding 25
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformNewsDispatcher.cs
- **Line:** 50
- **Anchor:** `PlatformNewsDispatcher.DispatchAsync`
- **Comment:** Fully qualified `Domain.Entities.PlatformNewsItemInput` and an inline broadcast → draft mapping; a
  `PlatformNewsDraft.From(broadcast)` would sit with the draft type.

### Finding 26
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/SendPlatformNewsCommand.cs
- **Line:** 86
- **Anchor:** `SendPlatformNewsCommandHandler.Handle`
- **Comment:** « read the audience, then plan it » is repeated in `GetPlatformNewsAudienceQuery`; the confirm and the
  send agree only because both copy the same two lines. One `PlatformNewsAudiencePlan.ReadAsync(...)` for both.

### Finding 27
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/Commands/SendPlatformNewsCommand.cs
- **Line:** 135
- **Anchor:** `SendPlatformNewsCommandHandler.Replay`
- **Comment:** « cabinets = distinct ClinicId among recipients » is written three times (here, the audience plan,
  the SQL projection), and the replay loads and tracks every recipient just to count them. A `ClinicCount` on the
  aggregate and an `AsNoTracking` replay read.

### Finding 28
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformNews.cs
- **Line:** 172
- **Anchor:** `PlatformNewsEmail.AppLink`
- **Comment:** A 4th hand-built `/login` URL (`CreateClinicUserCommand`, `ResendStaffInvitationCommand`,
  `VerifyClinicSignUpCommand`, the last without `TrimEnd`). One `LoginUrl` on `IPublicAppUrlProvider`.

### Finding 29
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Common/Email/EmailLayout.cs
- **Line:** 156
- **Anchor:** `EmailLayout.Html`
- **Comment:** « Is there a kicker » is decided twice (inside `Kicker()` and inline for the heading padding);
  compute `hasKicker` once.

### Finding 30
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Infrastructure/Persistence/Configurations/PlatformNewsBroadcastConfiguration.cs
- **Line:** 61
- **Anchor:** `PlatformNewsRecipientConfiguration.Configure`
- **Comment:** Bare `HasMaxLength(200/256/200)` beside named constants elsewhere in the same entity, and 256 for
  `Email` against `User.Email`'s 200 (`UserConfiguration.cs:26`). Named constants, and 200 to match.

### Finding 31
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** api/ClinicManagement.Application/Features/Platform/PlatformReadShape.cs
- **Line:** 320
- **Anchor:** `PlatformReadShape.AllowedLeafNames`
- **Comment:** A 3-line comment; shorten to one line.
- **Challenge note:** kept as Suggestion — every block in this allow-list carries a reasoned comment by design.

### Finding 32
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** console/components/news-history.tsx
- **Line:** 37
- **Anchor:** `NewsHistory` (table « Objet » cell)
- **Comment:** `max-w-80` on a `<td>` does nothing in auto table layout, and `break-words` does not lower the cell's
  minimum width — a long unbroken subject widens the table. Wrap the content in
  `<div className="max-w-80 [overflow-wrap:anywhere]">`.

### Finding 33
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** console/scripts/check-responsive.mjs
- **Line:** 217
- **Anchor:** `check("touch-target-position", "N9", …)`
- **Comment:** N9 matches only a bare `.touch-target` selector with `position:` text; `@utility touch-target`,
  `@apply relative` or `button.touch-target` would bring the run-1 bug back with N9 green. Widen it to any selector
  containing `.touch-target` (no pseudo-element, not in `:where`) or `@utility touch-target` that sets a position.

### Also raised, merged above or adjusted
- Textarea is 2 rows at 320 px (~40 of 300 characters visible) — **Suggestion** (adjusted from Minor: the console's
  leading device is the desk): `rows={3}` or `[field-sizing:content] max-h-48`, select on its own line below `sm:`.
  Line 215, `NewsComposer`.
- `ReadFailure` is the console's 5th copy of the same component — **Suggestion**: extract
  `components/read-failure.tsx` with a `title` prop. `console/app/nouveautes/page.tsx:66`.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 4 |
| Minor | 16 |
| Suggestion | 15 |
| **Total** | **35** (33 numbered + 2 listed Suggestions) |

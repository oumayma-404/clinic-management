# Progress: Nouveautés par e-mail depuis la console

**Started:** 2026-11-06
**Type:** Small
**Branch:** feature/console-news-broadcast (worktree `.claude/worktrees/console-news-broadcast`, from `origin/main` 7000b0e0)

## Status
- [x] Implementation
- [x] Quality checks — `dotnet build` (solution via UnitTests) 0 errors, no new warning in touched files; console
      `tsc --noEmit` clean, `check:responsive` 15/15, `npm run build` green
- [x] Blast radius closed (15 rows: 8 changed, 1 to re-test, 6 unaffected)
- [x] Tests — 3 new classes + 4 scenarios in `EmailLayoutTests`; **full unfiltered suite 6 372 passed, 0 failed**
      (6 skipped, all pre-existing); no new warning in touched test files
- [x] Browser QA — `qa/run-2.md` GREEN (22 ✅, 3 ⏭ with reasons); run 1 found F-1 (console sheet « X » + login eye, fixed with `:where(.touch-target)` + guard N9)

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `EmailContent` (+ `Kicker`, `Bullets`) | shared e-mail model | 9 transactional senders (signup, verify, 2× reset, 2× console reset, staff reset/TOTP, team notice) | unaffected — additive `init` members defaulting to null / empty |
| 2 | `EmailLayout.Html` / `PlainText` | the one e-mail renderer | `SmtpTransactionalEmailSender` (every transactional mail) | must re-test — AC-10: a mail with no kicker/bullets renders byte-identical |
| 3 | `ClinicActivityRepository.SubscriptionPredicate(Active)` | « covered and not suspended » SQL | portfolio list filter, summary strip counts | unaffected — predicate untouched; the audience read REUSES it rather than copying it |
| 4 | `IClinicActivityRepository` (+ `GetNewsAudienceAsync`) | repo interface | `PlatformConsoleFakes` (tests) implement it | must change if the fake implements the interface (compile) |
| 5 | `ApplicationDbContext` + migration (3 new tables) | schema | model snapshot | must change — migration + snapshot in this commit |
| 6 | `TenantScopeFilterTests.UnfilteredByDesign` | derived guard over ClinicId tables | — | must change — `PlatformNewsRecipient` carries a ClinicId, vendor-owned like `PlatformAccessEntry` |
| 7 | `ClinicPurge.SurvivesByDesign` | derived guard over every table | `ClinicPurgePlanTests` | must change — both news tables survive a cabinet's deletion (the vendor's send log) |
| 8 | `ClinicArchiveScope.Excluded` | archive / PC de secours scope | `ClinicArchiveScopeTests` | must change — vendor tables never travel in a cabinet's zip |
| 9 | `AuditSaveChangesInterceptor.ExcludedEntityTypes` | clinic « Journal d'activité » | every save | must change — a news row must not land in a cabinet's own journal |
| 10 | `SubscriptionExemptionCoverageTests.ExpectedExemptWrites` | derived guard over non-GET actions | — | must change — 3 new console POSTs carry `[AllowsWithoutSubscription]` |
| 11 | `ConsolePortGate` (`/api/platform/*`) | console-port-only routing | all console routes | unaffected — new routes live under `/api/platform/news` |
| 12 | `RealtimeResourceResolver` ExcludedAreas | realtime keys | — | unaffected — code stays under `Features/Platform`, already excluded |
| 13 | console `app/cabinets/page.tsx` header | console nav | journal page has its own back link | must change — add « Nouveautés » link |
| 14 | console CSP (`next.config.ts`) | 4 byte-identical copies | — | unaffected — preview is an `srcdoc` iframe + `data:` logo, both already allowed |
| 15 | `PlatformReadShape.AllowedLeafNames` | closed set every console DTO name must be in | `PlatformReadShapeTests` (both directions) | must change — 13 names added, each returned by a news DTO; no admin address is returned |

| 16 | `console/app/globals.css` `.touch-target` → `:where(.touch-target)` (QA F-1) | containing block for the 44 px `::after` | every console `<Button>` (no position utility → still `relative`), `ui/sheet.tsx` close « X » and `ui/password-input.tsx` eye (both `absolute` → now win) | must re-test — C2, C6, D5; guarded by `check:responsive` N9 |

Row 4 turned out **unaffected**: only Moq mocks implement `IClinicActivityRepository`. Row 2 is the one `must re-test`.

## Files Changed
- **Domain**: `Entities/PlatformNewsBroadcast.cs`, `Entities/PlatformNewsRecipient.cs`, `ValueObjects/PlatformNewsItem.cs`,
  `Enums/PlatformNewsDeliveryStatus.cs`, `Repositories/IPlatformNewsRepository.cs`, `Repositories/IClinicActivityRepository.cs`
- **Application**: `Common/Email/EmailContent.cs` (+`Kicker`, +`Bullets`), `Common/Email/EmailLayout.cs`,
  `Features/Platform/PlatformNews.cs` (rules · audience plan · the one composer), `PlatformNewsDispatcher.cs`,
  `Commands/SendPlatformNewsCommand.cs`, `Commands/SendPlatformNewsTestCommand.cs`, `Queries/GetPlatformNewsAudienceQuery.cs`,
  `Queries/PreviewPlatformNewsQuery.cs`, `Queries/ListPlatformNewsQuery.cs`, `Dtos/PlatformNewsDtos.cs`, `PlatformReadShape.cs`, `Extensions.cs`
- **Infrastructure**: `Persistence/Configurations/PlatformNewsBroadcastConfiguration.cs`, `Repositories/PlatformNewsRepository.cs`,
  `Repositories/ClinicActivityRepository.cs` (`GetNewsAudienceAsync`), `Persistence/ApplicationDbContext.cs`, `ClinicPurge.cs`,
  `ClinicArchiveScope.cs`, `AuditSaveChangesInterceptor.cs`, `Extensions.cs`, migration `20261106120514_AddPlatformNewsBroadcasts` (+ snapshot)
- **API**: `Controllers/Platform/PlatformNewsController.cs`, `BackgroundJobs/PlatformNewsDispatchJob.cs`, `Models/PlatformNewsRequests.cs`
- **UnitTests (named decisions only)**: `Common/TenantScopeFilterTests.cs`, `Api/SubscriptionExemptionCoverageTests.cs`
- **Console**: `app/nouveautes/page.tsx`, `components/news-composer.tsx`, `components/news-history.tsx`, `components/auto-refresh.tsx`,
  `app/bff/nouveautes/{route,test/route,apercu/route}.ts`, `lib/bff.ts`, `lib/api/platform.ts`, `app/cabinets/page.tsx` (header link)

## Working tree note (start of session)
- Main checkout is on `feature/security-remediation` with ~30 dirty files (other work). This feature is built in its own
  worktree from `origin/main`, so none of them are touched or staged.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Error codes `news_invalid` / `news_no_audience` / `email_not_configured` (snake_case), not the spec's hyphenated spelling | every other refusal code in the repo is snake_case |
| Greeting is « Bonjour <Nom>, » (no « Docteur ») | the spec cites « existing greeting rule », which is `EmailGreeting.For`; an admin is not always a dentist |
| « Happy Apexing ! 🦷✨ » renders as a brand-coloured line **above** the heading | « a word at the start » — it is the first line of the e-mail |
| Test send failure → `502 news_test_failed`; send answers `202` | 503/202 as pinned; 502 for « the mail server refused », distinct from « not configured » |
| Test e-mails' subject is prefixed « [Test] » | tells the test apart from the real one in an inbox |
| Audience read lives on `IClinicActivityRepository` | it owns the private « Actif » predicate; a second repo would have to copy it |
| `TenantScopeFilterTests` + `SubscriptionExemptionCoverageTests` entries added here | named decisions that move with the entity / the attribute, not test scenarios |
| Recipient rows **survive** a cabinet's deletion (`ClinicPurge.SurvivesByDesign`) | the vendor's own send log, `PlatformAccessEntry`'s rule |

## Significant Deviations
**DEV-1 — `POST /api/platform/news/preview` added (not in the pinned contract).** Original: four endpoints. Actual: a
fifth, read-only, returning the rendered HTML. Why: AC-6 requires the preview to use the same code as the send, and
the layout lives on the server. Impact: one extra console route, writes nothing. Approved: not asked — forced by AC-6.

## Test Plan
| AC | Action | Target file | Notes |
|----|--------|-------------|-------|
| AC-1 | New class | `Features/Platform/PlatformNewsTests.cs` | admins of eligible cabinets are recipients; a cabinet with none counted apart |
| AC-1 | — | covered by QA (api C5, D3) | the « Actif » predicate is SQL; no DB in UnitTests |
| AC-2 | New class | `PlatformNewsTests.cs` | two admins both; one address twice (any case) → one e-mail |
| AC-3 | New class | `Features/Platform/PlatformNewsCommandTests.cs` | the broadcast records the plan's recipients at the click; counts returned |
| AC-4 | New class | `PlatformNewsCommandTests.cs` | repeated `RequestId` → replay, nothing written; a racing press losing on the unique index → the winner |
| AC-5 | New class | `PlatformNewsCommandTests.cs` | test goes to the signed-in account only, « [Test] » subject; no news repository in the handler at all |
| AC-6 | New class | `PlatformNewsTests.cs` + `PlatformNewsCommandTests.cs` | one composer: frame text, preview = real HTML with the logo inlined, dispatcher greets each recipient and names its cabinet |
| AC-7 | Add scenarios | `Common/Email/EmailLayoutTests.cs` | bullets with markers in HTML and text, in order; bullet text escaped; kicker first in both parts |
| AC-8 | New class | `PlatformNewsCommandTests.cs` | send and test refused with `email_not_configured` before reading the audience; dispatcher records « not configured » as a French failure |
| AC-9 | New class | `PlatformNewsTests.cs` | 6 refused forms → `news_invalid`; blank rows dropped, whitespace + CR/LF collapsed, default emoji |
| AC-10 | Add scenarios | `EmailLayoutTests.cs` | no kicker/bullets → no kicker row, no bullet panel, heading row unchanged |
| AC-11 | — | covered by QA run-2 (C1, C2, C4) | 320 px, coarse pointer, widths |
| — | New class | `Api/PlatformNewsControllerTests.cs` | each refusal code → 503 / 502 / 400 |
| — | Modify guards | `RelayFenceCoverageTests`, `AuditInterceptorTests` | reviewed entries for the new job and the new excluded root (see below) |

**Coverage notes**
- **AC-10 byte-identity, verified once outside the suite:** `main`'s `EmailLayout` and this branch's, compiled side by
  side in a scratch project, render a full and a minimal existing e-mail **identically** (HTML and text). The suite
  keeps the structural half (`A_message_with_no_kicker_or_bullets_…`), since a golden string would break on every
  intended future change.
- The successful `POST /api/platform/news` path queues a Hangfire job through the static `BackgroundJob` — not
  unit-testable without job storage; its effect is QA rows A4–A6 (3 e-mails sent, history « 3 / 3 »).
- `ListRecentAsync`'s SQL (`COUNT(DISTINCT)` in a projection) ran against real PostgreSQL in QA (A6, B1).

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Unit — new + touched guards | `PlatformNews*`, `EmailLayoutTests`, `PlatformReadShapeTests`, `TenantScopeFilterTests`, `SubscriptionExemptionCoverageTests`, `ClinicPurgePlanTests`, `ClinicArchiveScopeTests` | 111 passed, 0 failed |
| Unit — **full, unfiltered** (the repo's gate) | none | 6 372 passed, 0 failed, 6 skipped (pre-existing) |

## Bug found & fixed by the tests
The first full run had **3 failures, all wiring this feature had missed** (each guard is derived, so it caught the
new code the day it was written):

| Guard | What it caught | Fix |
|---|---|---|
| `LogTemplateCoverageTests` | two log lines put a recipient's / the console account's **e-mail address** in a log placeholder | log `{RecipientId}` / `{AccountId}` instead (`PlatformNewsDispatcher.cs`, `SendPlatformNewsTestCommand.cs`) |
| `RelayFenceCoverageTests` | `PlatformNewsDispatchJob` declares `UseSystemWide` and never asks `IClinicWriteFence` | reviewed entry: the news tables are in `ClinicArchiveScope.Excluded`, so `ClinicRelayScope` never carries them and the fence cannot refuse their writes |
| `AuditInterceptorTests` | the exclusion list grew without its documented reason | kept `PlatformNewsBroadcast` (no cabinet — `PlatformAccount`'s reason) and **dropped `PlatformNewsRecipient`**: a child entity never gets its own audit row, so excluding it did nothing |

## Review fixes (`reviews/feature-review.md`, 35 findings)
| Findings | Verdict | Done |
|---|---|---|
| F1–F4 (Major), F5–F20 (Minor) | valid | all fixed |
| F22, F25, F31, F32, F33, textarea rows | valid, cheap, same code | fixed |
| F21 (partly: renamed `forwardPost`), F23, F24, F26–F30, ReadFailure copy | optional refactors | skipped |

What changed, by mechanism:
- **No double e-mail:** each recipient is **claimed** (`Sending`, saved) before its e-mail leaves; a run that loses the
  claim stops (xmin `ConflictException`); a claim older than 15 min ends `Failed`, never re-sent; outcomes save with
  `CancellationToken.None`. `PlatformNewsDeliveryStatus.Sending = 3` appended (stored as int, no migration).
- **Never stuck « en cours »:** recurring `resume-platform-news` (every 5 min, `ServesPlatformConsole` only) re-runs
  every send unfinished after 2 min. A repeated press no longer queues a second job.
- **Race catch filtered** to PostgreSQL 23505 (`IsUniqueViolation`, the repo's pattern).
- **Console:** history re-read on its own (BFF GET `/bff/nouveautes/historique`), never the page — a failed read stays
  inline; the confirm re-reads the audience (`/bff/nouveautes/audience`); draft kept in `sessionStorage`; row buttons
  44 px on a finger with 8 px gaps and the trash set apart; focus managed (confirm, delete, move, add); preview retry,
  inert links, `min(36rem,70dvh)` + sticky; empty-audience reason; result/subject wrap in cards; shared
  `readRefusal`/`readJson`. `auto-refresh.tsx` deleted.
- **Guards:** `check:responsive` N9 widened (any `.touch-target` selector outside `:where`, `@utility`, `@apply`) and
  new N10 (form limits + default emoji = server constants); both proven red on deliberate violations.

Gates after the last edit: API build ✅ · full unit suite **6 380 passed, 0 failed** · console `tsc` ✅ ·
`check:responsive` 17/17 ✅ · `npm run build` ✅ · browser QA `qa/run-3.md` GREEN.

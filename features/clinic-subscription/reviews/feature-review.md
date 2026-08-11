# Feature Review: clinic-subscription

**Status:** COMPLETE
**Challenged:** Yes
**Date:** 2026-08-10
**Challenged Date:** 2026-08-10
**Parent Branch:** `main` (merge-base `9798b95`); the feature sits on `b79a4f4`
**Feature range reviewed:** `b79a4f4..HEAD` — 7 commits, Parts A–G (`c541897` … `e379f09`)
**Files Reviewed:** 136 code files (+11,669 / −89)

**Review method:** seven parallel agents (the default four adapted to this stack — MediatR + `Result<T>`, no ROP — plus Device & UX, Frontend correctness and Security). Every finding below was then re-read against the **actual source files**, not the diff, including verification of every precedent claim and of the feature's own `spec.md`.

## Challenge Summary

| Metric | Count |
|--------|-------|
| Original findings | 54 |
| Confirmed | 40 |
| Confirmed (adjusted) | 12 |
| Dismissed (false positive) | 1 |
| Dismissed (pre-existing) | 1 |
| **Final findings** | **52** |

### What the challenge pass changed

- **1 false positive removed.** The claim that `AuthController.GetMode` answers "are subscriptions enforced?" from two sources is **factually wrong** — lines 91 and 96 both read `deployment.RequiresSubscription`. The cross-file variation (`ISubscriptionPolicy` in Application, `DeploymentProfile` in API) is a **documented structural requirement**: `Application/CLAUDE.md` states the policy seam "is **structurally required rather than stylistic**: `DeploymentProfile` lives in Infrastructure and this project references Domain alone."
- **1 pre-existing issue removed.** The `POST /api/backup` cross-tenant `pg_dump` + unvalidated destination path lives entirely in **unchanged** code (`PgDumpBackupService`); this branch only adds the exemption attribute, and FR-8 + the manual/scheduled coherence argument make that exemption spec-aligned. ⚠️ **The underlying exposure is real and should be captured as its own item** — see the note at the foot of this file.
- **5 severities lowered because the spec or the code documents the behaviour** (Findings 10, 41, 42, 44, 45). Two of these matter most: **AC-4.9 explicitly exempts "rendering a document for download"**, and the 402-dispatch keying is deliberate with a stated reason.
- **5 severities lowered because measured impact is smaller than reported** (Findings 11–14, 43).
- **1 finding had its numbers corrected but kept its severity** (Finding 6 — the dismiss control is *absent* on the expired state, so the paragraph gets ~146 px not ~90 px).
- **2 findings were strengthened by the challenge.** Finding 7's divergence is confirmed against **three** independent authorities, and Finding 2's risk class is **named verbatim in the spec** (FR-8, line 333).

---

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Services/SubscriptionLedger.cs`
- **Line:** 102
- **Anchor:** `SubscriptionLedger.FoldWithSpans` (`ExplicitEndsOn` branch) — with `SubscriptionPeriod.Create` (`api/ClinicManagement.Domain/Entities/SubscriptionPeriod.cs:102`)
- **Comment:** **A recorded grant can silently revoke paid entitlement, breaking AC-5.2.** Verified at lines 96–105: the month and day branches anchor on `start = max(cursor, recordedDay)`, but the third assigns `cursor = entry.ExplicitEndsOn!.Value.Date.AddDays(1)` **unconditionally** — `start` is computed and then **unused** on that path, which is the tell — and `endsOn` is set from it. Reachable from the shipped verb: a cabinet covered to 2027-09-20, vendor runs `subscription-grant --until 2026-12-31`; `EndsOn` becomes 2026-12-31 and **~9 months of paid cover are revoked**, with no refusal and a success message. Compounded by `SubscriptionPeriod.Create`, verified at lines 102–127: `forms > 1`, `durationMonths <= 0`, `durationDays <= 0` and `amount < 0` are all guarded, and **`ExplicitEndsOn` is validated nowhere** — not against `recordedOnClinicDay`, no upper bound. So `--until 2026-08-10` (mistyped year) makes a paying cabinet read-only mid-consultation, and `--until 9999-12-31` grants effectively permanent cover through the door the handler's own comment says is closed. Fix both halves: (a) `cursor = Max(entry.ExplicitEndsOn.Value.Date.AddDays(1), start)` so no recorded entry can reduce cover and only a cancellation may; (b) refuse an `explicitEndsOn` earlier than `recordedOnClinicDay` in `Create`, bound it a few years out the way `SubscriptionPolicy.MaxTrialDays` bounds the trial, and have `SubscriptionGrantCommand` print before/after dates as a shortening warning.

### Finding 2
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/BackgroundJobs/NotificationJob.cs`
- **Line:** 151
- **Anchor:** `NotificationJob.ReviewBlockedRowsAsync` — and `PushDispatchJob.ReviewBlockedRowsAsync` (`api/ClinicManagement.API/BackgroundJobs/PushDispatchJob.cs:245`)
- **Comment:** **Part G re-arms, on the un-park side, the starvation defect `Blocked` was invented to fix — and the spec names this exact risk class.** Verified in both repositories: `NotificationRepository.GetDueForDispatchAsync(batchSize, perClinicBound)` (line 25) carries the fairness bound and a `GroupBy` backlog projection, while `GetBlockedForReviewAsync(take)` (line 82) is a flat `Status == Blocked` scan ordered `ScheduledFor`/`Id` with `.Take(take)`, **no clinic dimension and no `BlockedReason` filter**; `PushDeliveryRepository.GetBlockedForReviewAsync` (line 77) is identical. `PurgeTerminalOlderThanAsync` (line 93) names only `Sent`/`Failed`, so `Blocked` rows are never deleted. Since a `SubscriptionExpired` row never clears while a cabinet stays expired, those rows accumulate **permanently at the front** of an oldest-first capped scan. Concrete failure: cabinet A expires and parks 60 reminders (default batch 50); cabinet B's WhatsApp credentials were missing so its rows are parked `ChannelUnconfigured`; the operator fixes them; B's rows sort after A's 60, the review reads only the first 50 (all A's, all skipped), and **B's reminders are never released** while A remains expired. ⚠️ `spec.md` FR-8 line 333 states the rule verbatim: *"Parking must not be replaced by « filter expired cabinets out of the dispatch scan ». That recreates the starvation defect the parked status was invented to fix: unsendable rows accumulate at the front of an oldest-first, capped scan and consume every tick for ever."* Fix: give the review scan the same per-clinic bound, or exclude `BlockedReason == SubscriptionExpired` and drive that un-park off a bounded per-clinic query.

### Finding 3
- **Severity:** Major
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/SubscriptionRefold.cs`
- **Line:** 57
- **Anchor:** `SubscriptionRefold.SaveAsync`
- **Comment:** **The conflict retry re-reads through EF's identity map and can silently re-apply stale ledger values.** Verified: the retry path detaches only the entitlement (`unitOfWork.StopTracking(subscription)`, line 87) and reloads it (line 89), then the loop re-enters at line 57 calling `subscriptions.GetEntriesAsync` — which `ClinicSubscriptionRepository.cs:33–39` performs **with no `AsNoTracking()`**, while its sibling `GetForReportAsync` (line 50) uses `AsNoTracking()` explicitly. EF does not overwrite current/original values of an already-tracked entity, so on attempt 2+ every `SubscriptionPeriod` returns with its **attempt-1 values**. If the conflict came from a concurrent `subscription-cancel`, the re-fold sees `IsCancelled == false` for the entry just voided, computes an `EndsOn` still including its days, and **commits it** — the drift `verify-schema`'s `subscription-end-date-matches-ledger` exists to catch, produced by the code meant to converge. Second problem: the loop assumes the conflict is on `ClinicSubscription`, but the cancel handler's save also carries the modified `SubscriptionPeriod` (its own root with its own `xmin`); a conflict there is not resolved by detaching the entitlement, so all five attempts re-issue the same doomed UPDATE and the operator is told the cabinet was concurrently modified for a cause that is not that. Fix: detach the ledger entries too before re-reading, or add an `AsNoTracking()` `GetEntriesForFoldAsync`.

### Finding 4
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Controllers/UsersController.cs`
- **Line:** 97
- **Anchor:** `UsersController.ResetPassword`
- **Comment:** **An expired cabinet loses its guaranteed reads, not just its writes.** Verified: `ResetPassword` (line 97) carries **no** `[AllowsWithoutSubscription]`, while `SetStatus` (line 115) does and `CreateUser` (line 68) does not either. So on an expired `HostedMultiTenant` cabinet an admin cannot reset a colleague's password, and a staff member who has forgotten theirs cannot log in at all — losing the reads, CSV exports and PDFs AC-4.1/AC-4.2 guarantee, which is the load-bearing product decision of the whole feature. No other recovery exists on a hosted deployment: `change-password` needs the current password, `POST /api/users` is likewise not exempt, and `reset-admin-password` needs container access and only targets admins. ⚠️ **AC-4.7's own reasoning extends to this endpoint verbatim** — *"Changing a password still works, including a password change the administrator has forced — otherwise an expired cabinet whose user must change their password can do neither"* — which is exactly the argument for exempting the reset that *creates* that forced change. Fix: exempt `ResetPassword` with a reason stating that regaining read access must not depend on payment, and add `Users.ResetPassword` to `SubscriptionExemptionCoverageTests.ExpectedExemptWrites`.

### Finding 5
- **Severity:** Major
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `deploy/docker-compose.hosted.yml`
- **Line:** 42
- **Anchor:** `services.api.environment`
- **Comment:** **On the only deployment kind where the gate exists, « Abonnement » is a dead end that names nobody to contact.** Verified: `grep -rn "Subscription__\|Subscription:" deploy/` returns **zero matches** across the whole folder, and the `api` service enumerates its environment explicitly with **no `env_file`** — so an operator cannot set `Subscription__Plans__*`, `Subscription__PaymentInstructions`, `Subscription__ContactEmail`/`ContactPhone` or `Subscription__TrialDays` without editing the compose file. Meanwhile `appsettings.json:157–164` ships every price at `0`, and that file's own comment says a non-positive figure *"reads as « tarif non publié »"*. So the screen a 402 points a chairside user at renders « Aucun tarif n'est publié », « Les modalités de paiement ne sont pas publiées… » and « Aucune coordonnée n'est publiée », on exactly the cabinet just made read-only — defeating US-2's « paying is never blocked on not knowing what to do ». Fix: add the five `Subscription__*` variables to the `environment:` block sourced from `.env`, document them in `.env.hosted.example` and `deploy/README.md`, and list the five `subscription-*` verbs beside the existing `verify-schema`/`reconcile-money`/`provision-clinic` block — they are the only way to grant, and they are undocumented for operators.

### Finding 6
- **Severity:** Major
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — figures corrected)
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 65
- **Anchor:** `SubscriptionBanner`
- **Comment:** **The banner never wraps, so it becomes a ~160 px strip at 320 px on every screen of the app.** Verified: the root (line 60) is `flex-wrap`, but the paragraph is `min-w-0 flex-1` — `flex: 1 1 0%` with `min-width: 0`, so its hypothetical main size is **0** and it can never trigger a wrap of the flex line. The `shrink-0` icon and the `whitespace-nowrap` button hold their widths, so the paragraph only ever receives the leftover. On the **expired** state (icon 16 px + « Renouveler » ~102 px + 2 × 12 px gaps ≈ 142 px) that leaves ~146 px of a 288 px content box at 320 px — roughly **8 lines ≈ 160 px, ~28 % of a 320 × 568 viewport, permanently, on every screen**. The comment at lines 51–52 asserts the opposite ("the wrap only ever happens on a narrow *portrait* phone"), which is the belief the code encodes. It also misses the spec's own budget: at **568 × 320 landscape** the paragraph gets ~394 px → ~3 lines → **~68 px against the stated ≤ 15 % of a 380 px viewport (~57 px)**. Fix, both halves: (a) give the paragraph a real basis so the line can break as intended — `min-w-0 flex-1 basis-64 sm:basis-0` — pushing the controls onto their own row instead of starving the text; (b) shorten the phone copy by wrapping `state.detail` in `<span className="hidden sm:inline">`, keeping title + date below `sm:`, since the date is the fact the strip exists to carry.
- **Challenge note:** Severity kept at Major, **figures corrected**. The original said "~90 px / ~13 lines / 46 % / 27 % of a 320 px-tall viewport", assuming the 44 px dismiss control was present on the expired state. It is not — `bannerState` returns `dismissible: false` there (line 135) and AC-3.3 requires its absence — so the paragraph gets ~146 px, not ~90 px. The defect and the landscape-budget breach are both real; the magnitude is smaller than reported.

### Finding 7
- **Severity:** Major
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Services/SubscriptionLedger.cs`
- **Line:** 100
- **Anchor:** `SubscriptionLedger.FoldWithSpans` (`DurationMonths` branch)
- **Comment:** **A month-duration grant delivers one day less than the spec, the repo's own documentation, and the test's own comment all state.** `cursor = start.AddMonths(months)` clamps and `endsOn = cursor.AddDays(-1)` then lands a day short whenever the anchor's day-of-month exceeds the target month's length. Confirmed against **three** independent authorities, all of which agree with each other and disagree with the code: (1) `spec.md` FR-2 line 223 — *"A duration is a whole number of months, clamped to the last day of the target month — **31 January + 1 month ends 28 (or 29) February**, never spilling into March"*; (2) EC-3 line 575 repeats it; (3) `api/ClinicManagement.Domain/CLAUDE.md` states *"`AddMonths` still clamps 31 Jan + 1 month → 28/29 Feb"*. The code yields **27 February** (and **28 February** in a leap year). ⚠️ **The test entrenches the divergence rather than catching it**: `SubscriptionLedgerTests.Month_Durations_Clamp_To_The_End_Of_A_Shorter_Month` (line 189) carries the comment *"AddMonths clamps: 31 Jan + 1 month is 28 Feb (29 in a leap year)"* directly above `[InlineData("2026-01-31", 1, "2026-02-27")]` and `[InlineData("2028-01-31", 1, "2028-02-28")]` — **both expectations are one day less than the comment above them**, so a green run reads as verification. The loss appears only on the 29th–31st of a month, unpredictably, in the vendor's favour. Fix: whichever way this is resolved, the code, the two spec clauses, the Domain `CLAUDE.md` bullet, and the test's comment **and** its expectations must be made to agree.

### Finding 8
- **Severity:** Major
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Middleware/SubscriptionGateMiddleware.cs`
- **Line:** 102
- **Anchor:** `SubscriptionGateMiddleware.Applies`
- **Comment:** **An unroutable `/api` path answers 402 instead of 404.** Verified at line 102: `context.GetEndpoint()?.Metadata.GetMetadata<AllowsWithoutSubscriptionAttribute>() is null` conflates "this endpoint declared no exemption" with "**no endpoint matched at all**" — the `?.` yields `null` for an unroutable path, which `is null` reads as "not exempt", so `Applies` returns true. On an expired cabinet a non-GET request to any unknown `/api/...` route therefore answers **402 `subscription_required`**, and `throwIfNotOk` fires `onSubscriptionRequired` on the strength of it. A refusal naming the subscription is the loudest thing this gate can say and must not be the answer to a mistyped URL or to an old client calling a removed endpoint. Fix: short-circuit when there is no endpoint — `context.GetEndpoint() is { } endpoint && endpoint.Metadata.GetMetadata<AllowsWithoutSubscriptionAttribute>() is null` — so an unmatched request falls through to routing's own 404.

### Finding 9
- **Severity:** Major
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Entities/ClinicSubscription.cs`
- **Line:** 129
- **Anchor:** `ClinicSubscription.Suspend`
- **Comment:** **The method mutates before it finishes validating, and hides a `throw` inside an assignment expression.** Verified at lines 129–134: `IsSuspended = true;` runs first, then `SuspensionReason = reason.Trim().Length > MaxSuspensionReasonLength ? throw new ArgumentException(...) : reason.Trim();`. On the over-length path the aggregate is left **suspended with a null reason** — the exact state the mandatory-reason rule exists to prevent — and `reason.Trim()` is computed twice. Nothing persists today (the vendor verb returns a failure and disposes its scope), but any future caller in a scope that saves afterwards would suspend a paying practice with no reason recorded, and the audit summary lists `IsSuspended` by name only, not its value. Every sibling (`SubscriptionPeriod.Create`, `SubscriptionPeriod.Cancel`) validates fully with plain `if`/`throw` before touching a field. Fix:
  ```csharp
  var trimmed = reason.Trim();
  if (trimmed.Length > MaxSuspensionReasonLength)
      throw new ArgumentException($"Le motif de suspension dépasse {MaxSuspensionReasonLength} caractères.", nameof(reason));
  IsSuspended = true;
  SuspensionReason = trimmed;
  ```
  Same method, line 134: `SuspendedBy = by?.Trim();` is never length-checked although `MaxActorLength = 200` is declared on this class (line 29) and enforced by `ClinicSubscriptionConfiguration`, so an over-long actor becomes a `DbUpdateException`/500 where `SubscriptionPeriod`'s `Trimmed` helper (used at lines 139–143) gives a French `ArgumentException`. Extract `Trimmed` somewhere shared so both entities refuse identically.

### Finding 10
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `api/ClinicManagement.API/Controllers/MedicalDocumentsController.cs`
- **Line:** 291
- **Anchor:** `MedicalDocumentsController.GeneratePdfForDownload`
- **Comment:** **The exemption's stated reason is not what the endpoint does.** The attribute reads *"AC-4.3, AC-4.9 — renders a document the cabinet already holds for immediate download"*, but the action takes an arbitrary `MedicalDocumentPdfData` **body** and renders it: no document id, no lookup, no ownership check (the server does correctly re-resolve the cachet from a tenant-checked `IssuingDoctorId` and clears four client-supplied identity fields, lines 309–317). So an unsubscribed cabinet can keep producing brand-new ordonnances, certificats, arrêts de travail and CNAM BS1 bulletins — cachet-stamped, authored entirely after expiry. Fix: **reconcile the reason with AC-4.3's qualifier**, by requiring the body to reference a stored document id the handler loads and tenant-checks (rendering from the persisted `ContentJson`), or by narrowing the attribute's stated reason to what it actually grants.
- **Challenge note:** Severity lowered Major → Minor, and the fix reframed. **`spec.md` AC-4.9 explicitly exempts this endpoint**: *"Requests that look like writes but only compute or preview — a CNAM reimbursement estimate, a CSV import dry run, **rendering a document for download** — still succeed."* So the exemption was mandated, not overlooked, and ⚠️ **`/apply-review-fixes` must NOT simply remove the attribute** — that would break AC-4.9. Impact is also lower than reported: the endpoint **persists nothing**, so the cabinet cannot *record* the work and the commercial pressure the feature exists for is largely intact. What remains is a genuine tension the spec itself created between AC-4.3's "a document the cabinet already holds" and AC-4.9's blanket exemption; resolving it may need a spec amendment rather than a code change.

### Finding 11
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 57
- **Anchor:** `SubscriptionBanner`
- **Comment:** **The banner has no `print:hidden`, against the repo's explicit convention.** Verified: the `cn()` at lines 59–62 carries no print class, and the banner is the topmost element of `AppShell`'s content column (`app-shell.tsx:102`). `.claude/rules/frontend-web.md` § 15 states the rule directly: *"Hide a new piece of chrome by putting `print:hidden` **on the element** — the block in `globals.css` owns only what a class cannot reach"* — and the rail, drawer, header, `BottomNav` and AI launcher/panel each carry it. Fix: add `print:hidden` to the `cn()`, beside `border-b`.
- **Challenge note:** Severity lowered Major → Minor. The original claimed the notice lands "on documents handed to a patient or an employer", but the two paths that produce those bypass the page entirely: `document-editor-content.tsx` prints **through the preview iframe's PDF** (per `components/CLAUDE.md`), and invoice/devis PDFs are **server-rendered by QuestPDF**. So the banner only reaches a browser `Ctrl+P` of an app screen. Still a real convention violation, and a one-line fix.

### Finding 12
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/lib/subscription/subscription-context.tsx`
- **Line:** 142
- **Anchor:** `SubscriptionProvider` → `refresh` / Trigger 3
- **Comment:** **A failed capability probe cannot be recovered by the 402 trigger, so the comment beside it is false.** Verified: `refresh` early-returns on `!enforced` (line 143); `enforced` becomes true only via the one-shot probe at lines 126–140, whose failure is swallowed (`.catch(() => undefined)`) with deps `[signedIn]`; and the 402 listener (line 196) merely calls `refresh()`. The comment at lines 194–195 claims *"`refresh` no-ops until `enforced` catches up a moment later"* — true only when the probe succeeded. If `GET /api/auth/mode` fails once, no banner appears, `DashboardSidebar` hides the « Abonnement » row, and every 402 re-read is dropped for the session, **even though a 402 carrying a subscription code is positive proof the deployment enforces**. This specifically defeats EC-1 (midnight passing mid-consultation, the banner appearing with no reload). Fix: make the 402 listener authoritative — `onSubscriptionRequired(() => { setEnforced(true); refresh() })` — and gate `refresh` on `signedIn` rather than `enforced`; the 404 branch at line 154 already provides the fail-safe that `enforced` was standing in for.
- **Challenge note:** Severity lowered Major → Minor. The initial fail-safe direction is **deliberate and documented** (lines 134–136: *"A failed probe leaves the feature off rather than guessing it on"*), so the defect is narrowly that the 402 path cannot override it. Impact is also bounded: a refused save still surfaces the **server's own French sentence naming the date** through `showErrorToast` (that path is independent of this provider), Part E's bell rows are unaffected, and a page reload re-runs the probe.

### Finding 13
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/app/abonnement/page.tsx`
- **Line:** 62
- **Anchor:** `AbonnementPage` → `load`
- **Comment:** **The screen re-implements state the provider already owns, and the context's `refresh` has no callers.** Verified: `load` fires its own `authApi.getMode()` **and** `subscriptionApi.get()` on mount (two duplicate requests per visit, since the provider's Trigger 0 has already read both) and keeps a private `availability`/`subscription` pair that can disagree with the provider's `enforced`/`subscription`. `refresh` — documented at `subscription-context.tsx:26` as *"« Abonnement » can too after a state-changing action"* — has **zero callers in the repo**, and after a successful « Réessayer » here nothing propagates the fresh read to the banner or the rail row. Fix: `const { subscription, enforced, refresh } = useSubscription()`, derive `unavailable` from `enforced`, and use `refresh` for the retry; keep a local `error` only for the failure the provider swallows, or expose a `lastError` on the context.
- **Challenge note:** Severity lowered Major → Minor. The stale banner **self-corrects** — the provider's focus/`visibilitychange` trigger (lines 180–192) fires on any tab return and the interval re-reads every 5 minutes while a warning is in force — so the divergence window is short rather than persistent. The duplication and the dead `refresh` remain real (this is the repo's `fixes-dont-propagate` shape: a helper wired to no call site).

### Finding 14
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed (adjusted — was Major)
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 129
- **Anchor:** `bannerState` (expired branch)
- **Comment:** **The end date renders one day early on any client west of UTC.** `EndsOn` is an inclusive **calendar day** but is stored as `timestamp with time zone` at UTC midnight (per DEV-2 and the verified backfill value `2026-08-10 00:00:00+00`) and rendered through `formatDate` (`web/lib/format.ts:154`), which is `format(parseISO(iso), "dd/MM/yyyy")` — i.e. **the workstation's timezone**. At UTC−4 that renders the previous day, so the banner, the page subtitle (`page.tsx:209`), the state card (`:256`) and the history's `fromDay`/`throughDay` all shift back one day and disagree with the server's own 402 sentence, which formats the same value as `dd/MM/yyyy` at UTC. This is the class `todayLocalIso()` exists to prevent and that `ChequeDueDate` is sent as a bare `YYYY-MM-DD` to avoid. Fix: send `endsOn`/`fromDay`/`throughDay` as bare `YYYY-MM-DD` as `ChequeDueDate` already does, or format from the ISO string's date part rather than through a `Date`.
- **Challenge note:** Severity lowered Major → Minor. **Tunisia is UTC+1**, so for this product's actual users the value renders **correctly** (UTC midnight → 01:00 the same day); the defect manifests only west of UTC, which for a Tunisia-targeted deployment means a travelling mobile shell. Worth fixing as the third instance of a defect class this repo has already fixed twice, but not a live defect for the target population. ⚠️ One residual uncertainty: the shift depends on `DateTime.Kind` surviving as `Utc` through serialization (a `Kind`-less string would parse as local and render correctly everywhere) — worth a runtime check before the fix.

### Finding 15
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/Queries/GetSubscriptionQuery.cs`
- **Line:** 122
- **Anchor:** `GetSubscriptionQueryHandler.IsOnTrial`
- **Comment:** **A cabinet that has already paid still reads « Essai gratuit ».** Verified at lines 117–129: the covering span is `spans.LastOrDefault(s => from <= day && (ThroughDay is null || day <= ThroughDay))`, and `isTrial` is true when that entry's `Kind == Trial`. FR-1 defines `Essai` as *"only trial entries so far, still valid"* and `Actif` as *"a paid or open-ended entitlement, still valid"*. Concrete input, and it is EC-3's own scenario (paying early is expected): a cabinet on day 5 of its 30-day trial pays for 12 months. The fold gives the trial days 1–30 and the paid entry a span starting day 31, so today is covered only by the trial — the screen shows `Trial` / « Essai gratuit » beside an `endsOn` twelve months away, for the next 25 days, to a customer who has paid. Fix: make it false as soon as a non-cancelled non-`Trial` entry exists, or have the covering test prefer the newest non-`Trial` entry.

### Finding 16
- **Severity:** Minor
- **Category:** Business Logic
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/BackgroundJobs/SubscriptionWarningJob.cs`
- **Line:** 126
- **Anchor:** `SubscriptionWarningJob.ReviewClinicAsync`
- **Comment:** **Warning rows naming a superseded date are never withdrawn when `EndsOn` moves *within* the window.** Verified at lines 125–135: rows are cleared only when `threshold is null || status.EndsOn is null` (date beyond the window, or absent); otherwise `EnsureSubscriptionWarningAsync(clinicId, threshold.Value, status.EndsOn.Value)` writes/updates the row **for that threshold only**, since the dedupe key is (clinic, threshold). Concrete input: today 11/09, `EndsOn` 12/09 → a « 1 jour restant … se termine le 12/09/2026 » row exists. The vendor grants 5 days; `EndsOn` becomes 17/09, `daysRemaining` 6, `ThresholdReached(6)` → 7 → a new « 7 jours restants … 17/09 » row is written and the « 1 jour … 12/09 » row is left in place with its read-tracking intact, so the bell shows two rows asserting two different dates. The mirror case happens after `subscription-cancel` moves the date closer. Fix: clear (or correct) rows whose stored end date no longer matches whenever `EndsOn` has changed, not only when the countdown leaves the window.

### Finding 17
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/Commands/GrantSubscriptionPeriodCommand.cs`
- **Line:** 155
- **Anchor:** `GrantSubscriptionPeriodCommandHandler.Handle` — same at `CancelSubscriptionPeriodCommand.cs:134` and `SetSubscriptionSuspensionCommand.cs:114`
- **Comment:** The three vendor handlers catch `ArgumentException` and surface `ex.Message` verbatim, but not `InvalidOperationException` — and the domain throws its French guards through **both**. Verified: `ClinicSubscription.RecomputeFrom` throws `InvalidOperationException("Le journal d'abonnement fourni contient une période appartenant à un autre cabinet.")` (line 96) and `SubscriptionPeriod.Cancel` throws one for an already-cancelled entry; both are reachable inside these `try` blocks and land in the catch-all, replacing the operator's only diagnosis with a generic « Erreur lors de l'enregistrement… » and exit 1. Conversely the `catch (ArgumentException)` also captures `ArgumentNullException`/`ArgumentOutOfRangeException`, printing English framework text including `(Parameter 'entries')` to a vendor console as though it were a validation refusal. Fix: catch `InvalidOperationException` alongside `ArgumentException` for the domain's intentional French guards, and narrow the argument catch so a genuine programming fault falls through to the logged catch-all.

### Finding 18
- **Severity:** Minor
- **Category:** Error Handling
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/SubscriptionCabinetLookup.cs`
- **Line:** 44
- **Anchor:** `SubscriptionCabinetLookup.ResolveAsync`
- **Comment:** **The e-mail branch does not verify the resolved clinic exists, while the id branch does.** Verified: the id branch runs `clinics.ExistsAsync(id)` and refuses with a sentence naming the value; the e-mail branch returns `Result<Guid>.Success(user.ClinicId)` with no such check. `User.ClinicId` is a non-nullable `Guid` with no FK guarantee in play here, so an account attached to no cabinet resolves to `Guid.Empty` and the verb proceeds: `UseClinic(Guid.Empty)`, `GetByClinicAsync` finds nothing, and the operator is told « L'abonnement de ce cabinet est introuvable. Contactez-nous, nous le rétablissons. » — our own bookkeeping blamed for an address belonging to no practice, exactly the confusion the two-distinct-sentences design (lines 16–18) exists to avoid. Fix: run the same `clinics.ExistsAsync(user.ClinicId)` check (and reject `Guid.Empty`), refusing with a third accurate sentence — « Le compte « … » n'est rattaché à aucun cabinet. »

### Finding 19
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Controllers/UsersController.cs`
- **Line:** 115
- **Anchor:** `UsersController.SetStatus`
- **Comment:** **The exemption is justified one-directionally but the endpoint is bidirectional.** The reason reads *"offboarding must not wait on an invoice; a colleague who left keeps access otherwise"*, yet `SetUserActiveCommand` carries `IsActive` and branches on it (`if (request.IsActive)`, line 81) — so it also **re-activates** accounts. An expired cabinet therefore cannot create staff accounts (`POST /api/users` is correctly gated) yet can bring any previously deactivated account back online: the same effect by another route, and exactly the kind of write the gate exists to refuse. Fix: scope the exemption to the deactivation direction — split the action, or refuse `IsActive == true` in the handler when `SubscriptionStateReader` says writes are not allowed — so the exempt surface matches the reason recorded on it.

### Finding 20
- **Severity:** Minor
- **Category:** Security
- **Verdict:** Confirmed (adjusted — scope narrowed)
- **File:** `api/ClinicManagement.Application/DTOs/SubscriptionPeriodDto.cs`
- **Line:** 44
- **Anchor:** `SubscriptionPeriodDto.Note` (and `RecordedBy`, line 49)
- **Comment:** **Two vendor-internal fields are returned to the cabinet and rendered by nothing.** Verified: `Note` (line 44) and `RecordedBy` (line 49) are on the wire from `GET /api/subscription/history`, and `subscription-history-table.tsx` renders only `recordedAt`, `kindLabel`, `coveredPeriod`, `amount`, `methodLabel`, `reference` and a `StateBadge` — **neither field appears in either tree**. So this is over-exposure with no product benefit: `--note` is documented as the vendor's own free text for *"a goodwill extension, a pilot, an apology"*, i.e. commercial annotations about the customer readable by that customer's admin in devtools, and `RecordedBy` publishes our internal command vocabulary (`job|subscription-grant`). Fix: drop both from the history projection and keep them in the console report, where the vendor reads them.
- **Challenge note:** Scope narrowed and severity kept at Minor. The original also flagged `SubscriptionDto.SuspensionReason`; that half is **dismissed** — showing the suspension motif to the cabinet is intentional, pinned by Part C's `A_Suspended_Cabinet_Reads_Suspendu_And_Carries_Its_Motif`, and EC-11's own reasoning requires the practice to be told why. If the operator's internal wording and the customer-facing message should differ, that is a spec question, not a defect. `Note`/`RecordedBy` have no such mandate and no renderer.

### Finding 21
- **Severity:** Minor
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Infrastructure/Migrations/20260810175512_AddClinicSubscriptions.cs`
- **Line:** 150
- **Anchor:** `AddClinicSubscriptions.Up` (grandfathering backfill)
- **Comment:** **The re-runnability claim does not hold for a partial-failure ordering.** The class doc says both inserts are gated so `Up()` is *"re-runnable"*, but the two statements share one predicate (`NOT EXISTS … "ClinicSubscriptions"`) and the **ledger entry is inserted first**. If the pair is ever executed with the entitlement insert not committed — a hand-run of the SQL during recovery, a future edit that splits the statements, or any `suppressTransaction: true` — a second run still sees "no entitlement row" and inserts a **duplicate `Grandfathered` entry per cabinet**. `EndsOn` stays correct (two open-ended entries still fold to NULL), so nothing goes red; the damage is silent: `subscription-grandfathered-entries` doubles, AC-6.4's prescribed before/after diff becomes unreadable, and « Antériorité » appears twice on the history screen. Fix: gate the entry insert on `NOT EXISTS (… "SubscriptionPeriods" WHERE "ClinicId" = c."Id" AND "Kind" = 3)` as well, or insert the entitlement first and gate the entry on its presence.

### Finding 22
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Entities/ClinicSubscription.cs`
- **Line:** 101
- **Anchor:** `ClinicSubscription.RecomputeFrom`
- **Comment:** `RecomputeFrom` reads the clock directly (`UpdatedAt = DateTime.UtcNow;`) while every other mutator on this class takes the instant as a parameter — `SetPlan(plan, whenUtc)` (line 108), `Suspend(reason, by, whenUtc)` (line 122), `Unsuspend(whenUtc)` (line 143) — and `SubscriptionLedger`/`SubscriptionStateReader` are explicitly clock-free for testability. This is the one place in the entitlement aggregate a test cannot control the timestamp. Fix: give it the same shape — `RecomputeFrom(IEnumerable<SubscriptionPeriod> wholeLedger, DateTime whenUtc)` — and pass `now` from the two callers that already hold one (`SubscriptionProvisioning.CreateForNewClinic` has `recordedAtUtc`; `SubscriptionRefold.SaveAsync` calls `DateTime.UtcNow` one line above for `SetPlan`).

### Finding 23
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionCancelCommand.cs`
- **Line:** 96
- **Anchor:** `SubscriptionCancelCommand.RunAsync`
- **Comment:** `endsOn.Date < DateTime.UtcNow.Date` compares an **inclusive clinic-local day** against the UTC day. `ClinicClock` is documented as the only thing in this solution that knows Tunisia is UTC+1, and the precedent claim checks out verbatim — the sibling verb in the same folder does it right (`SubscriptionReportCommand.cs:64`, `var today = ClinicClock.ClinicToday();`). For the first hour of every Tunisian day this prints or omits the "date is in the past" warning against yesterday. Fix: `endsOn.Date < ClinicClock.ClinicToday().Date`.

### Finding 24
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionUnsuspendCommand.cs`
- **Line:** 81
- **Anchor:** `SubscriptionUnsuspendCommand.RunAsync`
- **Comment:** The same defect as Finding 23, verified as a second copy: `lifted.EndsOn is { } endsOn && endsOn.Date < DateTime.UtcNow.Date` measures a clinic-local inclusive end day against the UTC day. Two copies of one wrong comparison in a single feature; fix both, and prefer a single `SubscriptionVerbs.IsInThePast(DateTime? endsOn)` helper so the third verb that needs it cannot get a third answer.

### Finding 25
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionVerbs.cs`
- **Line:** 91
- **Anchor:** `SubscriptionVerbs.DeclareActor`
- **Comment:** `return $"job|{commandName}";` re-implements a convention that already has a named authority, and the precedent claim is verbatim accurate: `AuditActor.ProcessPrefix = "job|"` (`IAuditActorProvider.cs:39`) plus `AuditActor.Process(processName)` (line 48), which also trims and substitutes `unknown` for a blank name. A hardcoded prefix means the actor string these five verbs stamp onto `RecordedBy`/`CancelledBy`/`SuspendedBy` can silently diverge from the one the audit interceptor writes for the same run. Fix: `return AuditActor.Process(commandName).UserId;` (or at minimum `$"{AuditActor.ProcessPrefix}{commandName}"`).

### Finding 26
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionReportCommand.cs`
- **Line:** 106
- **Anchor:** `SubscriptionReportCommand.NeedsAttention`
- **Comment:** **Two implementations of "which cabinets the vendor must act on", over two different shapes.** Verified: `SubscriptionReportCommand.NeedsAttention(line, withinDays)` (line 106, private, in API, over `AllowsWrites` + `State` + `DaysRemaining`) beside `SubscriptionReport.NeedsAttention` (`SubscriptionReportService.cs:45`, a property over bucket counts `Expiring.Count > 0 || Expired.Count > 0 || WithoutEntitlement.Count > 0`). The doc at line 105 admits the relationship — *"The single-cabinet mirror of SubscriptionReport.NeedsAttention — same three groups"*. Only one is unit-testable (the API verb is outside the test project's reach; `SubscriptionReportServiceTests` covers the property in five assertions). They agree today only by coincidence of how `SubscriptionStateReader` fills those fields; change the bucketing rule and `subscription-report --clinic <id>` keeps exiting 0 for a cabinet the deployment-wide run flags at exit 2 — an exit code that silently stops alarming is indistinguishable from a clean run. Fix: have `RunForCabinetAsync` return the cabinet's bucket (or a flag the service computes from the one predicate) and let the verb read it. This also removes the `Domain.Enums.SubscriptionState` reach-through from a presentation file.

### Finding 27
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Common/Maintenance/SubscriptionReportService.cs`
- **Line:** 140
- **Anchor:** `SubscriptionReportService.RunForCabinetAsync`
- **Comment:** Verified at lines 140–141: `(await _subscriptions.GetForReportAsync(cancellationToken)).FirstOrDefault(r => r.ClinicId == clinicId)` materialises **every cabinet of the deployment plus every entitlement row** to select one. `IClinicSubscriptionRepository` already exposes `GetByClinicAsync(clinicId)`, and the only other thing needed is the clinic's name. Fix: add a targeted `GetReportRowAsync(Guid clinicId)`, or give `GetForReportAsync` an optional clinic filter — as written, the cost of `subscription-report --clinic <id>` grows with the whole tenant list rather than with the one cabinet asked about.

### Finding 28
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Common/Maintenance/SubscriptionReportService.cs`
- **Line:** 183
- **Anchor:** `SubscriptionReportService.Describe`
- **Comment:** Verified at line 183: `"Aucun abonnement"` is a fifth state label inlined here while the other four live in `SubscriptionLabels.State(...)`, the class whose stated purpose is *"the French name of every value « Abonnement » renders"*. The vendor report, the console output, the DTO and the client all have to agree on how a cabinet with no entitlement reads, and one of the five is outside the authority. Fix: add `public const string NoSubscription = "Aucun abonnement";` to `SubscriptionLabels` and reference it here (and in `SubscriptionReportServiceTests`, which repeats the literal).

### Finding 29
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Common/Maintenance/SchemaVerificationService.cs`
- **Line:** 592
- **Anchor:** `SchemaVerificationService.NotApplicable`
- **Comment:** Verified: `NotApplicableIn(scope, check, why)` (line 584) and `NotApplicable(check, why)` (line 592) build the same finding, and the doc at 581–582 names the relationship (*"Its sibling hardcodes that scope"*) without collapsing them. There is in fact a **third** inline construction at line 266, whose comment says *"Built inline rather than through `NotApplicable`, which hardcodes the « Data migrations » section"* — so the message text exists three times. Fix: make the older one delegate — `private static SchemaVerificationFinding NotApplicable(string check, string why) => NotApplicableIn("Data migrations", check, why);` — and route line 266 through `NotApplicableIn` too.

### Finding 30
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/Queries/GetSubscriptionHistoryQuery.cs`
- **Line:** 76
- **Anchor:** `GetSubscriptionHistoryQueryHandler.Handle`
- **Comment:** Verified at lines 76–77: two span fields depend on an out-variable declared inside a *previous* object-initializer element —
  ```csharp
  FromDay = spanById.TryGetValue(e.Id, out var span) ? span.FromDay : null,
  ThroughDay = span?.ThroughDay,
  ```
  It compiles and is correct only because initializer elements evaluate top-to-bottom; reordering the two lines or inserting a field between them silently breaks or fails to compile, and `span` is dereferenced on the next line after being proven possibly-null on this one. Fix: resolve the lookup before the projection — `.Select(e => { var span = spanById.GetValueOrDefault(e.Id); return new SubscriptionPeriodDto { … }; })`. `SubscriptionReportService.RunForCabinetAsync` already uses that clearer shape over the same dictionary.

### Finding 31
- **Severity:** Minor
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Domain/Entities/SubscriptionPeriod.cs`
- **Line:** 111
- **Anchor:** `SubscriptionPeriod.Create`
- **Comment:** Verified at lines 102–112: the mutually-exclusive-duration guard always reports `nameof(durationMonths)` as the offending parameter, even when the conflict is `durationDays` + `explicitEndsOn` and `durationMonths` was never supplied. `ArgumentException.ParamName` is what a caller — and the `catch (ArgumentException)` in `GrantSubscriptionPeriodCommandHandler` — has to reason about. Fix: name the parameter actually supplied second (`durationDays.HasValue ? nameof(durationDays) : nameof(explicitEndsOn)`), or drop `paramName` since the message already enumerates all three forms.

### Finding 32
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 58
- **Anchor:** `SubscriptionBanner`
- **Comment:** **`role="status"` on persistent chrome re-announces the whole strip on every navigation.** Verified, and the precedent claim is verbatim accurate: `app-shell.tsx:107` states *"The shell remounts on every route change, so this runs once per navigation"* (which is why `animate-page-in` works), and the banner is mounted inside it at line 102. So a screen-reader user hears the full expiry sentence plus « Renouveler » and the dismiss button, **on every navigation**, dozens of times a day. `role="status"` is for an inline async *result* (how `EmptyState` and `LoadFailureNotice` use it); this is a standing statement, and Part E's `StaffNotification` already announces the change once. Fix: drop `role="status"` from the strip — the visible icon plus the French word carry it in greyscale, as the component's own doc argues — or move the live region to a text-only child not re-created per route.

### Finding 33
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 71
- **Anchor:** `SubscriptionBanner`
- **Comment:** **The CTA's verb contradicts the sentence beside it on two of three states.** Verified: line 71 renders a hardcoded « Renouveler » whenever `!onSubscriptionScreen`, for all three states — including `Suspended`, whose own detail two lines up (line 119) says *"Contactez-nous pour le rétablir"*, i.e. that paying will **not** lift it, and including the missing-entitlement case the code documents as "our fault, not a lapse on theirs". On a phone the strip is the only feedback channel, and a control whose verb contradicts the sentence beside it sends a practice to pay for something that will not unblock them. Fix: make the label part of `BannerState` (`cta: "Renouveler" | "Voir les détails"`) and give the suspended/missing states a neutral one.

### Finding 34
- **Severity:** Minor
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/app/abonnement/page.tsx`
- **Line:** 191
- **Anchor:** `AbonnementPage` (non-admin history branch)
- **Comment:** **`AccessDeniedCard` is used as a section-level refusal, outside its own documented contract.** Verified: its root is `flex min-h-full items-center justify-center p-6` with the comment *"`min-h-full` centring resolves against `<main>`, so the caller passes `width="none"` on its `AppShell`"* (`access-denied-card.tsx:40–41`) — but this page renders `AppShell contentClassName="space-y-6"` with no `width="none"`. Two consequences: at 320–640 px it inserts a centred `max-w-md` card with 24 px of its own padding under the payment instructions, on the screen a secretary was sent to *by a refused save*; and its only control is `backHref` defaulting to `/appointments` with label « Retour à l'agenda » (lines 28–29), which navigates her **off** the page she is allowed to read and away from the bank details she came for. Fix: use `EmptyState icon={Lock} size="compact"` — as `subscription-history-table.tsx` already does for « Aucun paiement enregistré » — or a plain `Card` with the same sentence and **no** back action; the refusal withholds one section, so it must not offer an exit from the screen.

### Finding 35
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/lib/subscription/subscription-context.tsx`
- **Line:** 180
- **Anchor:** `SubscriptionProvider` → Trigger 2 (focus / `visibilitychange`)
- **Comment:** **The focus trigger is unthrottled and unscoped, so the doc's polling claim is false.** Verified at lines 180–192: the effect is gated only on `enforced && signedIn` — **not** on `warningInForce`, unlike the interval at line 173 — so every alt-tab and every `visibilitychange` on every enforced cabinet issues a `GET /api/subscription`. That contradicts `REREAD_INTERVAL_MS`'s own doc at lines 50–51: *"Nothing polls outside that window: FR-15 bounds this per client, and a cabinet three months from its date has nothing to learn."* `inFlight` (line 108) only collapses *concurrent* reads, so it also does not bound a 402 burst spread over a second or repeated tab switching. Fix: add a `lastReadAtMs` ref and return early in `refresh` when `Date.now() - lastReadAtMs.current < 60_000` unless a `force` flag is passed (the 402 path can pass it) — one guard fixes the poll rate and the burst coalescing together.

### Finding 36
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/app/abonnement/page.tsx`
- **Line:** 97
- **Anchor:** `AbonnementPage` → `loadHistory`
- **Comment:** Verified: neither `load` (line 62) nor `loadHistory` (line 97) guards its `setState` calls against unmount, and neither has a sequence guard. `loadHistory` is the one that can be visibly wrong: it is re-created on every `page`/`pageSize` change (deps at line 106), so clicking through pages quickly lets an older response land last and render page 2's rows under « 76–100 sur 120 ». Fix: mirror `patients-table.tsx`'s existing out-of-order guard — a `requestIdRef` incremented on entry, result ignored if no longer current — plus a `cancelled` flag in the effect cleanup.

### Finding 37
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/app/abonnement/page.tsx`
- **Line:** 138
- **Anchor:** `AbonnementPage` → render (`LoadFailureNotice onRetry`)
- **Comment:** Verified: `load` never clears `error` before retrying (it sets it only in the catch, and clears it on the success paths), and the render ternary tests `error` **before** `loading` — so pressing « Réessayer » leaves the failure notice on screen with no in-flight feedback and no disabled control, and a second click starts a second concurrent `load` (there is no in-flight guard here, unlike the provider's `inFlight`). Fix: `setError(null)` beside `setLoading(true)` at line 63 (and `setHistoryError(null)` beside line 98) so the retry falls through to the skeleton branch, and guard re-entry.

### Finding 38
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/lib/subscription/subscription-context.tsx`
- **Line:** 211
- **Anchor:** `SubscriptionProvider` → `SubscriptionContext.Provider value`
- **Comment:** Verified at line 211: the context value is an inline object literal, so every provider render hands consumers a new identity and re-renders `SubscriptionBanner` and the whole `DashboardSidebar` (which rebuilds `buildNavSections` on each render) even when nothing changed — including on each 5-minute poll, since `setSubscription` stores a fresh object whose contents are usually identical. `dismiss` and `refresh` are already `useCallback`-wrapped, so only the container is missing. `CloudBridge` in `lib/auth/session.tsx:80` memoizes for exactly this stated reason. Fix: `useMemo(() => ({ subscription, enforced, dismissed, dismiss, refresh }), [subscription, enforced, dismissed, dismiss, refresh])`.

### Finding 39
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/lib/api/subscription.ts`
- **Line:** 99
- **Anchor:** `subscriptionApi.history`
- **Comment:** **Deviates from the repo's paged-read convention twice**, and both precedent claims check out verbatim: `recalls.ts:11` is `listPaged: async (params: PageParams): Promise<PagedResponse<RecallDto>> => apiGet(...)` and `users.ts:77` is the same shape. This one takes positional `page`/`pageSize` and hand-builds the query string (`?page=${page}&pageSize=${pageSize}`), so it cannot pick up `PageParams`' `search` without another literal, and it re-declares the default page size as a bare `25` instead of importing `DEFAULT_PAGE_SIZE` from `./paging` — a second authority on a number the page itself already reads from that module (`page.tsx:60`). Fix: `history: (params: PageParams) => apiGet<SubscriptionHistoryPageDto>('/subscription/history', params)`.

### Finding 40
- **Severity:** Minor
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/lib/errors.ts`
- **Line:** 46
- **Anchor:** `isPaymentRequiredError`
- **Comment:** **Exported with zero callers.** Verified: `grep -rn isPaymentRequiredError web/` matches only this declaration and four `.next` build-cache binaries — no source consumer anywhere. `web/` has no working ESLint (`eslint` is in the `lint` script but not in `devDependencies`, and `next.config.ts` sets `eslint.ignoreDuringBuilds`), so an unused export is invisible to the gate; its siblings `isConflictError`, `isForbiddenError` and `isNetworkError` all have real call sites, and `lib/CLAUDE.md` documents this one as though it were consumed. Fix: wire it where a caller genuinely needs to tell a subscription refusal apart (nothing in this diff does — AC-4.6 deliberately shows the server's sentence verbatim through the ordinary `showErrorToast` path), or drop it and add it back with its first consumer.

### Finding 41
- **Severity:** Suggestion
- **Category:** Error Handling
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `api/ClinicManagement.Application/Common/Services/NotificationGenerator.cs`
- **Line:** 311
- **Anchor:** `NotificationGenerator.EnsureSubscriptionWarningAsync`
- **Comment:** `if (string.Equals(existing.Message, message, StringComparison.Ordinal)) return false;` decides whether the end date has moved by **comparing two French sentences**, when the fact being tested is available as data on both sides. Reword `SubscriptionWarningMessage` in a future release and the next daily pass `Restate`s every outstanding warning row on every cabinet, broadcasting `"notifications"` and making every open browser refetch — the churn the dedupe column was introduced to prevent. Fix (robustness): compare a stored fact — `StaffNotification` already carries `SubscriptionThresholdDays`; add the end date so the restate condition is `existing.EndsOn != endsOn`, and let the message be derived rather than interrogated.
- **Challenge note:** Severity lowered Minor → Suggestion. The comparison is **deliberate and documented** at lines 308–310, with a correct justification (*"the whole message is compared rather than a prefix: it carries no countdown, only the end date, so it is stable day to day and differs exactly when a grant has moved the date"*) — so it is the intended change-detection mechanism and works today. This is hardening against a future rewording, not a live defect. It remains worth doing because this repo deleted one behaviour recovered by prose matching (`Contains("déjà facturée")`).

### Finding 42
- **Severity:** Suggestion
- **Category:** Security
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `api/ClinicManagement.Application/Features/Auth/Commands/SignUpClinicCommand.cs`
- **Line:** 120
- **Anchor:** `SignUpClinicCommandHandler.Handle`
- **Comment:** **A trial is resettable with a fresh address, using only paths the gate never touches.** Verified: the handler checks the *same* address against existing users (line 160) and pending signups (line 167), but nothing links a new signup to an existing or expired cabinet and nothing caps trials per phone or clinic name. So: export the patient list (a GET, always allowed) → `POST /api/auth/signup` with a new address (anonymous, class-level `[AllowsWithoutSubscription]`, full new trial) → CSV-import into the new cabinet, which is inside its trial and accepts writes. The only bound is the per-account/per-address auth limiter, which a new address defeats by construction. Fix (or defer explicitly): flag a signup whose clinic name, phone or admin address collides with an existing cabinet, cap trials per address/phone, and add a "repeat trial" group to `subscription-report`.
- **Challenge note:** Severity lowered Minor → Suggestion. **`spec.md`'s Out of Scope excludes this explicitly**: *"Changes to public self-signup itself, beyond stating the trial (AC-1.3)."* It is also self-limiting — the practice loses every appointment, invoice and document, with no merge or migration path — so it is costly rather than free. ⚠️ `/apply-review-fixes` should **not** add signup caps without a spec amendment; the actionable half here is the reporting gap (`subscription-report` has no notion of a repeat signup), which is inside this feature's own scope.

### Finding 43
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `web/components/subscription/subscription-history-table.tsx`
- **Line:** 70
- **Anchor:** `SubscriptionHistoryTable`
- **Comment:** **Empty and not-yet-loaded are conflated on the first committed render.** Verified: `AbonnementPage` initialises `historyLoading = false` and `history = null` (lines 57–58), and `loadHistory` only runs in the effect *after* the render in which `availability` becomes `"available"` — so that render commits with `loading === false`, `data === null`, `rows.length === 0`, taking the `EmptyState` branch and asserting « Aucun paiement enregistré » before the skeleton replaces it. Fix in the page: start `historyLoading` at `true`, or gate the section on `history === null && !historyError` → skeleton, so the order is skeleton → data/empty and never empty → skeleton → data.
- **Challenge note:** Severity lowered Minor → Suggestion. The flash is **one render commit** (`setHistoryLoading(true)` runs synchronously as `loadHistory`'s first statement), so it is a sub-frame flicker rather than a visible state. It still technically violates `.claude/rules/frontend-web.md` § 13's "a loading skeleton distinct from empty", and the fix is a one-word change to the initial state.

### Finding 44
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `web/lib/api/client.ts`
- **Line:** 414
- **Anchor:** `throwIfNotOk` (402 dispatch) / `STATUS_FALLBACK_FR[402]`
- **Comment:** **The three pieces added together key on two different things.** The re-read dispatches on the **code** (line 414, `if (errorCode && SUBSCRIPTION_CODES.has(errorCode))`) while `STATUS_FALLBACK_FR[402]` (line 241) and `errors.ts`'s `isPaymentRequiredError` (line 47, `err.status === 402`) key on the **status**. So for the exact case lines 238–240 say the fallback exists for — a 402 whose `{ error, code }` body is missing or stripped by an intermediary — the user is shown « Rendez-vous dans « Abonnement » » while `errorCode` is `undefined`, no listener fires, and the banner that sentence implies never appears. Fix: drop the 402 entry from `STATUS_FALLBACK_FR` (or reword it so it does not promise a banner), so a bodyless 402 does not assert something the state machine did not act on.
- **Challenge note:** Severity lowered Minor → Suggestion, and the fix direction **reversed**. The original offered "dispatch on `response.status === 402`" as an equal option; the code-keyed dispatch is **deliberate and documented** at lines 411–413 (*"Keyed on the code, not on the status, so a 402 from anything but our own gate cannot trigger a re-read"*), which is the correct call — an intermediary's 402 must not trigger a re-read. So the inconsistency should be resolved on the **fallback-message** side only.

### Finding 45
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed (adjusted — was Minor)
- **File:** `web/components/subscription/subscription-banner.tsx`
- **Line:** 148
- **Anchor:** `bannerState` (warning branch)
- **Comment:** **Two guards disagree about whether an uncounted warning is dismissible.** `dismissible: true` is returned unconditionally in the warning branch, but `dismiss()` is a no-op whenever `daysRemaining === null`: `clinicDayKey` (`subscription-context.tsx:76`) returns `null` for that case and `dismiss` bails at line 205. `countdown(null)` (line 158) exists precisely because the author treats a warning with no countdown as reachable, so the two halves of the file disagree. Fix (consistency): `dismissible: subscription.daysRemaining !== null`, or key the storage value on `endsOn` plus `daysRemaining ?? "?"`.
- **Challenge note:** Severity lowered Minor → Suggestion — **the state appears unreachable**, so this is a latent inconsistency rather than a live dead control. Traced through `SubscriptionStateReader.Read`: the suspended branch sets `DaysRemaining: null` but is caught earlier by the banner's `state === "Suspended"` check (line 116); an open-ended entitlement has `endsOn === null` and returns at line 141; an elapsed date sets `allowsWrites: false` and is caught at line 126. So reaching line 148 requires `allowsWrites && shouldWarn && endsOn !== null`, where the reader always produces a `daysRemaining`. Worth aligning so a future reader change cannot open the gap silently.

### Finding 46
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionSuspendCommand.cs`
- **Line:** 36
- **Anchor:** `SubscriptionSuspendCommand.RunAsync`
- **Comment:** The same six-step scaffold appears in all four mutating verbs — `BuildForConsoleVerb` → `HasConnectionString` → `BuildProvider` → `CreateScope` → `DeclareActor` → `ResolveCabinetAsync` → `UseClinic` → construct a handler from the same `GetRequiredService` calls — and suspend/unsuspend construct the *identical* `SetSubscriptionSuspensionCommandHandler` twice. `SubscriptionVerbs` already exists as the shared home; adding e.g. `SubscriptionVerbs.RunForCabinetAsync(args, commandName, purpose, (sp, clinicId, actor) => …)` plus a `NewSuspensionHandler(sp)` factory would remove ~150 duplicated lines and mean a future fix to the scaffold lands once. ⚠️ One constraint the class doc already records: the `UseClinic`/`UseSystemWide` call must stay textually in each `*Command.cs` for `SystemWideCallerCoverageTests`' source scan.

### Finding 47
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.API/Maintenance/SubscriptionVerbs.cs`
- **Line:** 66
- **Anchor:** `SubscriptionVerbs.ResolveCabinetAsync`
- **Comment:** Verified: the five new verbs call `ProvisionClinicCommand.ReadOption(args, …)` **13 times** across five files. `ReadOption` is a general command-line argument reader with nothing to do with provisioning a clinic, and no pre-existing verb reached into `ProvisionClinicCommand` for it — this feature is what turns a private detail into a de-facto shared utility. Fix: move `ReadOption` into `SubscriptionVerbs` (or a neutral `Maintenance/ConsoleArgs`) and have `ProvisionClinicCommand` call the shared one, so a verb's parsing helper is not owned by an unrelated verb.

### Finding 48
- **Severity:** Suggestion
- **Category:** Code Quality
- **Verdict:** Confirmed
- **File:** `api/ClinicManagement.Application/Features/Subscriptions/OutboxSubscriptionGate.cs`
- **Line:** 98
- **Anchor:** `OutboxSubscriptionGate.DecideAsync`
- **Comment:** Verified: this `status switch { { State: Suspended } => …, { EndsOn: { } endsOn } => …, _ => … }` is the same three-arm classification of a refused `SubscriptionStatus` that `SubscriptionGateMiddleware.InvokeAsync` performs (`SubscriptionGateMiddleware.cs:80–89`), down to the comment about suspension outranking a date **and** the comment marking the third arm unreachable. Only the wording differs (channel-neutral vs. HTTP), and a third consumer will copy the branching a third time. Consider one classifier in Application — `SubscriptionStateReader.ClassifyRefusal(status) → SubscriptionRefusalKind { Suspended | Expired(endsOn) | Inactive }` — with each caller supplying its own sentences off that enum, so "which refusal is this?" has one answer and only the prose is per-surface.

### Finding 49
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/subscription/subscription-history-table.tsx`
- **Line:** 56
- **Anchor:** `SubscriptionHistoryTable`
- **Comment:** The loading branch hand-rolls three `animate-pulse` bars inside a `Card` beside a primitive that already does exactly this, and the precedent claim checks out: `ui/card-list.tsx` accepts `loading` (line 104/128) and renders its skeleton with `role="status"`, an `aria-label` and `aria-busy="true"` (lines 137–139) — which this copy does not, so on a phone a screen-reader user gets silence for the whole fetch. Fix: pass `loading` through to `CardList` (keeping a `TABLE_ONLY_LG` skeleton if the desktop tree needs one) rather than a second skeleton treatment that will not receive the next fix made to the shared one.

### Finding 50
- **Severity:** Suggestion
- **Category:** Device & UX
- **Verdict:** Confirmed
- **File:** `web/components/subscription/subscription-history-table.tsx`
- **Line:** 104
- **Anchor:** `SubscriptionHistoryTable`
- **Comment:** The Montant cell hand-writes `className={cn("tabular-nums", …)}` instead of `<TableCell numeric>`, and the precedent claim is verbatim: `ui/table.tsx:162–164` documents `numeric` as *"the single highest-value change in this file"* and it applies **both** `text-right` and `tabular-nums` (line 176). Left-aligned, the amounts do not line up on their decimal comma from `lg:` upward — the one width this tree exists for, and the only place several payments are compared vertically. Fix: use `numeric` and give the « Montant » `TableHead` a matching `text-right`, so this money column follows the same rule as `/factures` and la caisse.

### Finding 51
- **Severity:** Suggestion
- **Category:** Frontend
- **Verdict:** Confirmed
- **File:** `web/app/abonnement/page.tsx`
- **Line:** 433
- **Anchor:** `stateTone`
- **Comment:** Verified at lines 433–444: the switch is exhaustive over `SubscriptionDto["state"]` with no `default` and no trailing return, so `tsc` accepts it — but `apiGet` performs no shape validation, and a state added server-side ahead of this build falls off the end and returns `undefined` at runtime. `statusToneClass` happens to absorb that (defaulting to `neutral`), so nothing breaks, but that safety is accidental and the declared return type says it cannot happen. Fix: add an explicit `return "neutral"` **after** the switch rather than a `default:` case — the trailing return is unreachable per the types, so exhaustiveness checking is preserved and a new union member is still a `tsc` error, while an unknown wire value degrades honestly.

### Finding 52
- **Severity:** Suggestion
- **Category:** Breaking Change
- **Verdict:** Confirmed
- **File:** `web/lib/subscription/subscription-context.tsx`
- **Line:** 129
- **Anchor:** `SubscriptionProvider` (capability probe effect)
- **Comment:** The provider's own doc (line 97) and `app/layout.tsx`'s comment both state it *"fetches nothing until a user exists, and nothing at all where `requiresSubscription` is not `true`"*, but the capability probe itself (`authApi.getMode()`, lines 129–136) fires on **every** deployment for every signed-in user — so `SelfHostedLan` and `CloudBrowser` gain one request per session they did not make before. Not a behavioural break, but the claim as written is what a future reader will rely on when reasoning about the unenforced deployments. Fix: correct the two comments to say "fetches only `auth/mode`" — and see Finding 13 for the second, independent `getMode()` the page issues.

---

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 8 |
| Minor | 31 |
| Suggestion | 12 |
| **Total** | **52** |

### By category

| Category | Count |
|----------|-------|
| Business Logic | 5 |
| Code Quality | 14 |
| Error Handling | 5 |
| Breaking Change | 5 |
| Security | 4 |
| Device & UX | 8 |
| Frontend | 11 |

### Themes for the fix pass

1. **The fold is where this feature's correctness lives, and two of its three branches are wrong** (Findings 1, 7). Both are invisible to the current tests — one because no test exercises `--until` against existing cover, one because the test pins the implementation's answer under a comment quoting the spec's.
2. **Part G's un-park needed a per-clinic bound, not only a reason enum** (Finding 2) — and the spec named the exact risk class it re-armed.
3. **The exempt set deserves a re-read against what each action *does*, not what it is called** (Findings 10, 19, and the missing exemption in 4).
4. **The `/abonnement` screen and `SubscriptionProvider` are two authorities on one state** (Findings 12, 13, 52): the provider's `refresh` has no callers, the page re-probes, and a failed probe cannot be recovered by a 402.
5. **Config and deploy assets lag the code** (Finding 5) — the one screen a refusal points at is unconfigurable on the only kind that enforces.

---

## Out of scope, but do not lose it

`POST /api/backup` → `BackupNowCommand` → `PgDumpBackupService` runs `pg_dump` over
`ConnectionStrings:DefaultConnection` — on `HostedMultiTenant` the single database holding **every** clinic — and
writes the dump plus a recursive copy of the file-storage tree to a **caller-supplied folder used verbatim**, with
no allow-list, canonicalisation or root check, and no deployment-profile gate on the controller or the command.
Any one clinic's admin can therefore cause a cross-tenant dump to be written to any path the API process can write.

This was **dismissed as a review finding** because every line of it is in **unchanged** code: this branch only adds
`[AllowsWithoutSubscription]`, and that attribute is spec-aligned (FR-8 keeps the scheduled backup running, so
refusing the manual one would be incoherent). It is recorded here because the exemption is the moment somebody last
looked at this endpoint, and it should be **captured as its own item** (`/capture-followup`) rather than lost:
suggested fix is to gate `POST /api/backup` on a capability (`HasLocalDbTooling` / `UsesDiskStorage`) so it 404s on
`HostedMultiTenant`, and to restrict the destination to the resolved default root.

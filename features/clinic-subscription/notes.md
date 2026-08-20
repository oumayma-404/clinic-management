# clinic-subscription — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A cabinet's right to record work is a dated entitlement (`clinic-subscription`, all 7 parts)

on the
hosted deployment a clinic gets **30 free days**, and past its date it becomes **read-only** — every read, every
CSV export and every PDF keep working, and only writes are refused. Part A ships the foundation: one
**`ClinicSubscription`** per clinic whose `EndsOn` is a full re-fold of an append-only, cancellable
**`SubscriptionPeriod`** ledger, the 16th `DeploymentProfile` capability **`RequiresSubscription`**
(`HostedMultiTenant` only, decided by the **kind** and by nothing an operator can set — AC-7.3), and one migration
that grandfathers every pre-existing cabinet **open-ended** so no clinic anywhere can be refused for at least 30
days after deployment. **Part B ships the enforcement**: `API/Middleware/SubscriptionGateMiddleware` refuses every
non-GET request under `/api` with **402** + a code + a French sentence naming the date, unless the endpoint carries
**`[AllowsWithoutSubscription("<reason>")]`** — see `api/ClinicManagement.API/CLAUDE.md` for the exempt set, the
ordering rationale and the two derived guards. **Part C ships the visibility**: `GET /api/subscription`
(`AnyClinicRole`) + `GET /api/subscription/history` (`AdminOnly`), the **« Abonnement » screen** at
`web/app/abonnement/`, and `requiresSubscription` on `GET /api/auth/mode`. **Part D ships the client half**: the three
402 codes + `onSubscriptionRequired` in `web/lib/api/client.ts`, **`SubscriptionProvider`** owning FR-15's three
re-read triggers, the **`SubscriptionBanner`** on every screen, and AC-1.3's trial sentence — served from
`Subscription:TrialDays` as `trialDays` on `GET /api/auth/mode`, never a literal. **Part E ships the warnings**:
the daily **`SubscriptionWarningJob`** writes one in-app `StaffNotification` per threshold crossed — **7, 3, 1 and
0 days** out, four genuinely new unread rows deduped on the new `StaffNotification.SubscriptionThresholdDays`
column, deep-linking to « Abonnement » and **never** reaching a locked phone (AC-3.6). **Part F ships the vendor's side**: three commands
(`Grant` / `Cancel` / `SetSuspension`) reached only by **five console verbs** — `subscription-grant`, `-cancel`,
`-suspend`, `-unsuspend`, `-report` — plus `SubscriptionReportService`. **Part G ships the background half**: SMS,
WhatsApp and OS push all stop, and a queued row is **parked with a stated reason** rather than sent or discarded, so
extending the entitlement before the visit still gets the reminder out (EC-7).
⚠️ **Part G's load-bearing half is the *un-park*, not the park.** Both outboxes already had a non-terminal
`Blocked` status that survives the purge and carries a French sentence — but the pass that returns rows to the queue
asks only whether the **channel** can send (is there a sender · is it enabled for this clinic · are its credentials
present), and a row parked for expiry passes all three, so it would be released and dispatched **within a minute**
on a cabinet that has not paid. Hence the machine-readable **`OutboxBlockReason`** beside the prose (recovering an
outcome by matching French text is the defect this repo deleted in `adoption-gaps-remediation`) and hence the two
halves shipping in **one commit**. One **`OutboxSubscriptionGate`** is consulted from all four places — dispatch and
review, in each queue — rather than a condition written twice per queue, and it is asked for *every* parked row so a
channel-parked row is not released into a queue about to park it again.
⚠️ **A cabinet with no entitlement row keeps sending, unlike at the HTTP gate.** Fail-closed is right there — a
missing row must not become a way to write for ever — but nothing in the outbox is an authorization decision: the
work was recorded legitimately while the cabinet could write, and silencing a practice's reminders over our own
bookkeeping fault is invisible to the practice and unfixable by it. That fault is surfaced where it can be acted on
(`verify-schema`'s `every-clinic-has-an-entitlement`, `subscription-report`).
⚠️ **The scheduled backup and the daily stock-expiry alert are untouched, deliberately** (FR-8): an unbacked-up
medical record is a liability regardless of who has paid, and it is the one consequence paying late cannot undo. The
**manual** backup carries `[AllowsWithoutSubscription]` for the same reason — refusing it while the automatic one
runs would be incoherent. And **nothing is ever deleted** however long a cabinet stays expired (FR-14): both purges
drop terminal rows only, so `Blocked` was out of scope the moment it existed.
⚠️ **Part E's four rows are the opposite of the two ensure/clear alerts beside them, and deliberately.**
`StockExpiringSoon` and `BackupStale` keep **one** row and reword it; rewording **does not clear who has read it**,
so once the owner has read « 7 jours » the « 3 jours », « 1 jour » and « dernier jour » restatements would stay read
and never badge the bell again — AC-3.4's last three warnings invisible to exactly the person paying attention.
Hence a real dedupe **column** rather than a message prefix, and hence the wording being derived from the
**threshold** and not from the live countdown: a message rebuilt from « days remaining » differs every day, so the
ensure would restate and make every open browser refetch on every daily pass.
⚠️ **Two states are left exactly as they are.** A **suspended** cabinet is not warned (`SubscriptionStateReader`
surfaces no countdown for one — EC-11 — and « se termine dans 3 jours » sends a practice suspended for another
reason to pay for something that will not unblock it), and an **expired** one is neither warned again nor has its
rows **cleared**: it is now meeting a refused save, and those four rows are what explain it. Only an extension past
the window withdraws them — which is what **re-arms** the thresholds, so a cabinet that renews and later approaches
expiry again is warned all four times again (FR-5).
⚠️ **The job takes « today » as a parameter** (the Hangfire entry point resolves `ClinicClock.ClinicToday()` and
calls it), for the reason `SubscriptionStateReader` does: the four thresholds and the midnight they turn on are
otherwise untestable, and midnight is the only boundary that matters for a date that arrives by itself.
⚠️ **The banner mounts in `AppShell`, not in `app/layout.tsx`** where the plan put it: `AppShell` is `flex h-dvh`,
so a strip above it makes the document taller than the viewport — the page scrolls as a whole and the phone's
bottom bar goes off screen, which also makes the spec's « ≤ 15 % of a 380 px landscape viewport » budget
unmeetable. As a flex sibling of `<main>` it costs no height maths, exactly as `BottomNav` already does. It is
also what makes « no banner on `/login` or `/signup` » **structural**: the six routes that render no shell are
precisely those two plus `/setup`, `/join`, `/change-password` and `/signup/verifier`.
⚠️ **The per-day dismissal is keyed on the server's own `endsOn|daysRemaining` pair, never on a date the browser
computes.** « The next clinic day » is a fact about Tunis, and a workstation on any other timezone would bring the
banner back hours early or late — the defect `todayLocalIso()` exists to prevent one layer over. `daysRemaining`
decrements at Tunisian midnight, so the pair changes exactly when it should and needs no clock at all.
⚠️ **The 402 hook is the one that changes nothing about the failing call.** Unlike 426 (`<ClientVersionGate>` takes
the screen) and `must_change_password` (routed, and its English message replaced), a subscription refusal carries
the gate's own French sentence naming the date, so it travels on verbatim to `showErrorToast` and the form stays
open with everything typed still in it (AC-4.6). It must never touch `handleRequest`'s one-shot 401 retry — the
account is fine, and the refusal never signs anybody out (AC-4.5).
⚠️ **Part C's interim rail row is closed here**: `buildConfigItems` now takes `showSubscription`, fed from the
provider, so `SelfHostedLan` and `CloudBrowser` show no « Abonnement » row at all (AC-7.1/7.2). `lib/zones.ts`
keeps the full set — it builds the route→icon map and needs every destination that can render — which is why the
parameter defaults to *showing* the row.
⚠️ **« Abonnement » is reachable by a secretary, and that is a deliberate exception** to the product's rule that a
secretary sees no clinic-wide money screen (AC-2.2): the amounts are what the practice owes its software *vendor*,
none of it appears in la caisse or a patient's balance (FR-2), and the person who meets the refused save chairside is
usually not the person who pays. What stays `AdminOnly` is the payment **history**, not the screen.
⚠️ **`GET /api/subscription` reads the ledger; the gate deliberately does not.** The entitlement row carries one date
and no memory of where it came from, so « is the cover in force the free **trial**? » needs the fold — which is
exactly why `SubscriptionStateReader.Read` takes `isTrial` as a parameter. The gate stays one indexed row.
⚠️ **`Subscriptions` is on `RealtimeResourceResolver.ExcludedAreas`** (FR-15): the state is learned by a **re-read**,
never a broadcast, because neither moment that changes it can push one — a vendor grant runs in a separate process
with no caller's token to derive a clinic from, and an entitlement ending at midnight has no actor at all.
⚠️ **The vendor's verbs are verbs and not endpoints, and FR-6 is held by a derived guard.** A cabinet able to
extend its own entitlement over HTTP would not have one, so no controller references the three commands —
`SubscriptionVendorCommandReachabilityTests` asserts that over the commands it finds by *reflection*, and also
that every verb is actually dispatched by `Program.cs`, since a missing branch boots the **web host** instead and
reads to an operator as « the command did nothing ». All five gate on `MaintenanceDatabase.HasConnectionString`
rather than on a capability (amendment M3), and each declares its own tenant scope — `UseClinic(id)` for the four
that act on one cabinet, `UseSystemWide` for the report.
⚠️ **EC-5's race is resolved by a bounded re-fold retry, not by surfacing the 409.** « Two simultaneous grants both
land and are both kept », and yet `ClinicSubscription.Version` is mapped onto `xmin`, so the second writer's UPDATE
matches nothing and raises `ConflictException`. `SubscriptionRefold` retries the whole fold up to five times
(`IssueInvoiceCommand`'s precedent) — correct **only because `EndsOn` is derived**, so whoever saves last computes
the same date from every entry. The suspension command deliberately does *not* use it: it touches no ledger, so a
lost update there is an ordinary conflict and 409 is right.
⚠️ **`subscription-report --clinic <id|email>` is the only thing in the product that prints a period id**, and
`subscription-cancel` takes one — without that mode a mis-keyed grant older than the current session would be
uncorrectable. It shares `reconcile-money`'s exit codes; a **suspended** cabinet is listed but is not a *finding*
(an alarm that is always on is one nobody reads) while a cabinet with **no entitlement** is, because that is
FR-13's failure state rather than a state anyone chose.
⚠️ **A granted cabinet keeps its four expiry notifications for up to 24 h**, deliberately: the banner clears within
one 5-minute re-read because it reads the entitlement directly (AC-5.8), and the bell rows are withdrawn by Part
E's daily pass. Clearing them from the grant would force every verb to register a no-op `IRealtimeNotifier`, since
`INotificationGenerator`'s only implementation of that seam is the API's SignalR notifier.
⚠️ **A 52-finding review pass then corrected the feature in place, and four of its fixes changed behaviour rather
than tidying it.** (a) **The fold was wrong in two of its three branches**: a month duration clamped on the
*exclusive* cursor, so 31 Jan + 1 month ended **27** Feb where the spec, the Domain guide and the test's own comment
all say 28 — a day lost unpredictably, in the vendor's favour, with the test's expectations pinning the defect; and
an `--until` entry set the cursor **unconditionally**, so a mistyped year silently revoked months a cabinet had paid
for, with a success message. A recorded entry can now only ever *extend* cover, and `SubscriptionPeriod.Create`
refuses an end date before its own recorded day or beyond five years. (b) **Part G's un-park re-armed the very
starvation it was invented to fix**: the review scan had no per-clinic bound, and an expired cabinet's rows never
clear and are never purged, so past the batch size they occupied every review tick for ever and another practice's
channel-parked rows were never released. Both blocked scans now carry the due scan's fair-share loop. (c) **The gate
answered 402 to an unroutable `/api` path** — `GetEndpoint()?.…is null` read « nothing matched » as « declared no
exemption » — so a mistyped URL was answered with the loudest thing it can say. (d) **`users/{id}/reset-password` is
now exempt** (a forgotten password otherwise costs an expired cabinet the *reads* AC-4.1/4.2 guarantee, with no
other recovery on a hosted deployment) while **`users/{id}/status` is exempt in one direction only** — its recorded
reason is offboarding, but the action also re-activates, which the handler now refuses.
⚠️ **One documented decision was deliberately reversed**: a warning row naming a **superseded** end date is now
withdrawn rather than kept as feed history, because the bell otherwise showed « 1 jour … 21/08 » beside
« 3 jours … 22/08 » — two live claims about one date. A pure countdown escalation with the date unchanged still
keeps both rows, and the new row is still genuinely new rather than a rewrite (which would carry read markers).
⚠️ **`deploy/` now carries the ten `Subscription__*` variables**: on the only kind that enforces, « Abonnement » was
unconfigurable, so the screen a 402 points a chairside user at rendered « Aucun tarif n'est publié ». The five
vendor verbs are documented in `deploy/README.md` beside `verify-schema` — they are the only way to grant time.
Three findings were deferred with the remedy chosen: `follow-up/subscription-review-deferred.md`.
⚠️ **Interim state until Part D**: `/abonnement` is in `buildConfigItems` unconditionally, so `SelfHostedLan` and
`CloudBrowser` show one rail row whose page says « cette installation ne fonctionne pas par abonnement ». Both
endpoints **404 before the mediator** there, so nothing behind them is resolved; Part D's provider removes the row.
⚠️ **Reads are untouched by construction, not by a list**: the gate never inspects a GET/HEAD/OPTIONS, so « every
read, every CSV export and every PDF keep working » holds for every read that exists *and* every read added later.
An allow-list of readable endpoints would have to be kept complete, and the day it was not, an expired cabinet would
lose part of its own records.
⚠️ **The gate goes after `LocalAuthEnforcementMiddleware`, not beside `TenantScopeMiddleware`** — one block earlier
and a **402 masks the 401** of a revoked token and the **403 `must_change_password`** of a forced password change,
so a deactivated colleague is told the subscription lapsed and a user owing a password change is sent to
« Abonnement » instead of to the screen that unblocks them. It is correct in isolation and wrong only in *position*,
which is why `SubscriptionGateMiddlewareTests` asserts the ordering against `Program.cs`'s own source.
⚠️ **A caller who is not a cabinet passes**, rather than meeting `subscription_missing`: no clinic in scope means no
entitlement to find, and that fault code would otherwise land on precisely the vendor-console endpoints whose whole
purpose is to *end* a refusal.
⚠️ **`SubscriptionLedger.Fold` takes no clock and folds on an EXCLUSIVE cursor**, and both halves are load-bearing.
Passing « today » in — the naive reading of « the later of the current end or today, plus the duration » — makes the
answer depend on when it is recomputed, so a lapsed entry restarts from today and `verify-schema` flaps daily. And
a recorded day is an inclusive *start* while a running end is an inclusive *end*, so a single `anchor + duration`
over both is wrong in one of the two cases whichever way it is written: a **31-day** trial (AC-1.1 says 10 Aug →
8 Sep) or a one-day grant on a lapsed cabinet. Consequently **the trial's own date is not written directly either**
— provisioning builds the entry and calls `ClinicSubscription.RecomputeFrom`, which is the *only* writer of
`EndsOn`; a hand-computed `creationDay.AddDays(trialDays - 1)` disagrees with its own fold by one day and turns
`subscription-end-date-matches-ledger` red on every new cabinet.
⚠️ **Two construction doors, three callers of the helper.** `LocalClinicProvisioning.ProvisionAsync` is a `static`
taking its repositories as parameters, so the signature change breaks all three (`CreateClinicCommand`'s Local
branch, the **`provision-clinic`** verb — container = `AddInfrastructure` **only**, which is why the repository and
the policy are registered there — and **`VerifyClinicSignUpCommand`**, the public self-signup that will create most
trials). Door 2 of 2 is `CreateClinicCommand`'s **Auth0/Cloud** branch, which builds its own `Clinic`, never
reaches the helper, and always yields an **open-ended** entitlement — which is exactly why it is the door easiest
to forget, and why `ClinicCreationEntitlementTests` derives the door set by scanning for `new Clinic(` instead of
listing today's two.
⚠️ **The vendor's money is never the clinic's** (FR-2): separate tables and a separate
`SubscriptionPaymentMethod` enum, and `MoneyReadConsistencyTests` is **unchanged** — a subscription payment reaches
neither la caisse, l'extrait, « Créances », the dashboard's Argent section nor any patient's balance.

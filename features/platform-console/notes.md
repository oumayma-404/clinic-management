# platform-console — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## The vendor has a private console, and it cannot read a patient record (`platform-console` Part 1)

a
**fourth surface** on this product — a second identity population (`PlatformAccount`, its own tokens, a
mandatory TOTP second factor), a **second Kestrel listener** on an unpublished port reached over an SSH tunnel,
and a second Next application (`console/`) that contains none of the clinic bundle. Gated on the 16th
capability, **`DeploymentProfile.ServesPlatformConsole`** (`HostedMultiTenant` ✓ only). Part 1 delivers reaching
it and signing in; **Part 2 adds the portfolio and the counters behind it** (see the bullet below); the three
writes wait on
`features/clinic-subscription/`.
⚠️ **Binding the console listener can take the whole product offline, and that is the trap this part is built
around.** In `HostedMultiTenant` there is no cert file, so `Program.cs` never called `ConfigureKestrel` at all —
`ASPNETCORE_URLS` alone binds 5000 — and an explicit Kestrel endpoint **overrides that configuration
wholesale**. A bare `ListenAnyIP(consolePort)` would unbind 5000, Caddy's `/api/*` would stop resolving, and the
entire product would go dark while the console worked perfectly. `ConsoleListenerPlanning` resolves **both**
ports and they are bound in **one** call; EC-4's collision check is derived from the ports *actually resolved*,
never from `Hosting:HttpPort`/`HttpsPort`/`WebPort` — none of those three is set in the hosted compose file, so
a check written against them cannot fire in the one profile the console exists on.
⚠️ **A port bind is not a scoped surface**: every mapped route answers on every bound port, so `ConsolePortGate`
is what does the work — and unlike `TrustPortGate` it refuses **both** directions (a console path anywhere but
the console port, and anything but a console path on it), matched with `StartsWithSegments` so
`/api/platform-ish` cannot slip through. It is registered **unconditionally**: with the console off the port is
`0` and every console path 404s everywhere, which is what AC-1.8's « absent, not present-and-refusing » means.
⚠️ **AC-1.4 is a property of the signature, not of a policy.** The console's tokens carry their own key, issuer
and audience (`PlatformAuthConfig`, which **throws** rather than borrowing `Auth:Local:SigningKey`), so each
scheme fails the other's validation and a token on the wrong surface is **401, not 403**. The
`AuthorizationPolicies.PlatformConsole` policy **pins its scheme** — without that it authenticates against the
default (clinic) one and rejects every console token — while the four clinic policies keep **no** explicit
scheme, and that asymmetry is what makes the refusal true in both directions.
⚠️ **Console requests skip `AccountStateMiddleware`, `LocalAuthEnforcementMiddleware` and
`TenantScopeMiddleware`** (a console principal has no `User` row), so two middlewares fill the holes that
creates: **`PlatformAccountStateMiddleware`** re-reads the account and refuses a deactivation or a stale
`TokenVersion` on the **next** request (AC-1.6) — without it the deactivation verb would leave a revoked account
with full cross-cabinet access until its token expired, ⚠️ **which is exactly what it did until Part 7 fixed it;
see that bullet before trusting this sentence's history** — and **`PlatformTenantScopeMiddleware`** declares
`UseSystemWide("platform console")`, because an `Unset` scope reads **zero rows with no error**, which is
indistinguishable from a portfolio where every cabinet is idle (EC-12).
⚠️ **`AuditActor.Console(accountId)` lands here rather than with the first console write.** `AuditActorProvider`
returns the token's `sub` first, so consulting `IPlatformSessionContext` *before* `IClinicContext` is what stops
a console write being recorded as a bare GUID — indistinguishable from a clinic user in that cabinet's journal,
and invisible to Part 2's `console|` counter exclusion, which would then match nothing. Both failures are
silent, and both later parts consume a seam that is already correct.
⚠️ Accounts are created **only** by `platform-account create` (AC-8.5) — no MediatR command, because a handler
is one attribute away from being callable over HTTP — and it prints a one-time password plus an enrolment secret
shown **once**. The enrolment response carries the recovery codes, also once; a recovery code is **spent even
when the sign-in it accompanied fails** (AC-1.3b), while a *wrong password* spends none, or knowing an address
would be enough to burn all eight. Operator runbook in [`deploy/README.md`](deploy/README.md).

## The portfolio is a counter table, not a query over the ledger (`platform-console` Part 2)

`GET /api/platform/clinics` lists every cabinet with its **real activity** beside it — patients, staff accounts,
RDV pris (30 j), enregistrements (7 j / 30 j), jours actifs, dernier enregistrement, dernière connexion and what
the cabinet itself collected this month — filterable (« dormant »), searchable and paged, under a summary strip
(`GET /api/platform/summary`). Behind it: **`ClinicActivityDay`** (one cabinet, one clinic-local day — the durable
history Part 3's six-month trend reads) and **`ClinicActivitySnapshot`** (one cabinet, the row the list JOINs),
both written by the daily **`ClinicActivityCounterJob`**.
⚠️ **Two tables rather than one, and the snapshot exists because of AC-2.4a**: the list filters and sorts *on
activity*, so every figure must exist for every cabinet **before** a page is cut — a figure folded after the page
was selected would sort a window rather than the portfolio, and « les cabinets dormants » would mean « ceux de
cette page ». It is one bounded `Clinics ⋈ snapshot` LEFT JOIN, so the read is bounded by the number of cabinets
rather than by the busiest practice's whole history (EC-11). A **LEFT** join because a cabinet the pass has never
reached must still appear — with its counters stated as **unknown**, never as zeros (EC-15).
⚠️ **« Saves » counts only people at the cabinet, and both exclusions are silent failures otherwise** (AC-2.2):
`job|…` actors (backups, reminder dispatch, expiry passes — they write into *every* cabinet's ledger every day,
so counting them makes the emptiest practice read as active) **and the console's own `console|…` actor**, or
granting a dormant cabinet a subscription makes it read as active the next morning — on exactly the cabinet the
« dormant » filter just surfaced. `PlatformCounterPass` is pure and matches on **`AuditActor`'s own prefix
constants**, never a retyped literal.
⚠️ **Not every figure is audit-derived, and one of them must not be.** `patients` is a `COUNT` over the cabinet's
patients — *never* audit `Insert` rows: the ledger only exists since `adoption-qa-i`, so an established practice
would read as nearly empty, which is wrong in the direction of « barely used », i.e. exactly the churn signal the
list exists to give. `users`/`lastLoginAt` come from `IUserRepository.GetStaffSummaryAsync`, and
`clinicCollectedThisMonthDt` from **`PlatformCollectedReader`** — the same repository predicates la caisse sums,
through `PlanBillingRules.BilledPlanIds`. That makes the console the **fifth** money read, and
`MoneyReadConsistencyTests` was extended to pin it equal to `caisse.CashIn − caisse.Refunds`: the vendor quoting
a practice a turnover its own caisse contradicts is the worst possible place for drift.
⚠️ **`PlatformReadShape` is the whole of AC-7.2, and the tenant filter explicitly is not (AC-7.2a).** The filter
is *lifted* on this surface by design — a portfolio is a cross-cabinet read — so the guarantee is carried by a
**closed set of returned field names**, checked by `PlatformReadShapeTests`, which reflects over every
`Features.Platform` request's response type, recurses into nested DTOs and **fails the build** on any name
outside it. Names, not types: a type allow-list is satisfied by adding a field to a type already on it, which is
precisely how a patient's name would arrive. Asserted in **both** directions, so an unused allowance is a hole
that fails too.
⚠️ **The counter job rewrites the whole 30-day window each night, not only yesterday** — the audit rows are
already in hand for the snapshot, so the day rows are nearly free, and it makes the history self-healing: a
container down for three days would otherwise leave permanent holes in a trend nothing can reconstruct, since
the window the pass reads is itself 30 days. Idempotent through the unique `(ClinicId, Day)` index.
⚠️ **Subscriptions are deliberately absent, from one clearly-named place.** `PlatformSubscriptionPlaceholder`
keeps `plan`/`state`/`endsOn`/`daysRemaining` **null** and reports `subscriptionDataAvailable: false`, off which
the screen hides the state filters and says so; the « par date de fin » sort is not offered at all rather than
silently sorting by something else. Part 4 **deletes** that file — the compiler then lists every caller — rather
than widening it into a console-side entitlement fold, which is the FR-4 violation this feature is defined
around. ⚠️ `verify-schema` gained three checks; the plan's `clinic-activity-day-unique-per-clinic-day` was
**replaced**, because the unique index makes it unfalsifiable and the index is already diffed for free — see
`features/platform-console/stories/progress.md` DEV-4.
⚠️ **All four « Abonnement » filters were dead from Part 2 until a user reported it, and the defect was one missing
method parameter**: `PlatformPortfolioController.ListClinics` bound `dormant`/`q`/`sort`/`page`/`pageSize` and **not
`state`**, so the console sent `?state=expired`, model binding had nowhere to put it, `ListPlatformClinicsQuery.State`
stayed null and the list narrowed nothing. Every layer behind the hop was correct — the SQL predicate, `ParseState`,
the chips — and `PlatformPortfolioQueryTests` asserts the *handler* forwards every filter **it is given**, which it
did. A dropped filter also fails silently: the list still answers, with more cabinets than were asked for. The fix is
the parameter plus `PlatformPortfolioControllerTests`, whose second case is **derived** (every settable property of
the query must have a parameter to arrive on) so the next filter cannot be dropped the same way; both halves were
proven red. This is `fixes-dont-propagate`'s neighbour — a correct rule wired to one caller fewer than it has.
⚠️ Three adjustments landed with it. The list now defaults to **the newest cabinet first** (`ParseSort`'s fallback and
the console's `DEFAULT_PORTFOLIO_SORT` are the *same* value, which is what keeps the default out of the URL), a table
row is **clickable** — as an *addition* to the « Ouvrir » link, which stays because a `<tr>` with an `onClick` has no
keyboard path and no accessible role — and the row carries the cabinet's **administrator's e-mail**
(`PlatformClinicRowDto.AdminEmail`, already an allowed `PlatformReadShape` name from the fiche). That last one is
resolved by **`IUserRepository.GetPrimaryAdminContactsAsync`**, one batched read over the page, and the single-cabinet
`GetPrimaryAdminContactAsync` the fiche uses now **delegates to it**: « which admin is the contact? » is a precedence
rule (active first, then the founder, then deterministic), and two expressions of it would drift into the list naming
one person and the fiche naming another, with both screens looking right on their own.

## The console records what it looked at, and the record is readable (`platform-console` Part 3)

`GET /api/platform/clinics/{id}` opens one cabinet — the list's own figures, a **six-month trend** off
`ClinicActivityDay`, and the administrator's name, address and whether that account is still active — and
`GET /api/platform/access-log` + `/journal` serve the console's own append-only **`PlatformAccessEntry`** ledger,
paged, newest first, filterable by console account and by cabinet.
⚠️ **The detail read is recorded and the list read is deliberately not** (AC-3.5): one list read touches every
cabinet, so a row per cabinet per page load would drown every reading anyone wants — including this one. That
asymmetry is held by a test asserting on `ListPlatformClinicsQueryHandler`'s **constructor**, because « I ran the
list and no row appeared » passes just as happily when the ledger is broken for every caller.
⚠️ **`GetPlatformClinicDetailQuery` is a query that writes, and its ledger row is *not* best-effort** — the one
place in this codebase where a failed side effect fails its operation. `INotificationGenerator` swallows because
the operation it follows has already committed; here the operation **is** what is being recorded, and « every
detail read is recorded » is false the moment an unrecorded read succeeds. A `Command` was rejected for a
mechanical reason too: `RealtimeBroadcastBehavior` derives its key from the namespace, so one under `.Commands`
would broadcast into a clinic group on every page load.
⚠️ **`PlatformAccessEntry` has no FK to `Clinics` and none to `PlatformAccounts`** — the opposite of its two Part-2
siblings, which cascade on purpose. Those are measurements *of* a cabinet; this records what the **vendor** did,
and « who opened the file of the practice that has since been closed? » is the first row an audit would want. Hence
the denormalised `ClinicName`/`AccountEmail`, and hence its entries in `TenantScopeFilterTests.UnfilteredByDesign`
(its `ClinicId` is the cabinet *looked at*, not the row's owner) and in the audit interceptor's exclusion list
(auditing a ledger records the writing of a record, and a mere *read* would appear in the practice's own
« Journal d'activité » as a mutation of its data).
⚠️ **AC-3.2's payment history is explicitly deferred, and the screen says so rather than showing an empty table** —
an empty « Historique des paiements » asserts that this cabinet has never paid, a claim about the cabinet, where
the truth is a claim about the console. Same for EC-14: no end date is shown at all, because until the entitlement
ledger exists « sans échéance » and « nous ne pouvons pas le lire » are indistinguishable and the second is what is
true. Both sentences come from `PlatformSubscriptionPlaceholder`, which Part 4 deletes.
⚠️ On the client, the trend **states every value as text beside the bars** (reading a figure off a 40 px column is
guessing, and the vendor's next question is always « by how much? »), a month with `daysMeasured == 0` is hatched
and labelled « non mesuré » rather than drawn flat, and `components/ui/pager.tsx` was **extracted** so the journal
and the portfolio cannot drift on links-not-buttons and disabled-step-as-text.

## The vendor records a payment and the cabinet unlocks (`platform-console` Part 4)

`POST /api/platform/clinics/{id}/subscription-periods` — the console's **only** write, on its own
`PlatformSubscriptionsController` so `PlatformPortfolioController`'s « read-only by construction » stays a
checkable claim. The portfolio's entitlement column, its five state filters, its « par date de fin » sort, the
summary strip's state counts and the detail's payment history all become real, and
`PlatformSubscriptionPlaceholder` is **deleted** — the compiler listed its callers, which is what it was for.
⚠️ **The console computes no date** (AC-4.2). It reuses the companion's own pieces — `SubscriptionCabinetLookup`,
`SubscriptionPeriod.Create`, `SubscriptionRefold` — rather than sending `GrantSubscriptionPeriodCommand`, and the
reason is **atomicity**: that command commits on its own, so the FR-5 access-ledger row would be a second
transaction, and a payment recorded with no ledger row behind it is the « an unattributable action must not
aboutir » Part 3 settled for reads. Staging the ledger row before `SubscriptionRefold`'s single save is the only
shape in which AC-4.7 and AC-7.3 are true of the same instant. An explicit transaction was rejected too — the
refold retries on `ConflictException`, and a failed statement aborts the ambient transaction.
⚠️ **« En essai » had to become a SQL predicate, and the obvious column was unstorable.** AC-2.4a requires every
figure the portfolio filters on to exist before a page is cut, and folding N cabinets' ledgers is the unbounded
read EC-11 forbids — but « is the cover in force **today** the trial? » is a function of the ledger *and of
today*, while `RecomputeFrom` is deliberately clock-free. The storable form is **`ClinicSubscription.LatestCoverKind`**,
the kind of the last non-cancelled entry in fold order: a pure function of the ledger, written by `RecomputeFrom`
alone and re-derived by `verify-schema`'s **`subscription-cover-kind-matches-ledger`** through the *real* fold.
The filter ANDs it with the state terms, so a lapsed trial is excluded by « expiré » regardless of its kind.
`IsOnTrial` **moved** out of `GetSubscriptionQuery` into `SubscriptionTrial` — the console is its second caller.
⚠️ **A double-click produces one entry, and the guard is a partial-unique index** on
`PlatformAccessEntry.IdempotencyKey` — never the handler's read-first check, which two simultaneous submissions
both pass. The key lives on the access ledger rather than in a table of its own because every console write
already produces exactly one row there, in the same transaction; the row also names the `SubscriptionPeriodId`
it created, so a replay returns the **first** outcome instead of guessing. A lost race replays rather than
surfacing the unique violation (EC-5), and two *different* grants both land and are both kept (EC-6).
⚠️ **`Platform` joins `RealtimeResourceResolver.ExcludedAreas`**: a console account belongs to no clinic, so the
behaviour's audience would be nobody — silently — and a new key fails the contract test in both directions.
AC-4.4a is dropped for the reason `Subscriptions` is already excluded (progress.md DEV-12).
⚠️ **The console cannot grant open-ended cover**, because the companion refuses it in its own handler: a cabinet
that should never expire is grandfathered by a migration. EC-14 is met on the **read** side — « Sans échéance »
is said in words wherever a null end date appears.

## A mis-keyed payment is corrected, never erased (`platform-console` Part 5)

`POST /api/platform/clinics/{id}/subscription-periods/{entryId}/cancellation` strikes one ledger entry through with
a **mandatory motif** and the cabinet's end date recomputes — possibly into the past, at which point the practice
becomes read-only again. The entry is **kept**, struck through *and* marked « Annulé » in words, carrying its motif,
its canceller (`console|{accountId}`) and the moment; `PlatformAccessAction.CancelledPeriod` arrives with the write
that produces it, and Part 4's shape is reused verbatim — the companion's own pieces, with the FR-5 access row
staged before `SubscriptionRefold`'s single save.
⚠️ **AC-5.3's « from which date » is computed by re-folding the real ledger with that one entry marked cancelled**,
and it travels **on the detail read** (`PlatformSubscriptionEntryDto.IfCancelled`) rather than behind a preview
endpoint — so the confirmation cannot open without the sentence, which a preview call that can fail would allow.
The naive client-side form (« the current end minus this entry's duration ») is right only when the entry is the
*latest* one: the fold advances on an **exclusive cursor**, so removing a **middle** entry shortens every stretch
after it. `isTrial` comes from the *previewed* fold too, since cancelling a paid entry can hand the cover back to
the trial. The preview and the write are held equal by a test that runs both over one ledger, proven red.
⚠️ **Cancelling a cabinet's ONLY entry yields « sans échéance », not « expiré »** — `FoldWithSpans` starts at null,
so a wholly-cancelled ledger folds to no end date, which the state reader reads as *no expiry*. It is the
companion's own semantics, left alone (FR-4), and unreachable in practice because every cabinet is provisioned with
an opening entry (FR-13). It is why EC-7's fixture seeds a lapsed **trial** beside the grant: a one-entry fixture
asserts the opposite of EC-7 and passes.
⚠️ **No idempotency key here, unlike the grant, and that is deliberate.** A double-click on « Enregistrer le
paiement » is the vendor's own repeated action, so replaying the first outcome is what they wanted (AC-4.6) — but an
entry already struck through was struck through by **somebody**, and which colleague and for what motif is a fact
the vendor needs. So « déjà annulée » is a **refusal** carrying `period_already_cancelled` (409) and the dialog
re-reads the fiche so that motif and author appear beside it. ⚠️ And it is a **POST, not a DELETE**: nothing is
deleted, and `DELETE` would advertise the opposite to every reader of the controller.

## A cabinet is stopped for abuse, and never told it has expired (`platform-console` Part 6)

`POST /api/platform/clinics/{id}/suspension` (mandatory motif) and `…/suspension/lifting` make a cabinet read-only
**independently of what it has paid**, and `PlatformAccessAction` closes with `Suspended`/`Unsuspended`. No
migration and no model change: the action column is `HasConversion<int>()` and the entitlement already carried
`SuspensionReason`/`SuspendedAtUtc`/`SuspendedBy`.
⚠️ **Nothing here touches the ledger, and that is the whole of AC-6.4.** « Unsuspending restores whatever
entitlement the cabinet had » is not a restore step — it is a property of never having spent anything, so
`SetClinicSuspensionFromConsoleCommand` deliberately does **not** use `SubscriptionRefold` (no entry changed ⇒ no
date to re-fold ⇒ a lost update is an ordinary 409, the companion's own reasoning). The response echoes the
unchanged `endsOn` precisely so that is checkable on the screen that did it.
⚠️ **The load-bearing case is a lift landing on a cabinet that is still expired.** The outcome is read back through
`SubscriptionStateReader`, never asserted from the button pressed: a naive `MakesReadOnly = IsSuspended` passes
**16 of the 17** new tests and tells the vendor a practice can work again when its next save will be refused.
Proven by writing that exact line and watching one test — and only that one — go red.
⚠️ **One command with a `bool Suspend`, two endpoints.** It mirrors the companion's own
`SetSubscriptionSuspensionCommand` rather than the story's planned suspend/unsuspend pair, because two handlers
would be two copies of « resolve · mutate · stage the access row · save » — the `fixes-dont-propagate` shape. The
direction stays in the **URL** so no truncated body can flip it, and which journal action is recorded is decided in
one place.
⚠️ **Re-suspending is a 409, not a re-statement**: the entitlement holds exactly one motif, one author and one
moment, so a second `Suspend` would overwrite a colleague's reasoning with no trace of it anywhere — changing a
motif is lift-then-suspend, and both halves land in the journal. **Lifting a cabinet that is not suspended is a
409 too**, because `Unsuspend` clears nothing there: a silent success would record an action that never happened
and read on the fiche as having released a cabinet whose real problem is its end date, which the refusal names.
⚠️ **The two journal rows are the only durable record.** `Unsuspend` clears the trail off the entitlement by
design, so `GET /api/platform/clinics/{id}`'s new `suspension` object explains a *live* suspension only, and
« qui a suspendu ce cabinet en mars ? » is answerable at `/journal` and nowhere else.
⚠️ **On the client it is its own section, not a control under « Abonnement et paiements »** — that placement *is*
AC-6.3. A « Suspendre » button beside the payment history presents suspension as a billing lever, and a vendor who
reads it that way reaches for a **cancellation** instead, which is not reversible. « Suspendu » is stated in words
with the motif quoted, so a greyscale printout and a screen reader get the same facts as a colour.

## The verification found the hole the six parts before it could not (`platform-console` Part 7)

Part 7 adds no
feature — it is the schema gate run before and after the migration batch and diffed, the operator runbook in
[`deploy/README.md`](deploy/README.md), and the paragraph a clinic can be *sent* about what the vendor sees. Its
step 51 says « confirm the two "cannot look the same" requirements **by trying them** », and trying them is what
earned this bullet.
⚠️ **`PlatformAccountStateMiddleware` was inert in production for the whole life of the feature**, so AC-1.6's two
revocations and AC-8.1's « one-time » password were absent while every layer reported them present. It read
`context.User`, which for a **console** token is never populated where it runs: `UseAuthentication` authenticates
only the *default* (clinic) scheme — which a console token fails **by design**, that being AC-1.4 — and the
console's own scheme is authenticated inside `AuthorizationMiddleware`, because the policy pins it, i.e. *after*
this middleware. So the account id resolved to null on every request and everything passed through. Proven over
the wire: signed in, ran `platform-account --deactivate`, called `/api/platform/summary` again with the same
token — **HTTP 200, the whole portfolio**. The fix authenticates the console scheme in the middleware and every
check reads *that* principal; `HasCurrentTokenVersion` was still reading `context.User` after the first attempt,
which the new test caught.
⚠️ **Its tests passed throughout because they set `context.User` by hand** — the one thing production does not do.
That is the general lesson: a middleware whose subject is established by a *pinned* authentication scheme cannot be
unit-tested through `DefaultHttpContext.User` without asserting the very arrangement that is broken. The new cases
install a stub `IAuthenticationService` instead, so they exercise the call production makes.
⚠️ **EC-12 and EC-15 both hold, and the second was proven twice.** With the database frozen the portfolio read
answers **500 + a French sentence** and the page renders « je n'ai pas pu lire » — never an empty table, because
« aucun cabinet » and « je n'ai pas pu lire » are the same picture and opposite facts. A deployment whose counter
pass has never run reports « jamais mesuré » on screen (`neverMeasured: 4`, `dormant: 0`) **and** as
`verify-schema`'s `clinic-activity-snapshot-covers-every-clinic` — the one figure that says « the pass has not run »
rather than « these practices are idle ».
⚠️ **The before/after schema diff is the clean result the workflow is for**: 11 drifts, every one of them a
`MISSING` index or FK in the four new tables, going to **0** — plus `subscription-cover-kind-matches-ledger` and
the two counter checks moving from « not applicable — does not exist yet » to green, which independently
re-derives Part 4's `LatestCoverKind` through the *real* fold.

## The vendor can put a lost authenticator right, and the journal finally names who did (`hosted-security-hardening` FR-1.4)

`POST /api/platform/clinics/{id}/second-factor/reset` (mandatory motif, address in the body) clears one clinic
account's second factor so its owner can enrol a new one. `PlatformAccessAction` gains `SecondFactorReset`, and
`PlatformAccessEntries` gains three nullable columns — `TargetUserId`, `TargetEmail`, `Reason`.

⚠️ **Why the vendor has to be able to do this.** Clearing a factor may never rest on the password alone, so
somebody must vouch for whoever lost their authenticator. A recovery code they still hold does it with no vendor
involved (`User.GrantTotpReplacement`), and their own administrator does it otherwise (`ResetUserTotpCommand`).
Both fail in the ordinary case for this product: **a cabinet with one administrator** whose phone is gone and whose
codes were never printed. The vendor's only previous route was `dotnet run -- reset-user-totp` over SSH, so a
support call was answered by whoever had shell access, off the console's own record.

⚠️ **The motif lives on the ledger row, and it is the only console write for which that is true.** A suspension
writes its reason onto the entitlement and a cancellation onto the entry it strikes through; `DisableTotp` keeps no
trace of anything. Without `TargetUserId`/`Reason` on the row, « qui a désarmé le compte de qui, et pourquoi ? » has
no answer anywhere in the product — so the journal row is not bookkeeping here, it is the whole record, and it is
what stands between the endpoint and a social-engineered telephone call.

⚠️ **It adds no new READ.** There is deliberately no roster endpoint: the vendor types the address the caller gave
them over the phone, so « the console cannot see your records » stays exactly as narrow as it was. The cabinet is in
the **URL** and the person in the **body**, which is what bounds a mis-keyed character to the practice already open —
and an unknown address and one belonging to another cabinet answer with the **same sentence**, or the endpoint
becomes a way of asking « does this person work there? » about any address at all.

⚠️ **Its own controller** (`PlatformClinicSecurityController`), for Part 6's reason one level along: filing it under
« Abonnement et paiements » would present a support action as a billing lever. On the fiche it sits in
« Administrateur du cabinet » — the only section about *people*, and next to the address the vendor is about to ring
back.

⚠️ **`[AllowsWithoutSubscription]` is the point of the endpoint, not defensive decoration.** The person who cannot
sign in is frequently the sole administrator of a cabinet whose cover lapsed *because* nobody could sign in to pay.
Gating on the entitlement would make that lockout self-sustaining. `SubscriptionExemptionCoverageTests` refused the
new exemption until it was written into the reviewed set with that reason — the guard working as designed.

⚠️ **The notice names the actor, and that needed a new parameter.** `INotificationGenerator.SecondFactorResetAsync`
takes a required `SecondFactorResetBy`, and the four sentences (in-app and e-mail, × two actors) moved into one
`SecondFactorResetNotice`. « Prévenez votre administrateur » is useless advice for a vendor action — the
administrator has no record of it and no power over it — and that notice is the only mechanism by which a
social-engineered reset becomes visible to somebody able to recognise it.

⚠️ **The wire test found that `AccountEmail` had NEVER been populated — on any row, since the console shipped.**
34 rows across all five action kinds the console had performed, every one blank, while the column's own docstring
promised « the account's address at the time, so a row stays readable without joining a live account ».
`PlatformSessionContext.GetEmail()` asked for the short claim name `email`, but the JWT handler's inbound mapping
is on, so it arrives as the long `ClaimTypes.Email` URI. `GetAccountId()` survived only because it already checked
both spellings — the fix is the same two-name lookup one line below it. **Every test in the suite mocked
`IPlatformSessionContext`**, which is why nothing saw it; `PlatformSessionContextTests` now exercises the real
class against a principal built the way the handler leaves one, and Part 7's own lesson repeats itself exactly.

## The first person to open the console met three dead ends, and none of them said so (first hosted deployment)

Found on the first real `HostedMultiTenant` bring-up (OVH VPS, 2026-08-27), by an operator following
`deploy/VPS-BRINGUP.md` § 9 top to bottom. Every part above was verified; each of these three sits in the gap
between them, and each fails as something other than what it is.

⚠️ **The documented console URL cannot load in any browser.** The Caddy site was addressed *only* as
`https://127.0.0.1:9443`, and no browser sends SNI for an IP literal — there is no name to send. Caddy has no site
matching an empty `ServerName` on that port, so it ends the handshake with `internal_error`, which Chrome reports
as `ERR_SSL_PROTOCOL_ERROR`. Measured, over the tunnel and on the server's own loopback alike:

```
openssl s_client -connect 127.0.0.1:9443 -servername 127.0.0.1  → TLSv1.3, TLS_AES_128_GCM_SHA256
openssl s_client -connect 127.0.0.1:9443 -noservername          → tlsv1 alert internal error (80)
```

The site now lists `https://console.localhost:9443` first and keeps the IP for clients that do send it as SNI.
**What made this survive review is that every check anyone would run passes**: `curl -k`, `wget
--no-check-certificate` and a container-internal probe all report a healthy console, and the design's own
paragraph *predicts* a certificate warning — so an operator who hits a hard TLS failure has already been told to
expect trouble at exactly that step and reads it as the expected one. A « certificate warning is expected » note
is a place a real handshake failure can hide.

⚠️ **`must_change_password` is a destination, and all three reading pages rendered it as prose.**
`PlatformAccountStateMiddleware` refuses every console read while a bootstrapped account still holds the one-time
password — correctly. But `/cabinets`, `/journal` and `/cabinets/[clinicId]` each caught the `ConsoleApiError` and
rendered `ReadFailure`, so the first account created on a deployment signs in and is told « Portefeuille
illisible — la liste n'a pas pu être lue » about a server that read fine and answered precisely. Nothing links to
`/mot-de-passe`, so the only way forward was typing the URL. `redirectIfPasswordChangeRequired` is the fix and
`check:responsive`'s `password-change-is-a-destination` is the derived guard — the distinction already existed
one file away, in `sign-in-form.tsx`'s `totp_enrolment_required` branch ("a destination rather than a message"),
and was simply never carried to the pages. Three sites, one decision, made once: the shape
[[fixes-dont-propagate]] describes.

⚠️ **The PITR sidecar had been crash-looping since the deployment came up, printing three healthy lines each
time round.** `pitr-entrypoint.sh` ended `exec supercronic /etc/pitr.cron` — a PATH-resolved `argv[0]`. supercronic,
seeing it is PID 1, re-execs `os.Args[0]` to install its process reaper *without* a PATH lookup, gets ENOENT, and
dies with « Failed to fork exec: no such file or directory » one second after « handing off to supercronic ». The
absolute path fixes it. What matters is the shape of the failure: WAL kept shipping the whole time — that is
postgres's own `archive_command`, in a different container — so the off-site prefix went on filling with segments
while the base backups every one of them has to anchor to had stopped at the very first. A restarting container
whose log's last full cycle reads « existing base backup(s) found » and « handing off » looks, at a glance, like
one that is working.

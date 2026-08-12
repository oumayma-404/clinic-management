# Codebase Context: Forfait de rappels WhatsApp (vendor-whatsapp-messaging-quota)

**Feature:** [features/vendor-whatsapp-messaging-quota/](../)
**Written:** 2026-08-12 at commit `4e59462`
**Last verified:** 2026-08-12 at commit `64998f1` (end of Part 5 — the story is complete)
**Scope:** durable facts only. Anything marked ⚠️ is volatile and MUST be re-checked.

> Written during **Part 4** (the fifth session of Story 1). Parts 0–3 had no `context.md`, so the facts below
> were re-derived four times; this file exists so Part 5 does not pay for a fifth.

## Staleness check (run this first — one command)

```bash
git diff --stat 64998f1..HEAD -- \
  api/ClinicManagement.Application/Features/Messaging \
  api/ClinicManagement.Application/Common/ClinicClock.cs \
  api/ClinicManagement.Application/Common/Interfaces \
  api/ClinicManagement.Application/Common/Maintenance/MessagingReportService.cs \
  api/ClinicManagement.Application/Features/Platform \
  api/ClinicManagement.Application/Features/Clinics/Commands \
  api/ClinicManagement.Domain/Entities/ClinicReminderSettings.cs \
  api/ClinicManagement.Domain/Enums \
  api/ClinicManagement.Domain/Repositories/IMessagingAllowanceRepository.cs \
  api/ClinicManagement.Domain/Repositories/IClinicReminderSettingsRepository.cs \
  api/ClinicManagement.Infrastructure/Services \
  api/ClinicManagement.Infrastructure/Repositories/ClinicReminderSettingsRepository.cs \
  api/ClinicManagement.API/BackgroundJobs \
  api/ClinicManagement.API/Controllers \
  api/ClinicManagement.API/Program.cs \
  web/components/reminder-settings.tsx web/components/rappels web/app/rappels \
  console/components
```

- **Empty output** → every pointer below still holds. Skip Step 6's exploration entirely.
- **Non-empty** → re-read only the listed files that moved, correct this file **in place**, re-stamp
  "Last verified".

## Gate commands (verified working)

| Gate | Command | Verified | Notes / quirks |
|------|---------|----------|----------------|
| Backend build | `cd api && dotnet build --no-incremental -p:BaseOutputPath="$TEMP/cm-build/"` | 2026-08-12 | `--no-incremental` is required to see the **true** warning set; an incremental run skips unchanged projects and reports « 0 Warning(s) » having compiled nothing |
| Backend tests | `cd api && dotnet test -c Release -p:BaseOutputPath="$TEMP/cm-test/"` | 2026-08-12 | **Both** flags. Smart App Control refuses freshly-built in-repo Debug test assemblies (`0x800711C7`); `-c Release` + an out-of-repo path is the combination that runs |
| Targeted tests | same, plus `--filter "FullyQualifiedName~Foo\|FullyQualifiedName~Bar"` | 2026-08-12 | |
| Schema | `cd api/ClinicManagement.API && dotnet run -- verify-schema` | 2026-08-12 | The **only** gate a migration has. Run before *and* after and `diff`. Exit 0 clean / 1 couldn't run / 2 drift |
| Money | `cd api/ClinicManagement.API && dotnet run -- reconcile-money` | 2026-08-12 | Same exit codes, same before/after-and-diff workflow |
| `web` typecheck | `cd web && npx tsc --noEmit` | 2026-08-12 | |
| `web` device gate | `cd web && npm run check:responsive` | 2026-08-12 | 15 checks; every one enforced (no staging set) |
| `web` build | `cd web && npm run build` | 2026-08-12 | A failure here is almost always a concurrent `next dev` holding `.next` — check the process before reading the error |
| `console` typecheck | `cd console && npx tsc --noEmit` | 2026-08-12 | |
| `console` device gate | `cd console && node scripts/check-responsive.mjs` | 2026-08-12 | ⚠️ `scripts/`, not the repo root — `web`'s equivalent is an npm script |
| `console` build | `cd console && npm run build` | 2026-08-12 | |

**Gates that do NOT exist in this repo:** no test runner in `web/` or `console/`; no working ESLint in `web/`
(`eslint` is in the `lint` script but not in `devDependencies`); no CI for `mobile/ios`; **nothing in
`ClinicManagement.UnitTests` touches a database**, which is why `verify-schema` is the only check a migration
has. There is a CI gate (`.github/workflows/ci.yml`, five jobs) but it does not run any of the console verbs.

**Environment quirks that cost time to discover:**
- Build/test output must go **outside** the repo (`BaseOutputPath`): a running `dotnet run` API holds
  `api/**/bin`, and it is also what lets `dotnet ef migrations add` work while the dev API is up.
- `MSB3021`/`MSB3027` is a **file lock**, not a compile error — the message names the PID; kill it and rebuild.
- `dotnet ef` on this machine sometimes cannot load the freshly-built assembly (same WDAC/SAC rule), so
  several migrations in this repo are **hand-written** (migration + `.Designer.cs` + snapshot). Check the
  newest migration's own docstring before assuming the scaffolder works.
- EF's differ emits an **`xmin` column** in every `CreateTable` for `Entity<T>.Version`; PostgreSQL rejects it
  (`conflicts with a system column name`). Remove those lines by hand.
- `find` under `api/` hits `Permission denied` on `ClinicManagement.API/bin/.../Backups/clinic-backup-*` —
  use `Glob`/`Grep` instead.

## Reference implementations — imitate these

| Doing what | Read this first | Why it's the right model |
|------------|-----------------|--------------------------|
| A vendor-side console write | `Application/Features/Platform/Commands/CancelMessagingAllowanceFromConsoleCommand.cs` | The shape Parts 3–5 settled: the companion's own pieces, access-ledger row staged **before** the single save |
| A pure ledger fold | `Domain/Services/MessagingAllowanceLedger.cs` (and its `SubscriptionLedger` sibling) | Clock-free, total, one arithmetic for write + verify + read |
| An ordered-terms outbox gate | `Application/Features/Messaging/OutboxMessagingGate.cs` | Part 4 § 33a adds a **term** here, not a second gate |
| A daily reconciling pass | `API/BackgroundJobs/MessagingAllowanceJob.cs` | Per-cabinet try/catch **per duty**; § 35's poll is its third |
| An anonymous webhook-style action | `API/Controllers/GoogleCalendarController.cs` (`callback`) | The repo's only other `[AllowAnonymous]` action that writes |
| A capability-gated 404-before-the-mediator endpoint | `API/Controllers/SubscriptionController.cs` | AC-1.6/EC-16's « absent, not present-and-refusing » |
| A channel sender | `Infrastructure/Services/WhatsAppSender.cs` over `HttpReminderChannelSender.cs` | § 37 adds a classification hook to the base and overrides it here only |
| A derived guard test | `UnitTests/.../MessagingVendorCommandReachabilityTests.cs`, `PlatformReadShapeTests.cs`, `RealtimeResourceResolverTests.cs` | Each derives its set by reflection or by parsing the source, never from a list |
| A clinic card with tri-state availability | `web/components/rappels/messaging-allowance-card.tsx` | Part 2's card — § 38's connect card is its sibling on the same page |
| A console cabinet-file section | `console/components/messaging-section.tsx` | § 36 adds the template category to it |

## Authorities this feature must go through

**Reuse, never re-solve** — a second implementation of one of these is the defect this table exists to prevent.

| Question | Authority (path) | ⚠️ |
|----------|------------------|----|
| What a Tunisian month / day / year is | `Application/Common/ClinicClock.cs` | Part 0 moved two private copies into it |
| What allowance a month has | `Domain/Services/MessagingAllowanceLedger.cs` | |
| Is this a raise or a lowering, and from which month | `Application/Features/Messaging/MessagingAllowancePlan.cs` | Two doors call it (DEV-8) |
| Re-folding after a write | `Application/Features/Messaging/MessagingAllowanceRefold.cs` | |
| May a queued WhatsApp reminder leave the building | `Application/Features/Messaging/OutboxMessagingGate.cs` | |
| May a queued row leave at all (entitlement) | `Application/Features/Subscriptions/OutboxSubscriptionGate.cs` | Asked **before** the messaging gate |
| The French refusal sentences + their codes | `Application/Features/Messaging/MessagingRefusals.cs` | |
| AC-1.4's five sender states and their French labels | `Application/Features/Messaging/MessagingSenderState.cs` | |
| The 80/95/100 % thresholds | `Application/Features/Messaging/MessagingAllowanceThresholds.cs` | |
| French labels for the messaging enums | `Application/Features/Messaging/MessagingAllowanceLabels.cs` | |
| Which console field names may be returned | `Application/Features/Platform/PlatformReadShape.cs` | Asserted in **both** directions |
| Whose rows this scope may read | `Application/Common/Interfaces/ITenantScope.cs` | `Unset` reads **nothing**, silently |
| Does this deployment sell vendor messaging | `Application/Common/Interfaces/IVendorMessagingAvailability.cs` | Two members: kind vs. kind **and** Meta credentials |
| The operator's default allowance + contact route | `Application/Common/Interfaces/IMessagingAllowancePolicy.cs` | |
| Meta app id / secret / Graph version | `Infrastructure/Services/MetaConfig.cs` | The **one** server-side Graph-version authority |
| The browser's Graph version | `NEXT_PUBLIC_META_GRAPH_VERSION` → `web/components/reminder-settings.tsx` | Story 0 made both derive from one `META_GRAPH_API_VERSION` key in `deploy/` |
| Money rounding | `Domain/Services/InvoiceCalculator.cs` | |
| Who the audit ledger stamps | `Application/Common/Interfaces/IAuditActorProvider.cs` | A job declares `RunAs`; a restore/console prefixes |

## Conventions in force (and where they're written)

| Convention | Stated in |
|------------|-----------|
| Comment budget — one line, two at the very most | `~/.claude/rules/backend-style.md` |
| The device + UX contract and the frontend gate | `.claude/rules/frontend-web.md` (directive) + `web/CLAUDE.md` (rationale) |
| Layer boundaries, `Result<T>`, the MediatR pipeline | `api/ClinicManagement.Application/CLAUDE.md` |
| Entity/enum/repository conventions | `api/ClinicManagement.Domain/CLAUDE.md` |
| EF config, migrations, DI wiring | `api/ClinicManagement.Infrastructure/CLAUDE.md` |
| Controllers, policies, jobs, middleware order, console verbs | `api/ClinicManagement.API/CLAUDE.md` |
| Realtime key contract (both directions) | `web/lib/realtime/clinic-hub.ts` + `RealtimeResourceResolverTests` |

## Seams this story touches

Paths only — signatures go stale, and the previous part is the most likely thing to have changed them.

| Path | What it currently owns | Touched by part |
|------|------------------------|-----------------|
| `Application/Common/ClinicClock.cs` | the month primitives | 0 |
| `Domain/Enums/{OutboxBlockReason,PlatformAccessAction,NotificationCategory,NotificationTargetKind,MessagingAllowanceKind,WhatsAppTemplateStatus}.cs` | the feature's extension points | 0, 3 |
| `Infrastructure/Deployment/DeploymentProfile.cs` | the 18th capability `SellsVendorMessaging` | 0 |
| `Domain/Entities/{MessagingAllowanceEntry,ClinicMessagingMonth}.cs` | the ledger + the counting row | 1 |
| `API/BackgroundJobs/NotificationJob.cs` | dispatch, counting, both gate call sites, the outcome `switch` | 1, 4 |
| `API/BackgroundJobs/MessagingAllowanceJob.cs` | provision + reconcile (+ § 35's poll) | 2, 4 |
| `Application/Features/Messaging/Queries/*` | the two clinic reads | 2 |
| `Application/Features/Platform/Queries/GetPlatformClinicDetailQuery.cs` | the cabinet file's `messaging` object | 3, 4 |
| `Application/Common/Maintenance/MessagingReportService.cs` | the four report buckets | 3, 4 |
| `API/Maintenance/MessagingCommands.cs` | the three `messaging-*` verbs | 3 |
| `Domain/Entities/ClinicReminderSettings.cs` | channel settings + the WhatsApp connection block | 4 |
| `Infrastructure/Services/{IReminderChannelSender,HttpReminderChannelSender,WhatsAppSender}.cs` | the send outcomes | 4 |
| `Application/Features/Clinics/Commands/{ConnectClinicWhatsAppCommand,UpdateClinicReminderSettingsCommand}.cs` | the connect path + the manual credential fields | 4 |
| `web/components/reminder-settings.tsx` | the Embedded-Signup connect path and the manual fields | 4 |
| `web/app/rappels/page.tsx`, `web/components/rappels/*` | the clinic surface | 2, 4 |
| `console/components/messaging-section.tsx` | the cabinet file's forfait section | 3, 4 |

## ⚠️ Volatile — re-check every session, never trust

| Fact | Why it moves | How to check |
|------|--------------|--------------|
| Which parts have landed | one part per session | `git log --oneline -6` + `progress.md`'s part headings — **all six parts (0–5) are in; the story is done** |
| Whether a symbol a later part needs exists yet | Parts 4–5 create several | grep for the symbol |
| The dev database being **ahead** of this branch | it carries another feature's migration (`AddUserSecondFactorAndSessionFamilies`) | `dotnet ef migrations list` vs `__EFMigrationsHistory` |
| Working-tree cleanliness | other authors' work arrives between sessions | `git status` + `git diff HEAD --numstat` before staging |
| Untracked `features/hosted-security-hardening/` and `features/landing-website/agent-prompt.md` | another author's in-flight work | `git status` — **never** stage these |

## Answered questions (don't re-litigate)

- Why the migration batch is split by part → `progress.md` DEV-5, DEV-9 (the plan's own « before and after the
  migration **batch** » wording allows it; Part 5 diffs the whole batch).
- Why `MessagingAllowancePlan` is extracted rather than a private method → `progress.md` DEV-8.
- Why the access ledger needed its own column instead of reusing `SubscriptionPeriodId` → `progress.md` DEV-9.
- Why `MessagingReportService.Classify` is public → `progress.md` DEV-10.
- Why `Messaging` is on `RealtimeResourceResolver.ExcludedAreas` → Part 3's ⚠️ in `progress.md`.
- Why `MessagingSender.From` takes a **nullable** template status → `progress.md` DEV-6.
- Why `senderNumber` is always null → `progress.md` DEV-7 (nothing stores a cabinet's own number).
- Which Embedded Signup version we are on and where it is pinned → Story 0 in `progress.md` (**v3 → v4** in
  Part 4 § 31; the 15 Oct 2026 deprecation names **v2 only**).
- Why bumping Graph `v21.0` → `v26.0` is *not* Part 4's work → plan § 31's ⚠️ + follow-up (**R-2a**).

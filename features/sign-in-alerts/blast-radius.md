# Blast radius — sign-in refusals recorded, vendor alerted

Written before the first edit (`.claude/rules/regression-safety.md` § 1).

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `Application/Extensions.cs` — a 4th open-generic `IPipelineBehavior` | runs on every MediatR request | every command/query (~400), the jobs that send through the mediator | unaffected — `SignInRefusalBehavior` passes through unless `TRequest` implements `ICredentialAttempt` (decided once per closed type, like `RealtimeBroadcastBehavior.Resource`) and the response is a refused `Result` |
| 2 | The six anonymous credential commands gain `ICredentialAttempt` (`LoginCommand`, `EnrolTotpCommand`, `RedeemRecoveryCodeCommand`, `PlatformLoginCommand`, `EnrolPlatformTotpCommand`, `RedeemPlatformRecoveryCodeCommand`) | marker interface + a `Door` getter | `AuthController`, `PlatformAuthController`, `relay-pairing-code` (sends `LoginCommand` itself) | unaffected — additive; JSON binding ignores a get-only `Door`. The relay pairing door now records too, which is correct: it is the same sign-in |
| 3 | Handlers of those six commands | where the refusals are decided | `LoginCommandHandlerTests`, `ClinicTotpAuthTests`, `PlatformAuthTests`, … | unaffected — no handler changes; the behavior reads the `Result.Code` they already return |
| 4 | `ISignInRefusalRecorder` (new), registered **Singleton** in `AddInfrastructure` | in-memory counters + log line + alert dispatch | console verbs build from `AddInfrastructure` alone | unaffected — they never send through the mediator, and a singleton is resolved lazily |
| 5 | `VendorAlertRecipients` | the vendor alert inbox rule | `RelayWatchJob` | unaffected — read, not changed. On prod `Backup:AlertEmail` is unset, so alerts go to the active console account |
| 6 | `ClinicAuthRefusals` / `PlatformAuthRefusals` codes | the strings the behavior classifies on | clients branch on them | unaffected — read only; a new test pins that the four shared codes stay equal on both sides |
| 7 | API log | one `WRN` line per refused sign-in | operator reading `docker logs` | unaffected — bounded by the auth rate limiter; e-mail masked, no password, no code |
| 8 | `LogTemplateCoverageTests` (placeholder names) | guard over every log statement | — | must re-test — the new template uses `{MaskedEmail}`, not a forbidden `{Email}` |

No endpoint, no migration, no rendering file → no browser pass (`verification-proportional-to-fix-size`).

## 2026-11-05 — the address is written in full (owner: « i want the mail not hidden »)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 9 | `SignInRefusalRules.MaskEmail` → `ShownEmail` | the one rule for how a typed address is written | `SignInRefusalBehavior` (its only producer), 4 test classes | must change — tests flip to « the address in full, nothing of what is not one » |
| 10 | `SignInRefusal.MaskedEmail` → `ShownEmail` | record field | recorder log line, alert « E-mail saisi », tests | must change — renamed in this change |
| 11 | Recorder log template `{MaskedEmail}` → `{Email}` | the durable log (hosted: `api_logs` volume, 30 days) | `LogTemplateCoverageTests` forbids `{Email}` | must change — one reasoned `AllowedByDesign` entry: a sign-in address is a staff, console or prospect account, never a patient |
| 12 | Alert e-mail « E-mail saisi » line | the vendor's inbox only | `SignInAlertNotice.Subject` | unaffected — the subject stays fixed, so the address never reaches a header |

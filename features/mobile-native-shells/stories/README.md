# The clinic on a phone — native Android & iOS shells · Implementation Stories

**Status:** APPROVED
**Plan:** [../plan.md](../plan.md) (APPROVED)
**Spec:** [../spec.md](../spec.md) (Challenged: Yes — AC-1 … AC-77)
**Blueprint:** [../blueprint.md](../blueprint.md) — ⚠️ superseded where it disagrees with the spec

## Summary

Two thin native shells (Kotlin/`WebView`, Swift/`WKWebView`) in a new top-level `mobile/`, each rendering the hosted
server's **own** web bundle — plus the web-side fixes a webview makes load-bearing, a client-version floor, sliding
session expiry, OS push, and two store listings.

**This breakdown is one story, deliberately.** `plan.md` encodes that decision (risk **R-1**) and `/break-plan` was
invoked with `one us`, so it is materialized as written rather than re-opened.

> ⚠️ **Departure from the single-layer (BE/FE) rule, stated rather than implied.** The one story is
> `Layer: Full` — it spans assets, domain, API, BFF, web client, two native toolchains, docs and store operations.
> Its **eight ordered parts** carry the internal structure the layer split would otherwise provide: each part is a
> *vertical* increment that compiles, passes its own gate and is independently shippable, and **each part boundary is
> a commit point**. If a session runs out, split at a boundary — never inside a part.

**No test plans exist** for this feature (`test-plan-e2e.md` / `test-plan-api.md` / `test-plan-integration.md` are
absent), so verification cites the spec's acceptance criteria and the repo's own gates directly rather than scenario
numbers. For `web/` that gate is `check:responsive` + `tsc --noEmit` + `build` + a recorded eye pass — there is no FE
test runner, no working ESLint and no CI.

## Part order

One story, so there are no story dependencies. The graph that matters is the story's **internal part order** —
each part is a vertical increment and each boundary is a commit point:

```mermaid
graph TD
    P1["Part 1 · Phase 0<br/>web fixes<br/>✅ EXECUTABLE"] --> P2["Part 2 · Phase 2b<br/>session continuity<br/>✅ EXECUTABLE"]
    P2 --> P3["Part 3 · Phase 2<br/>client version floor<br/>✅ EXECUTABLE"]
    P3 --> P4["Part 4 · Phase 1<br/>Android shell<br/>✅ IMPLEMENTED"]
    P4 --> P5["Part 5 · Phase 1<br/>iOS shell<br/>🔒 macOS + Apple"]
    P4 -.-> P6["Part 6 · Phase 3<br/>OS push<br/>✅ EXECUTABLE"]
    P5 --> P7["Part 7 · Phase 4<br/>native capability<br/>⚠️ PARTLY BLOCKED"]
    P6 --> P7
    P7 --> P8["Part 8 · Phase 5<br/>store submission<br/>🔒 accounts + decisions"]

    style P1 fill:#dcfce7,stroke:#16a34a
    style P2 fill:#dcfce7,stroke:#16a34a
    style P3 fill:#dcfce7,stroke:#16a34a
    style P4 fill:#dcfce7,stroke:#16a34a
    style P5 fill:#fee2e2,stroke:#dc2626
    style P6 fill:#dcfce7,stroke:#16a34a
    style P7 fill:#fef9c3,stroke:#ca8a04
    style P8 fill:#fee2e2,stroke:#dc2626
```

⚠️ **Part 3 → Part 4 is a real edge, not just ordering:** the Android shell reads
`GET /api/meta/client-requirements` natively at launch, so Part 3's endpoint must exist first.
**Part 7 needs both** Part 5 (its iOS halves) and Part 6 (a notification to tap for the deep-link criterion), which
is why it sits downstream of the two blocked parts even though its web and Android halves are executable.

⚠️ **Part 4 landed on 2026-08-06.** With Parts 1, 2, 3, 4, 6 and Part 7's shell-free halves in, the next executable
work is **Part 7's remaining steps** — `mobile/shared/bridge.md` now exists, so step 2's biometric contract can be
*amended* rather than invented, and steps 1/3/4 each have their Android half's home. Parts 5 and 8 stay blocked on
a Mac + Apple membership and on store accounts + the four deferred business decisions respectively.

⚠️ **Part 4 → Part 6 is drawn dotted because it is not a build dependency.** Push needs a shell to *register a
device token*, so the two must meet before AC-40 can be demonstrated end to end — but Part 6's whole backend half
(the `PushDevice` aggregate, registration, `PushDispatchJob`, quiet hours, the tenant declaration, `verify-schema`)
compiles and is testable with no shell in existence — which is how Part 6 landed *before* Part 4. Now that both are
in, the token-registration criteria are demonstrable end to end for the first time: `onPushToken` is registered and
inert in the Android bridge, and `window.__clinicShellDeliverPushToken` is the one line an FCM token has to reach.

## Status Tracker

**One story: [`story-1-full-clinic-on-a-phone.md`](./story-1-full-clinic-on-a-phone.md)** — `Layer: Full`, depends on
nothing, blocks nothing. There is no second story and no story-level dependency graph to draw.

### Part tracker (plan-time view)

The unit of progress is the **part**, not the story. This table is the plan-time snapshot; once implementation
starts, **`progress.md` carries the authoritative live part-status table** plus a per-part session log — the
convention `/next` defines for a single `Layer: Full` story delivered part-by-part, where every part shares one
`spec.md` / `plan.md` / `stories/` / `progress.md` and nothing is archived per part.

| Part | Slice | Name | Executable here? | ACs | Status |
|------|-------|------|------------------|-----|--------|
| 1 | Phase 0 | The web fixes a webview makes load-bearing | ✅ yes | AC-1…AC-12, AC-69 | **implemented** (gate green; on-device checks owed — `progress.md`) |
| 2 | Phase 2b | The session lasts the working day | ✅ yes | AC-35…AC-39 | **implemented** (gate green; the felt behaviour + desktop shell owed — `progress.md`) |
| 3 | Phase 2 | A stale app says so | ✅ yes | AC-28…AC-34, AC-70, AC-71 | **implemented** (gate green; AC-33's launch half is Part 4's code — `progress.md`) |
| 4 | Phase 1 | The Android shell | ✅ yes | AC-13…AC-27, AC-74, AC-76 | **implemented** (Gradle build + lint green with `warningsAsErrors`, debug **and** minified release APKs; the hardware walk and the bundle id are owed — `progress.md`) |
| 5 | Phase 1 | The iOS shell | 🔒 **BLOCKED** — macOS + Apple Developer Program | AC-13…AC-27 (iOS half) | blocked |
| 6 | Phase 3 | A backgrounded phone still knows | ✅ yes | AC-40…AC-55, AC-70…AC-73, AC-75 | **implemented** (web + backend gates green; the backend suite re-run, both console verbs and every device-token criterion owed — `progress.md`) |
| 7 | Phase 4 | The phone becomes an instrument | ⚠️ web + Android halves only | AC-8, AC-56…AC-64, AC-77 | **partly implemented** — the three shell-free halves landed 2026-08-06 (AC-62…64 · AC-8 · AC-77 + AC-56's web half); AC-57…AC-61 left unstarted with reasons in `progress.md` |
| 8 | Phase 5 | Two store listings | 🔒 **BLOCKED** — accounts + 4 deferred decisions | AC-65…AC-68 | blocked |

**Suggested first session: Part 1.** It depends on nothing — no hosted origin, no accounts, no Mac, none of the four
deferred business decisions — and it fixes four defects live in the browser today (iOS Safari downloads that silently
deliver nothing, blank PDF previews, printing the sidebar, a dead mic button on every iPhone).

> ### ⚠️ A dependency moved during this breakdown — verified in code, 2026-08-05
>
> `multi-tenant-cloud` **US-1 has landed**: `Infrastructure/Deployment/DeploymentProfile.cs` exists with **13
> capabilities**, `DeploymentProfileCoverageTests` and `DeploymentProfileTests` are both live, and `IsLocalMode(`
> survives only in its own definition, in `Resolve`'s back-compat derivation, and in one test. **US-2 has not**
> (no `ITenantScope`, no `SystemWideCallerCoverageTests`). Three effects:
>
> - **Part 6's blocker narrowed** from US-1 + US-2 to **US-2 alone**.
> - **AC-70's guard is live**, so Part 3 must ask a named capability — a new `IsLocalMode(` **fails the build today**.
> - **Part 7's connectivity work lost its API half and gained urgency.** `ConnectivityController:40` already gates on
>   `ExposesTrustEndpoints`, which is **✗ for `HostedMultiTenant`** — so the probe 404s there while
>   `connectivity.tsx` still polls on `AUTH_MODE === 'local'`. The defect the challenge predicted is **live in code**;
>   the remaining fix is web-side only.
>
> Re-verify both before starting Part 6 — they move on their own schedule, not this feature's.

## Blocked parts — what unblocks each

| Part | Blocked on | Who can unblock it |
|------|-----------|--------------------|
| ~~4~~ | ~~A JDK + Android SDK on the build machine~~ | ✅ **UNBLOCKED and IMPLEMENTED 2026-08-06 (sessions 5 + 6).** Toolchain: **Temurin JDK 17.0.20**, SDK **platforms;android-35 · build-tools 34.0.0 + 35.0.0 · platform-tools (adb 1.0.41)**; the shell pins **AGP 8.7.3 / Gradle 8.9 / Kotlin 2.0.21**. ⚠️ There was no `gradle` and no wrapper jar on the machine and Studio's wizard is a GUI step — session 6 downloaded Gradle 8.9, **verified its SHA-256**, and ran `gradle wrapper` once, so `mobile/android/gradlew` is now committed and nothing external is needed again. ⚠️ **Smart App Control is `Enforced` and never interfered**: Gradle runs through the signed Temurin `java.exe` and *produces* artifacts rather than executing them. ⚠️ **Still owed to finish Part 4:** a physical Android phone for step 7's hardware walk, and Part 8's bundle-identifier decision |
| 5 | macOS + Xcode (or Xcode Cloud / Codemagic) **and** an Apple Developer Program membership | Not solvable in this repo: win32, no CI, and the project **has never had an iOS device** |
| ~~6~~ | ~~`features/multi-tenant-cloud` **US-2** (`ITenantScope`) merged~~ | ✅ **UNBLOCKED, verified 2026-08-05 (session 2)** — `Application/Common/Interfaces/ITenantScope.cs` and `UnitTests/Common/SystemWideCallerCoverageTests.cs` both exist and the suite is green. Part 6's `PushDispatchJob` must declare `UseSystemWide(...)` or that guard fails the build |
| 8 | Store accounts + a public domain a reviewer can reach + the four deferred decisions (hosted domain · bundle ids and display name · demo-tenant data policy · store-account ownership) | Business/ops. ⚠️ A bundle identifier **cannot be changed after first submission** |

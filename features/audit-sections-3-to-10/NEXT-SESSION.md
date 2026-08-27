# Next session — resume prompt

Copy the fenced block below into a fresh Claude Code session.

---

```
/next audit-sections-3-to-10

START PART P5 (Build & tooling). P1, P2, P3, P4 and **P6 are COMPLETE** — P6 closed on
2026-07-29 (money truth & timezone, all nine steps, AC-P6.1–6.23, and with it the audit's
only 🔴, § 4.2). Read stories/progress.md's latest session log first; do not re-derive any
part's work list, and do not re-open anything they closed.

50 of 57 bullets are closed. What is left: **P5** (this session), **P7** (§ 6.4, the audit
trail — the largest remaining piece) and **P8** (§ 6.5, CNAM claims — still **blocked** on
Q-1…Q-6, and Q-3 is decisive).

P5's steps are in plan.md (« Part P5 »). Two ordering rules that are not negotiable:
 1. `TreatWarningsAsErrors` (step 8) is the **LAST thing in the entire story** — after P7.
    Do not land it in this session.
 2. Step 3 (`remove eslint.ignoreDuringBuilds`) only after step 2 has actually fixed or
    explicitly waived the existing violations, or the build breaks for everyone.

Step 1 is the one that unblocks a gate four parts have had to substitute for: `npm run
lint` has never run on a clean install (ESLint is not a dependency and `next.config.ts`
disables lint during build), so P3, P4 and P6 all used an ad-hoc unused-import scan
instead. Add `eslint` ^9.26.0 (the floor where the `eslint/config` subpath used by
`eslint.config.mjs` exists) and `eslint-config-next` pinned to the exact Next version
`15.5.9` — it ships in lockstep. Note that P6 added one `// eslint-disable-next-line
react-hooks/exhaustive-deps` (the BS1 estimate effect in `document-editor-content.tsx`,
which keys on a serialized cotation+date list on purpose); step 1 is the first time that
rule will actually run, so review it rather than assume it is fine.

Step 4 (CI) is **net-new scope** and is flagged as such in the plan. Confirm it with the
user before building it; if declined it moves to Out of Scope in the same edit. Without it
the ~85 tests this story has added never run again.

BASELINES (trust progress.md, not spec.md/plan.md):
- Full unit suite: **1402 passed / 0 failed**. FULLY GREEN — any red is ours.
- Frontend: `tsc --noEmit` clean, `npm run build` 27/27.
- Backend: 0 errors. Warnings are pre-existing and outside changed files.
- SAC not blocking. Use the OutDir + vstest workaround:
      dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/
      dotnet vstest <scratch>/ClinicManagement.UnitTests.dll
- A `dotnet build` failing ONLY with MSB3021/MSB3027 is a running API holding its DLLs, not
  a compile error. The message names the PID. Either stop it, or build the project you need
  to a scratch `-p:OutDir=` — P6 did the latter and never had to kill the user's API.

DO NOT REGRESS THE SCHEMA GATE. P5 adds no migration, so re-run it purely to prove that:
    cd api/ClinicManagement.API
    ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile -- verify-schema
It must stay **exit 0** (« schema matches the model »). Note it in the gate table as
*re-run, unchanged* — never as « not applicable », which is how a gate that does not exist
hides (see the P4 Learning).

THREE GUARDS TO KNOW BEFORE YOU EDIT TESTS:
- `RealtimeResourceResolverTests` is a reflection-derived, both-direction exact-set contract
  that PARSES `web/lib/realtime/clinic-hub.ts`. Any new feature area must be declared on
  both sides or it fails. Its two allow-lists are asserted empty on purpose.
- `AdminSurfaceCoverageTests` is reflective WITHIN its hardcoded `CatalogControllers` list.
  A new action on an existing catalog controller is covered free; a new *controller* is not.
  P6 added a `NonMutatingExemptions` list (one entry: the batch CNAM estimate, a POST that
  writes nothing) plus a test that every exemption still names a real action. Do not widen
  that list into a predicate.
- `MoneyReadConsistencyTests.Wire()` hand-mirrors repository SQL in LINQ (R-10). P6 proved
  the cost of that literally: the batched patient read in « Créances » turned that file's 8
  tests red until the fake was moved with it. Treat `Wire()` as part of the repositories'
  public contract — change both or neither.

WORKING TREE: `web/app/recalls/page.tsx` was already modified before P6 started and is NOT
part of this story. Leave it out of your commits.

Stage explicitly by path — never `git add -A`. No broad find/replace whose left-hand side
is shorter than the construct it belongs to (this has cost two sessions already).
```

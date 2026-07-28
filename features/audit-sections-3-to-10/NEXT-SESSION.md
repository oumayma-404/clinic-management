# Next session — resume prompt

Copy the fenced block below into a fresh Claude Code session.

---

```
/next audit-sections-3-to-10

START PART P5 (Build & tooling). P1, P2, P3 and **P4 are COMPLETE** — P4 closed in two
halves, the schema half in `c40ce8f` and the six code-only items in the commit after it.
Read stories/progress.md's latest session log first; do not re-derive P4's work list, and
do not re-open anything it closed.

P5's steps are in plan.md (« Part P5 »). Two ordering rules that are not negotiable:
 1. `TreatWarningsAsErrors` (step 8) is the **LAST thing in the entire story** — after P6,
    P7 and P8. Do not land it in this session.
 2. Step 3 (`remove eslint.ignoreDuringBuilds`) only after step 2 has actually fixed or
    explicitly waived the existing violations, or the build breaks for everyone.

Step 1 is the one that unblocks a gate three parts have had to substitute for: `npm run
lint` has never run on a clean install (ESLint is not a dependency and `next.config.ts`
disables lint during build), so P3 and P4 both used an ad-hoc unused-import scan instead.
Add `eslint` ^9.26.0 (the floor where the `eslint/config` subpath used by
`eslint.config.mjs` exists) and `eslint-config-next` pinned to the exact Next version
`15.5.9` — it ships in lockstep.

Step 4 (CI) is **net-new scope** and is flagged as such in the plan. Confirm it with the
user before building it; if declined it moves to Out of Scope in the same edit. Without it
the ~60 tests this story has added never run again.

BASELINES (trust progress.md, not spec.md/plan.md):
- Backend warnings: 56, all pre-existing outside changed files. 0 errors.
- Full unit suite: 1286 passed / 0 failed. FULLY GREEN — any red is ours.
- Frontend: tsc clean, `npm run build` 27/27.
- SAC not blocking. Use the OutDir + vstest workaround:
      dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/
      dotnet vstest <scratch>/ClinicManagement.UnitTests.dll
- A `dotnet build` failing ONLY with MSB3021/MSB3027 is a running API holding its DLLs, not
  a compile error. The message names the PID; kill it and rebuild.

DO NOT REGRESS THE SCHEMA GATE. P5 adds no migration, so re-run it purely to prove that:
    cd api/ClinicManagement.API
    ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile -- verify-schema
It must stay **exit 0**. Note it in the gate table as *re-run, unchanged* — never as « not
applicable », which is how a gate that does not exist hides (see the P4 Learning).

TWO GUARDS TO KNOW BEFORE YOU EDIT TESTS:
- `RealtimeResourceResolverTests` is now a reflection-derived, both-direction exact-set
  contract that PARSES `web/lib/realtime/clinic-hub.ts`. Any new feature area must be
  declared on both sides or it fails. Its two allow-lists are asserted empty on purpose.
- `AdminSurfaceCoverageTests` is reflective WITHIN its hardcoded `CatalogControllers` list.
  A new action on an existing catalog controller is covered free; a new *controller* is not.

WORKING TREE: four files unrelated to this story were already modified and must stay out of
your commits — `LoginResultDto.cs`, `LoginCommand.cs`, `web/app/bff/auth/local-login/route.ts`,
`web/app/bff/auth/token/route.ts` (a `RefreshExpiresAt` / BFF-cookie-lifetime change).

Stage explicitly by path — never `git add -A`. No broad find/replace whose left-hand side
is shorter than the construct it belongs to.
```

---

## Open items carried forward (not P5's, but do not lose them)

| Item | Where it belongs | Note |
|---|---|---|
| **A human pass at 375 px and by keyboard** | before `/review-feature` | P3's walk (AC-P3.48) was **static** — markup-level only. Stated plainly in progress.md per plan risk **R-9**; it is not equivalent to a browser pass and there is no frontend test runner. |
| **Calendar row-trim** (AC-P1.33) | follow-up, not a part | The shading is correct per-day, but the grid still renders rows 0..23. Re-basing needs the appointment overlay reworked (it positions blocks from midnight against a fixed `HOUR_HEIGHT`). Deferred deliberately in P1. |
| **`npm run lint`** | P5 step 1 | Cannot run at all today — ESLint is not a dependency and `next.config.ts` disables lint during build. P3 and P4 substituted an ad-hoc unused-import scan. |
| **Wide tables + the calendar grid at 375 px** | Out of Scope (spec) | They squeeze rather than overflow, so AC-P3.14 holds (the *body* never scrolls). A real responsive pass over them is explicitly excluded. |
| **P8's Q-1 … Q-6** | blocks P8's start | Q-3 is decisive: `follow-up/cnam-conventionné-bordereau.md` records the bordereau as a conventionné / tiers-payant pathway, so if this clinic is filière privée, P8 has no user and should follow patient merge into `follow-up/`. |
| **Check what a guard DERIVES before trusting a one-line summary of it** | any future part | R-14 says `AdminSurfaceCoverageTests` is "a hardcoded array — add by hand". Its hardcoded part is the *controller list*; the rule inside them is reflective, so P4's new catalog action was covered for free. The mirror image of the `verify-schema` lesson below — in either direction, read the guard. |
| **A gate named in a plan must be proven to run** | any future part | `verify-schema` was cited as "the gate" by P1, P2 and P3 (two of them writing « not applicable ») while it did not exist. See the Learning in progress.md: *"not applicable" and "not implemented" look identical in a table.* |

---

## State at the end of the last session

**Branch:** `feature/audit-sections-3-to-10`, off `feature/windows-desktop-app` @ `1932acf`.

| Part | Status |
|---|---|
| **P1** Appointment lifecycle & booking | **complete**, migrations included (`6906f83`) |
| **P2** Finish what's built | **complete** (AC-P2.1–2.45) |
| **P3** UX, accessibility & French | **complete** (AC-P3.1–3.54) |
| **`verify-schema`** (P4's gate) | **complete** (`edecd23`) — model-driven; **exit 0** |
| **P4** Stock, realtime & schema | **complete** — all 11 steps, AC-P4.1–4.43. Schema half in `c40ce8f`, the six code-only items in the commit after it |
| P5 Build & tooling | **not-started ← start next.** Its `TreatWarningsAsErrors` step is the LAST thing in the whole story; step 4 (CI) is net-new scope and needs the user's decision |
| P6 Money truth & timezone | not-started |
| P7 Audit trail, dup prevention, anonymize | not-started (P7a first; `confirm-by-typing-dialog.tsx` is already in place for AC-P3.47) |
| P8 CNAM claims & reconciliation | **blocked** on Q-1 … Q-6 |

**Closed so far: 43 / 57 bullets**, plus adjacent defects A-1 … A-19.

**Gates at the end of the session:** backend build 0 errors / 56 warnings / **0 in changed files** ·
full unit suite **1286 passed, 0 failed** (+11 net: +22 new, −11 retired with the old `[InlineData]` realtime
table) · **`verify-schema` exit 0** — re-run to prove it had *not* moved, since P4's second half added no
migration · `tsc --noEmit` clean · `npm run build` clean at 27/27 · `npm run lint` still cannot run (P5 step 1),
substituted by an ad-hoc unused-import scan over all 12 changed frontend files, 0 unused.

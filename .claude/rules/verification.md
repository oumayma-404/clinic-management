# Verification Rules — one full pass, not a loop

Directives for verifying any change in this repo, by any means: unit tests, `check:responsive`, a Playwright
walk, a manual eye pass. Sibling of [`frontend-web.md`](frontend-web.md), which says *what* a rendered result
must satisfy; this says *how to find out whether it does* without burning an afternoon.

## § 0 The governing rule

> **Test everything at once. Collect every failure. Fix them all. Then run the same pass again.**
>
> Never: check one thing → find a problem → fix it → re-launch → check the next thing. That loop is slower and
> less accurate than one full pass, and it is banned.

It is slower because the setup is the expensive part and the loop pays it N times: in this app a browser launch
costs ~20 s of auth/session warm-up plus 15–20 s per page settle, so ten one-check launches is most of an hour
of pure overhead.

It is **less accurate** for three reasons that matter more than the time:

- A fix aimed at finding #1 can create finding #4, and a loop never observes it.
- Findings only make sense next to each other. Four of this repo's worst defects were one chain — a dropped
  field, a DTO with no such property, a guard whose band the seed already violated, and an orphaned constant —
  and each looked unrelated and minor alone.
- A loop hides accumulation. A `--filter`ed test run stayed green for a whole feature while **21 failures**
  across 4 causes piled up behind it, one of them a real regression.

## § 1 Write the walk to collect, not to stop

```js
const findings = []
const ok   = (id, m) => console.log(`  ✅ [${id}] ${m}`)
const bad  = (id, m, d = '') => { console.log(`  ❌ [${id}] ${m} ${d}`); findings.push({ id, m, d }) }
const skip = (id, why)      => { console.log(`  ⏭  [${id}] not exercised — ${why}`); findings.push({ id, why }) }
// …every area the change touched, in one launch…
findings.length ? findings.forEach(f => console.log(f)) : console.log('ALL CLEAR')
```

- **No check throws and no check returns early.** One dead selector must not end the run.
- **Three outcomes, never two:** pass · fail · *not exercised, with the reason*. « I could not reach this »
  reported as « fine » is the worst output a verification pass can produce.
- **Re-run the same script** after fixing. Writing a new narrower one loses the coverage you just paid for.

## § 2 Triage probe-vs-product before fixing anything

**A failing check is a claim about your probe until you have excluded that.** Measured on one real run: **5 of
7 "failures" were the probe**, not the app. The five, each worth knowing:

| Symptom | Actual cause |
|---|---|
| Confirmation dialog "has no text" | A confirm is `role="alertdialog"`; the selector list only had `[role="dialog"]` |
| "No steps control on any row" | The control is named « Modifier les 4 **étapes** de … » and the regex matched « séances » |
| Search filtered nothing | `getByPlaceholder(/Rechercher/i)` also matches the header's global patient search |
| Toast "not shown" | sonner had already dismissed it — poll for the text, don't `waitForSelector` once |
| Three passing checks turned red | The page was still on « Chargement… »; the API had just cold-started |

**The discriminator:** a finding that contradicts a check which passed minutes ago, on unchanged code, is a
probe bug. Prove it before you touch source. Read the screenshot — `Read` the PNG — rather than trusting the
text dump.

## § 3 Assert positively, and log what you captured

`if (!/Erreur|introuvable/.test(text))` is **not** "it loaded" — it reported a loading spinner as a success and
sent three good checks red. Assert on the value you expect to *be there*, and print the captured text so a red
result is readable without a re-run.

## § 4 Run the whole suite, never a filter

```bash
# ✅ the gate
BaseOutputPath=<temp> dotnet test api/ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -c Release
# ❌ never the gate
… --filter "FullyQualifiedName~TheThingIJustTouched"
```

4 000+ tests run in ~30 s here, so there is no reason to narrow. A filter is for iterating on one failure
*inside* a full-suite red, never for deciding you are done. `--filter` green means nothing about what else moved.

The frontend equivalent is the whole of `.claude/rules/frontend-web.md` § 14 — `check:responsive` **and**
`tsc --noEmit` **and** `npm run build`, all three, every time; and re-run them after the *last* edit, not after
the second-to-last.

## § 5 Reach for the cheapest ground truth first

Before re-launching a browser to answer a question, ask whether something cheaper is decisive:

- **Read-only SQL** against the dev database settled « did the save persist, and did it keep the field? » in one
  query — with the stored JSON showing both the edit and the preserved interval, which the UI could only imply.
- **The API** answers most "is it on the wire" questions.
- **The source** answers "is this reachable at all" (an orphaned constant, a call site that never runs).

A browser is for what only a browser can see: layout, overflow, gestures, focus, what is actually painted.

## § 6 Verify the environment before believing any result

A green or red result against a stale process is noise. In this repo, specifically:

- The running API serves `bin/Debug`; building Release to a temp `BaseOutputPath` **does not reach it**. Stop
  the process, rebuild, restart — and stop it by the PID owning the port, not `taskkill /IM dotnet.exe`, which
  also kills the build servers:
  ```powershell
  Get-NetTCPConnection -LocalPort 5000 -State Listen | Select-Object -First 1 -ExpandProperty OwningProcess
  ```
- A migration must be applied before a walk can exercise the column it adds.
- A cold-started API needs its first request absorbed before timings mean anything (EF model build + JIT).
- `web/.next` build failures are usually a concurrent `next dev`, whatever the error says.

## § 7 Mutation discipline during a walk

- **Mutate through the product**, so what ships is what was tested; use SQL for *reads* and for reverting your
  own test artefacts (say so when you do).
- **Money and clinical records are read-only** in a verification pass unless the change under test is
  specifically about writing them — do not press « Confirmer la séance », « Facturer » or « Arrêter le
  traitement » to satisfy a check. Report the case as *not exercised* with the reason instead (§ 1).
- Never sign in from a script: the account locks after 5 attempts on a 15-minute sliding window. Reuse a stored
  `storageState`, and never fetch `/bff/auth/token` from a script — it rotates the credential.

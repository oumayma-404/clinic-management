# `e2e/` — the hot-path gate

The money, fiche and devis paths, exercised end to end. **~125 tests across 14 spec files**; scenario ids match
[`features/e2e-hot-paths/scenarios.md`](../features/e2e-hot-paths/scenarios.md), so a red line in the log points
straight at the row describing it.

## Why this exists at all

Two gates already run on every push, and **neither can see any of this**.

- **`api`'s unit suite structurally cannot.** `api/CLAUDE.md` says it plainly: nothing in `UnitTests` touches a
  database. Every tier-0 scenario here is « this figure is right on the screen you touched and wrong on the four
  you did not » — a fiche's payment reaching la caisse on the *séance's* own day, a plan dropped whole from
  « Créances » by the devis→note bridge, an odontogram asserting an implant from a step 1 of 2. Those are facts
  about the reads over the database, and the only way to hold them is to run the product.
- **`web`'s gate cannot either.** `tsc --noEmit` + `check:responsive` + `next build` all passed while every
  `Completed` treatment plan threw during render and its workspace showed **nothing**. Only a browser saw it.
  That is `PLAN-01`.

## Running it locally

Needs the stack up: Docker (postgres + minio), the API on :5000, `web` on :3000.

```bash
cd e2e
npm install
npx playwright install chromium      # once; the local run prefers real Chrome via `channel`
npx playwright test                  # the whole gate
npx playwright test --grep @t0       # tier-0 only
npx playwright test specs/fiche-money.spec.ts
npx playwright show-report .artifacts/report
```

### Credentials

⚠️ **None are in this repository, on purpose.** This codebase's own convention is explicit — `appsettings.json`
carries « SECRET — do NOT commit a value here » — and a password plus a TOTP secret in a git history is
permanent, survives a change of repository visibility, and here belongs to an **admin** on a database of
real-shaped patient data. `lib/env.ts` therefore reads them, in order, from:

1. **the environment** — `CLINIC_EMAIL`, `CLINIC_PASSWORD`, `CLINIC_TOTP_SECRET`, `CLINIC_ID`. What CI supplies.
2. **`e2e/credentials.local.json`** — git-ignored. `cp credentials.local.example.json credentials.local.json`
   and fill it in.

With neither, the suite **fails at setup with these instructions** rather than signing in as nobody and
reporting a green run. On a developer machine the values are the throwaway dev admin recorded in
`~/.claude/skills/clinic-browser/SKILL.md`.

### ⚠️ Loosen the rate limiter, or the tail of a full run 429s

The auth window is **30 attempts per five minutes per account**, and the browser half exchanges a token
(`/bff/auth/token` → `/api/auth/refresh`, an `/api/auth/*` call) on **every navigation**. A full pass passes 30
in about ninety seconds, and the 429 that follows reads exactly like a broken endpoint. Measured: `CAT-05` and
`STEP-04/05` failed at the end of a full run and both passed in isolation.

`RateLimiting.cs` exposes the keys for precisely this — « so an unusually busy cabinet can be loosened ». Start
the API with:

```bash
RateLimiting__Auth__PermitLimit=2000 \
RateLimiting__Auth__AddressPermitLimit=5000 \
RateLimiting__Api__PermitLimit=5000 \
  dotnet run --project api/ClinicManagement.API
```

CI's `e2e` job sets the same three. `ClinicApi` also honours `Retry-After` on top, so a genuine limit is still
backed off rather than hidden — but the burst is an artefact of the harness, not of the product, and it should
not be the thing under test.

### Afterwards, on a shared database

```bash
docker exec -i clinic-postgres psql -U clinic_user -d clinic_management < cleanup.sql
```

A full pass creates ~65 fixture patients with their invoices, payments and plans, and those land in the **same**
caisse, « Créances » and dashboard you read all day. One run is a nuisance; five is 300 patients and the day's
figures stop meaning anything. CI bootstraps an empty database per run, so it never needs this.

## Running it against a fresh database (what CI does)

```bash
# 1. Start the API — this is also what APPLIES THE MIGRATIONS.
#    ⚠️ Order matters: `provision-clinic` intercepts before the web host boots, so run first on an empty
#       database it fails with « relation "Users" does not exist ».
dotnet run --project api/ClinicManagement.API

# 2. A clinic and its first administrator, through the product's own operator verb.
dotnet run --project api/ClinicManagement.API -- provision-clinic \
    --name "Cabinet E2E" --admin-email e2e.runner@cabinet-e2e.tn --admin-name "Dr E2E"

# 3. Enrol the second factor, spend the one-time password, seed the catalogue.
node e2e/bootstrap.mjs --temp-password '<the printed password>'
#    → prints CLINIC_EMAIL / CLINIC_PASSWORD / CLINIC_TOTP_SECRET / CLINIC_ID
```

⚠️ Step 3 takes ~90 s **on purpose**: a TOTP code is single-use and the auth endpoints are rate-limited, so each
step waits for a fresh 30-second window instead of retrying inside one.

## Read-only mode — the deploy smoke

```bash
CLINIC_E2E_READONLY=1 \
CLINIC_ORIGIN=https://… CLINIC_API=https://…/api \
CLINIC_EMAIL=… CLINIC_PASSWORD=… CLINIC_TOTP_SECRET=… \
  npx playwright test
```

Skips every `@mutating` spec — most of the suite — because a practice's real ledger is not a fixture. What runs
is `smoke-readonly.spec.ts` plus the `XCUT-09` route checks: does every hot-path screen render *inside the
application*, does it raise no render crash, does the page scroller still clip its own absolutes, and do the
money reads **agree with each other**. Compared read-against-read, never against a literal, so it works on any
database with any amount of real money in it.

`deploy-hosted.yml` runs this after `/health` and `verify-schema`, as a **warning, not a gate**: by then the
deploy is already serving, so failing the workflow rolls nothing back.

## How a test is built

**Arrange over the API · act in the browser · assert on the coupled reads.**

```
api.startTreatment(…)              arrange — the wire, because arranging « a plan bridged to a live
arrange.fiche(…)                             note that has collected 800 of 1 000 » through the UI is a
                                             dozen dialogs, and the scenario under test would be consumed
                                             by its own setup

gotoApp(page, '/patients/…')        act — always the real gesture. Half the defects this suite exists for
page.getByLabel(…).fill(…)                live entirely in the client: a field the server also imposes,
                                          a `plan.items[0]`, a `setError` that poisons a form for good

api.billingSummary(patientId)       assert — on the FIGURE, on every surface that should have moved,
api.receivables(); api.caisse()             never on a toast
```

## Things that will bite you

Each cost real time in the pass that produced this suite; each is also documented at its call site.

| | |
|---|---|
| `workers: 1` is **load-bearing** | half of what is asserted is a clinic-wide total; two tests billing concurrently make each other's expected figure wrong. Parallelising does not make it fast, it makes it lie |
| One browser **page** per worker | the BFF rotates its refresh cookie on every `/bff/auth/token` and `PreviousCredentialHash` **detects** a replay rather than forgiving it. Two holders of the original cookie end the session ~20 s in — green early, « element not found » late |
| Never `page.goto` directly | use `gotoApp()`. On the login page most assertions pass **vacuously**: it is short, does not scroll, and raises no console error. Seven `XCUT-09` checks once reported green from there |
| Never name a catalogue act or its tarif | use `protocolAct` / `singleSeanceAct` / `procedureWhere`. Seven tests green on the developer's catalogue failed on a freshly seeded one, all of them looking like product defects |
| `/odontogram` is a **bare array** | not `{ items }`. `chart.items ?? []` makes « nothing is charted » always true |
| The caisse window is `fromDay`/`toDay` | **an unknown query parameter is ignored, never refused**, so `fromDate` silently returns *today* |
| The ledger field is `movements` | not `items` |
| `treatmentPlanId` is required **beside** `treatmentPlanItemId` | and its refusal is the *same sentence* the real `plan.items[0]` defect produces |
| A collapsed act card renders **no** price input | only the armed one does; counting inputs measures how many cards are open |
| A 429 is the limiter working | `ClinicApi` backs off on `Retry-After`. Do not shorten it |
| Don't debug against a long-lived `next dev` | one up 11½ hours served `Jest worker encountered 2 child process exceptions` on every route. CI runs a production build |
| Never run two Playwright processes at once | they share `outputDir`, so one deletes the traces the other is writing and the second reports `ENOENT` failures that have nothing to do with the tests. Three « failures » of a 14-minute run were exactly this |

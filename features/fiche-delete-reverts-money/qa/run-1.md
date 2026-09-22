# Run 1 — VOID (environment)

**Status: `VOID` — no product claim can be made from this run.**

## Environment

- API `:5000` pid 26252 (owned by `clinic-management-20`), started 23:31 — **stale assembly**, see below.
- web `:3000` pid 37080 (owned by `clinic-management-20`), started 22:55.
- Peers live: `clinic-management-20`, `clinic-management-d6`, `investigate-phone`, `clinic-management-bf`,
  `clinic-management-8b`, `anakin-28`, `anakin-9c`.
- Browser profile claimed 23:00, released 23:0x for `clinic-management-20`.

## Why it is void

Every write route answered HTTP 400 with its handler's **generic catch-all** sentence:

| Route | Answer |
|---|---|
| `POST /api/treatment-plans` | « Erreur lors de la création du plan de traitement. » |
| `POST /api/treatment-plans/start` | « Erreur lors de la création du traitement. » |
| `POST /api/patients/{id}/dental-records` | 400 — including a fiche with no money and no devis link |

`POST /api/patients` worked, and EF migrations were **in sync**
(`20260921225840_AddPatientAdditionalPhoneNumbers` applied, matching the tree), so it was neither schema
drift nor auth.

**Cause, found by `clinic-management-20` in the API's own console:**

```
Npgsql.PostgresException 42703: column p0.Label does not exist
LEFT JOIN "PatientPhoneNumbers" AS p0 … SELECT … p0."Label" …
```

The running process was compiled while `Label` was still on the entity; `clinic-management-d6` removed the
« libellé » field minutes later. Source consistent, **loaded assembly not**. Any query touching `Patients`
with its phone collection threw, and the handlers' catch-all flattened it into a French 400.

⚠️ **This is `verification.md` § 6 exactly** — « a green or red result against a stale process is noise ». It
is also the reason the rule says to check the environment *before* the probe and the probe before the source:
a run where most rows fail is one environment claim, not N product claims.

## Outcome

| id | Outcome | Note |
|---|---|---|
| A6 | ⏭ not exercised | no money-bearing fiche could be created |
| A1–C4 | ⏭ not exercised | the delete dialog was unreachable — the records tab had no row to delete |
| everything else | ⏭ not exercised | blocked upstream |

**Zero findings about the product.** Nothing here says anything about
`DentalRecordDeletionReversal` or the new confirmation, in either direction.

## Probe bugs fixed during the run (not source changes)

1. `import { chromium } from "@playwright/test"` — the package is CommonJS; named ESM exports are unavailable.
2. The API wraps `Result<T>` as `{ value, isSuccess, isFailure, error, code, diagnostic }` — **six** keys, so
   the first unwrap guard (`keys.length <= 3`) never fired and the login token read as missing on a 200.
3. The fallback target block was inserted after the check that consumed it, so `FALLBACK_RECORD` was ignored.

## Next

`clinic-management-20` is restarting `:5000`. Re-drive the **whole** plan as run 2 once it is up — not only
the rows that failed.

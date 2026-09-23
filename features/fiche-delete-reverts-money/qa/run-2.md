# Run 2 — PARTIAL. API layer green, eye pass NOT done.

**Status: `PARTIAL` — 1 scenario passed, 0 product findings, the eye pass is still owed.**

## Environment

- API `:5000` pid **19840**, started 00:06 — fresh build, verified to carry every changed file
  (`Fiche de soins du`, `chèque déjà marqué`, `no-money-without-a-fiche`, `deletion-preview` all present in
  the served assemblies).
- web `:3000` pid 37080 — **`.next` was rewritten mid-drive** by `clinic-management-20`'s `npm run build`,
  which voided one drive.
- Browser profile: claimed, then taken by a peer for the owner to look at the app directly.

## What passed

| id | Scenario | Layer | Result |
|---|---|---|---|
| A6 | The deletion-preview endpoint reports the money truthfully | api | ✅ |

Arranged and measured three times, consistently. On a fiche that collected 80,000 DT at the chair against
devis `2026-0528`:

```json
{"touchesMoney":true,"totalReversed":80,
 "plans":[{"number":"2026-0528","title":"Extraction simple","amount":80}],
 "note":null,"affectedCaisseDays":["2026-09-22T00:00:00Z"],
 "documentsKept":0,"refusal":null}
```

That is the whole contract the warning box renders from: the amount, the devis that holds it, and **the day
the money was received** rather than today. The arrange also proves the un-numbered-treatment path — the
devis is created as a Draft and the first chairside collection mints its number (`2026-0526`, `0527`, `0528`
across the three runs), which is `CollectOnTreatmentCommand`'s documented behaviour.

## What is still owed

| id | Scenario | Why not exercised |
|---|---|---|
| A1–A5, B1–B5, C1–C4, D1–D6 | every browser row, **including the eye pass at 320/390/820/1180/1440** | the profile was taken for the owner before a clean drive landed |

⚠️ **The eye pass has not happened.** No screenshot of the new dialog exists at any width. Nothing in this
report says the warning box fits at 320 px, and this repo's own record is that `tsc`, `check:responsive` and
`npm run build` were all green while a dialog ran past the screen edge.

## Probe bugs fixed (not source changes)

Three drives died on the probe. None was the product:

| # | Symptom | Cause |
|---|---|---|
| 1 | `SyntaxError: Named export 'chromium' not found` | `@playwright/test` is CommonJS — no named ESM exports |
| 2 | « no token in login body » on an HTTP **200** | the `Result<T>` envelope has **six** keys (`value, isSuccess, isFailure, error, code, diagnostic`); the unwrap guard tested `keys.length <= 3` |
| 3 | « no delete control found on the records tab », twice | the tab is **`medical-records`**; `?tab=records` is silently ignored and lands on the default tab. Also: « Supprimer la fiche de soins » is a `DropdownMenuItem` behind a « Autres actions sur la fiche du … » trigger, not a row button — and the list renders its table **and** its card form at once, so `:visible` is required |

All three are fixed in `walk.mjs`. The next drive should reach the dialog first try.

## Test data left behind (mine only, to clean up)

Patients `QA-Suppression Fiche…` — `752036`, `871853`, `111320`, `321818`, `992001` — plus two
`QA-Probe Plan…`. The later ones carry a devis and three fiches each; the earlier ones are inert.
No pre-existing clinical or money row was touched.

## Next

Re-drive the **whole** plan as run 3 when the profile frees. One drive should do it.

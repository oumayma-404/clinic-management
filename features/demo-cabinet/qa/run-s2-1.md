# QA run — story 2, cycle 1

**Plan:** `qa/plan-2.md` · **Driver:** `qa/walk-2.mjs` + `qa/walk.mjs` (story 1, tier D2)
**Environment:** isolated stack — API :5099 `PublicDemo` on a fresh `clinic_demo_local` (rebuilt on start), web :3099
`next dev`; fresh Chrome contexts via `playwright-core`. Shared `:5000/:3000` stack and Playwright MCP profile not used.
D2 re-run with `Demo__SessionMinutes=2` (the expiry row needs a short cap).

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | band « Cabinet de démonstration · rien n'est enregistré » + « Créer mon cabinet gratuitement » → signup, `_blank` |
| A2 | ✅ | 390: « Démo · rien n'est enregistré » + « Créer mon cabinet » |
| A3 | ✅ | patient dialog: neutral notice `role=status` + link, input « Rachel Green » kept (`shots/A3-patient-refused-1440.png`) |
| A4 | ✅ | second press: one notice, no toast |
| A5 | ✅ | settings: one neutral toast (not `error`) + « Créer mon cabinet » (`shots/A5-settings-toast-settled-1440.png`) |
| A6 | ✅ | three presses → one toast; form stays in edit mode |
| B1 | ✅ | `POST /api/patients` → 403 `demo_read_only`, exact sentence |
| B2 | ✅ | `GET /api/patients/export` → 403 `demo_read_only` |
| B3 | ✅ | `GET /api/patients` → 200 |
| C1 | ✅ | 320×640 57 px (2 lines) · 390×844 33 · 820×1180 57 · 1180×820 33 · 1440×900 33 · 1536×730 33; header below, no h-scroll |
| C2 | ✅ | hidden in print media |
| C3 | ✅ | coarse pointer: CTA 44 px |
| D1 | ✅ | empty patient save → 8 validation marks, no demo notice |
| D2 | ✅ | story-1 walk: 19 ✅, D1 ⏭ (no hosted API) — after one probe fix, see below |
| D3 | ✅ | `SELECT count(*) FROM "IdempotencyRecords"` → 0 |
| D4 | ⏭ | no HostedMultiTenant stack; covered by `DemoReadOnlyMiddlewareTests.Another_Deployment_Is_Untouched` |

## Findings

None in the product.

## Probe fixes (same cycle, no source change)

- D2/B4 first red twice: (1) the API ran with the default 2 h cap — restarted with `Demo__SessionMinutes=2`; (2)
  `isVisible()` was a one-shot check right after `waitForURL`; the screenshot showed the line. Now `waitFor`.
- A5's first screenshot caught the toast mid-entry under the band; settled position is y=32 below the 32 px band.

## Observations (off-plan)

- Next's dev overlay counts « 1 Issue » on each refusal: the dialogs `console.error` the failed save. Dev-only; no
  overlay in a production build.
- `/settings` logs 8 × 404 on load (absent logo/letterhead/etc. on a cabinet that has none) — pre-existing.
- Two 401s right after entry (token warm-up) — pre-existing, seen in story 1.

**Status:** GREEN (17 ✅, 1 ⏭ with reason)

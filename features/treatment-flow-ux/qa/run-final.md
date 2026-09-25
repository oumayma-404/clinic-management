# QA — pre-push regression pass (2026-09-25)

**Build under test:** production build (standalone, `next build`) of the working tree = commit e551ec57 + the
booking/fiche fixes 5–7, served on :3000. API/DB shared, peers idle.

| Check | Result |
|---|---|
| Backend unit tests (unfiltered, Release) | 4 842 passed · 0 failed · 6 skipped |
| `check:responsive` · `tsc --noEmit` · `next build` | 72/72 · clean · green |
| `e2e/scripts/check-coverage.mjs` | 64 tier-0 untested — identical to HEAD, not this change |
| e2e hot-path suite, 135 tests (quiet run) | 133 passed · 0 failed · 2 skipped (by design). A run beside the walks timed out twice under load; the quiet rerun is the gate |
| walk.mjs (plan 1) run6 | 84 ✅ · D1 red = the known agenda-menu probe |
| walk-2.mjs (plan 2) gap6 | 105 ✅ · 3 red, all fixture state: F1.plain already had a treatment from the previous run's secretary booking, so « Couronne » resolves to the devis act (screenshot C3-booking-coarse-390) |
| tour.mjs --dry (40 steps) | all set up · 0 pageerror |
| regress-fix7.mjs (the six review fixes) | ALL CLEAR, 19 checks |
| Code review of fixes 5–6 | 6 findings → all fixed in fix 7 |

**Pre-existing, not this change (measured on 5b430580 with `probe-back-guard.mjs`):** the browser Back closes a
typed booking, and a fiche opened by `?addRecord`, without « Abandonner les modifications ? ». The patient form asks.
Same result before and after this work.

**Data written:** throwaway « QAU… / QAG… / E2E … » patients only; the secretary accounts are deactivated.

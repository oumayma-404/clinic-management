# QA run 9 — Congés · the paths one day cannot show (K8–K14, M1, A11's figure)

**Date:** 2026-11-07 · demo stack (`clinic_pointage_demo`, local) · Chrome 9335 / 9336 · **Status:** RED — environment and
probe only.

| Rows | Outcome | Why |
|---|---|---|
| K8–K13 (new) | ✅ 19 | monthly rule from January (11 credits, one line each), a rule cancelled, 31/12 « perdus » (2025 read back 20 · 5 · 15 · 0), « reportés » (Report de 2025 · 15 j), a debt carried (−3 j), her page following with no reload |
| A7 → A22, F4, D3, K1 (cascade) | ❌ 26 | the first walk after `next dev` was restarted: `/users` compiled on first visit for longer than A7's 8 s wait, so R1 was never accepted and every row reading it went red |
| K14 | ❌ 3 | `Emulation.setEmulatedMedia` does not move `(pointer)`: everything measured the mouse sizes (32 / 36 px) |
| M1 | ✅ | see progress.md — and `verify-schema` caught `Days numeric(5,1)` against the `(18,3)` convention, fixed in the migration |

**Probe / environment fixes (walk.mjs):** every route the walk opens is visited once before the first row; K14 uses
`Emulation.setTouchEmulationEnabled` and asserts `matchMedia('(pointer: coarse)')` before measuring. **Re-run:** run-10.md.

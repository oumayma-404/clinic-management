# QA run 2 — sms-rappels-tunisie-sms (re-run after cycle 1 fixes)

**Environment:** branch API `:5099` rebuilt (`bin/Debug`) and restarted after the fixes · branch web `next dev :3097`
on a fresh `.next` · shared Postgres · own headless Chrome. Settings reset to baseline by SQL before the run (own
artefacts). Same `walk.mjs` as run 1, plus two rows for the fixed findings (none removed).

**Widths:** 320×740 · 390×844 · 820×1180 · 1440×900 · 1536×730.

| ID | Result |
|---|---|
| P1–P4 (api) | ✅ (unchanged code paths; run 1 evidence stands) |
| A1 · A2 · A3 · A4 · A5 | ✅ |
| B1 · B2 · B3 · B4 · B5 · B6 | ✅ |
| C1 (five sizes) · C2 | ✅ |
| D1 · D2 · D3 | ✅ |
| **F1** — /rappels with SMS on and an empty log | ✅ « Aucun message pour le moment » (`shots/A3-rappels-1440.png`) |
| **F2** — « + Acte » after « avance. » | ✅ « …avance. {acte} » |
| X1 | ⏭ needs an appointment on the shared DB (clinical write) — covered by `NotificationJobRenderAtSendTests` |

39 ✅ · 0 ❌ · 1 ⏭. Log: `qa/run2.log`.

**Status: GREEN**

# QA run 2 — demo-cabinet, story 1 (after cycle-1 fixes)

**Date:** 2026-11-03 · same plan, same `walk.mjs`, same isolated stack (web hot-reloaded the fixes; API unchanged).
**Widths:** 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730.

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | heading « Cabinet de démonstration » (h1), button, link → `https://app.apexa.tn/signup` `_blank` |
| A2 | ✅ | entered → `/`, no password field |
| A3 | ✅ | two visitors at once, first survives |
| A4 | ✅ | (sql, run 1) unchanged — no API change in cycle 1 |
| B1 | ✅ | `/demo` while signed in → `/` |
| B2 | ✅ | `/login`, `/signup`, `/mot-de-passe-oublie`, `/join` → `/demo` |
| B3 | ✅ | logout → `/demo`, no expiry line |
| B4 | ✅ | after the cap → `/demo?expired=1` with the line, no « Reconnectez-vous » toast (`shots/B4-expired-1440.png`) |
| B5 | ✅ | unit |
| C1 | ✅ | all six sizes, no side scroll, actions above the fold (505/730 at 1536×730) |
| C2 | ✅ | coarse 44 px |
| C3 | ✅ | Tab + Enter enters |
| C4 | ✅ | title + description |
| D1 | ⏭ | no `HostedMultiTenant` API on this stack; unit tests cover the unchanged mode |

**Status: GREEN**

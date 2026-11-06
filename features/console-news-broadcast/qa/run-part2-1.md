# QA run 1 — Part 2, « Nouveautés » dans l'app

**Date:** 2026-11-06
**Cycle:** 1 of at most 3
**Plan:** `plan-part2.md` (19 rows) · **Driver:** `walk-part2.mjs` (+ `prep-app.mjs`, `seed-news.mjs`, `totp.mjs`)
**Environment:** fully isolated — API from the worktree on :5094 (console listener :5494), DB `clinic_news_qa` (new),
own key ring, MinIO bucket `news-qa`; console standalone :3094; web standalone :3095; shared Mailpit, QA addresses
`@news-qa.test` only. Nothing shared was written. Seed: 12 news, 9 back-dated before the admin's account → 3 unread.
**Widths looked at:** 320 · 390 (coarse) · 820×1180 · 1180×820 · 1440×900 · 1536×730
**Status:** GREEN

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | rail foot row « Nouveautés » with « 3 »; shots/A1-rail-badge-1440.png |
| A2 | ✅ | 10 cards, 3 « Nouveau », « 3 non lues », index 12 under Novembre · Octobre · Septembre · Août; shots/A2-page-1440.png |
| A3 | ✅ | rail pill gone after the open; cards keep « Nouveau » |
| A4 | ✅ | SQL: one `PlatformNewsReadMarkers` row, `ReadUpToUtc` = newest visible `CreatedAtUtc` |
| A5 | ✅ | reload: no « Nouveau », no count, no pill |
| A6 | ✅ | Mailpit: « Ouvrir Apexa » → `http://localhost:3095/nouveautes` |
| B1 | ✅ | console « Masquer » → « Masquée » + « Afficher »; app badge 2; shots/B1-console-hidden-1440.png |
| B2 | ✅ | « Afficher » → « Visible »; app badge 3 |
| B3 | ✅ | secretary: no row, « Réservé aux administrateurs », `GET /api/news/summary` 403; shots/B3-secretary-1440.png |
| B4 | ✅ | « Nouveautés plus anciennes »: 10 → 12 cards, button gone |
| B5 | ✅ | index → oldest (not yet rendered) card revealed and scrolled into view |
| B6 | ✅ | all 12 hidden → « Aucune nouveauté pour l'instant »; shown again → back |
| C1 | ✅ | 390 coarse: « Plus » dot + « Ouvrir la navigation — 3 nouveautés non lues »; drawer foot row « 3 » |
| C2 | ✅ | 320 / 390: `main` does not scroll sideways, date inside the card, no index |
| C3 | ✅ | collapsed rail: dot on the icon |
| C4 | ✅ | 820×1180 · 1180×820 · 1536×730: no sideways scroll; index scrolls inside itself at 730 tall |
| C5 | ⏭ | a cabinet's own PC needs a SelfHostedLan API — the 404-before-mediator is `NewsControllerTests` |
| D1 | ✅ | `/patients`: « Patients » active, news row unchanged |
| D2 | ✅ | « Plus » still opens the drawer |
| D3 | ✅ | console history « 1 / 1 envoyé » + « Visible »; cards below `lg:` carry « Dans l'app » |

## Observations (off-plan)
- The « Premiers pas » hand-over prompt and its highlight show through the phone drawer on a new cabinet's first
  visit (shots/C1-drawer-390.png). Pre-existing (`guided-first-days`), not touched here.

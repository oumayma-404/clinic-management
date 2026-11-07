# QA run 2 — Part 2, « Nouveautés » dans l'app (after the review fixes)

**Date:** 2026-11-07
**Cycle:** 2 of at most 3 — re-run of the whole plan after `reviews/feature-review-part2.md` (31 findings) was applied
**Plan:** `plan-part2.md` (24 rows: the 19 of run 1 + A7, B7, C6, D4, D5) · **Driver:** `walk-part2.mjs` (same file, rows added, none removed)
**Environment:** fully isolated, rebuilt from scratch — API from the worktree on :5094 (console :5494), DB `clinic_news_qa`
(new), own key ring, MinIO bucket `news-qa`; console standalone :3094; web standalone :3095; shared Mailpit, QA
addresses `@news-qa.test` only. Seed: 12 news, 9 back-dated before the admin's account → 3 unread.
**Widths looked at:** 320 · 390 (coarse) · 820×1180 · 1180×820 · 1440×900 · 1536×730
**Status:** GREEN (first drive RED 2 → one probe, one product; both fixed, whole plan re-driven)

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | rail pill « 3 » |
| A2 | ✅ | 10 cards, 3 « Nouveau » (now the shared `Badge`), header « 3 nouvelles », index 12 under 4 months |
| A3 | ✅ | rail cleared, cards keep « Nouveau » |
| A4 | ✅ | SQL: one marker, at the newest news |
| A5 | ✅ | reload: no « Nouveau », no count, no pill |
| A6 | ✅ | « Ouvrir Apexa » → `/nouveautes` |
| A7 | ✅ | `POST /api/news/read` body `upTo` = the newest card's `sentAt` |
| B1 / B2 | ✅ | console « Masquer » → app badge 2; « Afficher » → 3 |
| B3 | ✅ | secretary: no row, page refused, API 403 |
| B4 | ✅ | 10 → 12 cards, button gone, **focus on the 11th card** |
| B5 | ✅ | index → oldest card rendered, scrolled into view and **focused** |
| B6 | ✅ | all hidden → empty state; shown → back |
| B7 | ✅ | send in progress: « Modifiable après l'envoi », no button, hide → **409**, row still visible |
| C1 / D2 | ✅ | phone « Plus » dot + name, drawer row « 3 »; « Plus » opens the drawer |
| C2 | ✅ | 320 / 390: fits, date in card, no index |
| C3 | ✅ | collapsed rail: dot on the icon |
| C4 | ✅ | 820×1180 · 1180×820 · 1536×730: no sideways scroll |
| C5 | ⏭ | a cabinet's own PC needs a SelfHostedLan API — `NewsControllerTests` holds the 404-before-mediator |
| C6 | ✅ | URL subject, unbroken line, 🦷🦷🦷 tile: nothing overflows at 320 / 390; shots/C6-long-news-320.png |
| D1 | ✅ | « Patients » active, news row « 3 » |
| D3 | ✅ | history « 1 / 1 envoyé · Visible »; cards carry « Dans l'app » |
| D4 | ✅ | rail « 1 » → news hidden → page opened from the rail → rail cleared (the Major finding) |
| D5 | ✅ | console card at 390: « Masquer » under no clipping box; shots/D5-console-card-390.png |

## First drive — findings

| ID | Verdict | Cause | Fix |
|----|---------|-------|-----|
| D4 | probe | the console's BFF answers **200** for a queued send, the probe expected the API's 202 — the rail had cleared | probe accepts any 2xx |
| D3 | product (this cycle's own change) | moving « Dans l'app » out of the card's truncating field (F17) also dropped its label — a card read « Visible · Masquer » with nothing saying about what | `AppVisibility` takes `labelled` on cards: « Dans l'app » above the state |

## Gates after the last edit

- `dotnet test` (unfiltered, scratch OutDir + vstest): **6404 passed, 0 failed**, 6 skipped (pre-existing)
- web: `check:responsive` 86/86 · `tsc` · `build` ✅
- console: `check:responsive` 17/17 · `tsc` · `build` ✅
- verify-schema: unchanged since the review (no migration in this cycle)

## Observations (off-plan)

- B7's screenshot is the top of the console page; the row it asserts on is below the fold (asserted by text, not by eye).

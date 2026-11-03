# QA run 2 — Guided first days, part C

**Date:** 2026-11-03
**Cycle:** 2 of at most 3 (after the F-1 fix)
**Plan:** the whole plan, same `walk.mjs` — output `walk-run2.log`
**Environment:** as run 1, driven ~90 s after it.
**Status:** RED (2 rows) — **environment**, plus one real wording defect it exposed

## Scenarios

50 ✅, 2 ❌ (B1, B5), 1 ⏭ (B10). F-1 verified by the label row passing at 320.

## Triage

B1 and B5 both waited for « Lien expiré » on a link that had answered 410 in run 1, on unchanged server code — a probe /
environment claim first. The API log shows **no** preview request reaching the handler after 17:15:22: the anonymous
auth limiter (`RateLimiting:Auth`, 30 calls / 5 min per address — a preview carries no e-mail, so it is keyed on the
address) refused them. Runs 1 + 2 back to back made ~33 such calls. Not a product defect; drives now run ≥ 5 min apart
(noted in `walk.mjs`).

## Findings

| ID | Severity | What | Owner |
|----|----------|------|-------|
| F-2 | minor | A refused preview (429) told the person « Vérifiez votre connexion » — the server's own sentence (« trop de tentatives… réessayez dans N minutes ») was dropped | `web/app/invitation/page.tsx` read-failure branch |

## Fixes applied (cycle 2)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-2 | confirmed | the read-failure stage carried no detail, so every non-410 failure got the network sentence | `web/app/invitation/page.tsx` | this page only; new plan row **B11** (429 → the server sentence + « Réessayer ») |

# QA run 6 — the gaps closed (Tier E) + the whole plan again

**Date:** 2026-10-07 · **Plan:** `plan.md` Tiers A–E · **Drivers:** `gaps.mjs` (Tier E) then `walk.mjs` (Tiers A–D)
**Environment:** API :5000 PID 42240 and `next dev` :3000, both this session's. The owner's letterhead (« Page entière »,
their framed paper) was saved before each driver and restored after it. One earlier attempt (gaps run 3) is void: Docker
stopped answering mid-run (« timed out dialing Hyper-V socket »).
**Widths:** 1536 × 730, 1440 × 900, 820 × 1180 with a coarse pointer and touch, 320 × 640.

## Findings of the gap pass, and what was done

| Finding | Severity | Verdict | Fix |
|---------|----------|---------|-----|
| A cream paper cut into bands printed beige bars on a white page (`E4b`, eye) | major | confirmed | `bands.ts`: whiten the paper's own colour (median of the page, tolerance 40), not only near-white |
| 820 × 1180: the cut page ran under the buttons (eye) | minor | confirmed | size the page by the dialog's real cap (85dvh from `md:`, the full screen below) |
| The first version of that fix compiled to an empty value — page 0 px wide (gaps run 3) | major (own regression) | confirmed | `calc()` spaces written as underscores in the Tailwind arbitrary property |
| Cut line's touch area 42 px on a coarse pointer (read in source) | minor | confirmed | `coarse:py-6` → 50 px |
| E2b « editor shows the text header » | — | probe | read too early / wrong `.light`; the editor was right (`probe-editor-page.png`) |
| E3b « buttons 36 px » | — | probe | a Button's 44 px is its `.touch-target` ::after |

## Result

**gaps.mjs — 12 ✅ · 0 ❌**
E1 a silent version change no longer 409s · E2 / E2b the card and the editor draw « Page entière » · E3 / E3b / E3c / E3d
tablet: finger drag moves the line, every control ≥ 44 px, no sideways scroll, page fits · E4a–d landscape, cream (now
white corners), watermark kept in « Page entière », two-page PDF uses page 1 only · E5 the archive carries the three
letterhead blobs and the clinic row names them.

**walk.mjs — 36 ✅ · 0 ❌ · 2 ⏭** (A9 Word withdrawn app-wide; D3 the QA clinic's blank name — unchanged).

**Also:** `verify-schema` — no letterhead drift; its 5 drifts are identical to the 3 October report · unit suite
4 989 ✅ (adds the LAN disk-storage round trip) · check:responsive 77/77 · tsc ✅ · build ✅.

**Not reachable:** a physical printer; a restore drill on the shared database (additive restore; export proven by E5).

**Status: GREEN**

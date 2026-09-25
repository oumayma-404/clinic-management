# QA — gap plan (plan-2.md), runs gap1 → gap3, and plan 1 re-runs (run3, run4)

**Driver:** `walk-2.mjs` (gap) + `walk.mjs` (plan 1), same scripts every run; fresh fixtures per run
(`arrange.mjs`, `arrange-2.mjs`). Stack: api :5000, web `next dev` :3000 (restarted fresh by this session),
docker. Peers live: all idle. Real phone/tablet contexts (`isMobile` + `hasTouch`) for the touch rows; dark theme
via `prefers-color-scheme` + stored theme; a real secretary account for the reception rows.

| Run | Checks passed | Red (product) | Red (probe) | Eye pass |
|---|---|---|---|---|
| gap1 | 96 | — | 7 (tap sizes read without the touch overlay, menu names, labels, an Escape in the fiche) | 91 shots, 2 reviewers: ~45 findings (7 major) → fixed by 3 workers |
| run3 + gap2 | 84 + 107 | 0 | 2 (upper-case heading, agenda menu) | ~125 shots, 3 reviewers: every round-1 item confirmed fixed; ~60 smaller items → fixed |
| run4 + gap3 | 84 + **108** | 0 | 1 (agenda menu, screenshot shows it open) | final review of changed screens |

**Gate after the last edit:** `check:responsive` 72/72 · `tsc --noEmit` clean · `next build` green (isolated copy).

**Written with product data:** throwaway patients « QAU… Flow » / « QAG… Gap » only; one fiche saved per run (the
« Today » fixture), in-row remise/price saves and one secretary booking (a draft treatment created — reception
booking a multi-séance act works, no 403). The three secretary accounts were deactivated afterwards
(`cleanup-accounts.mjs`).

**Not covered:** a physical tablet/phone (emulated only); screen readers.

**Status: GREEN** pending the final screenshot review.

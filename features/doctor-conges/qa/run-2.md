# QA run 2 — Congés du praticien (re-run after cycle 1)

**Date:** 2026-10-20 · same plan, same `walk.mjs` (+ row `F1-bar`; A1 now expects the label at 1536, icon at 1440; D1
also asserts the title no longer says « heures d'ouverture ») · same isolated stack; web `next dev` :3098 restarted
after the build · API rows re-run (`api-rows.mjs`): 13/13 ✅, probe visits removed by SQL afterwards (my own).

## Scenarios
All rows of run 1 ✅ — including B1 (« Le dernier jour doit être le même jour ou après le premier. » now shows) and
D1 (« Créer ce rendez-vous en dehors des disponibilités ? » over the congé sentence).

| ID | Result | Evidence |
|----|--------|----------|
| F1-bar | ✅ | no overflow at 1280 / 1366 / 1440 / 1536 / 1920, unfiltered and filtered; title vs. before the feature: 1280 36 (0) · 1366 122 (41) · 1440 196 (115) · 1536 241 = full (211) · 1920 full. « Congé » labelled from 1536, Google from 1920 |
| C3 | ❌ | F4 — the probe passed (innerText), the eye pass did not |

## Findings

### F4 — minor · on a phone week the congé pill reads « C… »
- **Repro:** 390×844, `/appointments?date=2026-11-02&view=week` (`shots/C3-390-week.png`).
- **Actual:** icon + « C… » — the 12 px icon leaves the word no room in a ~40 px column. `innerText` still says
  « Congé », which is why the probe passed; the Mois marker (word alone, no icon) fits (`C5-390-month.png`).
- **Owner:** `appointment-calendar.tsx` `renderLeaveChip`, compact branch.

## Observations (off-plan)
- At 1280 with the rail open the week title still gets only 36 px (12 px filtered). Before this feature it got 0 and
  the bar overflowed; what is left is the praticien Select shown from `xl:` — pre-existing, not touched.
- At 1366–1440 the Google and Congé buttons are two icon-only calendar glyphs side by side (each has a tooltip).

**Status: RED (1 finding)**

## Fixes applied (cycle 2)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F4 | confirmed | the compact chip kept the icon, so the word truncated | `appointment-calendar.tsx` — compact chip: no icon, « congé » like the Mois marker, `px-0.5` | one render function, 2 callers (ribbon week compact / non-compact); non-compact unchanged |

**Probe fixed:** C3 now measures the span's clip (`scrollWidth > clientWidth`) at 390 **and** 320, since `innerText` cannot see truncation.

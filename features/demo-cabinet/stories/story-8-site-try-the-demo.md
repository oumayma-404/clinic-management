# Story 8: Site - « Essayer la démo » on apexa.tn

**Status:** APPROVED
**Story Status:** not-started
**Layer:** Site (`site/`)
**Depends On:** 7 (demo live at demo.apexa.tn)
**Blocks:** None

## Objective

apexa.tn offers « Essayer la démo » in the hero, the phone menu and the odontogramme page; it opens
`https://demo.apexa.tn` in a new tab and each click is counted as `essayer-demo`. ⚠️ **Ships only with the owner's
OK: local preview first, then the two pushes (diff origin first).**

## Acceptance Criteria

_From spec:_

- [ ] AC-6.1 hero (beside « Essayer gratuitement »), phone menu, odontogramme CTA
- [ ] AC-6.2 new tab to `https://demo.apexa.tn`
- [ ] AC-6.3 click counted as `essayer-demo`
- [ ] AC-6.4 top bar keeps a single button; a text link only if it fits at desktop width

_Story-specific:_

- [ ] `check-tokens.mjs` green (no banned claim introduced)

## Entry Criteria

- [ ] Story 7 applied by the owner; demo.apexa.tn answers
- [ ] The Umami listener commit (`587ad9ea`, on `feature/security-remediation`) is in the site branch — owner says which branch

## Steps

1. **Bring in the Umami listener** (owner's call on branch).
2. **Links** — `site/src/pages/index.html` hero action, `site/src/partials/nav.html` drawer (+ top-bar text link if
   it fits), `site/src/pages/odontogramme.html` CTA; all `target="_blank" rel="noopener"`.
3. **Count** — `site/src/layout.html` capture listener: `a[href^="https://demo.apexa.tn"]` →
   `umami.track('essayer-demo')`.
4. **Preview** — build locally, show at 320 / 390 / 1440; owner approves.
5. **Ship** — the two pushes per memory `website-work-needs-two-pushes` (build from HEAD, diff origin first).

## Files to Create/Modify

### Files to Modify

| File | Changes |
|------|---------|
| `site/src/pages/index.html` | hero action |
| `site/src/partials/nav.html` | drawer / top-bar link |
| `site/src/pages/odontogramme.html` | CTA |
| `site/src/layout.html` | Umami event |

## Verification Steps

- [ ] `check-tokens.mjs` green; local preview at 3 widths
- [ ] Owner approval recorded
- [ ] After the push: click on live site opens the demo; `essayer-demo` visible in Umami

## Exit Criteria

- [ ] All ACs checked; owner approved and pushed; README status → `done`

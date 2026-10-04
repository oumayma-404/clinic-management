# Progress: Post-visit popup asks « venu ? » first

**Started:** 2026-10-03
**Type:** Small
**Branch:** feature/security-remediation (kept: the checkout is shared with peer sessions, so switching HEAD would move it under them)

## Status
- [x] Implementation
- [x] Quality checks — `tsc --noEmit` clean · `check:responsive` 72/72 · `npm run build` green · `h-11!` emitted as `!important`
- [x] Blast radius closed (6 rows: 1 changed, 3 to re-test, 2 unaffected)
- [ ] Tests (handled by /test-small-feature)
- [x] Browser QA — `qa/run-1.md` GREEN, 17 scenarios · 1440×900, 1536×730, 820 coarse, 390 coarse

## Blast Radius
| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `web/components/post-visit-review-popup.tsx` | the prompt | mounted once, `dashboard-header.tsx:262` | must change |
| 2 | « Plus tard » button | dismissal | `e2e/lib/goto.ts` `dismissPostVisitPrompt` clicks it by name | must re-test — still on step 1 |
| 3 | `appointmentsApi.update(id, { status: "NoShow" })` | existing status write | `visit-closure-list.tsx` « Absent » | unaffected — same call, read-only use |
| 4 | server side effects of `NoShow` | `CancelPostVisitReviewAsync` + realtime | bell, « Séances à clôturer », dashboard | must re-test — review leaves the bell, visit leaves the worklist |
| 5 | tablet toast (`action` / `cancel`) | per-toast sonner options | no other toast reads them | must re-test on a coarse pointer |
| 6 | `normalizeStatus` | status reader | many | unaffected — read only |

Shapes checked: status transition (`NoShow` is a legal manual exit from every pre-closure status, not from
`Completed` — that case opens on step 2) · concurrent writer (no `version` sent → no 409, same as the worklist) ·
tri-state DTO (`{ status }` alone leaves acts untouched — same body as the worklist).

## Working tree note (start of session)
~198 unrelated dirty/untracked files from peer sessions on this branch. Only the files below belong to this feature.

## Files Changed
- `web/components/post-visit-review-popup.tsx`
- `web/components/CLAUDE.md` (the popup's row — it still named the old `/documents` destination)
- `features/post-visit-presence-step/spec.md`, `progress.md`

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| A failed « Absent » also re-reads the pending reviews | on a tablet sonner removes the toast on « Absent »; the re-read brings it back, or moves on if a fiche was saved meanwhile |
| Toast buttons get `h-11! px-4! text-sm!` | sonner paints them at 24 px; the toast only renders on a coarse pointer, and AC-7 pins 44 px |

## Significant Deviations

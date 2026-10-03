# QA plan — Post-visit popup asks « venu ? » first

**Written:** 2026-10-03
**Change under test:** working tree on `feature/security-remediation` — `web/components/post-visit-review-popup.tsx`
**Rendering files in the diff:** 1 — `post-visit-review-popup.tsx`
**Blast-radius table:** `features/post-visit-presence-step/progress.md` (3 rows marked `must re-test`)
**Budget:** small → tiers A, B, C (reachable), D · 15 scenarios

**Target:** production build (`node .next/standalone/server.js`), not `next dev` — the toast's `h-11!` is a
CSS-order question a dev server can answer differently.

## Arrange (through the product's API, before the browser)

One throwaway patient « QA Présence » and five appointments **today, 09:00–11:00, 30 min each, no doctor**
(so the review targets everyone), `allowOutsideWorkingHours` + `allowOverlap` (today is a Saturday). Slots have
ended, so each carries a due post-visit review.

| Ref | Used by | State before the walk |
|-----|---------|-----------------------|
| ap1 | A1–A3, B2, C1 | Scheduled / Séance passée |
| ap2 | A4, A5, B5, D2 | Scheduled / Séance passée |
| ap3 | B1 | Scheduled; moved to `Completed` over the API **while its popup is open** |
| ap4 | B4 | `Completed` over the API (« Venu » from the worklist, no fiche) |
| ap5 | C2, C3 | Scheduled / Séance passée |

The probe narrows `GET /notifications/pending-reviews` to the one appointment a scenario is about (filtering the
**real** response — the clinic has a backlog, and the popup shows the first unsnoozed one).

## Tier A — happy path

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | any route, desk | load `/appointments` with ap1 pending | dialog « Séance terminée », text « Séance de … QA Présence terminée. Le patient est-il venu ? », buttons exactly « Plus tard » · « Absent » · « Venu » | browser | 1440×900 |
| A2 | same | click « Venu » | footer becomes « Plus tard » · « Remplir la fiche de soins »; text drops the question; **no** non-GET request fired | browser | 1440 |
| A3 | same | click « Remplir la fiche de soins » | URL is `/patients/<patientId>?addRecord=1&appointmentId=<ap1>` | browser | 1440 |
| A4 | desk | ap2 pending, click « Absent » | toast « Patient marqué comme absent. »; dialog gone | browser | 1440 |
| A5 | — | after A4 | ap2 `Status` = NoShow in the database | sql | — |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | desk | ap3 popup open on step 1 → set ap3 `Completed` over the API → click « Absent » | an error toast with the server's reason; the dialog **stays open on step 1**; ap3 still `Completed` | browser + sql | 1440 |
| B2 | desk | ap1 pending again (snooze cleared), click « Plus tard » on step 1 | dialog closes; `clinic:pvr-snooze` holds the review id; after a reload no dialog | browser | 1440 |
| B3 | desk | ap1, « Venu », then Escape | dialog closes; snooze written (same as « Plus tard ») | browser | 1440 |
| B4 | desk | ap4 (`Completed`, no fiche) pending | dialog opens **directly on step 2** — « Remplir la fiche de soins », no « Venu » | browser | 1440 |
| B5 | desk | ap2-like: double-click « Absent » fast | exactly **one** `PUT /appointments/<id>` | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | owner's laptop | ap1 step 1 at 1536×730 | all three buttons inside the viewport, no clipping; screenshot | browser | 1536×730 |
| C2 | tablet (CDP coarse + touch, 820×1180) | ap5 pending | `matchMedia('(pointer: coarse)')` true; **no dialog**; a toast with « Venu » and « Absent », each ≥ 44 px tall; tap « Venu » → the same toast now shows « Remplir », « Absent » gone | browser | 820 |
| C3 | tablet | ap5 again (reload), tap « Absent » | toast « Patient marqué comme absent. »; ap5 `NoShow` | browser + sql | 820 |
| C4 | phone (390×844, coarse) | load with ap1 pending | no dialog, no prompt toast, **no** `pending-reviews` request | browser | 390 |

## Tier D — regression sweep (from the blast-radius table)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #2 « Plus tard » | `e2e/lib/goto.ts` `dismissPostVisitPrompt` | `getByRole('button', { name: /^Plus tard$/ })` resolves on step 1 and closes it (B2 uses that exact locator) | browser | 1440 |
| D2 | #4 server side effects | bell + worklist | after A4, the real `pending-reviews` no longer lists ap2, and `/appointments/to-close` no longer asks presence for ap2 | api | — |
| D3 | #5 other toasts | the success toast | « Patient marqué comme absent. » renders as an ordinary toast (not 44 px-button styled, no buttons) | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| Saving the fiche after « Remplir » | writes a clinical record; the destination is unchanged by this feature (A3 stops at the URL) |
| Silence on a patient file | untouched code path (`onAPatientFile`) |
| The bell row's own deep link | untouched (out of scope in the spec) |

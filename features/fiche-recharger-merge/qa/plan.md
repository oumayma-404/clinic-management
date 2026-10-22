# QA plan — Fiche de soins « Recharger » reconciles instead of overwriting

**Written:** 2026-10-07
**Change under test:** working tree on `feature/security-remediation` (uncommitted) — `follow-up/fiche-recharger-silent-overwrite.md`
**Rendering files in the diff:** 1 — `web/components/patient-record-modal.tsx` (+ `components/record/fiche-merge.ts`, `components/record/use-session-acts.ts`)
**Blast-radius table:** in the follow-up file (4 rows; `must re-test`: the modal — open/edit/save, create, « Corriger la note », Recharger)
**Budget:** large (clinical data writer) → tiers A, B, C, D · 11 scenarios

**Environment:** shared stack (docker/api/web UP, owned by peer `clinic-management-cd`) — web :3000 is `next dev`
on this same working tree, so it serves the change; the API is unchanged by it. Nothing restarted. Own
`playwright-core` Chrome (channel `chrome`), **not** the shared MCP profile. Two browser contexts = two separate
session families of the QA account (« A » = the user, « B » = the colleague), plus one API token for arranging.
All writes go to a **test patient created for this pass** (`QA-Merge-<stamp>`); everything else is read-only.
A's context blocks `/hub/clinic` so its page list stays as old as a real stale tab (needed by B2).

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/patients/<p>?editRecord=<f>` in A and B | A opens; B opens, sets the act price 77, saves; A adds a note « note de A », saves | A's save refused with the 409 banner + « Recharger »; after Recharger: banner gone, **no** « Modifié entre-temps » line, price field reads 77,000, « note de A » still on screen; A saves → toast | browser | 1440×900 |
| A1s | — | read the row | `Cost = 77`, `Notes` contains « note de A » and « note initiale » | sql | — |
| A2 | same | B sets the price 88, saves; A (opened before) sets 55, saves → 409 → Recharger | the line « Modifié entre-temps par quelqu'un d'autre : « Actes »… » is shown; price field reads 88,000; A types 66, saves → toast | browser | 1536×730, then 320×700 screenshot of the line |
| A2s | — | read the row | `Cost = 66` | sql | — |
| A3 | same, « Prescription » section | B adds an examen « Panoramique B », saves; A (opened before) adds examen « Bilan A », saves → 409 → Recharger | the line names « Ordonnance »; the section shows « Panoramique B », not « Bilan A » | browser | 1440×900 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | A, no colleague | open the fiche, wait 3 s, change notes, save | no « Modifié entre-temps » line at any point; save succeeds with no 409 | browser | 1440×900 |
| B2 | A's page list is stale | A loads the patient page (hub blocked); B saves a price 99; A opens the fiche **from the row** (not a reload) | price field reads 99,000 after the open-time read lands, **no** notice; A changes a note and saves → **no 409** | browser | 1440×900 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | second save | after A2, reopen and save unchanged | toast; `Cost` still 66 and act count unchanged | browser + sql | 1440×900 |
| C2 | 320 px | A2's notice at 320×700 | the line wraps inside the dialog; no horizontal scroll on the dialog body | browser | 320×700 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | modal — create path | « Nouvelle fiche de soins » on the test patient: name an act, save | toast « Fiche de soins enregistrée »; a second fiche exists | browser + sql | 1440×900 |
| D2 | modal — hydration of a devis-carried fiche (read-only) | open fiche `0ec0a407…` (patient `8c0e2af9…`, carried, no note of its own), close without saving | the treatment band names the devis act; no « Modifié entre-temps » line | browser | 1440×900 |
| D3 | modal — ordonnance first load (read-only) | open fiche `b7033384…` (patient `4cf1c3ea…`), close without saving | « Prescription » shows at least one line; no notice | browser | 1440×900 |

## Not covered, and why

| Area | Why not |
|------|---------|
| « Corriger la note » | issues/corrects a numbered note — money; not what this change writes (verification.md § 7) |
| A colleague collecting money (« Payé ») | creates a real note d'honoraires and a payment in la caisse; the payment section merge is covered by the same code path as A1/A2 (`ficheSnapshot.payment`) |
| Other `useFreshVersion` forms | unchanged |

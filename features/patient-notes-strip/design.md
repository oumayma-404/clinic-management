# Design — Notes du patient : surfacing séance-level important notes

**Status: APPROVED — option A, implemented.**

Chosen: **A**, with one amendment requested at approval time — each séance alert names **its act as well as its
date**, so the reader can place the visit the warning came from without opening anything. Implemented in
`web/components/patient/patient-notes-strip.tsx`; B / C / D are kept below as the record of the trade-offs.

## Deviations from `/design-ui`

| Step | What happened |
|------|---------------|
| 0 — prerequisites | **No `spec.md`.** The requirement arrived directly (prompt + screenshot of the live component). Designing from that rather than blocking, since the ask is unambiguous: *séance-level important notes must be readable without a click, without costing much height.* |
| 3–4 — browser exploration | **No browser tooling in this repo** (no `agent-browser`, no `scripts/start-dev.sh`). Per the skill's own fallback, the design system was derived by **reading the frontend source**: `web/app/globals.css` tokens and the three real components below. The user-supplied screenshot covers the "current visual state" the browser step exists to establish. |

## What is being changed

`web/components/patient/patient-notes-strip.tsx`, built on `web/components/patient/quick-disclosure.tsx`.

**Current structure** (verified in source):

- An always-visible amber band carrying **`patient.importantNotes` only**.
- A collapsed `QuickDisclosure` labelled « Notes » with one count, holding `patient.notes` **plus every séance's notes**, where a séance's `importantNotes` render as amber boxes *inside* the panel.

**The defect.** A séance note marked important is invisible until the row is expanded. The band that exists to be read before touching the patient does not contain it.

## Design system in use (unchanged, honored)

Taken from `web/app/globals.css` — the mockups reproduce these exactly rather than approximating:

| Token | Light | Dark |
|---|---|---|
| `--background` | `oklch(0.99 0 0)` | `oklch(0.12 0.005 250)` |
| `--card` | `oklch(1 0 0)` | `oklch(0.16 0.008 250)` |
| `--muted-foreground` | `oklch(0.48 0.01 250)` | `oklch(0.58 0.01 250)` |
| `--border` | `oklch(0.93 0.005 250)` | `oklch(0.24 0.01 250)` |
| `--primary` | `oklch(0.52 0.14 245)` | `oklch(0.6 0.16 245)` |
| `--radius` | `0.5rem` | — |
| `--ease-snap` | `cubic-bezier(0.23, 1, 0.32, 1)` | — |

Semantic warning colour is Tailwind's amber (`amber-50/200/300` + `amber-700/800/950`), as the component already uses. Type is Geist in the app; the mockups fall back to the system sans stack — the Artifact CSP blocks font CDNs and inlining Geist as a data URI is not worth the page weight for a layout comparison. Metrics use a monospace utility face.

## Problems each option is measured against

1. **Séance alerts need a click.** (the stated requirement)
2. **Amber inside amber** — the standing patient warning and a one-visit observation get identical treatment, nested.
3. **One count for two kinds of thing** — « 4 » mixes notes and warnings, so it never says how many warnings exist.
4. **Two lines per séance** — header row then note row; three séances fill the bounded area.
5. **No recency on warnings** — an important note from yesterday and from last year read the same.

## The four options

Collapsed height is measured with the screenshot's real data: 2 patient warnings, 2 séance warnings, 1 patient note, 1 ordinary séance note.

| | Treatment | Collapsed | Alerts visible without a click | Fixes |
|---|---|---|---|---|
| **A** *(chosen)* | The amber band becomes the alert hub: patient warnings as **chips** (dense — two keywords on one line), séance warnings as one-liners carrying **date + act**, capped at 2 + « +N ». | **116 px** (−6) | All | 1, 2, 3, 5 |
| **B** | One card with a 3 px amber left rail. Warnings on top, the « Notes » trigger as the last row of the same card. | **104 px** (−18) | All | 1, 2, 3, 5 |
| **C** | A single 32 px « guard line »: count + first keywords, expands on click. Fixed height at any alert count. | **76 px** (−46) | Count + first ~2, rest truncated | 1 (partly), 2, 3, 5 |
| **D** | Today's band unchanged; the collapsed row gains a second amber « ⚠ 2 » badge and séance alerts are pinned to the top of the panel. | **122 px** (±0) | None | 3, 5 |

### Why A is the recommendation

It carries every alert at a glance while ending up *shorter* than today, because chips compress what the current band spreads over one line per keyword. And it resolves defect 2 by **form** rather than colour: a chip is a standing fact about the patient, a dated line is an observation from one visit — so the two stop being indistinguishable amber boxes, one nested in the other.

B is the fallback if height is the binding constraint; its risk is that housing an always-visible warning inside the same card as a collapsible control makes the warning read as part of what is collapsed.

C is right only if the odontogram must gain space at all costs — a truncated alert needs a click to be certain of, which reintroduces the original problem in another form.

D is included as the honest low-risk baseline and is **marked as not meeting the requirement**.

## Common to all four

- Every séance alert carries its **date**.
- (A / B / C) The collapsed row's count covers only what is actually collapsed.
- Practitioner spelling is rendered verbatim — never corrected at display time.
- No change to how any of this is written; all three fields stay authored through `edit-patient-dialog.tsx`.

## States covered in the mockups

- Real case (2 patient + 2 séance warnings)
- Cap overflow (3 patient + 5 séance warnings, « +3 » expanding in place) — option A
- No warnings at all (amber disappears entirely; only the 36 px row remains)
- Expanded panel with the ordinary notes
- Light and dark, both `prefers-color-scheme` and the viewer's `data-theme` toggle

## Accessibility

`aria-expanded` + `aria-controls` on every trigger (as `QuickDisclosure` already does); the amber band is not a control in A and D; decorative icons are `aria-hidden`; visible `:focus-visible` ring on every interactive element; motion respects `prefers-reduced-motion`; the 200 ms `--ease-snap` grid `0fr → 1fr` height transition is reused unchanged.

## Mockup

`features/patient-notes-strip/mockups/01-notes-options.html` — self-contained, interactive (disclosures and the « +N » really open).


## As built (option A)

`web/components/patient/patient-notes-strip.tsx` — the amber band is now the alert hub.

| Piece | Behaviour |
|---|---|
| Patient warnings | `splitPatientWarnings` splits the textarea on newlines and strips the bullet the practitioner typed. Chips are used **only** when it is genuinely a short list (≥ 2 pieces, each ≤ 48 chars) — otherwise it falls through to the pre-wrapped paragraph, so a written sentence is never stuffed into a pill. |
| Séance alerts | One paragraph each: `formatDate` (tabular, 11 px) · `record.procedureType` (11 px, lighter) · the note (14 px, medium). **Inline spans, not a flex row or a header row** — a second row per alert would double the band's height, and inline flow lets a long act name wrap instead of needing truncation. |
| Cap | A **height**, not a count: `COLLAPSED_BAND_PX = 48` ≈ two rows of either shape. An item cap cannot bound the band, because the two shapes have different line economics — two chips are one line, two long alerts are four, and a prose note is one "item" of five lines. Expanded is bounded too (`EXPANDED_BAND_PX = 168`, scrolls beyond). |
| « +N » | `useClippedAlerts` **measures** which rows fall past the cut (`data-alert-kind`, clipped when a row's *bottom* passes it, so a half-visible line counts as hidden), with a `ResizeObserver` on width. « +3 autres alertes » is therefore true at any viewport width. If the patient's own warnings are among what is cut, the hidden rows are not all countable alerts — one is a note whose remaining lines have no number — so the label falls back to « Tout afficher » instead of a figure short by an unknowable amount. The toggle is a **sibling** of the measured element; revealing it inside would resize what is being measured and oscillate. |
| Collapsed count | Patient note + **ordinary** séance notes only. Important séance notes are no longer listed in the panel — they are in the band, and listing them twice would double-report a warning and inflate the count that makes collapsing honest. |
| No warnings | The band is not rendered at all; only the 36 px `QuickDisclosure` row remains. |

Ordering is newest-first across both lists (`byDateDesc`), so the most recent séance — the one being followed up
on — is the first alert read.

# QA plan — the 24-hour time control

**Change under test.** Every `<input type="time">` (11 sites) and the one `datetime-local` rendered in the
**browser's** UI locale, which `lang="fr"` does not touch — measured in Chrome 2026-09-13, an input carrying
`lang="en-US"` and one carrying `lang="fr"` rendered identically. On an English-locale Chrome the whole product
asked for and displayed « 02:30 PM ». They are replaced by a masked 24-h text control, `TimeInput` /
`TimeField`, in `web/components/ui/time-field.tsx`.

**Blast radius** (`.claude/rules/regression-safety.md` § 1, written before the first edit):

| Touched | Other consumers | Verdict |
|---|---|---|
| `TimeField` | create/edit-appointment-dialog ×4 | contract unchanged → **must re-test** |
| `<Input type="time">` | clinic-settings ×4, doctor-working-hours-card ×4, setup-wizard ×2 | **must change** (mirrored set) |
| `<Input type="datetime-local">` | recurring-series ×1 (unrouted) | **must change** |
| `HH:mm` wire format | `WorkingHoursDto`, appointments API, agenda, reminders, CSV | unaffected — already 24 h |
| e2e specs / `check:responsive` | none fill or pin a time input | unaffected |

Budget: quick-fix on a shared primitive sitting on the product's most repeated action → A + B + the touched
rows of C and D. 17 scenarios.

## Tier A — happy path

| id | scenario | expected (observable) | layer |
|---|---|---|---|
| A1 | `/appointments` → « Nouveau » → read « Heure de début » | value matches `^\d{2}:\d{2}$`; no `AM`/`PM` anywhere in the dialog text | browser |
| A2 | type `0930` into « Heure de début » | field reads `09:30` | browser |
| A3 | type `1445` | field reads `14:45` — the afternoon hour AM/PM mangled | browser |
| A4 | type `9`, blur | field reads `09:00` | browser |
| A5 | type `930`, blur | field reads `09:30` | browser |
| A6 | from `09:30`, ArrowUp ×2 then ArrowDown ×1 | `09:40` then `09:35` (5-min step) | browser |
| A7 | type `2599` | clamps to `23:59` — never a bad time | browser |

## Tier B — alternate paths

| id | scenario | expected | layer |
|---|---|---|---|
| B1 | clear the required « Heure de début » and blur | snaps back to its previous time, not empty | browser |
| B2 | `/settings` horaires → clear an optional « Pause » field and blur | stays empty (`""`), no snap-back | browser |
| B3 | « Heure de fin » switch on, set fin before début | the dialog's own refusal still fires | browser |
| B4 | set début onto an occupied slot | « créneau occupé » / conflict notice still renders | browser |

## Tier C — edges (only what this change reaches)

| id | scenario | expected | layer |
|---|---|---|---|
| C1 | booking dialog + `/settings` horaires at **320 px** | no horizontal document scroll; both times fully legible | browser |
| C2 | same at **820 px** | ditto | browser |
| C3 | booking dialog at **1536 × 730** (owner's laptop) | the time row and the footer both reachable | browser |

## Tier D — regression sweep (one row per `must re-test`)

| id | scenario | expected | layer |
|---|---|---|---|
| D1 | create an rdv end to end with a **typed** time | the stored `AppointmentDateTime` holds the typed hour:minute | browser → sql |
| D2 | reopen it with « Modifier » | « Heure de début » pre-fills with that same 24-h time | browser |
| D3 | the agenda row / recap for it | prints the 24-h span (`HH:MM → HH:MM`) | browser |
| D4 | `/settings` horaires — change an hour, save, reload | persisted as `HH:mm`; no AM/PM | browser → sql |
| D5 | `/recurring-series` | date field + separate 24-h heure field, both populated | browser |
| D6 | `/setup` step « horaires » | 24-h fields render (wizard **not** completed) | browser |

## Read-only discipline

Money and clinical rows are untouched. D1 creates one appointment through the product, far in the future, and
reverts it by SQL afterwards — stated in the run report. D4 edits the clinic's own working hours and restores
the prior values through the product.

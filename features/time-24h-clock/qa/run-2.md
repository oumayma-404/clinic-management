# Run 2 — the 24-hour time control (after the F1 fix)

**Date** 2026-09-13 · **Cycle** 2 of 3 · **Plan** [`plan.md`](plan.md) · **Previous** [`run-1.md`](run-1.md)
**Environment** the stack was already UP and unclaimed; I started nothing.
⚠️ **`clinic-management-fe` restarted the .NET API mid-walk** (~55 s of silence, announced after the fact).
**Every API-dependent row was re-driven against the healed API afterwards** and is reported from that re-drive,
never from the pass that straddled the window. The layout rows are pure DOM measurements and do not read the API.
**Peers live during the run:** `clinic-management-64` · `-ae` · `-c2` · `-88` · `-fe` (the restarter).
**Widths actually looked at** 320 · 820 · 1440 · 1536 × 730.

## Scenario results

| id | scenario | outcome | evidence |
|---|---|---|---|
| A1 | dialog renders a 24-h field, no AM/PM | ✅ | `type=text inputmode=numeric`, value `18:55` |
| A2 | type `0930` | ✅ | `09:30` |
| A3 | type `1445` | ✅ | `14:45` |
| A4 | type `9`, blur | ✅ | `09:00` |
| A5 | type `930`, blur | ✅ | `09:30` |
| A6 | ArrowUp ×2 / ArrowDown ×1 | ✅ | `09:40` then `09:35` |
| A7 | type `2599` | ✅ | `23:59` |
| A8 | « heure de fin » + `1015` | ✅ | fin `10:15`, recap `09:00 → 10:15` |
| B1 | clear the required début | ✅ | snapped back to `10:15` |
| B2 | clear an optional « Pause » | ✅ | `12:00` then `""`, stayed empty |
| B3 | fin < début, submit | ✅ | « L'heure de fin doit être postérieure à l'heure de début » |
| B4 | colliding typed time | ✅ | « ⚠ Ce praticien a déjà un rendez-vous à 14:45 (patient : « E2E ActeFait-… ») » — **re-driven post-restart** |
| C1 | `/settings` horaires at 320 px | ✅ **was ❌** | from 191 · to 206 · **break-from 191 · break-to 206** px; no x-overflow (320/320) · `shots/C1-horaires-320.png` |
| C1b | booking dialog at 320 px | ✅ | time field 257 × 40 px, no x-overflow, no AM/PM · `shots/C1b-booking-320x800.png` |
| C2 | `/settings` horaires at 820 px | ✅ | 135 · 136 · 137 · 138 px — unchanged by the fix |
| C3 | booking dialog at 1536 × 730 | ✅ | 260 × 38 px at y=334, footer reachable |
| D1 | create an rdv with a typed time | ✅ | `POST /api/appointments` 201, `2026-12-30T13:45:00Z` = 14:45 UTC+1 |
| D2 | reopen with « Modifier » | ✅ | `edit-appt-start-time` = `14:45` — **re-driven post-restart** |
| D3 | the agenda row | ✅ | prints `14:45`, no AM/PM, no failed-read banner — **re-driven post-restart** |
| D4 | `/settings` horaires round-trip | ⚠️ **half** | **wire ✅** — the PUT body carried `"from":"08:30"`, and all 28 clinic fields render as 24-h text. **Persistence ⏭** — still refused 400 by O1 (the clinic's empty `Name`), unrelated to this change |
| D5 | `/recurring-series` | ⏭ | the screen renders « Page retirée »; the form is behind `RetiredPageCard` and unreachable, so that edit is inert |
| D6 | `/setup` horaires step | ⏭ | step 3 is only reached by completing steps 1–2, which creates a clinic |
| D7 | « Horaires de ce praticien » | ✅ | 56 fields, all 24-h text, no AM/PM — **re-driven post-restart** |

**21 exercised · 19 ✅ · 0 ❌ · 1 half · 3 ⏭**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| F1 | **confirmed (regression)** | `<input type="time">` carried a ~105 px intrinsic width that acted as a floor, so a row too narrow for two of them wrapped. `TimeInput` is a text field and nothing replaced that floor. | `ui/time-field.tsx` (declares `min-w-[4.5rem]`), `clinic-settings.tsx` (both rows `flex-wrap gap-y-1`), `doctor-working-hours-card.tsx` | **all 11 call sites** — the `min-w-0` each field carried is the same tailwind-merge group as the new floor and would delete it, so it was removed from every one and kept on the wrappers |
| O1 | **not fixed — off-plan** | the dev clinic's `Name` is `''`, so `PUT /api/clinics` 400s | — | reported, untouched: shared data with peers live |
| O2 | probe/environment | the stored session rotated out mid-walk | — | re-minted with `refresh-session.mjs` |

**Root cause named:** not « the pause fields are narrow » but « the control lost a width floor its predecessor
provided for free, and nothing declared a replacement ». That is why the fix is at the component, not at the
one row that showed the symptom — the sibling card survived by accident of its own `flex-wrap`.

**Guard added:** `check:responsive` **`time-input-keeps-its-width-floor`** (P6) — fails on any `TimeInput` /
`TimeField` call site passing `min-w-0`, which is the one class that silently deletes the floor. Derived from
the call sites themselves, no allow-list. ⚠️ **Proven red before trusting green**: a throwaway violation was
reported (`components/__guard-probe.tsx:3 min-w-0`, exit 1) and the file deleted. The first version of the
check passed that same violation — its tag scanner stopped at the `>` inside `onChange={() => {}}` — which is
exactly the too-loose-check failure `regression-safety.md` § 4 warns about.

**Gates after the last edit:** `check:responsive` **65/65** ✅ · `tsc --noEmit` ✅ · `npm run build` ✅
(backend untouched, so no `dotnet test`).

## Test artefacts, reverted

- Two appointments created through the product on 2026-12-30 14:45 for the E2E fixture patient
  `E2E ActeFait-mtslfy5g0` (one per run) — **deleted by SQL**, 1 row each.
- The working hours were **never written** (the O1 400 both times); confirmed still `"From":"09:00"` for every
  day, checked after the run.
- No money and no clinical row was touched.

---

**Status: GREEN (21 exercised, 3 ⏭ with reasons, 0 findings)**

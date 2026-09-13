# Run 1 — the 24-hour time control

**Date** 2026-09-13 · **Cycle** 1 of 3 · **Plan** [`plan.md`](plan.md)
**Environment** stack already UP and unclaimed (docker 22612 · api 37376 · web 13868), started outside any
session and not restarted. Browser lease held by `clinic-management-a8`.
**Peers live during the run** (`ListAgents`): `clinic-management-64` idle · `clinic-management-ae` shell ·
`clinic-management-c2` busy · `clinic-management-88` busy.
**Widths actually looked at** 320 · 820 · 1440 · 1536 × 730 (the owner's own laptop).

## Premise, measured before any code changed

`<input type="time">` follows the **browser's UI locale**, and `lang` does not touch it. Probe in this Chrome
(en-GB): an input with `lang="en-US"`, one with `lang="fr"`, one with `lang="ar-TN"` and one inheriting
`<html lang="fr">` all rendered `14:30` — i.e. all four followed the browser and ignored the attribute. On an
en-US Chrome all four would read `02:30 PM`. That is the defect, and no attribute fixes it.

## Scenario results

| id | scenario | outcome | evidence |
|---|---|---|---|
| A1 | booking dialog renders a 24-h field, no AM/PM | ✅ | `type=text inputmode=numeric`, value `18:30`; no `AM`/`PM` in the dialog text |
| A2 | type `0930` | ✅ | `09:30` |
| A3 | type `1445` (the afternoon hour AM/PM mangled) | ✅ | `14:45` |
| A4 | type `9`, blur | ✅ | `09:00` |
| A5 | type `930`, blur | ✅ | `09:30` |
| A6 | ArrowUp ×2, ArrowDown ×1 from `09:30` | ✅ | `09:40` then `09:35` |
| A7 | type `2599` | ✅ | clamps to `23:59` |
| A8 | « Définir l'heure de fin » + `1015` | ✅ | fin `10:15`; recap span `09:00 → 10:15` |
| B1 | clear the required début, blur | ✅ | showed `""`, snapped back to `10:15` |
| B2 | optional « Pause » set then cleared | ✅ | `12:00` then `""`, stayed empty |
| B3 | fin before début, submit | ✅ | « L'heure de fin doit être postérieure à l'heure de début » |
| B4 | type a colliding time | ✅ | « ⚠ Ce praticien a déjà un rendez-vous à 14:45 (patient : « E2E ActeFait-… ») » |
| C1 | `/settings` horaires at 320 px | ❌ | **F1** — ouverture/fermeture 91/92 px ✓, but the two « Pause » fields measure **26 px** |
| C1b | booking dialog at 320 px | ✅ | time field 257 × 40 px, no x-overflow (doc 320 / win 320) · `shots/C1b-booking-320.png` |
| C2 | `/settings` horaires at 820 px | ✅ | all four fields 135–138 px, no x-overflow |
| C3 | booking dialog at 1536 × 730 | ✅ | time field 260 × 38 px at y=334, footer reachable · `shots/C3-booking-1536x730.png` |
| D1 | create an rdv with a **typed** time | ✅ | recap `14:45 → 15:15` on 30/12/2026; `POST /api/appointments` **201**, `appointmentDateTime: 2026-12-30T13:45:00Z` = 14:45 UTC+1 |
| D2 | reopen it with « Modifier » | ✅ | `edit-appt-start-time` = `14:45`, `type=text`, no AM/PM |
| D3 | the agenda row for it | ✅ | prints `14:45`, no AM/PM anywhere in `main` |
| D4 | `/settings` horaires round-trip | ⚠️ **half** | **wire ✅** — the PUT body carried `"from":"08:30"`. **Persistence ⏭** — refused **400** `Le champ « name » n'est pas valide…`, unrelated to this change (see O1) |
| D5 | `/recurring-series` date + heure split | ⏭ | the screen renders « Page retirée » — the form is behind `RetiredPageCard` and unreachable, so that edit is inert |
| D6 | `/setup` wizard horaires step | ⏭ | step 3 is only reached by completing steps 1–2, which creates a clinic; not exercised rather than mutate |
| D7 | « Horaires de ce praticien » (the other mirrored-set member) | ✅ | 56 fields, all masked text, no AM/PM; at 320 px they measure 165/180 px — this card wraps correctly |

**17 exercised · 14 ✅ · 1 ❌ · 1 half · 3 ⏭**

## Findings

### F1 — major — the optional « Pause » fields collapse to 26 px at 320 px (`/settings` → Horaires d'ouverture)

- **Repro** `/settings` → « Horaires d'ouverture » → any weekday row, viewport 320 px wide.
- **Expected** a field wide enough to read `09:00` — the ouverture/fermeture pair manages 91 px on the same row.
- **Actual observed** `break-from` **26 px**, `break-to` **26 px** (both 28 px tall). Neither can show `--:--`,
  let alone a time. At 820 px they are 137/138 px, so it is a 320-px-only collapse.
- **Regression, and this change caused it.** `<input type="time">` carried a ~105 px *native intrinsic width*
  that acted as a floor, so the row wrapped instead of crushing. `TimeInput` is a text field and has no floor —
  the exact trap `doctor-working-hours-card.tsx` had written down. That sibling card does **not** regress
  (165/180 px at 320 px) because its pause row carries `flex-wrap`; this one does not.
- **Suspected owner** `web/components/clinic-settings.tsx:1301` — the pause row is
  `flex w-full items-center gap-2 sm:basis-full`, with no `flex-wrap` and no basis on the fields.
- **Evidence** measured `[{"id":"break-from","w":26},{"id":"break-to","w":26}]`.
  ⚠️ `shots/C1-horaires-320.png` was **overwritten by run 2** and now shows the fixed state (191/206 px), so
  the measurements above are this finding's only surviving evidence. Re-shooting a defect after fixing it is
  not possible; naming the overwrite is.

## Observations (off-plan)

- **O1 — the dev clinic's `Name` is an empty string**, so `PUT /api/clinics` refuses every save of the
  « Informations du cabinet » card *and* of the working hours with
  `{"error":"Le champ « name » n'est pas valide ou n'a pas été envoyé.","code":"validation_failed"}`. Confirmed
  in SQL: `Name` = `''` while `Phone` and `Email` are populated. Pre-existing and unrelated to this change; it
  is what blocked D4's persistence half. Not touched — it is shared data and two peers were live.
- **O2 — the stored session rotated out mid-walk** (`/login?returnTo=/settings`), as
  `clinic-browser` documents. Re-minted with `refresh-session.mjs`; nothing measured before that point was
  re-used afterwards.

## Probe bugs caught and re-driven in the same launch (3)

| symptom | actual cause |
|---|---|
| « fin < début produced no refusal » | the refusal is on **submit** (`create-appointment-dialog.tsx:839`), not live |
| « no overlap advisory » | my regex matched the « Créneau occupé » **radio label**, present before and after |
| « the horaires fields do not exist » | « Horaires d'ouverture » is a collapsible section and my card filter toggled it shut; and `Enregistrer` `.first()` resolved to an invisible one in a collapsed card |

## Test artefacts, reverted

- One appointment created through the product on 2026-12-30 14:45 for the E2E fixture patient
  `E2E ActeFait-mtslfy5g0` — **deleted by SQL** (`delete from "Appointments" where "Id"='0496fc56-…'`, 1 row).
- The working hours were **never written** (the 400 above); confirmed still `"From":"09:00"` for every day.
- No money and no clinical row was touched.

---

**Status: RED (1 finding — 1 major)**

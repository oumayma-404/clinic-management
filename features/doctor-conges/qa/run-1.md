# QA run 1 — Congés du praticien

**Date:** 2026-10-20 · **Plan:** `qa/plan.md` (26 scenarios) · **Driver:** `qa/walk.mjs` + `api-rows.mjs` (scratchpad)
**Environment:** isolated — API :5099 (worktree build, `bin/Debug` in scratchpad) + web `next dev` :3098 (worktree),
DB `clinic_conges_qa` (pg_dump copy of the dev DB, brought to main's last migration, then `AddDoctorLeaves` applied by
the API at startup). Headless Chrome, own profile. Peers live on the shared stack (not touched): clinic-management-38,
staff-chat (owns :3099), clinic-management-11 [120c5e], clinic-management-b5, clinic-management-51, anakin-e8, anakin-79.
**Widths looked at:** 320×640 · 390×844 · 820×1180 · 1280 · 1366 · 1440×900 · 1440×560 · 1536×730 · 1536 · 1920.

## Scenarios

| ID | Result | Evidence |
|----|--------|----------|
| A1 | ✅ | 1440: « Congé » left of « Nouveau », bar 36 px · 820: icon-only, one row — but see F1 (title) |
| A2 | ✅ | phone header button 40×44 px |
| A3 | ✅ | « Nouveau congé », Du = Au = 2026-11-02, Praticien prefilled « Salma Ben Youssef », focus on the title |
| A4 | ✅ | saved, toast, dialog closed (probe fix: wait for the exit animation) |
| A5 | ✅ | SQL `2026-11-02|2026-11-04|Vacances` |
| A6 | ✅ | « 6 rendez-vous sont déjà prévus pendant ce congé. » + « Voir »; 6 visits unchanged in SQL |
| A7 | ✅ | « Voir » → Jour 02/11, filter Salma, ribbon shown, hours hatched |
| A8 | ✅ | 3 ribbons « Congé — Salma Ben Youssef » at 1440 and 820; Monday not hatched (Nadia works); header « 2 RDV » |
| A9 | ✅ | filtered: Mon–Wed headers « congé », hatched; Thu open; booked visits still drawn (`A9-filtered.png`) |
| A10 | ✅ | « Modifier le congé », values back, praticien locked |
| A11 | ✅ | « Congé modifié » (4 rendez-vous …), 2 ribbons, SQL end 2026-11-03 |
| A12 | ✅ | Mon profil 1440 + 320: « du 2 novembre au 3 novembre 2026 · 2 jours · Vacances », menu Modifier/Supprimer, no overflow |
| A13 | ✅ | Paramètres → « Congés de ce praticien » under Salma lists it + « Ajouter un congé » (probe fix: card lookup) |
| A14 | ✅ | « Supprimer le congé de Salma Ben Youssef du 2 novembre au 3 novembre 2026 ? » → ribbons gone, SQL empty |
| B1 | ❌ | F2 |
| B2 | ✅ | api 400 « Salma Ben Youssef est déjà en congé du 02/11 au 04/11. » |
| B3 | ✅ | api 400 « Un congé ne peut pas dépasser 366 jours — vérifiez l'année saisie. »; 366 days accepted |
| B4 | ✅ | api 400 `outside_working_hours` « Salma Ben Youssef est en congé du 02/11 au 04/11. » |
| B5 | ✅ | api 201 with `allowOutsideWorkingHours` |
| B6 | ✅ | api 201 for Nadia, and for a visit with no practitioner |
| B7 | ✅ | « Abandonner les modifications ? » on Annuler after typing |
| C1 | ✅ | 320: bottom sheet 483 px, « Enregistrer » inside, no overflow |
| C2 | ✅ | 1536×730: footer at y 487, no inner scroll |
| C3 | ✅ | 390 Semaine: 2 compact « Congé » pills, no overflow |
| C4 | ✅ | 390 Jour: ribbon « Congé — Salma Ben Youssef » |
| C5 | ✅ | 390 Mois: 2 cells « congé », named in aria-label, nothing clipped |
| D1 | ✅ / ❌ | refusal shown with the congé sentence, nothing booked — but the title is false, F3 |
| D2 | ✅ | Sunday « fermé », hatched |
| D3 | ✅ | 1440×560: grid scrolled 119 px, ribbon stayed at y 218 (probe fix: 900 px tall needs no scroll) |
| D4 | ✅ | verify-schema before: 4 `DoctorLeaves` lines + pre-existing drift; after: pre-existing drift only |
| D5 | ✅ | api: moving a Thursday visit → 200; moving it into the congé → 400 with the congé sentence |
| D6 | ✅ | « Mes horaires » above « Mes congés » |
| R1/R2 | ✅ | clinic read names the practitioner; a > 400-day window is refused |

## Findings

### F1 — major · the agenda bar no longer fits from 1280 to 1440 px
- **Repro:** `/appointments?date=2026-11-02&view=week`, rail open, clinic not connected to Google.
- **Expected:** one row inside the card, the week title readable.
- **Actual (measured, `bar-probe.mjs`):** bar content vs. bar width, title width —
  1280: 1117/976 px (overflows 141; was 45 before « Congé »), title 0 px ·
  1366: 1117/1062 (overflows 55, `<main>` scrolls sideways 31 px; was 0), title 0 px (was 41) ·
  1440: fits, title 19 px of 225 (« 2.. », was 115); filtered on a practitioner it overflows 5 px (`A9-filtered.png`, « Nouveau » past the card edge) ·
  1536: title 115 px (was 211).
- **Cause:** the « Congé » label from `xl:` adds ~96 px to a bar whose only shrinkable item is the title; the Google
  label (also from `xl:`) already overflowed it at 1280.
- **Owner:** `web/components/appointment-calendar.tsx` — the « Congé » and Google buttons' `hidden xl:inline` labels.

### F2 — minor · « Au » before « Du » shows the browser's own message, not ours
- **Repro:** « Congé » → Du 04/11, Au 02/11 (typed) → Enregistrer.
- **Expected:** the field error « Le dernier jour doit être le même jour ou après le premier. ».
- **Actual:** nothing of ours renders; the `min` attribute fails native validation first, so Chrome shows
  « Value must be 04/11/2026 or later. » (English on an English-locale Chrome). `validity.rangeUnderflow = true`.
- **Owner:** `web/components/doctor-leaves/doctor-leave-dialog.tsx` — the `<form>` without `noValidate`.

### F3 — minor · the « book anyway » confirm calls a congé « hors des heures d'ouverture »
- **Repro:** create a visit for Salma on her congé day (D1).
- **Actual:** title « Créer ce rendez-vous hors des heures d'ouverture ? » over « Salma Ben Youssef est en congé du 02/11
  au 03/11. » — the cabinet is open that day. Same title shape in the edit dialog (« Enregistrer ce rendez-vous hors
  des heures d'ouverture ? ») and the drag-move prompt (« En dehors des horaires d'ouverture »): all three read the
  shared `outside_working_hours` code (blast-radius row #3).
- **Owner:** `create-appointment-dialog.tsx:1886`, `edit-appointment-dialog.tsx:1779`, `appointment-calendar.tsx:3696`.

## Observations (off-plan)
- The phone header congé button is 40 px wide (44 tall) — same box as its sibling header buttons.
- The closed-hour hatch is faint at 1440 (existing `--agenda-closed` token), on Sunday as on a congé day.

**Status: RED (3 findings)**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed (made worse by this feature; pre-existing at 1280) | the « Congé » and Google labels hinged on the VIEWPORT (`xl:`) while the bar's width also depends on the rail; the title is the only shrinkable item, so it went to 0 px and the bar spilled | `appointment-calendar.tsx` (bar `@container`, labels `@min-[76rem]` / `@min-[84rem]`, `title` on « Congé ») · `web/components/CLAUDE.md` | 3 labels in one bar, no other consumer; praticien Select's `xl:` hinge left alone (its other copy lives in a portal a container query cannot reach) |
| F2 | confirmed | `min` on « Au » fails native validation before `handleSubmit` runs | `doctor-leave-dialog.tsx` (`noValidate`) | one form |
| F3 | confirmed (blast-radius row #3) | three titles name « heures d'ouverture » while the shared `outside_working_hours` code now also carries a congé | `create-appointment-dialog.tsx`, `edit-appointment-dialog.tsx`, `appointment-calendar.tsx` (drag prompt) → « en dehors des disponibilités » | 3 titles = every door reading the code; no test or e2e asserts them (grep) |

**Gates after the last edit:** check:responsive 81/81 ✅ · tsc ✅ · build ✅ (web only; no `api/` change).
**Re-run:** run-2.md.

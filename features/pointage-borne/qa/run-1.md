# QA run 1 — Pointage sur la borne du cabinet

**Date:** 2026-11-05
**Cycle:** 1 of at most 3
**Plan:** features/pointage-borne/qa/plan.md (34 scenarios: A18 B13 C6 D5)
**Driver:** features/pointage-borne/qa/walk.mjs (drive 1 + a same-session re-drive of A10/C6/A15 after probe fixes)
**Environment:** isolated — api :5096 (restarted 2026-11-05 17:40 on the final backend, mine) · web :3096 `next dev`
(started 17:40, mine) · postgres `clinic_staffleave_qa` (the walk resets its own artefacts first) · Chrome 9333
(admin + the borne tab, same profile) and 9334 (secretary)
**Peer sessions live during the run:** none on these ports/DB (isolated stack)
**Widths looked at:** 320×640 · 390×844 · 560 (secretary window) · 820×1180 · 1000 · 1180×820 · 1440×900 · 1536×730
**Status:** RED (4 findings: 1 major, 3 minor)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| D2 | ✅ | a fresh context's /login has no « Pointage »; the secretary signed in through the form |
| A1 | ✅ | switch off → on, toast « Pointage activé. »; Ines + QA 991861 on, Dr Nadia off; shots/A1-settings-on.png |
| A2 | ✅ | step-up by code; toast names « Accueil »; list « Accueil · cet appareil · Jamais utilisée »; cookie `{httpOnly:true, path:/api/pointage, sameSite:Strict}` |
| A3 | ✅ | keyboard 1-2-3-6 twice → toast; « Enregistré » + « Changer de schéma »; shots/A3-mon-profil.png |
| A4 | ✅ | stored hash length > 40, not the pattern |
| A5 | ✅ | tiles « Pas encore arrivée » / « Schéma à créer dans Mon profil » (disabled); « Arrivée » in large; pointer drag → « Arrivée enregistrée · 17:46 · Borne Accueil »; tile « Présente depuis 17:46 »; shots/A5-*.png |
| A6 | ✅ | `2026-11-05|true|Accueil|true` |
| A7 | ✅ | admin's open « Aujourd'hui » showed Ines present with no reload |
| A8 | ✅ | « Départ » in large → « Départ enregistré »; row closed, `DepartureSource 0` |
| A9 | ✅ | second row today, open |
| A10 | ✅ | tabs Comptes · Congés · Présences; Ines column; today « 17:46 – 17:46 » + « 17:46 – présente »; Sundays hatched; Total row; shots/A10-sheet-1440.png (first drive's ❌ on the column was a probe: header is uppercase) |
| A11 | ✅ | « 08:30 – Départ non pointé » + « Corriger » (first drive's ❌ was a probe read during the realtime re-render; a live probe returned text ✓ and the button, count 1) |
| A12 | ✅ | dialog « Ines Gharbi · mardi 3 novembre »; toast « Pointage corrigé. »; « 08:30 – 17:00 » + « Corrigé » |
| A13 | ✅ | `2|Parti à 17 h|true` |
| A14 | ✅ | download carries the CSV; body names Ines and the corrected line |
| A15 | ✅ | « Présences »: Jours pointés 2 · Heures 8 h 30 (« sans la journée en cours ») · Départs non pointés 0; shots/A15-file.png (first ❌ = uppercase labels, probe) |
| A16 | ✅ | « 08:30 – 17:00 » listed, no « Corriger »; shots/A16-mes-presences.png |
| A17 | ✅ | block lists « Ines Gharbi · Présente depuis 17:46 » and « QA Secrétaire 991861 · Pas encore arrivée »; link → `/users?onglet=presences`; shots/A17-dashboard.png (first ❌ = read before the list arrived, probe) |
| A18 | ✅ | « Pointage » in the admin header, none in the secretary's |
| D3 | ✅ | hidden survives a reload, shown again |
| B1 | ✅ | « Reliez au moins 4 points. », still step 1 |
| B2 | ✅ | mismatch refused, back to step 1, hash unchanged |
| B4 | ✅ | « Retour » and Échap → tiles, no row |
| B3 | ✅ | « Schéma incorrect. Encore 4 essais avant le blocage. »; shots/B3-wrong.png |
| B5 | ✅ | « Trop d'essais. Réessayez dans 15 min… »; tile « Bloqué : trop d'essais », disabled; shots/B5-locked.png |
| B6 | ✅ | « Bloqué après 5 essais, jusqu'à … »; reset toast; tile « Schéma à créer dans Mon profil » |
| B7 | ✅ | untracked → no tile |
| B8 | ✅ | « Cet appareil n'est pas une borne du cabinet. » |
| B13 | ✅ | open line → « Présente » wins; no open line → « En congé », disabled (the line was closed by SQL — setup, own artefact) |
| B11 | ✅ | off → borne message, no « Présences » tab, Mon profil cards gone; back on |
| D4 | ✅ | the congés switch leaves the pointage switch on |
| D1 | ✅ | `?onglet=conges` selects « Congés » |
| C1 | ✅ | no horizontal scroll at 820×1180, 1180×820, 390×844, 320×640; pad + « Retour » on screen at 1180×820; dot cell 96 px; pad fits at 320 |
| C2 | ✅ | 390 and 320: cards, no table, no horizontal scroll; shots/C2-presences-390.png |
| C3 | ✅ | « Corriger » footer inside 730 px |
| C4 | ✅ | Paramètres → Équipe at 390: no overflow, « Retirer » reachable; shots/C4-settings-390.png |
| C5 | ❌ F-1 | 409 + « Recharger » shown ✓; after « Recharger » the departure still reads 17:00, server says 17:30 |
| C6 | ✅ | « Mois suivant » disabled on novembre; « octobre 2026 » (first ❌ = capitalised label, probe) |
| B12 | ✅ | « Retirer » → borne « n'est pas une borne », header link gone after reload (toast ❌ was the probe: `quoteFr` binds the guillemets with U+202F) |
| B9 | ⏭ | answered by curl before the launch: 401 `terminal_unknown` with no cookie and with a bogus one |
| D5 | ⏭ | answered outside the browser: unfiltered unit suite 6450 passed / 0 failed |

## Findings

### F-1 · major · « Recharger » in « Corriger le pointage » keeps showing the old hours

- **Repro:** /users → Présences → « Corriger » on a line → change that line behind the dialog (a colleague's
  correction; here `update … DepartureUtc + 30 min`) → Motif → Corriger → 409 banner → « Recharger ».
- **Expected:** the departure field shows the line as it is now (17:30), ready to be corrected again.
- **Actual:** it still reads 17:00; two GETs of the sheet returned the new line, the field did not move.
- **Evidence:** probe-c5 log — `POST …/correction 409`, then `GET /team/attendance` ×2, `DEPARTURE after Recharger: 17:00`.
- **Suspected owner:** `web/components/team/attendance-dialogs.tsx` — the « colleague's correction » sync effect
  compares `fresh.source` (still the copy read on open) with `current` (just reloaded) and puts the OLD copy back;
  the save then sends `fresh.source.version`, i.e. the old version, so a second press would 409 again.
- **Regression?** No — new code.

### F-2 · minor · « Mes présences » / a staff file: the three figures leave a grey empty cell

- **Repro:** Mon profil (secretary) at ~560–700 px wide.
- **Expected:** three figures on one row, or a layout with no hole.
- **Actual:** a 2-column strip with a grey slab where the 4th cell would be (shots/A3-mon-profil.png).
- **Suspected owner:** `web/components/team/attendance-panel.tsx` `StatStrip` with three `Stat`s — `kpi-grid`'s
  documented « one short at the base width for 3 » trap.

### F-3 · minor · the schéma card in Mon profil is a centred vertical stack

- **Repro:** Mon profil (secretary), any width.
- **Expected:** one row — icon · « Schéma de pointage / Enregistré » · button (like the other cards).
- **Actual:** icon, title and button stacked and centred (shots/A3-mon-profil.png).
- **Suspected owner:** `web/components/team/schema-setup-card.tsx` — `Card`'s base `flex-col` survives the
  `flex flex-wrap` given to it.

### F-4 · minor · day cards read « Jeudi 5 Novembre »

- **Repro:** /users → Présences at 390 (or any `person`/`mine` list).
- **Expected:** « Jeudi 5 novembre ».
- **Actual:** `capitalize` capitalises the month too (shots/C2-presences-390.png).
- **Suspected owner:** `web/components/team/attendance-panel.tsx` — `DayCards` title `className="capitalize"`.

## Observations (off-plan)

- A line added by an admin (« Ajouter un pointage ») shows « Corrigé » with its motif — consistent with the
  record carrying who entered it and why; left as is.
- `useFreshVersion`'s `mounted` ref is set false by StrictMode's dev double-invoke and never set back, so in
  `next dev` its re-read may never land (pre-existing hook, not this change).

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | the « colleague's correction » sync effect replaced the reloaded line with the copy read on open; the save sent that stale version | `team/attendance-dialogs.tsx` (adopt the fresh read only over the opened copy; save with `current.version`) | 1 dialog, no other consumer |
| F-2 | confirmed | `StatStrip`'s base is two columns, so three figures leave `kpi-grid`'s grey filler hole below 640 px | `team/attendance-panel.tsx` (`grid-cols-3` at the call site) | call site only; the primitive's trap is pre-existing and untouched |
| F-3 | confirmed | `Card`'s base `flex-col` survived `flex flex-wrap` | `team/schema-setup-card.tsx` (`flex-row`) | 1 card |
| F-4 | confirmed | `capitalize` raised the month too | `team/attendance-panel.tsx` (`dayTitle`, first letter only) | the card title only; the table's one-word « Jeu. 5 » keeps `capitalize` |

**Probe fixes carried in walk.mjs:** case-insensitive matches for uppercase/capitalised text (A10, A15, C6), A17 polls for
the list, A11 waits out the realtime re-render and splits its two checks, B12 tolerates U+202F.
**Gates after the last edit:** check:responsive 86/86 ✅ · tsc ✅ · next build ✅ (no backend change; suite 6450/0 from before)

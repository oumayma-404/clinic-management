# QA run 9 — Pointage · review fixes F1–F36 (F18 excluded)

**Date:** 2026-11-09 (Monday, 12:45 → 14:25 Tunis, inside opening hours)
**Cycle:** 1 of at most 3
**Plan:** `qa/plan-review-fixes.md` (49 scenarios: A23 B14 C7 D5)
**Driver:** `qa/walk-review-fixes.mjs` — one launch per drive; 4 drives, the first three ended on probe bugs (table below).
Drive 4 is the result of record (206 ✅ · 1 ❌ · 2 ⏭); its one ❌ (B6) was re-driven alone on a fresh arrangement with
the same file (`ONLY=B6`): 12 ✅.
**Environment:** API `:5096` (started 12:22, not restarted) · web `:3096` next dev (started 12:24, reused) · DB
`clinic_pointage_demo` · my own headless Chrome (playwright-core 1.49.1, `channel: 'chrome'`). The owner's CDP windows
9333 / 9334 were never connected to; both still listening at the end. Peer sessions: not listable from this agent.
**Widths looked at:** 320×568 · 320×640 · 390×844 · 812×380 · 820×1180 · 1180×820 · 1440×900 · 1536×730, plus a coarse
pointer (CDP touch emulation, `matchMedia('(pointer: coarse)')` asserted true) at 390 and 820.
**Status:** RED (1 finding: minor)

## Arrangement (decided before the browser — see the plan)

- The lock was **not live** today: « Accueil » went live at 09:29, after opening, so F6 defers it to tomorrow 09:00
  (`lockState: "from"`), and Ines already had a closed line. The walk made its own borne « QA » and back-dated **its own
  row**'s `ActivatedAtUtc` to 08:30 to start the lock; retiring « QA » (A22) put the chip back on « Actif à partir de
  demain 09:00 ».
- **T** = a throwaway secretary per drive (`QA Pointage 952025` / `810584` / `380618` / `905676`), invited by the admin
  over the API, deactivated at the end. T1 = her ordinary PC, T2 = a second PC (Linux UA) chosen as borne « QA ».

## Scenarios (drive 4 + the B6 re-drive)

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 F9 | ✅ | `#equipe` is the next card after `#horaires`; `/settings#equipe` lands it at the top; shots/A1-equipe-after-horaires.png |
| A2 F7 F8 | ✅ | « Horaires ✓ · Borne ✓ Accueil · Ines Gharbi · Chrome sur Windows · Schémas 1 sur 3 créé · à créer : QA Pointage …, QA Secrétaire 991861 · Blocage « Actif à partir de demain 09:00 » »; « Me guider »; no sideways scroll at 390; shots/A2-setup-checklist-1440.png, -390.png |
| A3 F7 | ✅ | 5 bubbles in order, step 4 opens « Ajouter une borne », « Terminer » → « Vous savez mettre en place le pointage. », stays on /settings; shots/A3-guide-devices-step.png |
| A4 F5 F25 F2 | ✅ | name « Accueil 2 »; Remplace « Aucune : nouvelle borne » + « Accueil »; rows « PC · Chrome sur Linux », « PC · Chrome sur Windows », « Téléphone · Chrome sur Android » (admin), « Tablette · Safari sur iPad », « Cet appareil (Dr Salma Ben Youssef) » last; T's Android phone absent; shots/A4-add-borne-1440.png |
| A5 F10 F3 | ✅ | toast « Borne « QA » en attente de l'appareil de QA Pointage … »; row « En attente depuis HH:mm · … · Chrome sur Linux »; T2 claims on opening the app; admin bell « Borne installée »; T's bell « Cet appareil est une borne » + « Créez votre schéma de pointage »; 991861 told too (SQL); shots/A5-*.png |
| A6 F3 | ✅ | the row opens « Créer votre schéma » on Mon profil, `?schema` removed; shots/A6-create-schema-dialog.png |
| A7 F6 F8 F14 F30 | ✅ | chip « Actif »; lock « Pointez votre arrivée · Bornes : « QA », « Accueil » · Créer votre schéma · Tracez votre schéma » + pad, « Se déconnecter », « Demander un congé », no « Schéma oublié ? »; shots/A7-lock-no-schema-1180.png |
| A8 F14 | ✅ | drawn twice in the lock → « Schéma enregistré. », lock stays with « Schéma oublié ? », no pad; hash stored |
| A9 F1 | ✅ | right password → « Schéma enregistré. », hash replaced (SQL) |
| A10 F4 | ✅ | « Cet appareil est une borne du cabinet. » + « QA Pointage … » above the pad + « Schéma oublié ? » + « Ce n'est pas moi » → `/pointage` tiles, no lock; shots/A10-borne-lock-820.png |
| A11 F13 F1 | ✅ | 1st wrong « Encore 4 essais »; after 5: « Schéma bloqué jusqu'à 13:31 » instead of the pad, no « Réessayer » (T1 too); tile « Schéma bloqué · réessayez dans 15 min » disabled; admin bell « Le schéma de … est bloqué jusqu'à HH:mm après 5 essais. »; Paramètres « Bloqué après 5 essais, jusqu'à … » + Débloquer; Aujourd'hui « Schéma bloqué » + Débloquer → toast, tries 0, schéma kept, pad back on T2; shots/A11-*.png |
| A12 F12 F23 | ✅ | « Pointer son arrivée » → T · today · now; toolbar « Choisir » · today · now (never 08:00); another day empties the hour; shots/A12-record-prefilled.png |
| A13 F24 F35 | ✅ (see F-1) | past open days « Aucun pointage » + « Ajouter »; dimanche not an absence; future rows muted; header links `/users/<id>`; inline « Ajouter » prefills T + 2026-11-07, hour empty; 820 cards say the same; shots/A13-sheet-absences.png, A13-cards-820.png |
| A14 F20 F28 | ✅ | « Votre départ du vendredi 6 novembre… » survives Escape and 35 s idle; then « … du samedi 7 novembre … » under « Vendredi 6 novembre · départ 17:00 »; « Arrivée enregistrée · 13:17 · Borne QA »; SQL both declared (17:00, 17:30, source 1); T1's lock lifts by itself; shots/A14-declaration-settled.png |
| A15 F19 | ✅ | chip « Arrivée à HH:mm »; T1 dialog « Pointez votre départ à la borne « QA » ou « Accueil » · Arrivée pointée à HH:mm. » + « Fermer » only; at 390 the hour only (words sr-only); T2 dialog has the pad; after departure « Départ à HH:mm »; shots/A15-*.png |
| A16 F16 | ✅ | « Pointage » opens a dialog « Borne QA » over /patients (same page); Escape on tiles closes, on the pad returns to tiles; départ then arrivée, each closes itself; chip follows; 2 lines today (SQL); shots/A16-pointage-dialog.png |
| A17 F29 F30 F31 | ✅ | « Mes présences » in T's rail, not the admin's; « Enregistré » + month; today « 13:17 – en cours » and « Borne : 13:17 – 13:17 · corrigé par Dr Salma Ben Youssef »; 6–7 nov « Ajouté par … » + « Déclaré »; hint « sans la journée en cours »; shots/A17-mes-presences.png |
| A18 F15 | ✅ | off a borne: « Pointez votre départ à la borne « QA » ou « Accueil » », footer exactly « Annuler » + « Se déconnecter » (filled); on the borne: « Pointez votre départ » + pad + « Annuler » + « Se déconnecter sans pointer » → « Départ enregistré · À HH:mm. » → /login with « Pointage »; shots/A18-*.png, C6-*.png |
| A19 F17 F35 | ✅ | « Dr Nadia Trabelsi pointe ? … devra pointer son arrivée avant de travailler. » → « pointe désormais »; her row « Voir sa fiche »; file header « Pointe » + « Schéma à créer »; off without a confirm; shots/A19-*.png |
| A20 F35 | ✅ | T's row « Voir sa fiche et ses congés »; Présences header « Pointe » (on) + « Schéma créé » + « Réinitialiser le schéma » + her month; shots/A20-staff-file.png |
| A21 F36 | ✅ | Présences « novembre 2026 », › disabled, ‹ → « octobre 2026 »; « Mes congés », « Mes présences » and Présences use one 32 px stepper (44 px coarse); shots/A21-D3-conges-tab.png |
| A22 F2 F22 | ✅ | Remplace « QA » names it « QA » and retitles; « Cet appareil est maintenant la borne « QA ». »; old « QA » retired in the same save (SQL); « Cet appareil ne pourra plus pointer. » without « L'app ne sera plus bloquée »; « Borne « QA » retirée. »; chip back to « Actif à partir de demain 09:00 »; only « Accueil » live |
| A23 F11 F12 F30 | ✅ | Ines « Départ à 09:35 »; 991861 « Pas encore de pointage » + « Schéma à créer » + « Pointer son arrivée »; no « Présente » / « Pas encore arrivée » anywhere; shots/A23-today-1440.png |
| B1 F22 | ✅ | « L'appareil de la borne « Accueil » (Ines Gharbi · Chrome sur Windows) ne pourra plus pointer. … L'app ne sera plus bloquée. »; Retour retires nothing (SQL) |
| B2 F25 | ✅ | « Une borne s'appelle déjà « accueil ». », no step-up |
| B3 | ✅ | « Choisissez l'appareil qui sera la borne. », no step-up |
| B4 F25 F5 | ✅ | API 409 `borne_name_taken`; 400 `borne_device_phone`; nothing written |
| B5 F1 | ✅ | wrong password → back on « Mot de passe de votre compte » with the server's refusal; hash unchanged; FailedLoginAttempts 0 → 1 |
| B6 F10 F2 | ✅ (re-drive) | toast « … en attente de l'appareil de QA Pointage 905676. »; row « En attente depuis HH:mm · … · Safari sur iPad »; session signed out → bell « Borne non reçue … Choisissez un autre appareil. » + red row + « Choisir un autre appareil » → « Remplacer la borne « QA tablette » », preset, signed-out device gone; saved onto Dr Salma's phone, old row retired in the same save (SQL); « Retirer » → toast; shots/B6-*.png |
| B7 F33 | ✅ (simulated 500) | « L'équipe du jour n'a pas pu être chargée. » + « Réessayer », month still shown, Réessayer brings the list; shots/B7-today-failed.png |
| B8 F12 F11 F9 | ✅ (simulated) | « Aucun pointage depuis l'ouverture » + link `/settings#equipe`; « Borne en attente de son appareil »; « Cabinet fermé aujourd'hui »; « Personne ne pointe dans ce cabinet » + link; shots/B8-setup-line.png |
| B9 F9 | ✅ (simulated) | tabs stay with congés off; « Les demandes de congé ne sont pas activées » + « Activer dans Paramètres » → `/settings#equipe` |
| B10 F32 | ✅ (simulated 500) | « Les congés n'ont pas pu être chargés. » and « Présences » still rendered |
| B11 F26 | ⏭ | not exercised — « Aucune borne active » and 409 `borne_last_while_present` need « Accueil » (the owner's borne) retired |
| B12 F27 F30 | ⏭ | not exercised — the « Départ non pointé » reminder fires at 17:15; moving the hours is the owner's data |
| B13 F3 F27 | ✅ | « Borne installée », « Schéma bloqué », « Borne non reçue » each open `/settings#equipe` |
| B14 F11 F30 | ✅ | tiles Ines « Départ à 09:35 » · 991861 « Schéma à créer » (disabled) · T « Pas encore de pointage »; shots/B14-tiles.png |
| C1 | ✅ | T1 lock at 320×568 / 390×844 / 1536×730: no sideways scroll, pad inside, ways out reachable; shots/C1-*.png |
| C2 F21 | ✅ | borne lock pad 228 px at 320×568, 152 px at 812×380 (≤ 40 dvh), « Demander un congé » reachable; shots/C2-*.png |
| C3 | ✅ | « Ajouter une borne » at 320×568 and 1536×730: no overflow, « Enregistrer la borne » on screen; shots/C3-*.png |
| C4 | ✅ | coarse: device rows, « Schéma oublié ? » / « Ce n'est pas moi », « Débloquer » / « Pointer son arrivée », month stepper all ≥ 44 px; shots/C4-*.png |
| C5 F34 | ✅ | 320 px: names wrap, none clipped; shots/C5-today-320.png |
| C6 | ✅ | sign-out prompt with the pad: both footer buttons and pad on screen at 1536×730 and 320×568 |
| C7 | ✅ | Présences tab at 320 / 390 / 820 / 1180 / 1536×730: `main` never scrolls sideways; cards below 1024, table above |
| D1 | ✅ | `/pointage` tiles + an arrival (A14) + a departure and an arrival through the dialog (A16), « … · Borne QA » |
| D2 | ✅ | « Corriger » → « Pointage corrigé. », line « 13:16 + Corrigé » |
| D3 | ✅ | `/conges` with her request and its month, no lock; `/users?onglet=conges` lists the request |
| D4 | ✅ | dashboard « Équipe aujourd'hui » = Présences (Schéma à créer, Départ à 09:35); shots/D4-dashboard.png |
| D5 | ✅ | Mon profil « Schéma de pointage · Enregistré » + « Changer votre schéma » + « Mes présences » |

## Findings

### F-1 · minor · « Aucun pointage » is shown for days before the person could point (F24)

- **Repro:** Utilisateurs → Présences, novembre, at 1440 — any account created today, or any day before the cabinet's
  first borne (6 nov).
- **Expected:** an absence only on a past open day the person was expected to point.
- **Actual:** « QA Pointage 380618 », created at 13:12 today, reads « Aucun pointage » + « Ajouter » on lun. 2 → jeu. 5;
  Ines and 991861 read the same on 2–5 nov, before the pointage existed. In a cabinet's first month every secretary
  looks absent on every earlier day.
- **Evidence:** shots/A13-sheet-absences.png, shots/A11-today-blocked.png (sheet under the list).
- **Suspected owner:** `web/components/team/attendance-panel.tsx:384` — `absent = onAdd && past && day.isOpenDay &&
  entries.length === 0 && !leave` has no start bound (account creation / tracked-since / first borne live); the sheet
  DTO carries no such date.
- **Regression?** No — new with F24.

## Observations (off-plan)

- **O-1** · A `/settings` load fired right after closing « Ajouter une borne » rendered the form with empty clinic fields
  and no « Équipe » card for 20 s+, at the same step in all 4 drives; a reload fixed it. Likely the dialog's history
  guard `back()` racing a scripted `goto` — a person cannot navigate that fast. Not chased.
  shots/OBS-settings-empty-after-dialog-close-390.png.
- **O-2** · 320 px « Aujourd'hui »: the name breaks one word per line beside « Pas encore de pointage » (C5 passes —
  nothing clipped). shots/C5-today-320.png.
- **O-3** · F17 shipped as a confirm only (no « App bloquée avant pointage » on the row) and F22 names « L'appareil de
  la borne « Accueil » (Ines Gharbi · Chrome sur Windows) » rather than « le PC d'Ines » — both allowed by the review.

## Probe bugs fixed during the run (not product defects)

| Drive | Row | Symptom | Real cause |
|-------|-----|---------|------------|
| 1 | C4 | page stuck after enabling coarse | a CDP device-metrics override fought Playwright's viewport across a navigation → touch emulation only, never navigate while on |
| 2 | C4 ×3 | `(pointer: coarse)` false | detaching the CDP session reverted the emulation → kept attached until « off » |
| 2 | A9 | « schéma replaced » false | A8's toast was still up; the SQL read ran before A9's save → wait for the hash to move |
| 2 | A13 · A17 · A20 · D4 | column / label « missing » | `text-transform: uppercase` reaches `innerText` (« QA POINTAGE … », « ÉQUIPE AUJOURD'HUI », « JOURS POINTÉS ») → case-insensitive |
| 2 | A14 | no settled list | cascade of A13 (7 nov never added) |
| 2 | A15 | strict-mode violation | the dialog's ✕ is also named « Fermer » → footer button |
| 2 | A21 · D3 | stepper not found | plan error: the admin « Congés » tab has no month; « Mes congés » shows one only with a request → arranged a request, compared Mes congés / Mes présences / Présences |
| 3 | A22 | SQL mismatch; strict mode | earlier drives' « QA » rows counted; « Retirer la borne QA » prefix-matched « QA tablette » → run-scoped SQL, exact names |
| 3 | B6 | bell row absent | 99+ rows, read slower than 1.2 s → wait for the row, reopen once |
| 4 | B6 | T's iPad row not clickable (session live per SQL) | not attributed; B6 now makes its own fresh sessions — re-driven alone: 12 ✅ |
| 1–4 | C3 | `/settings` section missing | O-1 → one reload with a debug shot |

## Demo state changed (all own artefacts)

- 4 throwaway secretaries `QA Pointage 952025 / 810584 / 380618 / 905676` created (invitation e-mails to Mailpit) and
  **deactivated**; their lines, congé requests and the bell rows the run created (categories 20–23 naming them, 25–28)
  deleted. Their accounts, revoked borne rows (« QA », « QA tablette », 11 in all) and audit rows remain as history.
- While a drive ran, « QA » back-dated made the lock live cabinet-wide (Ines had a line; 991861 has no session — nobody
  else was locked), and the admin bell showed « Borne installée / non reçue / Schéma bloqué » rows (now deleted).
- Dr Nadia was tracked then untracked; her override is back to NULL.
- Untouched: « Accueil » (activation 09:29, last use 09:33), Ines's line 09:33–09:35, the switches (pointage, lock,
  congés all on), hours. End state: live bornes « Accueil » only, 0 live sessions.

## Re-running

`node walk-review-fixes.mjs` makes its own throwaway, back-dates its own borne and cleans up; `ONLY=B6,A22` re-drives
rows on a fresh arrangement. Logs: `…/scratchpad/run9-drive3.log`, `run9-drive4.log`, `run9-b6.log`.

## Addendum — F-1 fixed (2026-11-09, after the run)

- **Cause:** « Aucun pointage » had no start date, so every past open day counted, including days before the cabinet's first punch and before the person's account existed.
- **Fix:** `AttendancePersonDto.countsFrom` = the later of the cabinet's first line (`IAttendanceEntryRepository.GetFirstDayAsync`) and the account's creation day; null before anyone ever pointed. The sheet marks an absence only from that day (`attendance-panel.tsx`).
- **Verified:** unit test `An_Empty_Day_Counts_As_Absent_Only_From_The_First_Line_And_The_Account_Creation`; full suite 6 888 passed · 0 failed; `tsc` + `check:responsive` 86/86 green; the live sheet answers `countsFrom = 2026-11-06` for both secretaries (first line in the demo cabinet), so 2–5 Nov no longer read as absences. Plan not re-run in full: a one-condition fix, checked at the API and in the gates.

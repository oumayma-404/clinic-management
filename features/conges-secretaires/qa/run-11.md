# QA run 11 — Congés · review fixes F1–F40

**Date:** 2026-11-09 (lundi, 21:24 → 21:50 Tunis)
**Cycle:** 1 of at most 3 (first pass on the review fixes)
**Plan:** `qa/plan-review-fixes.md` (53 rows: P3 · A25 · B12 · C6 · D5)
**Driver:** `qa/walk-review-fixes.mjs` — one launch, 203 checks. D4 re-driven alone after a probe fix
(`scratchpad/redrive-d4.mjs`); one read-only probe as Ines to triage C5 (`scratchpad/probe-sheet.mjs`).
**Environment:** API `:5096` (worktree `bin/Debug`, started 20:59, newer than every source) · web `:3096` next dev
(started 21:00, reused) · DB `clinic_pointage_demo` (migration `AddStaffLeaveHalfDayPartAndCancelReason` applied) ·
my own headless Chrome (playwright-core, `channel: 'chrome'`). The owner's CDP windows 9333 / 9334 never touched;
both still listening at the end. Peer sessions: not listable from this agent.
**Widths looked at:** 320×640 · 390×844 · 820×1180 · 1180×820 · 1440×900 · 1536×730, plus `(pointer: coarse)` (CDP touch
emulation, asserted true) at 390 and 820.
**Status:** RED (4 findings, all minor)

## Arrangement and what was reverted

- 5 throwaway secretaries (`QA Congés 846726`, `… Historique`, `… Perdus`, `… Règle`, `… Solde`) invited over the API,
  their rules / openings / absences set over the API (P1: all 200). T3's 2025 opening and absence were accepted by the
  server — no SQL was needed to arrange anything.
- **Pointage switched OFF then back ON** through Paramètres (A23). Turning it on re-armed the lock
  (`AttendanceLockArmedAtUtc` = now); reset to `null` by SQL (own artefact) — flags before and after: `t,t,t,null`.
- **Cleanup:** the 5 throwaways (+1 for the D4 re-drive) deactivated over the API; by SQL, their 13 requests, 8
  « Droit à congé » lines and 21 notifications (bell rows in the owner's admin bell too) deleted; every session the run
  opened is ended. Ines: read-only — hash of her 4 requests + 2 lines identical before / after
  (`747b39bf…`). Mailpit keeps the throwaways' e-mails (dev sink).

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| P1 | ✅ | 23 arrange calls, all 200 |
| P2 | ✅ | Ines hash unchanged (also after the triage probe) |
| P3 | ✅ | `true,true,true,null` before and after |
| A1 F8 | ✅ | « 22 jours restants », « Aucune demande pour le moment », header button at 1180, fixed bottom button at 390, « Voir le relevé » folded — shots/RF-A1-first-visit-1180.png, -390.png |
| A2 F9 | ✅ | stepper 2026, « Année suivante » disabled, 11 « Acquis — » lines → solde 22 j; 2025 « Aucun mouvement en 2025 » |
| A3 F15 | ✅ | Acquis 22 − Pris 0 = Reste 22; « À venir … +2 j »; « À prendre avant le 31/12 : 24 j »; rule + prochain |
| A4 F20 F22 | ✅ | picker, « Il lui restera 19 j au 9 novembre » → 20 j after « − » ×2; SQL counted 2 of 3, entered by admin |
| A5 F28 F3 | ✅ | « 5 jours · 14 → 18 décembre · Il restera 17 j au 14 décembre », slot between Au and Motif — shots/RF-A5-form-R1-1180.png |
| A6 F14 | ✅ | « Il restera 19 j au 21 décembre · 14 j avec votre demande en attente » |
| A7 F39 | ✅ | Journée · Matin · Après-midi on Du = Au only; « 0,5 jour · Jeudi 26 novembre · matin · Il restera 19,5 j … · 11,5 j avec … »; SQL HalfDayPart 1 / 2 (see F-3 at 320) |
| A8 F8 | ✅ | card → En cours et à venir → Voir le relevé → Historique; upcoming in date order; all « Retirer la demande »; « En attente : 9 j » apart — shots/RF-A8-mes-conges-1180.png |
| A9 F19 F40 | ❌ F-1 | strip « 22 / 22 j » + bar, « Droit à définir », Ines « 19 / 23 j » ✅ — but T reads « 20 j en attente » vs her card's 9 j |
| A10 F19 F36 F12 | ✅ | « Droit non défini » → her file; « Demande aussi ces dates : … (en attente) » both ways; R1 « 17 jours au 14 décembre »; no « Annuler » — shots/RF-A10-cards.png |
| A11 F16 F23 | ✅ | « Solde après au 14 décembre · 17,5 jours »; « 4,5 j retirés du solde (5 jours demandés) »; hers « 4,5 j retirés de votre solde » |
| A12 F10 F12 F17 | ✅ | « Annuler le congé de … du 21 au 23 décembre ? » · « 3 jours reviennent sur son solde. »; hers « Annulé par Dr Salma Ben Youssef · « Erreur de planning » » — shots/RF-A12-admin-cancel.png |
| A13 F10 | ✅ | past absence cancelled with « Saisie en double » (SQL status 3 + motif) |
| A14 F29 | ✅ | « Retirer la demande le 27 novembre (après-midi) ? », toast « Demande retirée », « Demande retirée par vous » |
| A15 F7 F32 | ✅ | first rule opens on « 1er janvier 2026 · par an · Perdus »; untouched Retour = no prompt; live « Acquis aujourd'hui : 20 j · prochain +20 j le 1er janvier 2027 » |
| A16 F11 F5 F32 | ✅ | Modifier seeds 20 · par an · 1er janvier 2026; par mois → 1er décembre 2026; live « prochain +2 j le 1er janvier 2027 »; Acquis 20 (no 22); « 20 j par an … → 2 j par mois à partir du 1er décembre » — shots/RF-A16-T5-file.png |
| A17 F24 F6 | ✅ | « Il lui reste [ ] j au [date] », no visible description, live « 8 j »; Acquis 10 · Pris 4 « dont 4 j inclus … » · Prévus 2 · Reste 8; March greyed « sans effet sur le solde » (see F-2) — shots/RF-A17-T6-releve.png |
| A18 F1 F6 | ✅ | Acquis 12 · Pris 5 « dont 5 j avant le début du droit à congé » · Reste 12 (see F-2) — shots/RF-A18-T2-file.png |
| A19 F2 F9 | ✅ | 2026 « 20 jours restants », « 6 j non pris en 2025 : perdus » first relevé line, no « Report de 2025 »; 2025 opening 10 · −4 · perdus −6 — shots/RF-A19-T3-2026.png, -2025.png |
| A20 F9 | ✅ | her /conges: same first line; 2025 readable |
| A21 F4 | ✅ | card and dialog « Solde 2025 après · 3 jours »; after: « 3 j non pris en 2025 : perdus » |
| A22 F18 | ✅ | Ines / T / T2 « — 2 j par mois », T3 « — 20 j par an », T5 « Droit à définir » + « Définir »; « Me guider » bubble — shots/RF-A22-settings-1440.png, -390.png |
| A23 F21 | ✅ (after triage) | « Équipe aujourd'hui » « 7 demandes de congé en attente » → /users?onglet=conges — shots/RF-A23-dashboard-pointage-off.png |
| A24 F16 F29 | ✅ (see F-4) | bell: « … accordé · 4,5 j retirés de votre solde. », « le 26 novembre matin … 0,5 j retiré … », refusal quoting « Période chargée », « … annulé par … : « Erreur de planning ». »; e-mails « Congé accepté : du 14 au 18 décembre » (Retirés 4,5 jours), « … le 26 novembre matin », « Congé refusé : du 4 au 15 janvier », « Congé annulé : du 21 au 23 décembre » — shots/RF-A24-bell.png |
| A25 F15 | ✅ | Ines 1536×730: Acquis 23 − Pris 1 − Prévus 3 = Reste 19, « À prendre … 21 j », order — shots/RF-A25-ines-1536x730.png |
| B1 F27 | ✅ | « Le cabinet est fermé ce jour-là. » red before sending; refused on send; nothing written |
| B2 F26 | ✅ | « Vous avez déjà une demande en attente du 14 au 18 décembre. » before any motif; nothing written |
| B3 F28 | ✅ | « Ne compte pas sur votre solde » |
| B4 F28 | ✅ | « Décompte indisponible » (preview answered 500 by route) |
| B5 F13 | ✅ | « 9 j de dépassement au 4 janvier 2027 » red; card « Solde 2027 après · 9 jours de dépassement » red |
| B6 | ✅ | empty motif refused; « Motif du refus : « Période chargée » » on hers |
| B7 | ✅ | « Indiquez le motif de l'annulation. », SQL still accepted |
| B8 F23 | ✅ | « Abandonner les modifications ? » after a button-only edit in 4 dialogs (absence picker, Matin, accept « − », Reportés) |
| B9 F25 | ✅ | Dr Nadia: no congé section, « Présences » present |
| B10 | ✅ | « 1 jour de dépassement » red, « Dépassement 1 j », relevé « dépassement 1 j » red, strip red + red bar |
| B11 | ✅ | started absence: no button for her |
| B12 | ✅ | T5 refused |
| C1 | ✅ | /conges, tab, a file, Paramètres × 5 widths + form open: `main` and document never wider |
| C2 F33 | ✅ | Type in 2 columns, equal one-line heights at 320, 820, 1180, 1536×730 |
| C3 | ✅ | bottom sheet at 320 with « Envoyer la demande » on screen; on screen at 1536×730 |
| C4 F30 | ✅ | every strip name ≥ 90 px at 320 — shots/RF-C4-strip-320.png |
| C5 | ✅ | coarse true; stepper, row buttons, form radios, footer, decision buttons ≥ 44 px (inputs 44 · 44 · 90) |
| C6 | ✅ | « Solde 2027 après », « Solde 2025 après » |
| D1 | ✅ | SQL: absence 3 · R3 1 · R4 3 · R1 1 (4,5) · R2 3 · R5 2; T3 both 1; T5 2 |
| D2 | ✅ | calendar 26 « congé accepté, matin », 27 cleared, today ringed, legend |
| D3 | ✅ | « Ajouter un pointage » — shots/RF-D3-presences.png |
| D4 | ✅ (re-drive) | pointage on: heading + « 1 demande de congé en attente » — shots/RF-D4-redrive.png |
| D5 | ✅ | T: strip 20 / 22, card 20, R1 17 then card 17 after decisions — one fold |

## Findings

### F-1 · minor · the team strip counts waiting days of every year
- **Repro:** T has 9 j waiting in 2026 and an 11 j request in January 2027 → `/users?onglet=conges`.
- **Expected:** under « Équipe · congé payé 2026 », « 9 j en attente » — the figure her card shows (« En attente : 9 j »).
- **Actual:** « 20 j en attente » (captured: `20 / 22 j · 20 j en attente · Aucun congé prévu`). Same for a waiting request
  of a past year (T3's 2025 one counted under 2026).
- **Evidence:** walk log A9; shots/RF-A10-cards.png (the 2027 card).
- **Suspected owner:** `api/ClinicManagement.Application/Features/Team/Queries/GetTeamLeaveOverviewQuery.cs:86` —
  `PendingDays` sums every pending congé payé, no year filter (the card's `Pending` reads `u.Day.Year == year`).
- **Regression?** No — new with F19.

### F-2 · minor · the « − » of « Pris » drops beside its note
- **Repro:** a file where « Pris » carries « dont … » (T6 after « Fixer le solde », T2).
- **Expected:** « 10 j − 4 j − 2 j = 8 j », each operator beside its value.
- **Actual:** the minus before « Pris » sits at the bottom of the note, so the row reads « 10 j  4 j  − 2 j = 8 j » and
  « 12 j  5 j = 12 j ».
- **Evidence:** shots/RF-A17-T6-releve.png, shots/RF-A18-T2-file.png.
- **Suspected owner:** `web/components/staff-leave/staff-leave-balance-card.tsx` `Term` — `items-end` on the `li`
  aligns the operator to the note's last line.
- **Regression?** No — new with F6's note.

### F-3 · minor · « Après-midi » wraps at 320 px
- **Repro:** « Demander un congé » at 320×640, Du = Au.
- **Expected:** « Journée · Matin · Après-midi » on one line each (F33's rule for the Type control).
- **Actual:** « Après- / midi » on two lines; the three options are 87 px wide and grow to 40 px. Fine at 390.
- **Evidence:** shots/RF-C2-form-320x640.png, shots/RF-PROBE-320-mouse.png (Ines, read-only).
- **Suspected owner:** `web/components/staff-leave/staff-leave-request-dialog.tsx` — the `DAY_PARTS` `ModeSegmented`.
- **Regression?** No — new with F39.

### F-4 · minor · a next-year congé loses its year in the bell and the e-mail
- **Repro:** refuse T's 4 → 15 janvier 2027 request.
- **Expected (F29, one date style):** the year named as on every screen (« 4 → 15 janvier 2027 », « Solde 2027 après »).
- **Actual:** bell « Votre demande de congé du 4 au 15 janvier (11 jours) est refusée … »; e-mail subject « Congé refusé :
  du 4 au 15 janvier ».
- **Evidence:** walk log A24; shots/RF-A24-bell.png.
- **Suspected owner:** `api/ClinicManagement.Application/Common/FrenchDayPeriod.cs` `Words` names the year only when the
  two ends differ (shared with the doctor congés — blast radius); the web's `staffLeavePeriodLabel` names it when it is
  not this year.
- **Regression?** No — the rule predates the fixes; F29 is what makes it visible.

## Observations (off-plan, not chased)

- The admin bell shows « 99+ » on the demo cabinet.
- The rule dialog still carries a visible description « Une nouvelle règle remplace l'ancienne à partir de sa date. ».
- With an absorbed congé the card's terms do not add up by eye (10 − 4 − 2 = 8): the owner's F6 design, explained only
  by the « dont » note — F-2 makes it harder to read.
- 18 headless Chrome processes from earlier sessions (started 2026-11-08 23:06 and 2026-11-09 15:28) are still alive;
  not mine, left alone.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| A23, D4 | « Équipe aujourd'hui » not found | the heading is CSS-uppercased, so `innerText` reads « ÉQUIPE AUJOURD'HUI »; regex made case-insensitive. A23 confirmed by its screenshot; D4 re-driven alone → PASS |
| C5 | the coarse-pointer shot shows no sheet | taken mid slide-in; a read-only probe as Ines measured the sheet at y 84 → 844 under `(pointer: coarse)`; the walk now waits before the shot |

## Addendum — the four findings fixed (2026-11-09, after the run)

| # | Fix | Verified |
|---|---|---|
| F-1 | The team strip's « en attente » is the card's own figure for the year (`balance.Pending`), not every waiting request of every year | full suite green |
| F-2 | The balance card's operator sits on the value's line (« Pris / − 1 j »), so a note under a figure no longer pulls the « − » down | screenshot at 320 px: « Acquis 23 j − 1 j − 3 j = 19 j » |
| F-3 | « Journée · Matin · Après-midi » uses tighter padding below `sm:` | at 320 px all three buttons measure 36 px tall (one line) |
| F-4 | Every bell/e-mail sentence names the year (« du 4 au 15 janvier 2027 », « le 16 novembre 2026 matin ») | 21 expected sentences updated; full suite 6 954 passed · 0 failed |

Gates after the last edit: unit suite 6 954 passed · 0 failed · 6 skipped; `tsc` clean; `check:responsive` 86/86. Plan not re-run in full: four contained fixes, each checked at its own level.

# QA plan — Congés · review fixes F1–F40 (run 11)

**Written:** 2026-11-09 (lundi 9 novembre 2026, cabinet ouvert lun–sam 09:00–17:00, fermé le dimanche)
**Change under test:** worktree `conges-secretaires`, uncommitted — `review-fixes-plan.md` (F1–F33, F35, F36, F39, F40;
F34 / F37 / F38 skipped on purpose)
**Rendering files in the diff:** `app/conges/page.tsx`, `app/users/[userId]/page.tsx`, `app/users/page.tsx`,
`components/staff-leave/*` (9), `team-modules-card.tsx`, `dashboard/dashboard-page.tsx`, `dashboard/team-today-block.tsx`,
`lib/staff-leave.ts`, `lib/api/team.ts`, `lib/hooks/use-staff-leave.ts`, `lib/guides/conges.ts`
**Blast-radius table:** `review-fixes-plan.md` § Blast radius (6 rows; #1 fold → every reader, #3 day wording, #5 cancel,
#6 relevé on both surfaces)
**Budget:** large (ledger + money-like balances, many surfaces) → tiers A–D · 50 rows
**Stack:** web :3096 (next dev) · API :5096 · DB `clinic_pointage_demo` (the owner's demo). My own headless Chrome
(playwright-core, `channel: 'chrome'`), one context per person; the owner's CDP windows 9333/9334 are never touched.
**Driver:** `qa/walk-review-fixes.mjs` · shots `qa/shots/RF-*.png` · report `qa/run-11.md`

## People (throwaway secretaries, created through `POST /users` + invitation, deactivated at the end)

| Who | Arranged (API, the product's own wire) | Used for |
|---|---|---|
| **T** « QA Congés ‹stamp› » | rule 2 j par mois, perdus, from 1 Jan 2026 | her own screens: F8, the request form, R1–R5, decisions, cancel, bell, e-mail |
| **T2** « … Historique » | absence 2 → 6 mars 2026 (5 j) accepted, THEN rule 2 j/mois from 1 juin 2026 | F1 + F6 « avant le début du droit à congé » |
| **T3** « … Perdus » | Solde de départ 10 j au 6 nov 2025 · absence 17 → 20 nov 2025 (4 j) · rule 20 j par an, perdus, from 1 Jan 2026 · her own waiting request 29 → 31 déc 2025 (3 j) | F2, F9 (relevé year, LostLastYear), F4 |
| **T5** « … Règle » | no droit · her own waiting request 21 déc 2026 (1 j) | F19 « Droit à définir / Droit non défini », F7/F32/F11/F5 rule dialog, F36, refuse |
| **T6** « … Solde » | absences 2 → 5 mars 2026 (4 j) and 14 → 15 déc 2026 (2 j), no droit | F24 « Fixer le solde », F6 « inclus dans le solde de départ », overdraft |
| **Ines** (the owner's) | **read-only** | the balance card on real demo data, « Mes congés » order |

T's requests (through her own browser): **R1** CP 14 → 18 déc (5 j) · **R2** CP 21 → 23 déc (3 j) · **R3** CP 26 nov matin ·
**R4** CP 27 nov après-midi · **R5** CP 4 → 15 janv 2027 (12 j). Admin absence for T: CP 2 → 4 nov (3 j, counted 2).

Hand-computed ledger (T, today): acquis 22 (janv–nov) · +2 le 1er déc · after the absence (2) reste 20.

## Answered before the browser (api / sql)

| ID | Check | Expected | Layer |
|----|-------|----------|-------|
| P1 | arrange calls | every arrange call 200; T3's 2025 opening and 2025 absence accepted by the server | api |
| P2 | Ines snapshot | her 4 requests + 2 entries hashed before the walk; identical after | sql |
| P3 | clinic flags | `LeaveRequestsEnabled`, `AttendanceEnabled`, `AttendanceLockEnabled` = t,t,t and `AttendanceLockArmedAtUtc` null — before and after | sql |

## Tier A — happy path

| ID | Finding | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|---------|-------|-----------------------|-------|--------|
| A1 | F8 | T `/conges` | first visit, no request | card « 22 jours restants »; « Aucune demande pour le moment »; header « Demander un congé » at 1180 and the fixed bottom one at 390 (header one hidden); « Voir le relevé » present and folded | browser | 1180, 390 |
| A2 | F9 | T `/conges` relevé | « Voir le relevé » | stepper « 2026 », « Année suivante » disabled, 11 « Acquis — <mois> » lines; « Année précédente » → « Aucun mouvement en 2025 » | browser | 1180 |
| A3 | F15 | T card | read it | Acquis 22 j = Reste 22 j (no Prévus), « À venir d'ici le 31/12 : +2 j », « À prendre avant le 31/12 : 24 j », « Droit : 2 j par mois · non pris perdus au 31/12 · prochain : +2 j le 1er décembre » | browser | 1180 |
| A4 | F20 F22 | admin `/users?onglet=conges` | « Ajouter une absence » (tab) → Secrétaire T → 2 → 4 nov → « − » twice | picker « Choisir une secrétaire… »; slot « 3 jours … Il lui restera 19 j » then « 20 j » after the count goes 3 → 2; toast « Absence enregistrée »; SQL `CountedDays=2`, accepted, entered by admin | browser + sql | 1440 |
| A5 | F28 F3 | T request form | Congé payé 14 → 18 déc | slot directly under Du/Au (above Motif): « 5 jours · 14 → 18 décembre · Il restera 17 j au 14 décembre »; send → toast « Demande envoyée » « du 14 au 18 décembre » | browser | 1180 |
| A6 | F14 | T request form | R2 21 → 23 déc | « Il restera 19 j au 21 décembre · 14 j avec votre demande en attente »; sent | browser | 1180 |
| A7 | F39 | T request form | Du = Au → « Journée · Matin · Après-midi »; R3 26 nov Matin, R4 27 nov Après-midi | Du ≠ Au hides it; Matin → « 0,5 jour · Jeudi 26 novembre · matin · Il restera 19,5 j au 26 novembre · 11,5 j avec … »; cards « Jeudi 26 novembre · matin », « Vendredi 27 novembre · après-midi »; SQL `HalfDayPart` 1 / 2 | browser + sql | 1180 |
| A8 | F8 | T `/conges` with requests | read the page | order: card → « En cours et à venir » → « Voir le relevé » → « Historique »; upcoming sorted by date; card « En attente : 9 j » apart, Reste 20 | browser | 1180 |
| A9 | F19 F40 | admin team strip | read T, T5, T6, Ines | T « 20 / 22 j » + « 9 j en attente » + a progress bar; T5 « Droit à définir » + « 1 j en attente »; T6 « Droit à définir » + « 4 jours pris · 2 j prévus »; Ines « 19 / 23 j » + « Prochain congé : 21 → 23 décembre » | browser | 1440 |
| A10 | F19 F36 F12 | admin « À décider » | read the cards | T5's card « Droit non défini » linking to her file; T5 ↔ T R2 each « Demande aussi ces dates : … (en attente) »; both « Déjà en congé sur ces dates : Ines Gharbi »; R1 « Solde après 17 jours · au 14 décembre »; no « Annuler » on any waiting card | browser | 1440 |
| A11 | F16 F23 | admin Accepter R1 | « − » once (5 → 4,5) | dialog « Solde après au 14 décembre · 17,5 jours »; toast « Congé accepté »; history « 4,5 j retirés du solde (5 jours demandés) »; T's row « Congé payé · 4,5 j retirés de votre solde » + « Accepté par Dr Salma Ben Youssef » | browser | 1440 |
| A12 | F10 F12 F17 | admin, T's file | accept R2, then ⋯ → « Annuler le congé » with motif « Erreur de planning » | title « Annuler le congé de T du 21 au 23 décembre ? », « 3 jours reviennent sur son solde. »; toast « Congé annulé »; T's row « Annulé par Dr Salma Ben Youssef · « Erreur de planning » » | browser | 1440 |
| A13 | F10 | admin, T's file | ⋯ on the PAST absence 2 → 4 nov → cancel, motif « Saisie en double » | accepted although started; T's row « Annulé par Dr Salma Ben Youssef · « Saisie en double » »; before it, T's own row for it had no button | browser | 1440 |
| A14 | F29 | T `/conges` | R4 « Retirer la demande » | button says « Retirer la demande » (not « Annuler »); confirm « Retirer la demande le 27 novembre (après-midi) ? »; toast « Demande retirée »; row « Demande retirée par vous » | browser | 1180 |
| A15 | F7 F32 | admin, T5's file | « Définir la règle » | « À partir du » = « 1er janvier 2026 », « par an », « Perdus »; untouched « Retour » closes with no « Abandonner ? »; 20 → live « Acquis aujourd'hui : 20 j · prochain +20 j le 1er janvier 2027 »; toast « Règle enregistrée »; T5's card « Solde après 19 jours » | browser | 1440 |
| A16 | F11 F5 F32 | admin, T5's file | « Modifier la règle » → par mois, 2 | opens on « 20 · par an · 1er janvier 2026 » with no prompt on an untouched Retour; par mois → « 1er décembre 2026 »; live « … prochain +2 j le 1er janvier 2027 »; card Acquis 20 (not 22), no « À venir », « Droit : 20 j par an · non pris perdus au 31/12 → 2 j par mois à partir du 1er décembre »; reopen → « par mois · 1er décembre 2026 » | browser | 1440 |
| A17 | F24 F6 | admin, T6's file | « Fixer le solde » 10 j today | sentence « Il lui reste [ ] j au [date] », no visible description; live « Après les congés déjà acceptés : 8 j »; toast « Solde fixé »; card Acquis 10 · Pris 4 « dont 4 j inclus dans le solde de départ » · Prévus 2 · Reste 8; box « Congé payé 6 j » + same note; relevé March line greyed « sans effet sur le solde » | browser | 1440 |
| A18 | F1 F6 | admin, T2's file | read | Acquis 12 · Pris 5 « dont 5 j avant le début du droit à congé » · Reste 12 (never negative, no « Dépassement reporté »); March line greyed « sans effet sur le solde » | browser | 1440 |
| A19 | F2 F9 | admin, T3's file | read, step the year | « 20 jours restants », Acquis 20 (not 26), « 6 j non pris en 2025 : perdus », « À prendre avant le 31/12 : 20 j »; relevé 2026 FIRST line « 6 j non pris en 2025 : perdus », then « Droit 2026 », no « Report de 2025 »; ‹ 2025: « Solde de départ » 10 · « Congé · 17 → 20 novembre 2025 » −4 · « Non pris en 2025 : perdus » −6 | browser | 1440 |
| A20 | F9 | T3 `/conges` | « Voir le relevé », step to 2025 | first line « 6 j non pris en 2025 : perdus »; 2025 readable | browser | 1180 |
| A21 | F4 | admin « À décider » | T3's 29 → 31 déc 2025 card, then Accepter | « Solde 2025 après · 3 jours » (not 17) on the card and in the dialog; after: T3 card « 3 j non pris en 2025 : perdus » | browser | 1440 |
| A22 | F18 | admin `/settings` | under the congés switch | « Droit à congé »: Ines « — 2 j par mois », T, T2 « — 2 j par mois », T3 « — 20 j par an », T5/T6 « Droit à définir » + « Définir » to their file; « Me guider » → bubble « Les demandes de congé des secrétaires s'activent ici. » | browser | 1440, 390 |
| A23 | F21 | admin `/` | pointage OFF (switch), then back ON | « Équipe aujourd'hui » « N demandes de congé en attente » → `/users?onglet=conges`; pointage back on, `AttendanceLockArmedAtUtc` reset to null (own artefact) | browser + sql | 1440 |
| A24 | F16 F29 F13 | T bell + Mailpit | after the decisions | bell « … du 14 au 18 décembre est accordé · 4,5 j retirés de votre solde. », « … le 26 novembre matin est accordé · 0,5 j retiré … », refusal « … est refusée : « Période chargée ». », « … a été annulé par Dr Salma Ben Youssef : « Erreur de planning ». »; e-mails « Congé accepté : du 14 au 18 décembre », « Congé accepté : le 26 novembre matin », « Congé refusé : … », « Congé annulé : du 21 au 23 décembre » carrying « Erreur de planning » | browser + api | 1180 |
| A25 | F15 | Ines `/conges` (read-only) | read | « 19 jours restants »; Acquis 23 − Pris 1 − Prévus 3 = Reste 19; « À venir … +2 j »; « À prendre avant le 31/12 : 21 j »; « 3 j retirés de votre solde » on 21 → 23 déc; order card → upcoming → relevé → history | browser | 1536×730 |

## Tier B — alternate paths

| ID | Finding | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | F27 | T form: Du = Au = dim. 15 nov, motif, Envoyer | slot « Le cabinet est fermé ce jour-là. » in red before sending; same sentence on send; nothing written | browser + sql | 1180 |
| B2 | F26 | T form: 16 → 17 déc (inside R1) | slot « Vous avez déjà une demande en attente du 14 au 18 décembre. » before the motif; refused on send; nothing written | browser + sql | 1180 |
| B3 | F28 | T form: Maladie | « Ne compte pas sur votre solde » | browser | 1180 |
| B4 | F28 | T form, preview answered 500 (route-simulated) | « Décompte indisponible », never an empty slot | browser | 1180 |
| B5 | F13 | T form R5 4 → 15 janv 2027 | « 10 j de dépassement au 4 janvier 2027 » in red (no « Il restera »); admin card « Solde 2027 après · 10 jours de dépassement » in red | browser | 1180, 1440 |
| B6 | F12 B7 | admin Refuser R5 | empty motif → « Indiquez le motif du refus. »; « Période chargée » → « Demande refusée »; T's row « Motif du refus : « Période chargée » » | browser | 1440 |
| B7 | F12 | admin cancel R2 with an empty motif | « Indiquez le motif de l'annulation. », nothing sent | browser | 1440 |
| B8 | F23 | button-only edits | request dialog: Matin then Escape → « Abandonner les modifications ? »; accept dialog « − » then Escape → same; rule dialog « Reportés » then Escape → same; absence dialog after picking a secretary → same | browser | 1180 / 1440 |
| B9 | F25 | admin `/users/<Dr Nadia>` | no « Droit à congé », no « Congé payé · 2026 », « Présences » present | browser | 1440 |
| B10 | F13 F19 | admin T6: « Ajouter ou retirer des jours » Retirer 10 · « QA dépassement » | card « 2 jours de dépassement » red, « Dépassement 2 j » red, last relevé line « dépassement 2 j » red; strip « 2 j de dépassement » red with a red bar | browser | 1440 |
| B11 | AC-10 | T `/conges` | the started absence 2 → 4 nov has no button for her | browser | 1180 |
| B12 | D | admin refuses T5's request | toast « Demande refusée » | browser | 1440 |

## Tier C — edge cases

| ID | Case | Expected (observable) | Layer | Widths |
|----|------|-----------------------|-------|--------|
| C1 | widths | T `/conges`, admin `/users?onglet=conges`, T6's file, `/settings`: `<main>` and document never scroll sideways | browser | 320×640 · 390×844 · 820×1180 · 1180×820 · 1536×730 |
| C2 | F33 | the request form's Type: 4 radios in 2 columns, every label on one line, equal heights | browser | 320, 820, 1180, 1536×730 |
| C3 | dialog edges | request form at 320: bottom sheet, « Envoyer la demande » on screen; at 1536×730 too | browser | 320×640 · 1536×730 |
| C4 | F30 | team strip at 320: every name ≥ 90 px wide (no letter column) | browser | 320 |
| C5 | coarse | `(pointer: coarse)` true; request form radios / segmented / footer buttons, decision card buttons, year stepper ≥ 44 px tappable | browser | 390, 820 |
| C6 | year edges | 2027 request « Solde 2027 après »; 2025 request « Solde 2025 après » | browser | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Expected (observable) | Layer | Widths |
|----|------------------|-----------------------|-------|--------|
| D1 | #5 decide / cancel | accept, refuse, admin cancel, self-withdraw all land: SQL statuses Approved / Rejected / Cancelled as driven | sql | — |
| D2 | #3 calendar | T's « Ce mois-ci »: November, today ringed, 26 « congé accepté, matin », 27 gone after the withdrawal, legend | browser | 1180 |
| D3 | pointage | `/users?onglet=presences` renders (« Ajouter un pointage ») | browser | 1440 |
| D4 | dashboard | pointage ON: « Équipe aujourd'hui » renders | browser | 1440 |
| D5 | #1 one fold | team strip, card and pending card agree for T (20 / 22 · 20 restants · 17 after R1) | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| F25's « anyone with congé history » half | needs a real account's role changed away from secretary — the owner's data |
| « Le cabinet est fermé ces jours-là » (plural) | this cabinet closes only on Sunday, so no multi-day range without an open day exists |
| A 409 between two admins (AC-14) | a millisecond race; F37 (codes) deliberately skipped |
| `verify-schema` / unit suite | backend gates of the parent session, not a browser pass |
| Push to a phone | congés are not pushed (DEV-2) |

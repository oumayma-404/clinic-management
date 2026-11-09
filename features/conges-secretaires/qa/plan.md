# QA plan — Congés des secrétaires

**Written:** 2026-11-05
**Change under test:** `feature/conges-secretaires` (uncommitted, worktree `.claude/worktrees/conges-secretaires`, off `origin/main` 3262d2e0)
**Rendering files in the diff:** 19 — `app/conges/*`, `app/users/page.tsx`, `app/users/[userId]/page.tsx`, `components/staff-leave/*` (5), `team-modules-card.tsx`, `user-management.tsx`, `clinic-settings.tsx`, `dashboard-sidebar.tsx`, `dashboard-header.tsx`, `notification-panel.tsx`, `lib/nav.ts`, `lib/zones.ts`, `lib/staff-leave.ts`, `lib/hooks/use-staff-leave.ts`
**Blast-radius table:** `progress.md` § Blast Radius (5 rows marked `must re-test`: #10–#14, plus #2/#7 verify-schema)
**Budget:** small feature, full-stack, new table → tiers A, B, C, D · 30 scenarios
**Stack:** isolated — API :5096 + web :3096 (worktree) on DB `clinic_staffleave_qa` (a dump of `clinic_management`). Two visible Chrome windows: admin (Dr Salma Ben Youssef) and secretary (Ines Gharbi).

**Dates used** (today = jeudi 5 novembre 2026, cabinet open Mon–Sat):
R1 Congé payé 16→18 nov (3 j) · R2 Maladie 25 nov ½ · R3 Congé payé 30 déc→2 janv (4 j, crosses the year) · R4 Congé payé 7→8 déc ·
R5 Autre 14 déc · absence Ines Sans solde 4→6 nov (already started) · absence « QA Secrétaire 991861 » Congé payé 30→31 déc.

## Answered before the browser (sql / api)

| ID | Row | Check | Expected | Layer |
|----|-----|-------|----------|-------|
| P1 | #13 migration | `verify-schema` before vs after, same DB | no new DRIFT line; the 3 `StaffLeaveRequests` index/FK lines go from DRIFT to ok | console |
| P2 | #14 API surface | full unit suite, unfiltered | 0 failed | dotnet test |
| P3 | AC-1 | secretary `POST /api/team/me/leave-requests` with the module off | refused, French sentence, nothing written | api + sql |

## Tier A — happy path

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | admin `/settings` | read the « Équipe » card | switch « Demandes de congé des secrétaires » is OFF | browser | 1440 |
| A2 | secretary rail, admin `/users` | module off | no « Congés » in the secretary's menu; `/users` has no tabs (no « Congés » tab) | browser | 1440 |
| A3 | admin `/settings` | turn the switch ON | toast « Demandes de congé activées. »; the secretary's menu gains « Congés » **without a reload** (AC-13) | browser | 1440 |
| A4 | admin `/users/<Ines>` | Droit annuel = 24 → Enregistrer | toast; card reads « 24 jours restants · 0 jour pris sur 24 jours » | browser | 1440 |
| A5 | secretary `/conges` | Demander un congé: Congé payé 16→18 nov, motif | form states « 3 jours · il restera 21 jours » before sending; after: toast « Demande envoyée », a card « En attente · du 16 novembre au 18 novembre 2026 » | browser | 1440 |
| A6 | sql | one notification for R1 | exactly 1 `StaffNotifications` row, `TargetRole='admin'`, message « Ines Gharbi demande un congé du 16 … (3 jours). » | sql | — |
| A7 | admin bell + tab | open the bell | row « Demande de congé » present; « Utilisateurs » rail badge and « Congés » tab badge read 1; clicking the row lands on `/users?onglet=conges` | browser | 1440 |
| A8 | admin `/users?onglet=conges` | R1 card | shows Ines Gharbi, « En attente », period, « Congé payé · 3 jours », motif, « Solde après : 21 jours » | browser | 1440 |
| A9 | admin | Accepter → Décompter prefilled « 3 » → type « 2,5 » → Accepter | toast « Congé accepté »; R1 moves to « Toutes les demandes » reading « 2,5 jours décomptés (3 jours demandés) » + « Accepté par Dr Salma Ben Youssef » | browser | 1440 |
| A10 | secretary `/conges` | no reload | R1 now « Accepté » (realtime); balance « 21,5 jours restants »; her bell has « Congé accepté » | browser | 1440 |
| A11 | mailpit | mail to qa.secretary@ibnkhaldoun.test | subject « Congé accepté : du 16 … » and body naming 2,5 jours | api | — |
| A12 | secretary → admin | R2 Maladie 25 nov, Demi-journée on | form states « 0,5 jour »; admin Refuser with an empty motif → « Indiquez le motif du refus. », nothing sent; motif « Période chargée » → toast « Demande refusée » | browser | 1440 |
| A13 | secretary + mailpit | R2 | card « Refusé » + « Motif du refus : « Période chargée » »; mail subject « Congé refusé : … » quoting the motif word for word | browser + api | 1440 |
| A14 | admin `/users/<991861>` | Ajouter une absence: Congé payé 30→31 déc | toast « Absence enregistrée »; her file shows « Accepté » + « Saisi par l'admin » | browser | 1440 |
| A15 | secretary → admin | R3 Congé payé 30 déc → 2 janv | form « 4 jours »; admin card shows « Déjà en congé sur ces dates : QA Secrétaire 991861 » (AC-6) | browser | 1440 |
| A16 | admin + secretary | accept R3 as is; then her `/conges` | balance 2026 = 24 − 2,5 − 2 (30, 31 déc) = « 19,5 jours restants · 4,5 jours pris » (AC-12 year split) | browser | 1440 |
| A17 | secretary → admin | R5 Autre 14 déc, accepted by admin; secretary « Annuler » it | confirm names « Annuler le congé le 14 décembre 2026 ? » + « Ce congé était accepté. »; card « Annulé »; admin bell gets « Congé annulé » (AC-10) | browser | 1440 |
| A18 | admin `/users/<Ines>` | Ajouter une absence Sans solde 4→6 nov | « Saisi par l'admin »; on her `/conges` that card offers **no** « Annuler » (started) and the API refuses a cancel | browser + api | 1440 |

### Added with the redesign (2026-11-05) — new surfaces, nothing removed

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A19 | admin `/users?onglet=conges` | « Équipe » strip | Ines « 19,5 / 24 j » + « Prochain congé : 4 → 6 novembre »; a secretary with no allowance reads « 2 jours pris » | browser | 986 |
| A20 | admin « Toutes les demandes » | « Refusées » then « Toutes » | only the refused 25 nov; then all 7 back | browser | 986 |
| A21 | admin `/users/<Ines>` | « Absences acceptées en 2026 » | Congé payé 4,5 j · Sans solde 3 j · Maladie 0 j · Autre 0 j | browser | 986 |
| A22 | secretary `/conges` | month + « Prochain congé » | 16–18 nov marked accepted, 25 nov not, today ringed; « 4 → 6 novembre · en cours » | browser | 546 |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | request form | Du 20 nov, then Au 18 nov → Envoyer | « Le dernier jour doit être le même jour ou après le premier. » under the field, nothing sent | browser | 1440 |
| B2 | request form | Au a year and more later (367 days) | « Un congé ne peut pas dépasser 366 jours — vérifiez l'année. » | browser | 1440 |
| B3 | request form | empty motif | « Indiquez le motif. », dialog stays open | browser | 1440 |
| B4 | request form | Du ≠ Au | no « Demi-journée » switch; Du = Au → it appears | browser | 1440 |
| B5 | request form | 17 nov (inside accepted R1) | refused naming R1: « Vous avez déjà une demande du 16 novembre au 18 novembre 2026. » (AC-4) | browser | 1440 |
| B6 | AC-14 | admin opens Accepter on pending R4; the secretary cancels R4 in her window; admin presses Accepter | banner « Cette demande a déjà reçu une réponse. » with « Recharger »; Recharger closes and R4 reads « Annulé » | browser | 1440 |
| B7 | a doctor | doctor `/conges` reachability | not reached — no doctor session in this walk | — | — |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 phone, coarse | secretary `/conges` at 320×640 with touch | no horizontal scroll; « Annuler » on a card ≥ 44 px tall | browser | 320 |
| C2 | 320 sheet | open « Demander un congé » at 320×640 | dialog is a bottom sheet (bottom edge = viewport bottom); « Envoyer la demande » inside the viewport | browser | 320 |
| C3 | 320 admin | `/users?onglet=conges` at 320 | cards, no horizontal scroll; Accepter/Refuser visible | browser | 320 |
| C4 | 730 tall | refusal dialog at 1536×730 | « Refuser la demande » button inside the viewport | browser | 1536×730 |
| C5 | off again | admin turns the switch off | secretary menu loses « Congés »; `/users` tabs gone; turning it on again shows R1…R5 unchanged (AC-1) | browser | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #10 `/users` tabs | « Comptes » tab | the accounts list renders with its rows; « Fiche » link on secretary rows; at 820 the cards carry « Voir sa fiche et ses congés » in the ⋯ menu | browser | 820, 1440 |
| D2 | #11 settings | Paramètres | « Équipe » card renders among the admin cards, no overflow at 390 | browser | 390, 1440 |
| D3 | #12 rail badge | collapsed rail + phone drawer | the badge shows in the collapsed rail and in the drawer at 390 | browser | 390, 1440 |
| D4 | #13 migration | = P1 | — | console | — |
| D5 | #14 API | = P2 | — | dotnet test | — |

## Added after the review fixes (2026-11-05) — nothing removed

| ID | Finding | Expected (observable) | Layer | Widths |
|----|---------|-----------------------|-------|--------|
| F1 | review #3 | at 820 with the rail open, Annuler / Refuser / Accepter all sit inside the waiting card; no sideways scroll | browser | 820 |
| F2 | review #5 | a typed refusal motif + Escape asks « Abandonner les modifications ? »; « Continuer la saisie » keeps the text | browser | 986 |
| F3 | review #4 | /conges scrolled to the bottom at 320: the last block ends above the fixed « Demander un congé » | browser | 320 |
| F4 | review #10 | the « refusée » bell row names the days once — a half day by « (demi-journée) », never followed by a second bracket | sql | — |
| G1 | review #1 (merge) | main's « Votre secrétaire » tour, started from « Premiers pas », opens /users on « Comptes » and lights « Créer un compte » | browser | 986 |

⚠️ Every « no horizontal scroll » row now measures `<main id="contenu-principal">` as well as the document: `<main>` is the scroller, so the old probe could not see the 320 px overflow review #2 found.

## Added for « Droit à congé » (2026-11-06, droit-conges-blueprint) — nothing removed

A4 now sets the rule « 24 j par an » from 1 January through « Définir la règle »; A16 reads « Prévus 4,5 j » (both
congés are still to come on the day of the run); A19's no-droit secretary reads « 2 jours prévus ».

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| K5 | refusals | rule 40 j par mois; days added with no motif | « Par demi-journée, entre 0 et 31 jours. », nothing written; « Indiquez le motif. » | browser + sql | 1000 |
| K1 | a monthly rule from next month | Modifier la règle → 2 j par mois, default « 1er … » | toast « Règle enregistrée »; card « prochain : +2 j le 1er … »; still 19,5; SQL keeps the yearly rule and appends the monthly one | browser + sql | 1000 |
| K2 | starting balance | Solde de départ 10 today | « 5,5 jours restants » (10 − 4,5 planned); relevé « Solde de départ » … « Congé prévu », ending « solde 5,5 j »; no « Droit 2026 » line | browser | 1000 |
| K3 | days removed | Retirer 1,5 · Rattrapage | « 4 jours restants »; relevé « Jours retirés · Rattrapage » −1,5 j | browser | 1000 |
| K4 | a line cancelled | the relevé's ✕ on it, motif « Erreur de saisie » | confirm names the line; empty motif refused; back to 5,5; the line stays struck through with the motif, in the relevé and the account list | browser | 1000 |
| K6 | her own page | secretary `/conges` | « 5,5 jours restants », « Total pris depuis le début », next credit; relevé with no button | browser | 560 |
| K7 | edges | the file and the rule dialog at 320×640 and 1536×730 | no sideways scroll; « Enregistrer la règle » on screen | browser | 320 · 1536×730 |

### Run 9 — the paths one day cannot show (nothing removed)

A11 also reads the e-mail's « Congé payé restant en 2026 : 21,5 jours ». A18's absence is now « the two days before
today and today » (it was pinned to 4 → 6 November, so A19/A22 broke on the 7th).

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| K8 | a monthly rule that started in January | « 2 j par mois » from « 1er janvier » on the no-droit secretary | Acquis = 2 × months so far; one « Acquis — <mois> » line per month; minus her 2 planned days | browser | 1000 |
| K9 | cancelling a rule | ✕ on « Règle : 2 j par mois » + motif | toast; card back to « Aucun droit à congé défini » with no balance; the rule struck through | browser | 1000 |
| K10 | 31 December, « perdus » | SQL: 20 j par an from 1 Jan 2025 + 5 days taken in March 2025 | 2025 read back: acquis 20 · pris 5 · perdus 15 · reste 0, relevé ends « perdus » −15; 2026 opens at « Droit 2026 », 18 left | api + browser | 1000 |
| K11 | 31 December, « reportés » | SQL: the same rule carries over | 2026 relevé opens « Report de 2025 » 15 j; 33 left | browser | 1000 |
| K12 | ended 2025 owing | SQL: 18 more days in June 2025, « perdus » again | « Dépassement reporté de 2025 » −3 j; 15 left; nothing « perdu » in 2025 | browser + api | 1000 |
| K13 | live update | her `/conges` open; admin adds 1 day | her « jours restants » +1, same page (no reload) | browser | 560 |
| K14 | tablet with a finger | `(pointer: coarse)` at 820×1180 | account buttons, relevé ✕, rule dialog fields / choices / buttons ≥ 44 px tappable | browser | 820 |
| M1 | the database update with real values | throwaway copy: back down to the previous migration, give 3 accounts a « droit annuel » (24 · 0 · none), up again; `verify-schema` | one rule each for 24 and 0, « par an, perdus », from 1 January, `Droit annuel (repris)`; the column gone; down restores 24; `verify-schema` clean | console + sql | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| Push to a phone (AC-9) | DEV-2: congé categories are not pushed; bell + e-mail only |
| A real two-admin 409 on a stale version (AC-14) | the dialog re-reads right before writing, so only a millisecond race reaches the server's 409; B6 exercises the same banner through the re-read |
| Doctor/admin trying to submit | API-only refusal; `/test-small-feature` covers it |

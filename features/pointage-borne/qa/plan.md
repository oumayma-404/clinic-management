# QA plan — Pointage sur la borne du cabinet

**Written:** 2026-11-05
**Change under test:** branch `feature/pointage-borne`, uncommitted (on top of b1a35839)
**Rendering files in the diff:** 17 — `app/pointage/{layout,page}.tsx`, `app/login/page.tsx`, `app/mon-profil/page.tsx`,
`app/users/page.tsx`, `app/users/[userId]/page.tsx`, `components/team/*` (8), `components/team-modules-card.tsx`,
`components/dashboard-header.tsx`, `components/dashboard/{dashboard-page,team-today-block}.tsx`, `components/user-management.tsx`
**Blast-radius table:** `progress.md` § Blast Radius (rows 4, 7, 8 `must re-test`; 2, 3, 9 `must change`)
**Budget:** large (anonymous endpoint, clinical-staff data, ~45 files) → tiers A, B, C, D · 34 scenarios
**Stack:** isolated — API :5096 + web :3096 (`next dev`) on `clinic_staffleave_qa`. Admin = Dr Salma Ben Youssef
(Chrome 9333, also the BORNE device: a second tab in the same profile), secretary = Ines Gharbi (Chrome 9334).
Tracked by default: Ines and « QA Secrétaire 991861 » (active secretaries). Today = jeudi 5 novembre 2026.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/settings` → Équipe | read the switch, turn it on | « Pointage des arrivées et des départs » off by default; toast « Pointage activé. »; « Bornes » (« Aucune borne ») and « Personnes qui pointent » appear; Ines + QA 991861 switched on, Dr Nadia off | browser | 1000 |
| A2 | same | « Faire de cet appareil une borne » → nom « Accueil » → Enregistrer → step-up (code) | toast « Cet appareil est maintenant la borne « Accueil » »; the list shows « Accueil · cet appareil · Jamais utilisée »; the admin context holds an `apexa_borne` cookie, HttpOnly, path `/api/pointage` | browser | 1000 |
| A3 | `/mon-profil` (Ines) | « Créer mon schéma » → draw 1-2-3-6 with the keyboard twice | toast « Schéma de pointage enregistré. »; card reads « Enregistré » + « Changer de schéma »; « Mes présences » card shown | browser | 560 |
| A4 | — | after A3 | `AttendanceSchemaHash` is a PBKDF2 hash (not the pattern text) | sql | — |
| A5 | `/pointage` (borne tab) | read the tiles; tap Ines; read the big word; DRAG the schéma 1→2→3→6 with the pointer | tiles: Ines « Pas encore arrivée », QA 991861 « Schéma à créer dans Mon profil » (disabled); « Arrivée » in large; confirmation « Arrivée enregistrée » + « HH:mm · Borne Accueil »; back to tiles with Ines « Présente depuis HH:mm » | browser | 820×1180 |
| A6 | — | after A5 | one `AttendanceEntries` row: today, open, `TerminalName` Accueil, `PunchedArrivalUtc = ArrivalUtc` | sql | — |
| A7 | `/users?onglet=presences` (admin, opened BEFORE A5) | wait, no reload | « Aujourd'hui » shows Ines « Présente depuis HH:mm » without a reload (AC-13) | browser | 1000 |
| A8 | borne | Ines → « Départ » → schéma | « Départ enregistré »; tile back to « Pas encore arrivée »; row closed with `DepartureSource = 0` | browser + sql | 820 |
| A9 | borne | Ines arrives again (lunch return) | a SECOND row today, open (AC-8) | sql | — |
| A10 | `/users` → Présences | read at 1440 | tabs « Comptes · Congés · Présences »; month « novembre 2026 »; sheet table with an Ines column; today's row shows « HH:mm – HH:mm » and « HH:mm – présente »; Sunday rows hatched; « Total » row | browser | 1440 |
| A11 | same | « Ajouter un pointage » → Ines, 2026-11-03, 08:30, no departure, motif « Oubli borne » | toast « Pointage ajouté. »; the 3 nov row shows « 08:30 – Départ non pointé » in red with « Corriger » | browser | 1440 |
| A12 | same | « Corriger » on that line → départ 17:00, motif « Parti à 17 h » | dialog shows « Corriger le pointage » + « Ines Gharbi · mardi 3 novembre »; toast « Pointage corrigé. »; line « 08:30 – 17:00 » + « Corrigé » | browser | 1440 |
| A13 | — | after A12 | row: `DepartureSource = 2`, `CorrectionReason` « Parti à 17 h », `PunchedDepartureUtc` null | sql | — |
| A14 | same | « Exporter » | a CSV download whose server name is `presences-2026-11-05.csv` and whose body names Ines | api + browser | 1440 |
| A15 | `/users/{Ines}` | read | « Présences » section: « Jours pointés », « Heures », « Départs non pointés » figures; day list | browser | 1000 |
| A16 | `/mon-profil` (Ines) | read « Mes présences » | her lines listed; NO « Corriger » control anywhere | browser | 560 |
| A17 | `/` (admin) | read | « Équipe aujourd'hui » block lists Ines « Présente depuis … » and QA 991861 « Pas encore arrivée »; link « Ouvrir les présences » → `/users?onglet=presences` | browser | 1000 |
| A18 | header + `/login` | admin header (a borne device) · secretary header (not one) | « Pointage » entry in the admin's header; none in the secretary's | browser | 1000, 560 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | `/mon-profil` (Ines) | « Changer de schéma »: 3 dots | « Reliez au moins 4 points. »; stays on step 1 | browser | 560 |
| B2 | same | 4 dots, then a DIFFERENT 4 dots | « Les deux schémas ne correspondent pas. Recommencez. »; back to « Tracez votre schéma »; nothing saved (hash unchanged) | browser + sql | 560 |
| B3 | borne | Ines → wrong schéma once | « Schéma incorrect. Encore 4 essais avant le blocage. »; panel stays | browser | 820 |
| B4 | borne | « Retour » and Échap on the panel | back to the tiles; no row written | browser + sql | 820 |
| B5 | borne | 4 more wrong schémas | « Trop d'essais. Réessayez dans 15 min… »; back to tiles; Ines tile « Bloqué : trop d'essais », disabled | browser | 820 |
| B6 | `/settings` | Ines row | « Bloqué après 5 essais, jusqu'à … » + « Réinitialiser le schéma » → confirm | toast « Schéma de Ines Gharbi réinitialisé. »; borne tile « Schéma à créer dans Mon profil » | browser | 1000 |
| B7 | `/settings` | switch QA 991861 off | the borne no longer shows her tile (after reload) | browser | 820 |
| B8 | secretary's Chrome | open `/pointage` | « Cet appareil n'est pas une borne du cabinet. » | browser | 560 |
| B9 | — | punch with no cookie / a bogus cookie | 401 `terminal_unknown` | api | — |
| B10 | Correction dialog | Corriger with an empty motif | « Indiquez le motif de la correction. »; dialog open | browser | 1440 |
| B11 | `/settings` | turn the pointage OFF | borne: « Le pointage n'est pas activé pour ce cabinet. »; `/users` loses « Présences »; dashboard loses the block; Mon profil loses both cards; turn it back on | browser | 1000 |
| B12 | `/settings` | « Retirer » the borne → confirm | toast « Borne « Accueil » retirée. »; borne page (reload) « Cet appareil n'est pas une borne »; header « Pointage » gone after reload | browser | 1000 |
| B13 | Ines on a full-day congé today | admin records an absence for today (API), borne reload | Ines tile « En congé », disabled; then the absence is cancelled (API) | api + browser | 820 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | borne on a tablet / phone | tiles + pad at 820×1180, 1180×820, 390×844, 320 | no horizontal scroll; pad fully visible on screen at 1180×820 with the « Retour » button reachable; dots' cells ≥ 44 px | browser | 820, 1180, 390, 320 |
| C2 | sheet below `lg` | `/users` → Présences at 390 and 320 | day CARDS (not a table), no horizontal scroll of `main`, the red line's « Corriger » visible | browser | 390, 320 |
| C3 | dialog at the owner's height | « Corriger » at 1536×730 | « Corriger » footer button visible without scrolling | browser | 1536×730 |
| C4 | settings card at 390 | Paramètres → Équipe at 390 | no horizontal overflow; « Retirer » and switches reachable | browser | 390 |
| C5 | stale version | correct a line in the sheet after changing it behind the dialog's back (SQL touch) | the save is refused with « Recharger »; Recharger shows the new hours | browser | 1440 |
| C6 | month navigation | « Mois précédent » → octobre; « Mois suivant » disabled on the current month | label « octobre 2026 »; next disabled on novembre | browser | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #7 `/users` tabs | « Congés » tab beside the new one | `?onglet=conges` opens Congés; its list renders | browser | 1000 |
| D2 | #8 `/login` | a non-borne device's login | form unchanged, no « Pointage » button; sign-in works (the secretary's login at the start) | browser | 560 |
| D3 | #9 dashboard blocks | « Personnaliser » → hide « Équipe aujourd'hui » → reload → show it again | hidden survives the reload (server accepted `teamToday`), and comes back | browser | 1000 |
| D4 | #3 team-modules | the congés switch beside the new one | toggling « Demandes de congé » does not touch the pointage switch | browser | 1000 |
| D5 | #4, #5, #10 guards | controller policies, anonymous list, archive/relay/secret guards | unit suite unfiltered green (6450/0) | api | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| Subscription 402 on the borne (AC-15) | `SelfHostedLan`-style dev profile? The QA API runs `HostedMultiTenant`, but lapsing the QA cabinet's entitlement means writing the vendor ledger; checked by reading the handler + the unit-tested reader instead |
| Rate limit on the punch | 30 punches a minute per address — reaching it would lock the walk itself |
| A real 10" tablet's touch | the dev browser has no coarse pointer; the drag is driven with the mouse (pointer events are the same path) |

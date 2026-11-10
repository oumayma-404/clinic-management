# QA plan — Pointage · review fixes F1–F36 (F18 excluded)

**Written:** 2026-11-09 (Monday, 12:30 Tunis, inside opening hours 09:00–17:00)
**Change under test:** worktree `conges-secretaires`, branch `feature/pointage-obligatoire`, uncommitted on top of `13302491`
(`review-fixes-plan.md`, `reviews/feature-review.md`).
**Rendering files in the diff:** 40+ — `components/team/*` (clock-in-gate, punch-flow, schema-setter, schema-setup-card,
sign-out-prompt, borne-board, borne-link, clock-chip, attendance-settings-card, attendance-panel, attendance-dialogs,
team-today-list), `app/pointage`, `app/mes-presences`, `app/users/**`, `dashboard-header`, `notification-panel`,
`team-modules-card`, `clinic-settings`, `user-management`, `ui/month-stepper`, `lib/guides/pointage`, `lib/nav`.
**Blast-radius table:** `review-fixes-plan.md` § Blast radius (rows 1, 2, 3, 5, 6, 7, 8, 9 → tier D / covered rows).
**Budget:** large (shared module, two roles, lock over every screen) → tiers A–D · 49 scenarios (A23 · B14 · C7 · D5).
**Driver:** `qa/walk-review-fixes.mjs` — my own headless Chrome (playwright-core, `channel: 'chrome'`), never the
owner's CDP windows 9333 / 9334.

## Environment and arrangement (decided before the browser opens)

- Stack: web `:3096` (next dev, started 12:24), API `:5096` (started 12:22), DB `clinic_pointage_demo`.
- **Measured before planning: the lock is NOT live today.** `GET /team/attendance-settings` → `setup.lockState = "from"`,
  `lockActiveFrom = 2026-11-10T08:00Z`: the only live borne « Accueil » was activated at 09:29 today, after opening, so F6
  defers the lock to tomorrow 09:00. And Ines already has a closed line today (arrived 09:33, left 09:35), which lifts her
  lock all day (F18 not done). So neither tracked secretary can show the lock without changing the owner's data.
- **Arrangement (own artefacts only):**
  - **T** = throwaway secretary « QA Pointage 952025 » (`qa.secr.p952025@…`), created over the API by the admin, invitation
    accepted over the API (password `QaAudit2026!y`). Deactivated at the end — the established QA pattern.
  - **T1** = T's ordinary PC (Chrome, Windows UA → « Chrome sur Windows »). **T2** = T's second PC (Linux UA →
    « Chrome sur Linux »), chosen by the admin as borne « QA » and claimed on T2. **ADM** = Dr Salma in a context of mine.
  - API sessions for the device list: T « Safari sur iPad » (tablet), T « Chrome sur Android » (a tracked phone — must be
    left out), Dr Salma « Chrome sur Android » (an admin phone — offered as « Téléphone »).
  - **To make the lock live today** the walk back-dates **its own borne « QA »**'s `ActivatedAtUtc` to 08:30 (SQL on my
    own row). Retiring « QA » at the end puts the cabinet back on « Actif à partir de demain 09:00 » by itself.
  - Ines, « Accueil » and Ines's lines are never written. 991861 receives one « Créez votre schéma » row (when « QA »
    goes live) — deleted at the end with every other notification the run created.

## Tier A — happy path

| ID | Finding | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|---------|-------|-----------------------|-------|--------|
| A1 | F9 | ADM `/settings` | read card order; open `/settings#equipe` | the « Équipe » card (`#equipe`) is the next card after « Horaires » (`#horaires`) and before the billing card; arriving on `#equipe` scrolls it into view | browser | 1440 |
| A2 | F7 F8 | ADM `/settings` → « Mise en place » | read the 4 steps | « Horaires du cabinet · Définis » ✓ · « Borne · Accueil · Ines Gharbi · Chrome sur Windows » ✓ · « Schémas · 1 sur 3 créé · à créer : QA Pointage 952025, QA Secrétaire 991861 » · « Blocage de l'app » chip « Actif à partir de demain 09:00 » beside the switch; « Me guider » shown | browser | 1440, 390 |
| A3 | F7 | ADM « Me guider » | press Suivant ×4, Terminer | 5 bubbles in order (switch, setup, people, Ajouter une borne, devices); step 4 opens « Ajouter une borne »; Terminer → toast « Vous savez mettre en place le pointage. », stays on `/settings` | browser | 1440 |
| A4 | F5 F25 F2 | ADM « Ajouter une borne » | open, read | name prefilled « Accueil 2 »; « Remplace » = « Aucune : nouvelle borne » (+ « Accueil »); each row leads with its kind (« PC · Chrome sur Windows », « PC · Chrome sur Linux », « Tablette · Safari sur iPad », « Téléphone · Chrome sur Android » for Dr Salma); T's Android phone absent; « Cet appareil (Dr Salma Ben Youssef) » is the LAST row | browser | 1440 |
| A5 | F10 F3 | ADM register « QA » on T2 (T2 on about:blank) → T2 opens the app | | toast « Borne « QA » en attente de l'appareil de QA Pointage 952025. »; row « En attente depuis HH:mm · QA Pointage 952025 · Chrome sur Linux »; T2 then shows « Cet appareil est maintenant la borne « QA ». »; admin row no longer waiting; admin bell « Borne installée »; T's bell « Cet appareil est une borne » + « Créez votre schéma de pointage » | browser + sql | 1440 |
| A6 | F3 | T1 bell | click « Créez votre schéma de pointage » | lands on `/mon-profil` with the « Créer votre schéma » dialog open (« Tracez votre schéma »), `?schema` gone from the URL | browser | 1180 |
| A7 | F6 F8 F14 F30 | T1 `/patients` after « QA » starts the lock | read the lock | chip in Paramètres « Actif »; lock « Pointez votre arrivée » · « Bornes : « Accueil », « QA » » · section « Créer votre schéma » with the pad (« Tracez votre schéma ») in place · « Se déconnecter » · « Demander un congé »; no « Schéma oublié ? » | browser | 1180 |
| A8 | F14 | T1 lock | draw the schéma twice | toast « Schéma enregistré. »; lock stays (not a borne) and now offers « Schéma oublié ? »; SQL hash set | browser + sql | 1180 |
| A9 | F1 | T1 lock « Schéma oublié ? » | right password, draw a new schéma twice | « Changer votre schéma » → « Mot de passe de votre compte » → pad → toast « Schéma enregistré. »; hash changed | browser + sql | 1180 |
| A10 | F4 | T2 `/patients` (T signed in on borne « QA ») | read; « Ce n'est pas moi » | « Cet appareil est une borne du cabinet. » + her name « QA Pointage 952025 » above the pad + « Schéma oublié ? » + « Ce n'est pas moi »; the link lands on `/pointage` tiles, no lock there | browser | 820 |
| A11 | F13 | T2 lock pad → ADM | 5 wrong schémas; read the bell, Aujourd'hui, Paramètres; « Débloquer » | lock shows « Schéma bloqué jusqu'à HH:mm » instead of the pad (T1 too); admin bell « Schéma bloqué » naming her; Aujourd'hui chip « Schéma bloqué » + « Débloquer »; Paramètres « Bloqué après 5 essais, jusqu'à … » + « Débloquer »; tile « Schéma bloqué · réessayez dans N min »; Débloquer → toast « Schéma de QA Pointage 952025 débloqué. », SQL lock cleared, hash kept, T2's pad back | browser + sql | 1440, 820 |
| A12 | F12 F23 | ADM Présences → Aujourd'hui / toolbar | « Pointer son arrivée » on T; then « Ajouter un pointage » | dialog prefilled with T + today + now (±2 min); toolbar: no person, today, now; a past day empties the hour | browser | 1440 |
| A13 | F24 F35 | ADM Présences sheet | read T's column; inline « Ajouter » on sam. 7 | past open days with nothing read « Aucun pointage » + « Ajouter »; Ajouter opens prefilled T + 2026-11-07 + empty hour; future rows muted; column header links to `/users/<id>` | browser | 1440 |
| A14 | F20 F28 | T2 `/pointage` | T's tile → schéma (2 open days set by A12/A13) → Escape → wait 35 s → 17:00 → 17:30 | « Votre départ du vendredi 6 novembre… » survives Escape and 35 s; after Valider: « … du samedi 7 novembre … » with « Vendredi 6 novembre · départ 17:00 » listed above; then « Arrivée enregistrée · HH:mm · Borne QA »; SQL both days declared; T1's lock lifts by itself | browser + sql | 820 |
| A15 | F19 | T1 and T2 header chip | read, click | « Arrivée à HH:mm »; T1 → dialog « Pointez votre départ à la borne « Accueil » ou « QA » » · « Arrivée pointée à HH:mm. » · « Fermer »; T2 → « Pointez votre départ » with the pad | browser | 1180 |
| A16 | F16 | T2 header « Pointage » | open; Escape on pad; punch departure; reopen; punch arrival | dialog « Pointage » · « Borne QA » over `/patients` (same page, no reload); Escape on the pad returns to the tiles; after each punch the dialog closes itself; chip follows (« Départ à » then « Arrivée à ») | browser | 1180 |
| A17 | F29 F30 F31 | ADM corrects T's open line → T1 `/mes-presences` | | rail shows « Mes présences » for T (not for the admin); page: schéma card « Enregistré » + month; today « HH:mm – en cours » + « Corrigé » + « Borne : … · corrigé par Dr Salma Ben Youssef »; 6 nov « Ajouté par Dr Salma Ben Youssef » + « Déclaré » | browser | 1180 |
| A18 | F15 | T1 then T2 « Se déconnecter » while present | | T1: « Pointez votre départ à la borne « Accueil » ou « QA » », footer exactly « Annuler » + « Se déconnecter » (filled); Annuler keeps her in. T2: « Pointez votre départ », pad, « Annuler » + « Se déconnecter sans pointer »; schéma → « Départ enregistré · À HH:mm. » → `/login` (with « Pointage ») | browser | 1180, 1536×730 |
| A19 | F17 F35 | ADM switch « Dr Nadia Trabelsi pointe » | confirm; open her file; switch off | alertdialog « Dr Nadia Trabelsi pointe ? » + « … devra pointer son arrivée avant de travailler. »; after Confirmer her `/users` row offers « Voir sa fiche »; file header « Pointe » + « Schéma à créer »; off without a confirm (override reset to NULL by SQL after) | browser + sql | 1440 |
| A20 | F32 F35 | ADM `/users/<T>` | read | Congés sections + « Présences » header (« Pointe » switch on, « Schéma créé », « Réinitialiser le schéma ») + her month; `/users` row offers « Voir sa fiche et ses congés » | browser | 1440 |
| A21 | F36 | ADM Présences and Congés tabs | ‹ › | same control (« Mois précédent » / « Mois suivant », 32 px desktop) on both; Présences « ‹ » → « octobre 2026 », « › » disabled on the current month | browser | 1440 |
| A22 | F2 F22 F10 | ADM « Ajouter une borne » → Remplace « QA » → « Cet appareil »; then Retirer | | Remplace prefills the name « QA »; toast « Cet appareil est maintenant la borne « QA ». »; old « QA » retired in the same save (SQL); Retirer confirm « Cet appareil ne pourra plus pointer. » without « L'app ne sera plus bloquée »; toast « Borne « QA » retirée. »; chip back to « Actif à partir de demain 09:00 » | browser + sql | 1440 |
| A23 | F11 F12 F30 | ADM Présences « Aujourd'hui » + dashboard | read | Ines « Départ à 09:35 »; 991861 « Pas encore de pointage » + « Schéma à créer » + « Pointer son arrivée »; T per its state; the dashboard « Équipe aujourd'hui » block says the same; no « Présente » / « Pas encore arrivée » anywhere | browser | 1440 |

## Tier B — alternate paths

| ID | Finding | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|---------|-------|-----------------------|-------|--------|
| B1 | F22 | ADM « Retirer la borne Accueil » (only live borne, lock on) | read, « Retour » | « L'appareil de la borne « Accueil » (Ines Gharbi · Chrome sur Windows) ne pourra plus pointer. Les pointages déjà enregistrés restent. L'app ne sera plus bloquée. »; Retour retires nothing (SQL) | browser + sql | 1440 |
| B2 | F25 | Ajouter une borne | name « accueil » | « Une borne s'appelle déjà « accueil ». », no step-up | browser | 1440 |
| B3 | — | Ajouter une borne | no device | « Choisissez l'appareil qui sera la borne. », no step-up | browser | 1440 |
| B4 | F25 F5 | API `POST /team/terminals` | name « Accueil »; T's Android session | 409 `borne_name_taken`; 400 `borne_device_phone`; nothing written | api + sql | — |
| B5 | F1 | T1 lock « Schéma oublié ? » | wrong password, draw twice | back on « Mot de passe de votre compte » with the server's refusal; hash unchanged | browser + sql | 1180 |
| B6 | F10 F2 | ADM: « QA tablette » for T's iPad session → that session signs out | | toast « … en attente de l'appareil de QA Pointage 952025. »; row « En attente depuis … · Safari sur iPad »; after sign-out: admin bell « Borne non reçue »; row red « Appareil déconnecté avant d'avoir reçu la borne » + « Choisir un autre appareil » → dialog « Remplacer la borne « QA tablette » », Remplace preset, signed-out device gone; saving onto Dr Salma's phone retires the old row in the same save; Retirer clears it | browser + sql | 1440 |
| B7 | F33 | ADM Présences, `/team/today` answers 500 (route) | | « L'équipe du jour n'a pas pu être chargée. » + « Réessayer »; the month still shows; Réessayer (route off) → the list | browser (simulated) | 1440 |
| B8 | F12 F9 | `/team/today` rewritten (route) | setupIssue · closed day · nobody | « Aucun pointage depuis l'ouverture » + link « Paramètres → Équipe » (`/settings#equipe`); « Cabinet fermé aujourd'hui »; « Personne ne pointe dans ce cabinet » + a link | browser (simulated) | 1440 |
| B9 | F9 | `/users` with congés reported off (route on user-status) | | tabs Comptes · Congés · Présences still there; Congés tab « Les demandes de congé ne sont pas activées » + « Activer dans Paramètres » → `/settings#equipe` | browser (simulated) | 1440 |
| B10 | F32 | `/users/<T>` with the congés file answering 500 (route) | | « Les congés n'ont pas pu être chargés. » + Réessayer, and « Présences » still rendered | browser (simulated) | 1440 |
| B11 | F26 | « Aucune borne active : demandez à l'administrateur. »; 409 `borne_last_while_present` | — | ⏭ planned not-exercised: needs « Accueil » retired (the owner's borne) | — | — |
| B12 | F27 F30 | « Départ non pointé » reminder wording and click | — | ⏭ planned not-exercised: the job fires 15 min after closing (17:15); moving the hours is the owner's data | — | — |
| B13 | F3 F27 | ADM bell rows « Borne installée » / « Schéma bloqué » / « Borne non reçue » | click | each lands on `/settings#equipe` | browser | 1440 |
| B14 | F11 F30 | `/pointage` tiles (T2) | read | 991861 « Schéma à créer » disabled; Ines « Départ à 09:35 »; T « Pas encore de pointage » | browser | 820 |

## Tier C — edge cases (devices)

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | lock, ordinary device | T1 lock (no schéma, then with) | no sideways scroll; « Se déconnecter » and « Demander un congé » reachable; pad fully on screen | browser | 320×568, 390×844, 1536×730 |
| C2 | lock on a borne (F21) | T2 lock with the pad | pad ≤ 40 dvh; « Demander un congé » reachable; no sideways scroll | browser | 320×568, 812×380 |
| C3 | Add-borne dialog | ADM | no scroller overflows sideways; « Enregistrer la borne » on screen | browser | 320×568, 1536×730 |
| C4 | coarse pointer | CDP touch emulation on T2 lock, ADM dialog, Aujourd'hui, month stepper | `matchMedia('(pointer: coarse)')` true; « Schéma oublié ? », « Ce n'est pas moi », device rows, « Débloquer »/« Pointer son arrivée », ‹ › ≥ 44 px | browser | 390, 820 |
| C5 | names at 320 (F34) | ADM Présences « Aujourd'hui » | « QA Secrétaire 991861 » / « QA Pointage 952025 » wrap, never clipped | browser | 320 |
| C6 | sign-out prompt with the pad | T2 | title, pad and both footer buttons on screen | browser | 1536×730, 320×568 |
| C7 | Présences tab widths | ADM `/users?onglet=presences` | `main` never scrolls sideways; cards below `lg`, table from `lg` | browser | 320, 390, 820, 1180, 1536×730 |

## Tier D — regression sweep (blast-radius rows)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #1 #3 #8 | `/pointage` tiles + punch (T2) | tiles + one arrival and one departure punch work (covered by A14/A16), confirmation « … · Borne QA » | browser | 820 |
| D2 | #1 #3 | Présences sheet + correction | « Corriger » saves, toast « Pointage corrigé. », « Corrigé » on the line | browser | 1440 |
| D3 | #9 | `/conges` (T1) and `/users?onglet=conges` (ADM) | « Mes congés » renders with its month; admin congés panel renders, no error toast | browser | 1180 |
| D4 | #3 | dashboard `/` (ADM) | « Équipe aujourd'hui » lists the same states as Présences | browser | 1440 |
| D5 | #6 | Mon profil (T1) | « Schéma de pointage · Enregistré » + « Changer votre schéma » + « Mes présences » | browser | 1180 |

## Not covered, and why

| Area | Why not |
|------|---------|
| F18 lunch break | deliberately not done (owner's call) |
| B11 / B12 | need the owner's borne retired / her hours moved |
| Money, fiches, devis | the change does not touch them (verification.md § 7) |
| Push to a phone | no phone app on this stack |

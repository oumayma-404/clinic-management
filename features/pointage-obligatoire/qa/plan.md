# QA plan — Pointage obligatoire (part 3)

**Written:** 2026-11-06 (00:00 Tunis — the walk runs on Friday 6 November; « hier » is Thursday 5)
**Change under test:** `feature/pointage-obligatoire`, uncommitted on top of `13302491`
**Rendering files in the diff:** 10 — `punch-flow.tsx`, `clock-in-gate.tsx`, `sign-out-prompt.tsx`,
`attendance-panel.tsx`, `team-modules-card.tsx`, `notification-panel.tsx`, `dashboard-header.tsx`,
`app/pointage/page.tsx`, `app/layout.tsx`, `lib/api/client.ts`
**Blast-radius table:** `features/pointage-obligatoire/progress.md` (rows marked `must re-test`: punch, entry/sheet,
the write gate, user menu, root layout)
**Budget:** small feature, but the gate's reach is the whole app → tiers A · B · C · D · 34 scenarios
**Run 5 (after the 41 review fixes):** six rows added (A18, B13, B14, C7, C8, C9) and A5 re-stated for the lock's
« Demander un congé »; no row removed.

**Stack:** isolated — API :5096 + web :3096 (`next dev`) on DB `clinic_pointage_qa` (copy of `clinic_management`,
all migrations applied). Chrome 9333 = admin Dr Salma (+ the borne tab, + context « BS » = Ines signed in on the
borne device, borne cookie copied); Chrome 9334 = Ines (secretary) on an ordinary device.
**Arrange (SQL, my own DB, stated in the run report):** pointage on, lock on, Friday's hours set to 00:00–23:59 so
« now » is inside them; Ines tracked with no schéma and no line; earlier days' attendance and leave rows cleared.
Later hour changes (before opening / closed day / closing passed for the reminder) are SQL too — the « Horaires »
editor is not the change under test.

## Tier A — happy path

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | Paramètres → Équipe (admin) | open the card with the pointage on | « Bloquer l'app tant que l'arrivée n'est pas pointée » switch, ON | browser | 1440 |
| A2 | Paramètres → Équipe | « Ajouter une borne » → « Cet appareil » → « Accueil » (DEV-12) | toast + « Accueil · cet appareil » | browser | 1000 |
| A3 | Ines, any screen (`/patients`) | no schéma yet | full-screen « Pointez votre arrivée » + « Créer mon schéma » + « Se déconnecter » | browser | 560 |
| A4 | Ines → « Créer mon schéma » | lands on `/mon-profil`, draws the schéma twice | not covered there; toast « Schéma de pointage enregistré. » | browser | 560 |
| A5 | Ines, `/patients` | reload | lock names « Borne : Accueil »; one button « Se déconnecter » + one link « Demander un congé » (congés on) | browser | 560 |
| A19 | « Ajouter une borne » (run 7, DEV-12) | admin picks Ines's signed-in device, names it « Accueil 2 », step-up | her row reads « Ines Gharbi · Chrome sur Windows · actif il y a … »; the admin's own session is only « Cet appareil »; toast « … envoyée à l'appareil de Ines Gharbi »; SQL: no secret, waiting for her session | browser + sql | 1000 |
| A20 | her open screen (run 7) | nothing — wait | toast « Cet appareil est maintenant la borne « Accueil 2 » » on HER screen; SQL claimed; her context holds the HttpOnly cookie (path `/api/pointage`); the admin list drops « En attente » by itself; her `/pointage` reads « Borne Accueil 2 » | browser + sql | 560 |
| A6 | API as Ines | `POST /api/patients` while locked | **423** `{ code: "clock_in_required" }`; no patient row | api | — |
| A7 | borne tab | Ines punches her arrival | Ines's lock closes **without a reload** (realtime) | browser | 560 |
| A8 | Ines | user menu → « Se déconnecter » while present | « Pointer votre départ ? », « Vous êtes présente depuis HH:mm. », « Borne : Accueil », « Se déconnecter sans pointer »; « Annuler » keeps her signed in | browser | 560 |
| A9 | context BS (Ines on the borne device) | sign in with no line today | lock shows the **pad**; punching from it closes the lock | browser | 820 |
| A10 | BS | « Se déconnecter » while present | the prompt carries the pad (« Départ »); punching signs her out with « Départ enregistré » | browser | 820 |
| A11 | admin sheet | « Ajouter un pointage » for Ines on 5 Nov, 08:00, no departure | line « 08:00 – Départ non pointé » | browser | 1440 |
| A12 | borne tab | Ines taps her tile, draws her schéma | step « Votre départ d'hier n'a pas été pointé »: arrival 08:00, « Dernière action dans l'app » = the journal's last non-pointage save of 5 Nov (or « aucune ») | browser + sql | 820 |
| A13 | same | « Heure de départ » 17:30 → Valider | « Arrivée enregistrée »; 5 Nov line closed at 17:30, `DepartureSource = 1`, `DeclaredLastSaveUtc` as shown | browser + sql | 820 |
| A14 | admin sheet | read 5 Nov | « 17:30 » in amber + « Déclaré » + « Dernière action dans l'app : … » | browser | 1440 |
| A15 | admin sheet | totals | Ines's total carries « 1 départ déclaré », hours include 08:00–17:30 | browser | 1440 |
| A16 | admin | « Ajouter un pointage » for Ines today while she is locked (lines cleared) | Ines's lock lifts with no reload | browser | 1440 / 560 |
| A18 | admin + borne | 3 and 4 Nov left open (« Ajouter un pointage », no departure); Ines punches | asked for « du mardi 3 novembre » FIRST, then « du mercredi 4 novembre »; « 930 » typed and Enter (no blur) is accepted as 09:30; then « Arrivée enregistrée »; both lines `DeclaredBySelf` | browser + sql | 820 |
| A17 | Hangfire `remind-missing-departures` | Ines present, closing passed + 15 min, trigger the job | one bell row « Départ non pointé » / « Vous êtes toujours présente depuis HH:mm. » for Ines only; no `AttendanceEntries` change (xmin unchanged) | sql + browser | 560 |

## Tier B — alternate paths

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| B1 | exempt writes | as Ines while locked: `PUT /team/me/schema`, `PUT /notifications/read-all`, `POST /team/me/leave-requests` (refused on its own merits or 2xx, never 423) | none answers 423 | api | — |
| B2 | reads while locked | `GET /api/patients`, `GET /api/appointments` | 200 | api | — |
| B3 | lock switch off | admin turns « Bloquer l'app… » off | Ines (no line) is not locked; `POST /api/patients` not 423; switch back on → locked | browser + api | 1440 / 560 |
| B4 | no lock after departure | Ines arrives then departs at the borne | no lock (she has a line today) | browser | 560 |
| B5 | before opening | Friday hours 08:00–17:00 (now 00:xx) | no lock | api + browser | 560 |
| B6 | closed day | Friday disabled | no lock | api | — |
| B7 | congé today | admin records an absence for Ines today | no lock; clock-status `locked:false` | api | — |
| B8 | admin tracked | Salma « pointe » switched on | admin never locked (DEV-9) | api + browser | 1440 |
| B9 | declaration refusal | « Heure de départ » 07:00 (before arrival 08:00) | sentence « … après l'arrivée (08:00) », Valider disabled, nothing written | browser + sql | 820 |
| B10 | declaration « Retour » | back to the pad from the step | pad shown again, nothing written | browser + sql | 820 |
| B11 | reminder once per line | trigger the job a second time | still one row | sql | — |
| B12 | not present | Ines with no open line: « Se déconnecter » | signs out at once, no prompt | browser | 560 |
| B13 | « Congés » open under the lock | Ines locked → « Demander un congé » | lands on `/conges`, « Mes congés » shown, no lock over it; back on `/patients` the lock returns | browser | 560 |
| B14 | no borne left | the only borne retired (SQL `RevokedAt`, then restored) | clock-status `locked:false` and a write is not 423; restored → locked again | api | — |
| B15 | a device that never opens (run 7) | « Salle 3 » for a second session of hers (« Firefox sur Windows »), then that session ends (SQL) | list « En attente de l'appareil · … »; not in her bornes; only that session hears `borneWaiting`; once ended: « Appareil déconnecté … » in red, no longer offered, « Retirer » clears it | browser + api | 1000 |
| B16 | only a waiting borne (run 7) | claimed bornes retired (SQL), « Salle 3 » waiting | not locked, no borne named, a write is not 423; restored → locked | api | — |
| B17 | no device picked (run 7) | « Enregistrer la borne » with no device chosen | « Choisissez l'appareil qui sera la borne. », no step-up | browser | 1000 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px | lock screen | fits with no scroll; « Se déconnecter » fully inside | browser | 320×568 |
| C2 | 380 px tall | lock screen, landscape phone | no clipped top; sign-out reachable | browser | 812×380 |
| C3 | 730 px tall | sign-out prompt with the pad | « Se déconnecter sans pointer » reachable | browser | 1536×730 |
| C4 | tablet + keyboard | declaration step at 820×600 (keyboard open) | field and « Valider » reachable | browser | 820×600 |
| C5 | chrome-less | Ines locked opens `/pointage` (BS) | tiles, not the lock | browser | 820 |
| C7 | focus + modal layer | the lock rises on `/patients` | focus on the title « Pointez votre arrivée », not « Se déconnecter »; `body[data-scroll-locked]` set; Enter does not sign her out | browser | 560 |
| C8 | departure in flight | sign-out prompt with the pad, the punch held 2.5 s | « Annuler » and « Se déconnecter sans pointer » disabled, Escape does not close; then « Départ enregistré » | browser | 1536×730 |
| C9 | corrected + declared line | 3 Nov corrected (arrival) while open, then declared at the borne | the row reads « Corrigé » then « Déclaré » side by side, before the motif line | browser | 1440 |
| C10 | « Ajouter une borne » at the edges (run 7) | the dialog at 320×568 and 1536×730 | no sideways overflow; « Enregistrer la borne » on screen; every device row a whole-row target (≥ 40 px) | browser | 320 · 1536×730 |
| C6 | correction of a declared line | admin corrects 5 Nov (motif) | line reads « Corrigé », « Déclaré » gone, total no longer counts it as declared | browser + sql | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | punch | ordinary arrival / departure with no earlier open line | « Arrivée enregistrée » / « Départ enregistré » as in part 2 | browser | 820 |
| D2 | team modules | congés switch off → on | toasts, rail entry follows | browser | 1440 |
| D3 | `ClinicDto` ×7 | `GET /api/clinics/user-status` | carries `attendanceLockEnabled` | api | — |
| D4 | bell routing | click the reminder row | lands on `/mon-profil` | browser | 560 |
| D5 | layout | `/login` with a stale session cookie absent | no lock, login form renders | browser | 560 |

## Not covered, and why

| Area | Why not |
|------|---------|
| PC de secours / cut (lock lifts) | no relay on this stack; `IsPcDeSecours` / fenced cloud are unit-test rows |
| lapsed subscription (DEV-3) | not reachable without vendor verbs; unit-test row |
| push to her phone | no push device registered on this stack; the decorator's fan-out is a unit-test row |
| CSV « Déclaré » column | export is a download (driven Chrome renames it); `StateLabel` is a unit-test row |

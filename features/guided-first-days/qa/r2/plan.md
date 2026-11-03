# QA plan — Guided first days, release 2 (parts D + E)

**Written:** 2026-11-03
**Change under test:** `feature/guided-first-days-r2` on top of `18dfeb6c` — `2dfbb0b6` (D1 Aide) · `98b6324b` (D3 Rappels) ·
`5cb3844b` (E1 server) · `a75a5b62` (E2 card + form) · `d7abc8a7` (E3 console) · `c8762ae5` (D2 devis new patient)
**Rendering files in the diff:** 13 — `web/app/rappels/page.tsx`, `web/components/{help-menu,dashboard-header,
post-visit-review-popup,create-appointment-dialog,clinic-settings}.tsx`, `web/components/feedback/{feedback-dialog,
feedback-prompt-card}.tsx`, `web/components/patients/{new-patient-fields,duplicate-patient-dialog}.tsx`,
`web/components/treatment-plans/treatment-plan-form-modal.tsx`, `console/app/retours/page.tsx`,
`console/components/feedback-list.tsx` (+ `console/app/cabinets/page.tsx`)
**Blast-radius table:** `features/guided-first-days/blast-radius.md` — R2 rows `must re-test`: #19, #20, #21, #23 (+ #28, #29
added below for R2)
**Budget:** large (a shared extraction in the booking dialog + a new database surface) → tiers A, B, C, D · 34 scenarios

**Environment:** isolated stack — API `:5097` (`dotnet run` from the worktree) with the vendor console on `:5094`
(`Console__Port`), web `next dev :3097`, console `next dev :3094` (`CONSOLE_API_URL=http://localhost:5094/api`), throwaway
database `clinic_gfd_r2` in the shared `clinic-postgres` (the branch carries `AddProductFeedback`, which the shared database
lacks), own DataProtection ring in the scratchpad, `Subscription__ContactPhone=+216 20 123 456` (the « Aide » number), mail
through the shared `clinic-mailpit`. Profile `HostedMultiTenant` (dev default). Own `playwright-core` Chrome, fresh
contexts. One console account created with the `platform-account create` verb against the throwaway database.
**Fixtures (SQL on the throwaway database only):** an account's `CreatedAt` moved 10 days back and `ClinicActivityDays` rows
inserted (the week-one eligibility needs a week and three days of real use); a prompt's `LaterUntil` moved into the past
(« a week later »). Everything else is done through the product.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | header « Aide » (admin, `/`) | open « Aide » | trigger labelled « Aide »; popover « Une question ? » with « Écrire à APEXA sur WhatsApp » (`wa.me/21620123456?text=Bonjour, ici <cabinet>.`, new tab), « Vidéos : vos premiers pas » → `https://apexa.tn/tutoriels.html` (new tab), « Donner mon avis ou une idée », the number « +216 20 123 456 » + « Copier le numéro » | browser | 1440 |
| A2 | `/settings` « Besoin d'aide ? » | admin opens Paramètres | the same WhatsApp + videos + number rows | browser | 1440 |
| A3 | `/rappels` | open the page | « Rappels par SMS », « Bientôt disponible », three steps, an example SMS naming the cabinet, « Retour à l'agenda » → `/appointments`; no « DT », no date of availability | browser | 1440, 390 |
| A4 | « Aide » → « Donner mon avis ou une idée » | pick « Problème », type a message, keep « APEXA peut me recontacter », Envoyer | dialog « Écrire à APEXA », « Depuis : Tableau de bord », the admin's address beside the box; toast « Merci. Votre avis est arrivé chez APEXA. »; dialog closed | browser | 1440 |
| A5 | — | the A4 row | `ProductFeedback`: kind `probleme`, source `aide`, the message, `FromScreen` « Tableau de bord », `MayContact` true, role `admin` | sql | — |
| A6 | mailpit | the console account's inbox | one « Nouvel avis sur APEXA » carrying the cabinet, « (Administrateur) », « Problème », the message, « Écran », and « Recontacter » = the admin's address | api | — |
| A7 | `/` week-one card | admin made eligible (fixture), wait on the dashboard | `GET /feedback/prompt` → `show: true`; after ≥ 60 s the card « Une semaine avec APEXA » / « APEXA vous aide-t-il au cabinet ? » with five stars, « Plus tard », « Ne plus demander », bottom-right; `UserFeedbackPrompts.TimesShown` = 1; body carries `data-feedback-open` | browser + sql | 1440 |
| A8 | card → form | (second showing, see B2) tap the 4th star, then pick « Facile » 3, « Agenda » liked, « Devis » hard, a word, Envoyer | form « Votre avis sur APEXA » opens with 4 stars lit (`aria-checked` on « 4 sur 5 »); toast; row source `semaine-1`, help 4, ease 3, liked `agenda`, hard `devis`; prompt `ClosedReason` = `answered`; `GET /feedback/prompt` → false | browser + sql | 1440 |
| A9 | console `/retours` | sign in to the console, « Retours » from « Cabinets » | rows newest first; A8's row « Avis » with notes « aide 4/5 · facile 3/5 · Aime : Agenda · Difficile : Devis » and **no** address; A4's row « Problème » with the admin's address | browser | 1440 |
| A10 | `/treatment-plans` « Nouveau traitement » | « Nouveau patient », fill Prénom/Nom/phone, one act with a price, Enregistrer | toast « Devis … créé »; exactly one new patient with that name; the devis belongs to that patient | browser + sql | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | `GET /feedback/prompt` | a fresh account (< 7 days), then 10 days old with 2 active days, then 3 | `false`, `false`, `true` | api | — |
| B2 | card « Plus tard » | admin presses « Plus tard » | card gone; `LaterUntil` ≈ now + 7 days; prompt `false`; after the fixture « a week later » the card comes back and `TimesShown` = 2 | browser + sql | 1440 |
| B3 | card « Ne plus demander » (doctor) | doctor eligible, card shows, « Ne plus demander », reload and wait 65 s | `ClosedReason` = `never`; no card after the reload | browser + sql | 1440 |
| B4 | card star → « Fermer » (secretary) | secretary eligible, tap the 2nd star, then « Fermer » | toast « Merci… »; one row: source `semaine-1`, help 2, nothing else; prompt closed `answered` | browser + sql | 1440 |
| B5 | « Écrire à APEXA » validation | open, leave the message empty | « Envoyer » disabled; untick « recontacter », type, send → row `MayContact` false, and the mail says « Recontacter : non » | browser + sql + api | 1440 |
| B6 | console filter | « Idées »/« Problèmes »/« Questions » chips | « Problèmes » lists only A4's kind; « Questions » shows « Aucun retour pour ces critères. »; « Tous » shows all | browser | 1440 |
| B7 | devis duplicate — « Choisir un patient existant » | « Nouveau patient » with A10's exact name + phone, Enregistrer | « Créer quand même un dossier pour … ? »; the cancel switches back to « Patient existant » with the fields cleared; patient count unchanged | browser + sql | 1440 |
| B8 | devis duplicate — « Créer quand même » | same, answer « Créer quand même » | devis created; patient count +1 exactly | browser + sql | 1440 |
| B9 | devis refused → no patient left | « Nouveau patient » with names, no act and no title, Enregistrer | « Ajoutez au moins un acte, ou saisissez un titre. »; patient count unchanged | browser + sql | 1440 |
| B10 | devis create-once on retry | « Nouveau patient », the plan POST made to fail once (route), Enregistrer, then Enregistrer again | after the failure « <name> est créé, pas le devis : réessayez » and the fields locked; the retry creates the devis; that name exists once | browser + sql | 1440 |
| B11 | « Aide » on a LAN install | `SelfHostedLan` profile | no « Aide » without a number, `/feedback/*` 404 | — | ⏭ needs a second API in the LAN profile (HTTPS front door, local accounts); covered by the controller's 404-before-mediator code path only |

## Tier C — edge cases

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| C1 | card never over a dialog | card due, but « Écrire à APEXA » open | no card while the dialog is open; the card appears within ~6 s of closing it | browser | 1440 |
| C2 | card never while typing | focus the header search | no card while focused; it appears after blur | browser | 1440 |
| C3 | card counted once across screens | card up on `/`, click « Patients » in the rail | the card is up on `/patients` too; `TimesShown` unchanged | browser + sql | 1440 |
| C4 | phone widths | card at 390×844; « Aide » at 320 | card full width above the bottom bar (its bottom ≤ the bar's top), no horizontal scroll; « Aide » icon-only with `aria-label`, popover inside the screen | browser | 320, 390 |
| C5 | feedback form on a phone and at 730 px | open the week-one form at 390×844 and at 1536×730 | sheet at 390 (full screen, « Envoyer » on screen); at 1536×730 « Envoyer » visible without scrolling the page | browser | 390, 1536×730 |
| C6 | devis « Nouveau patient » at 320 | open the form at 320×640 | no horizontal scroll; segmented and the three fields usable | browser | 320 |
| C7 | console `/retours` at 390 | open at 390 | cards, not the table; no horizontal scroll | browser | 390 |

## Tier D — regression sweep (from blast-radius.md)

| ID | Blast-radius row | What could have broken | Steps | Expected | Layer |
|----|-----------------|-----------------------|-------|----------|-------|
| D1 | #20 booking new patient | the extracted fields/create | `/appointments` « Nouveau », « Nouveau patient », names + phone, an act, Enregistrer | appointment saved; exactly one patient with that name | browser + sql |
| D2 | #20 duplicate « Choisir un patient existant » | the shared prompt's cancel | booking with an existing name | prompt; cancel → « Patient existant », fields empty; no patient created | browser + sql |
| D3 | #20 duplicate « Créer quand même » | the re-entry with the grant | same, « Créer quand même » | appointment saved; that name +1 exactly | browser + sql |
| D4 | #20 retry after a taken slot | create-once across confirmations | new patient booked onto D1's slot → « créneau » prompt → continue | appointment saved; that new name exists once | browser + sql |
| D5 | #20 phone check | `validate()` | « Nouveau patient » with phone « 123 » | « Numéro de téléphone invalide » shown, nothing created | browser + sql |
| D6 | #23 post-visit popup | waits on the card | a finished visit due for review while the card is up | no « Séance terminée » dialog while the card is up; it opens once the card is answered | browser |
| D7 | #19 header at 320 | four controls + Aide | `/` at 320×640 | no horizontal scroll; « Aide » button 36/44 px, named | browser |
| D8 | #21 devis from a patient page | preset path untouched | patient page → new devis | no « Nouveau patient » segmented; the patient's name in the header | browser |
| D9 | #21 plan workspace edit | edit path untouched | a devis → « Modifier » / « Tout modifier » | no segmented, the patient fixed | browser |
| D10 | #28 `HelpRows` in Paramètres | prop now optional | covered by A2 | rows render | browser |

### Blast-radius rows added for R2

| # | Touching | What it is | Other consumers | Verdict |
|---|---|---|---|---|
| 28 | `HelpRows({ phone, whatsapp, onFeedback? })` | shared rows | header « Aide », Paramètres « Besoin d'aide ? » | must re-test — A1, A2 |
| 29 | `useInlineNewPatient` + `NewPatientFields` + `DuplicatePatientDialog` | extracted from the booking dialog | booking dialog, devis form | must re-test — D1–D5, A10, B7–B10 |

## Out of scope

- C1/C2 (secretary invitation by e-mail) — not built in this release (blocked on `feature/security-remediation`).
- Part F « Me guider » — release 3.
- The real SMS « Rappels » screen — parked behind the coming-soon page, untouched.

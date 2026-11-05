# QA run 1 — Guided first days, release 2 (parts D + E)

**Date:** 2026-11-03
**Cycle:** 1 of at most 3
**Plan:** features/guided-first-days/qa/r2/plan.md (34 scenarios: A10 B11 C7 D10)
**Driver:** features/guided-first-days/qa/r2/walk.mjs — first drive `walk-run1.log`, re-drive after probe fixes `walk-run1b.log`
(both in this folder; the re-drive is the result below)
**Environment:** isolated — api :5097 + console listener :5094 (`dotnet run` from the worktree, owned by me) · web :3097
`next dev` from the worktree (owned by me) · console :3094 `next dev` from the worktree (owned by me) · database
`clinic_gfd_r2` (throwaway, all migrations incl. `AddProductFeedback`) in the shared `clinic-postgres` · mail through the
shared `clinic-mailpit` · `Subscription__ContactPhone=+216 20 123 456` · one console account from `platform-account create`
· own `playwright-core` Chrome. ⚠️ Environment fault found and fixed before the drive: my own production build in the
worktree's `web/.next` made `next dev` answer **404** on every public page (`/login`, `/signup`); cleared `.next`, restarted.
**Peer sessions live during the run:** clinic-management-29 (busy — demo-cabinet stack on :5099/:5098/:3098), anakin-7d
(busy), clinic-management-77 (shell), anakin-8f / career-ops-f5 / career-ops-83 (idle) — from `ListAgents`
**Widths looked at:** 320 · 390 · 1440 · 1536×730 (screenshots `Read`: C3, plus the shots named below)
**Status:** RED (2 findings: 1 major, 1 minor)

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Aide » labelled; `wa.me/21620123456` with « Bonjour, ici <cabinet>. »; videos → `apexa.tn/tutoriels.html`; feedback row; number + « Copier le numéro »; shots/A1-aide-1440.png |
| A2 | ✅ | Paramètres « Besoin d'aide ? » carries the same three rows; shots/A2-settings-help.png |
| A3 | ✅ / ❌ | page text, example SMS names the cabinet, no price/date ✅; 390 no scroll ✅; **« Retour à l'agenda » is a button, not a link** ❌ (F-2) |
| A4 | ✅ | « Depuis : Tableau de bord », address shown, « Idée » preselected, toast, dialog closed |
| A5 | ✅ | `probleme|aide|Tableau de bord|t|admin` (SQL) |
| A6 | ✅ | console mail carries cabinet, « Administrateur », « Problème », message, écran, address (mailpit) |
| A7 | ✅ | card text, 5 stars, both buttons, bottom-right, `data-feedback-open`, `TimesShown = 1`; shots/A7-card-1440.png |
| A8 | ✅ | 4th star kept; row `semaine-1|4|3|agenda|devis|…`; prompt `answered`, show false (reached after snoozing the visit question — see C3) |
| A9 | ✅ | console rows: week-one notes « aide 4/5 · facile 3/5 · Aime : Agenda · Difficile : Devis », no address; problem row with address; newest first; shots/A9-retours-1440.png |
| A10 | ✅ | toast « Devis … créé », one patient, devis owned by it |
| B1 | ✅ | prompt fresh false · 2 days false · 3 days true |
| B2 | ✅ | « Plus tard » → LaterUntil +7 d, show false; « a week later » the card returns, `TimesShown = 2` |
| B3 | ✅ | doctor: « Ne plus demander » → `never`, no card 68 s after a reload |
| B4 | ✅ | secretary: star 2 kept, « Fermer » sends it (toast), row `semaine-1|2|…`, prompt `answered` |
| B5 | ✅ | « Envoyer » disabled empty; unticked → `MayContact` false, mail « Recontacter : non », no address |
| B6 | ✅ | « Problèmes » only problems; « Questions » → « Aucun retour pour ces critères. »; « Tous » all |
| B7 | ✅ | « Créer quand même un dossier pour Sami … ? »; cancel → « Patient existant », fields gone, no patient |
| B8 | ✅ | « Créer quand même » → devis, that name = 2 exactly |
| B9 | ✅ | « Ajoutez au moins un acte, ou saisissez un titre. », no patient created |
| B10 | ✅ | plan POST failed once → « … est créé, pas le devis : réessayez », fields locked; retry → devis; name exists once |
| B11 | ⏭ | needs a second API in the `SelfHostedLan` profile (HTTPS front door, local accounts) |
| C1 | ✅ | no card while « Écrire à APEXA » open; card within 6 s of closing |
| C2 | ✅ | doctor: no card while the header search has focus, card after blur |
| C3 | ❌ | after « Patients » in the rail: `card=0 visitQuestion=1`, `TimesShown` 2 — the « Séance terminée » question took the screen; shots/C3-patients-after-screen-change.png (F-1) |
| C4 | ✅ | « Aide » at 320 inside the screen; card at 390 full width above the bottom bar; shots/C4-aide-320.png, C4-card-390.png |
| C5 | ✅ | 1536×730 « Envoyer » on screen; 390 sheet passed in the first drive, measured mid slide-in in the re-drive (probe, fixed in walk.mjs) |
| C6 | ✅ | devis « Nouveau patient » at 320: no horizontal scroll; shots/C6-devis-320.png |
| C7 | ✅ | console `/retours` at 390: cards, no scroll; shots/C7-retours-390.png |
| D1 | ✅ | booking + new patient saved, one patient |
| D2 | ✅ | duplicate → « Choisir un patient existant » → existing mode, fields gone, none created |
| D3 | ✅ | « Créer quand même » → saved, name = 2 exactly |
| D4 | ✅ | taken slot → « Continuer » → saved, the new name exists once |
| D5 | ✅ | phone « 123 » refused before anything is created |
| D6 | ❌ / ⏭ | the visit question opened while the card was up (same cause as C3); second half not reachable once snoozed |
| D7 | ✅ | header at 320: no scroll, « Aide » inside; shots/D7-header-320.png |
| D8 | ✅ | from a patient page: no patient switch, the patient named |
| D9 | ✅ | workspace « Tout modifier »: no patient switch |
| D10 | ✅ | covered by A2 |

## Findings

### F-1 — major — The week-one card loses its place on a screen change, and « Séance terminée » takes the screen

- **Steps:** admin with the card up on `/` (second showing) and a finished visit due for review; click « Patients » in the rail.
- **Expected:** the card is still up on `/patients`; « Séance terminée » waits until the card is answered (plan C3, D6;
  blast-radius #23 « never during … the feedback card »).
- **Actual:** `card=0 visitQuestion=1` — the « Séance terminée » dialog opened on `/patients`; the card is gone behind it.
- **Evidence:** shots/C3-patients-after-screen-change.png; walk-run1b.log C3/D6 lines.
- **Suspected owner:** `web/components/feedback/feedback-prompt-card.tsx` — `visible` is component state and the
  `data-feedback-open` marker is removed on unmount; the header (and the card with it) remounts on every screen, so on the
  new screen the visit question finds the body free before the new card instance has shown itself again.

### F-2 — minor — « Retour à l'agenda » is a button, not a link

- **Steps:** open `/rappels`.
- **Expected:** a link to `/appointments` (it navigates; a link can be opened in a new tab and is announced as one).
- **Actual:** `<Button onClick={() => router.push("/appointments")}>` — `link=0 button=1`.
- **Suspected owner:** `web/app/rappels/page.tsx:90`.

## Probe fixes made during this run (not product changes)

| Row | Probe cause | Fix in walk.mjs |
|---|---|---|
| A4 | read the dialog count during its closing animation | wait for `hidden` |
| B3 | the header search is `role="combobox"`, not `textbox` | combobox |
| B4 | two « Fermer » in the form (footer + the ✕'s sr-only label) | footer-scoped |
| C5 | measured the 390 sheet mid slide-in (`y=570`) | wait until it lands |
| D9 | the workspace actions sit behind « Autres actions sur ce traitement » | open that menu |
| A9 | re-run: the console account was already enrolled | sign in with the changed password |
| A3 | asserted a `link` role first; now checks both and reports which | (F-2 is real) |

## Observations (off-plan)

- None.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed (regression, blast #23) | the card's `visible` was per-instance state and its body marker was set in a passive effect after an async read; the header remounts on every screen, so the remounted « Séance terminée » question checked the body before the new card re-asserted | `web/components/feedback/feedback-prompt-card.tsx` | one mount (`dashboard-header.tsx`); module-level `latched` + the marker in a layout effect; the post-visit popup already reads the marker |
| F-2 | confirmed | navigation done with `router.push` in a `<Button>` | `web/app/rappels/page.tsx` | one page; now `<Button asChild><Link>` |

**Guards added:** none mechanical — F-1 is a mount-order race no grep can see; plan rows C3 + D6 hold it.
**Gates after the last edit:** check:responsive 81 ✅ · tsc ✅ · build ✅ (run after the walk — a build beside the live
`next dev` is the env fault of this run; `web/.next` removed afterwards) · dotnet test not re-run (no `api/` change since
6116 ✅)
**Re-run:** run-2.md — GREEN (73 ✅, 0 ❌, 1 ⏭ — B11, LAN profile)

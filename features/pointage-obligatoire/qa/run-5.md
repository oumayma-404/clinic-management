# QA run 5 — Pointage obligatoire (part 3) · after the 41 review fixes

**Date:** 2026-11-06 (11:47 Tunis)
**Why:** the challenged review's 41 fixes (`reviews/feature-review.md`, `progress.md` § Review fixes) changed the lock
screen, the sign-out window, the punch pad and the sheet — the whole plan is re-run.
**Plan:** `qa/plan.md` — 34 scenarios + 6 added for this run (A18, B13, B14, C7, C8, C9); A5 re-stated for the lock's
« Demander un congé »; no row removed. Same `walk.mjs`, extended.
**Environment:** isolated, rebuilt for this run — DB `clinic_pointage_qa` (fresh `TEMPLATE clinic_management`, no
connection to the template at copy time; all migrations applied by the API at start, last
`20261105223612_AddAttendanceLockAndDeclaredDeparture`) · API :5096 (`dotnet run` from the worktree, Debug) · web :3096
`next dev` · Chrome 9333 / 9334 (fresh profiles). Peers live (ListAgents): 7 sessions, none on these ports or this DB.
**SQL used for arrange / own artefacts (stated):** the walk's usual arrange (attendance rows, bornes, congés, hours,
categories 20–24 bell rows); B14 retires and restores the walk's own borne (`RevokedAt`).
**Status:** RED (1 finding) — 125 ✅ · 3 ❌ (one row, one cause) · 0 ⏭

## Scenarios

| ID | Outcome | Evidence |
|---|---|---|
| A1–A4 | ✅ | switch on by default; borne « Accueil »; lock with « Créer mon schéma »; « Mon profil » open, schéma saved |
| A5 | ✅ | « Borne : Accueil »; one button « Se déconnecter » + one link « Demander un congé » — `shots/A5-lock-named-borne.png` |
| **C7** (new) | ✅ | focus on the title « Pointez votre arrivée »; `body[data-scroll-locked]` set; Enter leaves her signed in and locked |
| **B13** (new) | ✅ | « Demander un congé » → `/conges`, « Mes congés », no lock over it; back on `/patients` the lock returns — `shots/B13-conges-open.png` |
| A6 | ✅ | 423 `clock_in_required`, French sentence, no patient row; clock-status locked / bornes [Accueil] |
| **B14** (new) | ✅ | borne retired → `locked:false`, write not 423; restored → locked |
| B1, B2, D3 | ✅ | exempt writes not 423; reads 200; `attendanceLockEnabled` on user-status |
| A7, A8, B4 | ✅ | lock lifts on her punch with no reload; sign-out prompt « présente depuis 11:50 »; no lock after departure |
| C1, C2 | ✅ | 320×568 no scroll, both controls on screen (`shots/C1-lock-320.png`); 812×380 title and sign-out reachable |
| A16 | ✅ | admin « Ajouter un pointage » lifts the lock live |
| B3 | ✅ | switch off → the open screen unlocks in **3 s** (the `clinics` broadcast — it waited for the minute poll before); back on → locked |
| B5–B8 | ✅ | before opening, closed day, half-day congé, tracked admin — no lock |
| C5, A9 | ✅ | `/pointage` not covered; the pad inside the lock, toast after it lifts |
| A10, C3 | ✅ | pad in the prompt, one headline, footer « Annuler » + « Se déconnecter sans pointer » on screen at 1536×730 (`shots/A10-C3-signout-pad-730.png`) |
| **C8** (new) | ✅ | punch held 2.5 s: both footer buttons disabled, Escape does not close; then « Départ enregistré », signed out |
| B12, D5 | ✅ | not present → signs out at once; `/login` renders unlocked |
| A11–A13, B9, B10, C4 | ✅ | « d'hier » step, arrival 08:00, last save « aucune »; 07:00 refused « … après l'arrivée (08:00) »; Retour; 820×600 reachable; 17:30 stored `DeclaredBySelf` |
| A14, A15 | ✅ | « 08:00 – 17:30 » amber + « Déclaré » + « Aucune action dans l'app ce jour »; total « 1 départ déclaré » |
| **C9** (new) | ✅ | the admin-added, then declared line reads « Corrigé · Déclaré » before the motif « Borne en panne hier » |
| A17, B11, D4 | ✅ | one « Départ non pointé » row, xmin unchanged, still one on a second pass, bell → `/mon-profil` |
| C6, D2 | ✅ | correction reads « Corrigé », « Déclaré » gone; congés switch round trip |
| **A18** (new) | ❌ 6 ✅ · 3 ❌ | 3 and 4 Nov added open; asked « du mardi 3 novembre » **first**, then « du mercredi 4 novembre » ✅ (`shots/A18-oldest-first.png`); « Valider » enabled on « 930 » ✅; **Enter on « 930 » submitted nothing** ❌ — 4 Nov still open, no arrival today (the last two ❌ follow from it) |

Widths looked at: 320×568 · 560×760 · 812×380 · 820×1000/1180 · 820×600 · 1000×830 · 1440×900 · 1536×730.

## Findings

### F1 — minor · Enter on a 3-digit hour does nothing in « Votre départ … n'a pas été pointé »

- **Repro:** borne → her tile → schéma → declaration step → type `930` in « Heure de départ » (field shows « 93:0 ») →
  press Enter.
- **Expected:** sent as 09:30 (review Finding 15: « « 930 » + Enter accepted »).
- **Observed:** nothing sent — SQL: 4 Nov `DepartureUtc` still null, no line today. « Valider » IS enabled (✅), and a
  click works, because the click's blur commits « 09:30 » first.
- **Cause (source):** the field is `ui/time-field.tsx`'s `TimeInput` with `pattern="([01][0-9]|2[0-3]):[0-5][0-9]"`
  and `required`; the declaration `<form>` has no `noValidate`, so implicit submission runs the browser's constraint
  validation on « 93:0 », which fails the pattern, and the form never submits (the native « format requis » bubble).
- **Owner:** `web/components/team/punch-flow.tsx` — the declaration `<form>` (~line 133); the step validates itself
  (`tooEarly` + the server), so `noValidate` there is the local fix.

## Observations (off-plan)

- At 1536×730 the sign-out prompt's title « Pointer votre départ ? » carries a visible focus ring (`shots/A10-C3-signout-pad-730.png`).
  That is `ui/dialog.tsx`'s title focus (shared by every dialog) after the walk's earlier keyboard input — not this change.
- **Environment side effect (not the product):** at start the QA API's Hangfire catch-up `take-recovery-points` ran on
  the DB copy, uploaded a 6 Nov recovery point per clinic and **pruned the 12 Oct zip of 8 clinics from the shared dev
  MinIO bucket**; the shared `clinic_management` still lists those 8 points. Dev data only; the shared stack's own
  nightly pass prunes the same oldest points. Next isolated run: a throwaway `MinIO__BucketName`.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | the declaration `<form>` let the browser run constraint validation, and the time field's `pattern` refuses « 93:0 » before it commits as 09:30 — so Enter submitted nothing while « Valider » looked ready | `web/components/team/punch-flow.tsx` (`noValidate` on that form, one-line comment) | 1 form; `ui/time-field.tsx` untouched (the « typed, not yet committed » path exists only here) |
| dialog title focus ring | observation | `ui/dialog.tsx` behaviour, not this change | — | not changed |
| MinIO recovery-point prune | environment | the QA API's Hangfire catch-up on a DB copy sharing the dev bucket | — | reported; memory updated for the next isolated run |

**Gates after the last edit:** check:responsive 86/86 ✅ · tsc ✅ · `next build` run after the walk, once the dev server
was stopped (see run-6) · no backend change.
**Re-run:** run-6.md.

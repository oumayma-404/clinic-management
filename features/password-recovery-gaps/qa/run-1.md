# QA run 1 — lost second factor: a visible way back

**Date:** 2026-11-03
**Cycle:** 1 of at most 3
**Plan:** `qa/plan.md` — driver `walk.mjs`; drives `walk-run1.log` … `walk-run1e.log` (probe fixes only, see below)
**Environment:** isolated stack — API `:5097` from this worktree (hosted profile, support number `+216 20 123 456`, auth limit
raised to 200 for the drive), web `next dev :3097`, throwaway DB `clinic_sfr_qa`, own headless Chrome, fresh cabinet per
drive. Peers: demo-cabinet session on its own ports, untouched.
**Widths looked at:** 320×640 · 390×844 · 1024 · 1279 · 1280 · 1440×900 · 1536×730 — `Read`: A2-recovery-1440,
C4-recovery-320, C1-users-1024, C2-users-1280, A4-stepup-1440
**Status:** RED (1)

## Probe fixes during the run (no source touched)

| Drive | Symptom | Cause |
|---|---|---|
| 1 | the secretary never had a second factor; 9 rows cascaded | on this profile only admins must hold one; a secretary's « Sécurité » is closed by design — the factor holder is now a **doctor** who enrols from « Sécurité », plus a **second admin** (A8) |
| 1 | D4 « all rows shown » | a menu left open by an earlier throw; a throw now presses Escape |
| 4 | D3 « En attente d'activation » instead of « Inactif » | deliberate: an account that never signed in reads that way (`User.IsPendingActivation`); D3 now uses the doctor |

## Scenarios (drive 5)

**57 ✅, 1 ❌, 0 ⏭**

| Row | Outcome |
|-----|---------|
| A1 | ✅ `auth/mode`: `vendorResetsSecondFactor: true`, `supportPhone` « +216 20 123 456 » |
| A2 | ✅ recovery step: exact sentence + `tel:+21620123456`; « Revenir au code de vérification » kept |
| A3 | ✅ doctor's menu offers « Réinitialiser le second facteur »; own row does not |
| A4 | ✅ step-up « Confirmer votre identité » names his e-mail, the codes, the sessions |
| A5 | ✅ toast; DB not enrolled + 0 codes; item gone from his menu |
| **A6** | ❌ **the sentence promises « en activera un nouveau à sa prochaine connexion »; the doctor signs in with his password alone** |
| A7 | ✅ his open session is closed (next page → `/login`) |
| A8 | ✅ second admin: toast, DB cleared, her next sign-in asks for a new factor |
| B1 | ✅ never-enrolled colleague: not offered |
| B2 | ✅ Annuler: nothing changes |
| B3 | ✅ wrong code: refusal in the dialog, still enrolled |
| B4 / B5 / B6 | ✅ LAN sentence · older API: colleague sentence only · failed probe: no line |
| C1 | ✅ 1024 and 1279: cards, no page scroll, menu inside the viewport |
| C2 | ✅ 1280 and 1440: table, « Actions » inside the viewport, no inner scroll |
| C3 | ✅ 320 and 390: cards, no page scroll |
| C4 | ✅ 320: line wraps, number on one line |
| C5 | ✅ 1536×730: « Confirmer » visible |
| D1 | ✅ reset password → « Mot de passe temporaire » |
| D2 | ✅ own « Désactiver » disabled |
| D3 | ✅ Désactiver → « Inactif » → Réactiver → « Actif » |
| D4 | ✅ search keeps only the match |
| D5 | ✅ admin, second admin and doctor sign-ins |

## Findings

### F-1 · major · the reset promises a next step that does not happen for a doctor

- **Repro:** `/users` → a doctor who turned a second factor on → « Réinitialiser le second facteur » → code → Confirmer;
  the doctor signs in again.
- **Expected:** what the dialog and toast say: « Cette personne en activera un nouveau à sa prochaine connexion. »
- **Actual:** he lands in the app with his password alone — only admins must hold a factor on this profile
  (`LoginCommand.cs:110`, `RequiresAdminSecondFactor && user.IsAdmin()`).
- **Evidence:** `walk-run1e.log` A6 « he met: the app, no second factor asked »; `shots/A6-doctor-after-reset-1440.png`.
- **Owner:** `web/components/user-management.tsx:214` (toast) and `:828` (step-up sentence).

## Observations (off-plan)

- **The same promise lives in four older places on `main`**: the e-mail and in-app notice the person receives
  (`SecondFactorResetNotice.cs` — « Vous enrôlerez … à votre prochaine connexion », with a 4-step walk-through), the vendor
  console's dialog (`console/components/reset-second-factor-dialog.tsx:256`) and the LAN verb
  (`ResetUserTotpConsoleCommand.cs:85`). The notice's own comment calls the sentence load-bearing, so not touched here.
- **A native « Please fill in this field » bubble shows the moment « Je n'ai plus accès à mon application » is pressed**
  (`shots/A2-recovery-1440.png`): the click lands as a submit of the new recovery form with its field still empty. Not from
  this change (the line is added after the buttons); likely React reusing the button node and flipping its `type`.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | the sentence promised a step only the admin rule triggers (`RequiresAdminSecondFactor && IsAdmin`) | `web/components/user-management.tsx` — step-up sentence and toast now say only what happened (factor + codes deleted, sessions closed) | 2 strings, 1 file; the 4 older copies on `main` (e-mail, in-app, console, LAN verb) → `follow-up/second-factor-reset-promises-re-enrolment.md` |

**Plan rows changed:** A4 (no claim about the next sign-in), A5 (new toast), A6 (doctor: password alone). A8 keeps the admin case.
**Gates after the last edit:** check:responsive 81/81 ✅ · tsc ✅ · build — after the re-run (needs the dev server stopped)

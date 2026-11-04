# QA run 6 — the guided tour end to end

**Change under test:** the owner's 2026-11-04 asks — (1) the lit control is always brought fully into view, window
and scrolling dialog body alike; (2) one continuous guided tour: after each step, back on the dashboard, the screen
dims around the next open step with « On attaque l'étape suivante : « … » ! Votre cabinet est configuré à N % » and
« C'est parti » / « Plus tard » (also on a new cabinet's first visit, « Bienvenue ! On commence par … », and after a
tick by hand); (3) all steps done → the « Bienvenue dans votre nouveau cabinet ! » window with confetti in waves;
(4) « Masquer » → the steps live on in « Aide » → « Premiers pas · N sur 7 », which brings the card back and
resumes the tour.
**Plan / driver:** `plan.md` + `walk.mjs` (rows A17, A18, C3, E4 added; E1/E2 now chained through « C'est
parti »; A14 checks the window) · **Logs:** `walk-run6.log` (first drive), `walk-run6b.log` (after two probe
fixes), `walk-run6c.log` (after the last source edit — the window's header centred)
**Environment:** web :3097 rebuilt after the last edit, API :5097 and DB `clinic_gfd_all` as before. `tsc` clean,
`check:responsive` 82/82 after the last edit.

**Status: GREEN** — 74 ✅ · 0 ❌ · 1 ⏭ (D5: no console account on this DB)

## New rows

| ID | Outcome | Evidence |
|----|---------|----------|
| A17 | ✅ | the dashboard a new cabinet lands on: « Bienvenue ! On commence par « Votre secrétaire ». … 14 % » — `shots/A17-welcome-1440.png` |
| A18 | ✅ | a tick by hand (« Terminé » on the phone window) prompts the next step, 29 % |
| A16 / A9 / E1–E3 | ✅ | after every tour: back on `/`, prompt on the next step with 43 → 57 → 71 → 86 %, prompt and row on screen — `shots/A16-prompt-1440.png` |
| E1 / E2 | ✅ | chained: « C'est parti » on the prompt starts the next tour itself |
| C1 | ✅ | 320 px: the prompt docked at the top, its row in view — `shots/C1-prompt-320.png` |
| C3 | ✅ | every bubble of every tour inside the window; at 1536×730 each lit control of the fiche in view |
| E4 | ✅ | « Masquer » → gone, « Ils restent dans « Aide » » → « Aide » shows « Premiers pas · 6 sur 7 » → card back, tour resumed — `shots/E4-aide-1440.png` |
| A14 | ✅ | the last tour's « Terminer » → « Bienvenue dans votre nouveau cabinet ! » window + confetti, on screen, « C'est parti ! » closes it — `shots/A14-ready-1536x730.png` |

## Findings

None.

## Probe fixes (not product)

| Row | Probe cause |
|-----|-------------|
| A17 | the welcome prompt belongs to the dashboard the sign-in lands on; the row reloaded the page first |
| A14 | the confetti waves are over in ~5 s; the row looked after its other checks — now read at once |

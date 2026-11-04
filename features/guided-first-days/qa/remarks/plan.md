# QA plan — owner's remarks of 2026-11-04 (fiche tour · phone link · « Aide » avis)

**Written:** 2026-11-04
**Change under test:** `feature/guided-first-days-all` @ 387030d5 + uncommitted remarks work
**Rendering files in the diff:** 6 — `lib/guides/dental-record.ts`, `components/guide/guide-overlay.tsx`,
`components/guide/guide-provider.tsx`, `components/patient-record-modal.tsx` (marks + 2 wrappers),
`components/dashboard/phone-app-dialog.tsx`, `components/feedback/feedback-dialog.tsx`
**Blast-radius table:** `features/guided-first-days/blast-radius.md` rows 33–42 (8 `must re-test`)
**Budget:** small → tiers A, B, C, D · 20 scenarios
**Stack:** API :5097 + web :3097 (production build) from this worktree, throwaway DB `clinic_gfd_all`, shared
mailpit read only. Own cabinet `qa-rk-<stamp>`; the owner's own Chrome and account are not touched.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/` « Premiers pas » | read the card | « Me guider : Première fiche de soins » present | browser | 1440 |
| A2 | `/` → « Votre téléphone » | open the dialog | a QR `<img>` (data: PNG) and « Ouvrir la page de téléchargement » with `href=https://apexa.tn/#telecharger` `target=_blank`; no « Android » heading, no « proposée » text | browser | 1440 |
| A3 | `GET /meta/phone-app` | curl | `url = https://apexa.tn/#telecharger`, `qrPng` starts `data:image/png;base64,` | api | — |
| A4 | tour, patient page | « Me guider » → open a patient → « Nouvelle fiche de soins » → « Suivant » ×6 | each step lights its mark (`record-acts`, `record-add-act`, `record-chart`, `record-notes`, `record-prescription`, `record-payment`, `record-submit`) with its sentence; last button reads « Terminer » | browser | 1536×730 |
| A5 | after « Terminer » | — | toast « Vous savez remplir une fiche de soins. »; bubble gone; **no** fiche in the DB; `WatchedGuidesCsv = dentalRecord` | browser + sql | 1536×730 |
| A6 | `/` | reload the card | « Première fiche de soins » done: no « Me guider » on it | browser | 1440 |
| A7 | header « Aide » → « Donner mon avis ou une idée » | open | « Écrire à APEXA », tab « Avis » selected, two star rows, two « Autre » inputs, « Un mot de plus ? »; tabs « Idée », « Problème », « Question » | browser | 1440 |
| A8 | same | 4 stars · chip « Agenda » · Autre (aimé) text · tab « Idée » text → « Envoyer » | toast « Merci… »; 2 rows: `avis` (HelpRating 4, LikedAreas agenda, LikedOther = text) and `idee` (Message = text) | browser + sql | 1440 |

| A9 | end of « Vos patients » guide (added run 2) | save the patient | confetti canvas appears; toast « 🎉 Votre premier patient est enregistré. » + its joke + « Prochaine étape : Votre secrétaire » + « Étape suivante » → `/` | browser | 1440 |
| A10 | end of the fiche tour (added run 2) | « Terminer » | « 🎉 Vous savez remplir une fiche de soins. » with the fiche joke | browser | 1536×730 |
| A11 | a step done by ordinary work (added run 2) | book a visit over the API, open `/` | « 🎉 C'est fait : Premier rendez-vous » + « Votre agenda commence à avoir du mordant. », no « Étape suivante » on the dashboard, shown once (a reload shows nothing) | browser | 1440 |

| A12 | phone window closed (added run 3) | « Terminé », and ✕ | the step ticks by hand (« Remettre à faire : Votre téléphone »), stored, celebrated | browser + sql | 1440, 320 |
| A13 | a step's circle (added run 3) | tick « Papier à en-tête », then untick | done + celebrated + stored; untick → open again with « Me guider » | browser + sql | 1440 |
| A14 | the last tick (added run 3) | tick the remaining steps | « 🎉 Votre cabinet est prêt. », the card goes | browser | 1440 |

| A15 | untick (added run 4) | « Remettre à faire : Première fiche de soins » | « Me guider » back on the row | browser + sql | 1440 |
| A16 | next-step highlight (added run 4) | finish any tour | back on `/`, the next open step's row ringed with « On attaque l'étape suivante ! Votre cabinet est configuré à N % » (N = done/7) | browser | 1440 |
| E1 | secretary tour (added run 4) | « Suivant » ×2, « Terminer » | the window opened by the tour; → `/`, ticked, no account created, « Premier rendez-vous » highlighted | browser + sql | 1440 |
| E2 | appointment tour (added run 4) | « Suivant » ×4, « Terminer » | → `/`, ticked, no visit booked, « Première fiche de soins » highlighted | browser + sql | 1440 |
| E3 | letterhead tour (added run 4) | « Suivant », « Terminer » | → `/`, ticked, no file asked for | browser + sql | 1440 |

**Run 4 changes (all guides are tours):** D1/A9 no longer type or save — the tour opens the window itself and
« Terminer » goes to `/` (no « Étape suivante » toast); B1 is now « no patient → one sentence + Terminer → ticked »;
A4 opens the patient and the fiche itself; A14 is reached by the fiche tour being the last open step; A11 runs on a
second, fresh cabinet (the first has every step ticked by then).

| A17 | first visit (added run 6) | the dashboard a new cabinet lands on | the tour starts: « Bienvenue ! On commence par « Votre secrétaire » … 14 % », « C'est parti » / « Plus tard » | browser | 1440 |
| A18 | a tick by hand (added run 6) | « Terminé » on the phone window | the next step is prompted | browser | 1440 |
| C3 | on screen (added run 6) | every tour bubble; the fiche at 1536×730 | each bubble inside the window, each lit control in view | browser | 1440, 1536×730 |
| E4 | « Aide » (added run 6) | « Masquer », then « Aide » → « Premiers pas » | « Premiers pas · 6 sur 7 » in « Aide »; the card back and the tour resumed | browser | 1440 |

**Run 6 changes:** the next step is PROMPTED by the tour (dim + bubble, « C'est parti » / « Plus tard ») instead of
a ringed row; E1/E2 start from that prompt; A14 is the « Bienvenue dans votre nouveau cabinet ! » window.

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | tour, no patient yet | « Me guider : Première fiche de soins » on an empty cabinet | toast « Ce guide a besoin d'un patient… », guide ends | browser | 1440 |
| B2 | tour stopped mid-way | step « record-chart » → Escape (closes the fiche) | guide ends; `WatchedGuidesCsv` still empty | browser + sql | 1440 |
| B3 | `PUT /dashboard/getting-started/securityCode/tick` | curl | 400, code `getting_started_step_not_tickable` | api | — |
| B6 | « Code de sécurité » row | read the card | no tick control on it | browser | 1440 |
| B4 | « Aide » form | open; then type on « Question » only; switch to « Avis » | « Envoyer » disabled on open, enabled after the Question text; the « Question » tab carries the filled dot; the typed text is still there when switching back | browser | 1440 |
| B5 | week-one card | (setup: own account 10 days old, 3 active days — SQL on the throwaway DB) wait for the card, tap 3 stars | « Votre avis sur APEXA » with 3 stars kept and two « Autre » inputs; type Autre (difficile) → Envoyer → row `semaine-1` with HardOther | browser + sql | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px | « Aide » form and phone dialog at 320×640 | no body overflow; the four tabs inside the dialog; phone dialog: QR withheld, button visible | browser | 320 |
| C2 | owner's height | tour at 1536×730, steps `record-payment` and `record-submit` | bubble inside the viewport, not over the lit control | browser | 1536×730 |

## Tier D — regression sweep (from the blast-radius table)

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #34 other guides | « Vos patients » guide | step 2 button reads « Suivant » (not « Terminer »), saving ends with « Votre premier patient est enregistré. » | browser | 1440 |
| D2 | #38 shared per-user row | `PUT /dashboard/preferences` hide then show a block | 200 both; `WatchedGuidesCsv` still `dentalRecord` | api + sql | — |
| D3 | #42 fiche wrappers | open « Nouvelle fiche de soins » without a guide; click « Notes de séance », then « Prescription » | both sections open (their fields appear); no guide bubble | browser | 1440 |
| D4 | #37 card read | `/` after the tour | card still renders its other steps (« Vos patients » done) | browser | 1440 |
| D5 | #40 notice e-mail | mailpit, console accounts | ⏭ unless a console account exists on this DB — e-mail content held by `FeedbackTests` | api | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| console « Retours » rendering of « Autre » | the console is not running on this stack; the DTO + `PlatformReadShape` are held by unit tests, the render is one string join |
| Enregistrer at the end of the tour | the tour says « n'enregistre rien »; a fiche is clinical data (verification.md § 7) |

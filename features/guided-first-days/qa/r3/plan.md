# QA plan — Guided first days, release 3 (part F « Me guider »)

**Written:** 2026-11-03
**Change under test:** `feature/guided-first-days-r3` — `2fec78fd` (engine, five guides, marks, N54)
**Rendering files in the diff:** `components/guide/{guide-provider,guide-overlay}.tsx`, `dashboard/getting-started-card.tsx`,
`appointment-calendar.tsx`, `create-appointment-dialog.tsx`, `edit-patient-dialog.tsx`, `patients-table.tsx`,
`patient-record-modal.tsx`, `user-management.tsx`, `letterhead/*`, `app/patients/{page,[id]/page}.tsx`, `app/layout.tsx`,
`ui/floating-notice.ts`, `post-visit-review-popup.tsx`, `feedback/feedback-prompt-card.tsx`
**Blast radius (R3 rows):** floating-notice selector (every Dialog/Sheet), the patients list wrappers (both trees),
the post-visit popup + week-one card gating, the « Premiers pas » buttons
**Budget:** large (a layer over every dialog) → tiers A–D · 26 scenarios

**Environment:** isolated stack — API `:5097` (worktree, `FrontendUrl=:3097`), web `next dev :3097`, throwaway
`clinic_gfd_c`, shared mailpit read only, own Chrome. A fresh cabinet + admin per drive (so « Premiers pas » shows).

## Tier A — each guide end to end (1440 × 900)

| ID | Steps | Expected (observable) | Layer |
|----|-------|-----------------------|-------|
| A1 | dashboard « Premiers pas » | « Me guider » on the five guided steps (each beside its video); « Code de sécurité » done; « Votre téléphone » keeps « Installer » | browser |
| A2 | « Me guider » · Votre secrétaire | lands on `/users`; bubble « Pour ajouter votre secrétaire, cliquez ici. »; body `data-guide-running="secretary"`; click the lit button → « Son nom et son e-mail… » + « Suivant »; fill, Suivant → « …créer son compte. »; send → toast « Votre secrétaire a son compte. », bubble gone, attribute gone | browser |
| A3 | « Me guider » · Vos patients | `/patients`; « Ajouter un patient » lit → dialog; names step; « Créer le patient » → patient page; toast « Votre premier patient est enregistré. » | browser |
| A4 | « Me guider » · Premier rendez-vous | `/appointments`; « Nouveau » lit → dialog; patient → jour/heure → acte → « Créer le rendez-vous » (any confirmation answered) → toast « …dans l'agenda. » | browser |
| A5 | « Me guider » · Première fiche de soins | `/patients` list lit → open a patient → « Nouvelle fiche de soins » lit → acts step → pick an act → « Enregistrer la séance » → toast « …fiche de soins est enregistrée. » | browser |
| A6 | « Me guider » · Papier à en-tête | `/settings`; « Importer mon papier à en-tête » lit → file step; a PNG chosen → « Voir l'aperçu » lit → « Enregistrer » lit → saved → toast « Vos documents s'impriment sur votre papier. » | browser |

## Tier B — alternate paths

| ID | Steps | Expected (observable) | Layer |
|----|-------|-----------------------|-------|
| B1 | patients guide, « Quitter le guide » | bubble gone, attribute gone, no toast | browser |
| B2 | secretary guide, dialog open, « Annuler » | guide ends, no « fait » toast | browser |
| B3 | patients guide, go to `/appointments` mid-way | guide ends | browser |
| B4 | patients guide on step 1, Escape | guide ends | browser |
| B5 | dental-record guide on a cabinet with NO patient | after ~10 s: toast « Ce guide a besoin d'un patient… », guide ends | browser |
| B6 | patients guide with A3's exact name | « existe peut-être déjà » confirmation → bubble hidden while it is up; « Créer quand même » → « fait » | browser |
| B7 | a due post-visit question, guide running 70 s | no « Le patient est-il venu ? » while the guide runs; it comes once the guide ends | browser |
| B8 | « Suivant » pressed inside a dialog | the dialog stays open | browser |
| B9 | type in a lit field, press « Suivant » | focus is still in that field | browser |

## Tier C — edge cases

| ID | Steps | Expected | Layer | Widths |
|----|-------|----------|-------|--------|
| C1 | appointment guide on a phone | the « + » is lit; bubble docked at the TOP (the « + » is low); no overflow | browser | 390×844 |
| C2 | patients guide, names step in the sheet | bubble inside the viewport, no overflow | browser | 320×640 |
| C3 | appointment guide, submit step | bubble fully on screen, not covering « Créer le rendez-vous » | browser | 1536×730 |

## Tier D — regression sweep

| ID | Row | Steps | Expected | Layer |
|----|-----|-------|----------|-------|
| D1 | patients list wrappers | `/patients` at 1440 and 390 | table rows (1440) / cards (390) visible; a row opens the patient | browser |
| D2 | booking dialog, no guide | agenda « Nouveau » | dialog opens, no bubble, `data-guide-running` absent | browser |
| D3 | « Premiers pas » buttons | security step « Commencer » (if not done) / phone « Installer » | unchanged | browser |
| D4 | chat notice over dialogs | — | ⏭ needs a second signed-in colleague sending a message; the selector change is additive (`, [data-guide-bubble]`) | — |

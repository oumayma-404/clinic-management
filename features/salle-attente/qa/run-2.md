# QA run 2 — Salle d'attente

**Date:** 2026-11-25
**Cycle:** 2 of at most 3 — after « walk-in notice says Sans RDV » (run 1 observation, fixed)
**Plan:** features/salle-attente/qa/plan.md (38 checked rows incl. new A4b + 4 ⏭: C3 C4 D9 D10)
**Driver:** features/salle-attente/qa/walk.mjs (run id `gqo2`, re-driven once after a probe fix — see below)
**Environment:** isolated — api :5098 (`dotnet run --no-build`, worktree Debug, restarted 20:46 by me on the rebuilt Debug output) on DB copy
`clinic_sa_qa` + bucket `clinic-sa-qa`; web `next dev` :3098 from the worktree (started by me). Shared :5000/:3000 untouched.
**Peer sessions live during the run:** clinic-management-01 (busy), clinic-management-8a, clinic-management-f7,
anakin-bb, anakin-d5 (idle) — none on these ports or this database.
**Widths looked at:** 320×640 · 390×844 (desk + chair) · 820×1180 · 1180×820 · 1440×900 · 1536×730
**Status:** GREEN

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| B9 | ✅ | idle panel « Fauteuil libre · Personne n'attend » disabled; shots/B9-chair-idle-1440.png |
| A1 | ✅ | lanes « À venir · En salle d'attente · Au fauteuil · Terminé »; P1 « 20:40 en retard de 11 min · Dr Nadia Trabelsi » |
| B5 | ✅ | no « Mon fauteuil » for the secretary |
| A2 | ✅ | « Amira Qagqo2a → En salle d'attente » + « Annuler » |
| A3 | ✅ | dentist notice « Patient arrivé · RDV 20:40 » |
| A4 | ✅ | dentist chips shown (group practice), P5 waiting « Sans RDV »; shots/A4-desk-dentist-chips.png |
| A4b | ✅ | dentist notice « Patient arrivé · Sans RDV » for the walk-in |
| A5 | ✅ | `IsWalkIn=t, ArrivedAt set, DoctorId=Nadia, BookedWithOverlap=t` |
| B3 | ✅ | walk-in menu « Ouvrir la fiche patient · Faire entrer · Parti sans être vu » (no « Annuler l'arrivée ») |
| B4 | ✅ | P5's row « Déjà dans la salle d'attente », disabled |
| A6 | ✅ | « Nouveau patient » prefilled Zaid / Qagqo2f → chips → waiting; 1 patient row |
| A7 | ✅ | mouse drag À venir → En salle d'attente moved P2; shots/A7-desk-dragging.png |
| A8 | ✅ | « Patient suivant » names P1, queue 4; shots/A8-chair-queue-1440.png |
| A9 | ✅ | « Amira Qagqo2a est appelé — la réception est prévenue », P1 au fauteuil |
| A10 | ✅ | desk notice « Dr Nadia Trabelsi · Faites entrer le patient suivant · Amira Qagqo2a · Voir la salle d'attente »; banner « faites entrer Amira Qagqo2a »; shots/A10-desk-rung-1440.png |
| A11 | ✅ | P1 → Terminé, P5 au fauteuil |
| A12 | ✅ | P1 `Status=4 (Completed)`, `LeftChairAtUtc` set |
| A13 | ✅ | « Annuler » put P3 back in « À venir », `ArrivedAtUtc` null |
| D8 | ✅ | waiting walk-in whose slot started: `Status=1` after ≥ 1 job minute |
| B1 | ✅ | `5 | Parti sans être vu` |
| B2 | ✅ | `1 | ∅` |
| A14 | ✅ | header « Salle d'attente : 2 patients pour vous » + « Suivant » on /appointments; shots/A14-chair-header-pill.png |
| A15 | ✅ | P5 « 20:51 – 20:51 · 0 min » in Terminé |
| B6 | ✅ | 409 `waiting-room-already-called` |
| B7 | ✅ | 400 `waiting-room-needs-dentist` |
| B8 | ✅ | 400 `waiting-room-walk-in-has-no-slot` |
| D6 | ✅ | rail lists « Salle d'attente » |
| D1 | ✅ | agenda shows P2 |
| D2 | ✅ | « À clôturer » renders, no alert |
| D3 | ✅ | /waiting-list heading « Liste d'attente » |
| D4 | ✅ | secretary `/` → `/salle-attente` |
| D7 | ✅ | dentist stays on `/` |
| C1 | ✅ | 320: main overflow 0, one column, input right edge 291; shots/C1-desk-320.png |
| D5 | ✅ | bar « Attente · À clôturer · Agenda · Patients », no « Accueil » |
| C2 | ✅ | no overflow at any width; PNGs read (820, 1440, chair 390) |
| C3 | ⏭ | long-press touch drag needs a coarse pointer; buttons cover every move |
| C4 | ⏭ | 20/40 min tones need a real wait or SQL backdating (banned) |
| D9 | ⏭ | fiche « Terminer la séance » writes a clinical record — `FicheVisitCompletionTests` |
| D10 | ⏭ | a payment writes money — interceptor unit tests |

## Findings

None.

## Observations (off-plan)

- (run 1's walk-in « RDV hh:mm » observation is fixed: `StaffMessageAppointmentDto.WalkIn` → « Sans RDV » — row A4b.)
- At 320 px the placeholder « Qui arrive ? Nom ou téléphone » is cut to « …télép ». Placeholder only.
- Five quick actions stack five toasts over the right of the stat strip for 8 s (sonner). Normal use is one at a time.

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| — | (run 1) `mkdir` EPERM on `…Oumayma%20Benkhalifa…` | shots path built from `URL.pathname`; switched to `fileURLToPath` |
| B9 A8–A12 A15 | first drive of run 2: « Patient suivant » named run 1's « Zaid Qa34klf » | run 1's visits were still waiting on the same day, so the product correctly called them first. Arrange now closes earlier QA runs' waiting / in-chair visits through the board (`Done`) before creating its own; re-driven, all green |

# QA plan — Salle d'attente

**Written:** 2026-11-25
**Change under test:** `feature/salle-attente` — 51de1663 (server) + 24d5ed4e (screen, « Patient suivant », ring)
**Rendering files in the diff:** 14 — `app/salle-attente/*`, `components/waiting-room/*` (5), `dashboard-header`,
`bottom-nav`, `staff-chat-notices`, `photo-import/new-patient-for-group`, `dashboard-page`, `waiting-list/page`, `lib/nav`
**Blast-radius table:** `features/salle-attente/plan.md` § 5 (re-test column, S1–S5)
**Budget:** large (new screen + clinical lane writes + a shared notice) → tiers A, B, C, D · 32 scenarios

**Stack:** isolated — API :5098 on DB copy `clinic_sa_qa` (bucket `clinic-sa-qa`), web `next dev` :3098 from the
worktree. Two browser contexts side by side: **desk** = `qa.secretary@ibnkhaldoun.test`, **chair** =
`qa.doctor@ibnkhaldoun.test` (= Dr Nadia Trabelsi). Group practice (Dr Salma Ben Youssef + Dr Nadia Trabelsi).

**Arrange (through the product's API, unique suffix per run):** 5 patients `SAQA-<run> …`; today: P1 Nadia at
now−10 min (late), P2 Nadia now+20, P3 Nadia now+40, P4 Salma now+25; P5 has no RDV (walk-in). P6 is typed new.

## Tier A — happy path

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | desk `/salle-attente` | open | 4 lane headings « À venir · En salle d'attente · Au fauteuil · Terminé »; P1–P4 cards in « À venir »; stat « À venir » ≥ 4; P1 card shows « en retard » | browser | 1440 |
| A2 | desk « Qui arrive ? » | type P1's name → press the RDV row | toast « P1 → En salle d'attente » with « Annuler »; P1 card in the waiting lane | browser | 1440 |
| A3 | chair | after A2 | a chat notice reaches the dentist (« Patient arrivé » style label) — arrival still announced | browser | 1440 |
| A4 | desk walk-in | type P5 → « Sans rendez-vous » row → « Dr Nadia » chip | P5 card in waiting lane tagged « Sans RDV » | browser | 1440 |
| A4b | chair | after A4 | the dentist's arrival notice for P5 reads « Patient arrivé · Sans RDV » (no « RDV hh:mm ») — added after run 1 | browser | 1440 |
| A5 | — | after A4 | P5's appointment: `IsWalkIn=true`, `ArrivedAtUtc` set, `DoctorId`=Nadia, `BookedWithOverlap=true` | sql | — |
| A6 | desk new patient | type « SAQA-<run> Nouveau » → « Nouveau patient « … » » → Créer → « Dr Nadia » | P6 card in waiting lane; exactly one patient row with that name | browser + sql | 1440 |
| A7 | desk drag | drag P2 card from « À venir » onto « En salle d'attente » | P2 card now in waiting lane; toast with « Annuler » | browser | 1440 |
| A8 | chair `/salle-attente` | open | « Mon fauteuil » panel: « Fauteuil libre », button « Patient suivant » naming P1 (first in Nadia's queue), queue count ≥ 3 | browser | 1440 |
| A9 | chair | press « Patient suivant » | toast « P1 est appelé — la réception est prévenue »; panel « Au fauteuil » shows P1 | browser | 1440 |
| A10 | desk | after A9 | urgent notice « Faites entrer le patient suivant » + P1's name + « Voir la salle d'attente »; board shows the call banner « faites entrer P1 » | browser | 1440 |
| A11 | chair | press « Patient suivant » again | P1 → « Terminé », next patient in chair | browser + sql | 1440 |
| A12 | — | after A11 | P1: `Status=Completed`, `LeftChairAtUtc` set | sql | — |
| A13 | desk undo | mark P3 « Arrivé » then press the toast's « Annuler » | P3 back in « À venir »; `ArrivedAtUtc` null | browser + sql | 1440 |
| A14 | chair header pill | open `/appointments` as the dentist | header pill shows the waiting count and « Suivant » | browser | 1440 |
| A15 | desk « Terminer » | press « Terminer » on the chair card | card moves to « Terminé » with « hh:mm – hh:mm » | browser | 1440 |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | desk card menu | waiting card ⋯ → « Parti sans être vu » | card in « Terminé » tagged « Parti sans être vu »; sql `Status=Cancelled`, reason « Parti sans être vu » | browser + sql | 1440 |
| B2 | desk card menu | that card ⋯ → « Remettre en salle d'attente » | back in waiting lane; sql `Status=Scheduled`, cancel reason null | browser + sql | 1440 |
| B3 | walk-in card menu | open P5's ⋯ | no « Annuler l'arrivée »; « Parti sans être vu » present | browser | 1440 |
| B4 | already here | type P5's name in « Qui arrive ? » | P5 row reads « Déjà dans la salle d'attente » and is disabled | browser | 1440 |
| B5 | desk | look for « Mon fauteuil » | panel absent for the secretary (`canCallNext=false`) | browser | 1440 |
| B6 | api double call | `call-next` with a stale `expectedNextId` | 409, code `waiting-room-already-called` | api | — |
| B7 | api walk-in, group practice, no dentist | `walk-in` without `doctorId` | refusal code `waiting-room-needs-dentist` | api | — |
| B8 | api walk-in to « À venir » | `move` a walk-in to `Upcoming` | refusal code `waiting-room-walk-in-has-no-slot` | api | — |
| B9 | chair, empty | after the queue is drained (API moves), panel | « Personne n'attend » disabled, or « Terminer la séance » when the chair holds someone | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px floor | desk board at 320×640 | `main.scrollWidth − main.clientWidth ≤ 1`; lanes one column; arrival field fully visible | browser | 320 |
| C2 | eye pass | desk + chair screenshots | nothing clipped or overlapping (read the PNGs) | browser | 390, 820, 1180, 1440, 1536×730 |
| C3 | touch long-press drag | — | ⏭ — the dev browser cannot emulate a coarse pointer's long press reliably; buttons cover the same moves | browser | — |
| C4 | wait tones 20/40 min | — | ⏭ — needs a real 20 min wait or SQL backdating of arrival (arranging through SQL is banned) | browser | — |

## Tier D — regression sweep

| ID | Re-test row | Area | Expected (observable) | Layer | Widths |
|----|-------------|------|-----------------------|-------|--------|
| D1 | S1 agenda | desk `/appointments` | agenda renders today's visits (P2's block present) | browser | 1440 |
| D2 | S1/S3 « À clôturer » | desk `/a-cloturer` | heading « À clôturer » renders, no error notice | browser | 1440 |
| D3 | S2 `/waiting-list` | desk | page heading « Liste d'attente » | browser | 1440 |
| D4 | S2 secretary home | desk `/` | redirected to `/salle-attente` | browser | 1440 |
| D5 | S2 bottom bar | desk at 390 | bar's first tab « Attente » → `/salle-attente`, no « Accueil » | browser | 390 |
| D6 | S2 rail | desk 1440 | rail « Quotidien » lists « Salle d'attente » | browser | 1440 |
| D7 | S2 doctor home | chair `/` | dashboard renders (no redirect) | browser | 1440 |
| D8 | S1 progress job | — | an arrived, still-waiting visit whose slot started is NOT `InProgress` | sql | — |
| D9 | S3 fiche closes chair | — | ⏭ — writes a clinical record (verification § 7); covered by `FicheVisitCompletionTests` | — | — |
| D10 | S6 payment closes chair | — | ⏭ — writes money (verification § 7); covered by the interceptor's unit tests | — | — |

## Not covered, and why

| Area | Why not |
|------|---------|
| fiche « Terminer la séance » / a payment ending the chair | clinical + money writes; unit-tested (D9, D10) |
| phone push / Windows notification of the ring | needs a device; the in-app notice is A10 |
| touch drag | C3 |

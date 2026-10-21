# QA plan — Congés du praticien

**Written:** 2026-10-20
**Change under test:** `feature/doctor-conges` (worktree), uncommitted, off `origin/main` 59e88ec9
**Rendering files in the diff:** 6 — `appointment-calendar.tsx`, `agenda-phone-header.tsx`, `doctor-leaves/doctor-leave-dialog.tsx`, `doctor-leaves/doctor-leaves-card.tsx`, `mon-profil-content.tsx`, `clinic-settings.tsx`
**Blast-radius table:** `features/doctor-conges/progress.md` § Blast Radius (7 rows `must re-test`)
**Budget:** small → tiers A, B, C (reachable), D · 26 scenarios
**Stack:** isolated — API :5099 + web :3099 from the worktree, on `clinic_conges_qa` (a pg_dump copy of the dev DB). The shared DB is never migrated.
**Data:** clinic « Cabinet Dentaire Ibn Khaldoun », admin Salma Ben Youssef (`salma.benyoussef@…`), two active practitioners (Salma Ben Youssef, Nadia Trabelsi). Congé window: a Mon–Wed of a future week **W** chosen where Salma has a booked visit.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/appointments` bar | look at the bar | a « Congé » button (`aria-label="Ajouter un congé"`) sits just before « Nouveau »; text « Congé » visible at 1440, icon only at 820; bar is ONE row (height ≤ 60 px) | browser | 1440, 820 |
| A2 | `/appointments` phone header | look at the header | a button `aria-label="Ajouter un congé"`, box ≥ 44 px tall | browser | 390 |
| A3 | `/appointments?date=<W Mon>&view=week` | « Congé » → dialog | title « Nouveau congé »; « Du » = W Mon (the day on screen); Praticien select present, empty or the admin's own (Salma) | browser | 1440 |
| A4 | same dialog | Praticien = Salma, Au = W Wed, Motif « Vacances », Enregistrer | dialog closes; a toast « Congé enregistré » (or the « N rendez-vous » warning variant, see A6) | browser | 1440 |
| A5 | — | read the row | `DoctorLeaves`: one row for Salma, `StartDate`=W Mon, `EndDate`=W Wed, `Note`='Vacances' | sql | — |
| A6 | toast of A4 | Salma has a booked visit inside W Mon–Wed | toast reads « 1 rendez-vous est déjà prévu … » (or « N rendez-vous sont … ») with a « Voir » action; that visit's row is unchanged in SQL | browser + sql | 1440 |
| A7 | toast « Voir » | press « Voir » | agenda switches to Jour on the visit's day, praticien filter = Salma | browser | 1440 |
| A8 | `/appointments?date=<W Mon>&view=week` (unfiltered, 2 doctors) | look at the header | an amber ribbon « Congé — Salma Ben Youssef » under each of Mon, Tue, Wed; Thu has none; hour cells of Mon–Wed are **not** hatched (Nadia works) | browser | 1440, 820 |
| A9 | same, filtered on Salma (`&doctorId=<Salma>`) | look | Mon–Wed headers read « congé » instead of the RDV count; their hour cells are hatched; Salma's booked visit still drawn | browser | 1440 |
| A10 | ribbon click | click the ribbon on Tue | dialog « Modifier le congé » with Du = W Mon, Au = W Wed, Motif « Vacances » | browser | 1440 |
| A11 | same dialog | Au = W Tue, Enregistrer | toast; ribbon now on Mon + Tue only; SQL `EndDate` = W Tue | browser + sql | 1440 |
| A12 | `/mon-profil` | look | card « Mes congés » lists « du … au … · 2 jours · Vacances »; row has a « ⋯ » menu with « Modifier le congé » / « Supprimer le congé » | browser | 1440, 320 |
| A13 | `/settings` → Médecins | open « Congés de ce praticien » under Salma | the same congé listed; « Ajouter un congé » present | browser | 1440 |
| A14 | ribbon → dialog → « Supprimer » | confirm | confirm title names « Salma Ben Youssef » and the period; after confirm the ribbon is gone; SQL row gone | browser + sql | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | new-congé dialog | Du = W Wed, then Au = W Mon, Enregistrer | field error « Le dernier jour doit être le même jour ou après le premier. »; dialog stays open; no row written | browser + sql | 1440 |
| B2 | API | POST a congé overlapping an existing one | 400, error « Salma Ben Youssef est déjà en congé du … » | api | — |
| B3 | API | POST a 367-day congé | 400, « …ne peut pas dépasser 366 jours… » | api | — |
| B4 | API | POST an appointment for Salma on W Tue (congé) | 400 `code: outside_working_hours`, error contains « est en congé » | api | — |
| B5 | API | same with `allowOutsideWorkingHours: true` | 2xx, the visit is booked | api | — |
| B6 | API | same day, `doctorId` = Nadia | 2xx (another practitioner is unaffected) | api | — |
| B7 | new-congé dialog | type a motif, press Annuler | « discard changes » confirmation appears | browser | 1440 |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px dialog | open « Congé » from the phone header at 320×640 | bottom sheet; `body.scrollWidth` ≤ 320; « Enregistrer » fully inside the viewport | browser | 320 |
| C2 | owner's laptop height | dialog at 1536×730 | footer buttons inside the viewport, no inner scroll needed | browser | 1536×730 |
| C3 | phone Semaine | W at 390 | each congé column shows a compact « Congé » pill; no horizontal page scroll | browser | 390 |
| C4 | phone Jour | W Mon at 390, Jour | the ribbon « Congé — Salma Ben Youssef » is visible although the date header is not rendered | browser | 390 |
| C5 | phone Mois | month of W at 390 | Mon–Wed cells carry « congé »; their day button's `aria-label` contains « en congé : Salma Ben Youssef »; no chip clipped below the cell | browser | 390 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #3 code reused | create dialog for Salma on a congé day | the « Continuer quand même » confirmation shows the sentence « … est en congé … » | browser | 1440 |
| D2 | #4/#5 closed day | a Sunday (clinic closed) in Semaine | header reads « fermé »; the Sunday hour cells are hatched | browser | 1440 |
| D3 | #6 sticky header | Semaine, scroll the grid 600 px | the date header (and ribbon) stay at the top of the scroller | browser | 1440 |
| D4 | #12 migration | `verify-schema` on the copy before and after | before: only `DoctorLeaves` missing + pre-existing drift; after: pre-existing drift only | console | — |
| D5 | #1 guard | PUT an existing future visit of Salma (outside the congé) with a new time | 2xx, no false congé refusal | api | — |
| D6 | #13 Mon profil | `/mon-profil` | « Mes horaires » card still renders above « Mes congés » | browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| Doctor editing a colleague's congé (read-only view), secretary editing any | no known password/second factor for `qa.doctor` / `qa.secretary`; covered by handler tests in `/test-small-feature` |
| Recurring series on a congé day | the series reuses the same guard per occurrence (unit-testable); no recurring screen is routed (`/recurring-series` is withdrawn) |
| Realtime refresh on a second device | needs two sessions; a second browser on the same account rotates its session — covered by the `doctors` key already declared |

# QA run 1 — Congés des secrétaires

**Date:** 2026-11-05
**Cycle:** 1 of at most 3
**Plan:** features/conges-secretaires/qa/plan.md (33 rows: P3 A18 B6 C5 D5 + setup-R6)
**Driver:** features/conges-secretaires/qa/walk.mjs (78 checks; `ONLY=<id>` re-drives one row)
**Environment:** isolated — api :5096 (worktree `bin/Debug`, started 2026-11-05 ~12:25 by me) · web :3096 `next dev` (worktree, started by me) · DB `clinic_staffleave_qa` (pg_dump of `clinic_management`, migrated by the branch) · mailpit :8025 (shared, filtered by recipient + run start). Two visible Chrome windows over CDP (own profiles, not the shared Playwright one).
**Peer sessions live during the run:** clinic-management-c2 (busy), clinic-management-29 (idle), clinic-management-77 (shell) — none touch :5096/:3096 or this DB.
**Widths looked at:** 320×640 coarse · 390×844 coarse · 820×1024 · 1440×900 · 1536×730 · admin window 986×738 · secretary window 546×738
**Status:** RED (2 findings: 0 blocker, 1 major, 1 minor)

## Answered before the browser

| ID | Outcome | Evidence |
|----|---------|----------|
| P1 / D4 | ✅ | `verify-schema` before → after on the same DB: 7 DRIFT lines gone (3 `StaffLeaveRequests`, 4 `DoctorLeaves` from main), **0 new**; numeric columns `(18,3)` ok |
| P2 / D5 | ✅ | unfiltered suite: `Passed 6220, Failed 0, Skipped 6` (28 s) |
| P3 | ✅ | module off: `POST /api/team/me/leave-requests` → `400 {"error":"Les demandes de congé ne sont pas activées pour ce cabinet."}`, 0 rows |

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | switch `aria-checked=false`; shots/A1-settings-off.png |
| A2 | ✅ | 0 `a[href="/conges"]`; « Les demandes de congé ne sont pas activées »; no tab on /users |
| A3 | ✅ | toast; secretary menu + page switched with no reload (realtime) |
| A4 | ✅ | « 24 jours restants · 0 jour pris sur 24 jours »; the secretary's page followed live |
| A5 | ✅ | preview « 3 jours · il restera 21 jours »; toast; card « En attente » |
| A6 | ✅ | one row: `admin \| - \| Ines Gharbi demande un congé du 16/11 au 18/11 (3 jours).` |
| A7 | ✅ | rail badge 1; bell row; click → `/users?onglet=conges`, tab selected with badge 1 |
| A8 | ✅ | card: name, statut, « Congé payé · 3 jours », motif, « Solde après : 21 jours » |
| A9 | ✅ | Décompter prefilled 3 → 2,5; « 2,5 jours décomptés (3 jours demandés) » + « Accepté par Dr Salma Ben Youssef »; `CountedDays=2.500` |
| A10 | ✅ | her card turned « Accepté » live; « 21,5 jours restants »; bell « Congé accepté » |
| A11 | ✅ | one e-mail « Congé accepté : du 16/11 au 18/11 », body « Jours décomptés : 2,5 jours » |
| A12 | ✅ | « 0,5 jour »; empty motif → « Indiquez le motif du refus. »; toast « Demande refusée » |
| A13 | ✅ | card « Refusé » + « Motif du refus : « Période chargée » »; e-mail quotes it |
| A14 | ✅ | absence saved « Accepté » + « Saisi par l'admin »; one notification to her |
| A15 | ❌ F-1 | preview « 4 jours · il restera **17,5** jours », admin card « Solde après : **17,5** jours »; « Déjà en congé sur ces dates : QA Secrétaire 991861 » ✅ |
| A16 | ✅ | after acceptance: « **19,5** jours restants · 4,5 jours pris sur 24 jours » |
| A17 | ✅ | confirm « Annuler le congé le 14 décembre 2026 ? » + « Ce congé était accepté. »; « Annulé »; admins notified « Ines Gharbi a annulé son congé le 14/12. » |
| A18 | ✅ | started absence: no « Annuler » on her card; API cancel → 400 « …a commencé… »; she is notified |
| B1 | ✅ | « Le dernier jour doit être le même jour ou après le premier. » |
| B2 | ✅ | « Un congé ne peut pas dépasser 366 jours — vérifiez l'année. » |
| B3 | ✅ | « Indiquez le motif. », dialog stays open |
| B4 | ✅ | « Demi-journée » only when Du = Au |
| B5 | ✅ | « Vous avez déjà une demande du 16/11 au 18/11. » (AC-4's own wording); shots/B5-overlap.png |
| B6 | ✅ | admin's Accepter open, she withdraws → « Cette demande a déjà reçu une réponse. » + « Recharger » → closes, R4 « Annulé », status 3 |
| B7 | ⏭ | not exercised — no doctor session in this walk; API refusal is `/test-small-feature`'s |
| C1 | ✅ | 320 coarse: overflow 0 px; card « Annuler » ≥ 44 px |
| C2 | ❌ F-2 | bottom sheet ✅ and « Envoyer la demande » in view ✅, but « Annuler » is cut at the bottom edge and the footer scrolls with the form; shots/C2-320-sheet.png |
| C3 | ✅ | admin congés at 320: overflow 0; Accepter / Refuser ≥ 44 px |
| C4 | ✅ | « Refuser la demande » inside 1536×730 |
| C5 | ✅ | off → her menu and page follow live, tabs gone; on → all 7 of her congés back (8 rows) |
| D1 | ✅ | « Comptes » at 1440 (« Fiche » link) and 820 (⋯ → « Voir sa fiche et ses congés »), overflow 0 |
| D2 | ✅ | « Équipe » card fits at 1440 and 390 |
| D3 | ✅ | badge on « Utilisateurs »: expanded rail, collapsed rail, phone drawer at 390 (re-driven after a probe fix) |

## Findings

### F-1 · major · a congé across 31 déc → 2 janv announces the wrong balance before it is accepted

- **Repro:** allowance 24, 2,5 days already taken. Secretary asks Congé payé 30/12/2026 → 02/01/2027.
- **Expected (AC-3 + AC-12):** the form and the admin card state the 2026 balance after it: 24 − 2,5 − 2 (30 and 31 déc) = **19,5 jours**.
- **Actual:** the form says « 4 jours · il restera **17,5** jours » and the admin card « Solde après : **17,5** jours ». Once accepted, her card, her file and the e-mail all say **19,5** (« Congé payé restant en 2026 : 19,5 jours »). Two screens disagree about one number.
- **Evidence:** shots/A15-others-off.png; captured preview text; mailpit body.
- **Suspected owner:** `api/ClinicManagement.Application/Features/Team/Queries/PreviewStaffLeaveQuery.cs:79-81` and `Queries/ListStaffLeaveRequestsQuery.cs:93-95` subtract the whole request (`days` / `RequestedDays`) instead of its share in the start year (`StaffLeaveRules.DaysInYear`, which `Balance` already uses).
- **Regression?** No — new with this change.

### F-2 · minor · at 320×640 the request sheet's footer is not pinned

- **Repro:** secretary, 320×640 touch, « Demander un congé ».
- **Expected (AC-15):** a bottom sheet whose main button stays visible (above the keyboard).
- **Actual:** the whole sheet scrolls as one block; « Annuler » is cut by the bottom edge, and « Envoyer la demande » sits at the end of the scroll, so with the keyboard up on « Motif » nothing keeps it in view.
- **Evidence:** shots/C2-320-sheet.png.
- **Suspected owner:** `web/components/staff-leave/staff-leave-request-dialog.tsx:147` — the form is not inside a `DialogBody`, so `DialogFooter` is not pinned (frontend-web.md § 5). Same in `AcceptLeaveDialog` / `RejectLeaveDialog` (smaller, fit today). The doctor congé dialog has the same shape (pre-existing, not this change).
- **Regression?** No — new surface.

## Observations (off-plan)

- `GET /api/team/me/leave` answers 200 with her history while the module is off (only the write is refused). The page never calls it while off. AC-1's « the API refuses » is read here as « refuses to write »; not chased.
- The decision e-mail prints « Cabinet : » with nothing after it — this dev clinic's `Name` is empty in the database. A real cabinet has a name; an empty detail could be omitted. Not chased.
- Server sentences (bell, e-mail, overlap refusal) use short dates « du 16/11 au 18/11 » while screens use « du 16 novembre au 18 novembre 2026 ». Matches the spec's own AC-4/AC-5 wording and the doctor congés. Not a defect.
- After the driver exits, the admin window shows the real « Séance terminée » prompt for a visit in the copied DB (the driver's route that empties that queue dies with it).

## Probe bugs fixed during the run (not product defects)

| Row | Symptom | Real cause |
|-----|---------|------------|
| A6, A11, A17, B5 | text « not found » | the probe expected long dates; the server writes « du 16/11 au 18/11 » / « le 14/12 », as the spec's AC-4 example does |
| D3 | `click` timeout (30 s), twice | first a cold `next dev` compile of the dashboard; then `next dev`'s « N » indicator sits over the collapsed rail's « Développer » button (dev only) — clicked from the DOM instead |

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | the « balance after » subtracted the whole request, while `Balance` charges a cross-year congé by year (`DaysInYear`) | `StaffLeaveRules.cs` (new `ShareInYear` + `BalanceAfter`; `DaysInYear` delegates, same output), `PreviewStaffLeaveQuery.cs`, `ListStaffLeaveRequestsQuery.cs` | 2 call sites of the arithmetic, both moved to the one owner; 5 `Balance` readers unchanged |
| F-2 | confirmed | the three congé dialogs put their form straight in `DialogContent`, so the whole sheet scrolled and the footer was not pinned | `staff-leave-request-dialog.tsx`, `staff-leave-decision-dialogs.tsx` (Accepter + Refuser) | 3 dialogs, all fixed; `doctor-leave-dialog.tsx` has the same shape (pre-existing, other feature — not changed) |

**Guards added:** none new — `check:responsive`'s `dialog-owns-one-scroller` already holds the `DialogBody` shape; `BalanceAfter` cross-year added to progress.md § Deferred to /test-small-feature.
**Gates after the last edit:** check:responsive 85/85 ✅ · tsc ✅ · dotnet build ✅ · dotnet test 6220/0 ✅ · `next build` not run (a `next dev` holds this worktree's `.next`; CI's web job is the gate)
**Re-run:** run-2.md — GREEN

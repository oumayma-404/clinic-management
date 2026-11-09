# QA run 1 — Pointage obligatoire (part 3)

**Date:** 2026-11-06 (09:18–09:30 Tunis — the plan was written at 00:00; B5's setup was made clock-independent, see probe 2)
**Cycle:** 1
**Plan:** `features/pointage-obligatoire/qa/plan.md` (34 scenarios)
**Driver:** `features/pointage-obligatoire/qa/walk.mjs` — one launch, own artefacts arranged first; D3 and B5 re-driven
in the same launch after their probe fixes (`ONLY=D3,B5`)
**Environment:** isolated — API :5096 (`dotnet run`, worktree, mine) · web :3096 `next dev` (mine, fresh) · DB
`clinic_pointage_qa` (copy of `clinic_management`, every migration applied) · Chrome 9333 (Dr Salma + borne tab +
context « BS » = Ines on the borne device, cookie copied) / Chrome 9334 (Ines on an ordinary device)
**Peer sessions live during the run:** 7 (none on :5096 / :3096 / `clinic_pointage_qa`): clinic-management-77,
-29, -c2, career-ops ×2, anakin ×2
**Arrange done in SQL (my DB only):** pointage + lock on, Friday's hours, Ines's schéma/lines cleared; hour changes
for B5/B6/A17, the half-day congé of B7, and clearing Ines's lines between rows. Everything under test went through
the product.
**Status:** RED (1 finding)

## Scenario results

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Bloquer l'app tant que l'arrivée n'est pas pointée », ON by default — `shots/A1-settings-lock-switch.png` |
| A2 | ✅ | borne « Accueil » registered (step-up) |
| A3 | ✅ | lock « Pointez votre arrivée » + « Créer mon schéma » + « Se déconnecter » — `shots/A3-lock-no-schema.png` |
| A4 | ✅ | « Mon profil » not covered without a schéma; schéma saved while locked (exempt) |
| A5 | ✅ | « Borne : Accueil », only control « Se déconnecter » — `shots/A5-lock-named-borne.png` |
| A6 | ✅ | `POST /api/patients` → 423 `clock_in_required` « Pointez votre arrivée à la borne… », 0 rows written; clock-status locked / [Accueil] / not a borne |
| A7 | ✅ | punch at the borne → Ines's lock closed on its own, same page load (window marker kept) |
| A8 | ✅ | « Pointer votre départ ? · Vous êtes présente depuis 09:20. Borne : Accueil » + « Se déconnecter sans pointer »; « Annuler » kept her in — `shots/A8-signout-prompt.png` (560, bottom sheet) |
| A9 | ✅ | on the borne device the lock carries the pad; punching from it closes the lock, one open line — `shots/A9-lock-with-pad.png` |
| A10 | ❌ | prompt carries the pad (« Départ »), the punch closes today's line and signs her out — **but « Départ enregistré à HH:mm. » is never seen** (F1) |
| A11 | ✅ | admin adds 5 Nov 08:00, no departure → « 08:00 – Départ non pointé » |
| A12 | ✅ | after the schéma: « Votre départ d'hier n'a pas été pointé », Arrivée 08:00, Dernière action « aucune » = the journal (no non-pointage row for Ines on 5 Nov); nothing written yet — `shots/A12-declaration-step.png` |
| A13 | ✅ | 17:30 → « Arrivée enregistrée »; SQL `17:30|1|` (DeclaredBySelf, no last save); today's line opened |
| A14 | ✅ | « 08:00 – 17:30 » in amber + « Déclaré » + « Aucune action dans l'app ce jour » — `shots/A14-sheet-declared.png` |
| A15 | ✅ | total « 1 départ déclaré », 570 min counted |
| A16 | ✅ | admin « Ajouter un pointage » today → Ines's lock lifted, same page load |
| A17 | ✅ | Hangfire « remind-missing-departures »: one row « Départ non pointé · Vous êtes toujours présente depuis 09:22. » target `Attendance`, nobody else, today's line `xmin` unchanged |
| B1 | ✅ | `PUT /team/me/schema`, `PUT /notifications/read-all`, `POST /team/me/leave-requests` — none 423 |
| B2 | ✅ | `GET /patients`, `GET /appointments` 200 while locked |
| B3 | ✅ | switch off → toast, stored `f`, not locked, a write not 423; the open screen unlocked within the minute, same load; back on → locked |
| B4 | ✅ | after her departure: not locked, `presentSince` null, no lock after reload |
| B5 | ✅ (re-driven) | opening an hour from now → not locked, no lock on screen; restored → locked |
| B6 | ✅ | Friday closed → not locked |
| B7 | ✅ | accepted half-day congé today → not locked; removed → locked |
| B8 | ✅ | Dr Salma tracked by override, no line → no lock (DEV-9) |
| B9 | ✅ | 07:00 → « L'heure de départ doit être après l'arrivée (08:00). », « Valider » disabled, nothing written |
| B10 | ✅ | « Retour » → pad; nothing written; schéma again → the step again |
| B11 | ✅ | second job run → still one reminder |
| B12 | ✅ | not present → « Se déconnecter » signs out at once |
| C1 | ✅ | 320×568: no scroll, « Se déconnecter » fully on screen — `shots/C1-lock-320.png` |
| C2 | ✅ | 812×380: title not clipped, sign-out reachable — `shots/C2-lock-380.png` |
| C3 | ✅ | 1536×730: « Se déconnecter sans pointer » on screen — `shots/A10-C3-signout-pad-730.png` |
| C4 | ✅ | 820×600: field + « Valider » on screen — `shots/C4-declaration-820x600.png` |
| C5 | ✅ | `/pointage` renders the borne for a locked, signed-in secretary, no lock over it |
| C6 | ✅ | correcting 5 Nov (motif) → « Corrigé », « Déclaré » gone, total drops it, `DepartureSource 2` |
| D1 | ✅ | ordinary arrival / departure at the borne (A7, B4) |
| D2 | ✅ | congés switch off/on, lock switch untouched |
| D3 | ✅ (re-driven) | `user-status` → `clinic.attendanceLockEnabled: true` |
| D4 | ✅ | bell lists « Départ non pointé », click → `/mon-profil` — `shots/D4-bell-reminder.png` |
| D5 | ✅ | `/login` renders, no lock |

Checks: 100 ✅ · 1 ❌ · 0 ⏭ (101 assertions over 34 scenarios). Not exercised by design (plan § Not covered): PC de
secours, lapsed subscription, push delivery, CSV.

**Widths looked at:** 320×568 · 560×760 · 812×380 · 820×600 · 820×1000 · 820×1180 · 1000×830 · 1440×900 · 1536×730.

## Findings

### F1 · minor · the departure punched from the sign-out prompt is never confirmed
- **Repro:** Ines signed in on a borne device, present today → « Mon compte » → « Se déconnecter » → draw the schéma
  in « Pointer votre départ ? ».
- **Expected:** « Départ enregistré à HH:mm. » readable before she is signed out (§ 13: success is a toast).
- **Actual:** the departure is recorded (SQL: today's line closed) and she lands on `/login`, but no toast is ever
  observed in 8 s of polling — `logout()` does a full navigation (`window.location.href`) right after
  `toast.success`, so the toast dies with the page. She leaves with no proof her departure landed.
- **Owner:** `web/components/team/sign-out-prompt.tsx:45-48` (`onDone` → `toast.success` then `logout()`), with
  `web/lib/auth/session.tsx` navigating away.

## Probe defects (fixed in the walk, re-driven in this launch — no source change)

1. **D3** read `json.clinic`; the endpoint wraps its body in `{ value }`. The field was there all along.
2. **B5** hard-coded 08:00–17:00 as « before opening », written at 00:00 — the walk ran at 09:21, inside those hours,
   so the lock was correct. Now opens an hour after « now ».

## Observations (off-plan)

- **Corrigé + Déclaré on one line.** A line whose arrival an admin added (« Ajouter un pointage », so `Corrigé` with
  its motif) and whose departure she then declared shows both chips — true, readable. But the CSV « État » picks
  `Corrigé` first (`AttendanceRules.StateLabel`), so the export drops « Déclaré » for that line.
- **Lock on a non-borne device:** a second decorative icon (`MonitorSmartphone`) sits under « Borne : Accueil » — it
  adds nothing to the sentence above it.
- **Lock on a borne:** two headlines stacked — « Pointez votre arrivée » and the flow's « Ines Gharbi / Arrivée ».
- **Sign-out prompt with the pad at 1536×730:** the dialog body scrolls; the flow's own « Annuler » is below the fold
  inside it, while the pinned footer holds only the quiet « Se déconnecter sans pointer » (the ✕ still closes it).

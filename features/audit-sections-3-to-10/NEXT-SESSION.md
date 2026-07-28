# Next session — resume prompt

Copy the fenced block below into a fresh Claude Code session.

---

```
/implement-story features/audit-sections-3-to-10/

Continue P2 from step 4. Read stories/progress.md FIRST — it has the resume
state, the corrected baselines, and DEV-1's resolution. Steps 1-3 are done,
nothing is committed yet, and the suite is fully green.

CORRECTED BASELINES (spec.md and plan.md record stale ones — trust progress.md):
- Backend warnings: 56, not 58. All pre-existing CS8618 in Domain.
- Suite: 1105 passed / 0 failed, not 941/3. Section 1's merge fixed the three
  ReminderSchedulerTests. The suite is FULLY GREEN — any red is ours and is a
  real failure, not a pre-existing baseline.

REMAINING P2 STEPS (plan.md "Part P2" has the detail — don't re-derive it):
 4. Delete buttons for fiche de soins + medical document behind AlertDialog;
    role-gate BOTH endpoints (A-12 — neither has a policy today, so a
    secretary can delete an ordonnance); fix DeleteMedicalDocumentCommand.cs's
    leak (A-8 — it returns $"Error deleting medical document: {ex.Message}",
    English AND a raw exception).
    Rule 1 is already satisfied: the un-mark (step 1) and the soft-link
    cleanup (step 3) are in, so the delete button is now safe to add.
 5. Amend a devis — wire treatmentPlansApi.amend (zero callers today).
 6. Revise the echeancier — wire reviseInstallments (zero callers today).
 7. Role change — validate the closed set, do NOT null email/fullName
    (User.Update defaults both to null), self-lockout guard, bump TokenVersion.
 8. Un-gate user management in Cloud — delete `mode === "local" &&` from
    dashboard-sidebar.tsx:86. One token; the page itself never checked mode.
 9. Edit another practitioner — no client function exists for PUT
    /api/doctors/{id} at all, which is why the endpoint is unreachable.
10. Google Calendar disconnect — new AdminOnly endpoint calling the existing
    uncalled Clinic.ClearGoogleCalendarConnection().
11. Colour palette from GET /api/procedure-types/colors — it returns bare
    hexes, so keep the French labels client-side (A-14).
12. Lab-order transition rules — LabWorkOrder.SetStatus is a bare assignment.
13. Specialties in French — display-time map, English storage keys retained
    (setup-wizard.tsx:33-40's weekdayLabelsFr is the precedent).

WATCH OUT:
- TreatmentPlansControllerAuthorizationTests has a drift guard that fails the
  build on any unclassified new action. Classify as you add.
- AdminSurfaceCoverageTests is a HARDCODED array — a new admin surface is
  silently ungated. Add it by hand.
- Step 13 reaches document-editor-content.tsx (2527 lines, ~20 documentType
  switches) via the printed certificat and signature block. Change the display
  map only; do not touch the switches. Plan risk R-11.
- dotnet test is Smart-App-Control-blocked on this machine. Use:
    dotnet build ClinicManagement.UnitTests/ClinicManagement.UnitTests.csproj -p:OutDir=<scratch>/utbuild/
    dotnet vstest <scratch>/utbuild/ClinicManagement.UnitTests.dll
  Never pass --no-build after changing production code.
- Steps 5, 6, 9, 11, 13 are frontend-heavy and there is no frontend test
  runner — the gate is `npx tsc --noEmit` + `npm run build` (expect 27/27
  pages) + a documented manual walk.

Commit P2 as one commit at the part boundary once all 13 steps are done.
Nothing is committed yet, including the feature artifacts.
```

---

## Two alternatives, if you'd rather not do all ten steps at once

**Backend only — everything behind an automated gate.** Replace the first line of the
step list with:

```
Do only P2 steps 4, 7, 10 and 12 — the backend ones. Leave 5, 6, 8, 9, 11, 13
for a following session. Reason: these four have real Moq gates; the others are
frontend with no test runner.
```

**Commit the artifacts first.** Before any more code:

```
Commit the feature artifacts for features/audit-sections-3-to-10/ as a
docs-only commit — spec.md, plan.md, design.md, exploration.md, mockups/,
stories/, plus follow-up/patient-merge.md, follow-up/mockups/ and the
follow-up/README.md row. Stage explicitly by path, never `git add -A`.
```

> Worth doing. An earlier merge on `feature/windows-desktop-app` used `git add -A` and swept this feature's
> `exploration.md` into an unrelated commit called `"claude files"` (`b0c472e`).

---

## State at the end of the last session

**Branch:** `feature/audit-sections-3-to-10`, off `feature/windows-desktop-app` @ `1932acf` (contains both the
merged audit § 1 and § 2 work). **Nothing committed.**

| Artifact | Status |
|---|---|
| `spec.md` | APPROVED — 301 ACs, 57/57 bullets traced |
| `plan.md` | APPROVED — one story, 8 parts, 18 risks, 15 migrations |
| `design.md` | APPROVED — 2 mockups (merge's moved to `follow-up/`) |
| `stories/progress.md` | P1–P8 resume table, DEV-1 resolved, baselines corrected |

**Closed so far: 3 / 57 bullets** — § 5.3 (no un-do for a réalisé act), § 6.11 (deleting a fiche orphans its soft
links), plus adjacent defects **A-13** (mark-done had no role policy) and **A-9** (English clinic-resolution leak).

**Gates at the end of the session:** backend build 0 errors / 56 warnings / 0 in changed files · full unit suite
**1105 passed, 0 failed** (+15 new tests).

**P8 is still blocked** on Q-1 … Q-6 (see `plan.md` § Part P8). Q-3 is decisive: `follow-up/cnam-conventionné-bordereau.md`
already records the bordereau as a conventionné / tiers-payant pathway, so if this clinic is filière privée, P8 has
no user and should follow patient merge into `follow-up/`.

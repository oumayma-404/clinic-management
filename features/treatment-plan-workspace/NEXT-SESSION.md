# Resuming treatment-plan-workspace in a fresh session

Hand a fresh Claude Code session the prompt in **[§2](#2-the-prompt)**. Everything else here is context for
whoever is reading.

> **Why this matters:** a fresh session opens in the **main checkout**
> (`C:\Users\Oumayma Benkhalifa\Desktop\clinic-management`), which is on `feature/windows-desktop-app` and is
> where the user is actively working. All of this feature's work lives in a **separate worktree**. Without the
> first line of the prompt, a session will confidently edit the wrong tree.

---

## 1. Where things stand

| | |
|---|---|
| **Worktree** | `.claude/worktrees/treatment-plan-workspace` |
| **Branch** | `feature/treatment-plan-workspace` (local only — never pushed) |
| **Branched from** | `f510f93` — a baseline commit of the user's then-uncommitted in-flight work |
| **Committed in the worktree** | **Nothing.** The user commits manually. |
| **Backend build** | ✅ 0 errors, 0 warnings in changed files |
| **Backend unit suite** | ✅ 668 passed / 8 failed — all 8 verified pre-existing on the baseline (see §4) |
| **Frontend typecheck** | ✅ for every file this feature touched (6 unrelated pre-existing errors remain — see §4) |

**Slice A is done** (backend + frontend, 19 files): the derived read-back, the four états, `PatientPlanCard`
above the patient-page tabs, duplicate booking made impossible, and three real defects fixed — a `Completed`
plan can now be paid (`EnsurePayable`), auto-close fires on both completion paths, and the pre-existing
patient-name N+1 / timezone-naive date filter / untranslated back-button are cleaned up.

**Session 2 is done** (11 files): **AC-8** (`InvoiceDto.treatmentPlanId` + the invoices-table « Devis » badge,
deep-linking to `?plan=` until slice C's route exists), **AC-9a** (the patient page now watches
`TreatmentPlans` + `Invoices`, so `PatientPlanCard` can't go stale), and **slice A2** — one shared
`Domain/Services/PlanBillingRules.cs` now drives « Solde patient », « Créances » and the dashboard, and both
`ITreatmentPlanRepository` installment aggregates exclude `Draft` plans. Two recorded deviations: **DEV-4**
(the helper lives in Domain, not Application) and **DEV-5** (the caisse deliberately gets the Draft exclusion
but *not* the billed-plan de-dup — it would delete real cash). Read both before touching the money reads.

**Session 3 is done** (6 new test classes + 1 extended, **64 new tests**, all green): the plan area went from
**zero** coverage to pinning every slice-A/A2 rule — the derivation table incl. the cancelled/no-show
un-schedule (AC-1/2/3/3a), `EnsurePayable` + unified auto-close (AC-10/AC-11), the batched-read contract
(AC-6), three-way money-read agreement (AC-12a/b/c), the plan area's **first** tenant-isolation guard
(AC-24), `CancelPlan` pinned to `AdminOrDoctor` (AC-24a — nothing pinned it before), and the
`treatmentplans` realtime key (AC-25). **2 of the spec's 8 classes could not be written** — they name code
slices B and C haven't built yet; see §3.

Read `spec.md` (APPROVED, Challenged, Type: Full) and `progress.md` (file-by-file state, per-AC table, three
auto-approved deviations, DEV-1/2/3) before writing code. They are the source of truth; this file is only a
launcher.

---

## 2. The prompt

```
Continue the treatment-plan-workspace feature.

WORK ONLY IN THIS WORKTREE — never edit or commit in the main checkout:
  .claude/worktrees/treatment-plan-workspace   (branch feature/treatment-plan-workspace)
The main checkout is on feature/windows-desktop-app and the user is actively
working there. Do not touch it, do not switch branches, do not commit anything
anywhere (the user commits manually).

Read these two first, they are the source of truth:
  features/treatment-plan-workspace/spec.md       (APPROVED, Challenged, Type: Full)
  features/treatment-plan-workspace/progress.md   (what's done, per-AC table, DEV-1/2/3)

Slice A, AC-8, AC-9a, slice A2 and the test suite are all done and green. Do
slice C next — the /treatment-plans/[id] workspace route (~9 files):

  - New "use client" route app/treatment-plans/[id]/page.tsx using useParams(),
    the standard shell (ClinicGuard → DashboardSidebar → DashboardHeader → main),
    with header / actes / échéancier / parcours sections. Loading + not-found +
    error follow app/patients/[id]/page.tsx:326-362 (a French state-driven card
    OUTSIDE ClinicGuard) — there is no notFound() and no not-found.tsx in web/.
  - Retire the plans-table "Gérer" dialog and the 8 unlabelled ghost icons; a row
    links to the workspace and keeps a small LABELLED dropdown.
  - Wire UpdateAppointmentCommand's treatmentPlanItemId through the existing
    AppointmentPlanLink — this finally gives the dead Appointment
    .SetTreatmentPlanItem a caller (AC-17).
  - Repoint the invoices-table « Devis » badge from /treatment-plans?plan={id} to
    /treatment-plans/{id} and drop the highlightPlanId plumbing in
    app/treatment-plans/page.tsx + treatment-plans-table.tsx.

Tests are NOT optional for this slice (DEV-2). Slice C must also add:
  - Features/Appointments/ — the update-path plan link: move, clear, and
    cross-tenant rejection (AC-17). This is one of the spec's 8 classes and is
    the ONLY reason it isn't written yet.
  - The reorder endpoint's rows in Api/TreatmentPlansControllerAuthorizationTests
    (it has a drift guard that FAILS on any unclassified new action — that is
    deliberate).

Do NOT touch web/components/patient-record-modal.tsx — the user is mid-refactor
on it (see DEV-3 in progress.md). AC-9 stays skipped.

Conventions (the repo wins over any skill's examples): Result<T>.Failure with
French messages, no ROP/Railway, no IBaseRepository, zero FluentValidation
validators, repos ctor-inject ApplicationDbContext and only stage (IUnitOfWork
commits), public ctors not static factories, DI in Infrastructure/Extensions.cs.

Environment gotchas, all previously hit:
  - Build the leaf project, not the solution:
    dotnet build ClinicManagement.Infrastructure/ClinicManagement.Infrastructure.csproj
    Baseline warning family is CS8618 (pre-existing, Domain value objects).
    Scope warnings to your changed files; anything there is yours.
  - npx tsc --noEmit already reports 6 PRE-EXISTING errors in
    patient-record-modal.tsx (ToothPaint.focused, DentalActInput.isPerTooth) from
    the user's in-flight refactor. Verify any new error is actually yours by
    stashing and re-running. npm run build cannot pass until that refactor lands.
  - ESLint is not installed; the FE gate is tsc --noEmit + npm run build only.
  - Smart App Control blocks dotnet test (0x800711C7). The workaround WORKS and
    was used successfully in session 2:
      dotnet build <test>.csproj -v quiet --nologo -p:OutDir=<scratch>/utbuild/
      dotnet vstest <scratch>/utbuild/ClinicManagement.UnitTests.dll
    Expect 8 failures you did not cause (Doctors/Reminders/Documents — see §4).
  - Deep links: use window.location.search in a mount effect, never
    useSearchParams — it forces the page out of static prerendering. Recorded at
    app/patients/page.tsx:27 and app/documents/page.tsx:60.
  - dotnet ef has failed here before (WDAC + held DLLs); hand-author the
    migration + Designer + snapshot if it does, and say so in progress.md.

Update progress.md as you go. Tests ARE in scope for this feature (DEV-2).
```

**Do not invoke `/implement-small-feature`.** It fails its own Step-0 prerequisite check — the spec is
`Type: Full`, and the only reason it ran before is that the user explicitly forced the scope after being shown
the ~60-file surface. A plain prompt avoids re-litigating that every session.

---

## 3. Swapping in a different chunk

Replace steps 1–3 above with **one** of these. One per session — each is genuinely a feature, not a task.

| Chunk | Size | Notes |
|---|---|---|
| **Slice B — sequencing + amendable plan + migration** | ~17 files + 3 migration files | Changes domain invariants on a numbered financial document. Highest risk. The slice-A/A2 rules it could regress are **now pinned**, so it is safe to start — but it must also write `AmendTreatmentPlanCommandHandlerTests` (the 8th class, blocked until the command exists) and extend `Domain/TreatmentPlanTests` (AC-18–AC-23), `TreatmentPlanTenantIsolationTests` (amend / revise-installments / reorder) and `TreatmentPlansControllerAuthorizationTests` (whose drift guard **fails** on an unclassified action — by design). |
| **Docs pass** | 7 `CLAUDE.md` files | Listed at the bottom of `spec.md`. Includes correcting the stale "seedable from the odontogram" claim and the `api/ClinicManagement.API/CLAUDE.md:3` `commitASP.NET` paste typo. |

---

## 4. Known-not-broken (don't "fix" these)

- **6 `tsc` errors in `web/components/patient-record-modal.tsx`** (`ToothPaint.focused`,
  `DentalActInput.isPerTooth`). Pre-existing — *verified* by stashing all feature changes and reproducing them
  identically on the pristine `f510f93` tree. The baseline commit captured the user's tooth-first refactor
  mid-change, so the shared types moved ahead of that component. They clear when that refactor lands on the
  base branch. **Do not edit the file to chase them** (DEV-3).
- **`CS8618` warnings across Domain** value objects and entities — the repo's long-standing baseline family.
- **`npm run build` failing** — same cause as the tsc errors; carries no signal about this feature yet.
- **8 failing unit tests** — `Features/Doctors/DoctorCachetTests` ×4,
  `Infrastructure/Services/ReminderSchedulerTests` ×3, and
  `Features/Documents/DocumentTypeAndFilenameTests.Create_With_Supported_Type_Passes_The_Type_Guard`.
  Pre-existing — *verified* in session 2 by stashing every feature change, rebuilding the pristine `f510f93`
  tree to a separate scratch dir and reproducing the identical 8 (587 passed / 595 there vs 604 / 612 here;
  the 17-test delta is exactly session 2's own additions). They arrived with the baseline commit of the
  user's in-flight work and belong to **their** branch, not this feature. Consequence: **AC-26's "the full
  unit suite passes" cannot be signed off from this branch** until they are fixed upstream.

## 5. Deliberately out of scope (recorded in `spec.md`, not oversights)

Collected installment money surviving the devis→facture bridge (a real pre-existing money bug: 800/1000 DT paid
via échéances vanishes once bridged and issued); `GetDashboardStatsQuery` not netting `CreditNote` refunds;
wider patient-page localization (three raw enum names still rendering as user-facing text, `formatFileSize`
using `B`/`KB`/`MB`); séances / visit grouping; per-act invoicing; any audit/actor field on financial documents.

## 6. Still open for the user

The branch was requested as **remote** but exists locally only. Pushing publishes the 131-file baseline commit
to GitHub, and the recorded `gh` account mismatch means it needs the Windows Credential Manager workaround —
left for the user to confirm rather than done silently.

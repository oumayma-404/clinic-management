# Session context — tooth-first record entry

**Updated:** 2026-07-26 · **Branch:** `feature/windows-desktop-app` (6 commits ahead of `origin`)

## Status: the UX is resolved and implemented

An earlier session designed five interaction models and had all five rejected (two-pane composer, per-tooth
popover, palette d'actes, odontogramme direct, confirmer le prévu). **That is no longer the state.** The design
was settled on 2026-07-26 by picking « confirmer le prévu » and fixing what had actually made it fail review,
and it shipped in **`c355514`**.

| Decision | Approved as |
|---|---|
| Density | **Confirm-first.** One ~780px column, not two panes. Detail folds into sections whose headers show a live summary of their own contents; a section holding a value auto-opens |
| Catalogue | **One dense searchable column.** Not a tile grid, not an icon-only magnifier popover. Category headings in the seed's clinical order, hidden while searching |
| Prefill | **Propose, never commit.** The appointment's `procedureTypeId` fills the *draft* only |

Mockup of record: `mockups/02-confirmer-le-prevu.html`
(published: https://claude.ai/code/artifact/d6468f5f-9cb5-4c63-a84d-2dfe0e69b96b) — three scenarios, a
19 ⇄ 42-act catalogue toggle to stress the presentation, and a table mapping all twelve old form fields to
where they now live. `mockups/01-interaction-models.html` is kept as the record of what was ruled out.

Two claims the earlier session used to reject « confirmer le prévu » turned out to be weaker than written, which
is why it was revived: the patient page **already loads the patient's appointments**, so the patient file *can*
propose today's appointment; and a generic « Consultation » slot is not a mis-billing risk because a proposal is
a draft — no `DentalRecordAct` exists until the dentist confirms.

## Where the code stands

```
c355514  feat(dental-records): confirm-first record entry with a dense act catalogue   ← local only
4263250  fix(ai-chat): gate the assistant on a session, collapse by default, greet on login
06d205f  docs(tooth-first-record-entry): add the interaction-model mockup
9b73547  fix(dental-records): widen the record modal so the adult arch cannot clip
fb8484e  fix(i18n): translate the last user-facing English strings to French
3d7b98b  test(dental-records): pin the tooth-first contract; regenerate the migration with EF
```

Working tree is clean apart from this file.

### Frontend — the shipped shape
- **`record/act-slot.tsx`** — one slot, two states: proposal card ↔ catalogue. Derived from `hasDraft`, not an
  effect, so confirming an act brings the list back on its own.
- **`record/act-catalog-picker.tsx`** — the dense list. Accent-insensitive filter, ↑↓/↵/esc, free-text row.
  Renders **inline, never in a Popover** — which also deletes the spec's own critical edge case, where Radix
  `Dialog`, `Popover` and `Command` all had a claim on `Entrée`.
- **`record/act-detail-fields.tsx`** — tarif, `/dent ↔ forfait`, état résultant, faces, note.
- **`record/record-section.tsx`** — collapsible + live-summary header.
- **`record/use-session-acts.ts`** — gained `applyAppointment`, `useFreeText`, `draftTotal`/`grandTotal`, and
  `perToothLocked`.
- **`record/session-acts-list.tsx`** and **`record-tooth-chart.tsx`** are unchanged — both already did what the
  design needed.
- **`record/session-act-composer.tsx`** was **deleted**; its job split between the slot and the detail fields.

### Pricing defect fixed on the way
`pickProcedure` derived `perTooth` from `selection.length > 0` and `toggleTooth` only ever *cleared* it. That
worked when teeth came first, but confirm-first arms the act *before* any tooth — so a per-tooth act stayed a
forfait and **two teeth billed as one**. `derivePerTooth` now re-derives on every selection change until the
dentist touches the switch (`perToothLocked`), which keeps AC-9 intact.

### Backend (S1) — done, tested, unaffected by any UX change
`DentalRecordAct.UnitCost`/`IsPerTooth`, `DentalRecordActInput`, mixed dentition via `FdiTooth.IsValid`,
`numeric(18,3)` widening, the per-tooth invoice bridge. **52 new unit tests + 149 in the regression slice.**

## Outstanding, concrete

1. **The layout is NOT verified.** `tsc --noEmit` and `npm run build` are both clean, but neither can see
   layout — the documented cause of two earlier failures. This environment has **no browser automation**
   (no `agent-browser`, no Playwright, no browser MCP), so it could not be checked here. Check on a patient with
   an appointment today: proposed act → tap 16 → total is the tarif not 0,000; tap 16 **and** 26 → total doubles
   (that is the defect above); « Ce n'est pas cet acte » swaps in place with no window-over-window; collapsed
   « Détails » reads back état · tarif · faces; an existing record opens with Actes + Notes expanded; 1366×768
   has no horizontal page scroll.
2. **The dentist's own account is still uncaptured** — the one thing the earlier session was right to insist on.
   This design settles *presentation*, not his workflow, and his other complaints ("lots of bugs, hard to use
   features") were never written down. Get that before designing anything further here.
3. **Migration never applied to any database.** `20260726204124_ToothFirstRecordPricing` is tool-generated and
   compiles; `dotnet ef database update` has not been run. It applies on next API boot via `Database.Migrate()`.
4. **`features/treatment-plan-workspace/spec.md`** (APPROVED, Type Full, unimplemented, same branch) needs
   rebasing onto the new layout — its AC-9 plan-item preselect still works (the `planItems` prop and its prefill
   path are intact) but the surface it targets has moved. Nine `adoption-qa-*` specs are also APPROVED and
   pending on this branch.
5. **`ClinicManagementApi` service is `StartType = Automatic`.** To keep it down:
   `Set-Service -Name ClinicManagementApi -StartupType Disabled` in an **admin** shell.

## Traps that cost real time — don't relearn them

- **`tsc --noEmit` + `next build` cannot see layout.** Both passed on a modal that was visually unusable. Any
  visual acceptance criterion needs the app rendered and looked at.
- **shadcn `DialogContent` ends its base class with `sm:max-w-lg`** (`components/ui/dialog.tsx:63`). An
  unprefixed `max-w-*` loses to it at every viewport ≥640px — tailwind-merge treats them as different groups.
  Width overrides must be `sm:max-w-*`. (Fixed in `9b73547`; the current column keeps the pattern at 780px.)
- **Never edit these files with PowerShell `Get-Content`/`Set-Content`.** It reads UTF-8 as ANSI and writes back
  double-encoded, mojibaking every French accent (`Prothèse` → `ProthÃ¨se`). Use the editing tools.
- **A running `next dev` serves 500s after a file is deleted under it.** Not a code fault — restart it.
- **An `position:absolute; inset:0` overlay collapses its container.** Cost a broken mockup render.
- **Smart App Control blocks `dotnet test`** (`0x800711C7`). Working recipe:
  `dotnet build <TestProj> -p:OutDir=<scratch>/utbuild/` then
  `dotnet vstest <scratch>/utbuild/<dll> --TestCaseFilter:...`
- **`dotnet ef` needs the API stopped**, and **never pass `--no-build`** — it loads a stale DLL from the API's
  bin and silently produces an *empty* migration. Use `migrations remove` to revert the snapshot.
- **EF emits `numeric(18,3)`**, not `decimal(18,3)`, in migration bodies.
- **`ProcedureType` has no `Category` column** — the seed passes the category into the ctor's `description`
  parameter, so `pt.description` de-facto holds it. Display hint only; a clinic-authored procedure may hold
  anything and falls into « Autres ». Never use it as the per-tooth signal (that is `resultingCondition`).
- **The seeded catalogue is 19 rows across 12 categories** (`feef4d8` cut it from 43). Beware the stale 42-row
  copies under `.claude/worktrees/*` — reading one by mistake wasted time this session. Because headings nearly
  outnumber rows at that size, the picker hides them while searching.
- **`GetOdontogramQuery` returns the edited record's own `ToothState` rows.** Any "prior state" overlay must
  filter `dentalRecordId !== record.id` or it double-counts the session's own output.
- **Never infer per-tooth pricing when loading a saved act** — read `UnitCost`/`IsPerTooth`. Dividing `cost` by
  the tooth count would double the price of any act whose total happens to divide evenly.
- **`DentalRecordPostVisitCompletionTests.cs.deferred`** is stale three ways against current code. It belongs to
  `post-visit-review-patient-record`'s debt, not this feature's.

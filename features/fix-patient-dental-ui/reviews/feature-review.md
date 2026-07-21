# Feature Review: fix-patient-dental-ui

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-21
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** 9798b95 (reference); reviewed commit cb49522 (7-fix batch, scoped)
**Review method:** 5 parallel agents (code-quality, error-handling, business-logic, breaking-change, frontend) adapted to MediatR/`Result<T>` + a dedicated FE agent.

## Findings

### Finding 1
- **Severity:** Critical
- **Category:** Frontend / Data Integrity
- **File:** web/components/patient-record-modal.tsx
- **Line:** 129 (population effect) → 138-144 (auto-fill effect)
- **Anchor:** `PatientRecordModal` — population `useEffect` (deps now include `procedureTypes`) + auto-fill-cost `useEffect`
- **Comment:** The #14 fix (adding `procedureTypes` to the population effect's deps) makes that effect re-run when procedure types finish loading, reclassifying an edited record from "Custom" to its real type. That reclassification triggers the auto-fill-cost effect, which calls `setCost(String(selectedProcedure.defaultCost))` **unconditionally** (its own comment claims "only set if cost is empty", but there is no such guard). Result: editing an existing dental record whose procedure is a defined type and whose stored cost differs from the type's default silently **overwrites the stored cost with the default on save** — corruption of a financial record. Before #14 the record stayed "Custom", so auto-fill never fired. Fix: (a) gate the auto-fill effect so it only sets cost when `cost` is empty (honor the comment); and (b) do the known-vs-custom reclassification in a separate, narrowly-scoped effect that only sets `procedureType`/`customProcedure` (not cost/notes/teeth) so the full form is not reset while procedure types load.

### Finding 2
- **Severity:** Minor
- **Category:** Frontend
- **File:** web/components/patient-files-manager.tsx
- **Line:** 104
- **Anchor:** `PatientFilesManager` — `defaultsInitializedFor` ref-guarded seed `useEffect`
- **Comment:** The ref is only armed when the seed actually runs (first load returns 0 folders). For a returning patient who already has folders, the ref stays `null`; if the user later deletes every folder, the seed fires and re-creates the 4 default folders — contradicting the comment's "not again if the user later deletes every folder". Arm the ref unconditionally once the first load for a patient resolves (regardless of whether seeding was needed).

### Finding 3
- **Severity:** Minor
- **Category:** Frontend
- **File:** web/components/patients-table.tsx
- **Line:** 34
- **Anchor:** `PatientsTable` — "Load patients from API" `useEffect` (deps `[searchQuery]`)
- **Comment:** Search is now server-side, but each keystroke fires `patientsApi.list({ searchTerm })` with no request sequencing/abort, so responses can resolve out of order and `setPatients` may show results for a stale term. Add an ignore-flag in the effect cleanup (`let ignore=false; … if(!ignore) setPatients(...); return () => { ignore = true }`); optionally debounce.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| Major | 0 |
| Minor | 2 |
| Suggestion | 0 |
| **Total** | 3 |

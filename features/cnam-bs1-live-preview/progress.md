# Progress: CNAM BS1 Live Preview

**Started:** 2026-07-20
**Type:** Small
**Branch:** feature/windows-desktop-app

## Status
- [x] Implementation
- [x] Quality checks (build, lint, typecheck)
- [x] Tests (added/modified — see Test Plan below)

## Quality checks
- `npx tsc --noEmit` (real type gate) → exit 0, clean.
- `npm run build` (`next build`, fresh `.next`) → exit 0; `/documents/[type]` compiled.
- ESLint not installed in this repo (`eslint .` script present but no binary; lint disabled
  during `next build`) — known repo condition, so tsc + build are the FE gate, per skill guidance.

## Working tree note (start of session)
- `.claude/worktrees/` — untracked, unrelated tooling; excluded from commits.
- `features/cnam-bs1-live-preview/` — this feature's own folder.
- On `feature/windows-desktop-app` (not `main`): all CNAM BS1 work (renderer, editor)
  lives only on this branch, so it is the correct base. Using current branch.

## Files Changed
- `web/components/document-editor-content.tsx` — BS1 live-preview state + debounced
  regeneration effect + unmount URL revoke; right preview pane branched for
  `bulletin-cnam` to embed the real generated PDF.

## Auto-Approved Deviations
| Deviation | Reason |
|-----------|--------|
| Removed the now-unreachable inner `{documentType === "bulletin-cnam" && (...)}` BS1 table block from the generic letterhead `Card`. | Internal/dead-code removal; that block sat inside the `documentType !== "bulletin-cnam"` branch so it never rendered. Output for the other four types is byte-for-byte unchanged (AC-6). |

## Significant Deviations
_None._

## Test Plan
Frontend-only feature (Scope: FE). Single file changed:
`web/components/document-editor-content.tsx`. No backend surface (server-side unchanged per spec).

**The `web/` project has no FE test framework** — no vitest/jest/testing-library, no `test`
script, and zero `*.test.*`/`*.spec.*` files in the tree. Per the test-small-feature rule for
FE-only changes with no FE test harness, each AC is **accounted for as a coverage note** (covered
by the type/build gate), not a contrived test. No test files written.

| AC | Behavior | Coverage |
|----|----------|----------|
| AC-1 | BS1 PDF embedded in preview, matches Download output | Reuses the exact `medicalDocumentsApi.generatePdfForDownload(buildDocumentData())` call the Download button uses → same bytes by construction. Covered by `tsc --noEmit` + `next build`; visual match is manual/operator. |
| AC-2 | Auto-refresh ~800ms after edits pause | Debounced `useEffect` keyed on serialized `buildDocumentData()`. Covered by type/build gate; timing is runtime/manual. |
| AC-3 | Latest input wins; prior object URLs revoked | `cancelled` flag drops superseded responses; each new URL revokes the previous via `bs1UrlRef`. Type/build gate; runtime/manual. |
| AC-4 | Loading + French error states, no crash | Loading overlay + error banner/state in the branch; error keeps last good preview. Type/build gate; runtime/manual. |
| AC-5 | No patient → neutral state, no API call | Effect short-circuits when `buildDocumentData()` is null (no patient). Type/build gate; runtime/manual. |
| AC-6 | Other four types byte-for-byte unchanged | Generic letterhead `Card` wrapped unchanged in the `documentType !== "bulletin-cnam"` branch. Covered by `tsc --noEmit` + `next build` (both green). |

**Coverage notes:**
- No unit/integration test surface exists in this repo for React components; the standing FE gate
  (`tsc --noEmit`, `next build`) is the automated coverage, and it passes.
- The visual/interaction ACs (PDF renders correctly, debounce timing, loading/error UX) are
  inherently manual/operator checks — no headless harness in the repo to assert them.

## Tests Run
| Suite | Filter | Result |
|-------|--------|--------|
| Type gate | `tsc --noEmit` (web/) | exit 0 — clean |
| Build gate | `next build` (web/, run at implementation) | exit 0 — `/documents/[type]` compiled |

_No xUnit/integration/Newman/E2E: FE-only feature, no backend surface, no FE test runner in repo._

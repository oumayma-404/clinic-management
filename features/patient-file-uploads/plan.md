# Implementation Plan: Patient File Uploads

**Status:** APPROVED
**Approved:** 2026-08-07 — the approach was chosen by the user from four challenged options in
`/think-solution` (Option 1, `FileTypePolicy`), together with the format breadth (imaging + photos · dental 3D /
CBCT · Office + text, **not** video) and the large-file degradation (accept and message). Not challenged through
`/challenge-plan`.
**Created:** 2026-08-07
**Spec:** [spec.md](./spec.md) — APPROVED
**Branch:** `feature/audit-sections-3-to-10` (user decision; the tree also carries the uncommitted
`clinic-self-signup` work, which this story must not stage)
**Structure:** **One story**, by explicit user decision, split into **five ordered parts (P1–P5)**.

---

## Overview

One story, US-1, closing all five AC groups. The user chose one story over the four slices proposed in
`/think-solution`; that decision is honoured and recorded as **R-1** rather than re-litigated.

Each part is a **vertical** increment — domain → application → API → web → tests — never a technical-layer
grouping. Each ends at a clean build gate and is a natural commit boundary, so `/implement-story` can land the
story incrementally and resume at a part boundary. The order is dependency-forced: **P1 is independently
shippable and fixes the reported symptom on its own**, P3 consumes P2's catalog, P5 consumes P3's policy
endpoint and P4's update command.

| Part | Covers | Touches | Migration |
|---|---|---|---|
| **P1** | AC-1 — the reported bug | `web/lib/api/{client,patient-files}.ts` | none |
| **P2** | AC-2, AC-3 — the catalog and the six call sites | `Application/Common/Files/`, 6 handlers, controller limits, tests | none |
| **P3** | AC-5.1 — the derived policy endpoint and client pre-check | `Features/Meta`, `MetaController`, `web/lib/api/upload-policy.ts` | none |
| **P4** | AC-4 — rename, describe, move | `Domain/Entities/PatientFile`, 2 commands, controller PUTs, rename dialog | none |
| **P5** | AC-5.2–5.10 — the manager UX and its rule violations | `web/components/patients/files/`, the two preview copies | none |

**The whole story is migration-free** (see spec § Out of scope). `verify-schema` is therefore genuinely not
applicable — and the verb does exist and runs, which was confirmed rather than assumed.

## Story shape

### R-1 — One story, five parts
The user asked for one story. The risk of a single story is an unreviewable commit; the mitigation is the part
structure above, each with its own build gate and commit. Not a mitigation to skip: P2 alone rewires six
handlers and deletes a shared file.

### R-2 — The working tree is not clean
`feature/audit-sections-3-to-10` carries 33 uncommitted files implementing `clinic-self-signup` (new entity,
two commands, an SMTP sender, `web/app/signup/`, a migration, a modified model snapshot). **Stage by explicit
path; never `git add -A`.** A pre-existing red in those files is not this story's. Recorded in `progress.md`
under the working-tree note.

### R-3 — `apiGetBlob` must not duplicate error handling
P1 needs a blob GET with `handleResponse`'s error branch but `response.blob()` as the body reader.
`handleResponse` currently both throws and reads, so the error branch is extracted into `throwIfNotOk` and
`handleRequest` takes an optional body reader. Copying the error branch would create a second place the
`{ error }` contract is interpreted — the exact defect P1 exists to fix.

### R-4 — AC-2.7's guard must be derived, not listed
A guard listing today's six upload sites cannot fail on the seventh, which is the only case it exists for. It is
written as a **source scan** (`no magic-byte literal outside `Common/Files/``), following
`RealtimeResourceResolverTests`, which already parses a `.ts` file from a test. **Prove it fails** on a
deliberate violation in a throwaway file before trusting a green run.

### R-5 — Widening the caps has three homes
The catalog entry, the action's `[RequestSizeLimit]`/`[RequestFormLimits]`, and the shell refusal message. Miss
the attribute and 150 MB is silently unreachable behind a framework 413. Verify `MinioFileStorage.UploadAsync`
accepts a 150 MB stream (`Minio` 5.0 wants a known object size); `IFormFile.Length` is computed by ASP.NET from
the parsed body, so it is a trustworthy size hint *and* lets an oversized upload be refused before a byte is
read — the actual stored length is still measured by a counting stream, never taken from the client.

### R-6 — Any frontend part holds the device contract in the same part
P1 and P3 are logic-only; P4 and P5 add UI and are responsible for their own 320 px / `coarse:` / sheet
behaviour. P5 additionally fixes the § violations already present in the file it rewrites
(ungated `grid-cols-2`, adjacent 32 px buttons, raw `toast.error`, missing `EmptyState`) — the file is open, and
leaving them is what turns a one-file change into a remediation feature.

## Gates

| Gate | Command | Applies |
|---|---|---|
| Backend build | `cd api && dotnet build --no-incremental` | P2, P3, P4 |
| Backend tests | `dotnet test --filter` on the new classes | P2, P4 |
| Frontend types | `cd web && npx tsc --noEmit` | P1, P3, P4, P5 |
| Frontend device check | `cd web && npm run check:responsive` | P1, P3, P4, P5 |
| Frontend build | `cd web && npm run build` | P1, P3, P4, P5 |
| Eye pass | 320 / 390 / 820 / 1180 / 1440 + landscape phone + keyboard | P4, P5 |
| `verify-schema` | — | **not applicable: no migration.** The verb exists (`Program.cs` console verbs) |

⚠️ `npm run lint` cannot run — `eslint` is in the script but not in `devDependencies`, and `next.config.ts` sets
`eslint.ignoreDuringBuilds`. `tsc` + `check:responsive` + `build` + the eye pass is the whole frontend gate.
⚠️ A backend build failing **only** with MSB3021/MSB3027 is a file lock from a running API, not a compile error.

# Stories — Security Hardening (Audit Section 2)

**Spec:** [../spec.md](../spec.md) — APPROVED, Challenged: Yes
**Plan:** [../plan.md](../plan.md) — APPROVED, Challenged: Yes
**Branch:** `feature/security-hardening` (off `feature/windows-desktop-app`, matching the PR #13 pattern)

## Structure — one story, five ordered parts

The user chose **one story** deliberately (twice). It is not split into separate story files. Instead the single story is worked through in **five ordered parts**, each a vertical increment ending in a committable state. A part boundary is the commit point and the split point if a session runs long — this is the mitigation for plan risk **R-1**.

| Part | Covers (spec stories) | Findings | Verifiable by | Status |
|---|---|---|---|---|
| **P1** Installer filesystem posture | US-1, US-2, US-3 | 🔴×4 | Operator only (`packaging/` R-1) | **done** |
| **P2** Backup output posture | US-14 | 🔴 | `dotnet test` + operator | **done** |
| **P3** Auth & session | US-4, US-5 | 🔴 + 🟠 | `dotnet test` + manual | **done** |
| **P4** Authorization | US-6, US-7, US-8, US-9 | 🟠×3 + 🟡 | `dotnet test` | pending |
| **P5** Hygiene | US-10, US-11, US-12, US-13 | 🟠 + 🟡×3 | `dotnet test` + page walk | pending |

Only P2's dependency on P1 is real (it reuses `DirectoryAclHardener`). P3, P4 and P5 are mutually independent.

## Story tracker

| Story | Status |
|---|---|
| [story-1-security-hardening.md](./story-1-security-hardening.md) | in progress (P1) |

## Progress

Session-by-session detail, deviations and learnings: [progress.md](./progress.md).

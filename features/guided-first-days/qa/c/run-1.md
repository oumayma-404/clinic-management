# QA run 1 — Guided first days, part C (colleague invited by e-mail)

**Date:** 2026-11-03
**Cycle:** 1 of at most 3
**Plan:** features/guided-first-days/qa/c/plan.md (24 scenarios) — driver `walk.mjs`, output `walk-run1.log`
**Environment:** isolated stack — API `:5097` (worktree, `FrontendUrl=http://localhost:3097`, `HostedMultiTenant`), web
`next dev :3097`, throwaway `clinic_gfd_c` (`AddStaffInvitations` applied on start), shared mailpit read only.
Before the drive: the page greeted with the first WORD of the name (« Bienvenue, Dr » for « Dr Ross QA ») while the
e-mail greets with the full name — fixed before driving (server `StaffInvitationPreviewDto.FullName`, page, test).
**Peer sessions live:** clinic-management-29 busy (its own ports :5099/:5098/:3098 — untouched), others idle.
**Widths looked at:** 320×640 · 390×844 · 1440×900 · 1536×730 — `Read`: C1-invitation-320, C2-resend-390,
B7-not-sent-1440, A7-login-prefilled
**Status:** RED (1 finding, minor)

## Scenarios

**54 checks ✅, 0 ❌, 1 ⏭** (B10 — needs a second API in the LAN profile or without `FrontendUrl`; held by
`StaffInvitationTests`).

## Findings

| ID | Severity | What | Evidence | Owner |
|----|----------|------|----------|-------|
| F-1 | minor | At 320 px the label « Mot de passe · au moins 12 caractères » splits into two side-by-side columns (« Mot de / passe » beside « · au moins 12 / caractères ») | shots/C1-invitation-320.png | `web/app/invitation/page.tsx` — `Label` is a flex row and the two halves were two shrinking flex items |

## Observations (off-plan)

- The dev server's « N » badge sits over the bottom-left of every screen — dev only.

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F-1 | confirmed | two flex items in a `flex` label shrink side by side instead of wrapping | `web/app/invitation/page.tsx` | this label only |

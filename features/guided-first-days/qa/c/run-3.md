# QA run 3 — Guided first days, part C

**Date:** 2026-11-03
**Cycle:** 3 of at most 3 (after F-2)
**Plan:** the whole plan (25 scenarios with B11), same `walk.mjs` — output `walk-run3.log`, driven > 5 min after run 2
(the anonymous auth limit). One probe fix first: a literal newline in B11's regex (the script did not parse; nothing
ran).
**Environment:** as runs 1–2.
**Widths looked at:** 320 · 390 · 1440 · 1536×730 — `Read`: C1-invitation-320 (F-1 verified: the floor drops to its
own line)
**Status:** GREEN

## Scenarios

**55 ✅, 0 ❌, 1 ⏭** — B10 (own-server profile / no `FrontendUrl`: needs a second API; held by `StaffInvitationTests`).
B1 and B5 pass again (run 2's red was the limiter). B11 passes: a 429 shows the server's sentence and « Réessayer ».

## Fix summary (cycles 1–2)

| Finding | Verdict | Root cause | Files |
|---------|---------|------------|-------|
| pre-drive | confirmed | greeting took the first WORD of the name (« Bienvenue, Dr ») while the e-mail uses the full name | `PreviewStaffInvitationQuery.cs` (`FullName`), `StaffInvitationTests.cs`, `web/lib/api/auth.ts`, `web/app/invitation/page.tsx` |
| F-1 | confirmed | two halves of a flex label shrank side by side at 320 | `web/app/invitation/page.tsx` |
| F-2 | confirmed | a refused read always said « vérifiez votre connexion » | `web/app/invitation/page.tsx` |

**Gates after the last edit:** check:responsive 81/81 ✅ · tsc ✅ · dotnet test 6131 ✅ (unfiltered; build verified
before run 1) · `npm run build` ✅ (C2 commit; the later edits are two strings and a class list).

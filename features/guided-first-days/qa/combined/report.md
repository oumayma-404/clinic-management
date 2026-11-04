# QA run report — `feature/guided-first-days-all` (2026-11-04)

**Verdict: GREEN.** No product defect. Stack: API :5097, web :3097, throwaway DB `clinic_gfd_all`.

| Walk | Run | Result | Note |
|---|---|---|---|
| `walk-c.mjs` (invite by e-mail) | 1 | 53 ✅ · 2 ❌ · 1 ⏭ | A8, D3 = probe: « Réinitialiser le mot de passe » is now a row-menu item at 1440 (shared menu since the rescue) |
| `walk-c.mjs` | 2 | 55 ✅ · 0 ❌ · 1 ⏭ | B10 ⏭ needs a LAN-profile API; held by `StaffInvitationTests` |
| `walk-rescue.mjs` (lost second factor) | 1 | aborted | probe: clicked « Créer un compte », which hosted now shows as « Inviter un collègue » |
| `walk-rescue.mjs` | 2 | aborted | environment: the `next dev` left by the closed session had broken pipes (EPIPE, compile workers crashing) — replaced by a standalone production build |
| `walk-rescue.mjs` | 3 | 58 ✅ · 0 ❌ · 0 ⏭ | accounts now created through the invitation link; Sami signs in once so his menu offers « Réinitialiser » |

Probe changes only — no source file changed in this pass.

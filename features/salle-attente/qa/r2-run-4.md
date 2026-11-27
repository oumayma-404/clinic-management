# QA run 4 — salle d'attente round 2 (owner-approved 4th cycle)

The skill stops at three cycles; the owner asked for a fourth to close three gaps left by run 3 (2026-11-26):
the round-1 e2e not re-run, C3 + the flaky A4 cleanup, and « Annuler » / reopening after « Terminé » never clicked.

Plan: `qa/r2-plan.md` (+ G3a, G3b, A4b, C3 at 1440) · driver: `qa/r2-walk.mjs` · environment: API `:5098`
unchanged (no backend change), web `:3098` rebuilt from the tree below (new chunks verified to carry
`basis-32` / `overflow-wrap:anywhere`), DB `clinic_sa_qa`. No peer session on these ports.
Widths looked at: 320, 390, 820, 1440, 1536 × 730; dentist panel 390.

## Fixes applied

| Finding | Verdict | Root cause | Files | Blast radius |
|---|---|---|---|---|
| C3 (run 3) | confirmed | at 320 the other-day row's text column was squeezed to 44 px by the pill beside it, and `break-words` cannot shrink a track below its longest word | `arrival-search.tsx` (`ResultRow`: `flex-wrap` + `basis-32` + `ms-auto` pill + `[overflow-wrap:anywhere]`, all gated on `secondary`) | `ResultRow` has one caller file; only the other-day row passes `secondary` — today's RDV, « Sans RDV » and new-patient rows unchanged |
| A4 cleanup (run 3) | probe | the cleanup click after the 390 → 1440 resize; now `bringToFront` + scroll first, and on failure it names what covers the switch, then resets through the API so nothing cascades | `qa/r2-walk.mjs` (row A4b) | — |
| run-4 first drive | probe | at 23:11 Tunis a « +50 min » test RDV is tomorrow and every today-only rule rightly refuses it — 10 rows red (B1, D2, B6, B7, A6b, A10, A7, B2, B3, A13), all « not today » | `qa/r2-walk.mjs` (`slot()` squeezes future offsets into what is left of today, rounds them up) | — |

## e2e — round 1's HP-21 against the round-2 build

`e2e/specs/waiting-room.spec.ts` (WAIT-01…05, 07, 08) on `:3098` / `:5098`: **7 passed (13.4 s)**. Not edited.

## Scenarios (second drive, run `2p3l`, squeeze ×0.17)

53 ✅ · 0 ❌ · 0 ⏭. New rows:

| ID | Result | Evidence |
|---|---|---|
| G3a | ✅ | desk « Terminer » → RDV 22:16Z · 1 min (prévu 21:55Z); toast « Annuler » → 21:55Z · 30 min, `PlannedStartUtc` null, back « Au fauteuil » |
| G3b | ✅ | « Terminé » → RDV 22:16Z (prévu 21:50Z); « ⋯ » « Remettre au fauteuil » → 21:50Z · 30 min, `PlannedStartUtc` null, status InProgress |
| A4b | ✅ | the switch turns « 2 fauteuils » back off by click |
| C3 | ✅ | 320: name and « RDV sam. 28 nov. 10:00 · Dr Nadia Trabelsi » in full, « Recevoir » wraps under them, nothing clipped (`shots/r2-C3-other-day-row-320.png`); 1440: one line (`shots/r2-C3-other-day-row-1440.png`) |
| A6b, A7 | ✅ | green again (run-3 cascade gone) |

Everything else — A1–A16, B1–B11, C1–C5, D1–D8, PAGE — ✅, as in run 2.

## Gates after the last edit

check:responsive 91/91 ✅ · tsc ✅ · build ✅ · e2e HP-21 7/7 ✅ (API untouched since run 1: unit 7455 ✅ still holds).

## Status

GREEN (53/53, 0 ⏭)

# QA run 1 — « Retirer ce PC » behind an authenticator code (2026-10-14)

**Environment:** isolated — relay e2e cloud `rtb-*` (own Postgres, real PC « PC-CI » paired) published on
127.0.0.1:5098; worktree `next dev -p 3099`; playwright-core Chrome (`channel: chrome`, headless, own context). No
shared port, database or Chrome profile touched. Driver: `qa/walk.mjs`, one launch.

**First drive:** two red rows, both probe bugs, fixed in the script and re-driven on a fresh `rtb` stack (A2 had
retired the first one's PC):
- B1 — the title holds a non-breaking space (`quoteFr`); the regex had plain spaces.
- C1 — the bottom sheet was measured while still sliding in; the walk now waits for animations to end.

| ID | Result | Evidence |
|---|---|---|
| A1 | ✅ | card shows « PC-CI » + « Retirer ce PC » — `shots/A1-card.png` |
| B1 | ✅ | confirm « Retirer « PC-CI » ? » → Annuler; no step-up; SQL `RetiredAtUtc` empty — `shots/B1-confirm.png` |
| B2 | ✅ | step-up « Confirmer votre identité », purpose « Vous allez retirer le PC de secours : sa copie s'arrête. », code field, no password; Annuler retires nothing — `shots/B2-stepup.png` |
| B3 | ✅ | `000000` → « Code de vérification incorrect. » in the dialog, dialog stays open, not retired — `shots/B3-wrong-code.png` |
| B4 | ⏭ | API row — `drive.mjs retire` in the round trip asserts 403 without a code |
| C1 | ✅ | 320×640 and 1536×730: Confirmer + Annuler inside the viewport, no horizontal scroll — `shots/C1-stepup-320.png`, `shots/C1-stepup-1536x730.png` |
| D1 | ✅ | « Modifier le patient » → address changed → saved; SQL holds the new value (no switch refusal) |
| D2 | ✅ | « Déclarer perdu ou volé » → Continuer → its own purpose; Annuler declares nothing — `shots/D2-lost-stepup.png` |
| A2 | ✅ | right code → toast « PC de secours retiré. », card reads « Copie arrêtée le 14/10 » with no « Retirer ce PC »; SQL `RetiredAtUtc` set + one journal row « PC de secours retiré » — `shots/A2-retired.png` |
| X1 | ⏭ | the « maybe already saved » switch refusal (#14) needs the Windows/Android shell and a real server switch |

**Widths looked at:** 1440×900 (all rows), 320×640 and 1536×730 (step-up dialog).

**Status: GREEN** (8 browser rows pass, 2 not exercised with reasons).

# QA run 7 — Congés · « Droit à congé », after the F1 fix

**Date:** 2026-11-06 (15:10 Tunis) · same environment as run 6 · **Status:** RED — walk only, no product finding.

| Row | Outcome | Why |
|---|---|---|
| C5 « on again » and every K row after it | ❌ | the walk pressed « on » before the switch had been read, and a « désactivées » toast still on screen satisfied `/activées/`: congés stayed OFF (SQL `LeaveRequestsEnabled = f`), so every K row met the « pas activées » page |
| A10 | ❌ | the relevé filter started with `\b`, which `hasNotText` (textContent: « −2,5 jsolde ») defeats |
| everything else | ✅ (89) | — |

**Probe fixes (walk.mjs):** C5 waits for the switch to read OFF, presses, waits for ON, asserts it in SQL, and waits
for the toast « congé activées »; the relevé filter drops `\b`. **Re-run:** run-8.md.

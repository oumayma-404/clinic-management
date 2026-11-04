# QA run 2 — remarks + « Étape suivante » + the celebration

**Plan:** `qa/remarks/plan.md` (rows A9–A11 added for the two new asks) · **Driver:** same `walk.mjs` ·
**Logs:** `walk-run2.log` (first drive), `walk-run2b.log` (after one probe fix)
**Environment:** as run 1 — API :5097, web :3097 rebuilt (standalone) after the change, DB `clinic_gfd_all`.
Backend suite 6146 ✅ / 0 ❌, `check:responsive` 82/82, `tsc` clean, build green — all after the last source edit.

**Status: GREEN** — 49 ✅ · 0 ❌ · 1 ⏭ (D5, as run 1: no console account on this DB)

## New rows

| ID | Outcome | Evidence |
|----|---------|----------|
| A9 | ✅ | after « Vos patients »: confetti canvas over the app (removed after ~3 s); toast « 🎉 Votre premier patient est enregistré. » + its joke + « Prochaine étape : Votre secrétaire »; « Étape suivante » → `/` — `shots/A9-celebration-1440.png` |
| A10 | ✅ | the fiche tour ends with « 🎉 Vous savez remplir une fiche de soins. » + its joke |
| A11 | ✅ | a visit booked over the API → `/` shows « 🎉 C'est fait : Premier rendez-vous » + « Votre agenda commence à avoir du mordant. », no « Étape suivante »; a reload celebrates nothing — `shots/A11-dashboard-celebration-1440.png` |

Every run-1 row re-ran and stayed green (A1–A8, B1–B5, C1–C2, D1–D4).

## Findings

None.

## Probe fixes (not product)

| Row | Probe cause |
|-----|-------------|
| A11 | the page was still on the dashboard when the visit was booked, so the realtime broadcast re-read the card and the celebration fired live, before the row looked — the product working. The row now leaves the dashboard before booking. |

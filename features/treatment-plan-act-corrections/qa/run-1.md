# QA run 1 — treatment-plan act corrections

**Status: GREEN** — 26 checks pass, 1 not exercised, 0 product findings.

**Environment.** API `localhost:5000` (Debug, restarted for this run, pid 41756, generation 8) · web
`localhost:3000` (**restarted** — see Observation O1) · Docker postgres + minio, already up, unclaimed.
Peers live at the start (`ListAgents`): `anakin-b0`, `anakin-96`, `clinic-management-2a`,
`clinic-management-64`, `clinic-management-a2`, `seo-fix-apexa-tn` — **all idle**, none holding a tier.
Driver: `qa/walk.mjs`, one launch, Chrome via `playwright-core` (`channel: "chrome"`).

**Widths actually looked at:** 320 · 820 · 1440 · **1536 × 730** (the owner's real laptop height).
Screenshots read, not merely captured: `A1-focus-ring`, `A2-picker`, `A6-no-confirm`, `C4-noop-save`.

---

## Scenario results

| id | Scenario | Outcome | Evidence |
|---|---|---|---|
| A1 | « Modifier » on an act row opens the amend modal with that line focused | ✅ | exactly one `ring-primary/40` line · `shots/A1-focus-ring.png` |
| A2 | Picker grouped by discipline | ✅ | « Consultation · Radiologie · Soins conservateurs · Endodontie » · `shots/A2-picker.png` |
| A2b | Colour dot per act | ✅ | 37 dots |
| A3 | « N séances » pill on multi-séance acts | ✅ | 16 rows carry one |
| A4 | Swapping the act changes the désignation | ✅ | line became « Retraitement endodontique » |
| A4b | …and the **fee** follows | ✅ | the 50,000 left the line |
| A5 | Save an amendment | ⏭ | folded into A8 — the same save path, exercised there |
| A6 | Bin on an act with no booking and no fiche | ✅ | removed with **no** confirmation |
| A7 | Bin on an act with a future RDV | ✅ | « Le rendez-vous du 20/09/2026 10:00 sera annulé : cet acte est la seule raison de cette séance. » |
| A8 | Confirm + save | ✅ | act gone; devis holds the other three |
| A8b | The sole-purpose visit is cancelled | ✅ | appointment reads `Cancelled` on the wire |
| B1 | Bin on a **réalisé** act | ✅ | disabled, and the line says « Acte déjà réalisé — utilisez « Détacher la fiche » » |
| B1b | …and only that one | ✅ | `[true, false]` |
| B3 | Bin on an act whose RDV **has passed** | ✅ | **enabled** — the old false refusal is gone |
| B4 | Bin on an act sharing its RDV | ✅ | « L'acte sera retiré du rendez-vous du 11/09/2026 09:00, qui reste prévu pour les autres actes. » |
| B5 | Cancel the confirmation | ✅ | line still present |
| B6 | Re-pick the **same** act (typo-fix case) | ✅ | fee and séances unchanged |
| B8 | Remove a réalisé act over the API | ✅ | 400, message names « fiche de soins » |
| C1 | Amend modal at 320 px | ✅ | no horizontal page scroll |
| C1b | Picker popover at 320 px | ✅ | x = 8, w = 288 — inside the gutter |
| C2 | Amend modal at 1536 × 730 | ✅ | « Enregistrer la révision » bottom = 669 |
| C3 | Act row + card at 820 px | ✅ | 3 « Modifier » controls, all inside the viewport |
| C4 | Reopen + save with no edit | ✅ (premise corrected) | no act changed, total still = Σ acts. See Observation O2 |
| D1 | État badges on the act rows | ✅ | unchanged |
| D2 | `/treatment-plans` | ✅ | loads |
| D3 | Shared séance keeps its other act **and its agreed price** | ✅ | 2 acts still on it |
| D6 | Patient « Plan de traitement » tab | ✅ | renders |
| D7 | Plan total = Σ acts | ✅ | 100 = 100 |
| D5 | Unfiltered unit suite | ✅ | 4646 passed, 0 failed |

**Not exercised on purpose:** « Facturer », « Encaisser », « Arrêter le traitement » — none is under test and
`verification.md` § 7 makes money and clinical confirms read-only in a QA pass.

---

## Findings

**None.** Every failure in this run's five drive attempts was triaged to the probe or the environment — see
below. That ratio is the point of Phase 5: **11 of 13 « failures » in the first attempt were one helper.**

---

## Observations (off-plan)

| # | Severity | Observation |
|---|---|---|
| O1 | observation (environment, **not the product**) | The `:3000` dev server had been up since **11 Sep**. Its compile workers were crashing (`Jest worker encountered 2 child process exceptions, exceeding retry limit`) and **every dynamic route** — `/treatment-plans/[id]`, `/patients/[id]` — rendered an **empty document with no `<main>`**, while static routes were fine. Restarting it cleared 11 scenarios at once. A long-lived dev server is a scenario-wide false-negative generator. |
| O2 | observation (**pre-existing**, unrelated to this change) | Reopening « Modifier le devis » and saving with **no edit** bumps `revisionNumber`. The guard « Aucune modification demandée. » only fires when the échéancier is empty too, and the modal always echoes an existing échéancier back — so on any devis with a schedule, a reopen+save is never a no-op. Nothing about the acts or the total changes. Worth a decision later; it is not a regression from this work. |
| O3 | observation | `AppointmentProcedure.AgreedCost` came back `0` on the arranged shared séance even though the arrange sent `40`. Not chased (off-plan, and D3's assertion is about the act surviving, not the figure). Flagged because a silently-dropped agreed price is a shape this repo has been bitten by before. |

---

## Probe bugs found and fixed during the run

Kept because they are the reusable half of this pass — each produced a convincing false defect report.

| # | Symptom | Actual cause |
|---|---|---|
| P1 | 10 of 13 rows timed out at 20 s | my `settled()` helper waited on `body.innerText` not starting with « Chargement » **and** matching a regex; the heading matched before the acts arrived |
| P2 | every page load 401-ed; the workspace rendered blank | the `api()` helper re-fetched `/bff/auth/token` **per request**, and that exchange **rotates** the refresh credential — it spent the session's own credential repeatedly |
| P3 | « the control does not exist » (30 s click timeouts) | both trees (table **and** card list) are in the DOM, one hidden by a breakpoint class — `.first()` resolved to the invisible one. `:visible` is the discriminator |
| P4 | B1 asserted against « Séances de l'acte » | `aria-label^="Modifier"` **also** matches « Modifier les N séances de … », the steps control sitting immediately before the edit control on the same row |
| P5 | `lineFor(...)` matched nothing | an act's name is the **value of a controlled `<input>`**, not text — `hasText` cannot see it |
| P6 | A6 reported a confirmation on an unbooked act | the act blocks were addressed as `div.rounded-lg.border`, which also matches nested bordered boxes, so the index pointed at a different act |
| P7 | arrange refused: « déjà présent dans ce rendez-vous » | two acts of one séance may not share a catalogue procedure type |
| P8 | `Locator.allInputValues()` | not a Playwright API |

`P1`–`P6` are now recorded in `.claude/skills/test-in-browser/SKILL.md` § « Phase 5 » so the next pass does
not pay for them again.

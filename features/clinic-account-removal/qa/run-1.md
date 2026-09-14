# Run 1 — clinic-account-removal, « Zone dangereuse »

**Status: GREEN** — 22 scenarios, 21 pass, 1 not exercised (with its reason), 0 findings.

## Environment

| | |
|---|---|
| Date | 2026-09-14 |
| Console | `http://localhost:3100` (dev, `CONSOLE_API_URL=http://localhost:5443/api`) |
| API | **restarted during this pass** — see Observation 1. New pid recorded, lease generation 6 |
| Peers live | `clinic-management-2a`, `clinic-management-64`, `clinic-management-a2`, `anakin-b0`, `anakin-96`, `seo-fix-apexa-tn` — **all idle**; the API's recorded owner (`opus-investigate`) no longer existed |
| Driver | `qa/walk.mjs`, playwright-core + `channel: "chrome"`, headless, `fr-FR` |
| Cabinet deleted | « Cabinet Jetable QA » `86249bfb-…`, created by `provision-clinic` for this pass. **No other cabinet was touched, and production was not touched.** |

## Scenarios

| # | Tier | Scenario | Outcome | Evidence |
|---|---|---|---|---|
| A1 | A | sign in: enrolment → recovery codes → forced password change | ✅ | 8 recovery codes shown once (counted, deliberately not written to disk — this folder is committed) |
| A2 | A | « Zone dangereuse » last, destructive border, names suspension as reversible | ✅ | 8 sections, `danger-heading` last; `shots/A5-outcome-1440.png` shows the section |
| A3 | A | the preview's census | ✅ | **371 rows** on screen, « comptes 1 », `jetable.qa@exemple.tn` listed, « définitive … pas de corbeille »; `shots/preview-text.txt` |
| A4 | A | the field asks for the ADDRESS | ✅ | label « Adresse e-mail d'un compte de ce cabinet », placeholder = that address |
| A5 | A | the real deletion; the panel stays open | ✅ | « Cabinet supprimé », freed address shown; `shots/A5-outcome-1440.png` |
| A5b | A | what it removed = what it promised | ✅ | `rowsDeleted` 371 = preview 371 |
| A6a | A | absent from the portfolio | ✅ | 8 cabinets → 7 |
| A6b | A | the journal row | ✅ | `Action=10`, `ClinicName=Cabinet Jetable QA`, `AccountEmail=qa.suppression@editeur.tn`, `Reason=cabinet de test QA — adresse a liberer` |
| A7 | A | the address is free (`sql`) | ✅ | `Users` holding it: 1 → **0**; every table of the cabinet: **0**; **re-provisioned with the same address** → « Cabinet Jetable QA (reprise) » `05e112f9-…` |
| B1 | B | blank motif | ✅ | refused in French, cabinet still in the database |
| B2 | B | wrong address | ✅ | `clinic_confirmation_mismatch`; the refusal does **not** spell the right address out |
| B3 | B | the cabinet's **name** typed | ✅ | refused — `clinic_confirmation_mismatch`, nothing deleted |
| B4 | B | field + button absent until the preview lands | ✅ | preview delayed 3 s on purpose: 0 inputs, 0 submit, « Lecture de ce que contient ce cabinet… » |
| B5 | B | « Revenir » with text typed | ✅ | « Abandonner cette suppression ? Rien ne sera supprimé. »; answering no keeps it open |
| B6 | B | the fiche afterwards | ✅ | « Ce cabinet n'existe plus » |
| C1 | C | 320 × 720 | ✅ | no page overflow, submit 36 px, footer pinned · `shots/C1-320x720.png` |
| C2 | C | 390 × 844 (coarse pointer emulated) | ✅ | `shots/C2-390x844.png` |
| C3 | C | 820 × 1024 | ✅ | `shots/C3-820x1024.png` |
| C4 | C | 1180 × 820 | ✅ | `shots/C4-1180x820.png` |
| C5 | C | 1440 × 900 | ✅ | `shots/C5-1440x900.png` |
| C6 | C | **1536 × 730** (the owner's real page height) | ✅ | dialog centred, footer and both buttons visible · `shots/C6-1536x730.png` |
| C7 | C | double submit | ⏭ | unreachable through the UI once the panel switches to its result view. Proven instead at the SQL layer: the database rehearsal's `IDEMPOTENT` check (second purge = 0 rows, no error) |
| D1 | D | `/cabinets` portfolio | ✅ | renders, lists the cabinets |
| D2 | D | the fiche's other sections | ✅ | Abonnement, Suspension, Activité, Administrateur, journal link — 8 sections |
| D3 | D | `/journal` | ✅ | renders; `shots/A6-journal-1440.png` |
| D4 | D | `tsc --noEmit` + `check:responsive` in `console/` | ✅ | clean · **15/15** checks |

Widths actually looked at, by opening the PNG: **320 · 390 · 1536×730 · 1440**. 820 and 1180 were measured
(overflow + button box) but not eyeballed.

## Findings

**None.** Everything the plan asserted about the product held.

## Observations (off-plan)

1. **The running API predated the feature, and an empty `401` is how this pipeline says « no such route ».**
   The process had been up since 2026-09-13 21:14 — before the controller existed — so the preview answered
   401 with an empty body. ⚠️ **`401` is therefore NOT evidence that a console route exists**: with a *valid*
   console token, `/platform/nope` and `/platform/clinics/{id}/definitely-not-a-route` both answer an empty
   401 too, because the fallback policy validates the token against the **clinic** scheme (a different signing
   key). I had read the earlier unauthenticated 401 as « the route is there », which was wrong. The API was
   restarted (its recorded owner no longer existed and every peer was idle) and re-recorded in the lease.
2. **Next's dev error overlay carries `role="dialog"` and is `aria-modal`.** It appears here for React's
   development-only `unsafe-eval` CSP warning, so a bare `[role="dialog"]` selector is both a strict-mode
   violation and a click-swallowing layer. `data-slot="sheet-content"` is the panel. Production is unaffected
   — React does not use `eval` there — but the console's dev CSP makes every dev session noisy.
3. **Changing the console password clears the session on purpose** (`app/bff/password/route.ts`: `SetPassword`
   bumps `TokenVersion`), so a forced change lands the operator back on the sign-in screen. Deliberate and
   documented; a probe that does not re-authenticate reports « could not sign in ».
4. **At 390 px and at 730 px tall the « Motif de la suppression » field sits below the panel's own fold.** The
   body scrolls and the submit is always visible, so the worst case is pressing the button, being refused for
   a missing motif, then scrolling — nothing is deleted. Minor, and inherent to a `max-h-[85dvh]` sheet.
5. **A stray cabinet was left behind on dev on purpose**: « Cabinet Jetable QA (reprise) » `05e112f9-…`, the
   re-provision that proves the address came back. Remove it whenever you like.

## Probe bugs fixed during the run (none of them a source change)

Five, and each produced a convincing false defect first:

| Symptom | Actual cause |
|---|---|
| « the portfolio was not reached » | the forced password change clears the session **by design**; the probe never signed in again |
| « could not sign in with either password » | **hydration race** — filling right after `domcontentloaded` sets the DOM value before React attaches, so the form posted empty credentials and answered 401 |
| « strict mode violation: 2 dialogs » | Next's dev error overlay also has `role="dialog"` |
| « the census reads 1371 rows » | a greedy digit class swallowed « comptes **1** » before « 371 lignes en tout » — the figure is now read off the wire |
| « the refusal spells the right address out » | the probe tested the whole panel, whose census lists the freed address **above** the refusal on purpose; it now tests `[role="alert"]` only |

Plus one plan error: the cancel button is « **Revenir** » (« Fermer » once deleted), never « Annuler ».

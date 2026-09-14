# QA plan — clinic-account-removal (« Zone dangereuse » / supprimer un cabinet)

Surface that moved (`git diff 01fd33fa~1..4b7ea088 -- console/`): `console/app/cabinets/[clinicId]/page.tsx`
(+41), `console/components/delete-clinic-dialog.tsx` (+405), `console/app/bff/suppression/route.ts` (+85),
`console/lib/api/platform.ts` (+70). Two of the four render.

Class: **large / irreversible**. All four tiers, D exhaustive over the console's own screens.

## Environment

| | |
|---|---|
| Console | `http://localhost:3100` (dev, `CONSOLE_API_URL=http://localhost:5443/api`) |
| API | `http://localhost:5443/api`. ⚠️ It had to be **restarted**: the running process predated the controller, and an empty 401 on this pipeline means « no such route », not « refused » — see `run-1.md` Observation 1 |
| Console account | `qa.suppression@editeur.tn`, created for this pass |
| Deletable cabinet | **only** « Cabinet Jetable QA » `86249bfb-5ad9-4858-97f5-aa484cd70c61`, admin `jetable.qa@exemple.tn` |
| Off limits | the 7 other dev cabinets · the production deployment · the web dev server |

## Baseline, read before the browser (`sql`)

| Figure | Value |
|---|---|
| `Users` holding `jetable.qa@exemple.tn` | 1 |
| `Users` of the cabinet | 1 |
| `ProcedureTypes` / `Medications` / `DentalActCodes` / `CnamLetterValues` | 35 / 25 / 102 / 4 |
| `ClinicSubscriptions` / `AuditEntries` | 1 / 171 |
| `Clinics` total | 8 |
| `PlatformAccessEntries` total | 35 |

## Ordering constraint

The deletion can only happen **once**, and it destroys the screen under test. So: every refusal and **every
width** is exercised **before** the real deletion; the post-conditions after it.

## Scenarios

### Tier A — happy path

| # | Scenario | Expected (observable) | Layer |
|---|---|---|---|
| A1 | Sign in: temp password → forced change → TOTP enrolment | reaches the portfolio; recovery codes shown once | browser |
| A2 | Open the cabinet's fiche | « Zone dangereuse » is the **last** section, `border-destructive`, and its text names « Suspendre ce cabinet » as reversible and costing no paid day | browser |
| A3 | Press « Supprimer définitivement » | a preview arrives; the census names **35 ProcedureTypes, 102 DentalActCodes, 25 Medications, 1 compte** (SQL above); `jetable.qa@exemple.tn` listed as freed | browser |
| A4 | Read the field's label | « Adresse e-mail d'un compte de ce cabinet », placeholder = that address — **not** the cabinet's name | browser |
| A5 | Correct address + motif → submit | the panel **stays open**, heading becomes « Cabinet supprimé », and it lists what was removed and the freed address | browser |
| A6 | Portfolio + journal | the cabinet is absent from `/cabinets`; the journal carries « Cabinet supprimé définitivement » with the typed motif | browser |
| A7 | The address is free | `select count(*) from "Users" where lower("Email")='jetable.qa@exemple.tn'` = **0** | sql |

### Tier B — alternate paths

| # | Scenario | Expected | Layer |
|---|---|---|---|
| B1 | Correct address, **blank motif** | refused, French sentence, cabinet still there | browser |
| B2 | **Wrong address** + motif | refused; the sentence does **not** spell the right address out | browser |
| B3 | The **cabinet's name** typed instead of the address | **refused** — a name is not unique | browser |
| B4 | The moment the panel opens | the field and the submit button are **absent** (not disabled) until the preview lands | browser |
| B5 | « Revenir » with text typed | a confirm « Abandonner cette suppression ? Rien ne sera supprimé. » | browser |
| B6 | Reload the fiche after deletion | « Ce cabinet n'existe plus » | browser |

### Tier C — edge cases

| # | Scenario | Expected | Layer |
|---|---|---|---|
| C1–C5 | The open panel at 320 / 390 / 820 / 1180 / 1440 px | no horizontal page scroll; field, motif and both buttons reachable; nothing clipped | browser |
| C6 | 1536 × **730** (the owner's real page height) | the submit button is reachable — the sheet scrolls its own body | browser |
| C7 | Double submit | one deletion, no error | ⏭ — unreachable through the UI once the panel switches to its result view; proven instead at the SQL layer by the rehearsal's `IDEMPOTENT` check |

### Tier D — regression sweep

No `blast-radius.md` exists for this feature; derived here from the diff. `page.tsx` gained one section and
`platform.ts` gained types only — nothing existing was modified, so the sweep is « the screens that import
what moved ».

| # | Scenario | Expected | Layer |
|---|---|---|---|
| D1 | `/cabinets` portfolio | renders, lists the cabinets, search works | browser |
| D2 | The fiche's other sections | Abonnement, Suspension, Activité, Administrateur, journal link all still render | browser |
| D3 | `/journal` | renders | browser |
| D4 | `npx tsc --noEmit` + `npm run check:responsive` in `console/` | clean | command |

## Status

Run 1: **GREEN** — 21 pass, 1 not exercised, 0 findings. See `run-1.md`.

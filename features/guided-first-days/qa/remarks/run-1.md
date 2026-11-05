# QA run 1 — owner's remarks of 2026-11-04

**Plan:** `qa/remarks/plan.md` · **Driver:** `qa/remarks/walk.mjs` · **Logs:** `walk-run1.log` (first drive), `walk-run1b.log` (same plan after one probe fix)
**Environment:** API :5097 + web :3097 (standalone production build, rebuilt after the change), DB `clinic_gfd_all`
(migration `20261104100350_AddFeedbackOtherAndWatchedGuides` applied; `verify-schema` shows no model/catalog drift —
its 2 DRIFT lines are a test admin without a factor and the dev key ring, both environmental). Own cabinets
`qa-rk-*`. Peers live: none on this stack (ListAgents: 6 idle/other-project sessions). The owner's own driven Chrome
was open on :3097 with her own account, untouched.

**Status: GREEN** — 42 ✅ · 0 ❌ · 1 ⏭

## Scenarios

| ID | Outcome | Evidence |
|----|---------|----------|
| A1 | ✅ | « Me guider : Première fiche de soins » on the card |
| A2 | ✅ | QR `data:` PNG shown; link `https://apexa.tn/#telecharger` `target=_blank`; no « proposée », no Android/iPhone split — `shots/A2-phone-1440.png` |
| A3 | ✅ | `GET /meta/phone-app` 200 `{ url, qrPng }` |
| A4 | ✅ | 7 tour steps each lit + sentence; « Suivant » ×6, « Terminer » last — `shots/A4-*-1536x730.png` |
| A5 | ✅ | toast « Vous savez remplir une fiche de soins. »; 0 fiche for the cabinet; `WatchedGuidesCsv = dentalRecord` |
| A6 | ✅ | card: no « Me guider » on « Première fiche de soins » (« Fait ») |
| A7 | ✅ | « Écrire à APEXA » opens on « Avis »; 2 star rows, 2 « Autre », « Un mot de plus ? » — `shots/A7-aide-avis-1440.png` |
| A8 | ✅ | rows `avis|aide|4|agenda|Les rappels au téléphone||` and `idee|aide|…|Un export Excel des devis.` |
| B1 | ✅ | no patient → « Ce guide a besoin d'un patient… » |
| B2 | ✅ | Escape on the fiche mid-tour ends it; nothing recorded |
| B3 | ✅ | `POST …/patients/watched` → 400 `guide_step_not_watchable` |
| B4 | ✅ | « Envoyer » disabled on open, enabled by Question text; dot on « Question »; text kept across tabs |
| B5 | ✅ | week-one form keeps the 3 stars, 2 « Autre », no tabs; row `avis|semaine-1|3|…|Trouver la caisse du jour|` — `shots/B5-week-one-1440.png` |
| C1 | ✅ | 320×640: no body overflow, 4 tabs inside the sheet; phone dialog QR withheld, button visible — `shots/C1-*-320.png` |
| C2 | ✅ | 1536×730: bubble inside the viewport and off the lit control on `record-payment` and `record-submit` |
| D1 | ✅ | « Vos patients »: middle step « Suivant », save ends with its toast |
| D2 | ✅ | `PUT /dashboard/preferences` hide + show → 200 ×2, watched set kept |
| D3 | ✅ | (run 1b) notes + prescription sections open after the wrappers; no bubble |
| D4 | ✅ | card still lists the other steps (patients done, appointment open) |
| D5 | ⏭ | no console account on this database, so the notice e-mail has no recipient; its content is held by `FeedbackTests.The_Autre_Lines_Are_An_Answer_On_Their_Own_And_Reach_The_Notice` |

**Widths looked at:** 320×640, 1440×900, 1536×730.

## Findings

None.

## Probe fixes (not product)

| Row | Probe cause |
|-----|-------------|
| D3 | an empty « Notes de séance » shows « Ajouter une note », not an input — the script looked for the input's placeholder |

## Observations (off-plan)

- At 1440 the « Aide » popover shows faded behind « Écrire à APEXA » (`shots/A7-aide-avis-1440.png`, top right). Not a defect: `help-menu.tsx` closes it (`setOpen(false)`) before opening the dialog — the shot caught its exit animation.

## Not covered

- Console « Retours » rendering of the « Autre » text (console not running on this stack; DTO + read-shape held by unit tests).

# Browser walk — run 1 (2026-11-13)

Isolated stack: the branch's API on `:5099` against `clinic_camera_qa` (a copy of the dev database, this branch's
three migrations applied), `next dev` on `:3099`, a throwaway MinIO bucket, headless Chrome (`playwright-core`). The
shared stack, the shared database and prod were not touched. Peers at the time: one idle clinic-management session.

Fixtures (through the product's API): three visits today — 15:00 and 16:00 **with** Dr Ben Youssef, 17:00 with **no**
practitioner. Photos: real JPEGs drawn on a canvas with an EXIF block (Canon EOS 250D, serial …8901).

## Result — GREEN after one fix (26 pass · 1 not exercised)

| # | Scenario | Result |
|---|---|---|
| A1 | `/photos/import` source step, no « Depuis l'appareil » in a browser; no overflow at 1440 | ✅ |
| A2 | 4 photos → 3 groups, each on its patient — including the visit booked with **no practitioner** | ✅ after fix F1 |
| A2b/c | « Ranger » on screen at 1440×730 (sticky); no overflow at 320 | ✅ |
| A3 | 4 photos filed in « Photographie » with capture key, capture time, camera model; hosted copy stored | ✅ |
| A4 | Same card again → « déjà rangées », nothing to file | ✅ |
| B1 | Windows app (fake 1.8 bridge): « Depuis l'appareil branché » + « 2 photos en attente »; no overflow at 320 | ✅ |
| B2 | Delivered batch opens straight into the review; « Ajouter 4 photos plus anciennes… »; RAW counted | ✅ |
| B3 | Every photo acknowledged `filed` + `keepOriginal`, with file and patient | ✅ |
| B4 | `camera-original-kept` → server records « Original : PC TEST »; shell told `recorded` | ✅ |
| B5 | « Appareil photo détecté » toast; « Ignorer cet appareil » → `ignoreCamera` | ✅ |
| B6 | `?appareil=` → scan of that device, key dropped from the URL | ✅ |
| B7 | Paramètres « Sur ce PC »: switch → `setCameraAutoImport(true)`, originals line, copy button; no overflow at 320 | ✅ |
| B8 | Preview of a camera photo held here: « Original » → `openOriginal`; « Original : PC TEST » line | ✅ |
| D1 | A cabinet's own quota (2 Go) reaches the storage line | ✅ |
| D2 | Patient files: camera badge « Prise le … » | ✅ |
| D3 | Mirror manifest without coffre files | ⏭ no coffre file in the test clinic (the filter is one SQL predicate) |
| — | Vendor console « Stockage » column / « Modifier le quota » | ⏭ not walked: needs the console app and a console sign-in; its gate (typecheck · 17 checks · build) is green |

## Finding fixed in this run

| # | What was broken | Where | Fix |
|---|---|---|---|
| F1 | A visit booked with **no practitioner** was invisible to a camera that follows a dentist's agenda — « Aucun rendez-vous à cette heure-là » over a visit in the book. Most visits name nobody (32 of 42 in prod, 678 of 986 here). | `agendaFor` (`web/lib/photo-import/match.ts`) — the filter actually used — and the same rule on the server (`GetPhotoImportVisitsAsync`) | A practitioner's agenda includes practitioner-less visits, on both sides; pure test added (`photo-match.spec.ts`, 19/19) |

## Probe notes (not product)

- One `400` in the console: `GET /api/clinics/letterhead/header` — the letterhead image lives in the shared bucket the
  isolated API deliberately does not use.
- The `401`s are the sign-in page's token probes before login.

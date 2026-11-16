# QA plan — Camera photo import, run 2

**Written:** 2026-11-16
**Change under test:** `feature/camera-photo-import` (d46c3d11..4208905e) — focus on the 2026-11-15 mistake-case
commits (b9045895, 4208905e), plus every `must re-test` row left from phases 2–3.
**Rendering files in the diff:** 26 — `components/photo-import/*` (incl. the new `photo-viewer-dialog.tsx`,
`new-patient-for-group.tsx`), `patients/patient-search-list.tsx`, `patients/files/*` (move dialog, camera badge,
preview « Original »), `settings/cameras-card.tsx`, `app/photos/import/page.tsx`, console components.
**Blast-radius tables:** `progress.md` § « Mistake cases » (2 `must re-test`) and § « Blast radius (phase 2 + 3) »
(6 `must re-test`).
**Budget:** large (clinical files, a shared search primitive, a quota on every upload door) → tiers A B C D ·
46 scenarios.
**Stack:** isolated — API `:5099` (DB `clinic_camera_sd`, bucket `camera-demo-5e2c`), web `:3099` (production
build), headless Chrome with its own profile (`scratchpad/qa-profile`, one sign-in). Cards: the Desktop folders
(`Carte SD démo`, `Carte SD démo 2 (Nikon)`, `Scans ordonnances (pas une carte)`) + generated ones in
`scratchpad/qa-cards/` (`raw-only`, `empty-dcim`, `clock-reset`, `big`). Only the folder dialog is stood in for.

## Tier A — happy path (the demo day, every mistake case)

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| A1 | `/photos/import` | choose the Canon card | review shows « 6 photos déjà rangées », « Afficher aussi 6 photos plus anciennes », the 2 RAW without a JPEG counted (not the 37 twins), no « autre ignoré » for CANONMSC; « Ranger N photos » visible | browser | 1536×730 |
| A2 | review | group 08:36 – 08:39 | « Sûr », patient Amira Ben Salah | browser | — |
| A3 | photo viewer | tap « Voir la photo de 08:37 » → « Écarter cette photo » → « Fermer » | dialog opens on « Photo de 08:37 »; after Écarter the group reads « 4 photos gardées sur 5 » | browser | 1536×730 |
| A4 | review | groups 09:52 – 09:54 and 10:21 – 10:22 | Karim Hamdi (late 40 min) and Leïla Trabelsi (no practitioner), both « Sûr » | browser | — |
| A5 | review | group 11:08 – 11:09 → pick « Ines Chaabane » | asks « Deux patients étaient au fauteuil… »; after the pick the group names Ines | browser | — |
| A6 | review | group 12:08 – 12:18 → « Couper à … » | two groups: 12:08 – 12:14 Mehdi Jaziri, 12:16 – 12:18 Sarra Ferchichi | browser | — |
| A7 | review | group 13:12 | Hichem Ayari « Sûr » (walk-in fiche) | browser | — |
| A8 | review | group 14:36 – 14:37 → « Autre patient… » → « Saidi » → Rim Saidi | chosen | browser | — |
| A9 | viewer | « Voir la photo de 15:10 » → « Ranger chez un autre patient » | a card « 15:10 » that reads « Photo sortie de son groupe. », offers Bilel Khemiri and NOT Olfa Mansour; Olfa's group keeps 3 photos | browser | — |
| A10 | review | group 16:25 – 16:26 → « Autre patient… » → « Sonia Mejri » → « Nouveau patient « Sonia Mejri » » → « Créer et choisir » | « Aucun patient pour cette recherche. »; form seeded Prénom Sonia / Nom Mejri; then « Choisi » + toast « Patient créé : Sonia Mejri » | browser | 1536×730 |
| A11 | review | group 17:12 | « Pas d'heure dans la photo. », « Appareil inconnu »; « Ignorer cette photo » | browser | — |
| A12 | filing | « Ranger 40 photos » with 2 uploads cut → « Réessayer » | « 2 photos non rangées » then « 40 photos rangées » | browser | — |
| A13 | — | after A12 | 40 new `PatientFiles` with `CaptureKey`, `CapturedAt`, `CameraModel`, `Origin = Camera`, a preview; Sonia Mejri exists once; Canon `LastImportedAtLocal` = 2026-11-15T17:… | sql | — |
| A14 | review | same Canon card again | « 46 photos déjà rangées »; nothing filed twice | browser | — |
| A15 | review | Nikon card | « Nouvel appareil » prompt; every group asks (« Nouvel appareil : confirmez son agenda… ») | browser | 1536×730 |
| A16 | review | pick « Nadia Trabelsi » | « … semble avoir 1 h de retard » notice; groups sure on Nadia's patients; « Ranger 12 photos » → « 12 photos rangées » | browser | — |
| A17 | `/patients/{Mehdi}/files` | IMG_0423 → « Changer de patient » → Ferchichi → « Ranger chez Sarra Ferchichi » | toast « Fichier déplacé »; the photo is in Sarra's « Photographie » | browser | 390, 1536×730 |

## Tier B — alternate paths

| ID | Surface | Steps | Expected (observable) | Layer | Widths |
|----|---------|-------|-----------------------|-------|--------|
| B1 | source | the PDF folder | « Aucune photo JPEG sur la carte » + « Les fichiers lus ne sont pas des photos… » | browser | 320 |
| B2 | source | a RAW-only card | « Aucune photo JPEG sur la carte » | browser | — |
| B3 | source | an empty DCIM with only CANONMSC | « Aucune photo dans ce dossier » | browser | — |
| B4 | review | all filed (Nikon again after A16) | « Aucune nouvelle photo à ranger » + « Toutes les photos de la carte sont déjà rangées. » | browser | — |
| B5 | review | pick Ines (A5) then show / hide « plus anciennes » | the Ines choice still stands | browser | — |
| B6 | review | Nikon: undo the clock correction, redo it | groups re-decided; a choice on an unchanged group stands | browser | — |
| B7 | review | before resolving the asks | « Ranger » blocked; the bar names how many groups have no patient | browser | 1536×730 |
| B8 | viewer | open a photo of a single-photo group (13:12) | no « Ranger chez un autre patient » | browser | — |
| B9 | viewer | ←/→ keys, Escape | moves through the group; Escape closes; focus returns | browser | — |
| B10 | moved card | « Remettre dans son groupe » then move again | the photo rejoins Olfa (4 photos), then leaves again | browser | — |
| B11 | new patient | « Nouveau patient » → « Annuler » | back to the search, nothing created | browser + sql | — |
| B12 | new patient | create « Rim Saidi » again from another group | « Ce patient existe peut-être déjà » question; cancel keeps the form | browser | — |
| B13 | api | upload a photo whose capture key is already filed | a named refusal (code), never a 500, nothing stored | api | — |
| B14 | owner prompt | « Confirmer » keeps the importer's agenda | the asks lift | browser | — |

## Tier C — edge cases and devices

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | review at every width | resize the live review | `main` never scrolls sideways (scrollWidth − clientWidth ≤ 1); screenshots | browser | 320, 390, 820, 1180, 1440 |
| C2 | viewer as a sheet | open the viewer | footer buttons visible inside the viewport; image inside | browser | 320, 390, 1536×730 |
| C3 | landscape phone | viewer at 740×380 | the image and « Fermer » reachable | browser | 740×380 |
| C4 | new-patient form | open it | fields + both buttons inside the card; no sideways scroll | browser | 320, 1536×730 |
| C5 | coarse pointer | emulate `pointer: coarse` at 820 | every control of a burst card, the viewer and the search ≥ 44 px (box or `.touch-target` overlay) | browser | 820 |
| C6 | sticky « Ranger » | 1536×730 and 390 (bottom bar) | the bar is on screen without scrolling to the end | browser | 390, 1536×730 |
| C7 | clock reset | a card from the paired Canon dated 2019-01-01 | « Aucune nouvelle photo à ranger » + « plus anciennes que la dernière importation » + « Afficher aussi … »; shown, they ask (no visit that day) | browser | — |
| C8 | big card | 400 full-size photos, 7 visits | read ≤ 60 s, review renders, « Ranger 400 » files all, tab memory stays bounded (logged) | browser + sql | 1536×730 |
| C9 | name with an accent | search « Leila » (no accent) | finds « Leïla Trabelsi » | browser | — |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | `PatientSearchList` (+ `onCreate`) | « Changer de patient » dialog | no « Nouveau patient » button there; search works (A17) | browser | 390 |
| D2 | `session.ts` recount | offset notice | « Annuler la correction » / redo works (B6) | browser | — |
| D3 | `session.ts` recount | owner prompt | (A15, B14) | browser | — |
| D4 | `session.ts` recount | « plus anciennes » | show / hide (A1, B5) | browser | — |
| D5 | cameras card | Paramètres « Appareils photo » | both cameras listed (Canon → Ben Youssef, Nikon → Trabelsi), rename + agenda change saved | browser | 390, 1440 |
| D6 | File preview slot | a DICOM and a HEIC in a patient's files | DICOM keeps « Visionneuse », HEIC keeps « Pleine résolution »; a camera photo shows « Prise le … » | browser | 1440 |
| D7 | `ClinicStorageAllowance` | upload under a custom quota; over it | under: stored; over: the server's French refusal, nothing stored | api + browser | — |
| D8 | Console portfolio (`hostedBytes`) | console list, « Stockage » sort, « Modifier le quota » | list loads, sort orders by bytes, the change is journaled | browser | 1440 |
| D9 | Manifest hosted-only | mirror manifest with a coffre file present | the coffre file is not listed | api | — |
| D10 | `VaultBridge.Script` | Windows app: coffre handle + chat toast | — | — | ⏭ see below |
| D11 | `CameraFileKinds` | Windows app with a real card | — | — | ⏭ see below |

## Not covered, and why

| Area | Why not |
|------|---------|
| D10, D11 — the Windows app itself | the shell only connects over HTTPS to a server whose certificate this PC trusts; the isolated stack is plain HTTP, and trusting a test root certificate means changing this PC's certificate store. Owed with the real camera at the pilot (its 242 unit tests are green). |
| A real Canon / Nikon on USB | no camera here (owed, pilot). Real files' EXIF are checked separately against public camera samples. |

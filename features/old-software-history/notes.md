# Old-software history — what shipped

A cabinet switching from another software brings, per patient, a PDF the old software exported. Everything in it
now has a place, and nothing in it becomes a fiche.

## Three patient fields

`Patient.Occupation` (« Profession »), `MaritalStatus` (« Situation familiale »), `CountryOfOrigin`
(« Pays d'origine ») — nullable, free text, through create / update (tri-state like `ReferredBy`) / DTO / CSV
export + import / the patient dossier export, shown in the record card and in « Modifier le patient » (one row of
three).

- ⚠️ **`CountryOfOrigin` is nationality, never `Address.Country`.** In the first real export 6 of 25 patients were
  « Guinée » and live in Tunisia; filing that as the address would assert where they live.
- ⚠️ **`MaritalStatus` stores the French word itself** (« Marié(e) »). The form offers four
  (`web/lib/marital-status.ts`); an imported word outside them (« Marie ») is kept and offered back as a fifth option,
  so an ordinary save never drops it. No enum → no key/label mirror to keep in step.

## « Ancien logiciel » — `PatientLegacyHistoryEntry`

One line per old consultation / ordonnance / radio / diagnostic: `Date` (`date`, nullable) · `Category` (the old
software's own word, shown as is) · `Title` · `Details` · `Position`. Read-only by construction: no mutator, no update
or delete endpoint, no money, no tooth, no status.

- `GET` / `POST /api/patients/{id}/legacy-history`, both `AnyClinicRole` — the import runs as a secretary account.
  The POST **appends** (a merged second record of one person brings its own lines) and is **all-or-nothing** per
  call (≤ 500 lines); re-run idempotency is the importer's job (read first, skip when non-empty).
- Clinical child like the antécédents: `ClinicId` denormalised + tenant filter, `IAuditable`, counted in
  `PatientLinkedDataCounts` (⚠️ so a patient holding lines can be neither deleted nor merged away — clean a test
  import with SQL), checked by `verify-schema`'s clinic-matches-patient, archived/purged by the model-derived plans.
- No navigation on `Patient`, so no aggregate load ever pulls the lines.
- The tab appears only when the patient has lines (or the read failed, or a `?tab=legacy-history` deep link — the
  deletion refusal links there).

## The import side (outside this repo)

The PDF layout and the table parser are recorded in `C:\dev\clinic-migration\migrate.mjs` (`parseRecord`,
`historyTables`); the in-app import page (`feature/legacy-import-page`) carries the TypeScript twin. Measured on the
first real export: 25 text PDFs, 47 history lines over 15 patients, none dateless.

## Migration

`AddPatientOldSoftwareHistory` — 3 nullable `Patients` columns + one table, two indexes, cascade FK. Purely additive,
no backfill. EF scaffolded an `xmin` column in `CreateTable`; removed by hand (the usual trap).

## Videos in a patient's files (added to the same PR, owner's request)

MP4 / M4V / MOV / WebM are accepted, hosted (up to 150 Mo, the study ceiling), played in the file viewer and given a
thumbnail; AVI is accepted and downloadable only (no browser plays it).

- ⚠️ **`media-src 'self' blob:` had to join all FIVE copies of the CSP** (API middleware, both Caddy sites, the demo
  Caddyfile, the console). The viewer plays the bytes from a `blob:` URL; without the directive the player is black
  **in production only** — a dev server sends no CSP, `worker-src`'s HEIC lesson one directive over.
- ⚠️ **A video is never sent to the coffre** — `No_Browser_Previewable_Format_Is_Sent_To_The_Coffre` holds it: a
  file a browser can show must open from any device.
- ⚠️ **A `.mov` is handed to the player (and to the thumbnail builder) re-typed as MP4.** Chrome refuses
  `video/quicktime` outright, while most .mov files are H.264 in an MP4-shaped box. An iPhone's HEVC .mov stays
  unplayable on a Windows Chrome without the codec — the player's `onError` lands on « téléchargez-le ».
- ⚠️ **The thumbnail is the frame a tenth of the way in (≤ 1 s), built in the browser on the way up**, with an 8 s
  limit — the first frame is often black, and an upload never waits on a codec the browser lacks.
- The preview dialog's ←/→ and sideways swipe change file; both are ignored when they start on the player, which
  owns them (seek).
- Not done: streaming with HTTP range requests. A video is fetched whole before it plays — fine at these sizes
  (≤ 150 Mo), the first thing to add if cabinets start storing long clips.

Verified 2026-11-24 on a side stack with ffmpeg-made MP4 / MOV / WebM / AVI: all four accepted, three thumbnails,
three play (640 px), AVI offers the download, → on the player seeks, 390 px fits.

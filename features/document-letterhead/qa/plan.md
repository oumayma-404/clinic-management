# QA plan — the cabinet's own letterhead (+ date of birth off the documents)

**Written:** 2026-10-06
**Change under test:** uncommitted diff on `feature/security-remediation` (Part 0 + 1 + 2 of `blueprint.md`)
**Rendering files in the diff:** 5 — `components/letterhead/{letterhead-card,letterhead-import-dialog}.tsx`,
`components/clinic-settings.tsx`, `components/document-editor-content.tsx`, plus every server PDF (QuestPDF)
**Blast-radius table:** `progress.md` § Blast Radius (4 rows `must re-test`: #1, #3, #4, #6)
**Budget:** large (money documents + every PDF) → tiers A, B, C, D · 24 scenarios

**Accounts:** admin `salma.benyoussef@cabinet-ibnkhaldoun.tn` (TOTP) · doctor `qa.doctor@ibnkhaldoun.test` (non-admin).
**Clinic:** `f64a8a75-94e1-4e08-a743-fa231abe438f`. **Sources:** `scratchpad/letterhead/` —
`entete-imprimeur.pdf` (vector, FR/AR header + footer), `entete-scan-300dpi.jpg`, `entete-photo.jpg` (900 px),
`notes.txt` (not an image).
**Documents read:** invoice `4974b09e…` · payment `e907d704…` · devis `aa752507…` · plan receipt `c58ef478…`
(plan `eea01758…`) · avoir `5604f44b…` · saved ordonnance `ce3bfc60…` · patient `ceafa5f9…` (Amine Trabelsi).
Every PDF is rasterised with PyMuPDF and the PNG is **read** (eye pass), and its text extracted.

## Tier A — happy path

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| A1 | `/settings` (admin) | open « En-tête des documents » | badge « En-tête texte », button « Importer mon papier à en-tête », a mini A4 page | browser | 1440 |
| A2 | import dialog | pick `entete-imprimeur.pdf` | step « Découpe »: the page painted; « Fin de l'en-tête » between 25 and 60 mm; « Pied de page » switch on, footer 8–30 mm | browser | 1440 |
| A3 | import dialog | « Voir l'aperçu » | step « Aperçu »: a PDF frame; the preview POST answers 200 `application/pdf` | browser | 1440 |
| A4 | import dialog | « Enregistrer » | toast « En-tête enregistré »; dialog closed; badge « Papier du cabinet »; mini page shows the header image (painted) | browser | 1440 |
| A5 | — | read the clinic row | both keys set, under `clinics/{id}/letterhead/{guid}/header|footer`, same guid | sql | — |
| A6 | PDFs (on paper) | render ordonnance (download path), note d'honoraires, devis, reçu (note), reçu (devis), avoir | each page 1 begins with the band edge to edge (image at y≈0, width = page), footer band at the very bottom, no clinic-name text header; money docs keep « Matricule fiscal » only if the clinic has one | api + eye | — |
| A7 | PDF (on paper, long) | ordonnance with 14 lines | 2 pages; the header band AND the footer band are on page 2 as well | api + eye | — |
| A8 | `/documents/prescription` | open the editor | the A4 preview starts with the header image; no « Nom du cabinet » text block | browser | 1440 |
| A9 | editor Word export | « Word » | `.docx` has `word/header1.xml` + `word/footer1.xml` each with a drawing, and a PNG in `word/media` — ⚠️ **corrected after run 2**: « Télécharger Word » is withdrawn app-wide by the owner (`WORD_EXPORT_OFFERED = false`, 85dfd995), so the row is ⏭ until the button returns | browser | 1440 |

## Tier B — alternate paths

| ID | Route / surface | Steps | Expected (observable) | Layer | Widths |
|----|-----------------|-------|-----------------------|-------|--------|
| B1 | import dialog | pick `entete-photo.jpg` (900 px) → Aperçu | ⚠️ **changed 2026-10-07 on the owner's report** (a 449 px real paper was blocked): enlarged, amber « Image de faible résolution (900 px) … » warning, aperçu 200 | browser | 1440 |
| B1b | import dialog | pick a 300 px image | refused on the file step: « Image trop petite (300 px de large) : le texte de l'en-tête serait illisible… » | browser | 1440 |
| B2 | import dialog | pick the PDF, header line to 50 % (keyboard) → Aperçu | refusal « L'en-tête occupe plus d'un tiers de la page … » | browser | 1440 |
| B3 | import dialog | pick `notes.txt` | message « Ce fichier n'est ni un PDF ni une image PNG ou JPEG … »; still on « Fichier » | browser | 1440 |
| B4 | import dialog | keyboard: focus « Fin de l'en-tête », ↓ ×4 | the « En-tête N mm » readout grows | browser | 1440 |
| B5 | import dialog | scan JPEG, switch « Pied de page » off → Aperçu → Enregistrer | saved; SQL footer key null; PDF has a header band and no footer band | browser + sql + eye | 1440 |
| B6 | card | « Retirer » | alertdialog « Retirer le papier à en-tête du cabinet ? » with « Retirer l'en-tête »; after: badge « En-tête texte », SQL keys null | browser + sql | 1440 |
| B7 | `/settings` (doctor) | open the card | no « Importer » / « Retirer »; « Modifiable par un administrateur. » | browser | 1440 |
| B8 | API | forged `letterheadHeaderKey` in a `generate-pdf-download` body while the clinic has no letterhead | the PDF carries no band (text header present) | api + eye | — |

## Tier C — edge cases

| ID | Case | Steps | Expected (observable) | Layer | Widths |
|----|------|-------|-----------------------|-------|--------|
| C1 | 320 px | card + import dialog (Découpe) at 320 × 640 | no horizontal scroll on the page; the page image and both footer buttons inside the viewport | browser | 320 |
| C2 | owner's laptop | dialog at 1536 × 730, steps Découpe + Aperçu | footer buttons visible without scrolling the page | browser | 1536×730 |
| C3 | snapshot | save ordonnance (editor) while on paper A, replace with paper B | the saved document's ContentJson keeps A's keys; its PDF still renders a band | sql + api | — |
| C4 | stale version | dialog open; a write moves the clinic version; « Enregistrer » | 409 banner with « Recharger »; after it, « Enregistrer » succeeds | browser | 1440 |
| C5 | « Page entière » | framed paper (`entete-cadre.png`, the owner's 449 px sample) | opens on « Page entière »; page fits above the buttons (C5b); saved with a body key; a 26-line ordonnance carries header + frame strip + footer on every page (C5c); the note d'honoraires too (C5d) | browser + sql + api | 1440 |

## Tier D — regression sweep

| ID | Blast-radius row | Area | Expected (observable) | Layer | Widths |
|----|------------------|------|-----------------------|-------|--------|
| D1 | #1 | every PDF **without** a letterhead | text header at the top as before, 2 cm margins, signature/footer intact | api + eye | — |
| D2 | #3 | saved document re-render (`GET /medical-documents/{id}/pdf`) | 200 PDF; a document issued with no letterhead keeps the text header | api | — |
| D3 | #4 | settings form after a letterhead save | « Informations du cabinet » « Modifier » → « Enregistrer » succeeds (no 409) | browser | 1440 |
| D4 | #6 | date of birth | no generated PDF contains « naissance »; the editor's A4 preview has no « Date de naissance » | api + browser | 1440 |

## Not covered, and why

| Area | Why not |
|------|---------|
| Android WebView / iOS PDF preview | the aperçu uses the existing coarse-pointer fallback (`PatientFilePdfPreview`); no device here |
| A real office printer's 3–5 mm clipping | paper, not software; stated in the blueprint |
| Archive / restore of the bands | `ClinicArchiveScopeTests` holds the declaration; a restore drill is out of proportion |
| Non-admin API refusal | the handler re-checks `IsAdmin()`; covered by `/test-small-feature` unit tests, not worth a TOTP-less token here |

## Tier E — the gaps closed on request (2026-10-07) · driver `gaps.mjs`

| ID | Where | Steps | Expected | Layer | Widths |
|----|-------|-------|----------|-------|--------|
| E1 | import dialog | the clinic row's version moves with no broadcast (SQL), then import → save | saved, no 409 — the dialog re-reads the version on opening | browser + sql | 1536 × 730 |
| E2 | card | save a framed paper on « Page entière » | body key saved; the mini page draws the frame strip | browser + sql | 1536 × 730 |
| E2b | document editor | open an ordonnance | A4 preview: header band, frame strip behind the text, footer band | browser | 1440 |
| E3 | import dialog, tablet | 820 × 1180, coarse pointer, touch drag on « Fin de l'en-tête » | the line moves (readout grows) | browser (CDP touch) | 820 |
| E3b | same | measure every control | ≥ 44 px (a Button's `.touch-target` ::after counts) | browser | 820 |
| E3c / E3d | same | page + dialog | no horizontal scroll; the whole page fits above the buttons | browser | 820 × 1180 |
| E4a–d | import dialog | landscape page · cream paper · watermark (« Page entière » by hand) · two-page PDF | each previews 200 on one page; the cream band prints WHITE (corner pixel ≥ 250); the watermark survives; page 2 of the PDF is never used | browser + eye | 1440 |
| E5 | API | step-up → `GET /backup/archive` | the zip holds the 3 letterhead blobs and the clinic row names them | api | — |
| — | `verify-schema` | run after both migrations | no letterhead drift; the 5 drifts found were all in the 3 October report already | console | — |
| — | LAN storage | `LocalDiskFileStorageTests.A_Letterhead_Upload_Lands_In_Its_Own_Folder_And_Reads_Back` | three bands land in nested folders on disk and read back | unit | — |

Out of reach, still: a physical printer, and a restore drill on the shared database (the restore is additive — it only
re-inserts missing rows — so a letterhead travels with the clinic row; the export half is E5).

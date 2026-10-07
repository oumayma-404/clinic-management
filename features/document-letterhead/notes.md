# The cabinet's own letterhead on every document (+ date of birth off the documents)

Shipped 2026-10-06 on `feature/security-remediation`. Asked for by several cabinets: « make the entête look like the
doctor's usual one ». Design: `blueprint.md` (option « bandes haut et bas », chosen by the owner). CNAM forms are out
of scope. QA: `qa/run-1.md` → `qa/run-3.md` (GREEN).

## What it does

- An admin imports the cabinet's real paper once — **PDF from the printer, PNG or JPEG scan** — in « Paramètres →
  En-tête des documents ». Two lines cut it into a **header band** and an optional **footer band**; an aperçu shows a
  sample ordonnance rendered by the real renderer; « Enregistrer » stores both bands.
- Every document then prints on that paper: ordonnance, examens, certificat, liaison, note d'honoraires, devis, reçus,
  avoir. Bands at true scale, edge to edge, on **every page**; content keeps its 2 cm side margins between them.
- **No letterhead = exactly what every document printed before.** Same A4, same 2 cm margins, same text header.
- « Retirer » goes back to the text header.
- **The date of birth is off every document** (owner's call, 2026-10-06), joining sexe and poids.

## « Page entière » (added 2026-10-07, on the owner's report)

- A framed paper cut into two bands printed its frame **broken** — the sides stopped where the bands did. The owner
  asked for the whole page as an option.
- **The page is cut into THREE bands**: header, footer, and the strip between them (`LetterheadBodyStorageKey`). Every
  page draws all three; the strip goes behind the text (`page.Background()`), stretched to exactly the space the two
  bands leave. **Only the strip stretches** — it holds frame lines or a watermark, never text — so a paper that is not
  quite A4 (the owner's was 449 × 613) never distorts its entête. A single full-page image with stored margins was the
  other design; it needed two numeric columns, and stretching it would have distorted the text.
- The same two lines still say where the text goes. The dialog's choice « En-tête et pied de page / Page entière » is
  **preselected from the paper**: a column inked down most of the page is a frame (`bands.ts`), and it is also ignored
  when guessing the lines — before, the frame counted as ink on every row and the footer guess fell to 86 mm.
- The strip has no height cap (it is stretched), but the same 1 240 px floor. It travels like the other two keys:
  snapshot (`letterheadBodyKey`), the five money models, the archive scope, `OrElse` keeps the three as one upload.
- **Small images are enlarged, not refused** (same day): a cabinet's only copy of its paper is often a 450 px image
  from WhatsApp, and the 1 240 px refusal blocked the import outright. The browser enlarges it smoothly to 2 480 px and
  shows an amber « Image de faible résolution » line; only below 400 px (text unreadable) is it refused. The server's
  1 240 px floor stays, so only a hand-made request meets it.
- Suggested lines are clamped to the server's caps (⅓ / ⅙ of A4), so a guess is never refused.
- **The paper's own colour is whitened, not just near-white** (gap pass, 2026-10-07): a cream stationery cut into
  bands printed beige bars at the top and bottom of a white page. `bands.ts` takes the paper colour as the median of
  the page and turns anything within 40 of it white; a dark full-bleed page (median luminance < 200) is left alone.
- **The cut page is sized by the dialog's real height**: the sheet fills the screen on a phone, but from `md:` the
  dialog is capped at 85dvh, so sizing on 100dvh clipped the footer line on a tall tablet (820 × 1180). The rule lives
  in a CSS variable (`--lh-h`); ⚠️ inside a Tailwind arbitrary property the spaces of `calc()` must be written as
  underscores, or the value compiles to nothing and the page renders 0 px wide — which is what the first try did.

## Decisions that are easy to undo by accident

- **Bands, not a full-page background.** A full page breaks on page 2 and hides content behind a scan's furniture.
  The bands are the only part of the paper that is the cabinet's identity.
- **True scale.** A band's pixel width IS the page's 210 mm, so the entête lands exactly where it sits on the real
  paper. Hence the rules: ≥ 1 240 px wide (150 dpi — below it printed text blurs), header ≤ ⅓ of the page, footer ≤ ⅙.
  `LetterheadRules` holds them; the browser applies the same numbers before uploading and the server repeats them.
- **PNG only on the wire** (door `letterhead-band`). The browser tool always encodes PNG; JPEG's ringing around
  printed text is exactly the amateur look this exists to avoid. QuestPDF draws it at 300 dpi with no lossy re-encode.
- **The browser does the image work** (`web/lib/letterhead/`): pdf.js renders the printer's PDF to a 2 480 px canvas,
  the lines are suggested from where the ink is, a scan's grey paper is whitened, the band is encoded and stepped down
  until it fits the door. The server only validates and stores — no image library on the API.
- **Versioned keys, never deleted.** Each save writes `letterhead/{guid}/header|footer`. The superseded band stays:
  medical documents **snapshot** the keys they were issued with (`PractitionerRenderSnapshot` reserved keys), so an
  old ordonnance keeps its paper. « Retirer » deletes no blob either. Only a save that **failed** deletes what it had
  just written. Archive/restore carries both keys (`ClinicArchiveScope`).
- **Medical documents snapshot, money documents read live.** Money documents keep no snapshot of anything (the
  existing rule for their identity block), so a note re-printed after a change of paper uses the new one.
- **Pre-letterhead documents keep their text header.** A document issued before the cabinet had a letterhead has no
  key in its snapshot, so it re-renders exactly as it was issued.
- **The two bands are one letterhead.** `OrElse` never pairs a header with another upload's footer, and a footer is
  never written without its header.
- **With a letterhead, the money documents print only the matricule fiscal** in their identity block — the paper
  already says who the cabinet is; the matricule is a fiscal mention letterheads rarely carry and a note must.
- **The reserved keys are server-written only.** The PDF job dereferences them without a user, so a client could
  otherwise point a document at another cabinet's blob. `generate-pdf-download` strips them from the body and
  overlays the server's.
- **One page geometry owner.** Every `container.Page(…)` goes through `DocumentPage.Compose`; a page that sets its
  own margins silently ignores the letterhead. `LetterheadRenderTests.Every_Page_Of_The_Renderer_Goes_Through_DocumentPage`
  holds it (red-proofed on two mutations).
- **An unreadable blob degrades, never fails.** Header unreadable → text header; footer unreadable → header band only.
- **Own read, not a `ClinicDto` field.** `ClinicDto` is built in 7 places; `GET /clinics/letterhead` returns
  `HasHeader`, `HasFooter`, a `Revision` (a hash of the keys — the browser's cache buster) and the row's `Version`.
- **The Word export draws the bands too** (`web/lib/letterhead/word.ts`, floating images in the header/footer), but
  « Télécharger Word » is withdrawn app-wide by the owner (`WORD_EXPORT_OFFERED = false`), so that path is compiled
  and unreachable until the button returns.

## Traps found while building it

- **The suggested cut line skipped the entête's underline** (QA F3, two cycles). The page was shrunk to a 240 px
  probe with the canvas's default low-quality sampling, which skipped a 0.56 mm rule entirely — the same algorithm
  with area averaging finds it. Fixed: an 800 px probe, `imageSmoothingQuality = "high"`, and an ink threshold
  relative to the paper's own shade. The suggestion also takes the **longest** blank run under the ink, not the first.
- **The import dialog reset itself on every `clinics` broadcast** (F1): its reset effect depended on the version prop,
  which every broadcast changes — so a colleague's save threw away the open import and the 409 « Recharger » could
  never be reached. It resets only on the closed → open edge now.
- **`/settings` collapsed every card on a broadcast** (F2): `loadClinicData` set the page loader on every reload, not
  only the first. Fixed for the whole page, not just this card.
- **pdfjs-dist 5.6.83–6.2.108 carries a high advisory** — pinned to 6.4.299. Its worker is served same-origin.

## Found, not fixed (outside this feature)

- « Supprimer le logo » only clears local state (`clinic-settings.tsx`).
- The doctor profile's `Version` is sent but never bound (`UpdateDoctorProfileRequest.cs`).
- The cachet is not frozen per document.
- The QA clinic's name is blank in the dev database, so text-header documents print « [Nom du cabinet] ».

# Task: an interactive DICOM viewer in the clinic app

> Paste this whole file as the opening message of a new session.

Build a real DICOM viewer — window/level, zoom/pan, frame scrolling, measurement — for
`C:\Users\Oumayma Benkhalifa\Desktop\clinic-management`. Today a `.dcm` renders as a **single flattened
PNG** and that is the whole capability.

**Read `CLAUDE.md`, `web/CLAUDE.md`, `web/components/CLAUDE.md` and `.claude/rules/frontend-web.md`
first.** The frontend rules file is binding, not advisory.

## What already exists — do not rebuild it

`features/clinic-file-decoders/notes.md` is the record. In short:

- **`web/lib/files/decoders/dicom.ts`** — `decodeDicom()`, over `dicom-parser` (already a dependency).
  Tags are named in a `TAG` const. It handles: raw little-endian (`1.2.840.10008.1.2`, `.1.2.1`,
  `.1.2.1.99`) and browser-decodable encapsulated JPEG (transfer syntaxes ending `.50` / `.51`).
  **Big-endian is deliberately excluded.** The greyscale path is: signed read → rescale
  slope/intercept → window (the file's own `WindowCenter`/`WindowWidth`, else derived from min/max) →
  `MONOCHROME1` inversion.
- **`web/lib/files/decoders/raster.ts`** — `MAX_EDGE = 2560`, `MAX_PIXELS = 60e6`, `fitWithin`,
  `encodeRgba` (goes through `ImageBitmap` to dodge the silent canvas-area cap), and the
  `DecodedImage` interface (`advisory?: string`).
- **`web/lib/files/decoders/advisory.ts`** — `DICOM_ADVISORY` (used on the fast path, deliberately
  hedged) and `dicomAdvisoryFor(frames)` (used by the decoder, exact).
- **`web/components/patients/files/use-file-preview.ts`** — the preview hook. It has a **fast path**:
  a decodable file with a stored stand-in paints the stand-in in ~300 ms, and the full decode is
  behind a « Pleine résolution » button. `stage` distinguishes the two waits.
- **`web/components/patients/files/file-preview-dialog.tsx`** — four render branches
  (`image` / `pdf` / `archive` / placeholder), plus an amber `role="note"` advisory strip that sits
  **outside** the scrolling pane.
- **`web/components/patients/files/file-kind.ts`** — `previewMode()` → `"image" | "pdf" | "decode" | "none"`.

## Two traps that will cost you a day each

1. **`readEncapsulatedImageFrame` throws when the basic offset table is empty** — which is the
   *ordinary* case. `dicom.ts` already branches to `readEncapsulatedPixelDataFromFragments`. Keep it.
2. **A decoder needing a `blob:` Worker fails only in production.** `worker-src` is inherited from
   `default-src 'self'` unless declared, and the dev server sends no CSP at all. The policy exists in
   **four byte-identical copies** (`SecurityHeadersMiddleware`, both Caddy sites,
   `console/next.config.ts`), held together by `ContentSecurityPolicyAgreementTests`. If you add a
   library that spawns a worker, all four move together.

## Where the bytes come from — this is not one path

`PatientFileDto.residency` is `Hosted` or `Vault`. A study over **25 Mo** on a hosted deployment never
reached the server: it lives in the cabinet's coffre and is read from the paired directory handle via
`findVerifiedInVault` (see `use-file-preview.ts`'s `sourceBytes()`). Asking the server for one can only
404. A CBCT is exactly the file this viewer is for, so **both paths must work.**

## Test files

`follow-up/decoder-samples/` (binaries are gitignored; `build-dicom-sample.mjs` regenerates them).
Real files, already there:

| File | Why it matters |
|---|---|
| `radiographie-thorax-mono1.dcm` | real CR, **MONOCHROME1** — inversion bugs show here and nowhere else |
| `coupe-ct-512.dcm` | 512², signed, rescale slope/intercept — the windowing case |
| `irm-cerebrale-256.dcm` | 256², different value range |
| `etude-16-images.dcm` | **16 frames** — the frame scrubber's only real test |
| `coupe-jpeg-2000.dcm` | encapsulated, **JPEG 2000** — currently *not* decodable; decide explicitly |

## What to design (the real decisions)

- **Window/level by dragging** is the one interaction that makes a DICOM a diagnostic image rather than
  a picture. Vertical = width, horizontal = centre, is the convention. On a **coarse pointer** this
  competes with scroll and with pinch-zoom — that conflict is the hard part, and
  `.claude/rules/frontend-web.md` § 2 and § 11 govern it. This app's primary device is **a tablet at
  the chair, gloved**.
- **Presets**: the standard CT presets (poumon / os / tissus mous) are chest presets. This is a
  **dental** product — decide what a dentist actually needs and say why in the notes.
- **Frame scrolling** for a multi-frame study. 16 frames means 16 decodes: decide whether they are
  decoded lazily or up front, and measure it rather than guessing.
- **Measurement in millimetres** needs `PixelSpacing` (0028,0030) — and a file **without** it can only
  measure pixels. Saying « 12,4 mm » from a guess on a radiograph is the kind of confidently-wrong
  output this repo's rules exist to prevent.
- **Where does it live?** The preview dialog is small and shared by four formats. A study viewer may
  want its own full-screen surface. Justify whichever you pick.
- **The advisory strip stays.** A rendering that looks authoritative and is not is worse than none —
  and it now becomes *more* true, not less, because the user is choosing the window themselves.

## The gate — run it, don't assume it

```bash
cd web
npm run check:responsive      # 30 checks incl. `decoder-extensions-are-in-the-catalog`
npx tsc --noEmit
npm run build
```

Then **look at it** at 320 / 390 / 820 / 1180 / 1440 px, plus a landscape phone. `web/` has no test
runner and `npm run lint` cannot run — that *is* the whole gate. For a signed-in browser walk use the
**`clinic-browser` skill** (stack + TOTP login + the account are all in it).

## Repo habits that will bite

- Never mirror a server rule in TypeScript — a drifting second copy is this repo's dominant defect.
- Branch on a `code` or an enum member's own name, never on French prose.
- An `<input type="file">` must have its `value` cleared *before* the upload runs, or a retry with the
  same file fires no `change` event.
- Put the reasoning in `features/clinic-file-decoders/notes.md` (or a new `features/<slug>/notes.md`),
  never in `CLAUDE.md` — that file is a map and stops working the moment it becomes a changelog.

## Context on why this was deferred

The owner asked « why can't we show the dicom as any dicom viewer? … couldn't that be a great gain? »
and then chose to finish resumable upload first. So this is **wanted**, and it was queued, not rejected.

Concurrently, a session is finishing `features/large-file-transfer` Part 2 (the browser half of
resumable chunked upload). It touches `upload-queue.tsx`, `patient-files-manager.tsx`,
`lib/api/patient-files.ts`, `lib/api/client.ts`, `lib/api/upload-policy.ts`, `lib/api/types.ts` and adds
`lib/files/resumable-upload.ts` + `lib/files/upload-resume-store.ts`. **Rebase before touching those.**

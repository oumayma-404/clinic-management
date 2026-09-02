# clinic-file-decoders — a file you upload is a file you can look at

> The complaint this answers, in the owner's words: *« why would the doctor upload the file to our app only to
> not be able to see it or open it… what's the fucking point? »*

Two facts made that true, and they compounded.

1. **Nothing in the product could decode a format a browser cannot paint.** HEIC — what an iPhone produces by
   default, and what a dentist photographing a case actually has — was accepted by the catalogue, stored
   correctly, and rendered as a grey icon on every machine in the practice. So did TIFF, which is what a
   scanner exports, and a laboratory's ZIP.
2. **No hosted file had a thumbnail at all.** `PreviewStorageKey` was written by the *coffre* registration and
   by nothing else, so on a hosted deployment — the one every clinic uses — `HasPreview` was false for every
   row and a patient's drawer was a column of grey icons whatever was in it.

---

## 1. The decoders, and why they are not a mirror of the catalogue

`web/lib/files/decoders/` — a registry keyed on extension, every decoder behind a dynamic `import()`.

⚠️ **`file-kind.ts` carries a standing rule that whether a format is previewable is the *server's* answer
(AC-5.2), and it still holds.** `FileTypeEntry.IsBrowserPreviewable` says whether a **browser** paints the
format unaided — a fact about the format, unchanged by anything here, and none of its values moved. Whether
*this build* ships a decoder for it is a different fact, about this bundle's module graph, and one the server
cannot know. The two are **unioned** at the point of use (`previewMode`) and never compared, so neither can
drift into contradicting the other.

The one thing that *could* go wrong is a registry key naming an extension the catalogue never accepts — a
decoder that can never run, silently, because the format simply keeps its icon. `check:responsive`'s
**`decoder-extensions-are-in-the-catalog`** parses both sources and fails on it. (Proved red on a deliberate
violation before being trusted green.)

| Format | Library | Notes |
|---|---|---|
| HEIC / HEIF | `heic-to/csp` (libheif) | ~3 Mo, so the dynamic import is load-bearing — verified absent from every route's initial chunk list. |
| TIFF / TIF | `utif2` | Pure JS. **The largest sub-image is chosen, not the first** — scanners put a thumbnail IFD ahead of the real page, and the dimensions are in the tags, so choosing costs nothing. |
| ZIP | **hand-written** | See below. |

⚠️ **`heic-to/csp`, never bare `heic-to`.** The default build evaluates a string as JavaScript, which
`script-src` (no `'unsafe-eval'`) refuses.

⚠️ **The ZIP reader is hand-written, and deliberately.** Every ZIP package on npm is built to *extract*: the
async paths spin a `blob:` worker and the sync paths inflate. Reading an archive's central directory is sixty
lines of a well-specified format, **decompresses nothing** (so no archive can expand into memory), and touches
about 64 Ko of a 2 Go file. It handles Zip64, both name encodings, and normalises `\` to `/` — measured, not
assumed: PowerShell's own `Compress-Archive` writes backslashes, which read as part of the file name.

⚠️ **A canvas has a maximum area and exceeding it fails silently** — Chrome caps at ~268 Mpx and paints a blank
one, no exception. `raster.ts` routes every decode through an `ImageBitmap` (no such cap) and the only canvas
created is at the size `fitWithin` allows.

### The CSP had to move, and it would have failed only in production

libheif runs inside a Worker the library builds from a `blob:` URL. `worker-src` was **undeclared**, so it
inherited `default-src 'self'` and the browser refused it — and a plain dev server serves no CSP at all, so
this works locally and shows a grey icon on the VPS. `worker-src 'self' blob:` is now in all four copies
(middleware, both Caddy sites, `console/next.config.ts`), which `ContentSecurityPolicyAgreementTests` holds
byte-identical. It grants nothing new: `script-src` already carries `'unsafe-inline'`, so anyone who can inject
a script runs it on the main thread and has no need of a worker.

---

## 2. Where the bytes come from depends on residency, and it did not

`useFilePreview` always called `downloadFile`. For a **coffre** file that can only fail — those bytes never
reached the deployment — so the one machine actually holding a 400 Mo study showed « ce format ne s'affiche
pas ». It now reads the original from the paired folder (`findVerifiedInVault`), and a machine with no copy
gets « l'original est conservé au cabinet » plus the way to find it, which is **not a failure**.

The dialog's placeholder now carries a **reason**: `elsewhere` and `undecodable` are opposite facts calling for
opposite actions, and one sentence covered both.

⚠️ The patient page's « Fichiers » tab passed **neither** the policy nor the coffre, so the two surfaces onto
the same drawer disagreed about what could be opened. Both now pass both.

---

## 3. Previews, and the two questions the thumbnail was asking wrong

**Hosted uploads now carry a stand-in image**, built by the browser from the same bytes (`lib/files/preview.ts`,
moved out of `lib/vault/` — where its `decodable()` was `png|jpeg|webp|gif|bmp`, i.e. exactly the set of
formats the coffre never takes, so it returned null every time it was called).

⚠️ Built in the browser, not on the server, and the cost is stated: the browser already holds the bytes and the
codecs and is idle while the user waits, where a server-side pipeline is another dependency, another decoder to
patch, and CPU on a shared host for every upload in every clinic. What it costs is that a file uploaded by an
*older* client carries none — which is what § 4 covers.

`PatientFilePreviewStore` is now **one copy for both doors**; it began inside `RegisterVaultFileCommand`, and
copying it would have given the product two answers to « how big may a stand-in be, and what happens to a bad
one? ».

⚠️ **The hosted handler cleans up BOTH blobs on a failed save.** The preview is written before the row exists
too, so cleaning up one of two leaves an orphan just as surely as cleaning up neither.

### Two live defects in the thumbnail's own gate

`FileThumbnail` paints the **stored preview**, and asked two questions about the **original**:

- `isThumbnailable` required the *original* to be browser-previewable — hiding the thumbnail of every HEIC and
  every TIFF whose stand-in was sitting in the object store ready to serve.
- A `MAX_THUMBNAIL_BYTES = 8 Mo` guard measured `file.fileSize`, left over from when the component downloaded
  the original — so a 40 Mo panoramique with a 200 Ko preview showed an icon to save a download nobody was
  making.

Both are gone. The gate is `file.hasPreview`, full stop.

---

## 4. What about the files already in every clinic?

Nothing stored before this has a stand-in, and on a real database that is *most* rows — which would have made
this whole slice visible only on files uploaded from today.

`DownloadPatientFilePreviewQuery` therefore serves a **small hosted original** where a stand-in should have
been. Three conditions, each doing work: hosted (a coffre original never reached the deployment),
browser-paintable (the tile is an `<img>`, so a PDF is excluded even though the catalogue calls it previewable),
and under `PreviewFallbackBytes` (2 Mo) — this route is called once per tile, so the ceiling is « cheap forty
times over on a clinic's uplink », not « a reasonable file ».

⚠️ **Serving it *here* rather than falling back to the download route on the client is the whole point.** The
download route records an access in the cabinet's journal, so a client-side fallback wrote one « fichier
téléchargé » row per tile scrolled past — which is exactly why the frontend abandoned its own fallback. This
route is exempt by a decision already recorded in it.

⚠️ **The fallback's content type is the row's validated one, not the key's extension.** A storage key carries no
extension (`clinics/{id}/{guid}-{timestamp}`), so deriving it the way a stand-in's is derived answers
`image/jpeg` for every PNG — and with `nosniff` in force the browser paints nothing.

⚠️ **`HasPreview` and the route must agree, and only a test can hold them.** The DTO flag decides whether the
browser *asks*; the route decides what to *serve*. A row the route would serve but whose flag says « none » is
never requested; the reverse draws a tile against a 404. Both go through `PatientFilePreviewPolicy`, and
`A_Row_The_Route_Will_Serve_Is_A_Row_The_Browser_Is_Told_To_Ask_For` pins the pair over eight shapes of row.

`PatientFileResidencyCoverageTests` gained `PatientFilePreviewPolicy` as a named decider — **with a second test
proving each named decider really does branch on residency**, so the list cannot become a way of passing the
guard without answering it.

---

## Verified

- **Backend**: 4072 tests pass, 0 failed. Solution builds with 0 errors, 0 warnings.
- **Frontend**: `tsc --noEmit` clean · `check:responsive` 30/30 (one new, proved red first) · `npm run build`
  succeeds · the 2.9 Mo libheif chunk appears in **no** route's initial manifest.
- **End to end, on real files** (`follow-up/decoder-samples/`, downloaded from the internet): each sample was
  uploaded through the app's own picker to the running API, read back from the database and opened.

| Sample | Thumbnail in the list | Preview |
|---|---|---|
| `photo-intrabuccale-iphone.heic` | ✅ | image 1440×960 |
| `sourire-avant-traitement.heif` (2,4 Mo) | ✅ | image 8192×5491 (capped by `fitWithin`) |
| `radio-retroalveolaire.tif` | ✅ | image 640×480 |
| `cliche-scanner.tiff` | ✅ | image 640×426 |
| `bon-labo-couronne-26.zip` | icon (correct) | listing: 3 entries, exact names and sizes |

- **Eye pass** at 320 / 390 / 820 / 1180 / 1440 + 740×380 landscape: `hOverflow` **0** at every width, zero page
  errors, zero 404s. Coarse pointer asserted in-page (`matchMedia('(pointer: coarse)')`); « Fermer » 44 px.

---

## 5. Eleven seconds, reported from production

The first deployment worked and was **unusable**: « the preview just keeps loading forever … unacceptable
amount of waiting », on `sourire-avant-traitement.heif`.

⚠️ **The obvious explanation was wrong, and measuring is what caught it.** That file is 2,4 Mo and decodes to
**51 megapixels** (8736×5856), so the guess was that the resize and re-encode were the cost. Measured in Chrome:

| stage | ms |
|---|---|
| **libheif's own decode** | **11 038** |
| resize + JPEG encode at 8192 px (what shipped) | 1 171 |
| the `<img>` decoding that JPEG again | 195 |
| resize + JPEG encode at 2560 px | 91 |

Tuning the pipeline could have won 1,2 s of 12,4. The decode is the wall, it is inside libheif, and no amount of
our own code touches it.

### What actually fixed it: don't decode the original at all

Every one of these files **already has a stand-in** — the ~200 Ko JPEG built at upload, the same one the
thumbnail paints. The viewer now shows that first. Measured end to end, click to picture on screen:

| file | before | after |
|---|---|---|
| `sourire-avant-traitement.heif` (51 Mpx) | ~12 400 ms | **342 ms** |
| `photo-intrabuccale-iphone.heic` | ~1 000 ms | **273 ms** |
| `radio-retroalveolaire.tif` | ~900 ms | **305 ms** |
| `bon-labo-couronne-26.zip` | — | **473 ms** |

⚠️ **The original stays reachable** (§ 0 — no capability removed by a performance decision): « Pleine
résolution » runs the decode on request. Deliberately a button rather than a background upgrade, because
arrowing through ten files would otherwise spend eleven seconds and several hundred megabytes each, for a
difference nobody asked to see.

### Three smaller things the measurement exposed

- **`MAX_EDGE` was 8192**, chosen as « inside the canvas limit », which is the wrong question for a dialog a
  thousand pixels wide. At 2560 the encode is 91 ms instead of 1171 and the blob is 1,4 Mo instead of 8,9 — for
  a picture nobody can tell apart.
- **The offer was made where it buys nothing.** Two 640×480 TIFFs offered « Pleine résolution » and produced a
  pixel-identical image: their originals are *smaller* than `PREVIEW_EDGE`, so the stand-in **is** the original.
  It is now gated on the loaded image really being at the cap.
- **The spinner said one thing for two waits.** Fetching a stand-in is a fifth of a second and a decode is
  eleven; a spinner that says nothing for eleven seconds reads as a hung screen. « Décodage de l'image… », with
  a line saying it takes a few seconds on a large image.

⚠️ And the footer's new control had to earn its place: at 390 px it squeezed « Télécharger » from 221 px to
**56 px** — a clipped label on the primary way out, to make room for the secondary control. Its label now goes
below `sm:` exactly as « Supprimer »'s does, with an `aria-label` (a `hidden` span leaves the accessibility tree
too), and **both** icon-collapsing buttons gained `coarse:min-w-11`: `coarse:h-11` alone left them 40 px wide,
four short of the floor, and an overlay would steal the neighbour's taps.

Re-verified at 320 / 390 / 1180: zero footer overflow, zero page overflow, every control 44×44 on a coarse
pointer, and the offer correct in all six cases.

---

## Still open (named, not hidden)

- **The coffre's own path is verified only by construction here.** The dev API does not offer `vaultAvailable`,
  so `panoramique-haute-definition.tiff` (31,6 Mo) and `etude-cbct-export.zip` (34 Mo) in the samples folder
  were not walked end to end — they are the two files that cross the 25 Mo line. Needs a deployment with a
  paired coffre.
- **DICOM** has no decoder. It is the next one worth having, and it needs a « aperçu, non diagnostique » label:
  a wrongly-windowed X-ray is misleading, not merely ugly.
- **Office formats** deliberately have none. Download and open is the correct answer for a `.docx`.
- **A large hosted image still has no thumbnail** unless it was uploaded with one — the 2 Mo fallback is
  deliberate, and a real backfill needs a server-side image pipeline.
- The **coffre threshold itself** (25 Mo) and resumable chunked upload remain the open product questions; this
  slice made the files visible, not the residency line right.

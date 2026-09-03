# dicom-interactive-viewer — a radiograph you can read, not just look at

> The complaint this answers, in the owner's words: *« why can't we show the dicom as any dicom viewer? …
> couldn't that be a great gain? »*

Before this, a `.dcm` rendered as **one flattened JPEG** — frame 0, windowed once by the decoder, at 2560 px on
the longest edge. That is a picture of a radiograph. What a dentist needs is the radiograph: a contrast they
choose, a zoom to the pixel, the other fifteen slices of the study, and a ruler.

It now has all four, on its own full-screen surface, and everything it draws still says it is not diagnostic —
**more** insistently than before, because the window is now the operator's own choice.

---

## 1. One pixel pipeline, two consumers — and this was the whole architectural decision

`decoders/dicom.ts` used to *be* the DICOM implementation: read the pixels, apply the modality rescale, choose a
window, invert `MONOCHROME1`, encode a JPEG. That was right while a DICOM was one stand-in. An interactive
viewer needs the same values with the window **not yet applied**, so the obvious move — a second reader in the
viewer — would have produced a second copy of the signed read, the rescale and the photometric inversion.

⚠️ **That is this repo's dominant defect shape** (a correct rule wired to one call site), and for this
particular rule the failure is invisible: a `MONOCHROME1` radiograph rendered as `MONOCHROME2` is a
photographic negative of itself — bone dark, air bright — which reads as a **finding**, not as a bug. Apply the
inversion *twice* and you are back to the original, which looks right and is wrong beside every
`MONOCHROME2` file in the same drawer.

So the split is:

| module | owns |
|---|---|
| **`lib/files/dicom/study.ts`** | parsing, geometry, scale, the declared windows, and frames **as values** |
| **`lib/files/dicom/window.ts`** | choosing a window, and turning stored values into greys — *nothing else does* |
| `lib/files/decoders/dicom.ts` | now a **consumer**: `openDicomStudy` → `defaultWindowFor` → `renderFrame` → JPEG |
| `components/patients/files/dicom-viewer{,-stage}.tsx` | the other consumer: live windowing, gestures, chrome |

Two things hold it in place, and only one of them is a check:

- **Module privacy holds the *application*.** `buildPackedLut` is not exported. A caller that could build its
  own table could invert a second time, so it cannot.
- **`check:responsive`'s `monochrome1-has-one-owner` holds the *decision*.** Exactly one file may compare
  against a `'MONOCHROME1'` string literal. Derived (it names whichever file it finds, so a deliberate move
  needs no edit) and proved red on a deliberate second comparison before being trusted green. Prose mentioning
  the tag in backticks is fine — only a comparison can disagree.

⚠️ A consequence worth knowing: `decodeDicom` and the viewer's first paint share `defaultWindowFor`, so opening
the viewer on a file cannot appear to change the image before anybody has touched a control.

---

## 2. Lookup table, not per-pixel arithmetic — and the measurement is the reason

Every transformation between a stored reading and a packed RGBA pixel (signed read → rescale → linear VOI →
clip → inversion → alpha) depends **only on the stored value**, of which there are at most 65 536. So they all
collapse into one `Uint32Array` rebuilt per window change, and the inner loop becomes a lookup and a store.

Measured (Node/V8, the same engine as Chrome), per repaint:

| frame | per-pixel arithmetic | lookup table |
|---|---|---|
| 440×440 (`radiographie-thorax-mono1`) | 8,8 ms | **1,8 ms** |
| 512×512 (`coupe-ct-512`) | 12,9 ms | **9,6 ms** |
| 1200×900 (`radio-jpeg-encapsule`) | 10,8 ms | **5,3 ms** |
| 2800×1400 (panoramique) | 36,5 ms | **23,2 ms** |
| 2560×2560 (full-field sensor) | 75,2 ms | **34,1 ms** |

Building the table costs 0,5–0,95 ms, so a window drag is bounded by the paint, not by the maths.

⚠️ **The one endianness-sensitive byte is alpha, and getting it wrong paints a *red* image rather than a wrong
one.** A grey pixel has R = G = B so channel order is immaterial, but `0xff000000` is alpha on a little-endian
machine and red on a big-endian one. Detected once at module load and folded into the table, so the loop stays
a single store.

---

## 3. Frame scrolling: there is nothing to decode, and that is measured

The brief asked whether sixteen frames are decoded lazily or up front. **Neither: on the uncompressed path a
frame is a zero-copy `TypedArray` view into the file's own buffer.** `etude-16-images.dcm` is one 1 Mo
`ArrayBuffer` and sixteen `Uint8Array` windows onto it — verified: sixteen distinct checksums at byte offsets
920 … 983 960, and **all sixteen frames windowed and painted in 8 ms**.

So frames are produced on demand and nothing is cached: pre-rendering all sixteen would spend 8 ms and sixteen
RGBA buffers to save nothing, and on a real CBCT volume (hundreds of slices) it would be ruinous.

⚠️ **The declared frame count is trusted only as far as the bytes go.** A raw file whose header says sixteen and
whose pixel data holds four would otherwise offer a scrubber onto twelve frames of adjacent buffer — which
render as *something*, not as an error. `frameCount` is clamped to what the file can actually hold.

The **encapsulated** path is the one with real work (a JPEG per frame), so there frames are decoded on first
visit and cached, bounded at 8.

⚠️ The window is set from **frame 0 only** and then persists across the study. Scrubbing a series is comparing
slices under one contrast; re-deriving the window per frame would make every step change two things at once.
Per-frame histograms *are* computed and cached, because the data-anchored presets need them.

---

## 4. Presets: what a dentist needs, and why the classic three are absent

⚠️ **There are deliberately no « poumon / os / tissus mous » presets, and the absence is a clinical decision.**
Those are fixed Hounsfield windows and they are only meaningful on values that really are Hounsfield units.
Two facts make that a bad bet in a *dental* product:

1. **CBCT — the DICOM a dental practice actually produces — is not HU-calibrated.** Its grey values shift with
   the machine, the field of view and the reconstruction, which is why the literature calls them « grey values »
   and not HU. A fixed « Os » window would land somewhere different on every cabinet's scanner while *looking*
   like a standard.
2. **The file rarely says.** `RescaleType` is the tag that would authorise reading the values as HU, and of the
   ten real samples in `follow-up/decoder-samples/` — `coupe-ct-512.dcm`, a genuine CT, included — **not one
   carries it** (verified by probing all ten). Detection would therefore come down to `Modality == 'CT'`, which
   a CBCT also reports. That is exactly the confidently-wrong output this repo's rules exist to prevent.

What is offered instead is anchored on **this file and this frame**:

- **the window(s) the file itself declares**, under the device's own name where it gave one. ⚠️ `WindowCenter`
  and `WindowWidth` are **multi-valued**, and reading only the first threw away a real feature:
  `coupe-jpeg-2000.dcm` carries `50\40` / `600\400` with `WINDOW1\WINDOW2` beside them, and
  `irm-cerebrale-256.dcm`'s is named « Algo1 ». The machine that made the picture chose those;
- **« Étendue complète »** — min to max, nothing clipped, the honest default when the file declares nothing;
- **« Contraste renforcé »** — the 2nd to 98th percentile of this frame, which makes an under-exposed film
  readable without asserting anything about tissue.

And the control a dentist actually reaches for is **« Inverser »**, which is not a preset at all: reading a
radiograph as a negative is a routine technique for caries and periapical lesions, and no chest preset provides
it. It XORs with the file's own `MONOCHROME1`, so the two cannot fight.

⚠️ The readout names its own unit — « valeurs stockées », « unités Hounsfield » (only when `RescaleType` says
so) or « niveaux d'affichage » — because printing a raw 16-bit CT's numbers the same way as an 8-bit JPEG's
would claim a calibration that is not there.

---

## 5. Measurement: three states, and « no scale » is the ordinary one

⚠️ **Saying « 12,4 mm » over a file with no `PixelSpacing` is a number invented from a guess**, and the most
realistic sample in the whole set — `radiographie-thorax-mono1.dcm`, a real CR of the same family as a dental
cliché — carries **neither** `PixelSpacing` **nor** `ImagerPixelSpacing`. So « the file has no scale » is not an
edge case, it is the common case for a radiograph. `etude-16-images.dcm` has none either.

| the file carries | the readout says | why |
|---|---|---|
| `PixelSpacing` (0028,0030) | « 12,4 mm » | a distance in the patient |
| only `ImagerPixelSpacing` (0018,1164) | « 12,4 mm **au capteur** » | a distance on the detector; a panoramique magnifies by ~1,05–1,25 and the file does not say by how much |
| neither | « 289 px » | there is no millimetre to state |

The qualifier travels **with the figure**, not in a footnote somebody can scroll past, and the advisory gains a
sentence about the ruler — but only while the Mesurer tool is active or a measurement exists, since it is a
fact about the ruler and the readout already states its own unit.

Scope, stated rather than implied: **one line at a time**, in-plane only, and **nothing is saved**. Both ends
are draggable (22 px grab radius, so it survives a gloved finger), and « Effacer la mesure » appears only when
there is one.

---

## 6. The gesture conflict, which was the hard part

Window/level by dragging competes with scrolling and with pinch-zoom, and this app's primary device is a tablet
at the chair. Three decisions settle it, and none is a heuristic:

1. **The stage never scrolls.** `touch-action: none` + `overflow-hidden`: pan and zoom are the component's own
   transform, so there is no browser scroll for a drag to be mistaken for. § 11 asks that wide content scroll
   in its own container; here the container *transforms* instead, which is the same promise kept differently.
2. **One finger does whatever the toolbar says, and the toolbar says so in words.** A hidden mode — one finger
   windows, two fingers pan — is undiscoverable and unlearnable through a glove. An explicit `radiogroup`
   (« Contraste / Déplacer / Mesurer », over the existing `ui/mode-segmented.tsx`) is how every tablet radiology
   application does it and the only shape that can be *read* rather than guessed.
3. **Two fingers always pinch, whatever the tool — and the tool's own change is rolled back when the second
   finger lands.** ⚠️ A pinch *begins* as one finger touching down and travelling a few pixels before its
   partner arrives, so without the rollback every two-finger zoom would also nudge the contrast. The gesture
   snapshots the window, the pan and the measurement at the first pointer-down and restores all three.

A mouse gets the same tools plus the wheel — one model, not two.

⚠️ **Every gesture reads its baseline out of a ref, never out of React state.** A pinch or a wheel produces
several events inside one frame, and `zoom`/`pan` from the last render are one event behind for all but the
first of them, which reads as a picture that stutters and drifts under the fingers. The pan has a ref mirror
written alongside the state, and each gesture is expressed relative to the values captured when it *began*.

⚠️ **The wheel listener is attached natively with `passive: false`.** React registers `onWheel` passively at the
root, so `preventDefault` there is ignored and the page scrolls behind the dialog instead of the image zooming.

⚠️ **The zoom-about-a-point pan is derived, not approximated.** `origin` folds a centring term that itself
depends on the scale, so a « multiply the offset by the ratio » shortcut drifts a few pixels per step and
visibly walks the image off the pointer over a long pinch.

⚠️ **Smoothing is off past 1:1.** Interpolating a radiograph invents intermediate greys, and a reader zooming to
the pixel is asking to see the sampling.

The **frame scrubber is a range input**, not a third drag axis: vertical drag is already the window's width, and
a slider is the only shape of this control a keyboard and a screen reader can both operate. `globals.css`
already gives every `input` a 44 px floor on a coarse pointer.

---

## 7. Where it lives, and the one thing that had to be suspended

⚠️ **It is its own full-screen surface over the preview dialog, not a mode of it.** The dialog is shared by four
formats and **owns the horizontal swipe** — sideways means « next file », which is exactly the gesture
window/level needs, so the two cannot live in one element without one becoming a modifier of the other. Its
chrome is also fixed and appropriate to a document: header, filmstrip, footer — about 240 px of furniture
around a picture that wants the viewport.

So the dialog keeps its job (a `.dcm` still paints its stored stand-in in ~300 ms and still walks the drawer
with the arrows) and gains a « Visionneuse » button. For a DICOM that button **replaces** « Pleine résolution »
rather than joining it: the viewer opens the original at its own resolution *and* adds the window, the zoom and
the ruler, so the older control would be a second weaker route to the same bytes — and a fifth button in that
row does not fit at 390 px, which is how « Télécharger » got clipped the last time something was added to it.

⚠️ **The dialog's own ←/→ handler is suspended while the viewer is open.** That listener is on `window`, so a
nested dialog cannot stop it from a React handler — and ←/→ in the viewer step a **frame**. Left live, one key
press would step the frame *and* the file underneath, so closing the viewer would land on a different
radiograph than the one it was opened from. Verified in the browser: with the viewer open, three ArrowRights
took the frame 0 → 3 while the dialog's counter held at « 1 / 15 »; on close, one ArrowRight moved it to
« 2 / 15 ».

⚠️ **The viewer fetches its own bytes through the preview hook's new `loadSource`.** The fast path never
downloads the original, so there is nothing to hand over — and the residency rule (a coffre original lives on
the machine that recorded it, and asking the server for one can only 404) must not be written a second time.
That is also why the viewer takes no `vault` prop: the handle stays in the hook, where it already was. A file
this machine does not hold says **where it is**, which is not a failure.

---

## 8. What it refuses, by name

⚠️ **JPEG 2000 stays unsupported, and that is the explicit decision the brief asked for.** A codec is about a
megabyte of WebAssembly *and* a `blob:` Worker — and `worker-src` lives in four byte-identical copies held
together by `ContentSecurityPolicyAgreementTests`, so a missed copy fails **only in production**. That is a real
cost for a format a dental practice rarely exports. What changed is that the refusal now **names it**:
« Les images de ce fichier sont compressées en JPEG 2000, un format que le navigateur ne sait pas décoder.
Téléchargez l'original pour l'ouvrir dans un logiciel d'imagerie. » Big-endian, JPEG Lossless, JPEG-LS, RLE and
the MPEGs are named the same way.

⚠️ **`undecodable-frame` gets a *different* sentence from `unsupported-syntax`**, and the probe over the samples
is why. `radio-jpeg-12-bits.dcm` declares JPEG Extended — a syntax we *do* accept — and the browser refuses the
fragment anyway, because it is 12-bit. Saying « compressé en JPEG, que le navigateur ne sait pas décoder » would
be visibly false to anyone who has ever opened a photograph, so that case says the browser could not decode it
and that a 12-bit JPEG is the usual reason.

⚠️ **Frame 0 is decoded before success is reported.** On the encapsulated path the only way to know the browser
can handle the codec is to hand it a fragment and see. Reporting success and then failing to paint would open
an empty stage with no sentence on it.

Other named refusals: `too-large` (past the 150 Mo parse ceiling), `frame-too-large` (past 24 Mpx — the RGBA
working buffer is 4 bytes a pixel and held for the life of the dialog, so 24 Mpx is 96 Mo, about ten times the
largest real dental frame), `no-pixel-data`, `truncated`, `not-dicom`.

---

## 9. The advisory got *stronger*, not weaker

The flattened preview at least used the window the file declared. In the viewer the operator has moved it, so
what is on screen is a slice of the range **they** picked — and a structure outside it is not dim, it is
**absent**. « I looked and saw nothing » is therefore a statement about the window, not about the patient.

`decoders/advisory.ts` gains `DICOM_VIEWER_ADVISORY` beside the existing `DICOM_ADVISORY`, for the same reason
the module exists at all: one clinical warning, one wording, whichever path produced the picture. It sits
**outside the stage**, so it cannot be panned away from the picture it qualifies.

⚠️ **One correction the browser walk caught.** `DICOM_RENDERED_VALUES_NOTE` first read « il n'y a pas de valeurs
brutes à fenêtrer » — which contradicted a control the reader could plainly still use: an encapsulated-JPEG
frame *is* windowable, it just holds 8-bit display levels rather than sensor readings. It now says the device
already chose the contrast and that the adjustment acts on **that image**, not on the sensor's values.

---

## 10. Devices — measured, at every width in the contract

`npm run check:responsive` (31 checks) · `npx tsc --noEmit` · `npm run build` all clean. Then walked in real
Chrome, signed in, on the real samples, at **both pointer types** — because `coarse:` is a *pointer* query and a
headless browser without touch reports `pointer: fine`, so a 36 px control measured there is the correct desktop
density rather than a defect. Measuring only the first is how a scripted audit produces a wall of false
touch-target findings.

| viewport | picture keeps | controls | touch targets |
|---|---|---|---|
| 320 × 800 | 517 px (65 %) | all reachable (strip scrolls, 440 px hidden) | all ≥ 44 px |
| 390 × 844 | 593 px (70 %) | all reachable (370 px hidden) | all ≥ 44 px |
| **844 × 390** | **256 px (66 %)** | all reachable (column scrolls) | all ≥ 44 px |
| 820 × 1024 | 661 px (65 %) | all on screen | all ≥ 44 px |
| 1180 × 820 | 489 px (60 %) | all on screen | all ≥ 44 px |
| 1440 × 900 | 563 px (63 %) | all on screen | all ≥ 44 px |

No horizontal or vertical **document** scroll at any width. Nothing painted over a control.

⚠️ **The landscape phone was a real defect and the fix is a height query, not a breakpoint.** Stacked, the
chrome came to header 77 + advisory 108 + controls 143 = 281 px of furniture in a 359 px dialog, leaving the
picture **78 px of 390** — which is not a viewer. Below 560 px of viewport *height* the whole thing becomes a
**row**: the chrome moves into a 320 px column beside the image and the stage gets 524 × 256. § 1's table is
about widths and has nothing to say here (an iPad landscape is 1180 px wide and 820 px tall, and wants the
stacked layout); the rule this satisfies is § 0's « usable at a 380 px viewport height ».

⚠️ **That column's width was tuned by measurement, and two plausible values were wrong.** The control strip
*wraps* there rather than scrolling, so the width decides the height. At 240 px (content box 216) every button
took a row of its own — « Inverser » + « Ajuster » do not fit side by side — and the column came to **537 px of
content in a 256 px box**. At 288 px it was 521: 48 px bought 16. At 320 px the buttons pack two-up *and* the
three tool options fit one row instead of stacking, bringing it to **365 px** — about a row and a half below the
fold, visible as a partial row, which is the only honest cue that the column scrolls. Every hidden control was
then verified hittable at 44 px after scrolling it.

⚠️ **The stage is dark in both themes, deliberately.** A radiograph is read against black; a light mount raises
the perceived black point so the bottom of the window stops being distinguishable. It is the one surface in
this app that does not follow the theme, so its overlay ink is fixed to match rather than reading a token that
would invert underneath it.

⚠️ **`relative` on the stage is load-bearing**, not styling: a `static` container does not clip its own
`absolute` children, so the overlays would resolve against the page and make the document taller than the
dialog — § 11's own trap, and what `page-scroller-contains-its-absolutes` exists for.

⚠️ **The floating badge at the bottom-left of every screenshot is `nextjs-portal`** — Next 16's dev indicator,
not app chrome. Confirmed by `elementFromPoint`; it does not exist in a production build. Do not chase it.

### Verified, on real files

| sample | what was checked | result |
|---|---|---|
| `radiographie-thorax-mono1.dcm` | **MONOCHROME1**, measured not eyeballed | mediastinum/spine 184,6 vs lung fields 99,7 → dense **bright**; « Inverser » flips it to 69,4 vs 154,3 |
| `coupe-ct-512.dcm` | the file's own 20/400 window | a correct slice — bone bright, air black |
| `coupe-cbct-hounsfield.dcm` | no declared window, intercept −1024, **no `RescaleType`** | window derived from the range; readout says « valeurs stockées », never « HU » |
| `irm-cerebrale-256.dcm` | a **named** declared window | preset reads « Algo1 » |
| `etude-16-images.dcm` | 16 frames | scrubber max 15; 8 × ArrowRight → index 8; all 16 painted in 8 ms; measures in **px** |
| `radio-jpeg-encapsule.dcm` | encapsulated JPEG Baseline, empty offset table | renders in the browser; window control live; note explains the contrast was fixed upstream |
| `radio-jpeg-12-bits.dcm` | JPEG Extended 12-bit | refused with the « le navigateur n'a pas réussi à décoder » sentence |
| `coupe-jpeg-2000.dcm` | JPEG 2000 | refused **by name** |
| `photo-couleur-rgb.dcm` | raw RGB | renders; window control disabled, no preset select — a colour DICOM has no window to choose |

Zero page errors throughout. (Three `401`s appear in the console on every walk: they are the session probe
before sign-in, unrelated to this feature.)

⚠️ **`dicom-parser` stays out of every route's initial load.** Verified against the production build's
manifests: the chunks containing `parseDicom` appear in no route's entry list, so the 547 Ko parser is still
fetched only when a DICOM is opened. `study.ts` and `window.ts` are plain TypeScript and small, so the viewer
components are imported statically — the dynamic-import discipline exists for libheif-sized dependencies, and
a lazy boundary here would only cost the dialog its open transition.

---

## Still open (named, not hidden)

- **Measurement needs a pointer.** There is no keyboard path to drawing a line, and inventing one (place a
  default line, then nudge an endpoint with the arrows that already step frames) would be a worse control than
  none. The window itself *is* keyboard- and screen-reader-operable — two numeric fields in the « Fenêtre »
  popover — precisely because a drag is not a capability for everyone.
- **One measurement at a time, and none is saved.** Persisting annotations means a schema, a tenant scope and a
  who-drew-this; it is a feature, not a polish item.
- **Angles, areas and Hounsfield readout at a point** are not offered.
- **No multi-planar reconstruction.** A CBCT volume arrives as a series of axial slices and is scrolled as
  such; coronal and sagittal reformats need the whole volume in memory and the slice geometry tags
  (`ImagePositionPatient` / `ImageOrientationPatient`), which nothing here reads.
- **A multi-file study is not a study.** A CBCT export is routinely one `.dcm` **per slice** in a folder or a
  ZIP; this viewer opens one file. The frame scrubber covers a genuine multi-frame file only. That is probably
  the largest remaining gain and it is a different feature — it needs a series concept in the drawer.
- **JPEG Lossless / JPEG-LS / JPEG 2000 / RLE** are still not decoded, now with named refusals. If a cabinet
  turns out to export one, this is where to look — and `worker-src` in four places is the cost to price in.
- **The coffre path is verified only by construction.** The dev API offers no `vaultAvailable`, so a >25 Mo
  study read from the paired folder through `loadSource` was not walked end to end. It is the same
  `sourceBytes` the download and preview paths already use.

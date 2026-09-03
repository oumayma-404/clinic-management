# mesh-interactive-viewer — the scan you can turn, measure and mark

> The owner's ask, verbatim: *« need this STL/PLY/OBJ → interactive: Rotate, Pan, Zoom, Different viewing
> angles, Potentially measurements, Potentially annotations »*.

STL, PLY, OBJ and 3MF have been **accepted, validated and stored** since AC-3.2 — signature rules already
reasoned through, the 150 Mo hosted line, the coffre above it, a generous `vaultMaxBytes`. Nothing about
uploading a scan was missing. What was missing was **looking at it**: `IsBrowserPreviewable: false` and no
decoder, so `previewMode()` returned `"none"` and « modèle.stl » sat beside a grey box. A dentist could not
tell one arch from another, a scan from a finished design, or an upper from a lower.

Three of the four now decode, render as a tile, and open into a viewer with orbit, pan, zoom, seven framings, a
straight-line measurement and surface markers.

---

## 1. The decision that shapes everything else: no mesh format records a unit

⚠️ **A DICOM usually declares its pixel spacing and `lengthCaveat` covers the case where it does not. STL, PLY
and OBJ declare nothing, ever.** They hold bare floats, so the distance between two picked points is a number
in units the file does not name. Dental scanners write millimetres — 3Shape, Medit and exocad all do — which
makes millimetres the right *default* and never a right *claim*: a model exported in centimetres looks
identical and measures ten times wrong.

So three things happen, and **the third is the one that actually works**:

1. The unit is a **control**, not a constant (`millimètres` · `centimètres` · `mètres` · `pouces`).
2. `inferUnit` reports every unit in which the bounding box would be a plausible dental object — 5 mm (a
   prepared die) to 400 mm (an articulated full-mouth model on a base) — and `unitCaveat` grades its sentence
   on the answer: corroborated, ambiguous, or « not a dental size at this scale ».
3. ⚠️ **The model's own dimensions are on screen at all times**, in the chosen unit.
   « Encombrement : 63,0 × 34,1 × 11,8 mm » is an arch; « 6,2 × 3,4 × 1,2 mm » is not, and a dentist knows
   which they are looking at instantly. No sentence this module could write is worth as much as that line,
   because it lets the reader check the assumption against the thing itself rather than trust a heuristic.

The same discipline governs the **view buttons**: they are « Face », « Dessus », « Gauche » — statements about
the bounding box, which are always true — never « occlusale » or « vestibulaire », which the file gives no
basis for. These formats record no orientation either, and dental tools disagree about which axis is up.

⚠️ And a measurement is **straight-line**, and says so. A dentist measuring across an arch often wants the
distance *over the surface*, which is longer; a chord presented without qualification quietly under-reports it.
Geodesic measurement is a much larger piece of work, so the honest move is to name what this is rather than to
approximate what it is not.

## 2. One scene, two consumers — the `monochrome1` shape, applied before it could bite

`lib/files/mesh/scene.ts` owns how a model is lit, coloured and framed. Two things draw these files: the
interactive viewer, and `mesh/thumbnail.ts`, which renders one off-screen frame **on the way up** so the drawer
shows arches instead of grey boxes. Had each built its own scene, a tile and the viewer it opens would light
the same model differently — which reads as the *file* having changed, not as two code paths disagreeing.
`check:responsive`'s **`mesh-scene-has-one-owner`** holds it (proven red).

Three decisions inside that scene are load-bearing:

- ⚠️ **Double-sided.** Intraoral scans are open surfaces — an arch is a shell, not a solid — and many arrive
  with some normals inverted by the export. Single-sided rendering makes those triangles vanish, so the model
  appears to have holes that are not in the file.
- ⚠️ **Matte off-white, never a shiny grey.** A specular highlight sitting in a fissure reads as a feature of
  the tooth. Plaster is what the physical model looks like and it hides nothing.
- ⚠️ **The mesh is centred by moving the MESH, never by rewriting the geometry.** A picked point has to come
  back out in the file's own coordinates — for a measurement, and for an annotation that must survive a
  reload — and baking the offset into the vertices would silently shift every one of them.

## 3. The gesture rule: a drag always orbits, a tap places

⚠️ **Placing a point and rotating the model want the same pointer, and on a coarse pointer there is no modifier
key.** The DICOM stage resolves its version of this by letting the *tool* decide what a drag does. That answer
is wrong here: a model you cannot turn is a model you cannot pick a point on — you have to see the far side to
measure to it. So the split is by **gesture length**: a drag orbits in every tool, a tap under 8 px places, and
a second finger cancels the pending tap so a pinch never ends by dropping a marker.

⚠️ **8 px and not 2**, measured on a finger rather than a mouse: a touch tap routinely travels four or five
pixels between down and up, and a tight threshold makes the tool feel broken on the tablet this product is
used on most.

Markers and the measurement line are **DOM, not scene objects**, positioned by mutating style from the frame
loop. Text in a WebGL canvas is blurry or expensive, unreadable by a screen reader and untappable; and
re-rendering React sixty times a second to move five elements is the jank this avoids.

⚠️ A marker on the far side is **dimmed by a facing test, not by occlusion**. True occlusion is a ray against a
million-triangle mesh every frame. The surface normal answers correctly for a convex shape — which an arch
approximately is — and errs inside a concavity, where a marker may stay bright while technically hidden. It
*dims* rather than hides, so the failure mode is a marker that is too visible, never one that vanished.

## 4. Bandwidth: something can paint an STL now, and that changed a rule

`use-file-preview.ts` documented that « a 150 Mo STL is not pulled across a clinic's uplink to discover that
nothing can paint it ». Something can paint it now — and the bandwidth argument **survived the reason for it**.

⚠️ The preview dialog is a *browsing* surface: whatever it does automatically, it does for every file somebody
arrows past. So `decodesWithoutAsking` stops the automatic decode above **24 Mo** for any format that has a
viewer of its own, and the dialog says « trop volumineux pour un aperçu automatique — ouvrez la visionneuse ».
The reader who actually wants it is one tap from a viewer that fetches the same bytes deliberately and gives
them something better than a still. DICOM gets the same rule, being the same question.

⚠️ Its first draft gated on `decodesToImage`, which is **false for an ordinary PNG** — so it would have refused
the normal path for every plain image in the app. It asks `decoderFor` now: a format with no decoder is not
this rule's business.

## 5. Four defects found by building and looking, none by reasoning

⚠️ **`placeAnnotation` called `setSelectedAnnotationId` inside the `setAnnotations` updater.** An updater must
be pure — React invokes it twice in development — so `crypto.randomUUID()` produced a different id per call and
the marker the list rendered was not the marker the selection pointed at. Symptom: tapping the model placed
**nothing at all**. Found by tapping.

⚠️ **« Dessus » and « Dessous » rendered an empty stage.** Those directions are parallel to the default up
vector, so the cross product `lookAt` builds its basis from is the zero vector and the view matrix is
degenerate. No error, no warning — a viewer that looks like it is still loading, for ever.

⚠️ **The camera framed the bounding SPHERE, so a model used 40 % of the stage.** Sphere-fitting treats every
model as a ball of its diagonal, correct only for a cube seen corner-on; an arch is flat and wide. Fixed by
solving per corner — and the *first* fix, taking `max|up|` and `max depth` independently, was still wrong
because the widest corner and the nearest corner are not the same corner. Measured: 40 % → 63 % → 76 %.

⚠️ **`formatFileSize` had no gigabyte step**, so the storage line read « 9,1 Mo sur 10 240 Mo » while the
server's own refusal sentence said « 10,0 Go » — one number, two units, on screens a user meets minutes apart.
Every *file* is under 150 Mo, so stopping at Mo was right until `large-file-transfer` Part 4 gave the app a
quota in the tens of gigabytes to show. Not a mesh defect; found while looking at a mesh.

## 6. What is deliberately not here

- ⚠️ **3MF stays undecoded.** It is a ZIP of XML rather than a mesh container, so it needs the archive opened
  before anything can be parsed — different work, for the format a dental scanner is least likely to write. It
  remains uploadable, storable and downloadable exactly as before.
- ⚠️ **Parsing is on the main thread.** A worker needs `worker-src`, which is inherited from `default-src
  'self'` unless declared and exists in **four** byte-identical copies — the trap that makes a decoder work on
  a laptop and show a grey icon on the VPS. libheif already spends eleven seconds on the main thread behind a
  spinner; a typed-array walk is far less.
- **Two ceilings, not one.** 128 Mo of file *and* 2 500 000 triangles, because binary STL spends 50 bytes per
  triangle on disk and about 72 in an unindexed `BufferGeometry` — the file size understates the memory by
  roughly half. An arch is 100 k–800 k triangles and a full-mouth study around 1,5 M, so this refuses only the
  CAD export that would allocate a gigabyte on a tablet.

⚠️ **A NaN coordinate is its own refusal**, because it is the one corruption that produces a *blank stage and
no error*: it spreads through the bounding box into the camera fit, and the result looks like a viewer still
loading.

## 7. The WebGL context is the resource that bites

⚠️ A browser allows a small number of live contexts — sixteen in Chrome — and asking for the next one **kills
the oldest**. That is not an error anywhere: the victim is an already-open viewer elsewhere on the page, which
simply goes black. `dispose()` does not release a context; only `forceContextLoss()` does, immediately.

It matters most where it is least visible: the thumbnail renderer builds one renderer **per file** on the way
up, so a dentist dropping a dozen models onto the upload zone reaches the limit inside one gesture.
`check:responsive`'s **`webgl-context-is-given-back`** holds it — and its first red-proof *passed*, because
commenting the call out leaves the text in the file. The check strips comments now, and was then proven red
both by commenting and by deleting.

## Verified

- `npx tsc --noEmit`, `npm run check:responsive` (**34**, two of them new and both proven red), `npm run build`.
- Against the running stack, with generated fixtures whose extent is 63,0 × 34,1 × 11,8: binary STL, an
  ASCII STL, a PLY **with per-vertex colour** (renders pink→white, and its « pas de normales » note fires
  because the file carries none) and an OBJ **holding two objects** (header reads « 3 840 triangles · 2 objets »,
  so the merge path works).
- Thumbnails in the drawer for all three; DICOM tiles unaffected.
- A measurement between two picked points reading « 27,6 mm », with the corroborated-unit caveat and the
  straight-line note both shown only while measuring.
- Two markers placed, renamed (« Limite cervicale 26 ») and deleted.
- Eye pass at **320 / 390 / 820 / 1440** and a **landscape phone (844 × 390)**, where the chrome moves into the
  shared `SHORT_VIEWPORT_*` side column — extracted from `dicom-viewer.tsx` rather than copied, since two
  viewers now open over the file dialog and a copied constant is this repo's dominant defect shape.

## Still open

⚠️ **Annotations do not persist.** They live as long as the dialog does. The viewer already takes them as data
with a stable shape (`MeshAnnotation`: a point in the file's own coordinates, a normal, a label), so
persistence is an entity, a migration, commands, a realtime key and a frontend module — a backend slice, and a
migration is the one change unit tests structurally cannot verify, so it does not belong in the same commit as
a viewer.

⚠️ **A resize does not re-frame.** Rotating a tablet keeps the camera where it was, which preserves a zoom
somebody set deliberately and leaves the model poorly framed until they press « Ajuster ». Preserving was
chosen over re-fitting; « Ajuster » is the escape hatch. The initial fit at mount uses the real aspect, so this
only affects a rotation mid-inspection.

⚠️ **No geodesic measurement**, per § 1.

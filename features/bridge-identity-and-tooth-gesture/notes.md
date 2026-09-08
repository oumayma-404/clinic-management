# A bridge is an object; a gesture is a gesture

Three things the dentist reported about the odontogramme, plus one the model could not say. Shipped
2026-09-08. `plan.md` beside this file is the blueprint and its edge-case pass; this is what shipped and why.

The migration is `20260908122028_AddBridgeIdentityAndImplantPilier` — two additive columns and one index,
**no backfill**, and it was checked for the `xmin` columns EF emits for all 38 `Entity<TId>.Version` mappings
(none, because the snapshot was current) and for a scaffolded `DropColumn` above a backfill (nothing is
dropped and nothing is backfilled). `verify-schema` before and after: the `ToothStates(BridgeGroupId)` drift
it reported beforehand is gone and **no new drift appeared** — the four that remain (`audit-chain-intact`,
`clinic-signup-has-no-orphans`, `messaging-month-covers-every-clinic`, `key-ring-protection`) are pre-existing
and unrelated.

## A bridge's EXTENT cannot be read off the arch either

`BridgeCharting`'s summary has said since the day it was written that a bridge's **shape** cannot be inferred
from position. `odontogram.tsx` then inferred its **extent** from position: join bridge-marked teeth in arch
order while the gap is at most `MAX_PONTIC_SITES = 3`. The same mistake, one level up, and the file that warned
against it was one directory away.

What that cost, measured against the code:

- A bridge on 14·15·16 beside a bridge on 17·18 drew **one five-unit bar**. The report was « sometimes I do
  several bridges that are not tied together, and the app shows one ».
- **Worse, and unreported because nobody would think to look:** the run's `planned` flag was accumulated with
  `existing?.planned || planned`, so the status leaked leftward through the merge. A **finished** crown on 16
  next to a *planned* bridge on 17·18 was drawn dashed-red — asserted as **not yet in the mouth**. On the one
  diagram a dentist reads at a glance, that is a false clinical statement, and it is the half of this defect
  that actually mattered.
- It failed in the other direction too: a bridge charted on abutments four sites apart drew **no travée at
  all**, because the cap that lets a bar cross un-charted pontic sites is also what stops it crossing too many.

So the record states it. `ToothState.BridgeGroupId` is an opaque grouping token — **not a foreign key**, there
is nothing to point at — minted per bridge **act** by `BuildToothStates`, and per **gesture** by the
multi-tooth diagnosis panel for a bridge that is only planned. `web/components/bridge-runs.ts` is the one
owner on the client and runs two passes whose order is load-bearing: grouped rows draw from their group's first
tooth to its last **in arch order**, crossing whatever lies between with no cap at all; every ungrouped row
goes through the original adjacency scan, unchanged.

### Four decisions inside that, each of which was nearly wrong

- **A group is minted only for an act with two or more teeth.** Before `PonticToothNumbers` existed the only
  way to record a three-unit bridge was **the same procedure twice** — `DentalRecordAct`'s own doc says so — so
  fiches are still entered as two acts of one tooth each. Give each of those a group and every one becomes a
  group of one, both are then excluded from the ungrouped fallback, and **the travée disappears from a workflow
  people use**. A one-tooth act asserts nothing about a span, so it must not claim one.
- **One bridge mark per tooth, the newest, resolved *before* grouping.** A tooth accumulates states over time,
  so 16 can carry a `Bridge` from 2024 and a `BridgePilier` from 2026. Grouping the *entries* puts that tooth in
  two groups, both runs claim it, and the merge joins them — the original defect, back on any bridge that was
  ever redone.
- **Grouped teeth are excluded from the fallback's candidate list**, not merely ranked below it, or a legacy
  `Bridge` on 17 reaches into a grouped bridge on 14·15·16.
- **A contested cell takes one run's status, whole** (newest wins), and `planned` is never OR-ed. That single
  operator is what drew the finished crown as still-to-place, and removing it is most of the fix.

`MAX_PONTIC_SITES` **stays**, re-documented as legacy-only. Deleting it with the pass it serves would stop
every pre-existing bridge in the database from connecting. It is also, unavoidably, why two *legacy* bridges
side by side still merge: that information was never recorded, and no migration can invent it — which is
exactly why the migration must **not** backfill.

## The gesture stopped being a mode

`useToothDragSelect` was already correct and complete — range semantics, direction locked once, row-clamped, a
long press on a finger so the arch's horizontal scroll survives. It was wired to **one** call site and gated on
« Plusieurs dents » being on, and the sentence that taught it (« Glissez pour cocher une série ») rendered
**only while the mode was already on**. The one thing that told you the gesture existed was behind knowing it
existed. The report was « it's not noticeable ».

Two changes, and the second is the one that made the first safe:

- **The drag is un-gated on both charts**, reaching the arch through a single optional `dragSelect` prop on
  `ToothArchLayout` — an opaque handle, so that component's « takes no per-tooth state » contract still holds
  and the fiche de soins got the same gesture from one edit. The fiche had **no drag at all** before this,
  which is the repo's signature defect shape: a correct helper wired to one of its call sites.
- **The hook now arms on reaching a SECOND tooth**, not on 4 px of movement. On the odontogramme a tap must
  still open the tooth's editor, and `MOUSE_DRAG_SLOP_PX = 4` meant a hand tremor or a trackpad tap with drift
  would chart-select instead. « Left the tooth it began on » is also the honest semantics: a gesture that never
  leaves its anchor *is* a click.

⚠️ Its companion: `end()` now sets `consumed` on **`applied.size`**, not on `armed`. With the arm rule above, a
long press held still arms and paints nothing, so swallowing the click on `armed` alone made a long press do
**nothing at all** — no editor, no selection, no feedback.

⚠️ **« Plusieurs dents » did not disappear; it changed job.** It is the selection readout (« 3 dents
sélectionnées · Vider ») and **still a real toggle**, for two reasons the comment that stood there had already
argued. A permanent labelled control is what tells somebody who has just charted the same carie on three molars
one at a time that there was a faster way — that argument was always right, and it is the *gate* the owner
overrode, not the affordance. And a drag is **pointer-only**: with the button gone there is no route into
multi-select without a pointer at all, since Space on a tooth opens its editor. The « glissez » hint is now
permanent, on both charts.

⚠️ The two charts are **not** symmetric and must not be « unified ». On the fiche a tap has one meaning — put
this tooth on the armed act — so there is no mode and nothing to disambiguate. On the odontogramme a tap must
also be able to open an editor, which is why the readout survives on that side only.

## The pontique question is now asked, and there are three roles

The roles were already recordable and already charted correctly. What the product never did was **ask**: the
control was a `text-2xs` word inside a tooth chip, so a dentist entering a three-unit bridge had no reason to
think the shape was theirs to state. The report — « you have to select the piliers and the pontiques
separately » — was about a flow that existed and could not be found.

`BridgeRolesStep` in `act-card.tsx` puts the question in words, with all three roles visible at once, and
**« Aucun pontique » is a real answer** rather than a dismissal: a bridge with nothing missing has no pontique,
which is what the product recorded before any of this and is now what the dentist has confirmed.

⚠️ **Always rendered; `bridgeRolesAnswered` decides expanded vs collapsed.** Appear/disappear leaves two bad
options for a tooth added afterwards — re-open (and nag once per tooth while four are tapped) or stay shut (and
silently default the new tooth to pilier, the wrong-default defect this exists to remove). Collapsed to a live
summary instead.

⚠️ **That tri-state is client-side and is never sent.** `ponticTeeth: []` means both « no pontique » and
« nobody was asked », and it must go on meaning both on the wire: `ConditionFor`'s fourth branch keys on *both*
lists being empty to leave every historical row charting exactly as it did. Seeding
`bridgeRolesAnswered = true` in `actFromDto` is the one line that makes a client-only tri-state sufficient — a
saved act has already been through this form, so reopening a fiche must not re-interrogate a bridge somebody
detailed last week.

### « Intermédiaire de bridge sur implant »

`ToothCondition.BridgePilierImplant = 17`, and it earns its own value: `BridgePilier`'s summary already said
« a prepared natural tooth **or implant** », which was the problem. Under the crown they are opposite things —
a natural pilier keeps its root and its periodontium, an implant pilier has a fixture in bone and none — and
« does this abutment have a root? » is what a dentist checks off the chart before touching it. Folded into one
value, an implant-borne bridge was drawn as a rooted tooth.

Recorded as `DentalRecordAct.ImplantPilierToothNumbers`, mirroring `PonticToothNumbers` exactly: one column, no
data migration, and the existing field's meaning untouched for every row. ⚠️ **Three roles fit as « a default
plus two exceptions »; a fourth does not.** Do not add a third list — replace both with a role map and migrate
the two columns. Both the entity and the client type carry that warning.

⚠️ `BridgeCharting.ConditionFor` gained a **required** fourth parameter with no default value. That is
deliberate and it is `TreatmentPlanItemStepInput`'s scar: its fourth argument defaults to null, so a
three-argument copy compiled, read correctly and silently erased an osseointegration wait. A required parameter
makes every call site answer at compile time.

⚠️ The glyph reuses `ImplantFixture`, **extracted** from the `Implant` symbol rather than copied. Two copies of
seven thread strokes and a platform rect would drift the first time either was nudged, and the symptom would be
two teeth on one chart claiming different hardware.

### The gap the fiche's fix did not reach

`MultiToothDiagnosisPanel` charts **one condition across N teeth**, so a *planned* bridge could only be charted
as three piliers or three pontiques — the identical defect the fiche had just fixed, still live on the
odontogramme, which is the surface the dentist was actually looking at. It asks the pontique question too now,
and mints the gesture's group id.

⚠️ **That group id lives in a ref and must survive a partial failure.** The panel posts one tooth at a time —
deliberately, so a failure can re-offer exactly the teeth that did not land — and the retry sends only those. A
fresh id on the retry files the landed teeth under one group and the retried ones under another, splitting one
bridge in two.

## What holds it

- `check:responsive`'s **N30 `bridge-run-has-one-owner`** — derived from the `bridgeSpan` prop, not from a file
  list, so the third chart to draw a travée is covered the day it is written; with a tripwire that fails if the
  prop is renamed, and red-proofed against a deliberate violation before being trusted green.
- `BridgePonticChartingTests` — one test per branch of `ConditionFor`, the pier-abutment and cantilever cases
  unchanged, plus the group rules (two acts in one fiche get two groups; a one-tooth act gets none; a
  non-bridge act gets none; `ToothState` folds away a group it is not entitled to).
- `OdontogramConditionMirrorTests`, which already existed, is what forces a new `ToothCondition` into both
  `CONDITION_ORDER` and `CONDITIONS`. ⚠️ `plan.md` recorded `CONDITIONS` as unguarded and that was **wrong** —
  the mirror test asserts it in both directions, so the extra `check:responsive` work it proposed was
  unnecessary and was not done.

## Verified — and the two defects the browser found

The static gate cannot see a gesture and cannot see a drawing, and both of the defects below passed `tsc`,
all 55 `check:responsive` checks and `npm run build` before a hand touched the app.

### ⚠️ 1. `data-tooth` was emitted only inside the `selectionMode` branch — so the un-gated drag was a deadlock

Measured: **32 tooth buttons, zero `data-tooth`** with the mode off. The attribute is how a drag asks the
document which tooth it is over, and it lived inside `if (selectionMode)` — i.e. it only existed once
« Plusieurs dents » was already on. Once the drag stopped being gated on that mode, that left a circular
dependency: with the mode off there was nothing to hit-test, so the gesture could never paint a tooth, and
painting a tooth is what enters the mode. **The drag did nothing at all until the button was pressed** — exactly
the behaviour the mode was removed to fix, now under a permanent hint promising otherwise. It is emitted in
both branches now.

### ⚠️ 2. The gesture held its anchor as an `Element`, and the anchor's node is replaced mid-drag

Reported from real use, in these words: « *when i select multiple, by hand gesture, it always selects 2* ».

The first painted tooth turns the mode on, which swaps every cell from the editor branch to the checkbox
branch — React **replaces all 32 DOM nodes in the middle of the gesture**. The anchor was then a detached node,
`cells.indexOf(anchor)` returned `-1`, and `applyRange` bailed out for the whole rest of the drag: the range
froze at the anchor plus the one tooth that had triggered the paint, so **every drag selected exactly two teeth
however far it went**. The anchor is a **tooth number** now, re-resolved on every move.

⚠️ Neither `tsc`, nor `check:responsive`, nor a DOM-query probe can see this one — a DOM probe cannot dispatch
a gesture that arms. Only a hand on a mouse finds it, which is what happened.

### A third, smaller one: `runLabel` was computed and never read

`bridge-runs.ts` built « Bridge 14 → 16 · 3 éléments » and `BridgeSpan` declared it; **nothing consumed it**.
An orphaned value, which is one of the four shapes this repo's own notes name. It is in the tooth's tooltip
now, above the charted entries — and the tooltip's gate had to widen to include it, because a tooth the bar
merely *crosses* has no entry and no treatment, so it would have been the one cell in the run unable to say
which bridge it belongs to.

### The wire: 10/10

One fiche with two adjacent bridge acts, over the real API. `bridgeGroupId` on the wire · **two adjacent
bridges carry two distinct groups** · one act charts three roles
(`14 BridgePilierImplant · 15 BridgePontique · 16 BridgePilier`) · an act with neither role list charts its own
condition unchanged (branch 4) · a planned bridge's three teeth share the gesture's client-minted group · a
group sent on a `Carie` is folded away rather than refused · both role lists round-trip out of the read.

### The drawing: 9/9, on the decisive arrangement

Seven **contiguous** teeth: bridge B (18·17, done), bridge A (16·15·14, done), a planned bridge (13·12).
Under the old adjacency rule that was **one run of seven, all dashed**. Measured now, off the rendered SVG:

- three separate runs, each capped — 18 `toNext` only, 17 `toPrev` only; 16 `toNext` only, 14 `toPrev` only;
- **both finished bridges stay SOLID beside the planned one** — the false-clinical-statement defect, gone;
- the planned run is dashed, and 11 · 43 · 47 carry no bar at all (runs do not bleed);
- **the legacy UNGROUPED pair (46 · 44, with 45 blank) still joins across the blank site** — the fallback is
  intact, which is the property every pre-existing bridge in the database depends on;
- terminal ticks drawn at each end.

### The gesture, with real mouse events

A quadrant drag selects **all eight** teeth (11…18) · dragging out and back **narrows** rather than leaving a
tail · a plain click still opens the tooth editor · **a 3 px jitter is a click, not a selection** (the reason
the arm rule changed) · a three-tooth drag selects exactly three at **320 · 390 · 820 · 1180 · 1440** · no
horizontal page overflow at any of those widths.

⚠️ **The probe was the unreliable half, not the app.** Results flipped run-to-run until two probe faults were
removed: a `scrollIntoView` that scrolled the *page* between measuring and dragging, and a `settle()` that
waited for 32 cells when `ToothArchLayout` renders **one arch of 16** below `md:` by design. A finding that
contradicts one which passed minutes ago on unchanged code is a probe bug — that rule earned its place again.
Two further environment traps: the odontogramme sits ~965 px down a 320 px-wide page, so a gesture probe must
scroll vertically first; and every standalone Playwright run rotates the stored refresh token, so a second
browser holding the same state is signed out — refresh immediately before each run and never run two.

### Not exercised — owed, and not claimed

- **`BridgeRolesStep` visually.** `?addRecord=1` alone does not open the record modal, and I ran out of
  patience with the selector hunt rather than the feature. Its *logic* is covered by the four-branch tests and
  the wire pass; its *rendering* at 320 px inside an already-dense dialog is not.
- **The fiche's drag actually painting onto an armed act.** What is verified there is that all 32 cells carry
  the attribute and that the chart correctly refuses a drag while no act is armed.
- **Touch.** No long-press was ever dispatched; the 350 ms arm and the arch's horizontal scroll under a finger
  are unverified, as is `elementFromPoint` under the Android shell's overlays.
- **The odontogramme reloads on every realtime event**, and `load()` sets `loading`, which swaps the arch for a
  loader and unmounts the gesture's container. A colleague's write landing mid-drag therefore loses the
  selection. Pre-existing churn, newly consequential; not touched, and worth a follow-up.

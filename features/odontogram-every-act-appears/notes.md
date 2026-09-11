# Tout acte réalisé apparaît sur l'odontogramme, et « Symboles » dit ce qu'il montre

Reported from use, 2026-09-11, in two halves that turned out to be one defect and one naming failure:

> « on n'a pas de symboles pour les actes réalisés » … « j'ai fait un coiffage pulpaire sur un patient et je ne
> le vois pas sur les symboles »

---

## Nearly half of all recorded work was invisible on the chart, in BOTH drawings

The « Diagnostics » chart read `ToothStateDto[]` and nothing else. A tooth state exists only when the act that
produced it has a `ResultingCondition`, and two ordinary things mean it does not:

- **An act that charts nothing by design.** `ProcedureTypeCatalogSeed` seeds « Coiffage pulpaire »
  `ToothCondition.Sain` with its reason in the file — *« Charts nothing: the barème's own line is à l'exclusion
  de l'obturation définitive »* — and so are « Inlay-core », « Couronne provisoire », « Incision d'abcès » and
  « Greffe osseuse ». `DentalRecordActParser.BuildToothStates` skips `null`/`Sain` before doing anything else.
- **A multi-séance act still under way.** `ToothChartingRules` withholds the end state until the act is `Done` —
  rightly, since charting « Implant » from séance 1 describes a mouth the patient does not have — but the tooth
  then said *nothing at all* for the length of the treatment.

Measured on the dev database:

```
actes nommant des dents et n'écrivant aucun état : 258
actes au total                                    : 566
```

**46 % of all recorded work.** No error anywhere; the tooth simply reads as untouched.

⚠️ **It was never a symbols defect.** « Cases » had the identical hole — it paints `conditionStyle(latest)` off
the same list — so the report arrived against « Symboles » only because that is the view the dentist had
switched to. The tooth carrying the reported coiffage (28, patient *eya gharbi*, two fiches on 2026-09-11) was
blank in both.

The one surface that always had it right is « Actes réalisés », which reads the fiches directly — and
`odontogram-acts-chart.tsx`' own docstring had recorded this exact trap as **the worst of the three defects it
was built to fix**: *« It showed only the acts that change a tooth's state … it looked like a display bug and
was a wrong source. »* That lesson was never carried to the chart beside it. This repo's dominant defect shape,
one more time.

## What it draws now

`web/components/odontogram-recorded-acts.ts` is the one owner of a **second, much smaller vocabulary** —
« something was done here, and it says nothing about the tooth's state ».

- **Symboles**: a filled point at the collet in the « réalisé » blue, mirrored across from `ATraiter`'s
  asterisk. It claims no anatomy, which is the design: the act produced no `ResultingCondition`, so a symbol
  borrowed from the clinical set would assert more than the fiche does.
- **Cases**: the box takes a `green-600` fill *only when the tooth carries no condition at all* — a condition is
  the more specific clinical statement and a box holds one fill — plus a dot in the shared dot row.
- **Both**: a tooltip line naming the act and its date. This is the only place in the product where the chart
  says « Coiffage pulpaire ».

### Why the test is `dentalRecordId`, not `resultingCondition`

Two branches, different certainty:

- an act with **no** `resultingCondition` charts nothing, full stop — no lookup can add information;
- an act that **does** carry one may still have charted nothing, because `ToothChartingRules` clears it *for the
  odontogram* while deliberately leaving it on the stored act (« The fiche keeps the condition the dentist
  chose »). So the honest test is the outcome — did this fiche write a `Treatment` state on this tooth? Every
  state a fiche produces carries its `dentalRecordId`, legacy rows included.

Under-reports in exactly one shape: one fiche, two acts on one tooth, one of which charted. That tooth already
wears a mark from that visit, so the cost is a missing tooltip line and never a silent tooth.

### The four derivations that had to see it too

`dentitionView`'s widening, the « N états hors de cette vue » notice, the phone's default arch, and the
dentition prompt all asked « what is there to show? » from `byTooth`. Left alone, a coiffage on a deciduous 55
of a patient charted « définitive » would not widen the arch, would not be counted by the notice, and would not
decide which arch opens — the one mark on the tooth simply unreachable, with nothing on screen saying so.

`planSeeds`, `bridgeSpans` and the condition legend deliberately do **not** see it: an act already done is not
work to plan, and a bridge is a run of conditions.

### The hue, measured rather than chosen

`emerald-600` was the first pick and is wrong: against the eighteen condition hexes it lands **ΔE 20.6 from
`Bridge`** (teal-500), inside the ~20 band `odontogram-conditions.ts`' « À traiter » note records as « the same
colour at the size this legend draws ». `green-600` takes the nearest neighbour to **ΔE 27.4**
(`RacineResiduelle`, lime-700) and sits 40.8 from `Bridge`.

### The glyph needed a non-scaling stroke, and only looking found it

Every other symbol here is made of strokes, which stay 2.6 CSS px at any scale — which is why they read at the
14 px the legend draws them at. A pure `fill` scales with the viewBox, so the first `r={7}` disc was a **1 px
dot in the key** while being perfectly legible on the 40 px chart: the legend taught a mark the reader could not
recognise. It now carries a `vectorEffect="non-scaling-stroke"` stroke of its own.

---

## « Diagnostics » was the wrong name, and that is the whole of the second complaint

The tab has carried **both** sources since it existed — `ToothStateDto.source` is `Diagnosis` *or* `Treatment`,
and in « Symboles » the colour *is* that axis (rouge à faire · bleu réalisé). Named « Diagnostics » it read as
one half of a pair whose other half is the tab beside it, so a dentist looking for his work in symbols concluded
there were no symbols for les actes réalisés.

- The tab is **« État dentaire »**. The two tabs are two *questions* over one mouth: « dans quel état est cette
  dent ? » and « qu'a-t-on fait, et avec quel acte ? ».
- The symbol legend's colour chips name their **source**, not just their status: « À faire (diagnostic) » ·
  « Réalisé (acte) ».
- « Actes réalisés » carries one sentence and one control pointing at the other tab. ⚠️ **Reworded in Part 2**,
  which made the switch work on that tab too: it no longer says where the symbols are (they are on both), it says
  what the colour means on each.

⚠️ **Splitting the symbols chart in two was the other option offered and it is the wrong one.** Composition is
the entire reason that view exists — a tooth that is dévitalisée *and* couronnée shows both, where a fill can
only ever show the latest state. Split by source, each tab is monochrome and the tooth loses half its history.
Part 2 below drew that out on six teeth and names the four costs; it also corrects one half of the argument I
made here, since splitting *does* free the colour channel.

⚠️ **The Cases/Symboles switch stayed withheld on « Actes réalisés » in Part 1, and Part 2 REVERSES that.** The
reason it was withheld is still true of the code as it stood: that chart drew its own thing and ignored
`chartView`, so the control took the press and changed nothing — « a control that lies is worse than a missing
one ». What the absence produced was worse than the control it prevented, which is the whole of Part 2: the fix
was to make the control true rather than to keep hiding it.

---

## What holds it

`check:responsive`'s **N35 `recorded-act-reaches-both-drawings`** — the derived list must be read inside the
`symbolBox` block *and* the `boxesBox` block of `odontogram.tsx` (found by their own declarations, never by a
line range), and the mark's label and colours may be defined only in `odontogram-recorded-acts.ts`. The failure
mode it exists for is silent: one drawing wired, the other not, and the tooth blank on whichever view the
reader happens to be on — the disagreement `odontogram-view-switch.tsx` forbids in as many words.

Red-proofed both halves before trusting the green: `hasRecordedAct={false}` in `symbolBox` and a second file
declaring `RECORDED_ACT_LABEL` each produced a red run.

## Verified

Gate: `npm run check:responsive` **62/62** · `npx tsc --noEmit --incremental false` **exit 0** ·
`npm run build` green.

One browser pass, one launch, on patient *eya gharbi* (the reported coiffage on 28, plus a traitement de canal
charted by a fiche on 37/38), at **1440×730 · 820×1024 · 390×844 · 320×844**:

| | |
|---|---|
| tab reads « État dentaire » | ✅ |
| tooth 28, Cases — recorded-act fill + two dots | ✅ |
| tooth 28, Symboles — the point at the collet | ✅ |
| tooth 37 — a charted act is **not** double-marked, in either drawing | ✅ |
| tooltip names « Coiffage pulpaire — Réalisé · 11 sept. 2026 », twice (two fiches) | ✅ |
| « Acte réalisé » legend row, both drawings, only when present | ✅ |
| « voir « État dentaire » en symboles » lands on the right tab *and* the right drawing | ✅ |
| no horizontal page scroll at 320 px (`doc 320 / win 320`) | ✅ |

⚠️ Three of the run's first failures were the **probe**, not the product, which is the ratio `verification.md`
§ 2 records: `locator.screenshot` never resolving (the card holds a perpetually-animating child — a clipped
page shot fixes it), an unscoped `[role="tab"][data-state="active"]` matching the patient page's own tabs as
well as the chart's, and an assertion still grepping for `emerald` after the hue moved.

⚠️ **Not exercised, and owed:** a deciduous tooth carrying only a recorded act on a patient charted
« définitive » — i.e. the « N états hors de cette vue » widening. No such row exists on the dev database and
`verification.md` § 7 forbids writing clinical records to satisfy a check. The code path is the same
`teethWithAnything` set the other three derivations read, and it is the one claim here resting on reading
rather than on looking.

---

## Part 2 — « Actes réalisés » gets the drawn teeth, and the switch stops disappearing

Asked for after Part 1 shipped, and it starts from a question I answered too defensively: *« pourquoi pas colorer
la dent avec la couleur du traitement, comme l'autre odontogramme ? »*

### Splitting the symbols chart by status was the other candidate, and it was rejected on drawn evidence

The owner's first instinct was to split « Symboles » into « à faire » and « réalisé », mirroring what they took
the two existing tabs to be. **They are not that**: « État dentaire » carries both sources already, and « Actes
réalisés » is a different *question* over a different source. Drawn out on six teeth
([the artifact](https://claude.ai/code/artifact/19fdbe10-a222-4ec1-9416-b6d6c9dcc226)), splitting costs four
things:

1. a crowned tooth with a new carie is cut across two charts — the reading that most often decides a booking;
2. an act with no `ResultingCondition` has **no glyph and cannot have one**, since the act catalogue is
   clinic-editable and accepts free text, so the coiffage this feature just made visible goes blank again;
3. a multi-séance act in flight belongs to neither chart;
4. four views to navigate instead of two, on a card whose chrome was fought down from 516 px to 265 px.

⚠️ One half of my objection was wrong and is recorded because it will be repeated: « la couleur doit dire le
statut » holds only while both statuses share a chart. **Splitting genuinely frees the colour channel** — the
boxes tabs already work that way. The argument against splitting is the four costs above, not the colour.

### What shipped instead: two channels, two questions

« Actes réalisés » now honours the Cases/Symboles switch and draws the teeth:

| Channel | Answers | How |
|---|---|---|
| **Colour** | which act | the act's catalogue hue, washed into the tooth's own gradients at `--act-tint`, plus one full-strength band per act at the collet |
| **Shape** | what that act left | the condition glyph, in `--chart-mark-ink` |

⚠️ **The wash MIXES the gradient stops; it does not replace them.** The first attempt flattened both stops to
one flat hue, and the owner's reaction was the correct one — « un peu pas professionnel ». What makes the shape
read as a *tooth* is the enamel/dentine falloff, which `globals.css` documents beside those literals; flatten it
and you have a coloured silhouette. The strength is `--act-tint` (22 % / 36 %), the token the agenda's blocks
already use, so « how much of an act's colour survives into a wide surface » keeps one answer — and its own note
(« small marks keep the whole hue ») is why the band is full strength.

⚠️ **`--chart-mark-ink` is a third mark colour and deliberately not a status.** On that chart the colour channel
is spent on the act, so a state drawn in `--chart-mark-done` would claim a status the chart does not sort by, and
one drawn in the act's hue would vanish into the wash behind it. It has **no dark twin**: it is always a stroke
on an ivory tooth, and the enamel literals keep their value in dark.

⚠️ **The state is read from `ToothStateDto`, never from `act.resultingCondition`.** This is the whole safety of
the drawing. `ToothChartingRules` withholds the condition from the odontogram while a multi-séance act runs —
after teeth were charted « Implant » weeks before the implant existed, seven rows on the live database, every one
from a séance 1 of 2. Reading the act row here would put all of it straight back. Reading the states means a
withheld one is simply absent: the tooth shows its act colour and makes no claim about the mouth.

### The switch was withheld for a correct reason, and withholding it caused the report

`odontogram.tsx` gated the Cases/Symboles switch on the tab because the acts chart ignored `chartView` — the
press moved the switch's own state and left the chart byte-for-byte identical, « a control that lies is worse
than a missing one ». That was right while it was true. What it produced was worse than the control it
prevented: a dentist found « Symboles » on one tab, nothing on the other, and concluded there were no symbols
for les actes réalisés. **The fix was to make the control true, not to keep hiding it.**

⚠️ « Symboles » means the same thing on both tabs — *draw the teeth* — while the colour stays each tab's own
question. Each chart states its own key beneath itself, and the switch's `title` hints were rewritten
tab-agnostic (the old one said « rouge = à faire », false on the acts tab).

### What holds Part 2

`check:responsive`'s **N37 `chart-view-switch-drives-every-chart`** — the switch may be offered unconditionally
only while the acts chart really branches on `chartView`. Re-adding the tab gate passes; silently dropping the
drawing fails.

⚠️ **Its first version passed its own red-proof and was therefore worthless.** It tested for
`chartView === "symbols"` anywhere in the file — and deleting the drawing branch left the *legend's* own copy of
that expression standing, so the check stayed green over exactly the edit it exists to catch. It is anchored on
`const cell = chartView === …`, the one line that decides what is painted, the same way N35 anchors on
`symbolBox` / `boxesBox`. Both failure shapes are red-proofed: drawing removed, and prop no longer passed.

### Verified

Gate: `check:responsive` **64/64** · `tsc --noEmit --incremental false` exit 0 · `build` green.

One browser pass, one launch, same patient, at 1440×730 · 820×1024 · 390×844 · 320×844:

| | |
|---|---|
| the switch is offered on « Actes réalisés » | ✅ |
| tooth 28 (coiffage): voile + 2 bandeaux, **0** état drawn — it charted none | ✅ |
| teeth 37/38 (canal): voile + bandeau + the canal in ink, 2 marks each | ✅ |
| 3 of 32 teeth tinted, and **every tinted tooth is banded** | ✅ |
| an untouched tooth stays ivory, unbanded, with no hover affordance | ✅ |
| no horizontal page scroll at 320 px | ✅ |

⚠️ Three of the run's findings were the probe again, and the ratio keeps holding: an assertion still grepping
for a button label I had just changed, one addressing an untouched tooth by a `data-tooth` the acts chart does
not emit (by design — nothing to say, so no affordance), and a screenshot clip truncating a card that another
author's new patient-page section had pushed down.

⚠️ **Not exercised, and owed:** a tooth carrying **two different acts** where only one charted a state — the
band-stacking and the per-tooth (rather than per-act) ink marks are reasoned, not seen. The only two-act tooth
on the dev database carries the same act twice.

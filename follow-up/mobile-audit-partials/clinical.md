# Mobile audit — CLINICAL + PATIENTS slice

Routes: `/appointments` (day/week/month) · `/patients` · `/waiting-list` · `/recurring-series` · `/rappels` ·
`/treatment-plans` · `/documents` · `/fichiers` · `/patients/[id]` (4 tabs) · `/patients/[id]/files`.

Patient used: `460f6b67-8ee8-405d-8f88-0efa2eb88eb4` (Mehdi Bouazizi) — the DB-supplied guid
`1f434ca5-16e1-43c0-b650-688428d2d4f8` belongs to a different clinic than the logged-in session and correctly
renders "Patient introuvable" (not a bug); this one was found by querying `Patients` filtered to the session's
`ClinicId`.

## Calibration note — most `smallTargets` flags are false positives, do not trust the raw count

Every `<Button>` bakes in `.touch-target` (`web/components/ui/button.tsx:22-25`), which on a coarse pointer
overlays an invisible 44×44 px hit area via `::after` (`web/app/globals.css:750-762`) without changing the
painted box. `getBoundingClientRect()` — what the harness measures — only sees the painted box, so a 32 px-tall
`Button` reads as a violation when its real tap target is 44 px. Likewise a patient-card's name link stretches
over the whole card (`after:absolute after:inset-*`, matching `ui/card-list.tsx`'s documented full-card overlay),
so a 24 px-tall link is not the real target either. Odontogram tooth buttons use the other sanctioned pattern —
`coarse:min-w-11` grows the box itself — and measured exactly 44 px, correctly.

Of the ~90 raw `smallTargets` entries collected across every route/width in this slice, only **2 survive** as
genuine violations (both detailed below): the patient page's `tel:` link, and the 320 px-only day-picker cells
in the phone agenda header. Everything else — every `Button`, every pager control, every card-list row link —
already carries `.touch-target`, `coarse:min-w-*`, or a full-card overlay link and is excluded here as a false
positive per that calibration.

## Per-route table

| Route | overflowX @390 | overflowX @320 | smallTargetCount (raw / real) | Verdict |
|---|---|---|---|---|
| `/appointments` (day) | 0 | 0 | 5 / 0 (390), 12 / 7 (320) | clean @390, **degraded @320** (day-picker width) |
| `/appointments?view=week` | 0 | not run | 4 / 0 | clean |
| `/appointments?view=month` | 0 | not run | 5 / 0 | clean |
| `/patients` | 0 | 0 | 18 / 0, 6 / 0 | clean |
| `/waiting-list` | 0 | 0 | 10 / 0, 10 / 0 | clean |
| `/recurring-series` | 0 | 0 | 1 / 0 | clean (retired stub page) |
| `/rappels` | 0 | 0 | 6 / 0, 6 / 0 | clean |
| `/treatment-plans` | 0 | 0 | 3 / 0, 3 / 0 | **broken** (Select truncation, see Finding 1) |
| `/documents` | 0 | 0 | 1 / 0 | clean |
| `/fichiers` | 0 | 0 | 2 / 0, 2 / 0 | clean (one decorative, non-interactive offender — see notes) |
| `/patients/[id]` (medical-records) | 0 | 0 | 29–39 / 1, 29 / 1 | degraded (tel: link only) |
| `/patients/[id]?tab=documents` | 0 | 0 | 39 / 1 | degraded — tab body empty for this patient |
| `/patients/[id]?tab=treatment-plans` | 0 | 0 | 33–30 / 1 | degraded — tab body empty for this patient |
| `/patients/[id]?tab=factures` | 0 | 0 | 45 / 1 | degraded — tab body empty for this patient |
| `/patients/[id]/files` | 0 | 0 | 3 / 0 | clean (empty state — 0 files) |

All routes: `docScrolls: false` everywhere (the page itself never scrolls; only `AppShell`'s own container does),
no `visibleTables` at either width anywhere in this slice (table→card fallback is working), `coarse: true`
confirmed on every read (real 44 px touch measurements, not simulated).

## Findings, worst first

### 1. `ui/select.tsx`'s trigger silently hard-clips its value with no ellipsis — confirmed via computed style

**Route:** `/treatment-plans`, both 390 px and 320 px. **Verdict: broken.**

The "Période" filter shows "Cette semaine" as **"Cette semain"** (390 px) or **"Cette sei"** (320 px) — the tail
is simply cut off mid-glyph, with no `…`. Screenshots: `390-treatment_plans.png` and `320-treatment_plans.png`
(cropped confirmation in `_crop390b.png`/`_crop320.png` in the shot dir).

Confirmed via computed style on the live page (not guessed from the screenshot):
```json
{ "text": "Cette semaine", "display": "flex", "overflow": "hidden",
  "webkitLineClamp": "1", "webkitBoxOrient": "vertical",
  "textOverflow": "clip", "whiteSpace": "nowrap", "width": 63, "scrollWidth": 105 }
```
Root cause, `web/components/ui/select.tsx:42`:
```
*:data-[slot=select-value]:line-clamp-1 *:data-[slot=select-value]:flex *:data-[slot=select-value]:items-center …
```
`line-clamp-1` needs `display:-webkit-box` for the native ellipsis-on-clamp rendering to fire; the same element
also carries the `flex` utility (`display:flex`, added so the value can host a leading icon+gap), and in the
generated stylesheet `flex` wins the cascade. The clamp's `overflow:hidden`/`-webkit-line-clamp` still clip the
text, but the ellipsis never renders (`text-overflow` stays the initial `clip`). Net effect: every `Select` in
the app silently swallows the tail of its value with zero indication, whenever the trigger is narrower than the
label — which is exactly the case at `/treatment-plans` because its filter card is `grid grid-cols-2` (unprefixed,
`app/treatment-plans/page.tsx:201`) with each `SelectTrigger` at `w-full` below `sm:` (`page.tsx:205,220`), so
"Cette semaine"/"Personnalisé" and the longer statut labels never fit their half-card column.

This is the shared `Select` primitive, so it is not scoped to this one page — any other narrow trigger with a
long label elsewhere in the app would reproduce it. Checked the other Selects reachable in this slice
(`/rappels` canal filter, `/fichiers` sort, `/waiting-list` priority, `/appointments` praticien filter) via the
same `scrollWidth > width` measurement at 320/390 px and none of them clip — their labels happen to fit their
triggers. `/treatment-plans` is the one confirmed instance in this slice, and it is exactly the "filter card"
surface the user complained about.

**Suggested fix (not applied, read-only audit):** drop the `flex` variant from the value span (an icon-in-value
Select can wrap its icon+label in an inner `<span className="flex items-center gap-2">` instead of putting
`flex` on the clamped element itself), or add `text-overflow: ellipsis` explicitly rather than relying on
`-webkit-line-clamp`'s own rendering.

### 2. Day-of-week picker cells are ~37 px wide at 320 px — under the 44 px floor, on the width axis only

**Route:** `/appointments`, **320 px only** (clean at 390 px — this is a 320-specific defect, the width the repo's
own rule calls out as the one 390 misses). **Verdict: degraded.**

Measured: `{"tag":"BUTTON","h":61,"w":37,"cls":"flex min-h-[52px] flex-col items-center rounded-lg pb-1 pt-0.5", "label":"lundi 24 août"}` — seven of these (one per weekday) in the phone agenda header's mini week-strip.

Source: `web/components/agenda-phone-header.tsx:398,412-414`:
```tsx
<div className="mt-1.5 grid grid-cols-7 gap-0.5 px-2">
  …
  {/* 58 px, not the 64 it painted: … The tap target stays 14 px past the § 2 floor. */}
  <button … className="flex min-h-[52px] flex-col items-center rounded-lg pb-1 pt-0.5">
```
The author's own comment reasons carefully about the **height** floor (`min-h-[52px]`, confirmed ≥44 px — not a
defect) but the class list has no `min-w-*` and no `coarse:` width rule at all, so the **width** floor was never
addressed. At 320 px, seven equal `grid-cols-7` columns inside the `px-2` gutter net ~37 px each; at 390 px the
same math clears ~44 px, which is why the harness never flagged it there and why this is the kind of defect
390 px alone would hide. Not a stolen-tap risk (cells are contiguous, no overlapping pseudo-elements to fight
over), but it is a confirmed sub-floor tap target with no compensation, on the one device width the repo's own
rule (§ 0: "320 px, not 390") exists to catch.

### 3. Patient phone number is a bare `tel:` link with no touch-target treatment

**Route:** `/patients/[id]`, both widths (appears on every tab, since it's in the page's persistent header above
the tabs, not tab content). **Verdict: degraded** (isolated, minor, but a real unguarded control).

Measured: `{"tag":"A","h":20,"w":91,"cls":"font-medium text-foreground underline-offset-2 hover:underline","label":"+21692110877"}` — no `touch-target`, no `coarse:` sizing.

Source, `web/app/patients/[id]/page.tsx:1048-1054`:
```tsx
<a href={`tel:${patient.phoneNumber}`} className="font-medium text-foreground underline-offset-2 hover:underline">
  {patient.phoneNumber}
</a>
```
This is exactly the "isolated small control" case § 2 of `frontend-web.md` prescribes `.touch-target` for
(`<Button size="icon" className="touch-target" />` is the sanctioned pattern) — every other icon-only or
isolated control in the app gets this treatment; this one plain link does not. 20 px tall, no neighbor stealing
risk (it is not in a row with other targets), so the fix is simply adding `touch-target` to the `className`.

## Verified NOT bugs (checked, ruled out — recorded so nobody re-flags them)

- **Odontogram/tooth-arch chart's wide "offenders"** (`/patients/[id]`, both widths): the harness's element-level
  overflow probe flags ~61–71 elements whose `right` extends past the 390/320 px viewport (e.g. `right:803` for
  the "Maxillaire (haut)" row). This is **by design and already load-bearing**: `web/components/tooth-arch-layout.tsx:62-69`'s
  docstring explicitly documents `mx-auto w-max` (never `justify-center`) inside its own `overflow-x-auto` box, so
  that teeth 18–15 stay reachable by scroll — the exact fix for a real defect this repo already shipped and now
  guards with an `arch-clipping` check. Document-level `overflowX: 0` and `docScrolls: false` on every read confirm
  the *page* never scrolls sideways; only the chart's own container does, correctly.
- **`/fichiers`'s one flagged offender**: a `pointer-events-none absolute -right-8 -top-10 … blur-2xl bg-chart-1/25`
  span — a decorative corner-glow on a patient card (documented in `web/components/CLAUDE.md`: "the hue lands on
  a corner glow and the tile"). Non-interactive, bleeds a few px past the card edge by design, does not create a
  document scrollbar (`overflowX: 0`). Not a bug.
- **Two repeating `404` console errors on every route** (`/api/connectivity`): a known, already-handled case per
  `ARCHITECTURE.md`/`web/CLAUDE.md` — the connectivity probe only exists in `SelfHostedLan`, and this environment's
  backend answers 404, which the app is documented to read as "no signal available", not "offline". Unrelated to
  rendering, out of scope for this audit.
- **Session redirects to `/login` on several first-pass runs**: the harness's session token is short-lived in
  this environment (expired mid-run at least 3 times over the course of this audit); each was resolved by
  `node ~/.claude/playwright/refresh-session.mjs` and re-running, per the brief. Not an app defect.

## Coverage

- **Walked**: `/appointments` at day/week/month views, `/patients`, `/waiting-list`, `/recurring-series`,
  `/rappels`, `/treatment-plans`, `/documents`, `/fichiers`, `/patients/[id]` on 4 tabs
  (`medical-records` default, `documents`, `treatment-plans`, `factures`), `/patients/[id]/files` — all at both
  390 px and 320 px, `hasTouch: true`/`coarse: true` confirmed throughout.
- **Empty for this dataset** (layout could not be checked under real content): `/fichiers` and
  `/patients/[id]/files` show 0 files for every patient in the clinic — only the empty-state card layout was
  exercised, never a populated file grid/list. `/patients/[id]?tab=documents`, `?tab=treatment-plans`,
  `?tab=factures` all render an empty tab body for this patient (no invoices/documents/plans on record) — only
  the page's persistent header (name, actions, "à compléter", odontogramme) was exercised with real data; the
  tab-specific content itself is unverified under load. `/rappels`' delivery log shows 0 today but has historical
  WhatsApp-forfait figures (588 sent, 412 remaining) so that card was exercised with real numbers.
- **Retired, not a real screen**: `/recurring-series` renders `ui/retired-page-card.tsx` per `web/CLAUDE.md` — a
  deliberate withdrawal, not a missing feature.
- **Unreachable as first supplied**: the DB-suggested patient guid (`1f434ca5-…`) belongs to a different clinic
  and correctly 404s as "Patient introuvable" — resolved by finding a same-clinic patient instead.
- **Not run**: `/appointments?view=week`/`month` only at 390 px (not 320 — both were clean at 390, and time was
  spent instead on the two real findings above).

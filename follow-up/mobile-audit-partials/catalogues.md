# Mobile audit — Catalogues + Admin slice

Routes: `/stock` `/fournisseurs` `/lab-orders` `/medications` `/dental-acts` `/procedure-types`
`/cnam-nomenclature` `/users` `/settings` `/securite` `/mon-profil`

Harness: `audit.mjs` at 390 px and 320 px, `hasTouch: true` (coarse pointer is real, not simulated).
Session expired mid-run three times during this audit (shared QA session file, refreshed with
`refresh-session.mjs` each time — visible in the raw NDJSON as `redirectedToLogin: true` runs that were
discarded and re-run clean).

## Per-route table

| Route | overflowX @390 | overflowX @320 | smallTargetCount (raw → real) | visibleTables | Verdict |
|---|---|---|---|---|---|
| `/stock` | 0 | 0 | 13 → 0 | 0 (cards) | clean |
| `/fournisseurs` | 0 | 0 | 8 → 0 | 0 (cards) | clean |
| `/lab-orders` | 0 | 0 | 8 → 5 → 0 | 0 (cards) | clean |
| `/medications` | 0 | 0 | 28 → 0 | 0 (cards) | clean |
| `/dental-acts` | 0 | 0 | 32 → 0 | 0 (cards) | clean |
| `/procedure-types` | 0 | 0 | 22 → 0 | 0 (cards) | clean |
| `/cnam-nomenclature` | 0 | 0 | 37 → 0 | 0 (cards) | clean |
| `/users` | 0 | 0 | 5 → 0 (small-target sense) | 0 (cards) | **degraded** (layout, see #1) |
| `/settings` | 0 | 0 | 34/48 → 1 real | 0 (cards) | **degraded** (see #2, #3) |
| `/securite` | 0 | 0 | 1 → 0 | 0 (cards) | clean |
| `/mon-profil` | 0 | 0 | 10 → 0 | 0 (cards) | clean |

`overflowX` is `document.documentElement.scrollWidth − clientWidth`: genuinely 0 on all 11 routes at both
widths — no horizontal escape anywhere in this slice. `visibleTables` is 0 everywhere too: every list in
this slice correctly renders as a card list below its hinge (nothing left in `TABLE_ONLY`/`TABLE_ONLY_LG`
visible at 390/320) — `/lab-orders`, `/stock` and `/procedure-types` are the `_LG`-hinge tables named in
`web/CLAUDE.md` and none of them leaked their desktop table onto a phone viewport.

**Small-target counts are mostly false positives, filtered against source, not just eyeballed.** The
harness's `smallTargets` list flags anything under 44×44 px via `getBoundingClientRect()`, which cannot see
a CSS pseudo-element hit area. I deduplicated every distinct `(tag, class)` pair across all 22 route/width
runs (10 unique shapes total) and checked each one's class string and, where relevant, its parent structure:

- Every `<Button>`-based control (« Actions pour X » kebabs, « Ajouter un article/acte/médicament »,
  « Confirmer les données », « Modifier », the day-of-week `Checkbox`es on `/settings`'s horaires editor,
  the skip-link) carries `touch-target`, confirmed in `web/components/ui/button.tsx` /
  `web/components/ui/checkbox.tsx` and the pseudo-element rule at `web/app/globals.css:750-765`. These are
  real 44 px targets; the harness just can't measure the invisible overlay. Checked specifically because it
  looked like the obvious next candidate: the day-of-week checkboxes on `/settings` and `/mon-profil` are
  each in their **own bordered row** (`web/components/doctor-working-hours-card.tsx:153`, one row per
  weekday, not packed side by side), so they don't hit the "`.touch-target` on close siblings steals a
  neighbour's tap" trap either.
- The `<a>` "Sonia Trabelsi" / "Youssef Mrad" prothésiste-name links on `/lab-orders` (24×105 painted) are
  the `CardList` row-title link, whose `::after` overlay stretches to the whole card
  (`web/components/ui/card-list.tsx:257,281-283`) — the real tap target is the entire card row, not the
  24 px text box.
- The "Aller au contenu" skip link (1×1) is `sr-only` by design; it's a keyboard-only affordance, not a
  coarse-pointer target.
- **One survivor**: see finding #3 below.

## Findings, worst first

### 1. `/users` — the « Créer un compte » button never lands on its own row below the title; it overrides only half of the CardAction grid placement, so it sits at top-right and squeezes the heading

Confirmed by computed style, not just the screenshot (screenshots alone made this look ambiguous — the
button reads as attached at different points across widths). Verified with a small standalone probe script
(kept in scratchpad, not part of the shared harness) that reads `getComputedStyle` on the header, its
`CardTitle` and its `CardAction`.

`web/components/ui/card.tsx:82-92` — `CardAction`'s own base class is
`"col-start-2 row-span-2 row-start-1 self-start justify-self-end"`.
`web/components/user-management.tsx:425` overrides it with
`className="col-span-full w-full sm:col-span-1 sm:w-auto sm:justify-self-end"`, intending — per the comment
right above it — to move the button onto its own full-width row *below* the title on any viewport under
`sm:` (640 px), citing the exact defect this reproduces as something already fixed.

It is not fixed. `col-start-2` and `col-span-full` are different tailwind-merge class groups (one sets only
`grid-column-start`, the other the `grid-column` shorthand), so **both** survive the merge, and normal CSS
cascade order — not the order they're written in the JSX — decides the winner. Measured computed style at
320 px:

```
actionClassName: "col-start-2 row-span-2 row-start-1 self-start justify-self-end col-span-full w-full sm:col-span-1 sm:w-auto sm:justify-self-end"
actionGridColumn: "2 / -1"   (i.e. still column 2, NOT 1/-1 — col-start-2 wins the start, col-span-full only supplies the end)
actionGridRow:    "1 / span 2"   (row-start-1/row-span-2 from the primitive is never touched by the override at all)
headerGridTemplateRows: "96px 0px"  (@320)  /  "64px 0px" (@390)  — row 2 is 0 px: nothing is ever placed there
```

Net effect, reproduced identically at 390 and 320: the button renders top-right, beside the icon chip, in
the *same* row as the title — and the title's own content (icon + "Utilisateurs" + the count `Badge`, and a
pending-activation badge when present) is squeezed into the narrower left column and wraps onto 2–3 lines
underneath the icon. This is close to word-for-word the "action appears above the heading it belongs to"
defect the comment already describes and believes is closed. It reproduces on **every** viewport under
640 px, i.e. every phone. Not "broken" (both controls are still reachable and tappable), but a confirmed
regression against the component's own documented intent — classified `degraded`.

Fix shape: either give the override an explicit `row-start-2` (or `row-span-1 self-start` on its own
`sm:`-gated axis) so it actually lands in row 2, or move the column override to also win outright (e.g.
`!col-start-1` or restructure so the base and override classes are in the same tailwind-merge group).

Screenshots: `follow-up/mobile-audit-shots/390-users.png`, `320-users.png`.

### 2. `/settings` — the doctor's « Spécialité » `Select` clips "Médecin dentiste" with no ellipsis at 320 px (confirmed instance of the cross-slice `SelectValue` bug)

This is the same root-cause bug another agent found on `/treatment-plans` (`web/components/ui/select.tsx:42`
applies both `line-clamp-1` and `flex` to `[data-slot=select-value]`; `flex`'s `display` wins the cascade, so
the clamp's `-webkit-box` never applies and the ellipsis never renders, while `overflow:hidden` still clips).
I checked every `[data-slot="select-trigger"]` on all 11 routes at both widths (`scrollWidth` vs
`clientWidth` on the inner `select-value` node, not just the trigger box) — **in this slice it only fires on
one field**: `web/components/clinic-settings.tsx:1016-1017`, the doctor roster's « Spécialité »
`<SelectValue>` showing "Médecin dentiste".

```
@390: scrollWidth 126, clientWidth 126 → not clipped (trigger 242px wide)
@320: scrollWidth 126, clientWidth 122 → clipped (trigger only 172px wide, no "…")
```

Small (4 px) but real, and notable because the call site *already* has a comment two lines above
(`clinic-settings.tsx:1013-1015`) acknowledging this exact field was too wide for its grid cell at 320 px and
"fixing" it with `w-full` — that fix made the *trigger* fit its cell, but the *text inside* now silently
loses its tail instead of ellipsizing. Every other `Select` in this slice (`Toutes les catégories`, role
selects on `/users`, `Tunis` governorate, `Plus récents`, page-size selects) has enough width headroom in
its trigger that it never clips at either width — so within Catalogues+Admin the blast radius of the shared
bug is **one field, one route, one width**. Root cause is shared, not local; no separate fix needed here
beyond whatever lands at `select.tsx:42`.

### 3. `/settings` — the « Horaires de ce praticien » disclosure toggle is a genuine 32 px target with no touch-target floor

The one small-target survivor after filtering. `web/components/clinic-settings.tsx:1143-1146`:

```tsx
<details className="mt-2">
  <summary className="cursor-pointer py-2 text-xs font-medium text-muted-foreground hover:text-foreground">
    Horaires de ce praticien
  </summary>
```

Measured 32×282 px at both 390 and 320. No `.touch-target` class, no `coarse:` sizing utility. Checked
`globals.css`'s only other mention of `<summary>` (line 812) — that rule is the `:focus-visible` outline
floor, unrelated to touch sizing; there is no automatic coarse-pointer floor for a bare `<summary>` the way
there is for `input`/`select`/`[data-slot=select-trigger]`. This is a real, if minor, § 2 violation: a
native disclosure control under the 44 px coarse-pointer floor. Low severity (it's a secondary disclosure,
not a primary action, and still tappable at 32 px) but confirmed, not guessed.

## Coverage

- All 11 routes walked at both 390 px and 320 px, twice each (once discarded per width due to session
  expiry mid-audit — not a product defect, a shared QA-session contention issue with other concurrent
  audits; re-run clean both times).
- **No route was empty.** `/stock` 9 articles, `/fournisseurs` has suppliers with WhatsApp actions,
  `/lab-orders` 5 bons, `/medications` 25, `/dental-acts` 100, `/procedure-types` 19, `/cnam-nomenclature`
  26, `/users` 3 accounts, `/settings` populated cabinet + 1+ doctors, `/securite` 2FA active with 8 unused
  recovery codes, `/mon-profil` partially filled doctor identity. No 404s, no error screens, no console
  errors beyond two benign 404s on every page load (not layout-related — didn't chase them, out of scope).
- **Not fully covered**: the harness screenshots only the initial viewport — `document.scrollHeight` equals
  `window.innerHeight` on every route in this app (the page itself never scrolls; `<main>` is an internal
  scroll container per `AppShell`), so content below the fold was not captured by a screenshot. I did not
  scroll and re-shoot every route's full length; `/settings`' billing/TVA card, reminders card and backup
  card, and `/users`' full roster below the third row, were not visually inspected beyond what the DOM
  probes covered. If another pass has spare budget, those are the parts of this slice I'd check next.
- Cross-slice patterns worth flagging to whoever aggregates: the `SelectValue` clipping bug (see #2) and the
  `.touch-target` false-positive shape (see coordinator note) both recur outside this slice; nothing else
  in my findings looks like it would recur outside `/users` and `/settings`.

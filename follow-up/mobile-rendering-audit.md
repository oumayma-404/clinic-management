# Mobile rendering audit — 2026-08-29

Phone-first audit of every authenticated screen, run by four parallel agents. Detail lives in
`follow-up/mobile-audit-partials/` (`finances.md`, `clinical.md`, `catalogues.md`,
`dialogs-and-tablet.md`, `click-swallow.md`). Screenshots were written to
`follow-up/mobile-audit-shots/` and are **deliberately not committed** (118 files, 13 MB of derived
evidence) — every finding below is backed by a measurement quoted inline, not by an image.

**STATUS: all findings below are FIXED** (same session, verified in a browser — see « Verification » at the
end). The findings are kept in full because the *reasoning* is what stops them coming back; each now carries
the change that closed it.

## What was covered

- **27 routes** at **390×844** and **320×568**, plus patient detail and its tabs.
- **820×1024** tablet portrait across every main screen (the device this app is used on most).
- **Dialogs, sheets, dropdowns, comboboxes, collapsibles, the notification panel** at 390px.
- **A click hit-test** of every interactive control at 390px.

## Headline

**Zero document-level horizontal overflow, on any route, at any width.** The thing the audit was
originally aimed at does not exist. Every real defect found is either a *functional* failure that
looks fine in a screenshot, or a tablet-width clip.

The two most valuable findings were both invisible to a layout measurement, and both are the same
shape: **a correct fix wired to some call sites and not the rest.**

---

## Findings, most impactful first

### 1. `/lab-orders` — the status control cannot be used on a phone; it navigates to the patient

**Functional, phone-only, reported by the owner.** Tapping « Envoyé » / « Reçu » on a lab-order card
opens the patient page instead of changing the status.

`CardList` stretches the card title's link across the whole card (`after:absolute after:inset-0`,
`web/components/ui/card-list.tsx:218`). Three slots are lifted above it with `relative z-10` —
`leading` (:200), `actions` (:257), `primaryAction` (:283). **`fields` is not** (the `<dl>` at
:259-278). `/lab-orders` puts its « Stade » `<select>` in `fields`
(`web/app/lab-orders/page.tsx:961`) while the card `href` is `/patients/${o.patientId}` (:949).

The comment at `card-list.tsx:281` describes this exact failure *for the slot that got the fix*:
*"without this the button would be under it and untappable."*

Evidence: hit-tested each control's centre at 390px; five cards, five swallowed selects, each
resolving to its own card's patient guid. **The select measures 110×44** — it passes the touch-target
rule and is still unusable, so no size-based check could ever catch it.

**Fix (applied):** in `card-list.tsx`, lift the interactive **descendants** of `fields` with
`relative z-10` via a descendant selector — NOT the `<dl>` itself. Putting it on the list was the
first attempt and it is wrong: it raises the static field text above the overlay too, which kills
tap-the-card-to-open on every list in the app. `actions`/`primaryAction`/`leading` are wholly
interactive, which is why they get the plain wrapper and this cannot.

**Blast radius: swept all 26 routes at 390px — `/lab-orders` is the only reproduction.**
`patient-summary-modal.tsx` also puts a `<Button>` in `fields`; the primitive fix covers it
structurally, but it renders only inside an open modal, which was never opened — **unverified.**

### 2. Eight tables clip their own Actions column at tablet portrait (820px)

**Degraded, recurring, one fix.** Eight files import the `md:` (768px) card hinge instead of the
`_LG` (1024px) pair, so at 820px — with the 256px sidebar still present — the table overflows its
card box and the row's Actions menu is reachable only through an unlabeled inner scrollbar.

| Route | File : line | Clipped |
|---|---|---|
| `/users` | `web/components/user-management.tsx:500,598` | Rôle, Statut, Dernière connexion, **Actions** |
| `/dental-acts` | `web/components/dental-acts-table.tsx:269,327` | Catégorie, Tarif, Statut, **Actions** |
| `/cnam-nomenclature` | `web/components/cnam-nomenclature-table.tsx:250,302` | Catégorie, Statut, **Actions** |
| `/journal` | `web/app/journal/page.tsx:292,328` | Dossier, **Détail** |
| `/waiting-list` | `web/app/waiting-list/page.tsx:489,548` | Date d'ajout, **Actions** |
| `/patients` | `web/components/patients-table.tsx:418,510` | Signalements, **Actions** |
| `/medications` | `web/components/medication-catalog-table.tsx:248,299` | Statut, **Actions** |
| `/rappels` log | `web/components/rappels/reminder-log-table.tsx:162,196` | Prévu, **Statut** |

Ten other tables already carry the `_LG` hinge and are clean. `web/CLAUDE.md` documents this exact
fix and names the tables that got it; `suppliers-table` records having fixed this precise symptom
("the Actions column — the WhatsApp button — sat off screen at 820 px"). **Swap the import and two
JSX props on eight files — one fix, not eight investigations.**

### 3. `SelectValue` clips its text with no ellipsis

**Shared primitive.** `web/components/ui/select.tsx:42` applies `display:flex` (for icon support),
which overrides `line-clamp-1`'s required `display:-webkit-box`. The clamp's ellipsis never fires
while `overflow:hidden` still cuts — so the value is silently truncated mid-word with no "…".

Confirmed via live computed style. Observed on:
- `/treatment-plans` filter — "Cette semaine" → "Cette semain" (390px) / "Cette sei" (320px)
- `/settings` doctor Spécialité — "Médecin dentiste" clipped at 320px (`clinic-settings.tsx:1016`)

Every select trigger on all 27 routes was checked at both widths; only these two are narrow enough
to clip today, but the primitive is wrong for all of them. **This is the closest match to the
original "filter cards overflowing" complaint.**

### 4. `/users` — the "Créer un compte" button never lands on its own row

`web/components/user-management.tsx:425` tries to override `CardAction`'s grid placement, but
`col-start-2` (from `ui/card.tsx:87`) and `col-span-full` (the override) are **different
tailwind-merge groups**, so both survive and the cascade resolves to `grid-column: 2/-1`. The button
stays beside the title. Confirmed by computed-style probe: `grid-template-rows: "96px 0px"` — row 2
is permanently empty. Reproduces at every width under 640px. The code's own comment describes this
symptom as already fixed; it is not.

### 5. Money values split their unit onto a second line at 320px

`web/components/ui/stat-strip.tsx:129` — the value span has no `whitespace-nowrap`, so
"30 046,200" / "DT" wraps. Seen on `/factures` and `/cheques`; suspected on `/caisse`
(unconfirmed). One fix covers every `StatStrip` user. The component's own docstring claims it fits.

### 6. Genuine sub-44px touch targets (4 confirmed out of ~380 flagged)

| Control | File | Size |
|---|---|---|
| `/settings` « Horaires de ce praticien » `<summary>` | `clinic-settings.tsx:1144` | 32px |
| Home Durée/Nombre chart toggle | `procedure-mix-chart.tsx:92` | 40px (`coarse:min-h-10`) |
| Patient phone `tel:` link | `app/patients/[id]/page.tsx:1049-1054` | 20px, no `.touch-target` |
| Home "Ouvrir l'agenda →" | `dashboard-section.tsx:83-88` | 16px (low — a duplicate CTA sits below) |

### 7. Agenda day-of-week cells fall under 44px at 320px only

`web/components/agenda-phone-header.tsx:398-414` — 7 equal `grid-cols-7` columns net ~37px wide at
320px. Clean at 390px. The author's comment reasons about the height floor (52px, fine) and never
addresses width.

### 8. A closing guillemet wraps alone onto the last line

`/cheques` at 320px — `web/app/cheques/page.tsx:34`. Static prose that never went through
`quoteFr()`, so it has the same visual failure the helper exists to prevent.

---

## Recurring patterns

**The repo's dominant defect shape appears three times in this audit** — a correct, documented fix
applied to some call sites and not the rest:

1. `relative z-10` on 3 of 4 `CardList` slots (finding 1)
2. the `_LG` card hinge on 10 of 18 tables (finding 2)
3. `.touch-target` present on nearly every control but missing from isolated links (finding 6)

Each is one fix at the shared layer, not N investigations. Fixing them per-call-site is what
produced this state.

**Two are invisible to every existing gate** — `tsc`, `check:responsive`, and any size-based
touch-target rule all pass while the control is unusable. Worth deriving guards:
- assert no interactive element's centre hit-tests to a different element within the same card;
- assert no table imports the `md:` hinge pair.

---

## Calibration — do not trust two numbers from the raw tooling

- **Sub-44px counts are ~99% false positives.** Every `<Button>` carries an invisible 44px hit area
  via a `.touch-target` `::after` (`ui/button.tsx:22-25`, `globals.css:750-762`) that
  `getBoundingClientRect()` cannot see. Of roughly 380 raw flags, **4 were real**.
- **The click hit-test first reported 99 swallowed controls across 20 routes.** Nearly all were
  controls scrolled beneath the sticky header and bottom nav — an artifact of the probe's own
  scrolling. Filtering to "the thief is in the same card as the control" gave 99 → 5, all on
  `/lab-orders`. Do not resurrect the 99.

Ruled out on inspection: the odontogram's 60+ overflowing elements are an intentional
`overflow-x-auto` pattern already guarded by an `arch-clipping` check.

---

## Verification of the fixes

Every fix was checked in a real browser at the width it was reported at, not assumed from the diff.

| Finding | Fix | Verified |
|---|---|---|
| 1 · lab-orders status control | `card-list.tsx` lifts interactive **descendants** of `fields` (not the whole `<dl>`) | hit-test 5 swallowed → **0**; a static field label still hits the patient link, so tap-the-card survives |
| 2 · 8 tables clip at 820 | swapped to `CARDS_ONLY_LG` / `TABLE_ONLY_LG` | `card-fallback` check passes; `tsc` clean |
| 3 · select clipped, no ellipsis | `select.tsx` value is `block truncate`, not `line-clamp-1 flex` | computed style now `display:block`, `text-overflow:ellipsis` on `/treatment-plans` (320 + 390) and `/settings` |
| 4 · /users button placement | reset `col-start` / `row-start` / `row-span`, restored at `sm:` | 390 px → `grid-column 1/-1`, `grid-row 2`, own row; 1440 px → `column 2`, `row 1/span 2`, unchanged |
| 5 · money unit wrapping | `whitespace-nowrap` on the `StatStrip` figure | « 30 046,200 DT » renders on **1 line** at 320 px |
| 6 · touch targets | `coarse:min-h-11`; `.touch-target` on 3 isolated links | chart toggle measures **44 px** |
| 7 · agenda day cells | full-bleed below 360 px | 320 px → **45 px** (was 37, then 40), 359 → 50, 390 → 47, no new overflow |
| 8 · guillemet orphan | routed through `quoteFr()` | `french-quote-binding` check passes |

**Gate:** `npx tsc --noEmit` → 0 errors · `npm run check:responsive` → **23/23** · full sweep of 24 routes at
390 px and 14 at 320 px → **0 horizontal overflow, 0 errors**.

⚠️ **Two fixes were wrong on the first attempt and the browser caught both** — worth knowing before trusting a
similar-looking change:
- `relative z-10` on the whole `fields` `<dl>` would have fixed `/lab-orders` and **broken tap-to-open-patient
  on every card list in the app**, by lifting the static field text above the overlay too. Only the controls
  may rise.
- Trimming the day strip's padding and gap got 37 → 40 px, not 44. `AppShell`'s 16 px gutter leaves 288 px, so
  seven cells cap at 41 px *by arithmetic* — the gutter itself had to be reclaimed. A diff-only review would
  have passed the 40 px version.

## Still open

- `patient-summary-modal.tsx` puts a `<Button>` in `fields`. The `card-list.tsx` fix covers it structurally,
  but it renders only inside an open modal, which was never opened — **unverified**.
- Below-the-fold content at 320 px on `/settings`, `/journal`, `/caisse` was never eye-checked.
- `/patients/[id]`, `/treatment-plans/[id]` and in-dialog controls were not hit-tested.

## Verified clean

- Every dialog and sheet at 390px (Nouveau RDV, edit-appointment, fiche médicale, ajouter un
  patient, and two confirmation `alertdialog`s including a nested one): full-height sheet, sticky
  footer, scrollable body. Correctly reverts to a **centred dialog** at 820px, which is the rule.
- Every interaction surface at 390px: comboboxes, the multi-select act picker, native selects, two
  inline collapsibles, the notification panel, the inline validation banner. No clipping.
- All tables fall back to cards at 390px — no raw table scrolling sideways on a phone anywhere.
- Historical bugs re-verified fixed: the odontogram scroller, and `/fournisseurs`' search box.

## Coverage gaps

- `/creances` and `/recurring-series` render a deliberate "Page retirée" placeholder — their "clean"
  verdict is meaningless.
- The app scrolls an inner `<main>`, so screenshots cover only the first viewport. Content below the
  fold on `/settings` (billing, reminders, backup), `/journal` and `/caisse` was **not** visually
  inspected at 320px.
- `/patients/[id]/files` was empty (0 files); `/patients/[id]` and `/treatment-plans/[id]` were not
  in the click hit-test route list.
- Controls inside dialogs were not hit-tested — including `patient-summary-modal`'s field button.
- Empty lists prove nothing; only rows with data were testable.

## Environment note

The auth cookie was invalidated repeatedly mid-audit with no code change. The notification panel
explains it: « Session interrompue : un identifiant déjà remplacé a été présenté ». Concurrent
agents each calling the session-refresh helper replace the previous session, so back-to-back
refreshes race. Not a product bug — but it will bite any future parallel browser run sharing one
account.

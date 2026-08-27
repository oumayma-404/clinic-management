# UI Design: Audit Sections 3–10 — the three novel surfaces

**Status:** APPROVED
**Approved:** 2026-07-28
**Created:** 2026-07-27
**Revised:** 2026-07-28 — **Screen 1 (patient merge) was dropped from scope.** Merge was replaced in the spec by
duplicate *prevention* (AC-P7.18–7.26): the audit's mandate for merge was a single sub-clause, § 1's archive already
covers the common duplicate, and the real cause — no duplicate detection at all, plus inline patient creation in the
booking dialog with no search — was un-guarded. The merge design is **complete and preserved** at
`follow-up/patient-merge.md` + `follow-up/mockups/01-fusion-patients.html`; do not redesign it if it returns.
The duplicate warning it was replaced by follows the existing deep-linked blocker-list pattern
(`patients-table.tsx:363-380`) and needed no new design.
**Spec:** [spec.md](./spec.md) — APPROVED
**Exploration:** [exploration.md](./exploration.md)
**Scope:** **Deliberately partial.** Only the three surfaces with no precedent in this codebase.

> **Mockups were derived from source, not from a live app.** There is no `agent-browser`, no `scripts/start-dev.sh`,
> and nothing was listening on `:3000` or `:5000`. Per the skill's documented fallback, the design system was
> extracted by reading `web/app/globals.css`, `web/components.json`, every primitive in `web/components/ui/`, the
> page shell, and the closest existing analogues. Every class string in the mockups is copied from real source.

---

## Why only three screens

The spec adds roughly fourteen interactive surfaces. Eleven of them follow a pattern that already exists and is
documented in the spec itself — P2's eight actions are the `AlertDialog` + `sonner` pattern from
`procedure-types-table.tsx`, P3's mobile nav is a shadcn `Sheet`, P1's hours editor is a standard form, P7's audit
history is a list. Designing those would produce mockups of things the codebase already decides.

Three have **no precedent at all**, confirmed definitively by exploration:

| Pattern needed | Exists in `web/` today? |
|---|---|
| Side-by-side / diff / before-after comparison | **No.** Zero instances. Every `grid-cols-2` is a two-up form layout. |
| Type-to-confirm | **No.** Zero. Also no disabled-until-typed submit anywhere. |
| Bulk row selection | **No.** `<Checkbox>` is imported by **zero** feature components; no table has a selection column. |

---

## Design system, as extracted

**Tailwind v4, CSS-first — there is no `tailwind.config.*`.** Tokens are `oklch` custom properties in
`globals.css`; primary is a medical blue `oklch(0.52 0.14 245)`, radius `0.5rem`. The mockups load the **v4 browser
CDN**, because the real class strings contain v4-only syntax (`outline-hidden`, `@container/card-header`,
`max-h-(--radix-…)`) that the common v3 play CDN silently ignores.

**The app is always light mode.** `next-themes` is installed but never imported; nothing ever adds `.dark`. Every
`dark:` utility in the components is dead at runtime. Mockups target light only.

**Primitives present** (22): alert-dialog, avatar, badge, button, calendar, card, checkbox, command, dialog,
dropdown-menu, form-error-banner, input, label, popover, select, separator, switch, table, tabs, textarea, tooltip.

**Primitives absent** — all nine checked: `sheet`, `drawer`, `skeleton`, `radio-group`, `pagination`, `scroll-area`,
`progress`, `accordion`, `alert`. The Radix packages for `radio-group`, `scroll-area`, `progress` and `accordion`
**are already in `package.json`**, so adding the wrappers is a CLI call with **no new dependency**.

Shape notes that matter for fidelity: the table is the tight newer generation (`p-2` cells, `h-10` heads); `Badge` is
`rounded-full` with four variants; `Input` and `Textarea` come from *different* shadcn generations and focus
differently; the dialog close button already reads « Fermer ».

### New primitives these three screens require

| Primitive | Needed by | Cost |
|---|---|---|
| `radio-group` | Merge field chooser | CLI add — Radix dep already installed |
| `checkbox` **column pattern** | Bordereau batcher | No new primitive; `table.tsx` already ships the unused hooks `data-[state=selected]:bg-muted` and `[&:has([role=checkbox])]:pr-0` |
| `sheet` | (P3, out of scope here) | CLI add — `vaul` + Radix dialog already installed |

No pagination primitive is added. None of these three screens paginates; if the claims list outgrows a screen, the
period filter is the answer, consistent with every other table in the app.

---

## Screen 1 — Patient merge

**Mockup:** [`mockups/01-fusion-patients.html`](./mockups/01-fusion-patients.html) · **ACs:** P7.18–7.30

### Shape: a four-step wizard

`Survivant → Champs → Dents → Vérifier`, reusing `setup-wizard.tsx`'s step indicator verbatim (the `w-12 h-12`
circles, the blue-600 ramp, the `w-16 h-0.5` connectors) and its Précédent/Suivant footer inside `mt-8 pt-6 border-t`.

Chosen over a single scrolling page because a merge is irreversible and multi-table. The wizard buys three things a
long page does not: each step can gate Next on being fully resolved, the odontogram gets room instead of being a
cramped section, and **step 4 forces a review before the commit**. The terminal button is green — the app's existing
signal that an action commits (`setup-wizard.tsx:733`).

### Step 2 — the field chooser

Two radios per conflicting field, laid out in a fixed two-column grid under a persistent column key naming which
record is which. Fields are grouped into `edit-patient-dialog.tsx`'s section pattern: icon + `text-lg font-semibold`
heading, fields inside a tinted `p-4 rounded-lg border bg-muted/30` panel.

**The load-bearing detail is the empty-survivor case.** When the survivor's value is empty, the *other* side is
pre-selected and flagged « Seule copie » in amber, with the empty side drawn as a dashed border reading
« Non renseigné ». AC-P7.21 says a value the survivor lacks must be *offered, not silently dropped* — making it the
default rather than something the operator must notice is what actually satisfies that.

Three bulk helpers (« Fiche conservée » / « Fiche fusionnée » / « Valeur non vide ») handle the common case where one
record is simply better.

### Step 3 — the odontogram

**This needs no new chart code.** `RecordToothChart` is presentational — it takes a `ToothPaint` map — and
`ToothPaint` *already* encodes two states on one tooth: `color` as the fill and `existingColor` as a 3px outline,
dashed when the prior state was a diagnosis. That is the app's existing grammar for "old value and new value on the
same tooth", so the merge view extends it: **fill = chosen, outline = the other record's**, plus an amber ring on
teeth still in conflict. The legend gains one row for that ring.

Per-tooth resolution reuses the odontogram's popover content shell (condition dot + label + Diagnostic/Réalisé pill +
date), with a checkbox to keep both states in the tooth's history rather than discarding one.

### Step 4 — review, and where duplicates surface

Two panels — what will be reparented (with counts), and the values retained. Then the part that matters:
**duplicate-child conflicts are surfaced here with a resolution each**, using the blocker `<ul>` markup from
`patients-table.tsx:363-380` in an amber panel. The two-appointments-in-one-slot case is exactly what AC-P1.15's
exclusion constraint would otherwise reject mid-transaction (EC-16, EC-32).

---

## Screen 2 — Anonymize

**Mockup:** [`mockups/02-anonymisation.html`](./mockups/02-anonymisation.html) · **ACs:** P7.31–7.40, P3.45

Five states: blocked · confirmation · name typed · in flight · after.

### The confirmation gate

Typing **the patient's full name** enables the destructive button. A fixed word like `ANONYMISER` proves only that
the operator meant to anonymize *something*; the failure mode being guarded is anonymizing the **wrong record**, and
only the name guards that. The field is `autocomplete="off"`, the hint flips to a green « ✓ Le nom correspond. », and
the button carries the standard `bg-destructive text-destructive-foreground hover:bg-destructive/90`.

### Removed vs retained, side by side

Two panels — destructive-tinted for what goes, plain-bordered for what stays. Retained explicitly names **invoice
numbers** and **who anonymized and when**. A separate amber strip calls out file deletion, because that is the one
part no retry can repair (AC-P7.37 makes blob removal follow the DB commit).

### The blocked variant

When an e-invoice is `Pending`, the dialog refuses and explains why: `EInvoiceService` re-reads the patient at
dispatch and sends `GetFullName()` as the TEIF legal buyer, so anonymizing first would transmit a placeholder onto a
filed fiscal document (AC-P7.35). This mirrors the blocked-delete dialog at `patients-table.tsx:350-398` — name the
blocker, deep-link it, and say plainly that nothing changed.

### After

The archived banner is replaced by a destructive-tinted one that **inverts the archive copy**. Archive says
« Aucune donnée n'a été supprimée » — anonymize must say the opposite, in the same register:
« Les données identifiantes ont été définitivement supprimées, y compris dans l'historique des modifications. »
Identity fields render as em-dashes, the title becomes « Patient anonymisé #A-1042 », and the action buttons are gone
because AC-P7.40 forbids un-archiving.

---

## Screen 3 — CNAM bordereau

**Mockup:** [`mockups/03-bordereau-cnam.html`](./mockups/03-bordereau-cnam.html) · **ACs:** P8.9–8.17

### Selection: checkbox column + sticky bar

A leading checkbox column with a tri-state select-all in the header. `table.tsx` already ships the CSS hooks for
this and nothing currently triggers them — `TableRow` has `data-[state=selected]:bg-muted`, `TableHead`/`TableCell`
have `[&:has([role=checkbox])]:pr-0`. So the pattern is new to the app but not new to the primitive.

The sticky bar shows « N créances sélectionnées » and a **running total in `formatDT`**, because the operator is
batching money and the total is the number they will reconcile against. The count is `role="status"` +
`aria-live="polite"` — the repo has exactly one `aria-live` region today and zero `role="status"`, so AC-P3.44 starts
here.

**A claim that cannot be batched is disabled with the reason inline** — « Accord préalable non obtenu » as an amber
outline badge — rather than being silently absent or failing at finalize (AC-P8.7 finally reads
`DentalActCode.RequiresAccordPrealable`, which is stored today and read by nothing).

### Lifecycle: mirror the invoice

`Brouillon → Constitué → Déposé → Remboursé`, with `Annulé` off to the side. This reuses the house fiscal palette
(grey → blue → amber → green, red for cancelled/rejected) that `invoice-labels.ts` and `treatment-plan-labels.ts`
already share, via a new co-located `bordereau-labels.ts` — the established convention.

Three rules lifted directly from `invoices-table.tsx`:

1. **Print is gated on "not draft."** A draft has no number, so nothing printable exists.
2. **Delete only ever applies to drafts.** Past constitution it is *cancelled with the number retained*, never
   deleted — matching the invoice and devis copy « le numéro est conservé. Un motif est requis. »
3. **`canFinalize` / `canDeposit` / `canCancel` ship on the DTO**, not re-derived in the UI. The comment at
   `invoices-table.tsx:370-372` documents the bug that produced this rule: a client-derived gate offered an action
   the API refused.

The received column stacks a secondary annotation under the figure — « 840,000 DT en attente », « −230,000 DT
rejeté » — copying the avoir annotation at `invoices-table.tsx:426-440`, including the real minus sign.

### Cancel dialog

States the claim count that returns to the pool, requires a motif, and keeps the number. Without it, one mis-click at
month end permanently strands every claim on the bordereau (AC-P8.13).

---

## Accessibility

All three meet the bar AC-P3.41–3.44 sets, since they are new surfaces and the spec extends §7.8's requirements to
them:

- Every control is keyboard-operable; radios and checkboxes are real inputs, not styled divs.
- Every `<label>` pairs with its control; each field group is a `<fieldset>` with a `<legend>`.
- Icon-only buttons carry `aria-label`; row checkboxes are labelled per row (« Sélectionner la créance de … »).
- The selection count is `role="status" aria-live="polite"`.
- The anonymize dialog is `role="alertdialog"` with `aria-modal`, `aria-labelledby`, `aria-describedby`.
- Colour is never the sole signal: conflicting teeth carry an amber ring **and** bold amber numerals; « Seule copie »
  is a labelled badge, not just an amber border.
- Usable at 375 px: the merge grid and the sticky bar wrap; the claims table scrolls inside its own container.

---

## What is deliberately NOT designed here

P3's mobile nav drawer · P2's eight new actions · P1's working-hours editor and conflict list · P7's audit history
view · P8's claim list, claim detail, reconciliation screen and patient CNAM position. All follow patterns already
in the codebase and named in the spec. They are still in scope for **implementation** and still subject to
AC-P3.41's accessibility bar and AC-P3.46's manual walk.

---

## Adjacent defect found while extracting the design system

**The Geist font is declared but never applied.** `layout.tsx:14-15` calls `Geist()` and `Geist_Mono()` and assigns
them to `_geist` / `_geistMono` — underscore-prefixed, never used. No `.className`, no `.variable`. `font-sans`
resolves through `@theme inline` to the literal string `"Geist"`, which does not match next/font's hashed family
name, so unless Geist is installed locally the browser silently falls back to its default sans.

Not in the audit, not in this spec's scope, and **not** being folded into P3 — recorded here so it is not lost.

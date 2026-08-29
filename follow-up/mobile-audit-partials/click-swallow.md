# Phone view — clicks swallowed by the card overlay

Reported by the owner: on `/lab-orders` at phone width, tapping the status control
(« Envoyé » / « Reçu » / …) navigates to the patient page instead of changing the status.

**Confirmed, root-caused, and the blast radius is measured.** This was NOT found by the layout
audit and could not have been: that pass measures a loaded page and never clicks anything.

## Mechanism

`web/components/ui/card-list.tsx` makes the whole card one big click target by stretching the
title link over it:

- `card-list.tsx:218` — the title `<a>` carries `after:absolute after:inset-0`, so an invisible
  overlay covers the **entire card**.
- Three slots are deliberately lifted above that overlay with `relative z-10`:
  - `leading` → `card-list.tsx:200`
  - `actions` → `card-list.tsx:257`
  - `primaryAction` → `card-list.tsx:283`
- **`fields` is not.** The `<dl>` at `card-list.tsx:259-278` has no `relative z-10`, so anything
  interactive placed in a field sits *underneath* the overlay.

The comment at `card-list.tsx:281-282` states the consequence exactly, for the slot that *did*
get the fix: *"the heading's `after:inset-0` overlay covers the whole card, and without this the
button would be under it and untappable."*

This is the repo's documented dominant defect shape: a correct fix wired to three of four call
sites. See the `fixes-dont-propagate` pattern.

## The instance

`web/app/lab-orders/page.tsx:961` puts an interactive `<select aria-label="Changer le statut">`
inside `fields`, while `page.tsx:949` sets the card `href` to `/patients/${o.patientId}`.

Tap → overlay → navigate to the patient. Phone-only because the select is phone-only by design
(the code comment at `page.tsx:956-958` says it "exists ONLY on the phone card").

### Evidence

Hit-tested each control's own centre with `document.elementFromPoint` at 390×844, `hasTouch: true`.
Five cards rendered, five swallowed selects — each resolving to its own card's patient link:

| Control (110×44) | Click actually lands on |
|---|---|
| Changer le statut — Sonia Trabelsi | `A href=/patients/64c922d5-e2da-4de1-9a96-0cdf2d3b7355` |
| Changer le statut — Fatma Zouari | `A href=/patients/0502beef-498d-4dce-b9ad-b29f12512ea3` |
| Changer le statut — Youssef Mrad | `A href=/patients/704a1e3e-72ad-492d-a22b-9133d2cd7bb1` |
| Changer le statut — Leila Gharbi | `A href=/patients/ed4d366c-9952-40a4-bb05-56bb3dfb01e9` |
| Changer le statut — Karim Hamdi | `A href=/patients/d48ed6b4-6e1e-40b5-b14a-224b33c6f512` |

Note the control measures 110×44 — it passes the touch-target rule and still cannot be used.
A size check could never have caught this.

## Blast radius — swept, and it is exactly one screen

All 26 authenticated routes probed at 390px. Signature: the element stealing the click lives in
the **same card** as the control it covers. **`/lab-orders` is the only route that reproduces.**

An earlier unfiltered run reported 99 hits across 20 routes; those were controls scrolled beneath
the sticky header and bottom nav — an artifact of the probe's own scrolling, not defects. They are
excluded. Do not resurrect that number.

Static grep found three other call sites putting something interactive in `fields`; none
reproduced at runtime:

- `web/components/creances/receivables-table.tsx` — an `onClick` in a field, did not reproduce
- `web/components/user-management.tsx` — a `<Select>` in a field, did not reproduce
- `web/components/patient-summary-modal.tsx` — **NOT TESTED**: it only renders inside an open
  modal, which the probe never opened. Verify by hand.

## Suggested fix (not applied)

Add `relative z-10` to the `fields` `<dl>` at `card-list.tsx:260`, matching the three sibling
slots. That fixes the primitive for every current and future call site rather than patching
`/lab-orders` alone.

Guard worth adding, since this class is invisible to `tsc`, to `check:responsive` and to any
size-based touch-target check: assert that no interactive element's centre hit-tests to a
different element within the same card.

## Coverage gaps

- Patient detail (`/patients/[id]`, its tabs, `/patients/[id]/files`) and
  `/treatment-plans/[id]` were not in this sweep's route list.
- Controls inside dialogs/sheets were not probed (nothing was opened).
- Only rows that actually had data were testable; an empty list proves nothing.

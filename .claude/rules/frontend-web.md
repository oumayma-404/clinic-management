---
paths:
  - "web/**/*.{ts,tsx,css}"
---

# Frontend Rules — every device, every time

Directives for any code written under `web/`. The *reasons* live next to the code
(`web/CLAUDE.md`, `web/components/CLAUDE.md`, the comment blocks in `app/globals.css`, and
`features/mobile-tablet-responsive/spec.md`); this file is the imperative form. Where they disagree, the code
and its own `CLAUDE.md` win — and fix this file.

**This is not a phone checklist.** The device this app is used on most is a **tablet held at the chair with
gloved hands**, and the widest defects the audit found (26 dialogs clamped to 512 px, teeth unreachable by
scrolling) were *desktop* defects that only a device pass surfaced.

## § 0 The governing rule

> **No capability is removed by a layout decision.** If a platform limit genuinely prevents one, show an
> explicit French message — never fail silently, never just hide the control.

Every screen must be usable at **320 px wide**, at a **380 px viewport height** (landscape phone), and at
**200 % browser zoom**. 320 px, not 390: an iPad in Split View renders a phone layout on a 1024 pt device.

A change is not "done on desktop, responsive later." There is no later — the deferred-remainder loop is what
`features/mobile-tablet-responsive` existed to close, and it cost eight phases.

## § 1 Four device states, and which hinge to use

| State | Width | Hinge |
|---|---|---|
| Phone | < 640 | `sm:` |
| Tablet portrait | 640–1023 | `lg:` |
| Tablet landscape / small laptop | 1024–1279 | `xl:` |
| Desktop | 1280+ | — |

These are Tailwind's **stock** boundaries. ⚠️ **Never declare `--breakpoint-*` in `globals.css`** — in Tailwind
v4 redeclaring an existing key silently re-points every utility already using it (75 `md:` sites). The
`breakpoint-tokens` check fails on it.

`md:` (768) is the ordinary phone↔desktop hinge. **A table of roughly eight or more columns needs `lg:`
instead** — an iPad portrait is 820 px and therefore already `md:`, so it would get the desktop grid *and* the
256 px rail: ~532 px for a 10-column table. Cells wrap (§ 6), so such a table now *fits* that box instead of
scrolling out of it — at ~50 px a column, which is legible without being readable.

Never assume a viewport in JS. Layout decisions are CSS; if a component must know, it reads a media query, not
a `window.innerWidth` snapshot taken once.

## § 2 Touch is a pointer question, not a width question

Gate touch sizing on **`coarse:`**, never on a breakpoint. An iPad landscape is 1180 px and is still operated
with a finger; a 1440 px desk machine with a mouse must keep its density.

**44 × 44 px minimum tappable area on a coarse pointer.** The *painted* control may stay smaller.

Two different fixes — picking the wrong one causes wrong-action bugs:

```tsx
// ✅ An ISOLATED small control: overlay a hit area, paint nothing.
<Button size="icon" className="touch-target" aria-label="Modifier" />

// ✅ Anything in a ROW or a STACK — menu items, tooth cells, pager buttons: grow its own box.
<DropdownMenuItem className="coarse:py-3" />
<button className="size-8 coarse:size-10" />

// ❌ `.touch-target` on siblings a few pixels apart. The 44px pseudo-element overhangs its
//    neighbours and, since the later sibling paints last, it STEALS their taps.
<div className="flex">
  <button className="touch-target" /><button className="touch-target" />
</div>
```

⚠️ `.touch-target` is **inert inside an `overflow-hidden` box** (an agenda appointment block) — the
pseudo-element is simply clipped. Grow the box or move the target.

⚠️ Do **not** re-add a 44 px floor to fields. `globals.css` already applies `min-height: 44px` under
`(pointer: coarse)` to `input` (non-checkbox/radio/hidden), `textarea`, native `select` and
`[data-slot="select-trigger"]`. It is a floor, not an override — a component's own larger height still wins.

## § 3 A field under 16 px zooms iOS and never zooms back

Every text-entry primitive carries `text-base md:text-sm` for this reason (`input`, `textarea`, `time-field`,
`select`, `command`).

```tsx
<Input className="text-sm" />        // ❌ tailwind-merge REMOVES the primitive's text-base. iOS now zooms.
<Input className="md:text-sm" />     // ✅ same desktop result, guard intact.
```

## § 4 Dialog width: always prefix the override

`DialogContent`'s base is `w-full max-w-[calc(100%-2rem)] … sm:max-w-lg`. An **unprefixed** `max-w-*` from a
call site is the same tailwind-merge group as the base gutter (caller wins, gutter gone) but cannot remove
`sm:max-w-lg`, which then wins at ≥ 640 px.

```tsx
<DialogContent className="max-w-4xl" />      // ❌ edge-to-edge on a phone AND 512px on a desktop.
<DialogContent className="md:max-w-4xl" />   // ✅ gutter kept below, declared width honoured above.
```

This shipped on **26 of 36 dialogs** undetected. It is the reason `check:responsive` exists — no type could
catch it and nobody could see it. `dialog-max-w` fails on it now.

## § 5 Below `md:`, a heavy dialog is a sheet

- **Heavy** data-entry surfaces (patient form, devis editor, fiche de soins, invoice form) → full-screen
  `Sheet` with a sticky header and a sticky footer holding the primary action.
- **Light** confirmations, including every `AlertDialogContent` → bottom sheet.
- **Never a sheet:** an unrequested interruption (the post-visit prompt). On a coarse pointer that degrades to
  a toast with an action, and suppresses itself while any dialog or sheet is open. A full-screen takeover that
  can fire mid-payment is not a presentation choice.

Non-negotiables for either:

- Size to **`dvh`**, never `vh` — a `max-h-[90vh]` cap does not shrink when the keyboard opens, so the sticky
  footer holding « Enregistrer » ends up under it (`sheet-vh` check).
- Dismissible by a **visible ≥ 44 px control and by `Escape`**, in addition to swipe. A reception tablet has a
  keyboard.
- Focus lands on the **title**, not the first field — autofocusing a field raises the keyboard over the
  content the user opened the sheet to read. Focus returns to the trigger on close.
- **Entered data confirms before discarding**, on *every* channel: swipe, back gesture, outside tap, close
  control.
- **Crossing a breakpoint must not remount the component.** Rotation, Split View and Stage Manager all cross
  768 px; presentation changes, state does not. Two components behind one `md:` toggle loses typed input.

## § 6 A `<Table>` never ships alone

Below its hinge, a table renders as a **card list** — `ui/card-list.tsx` with `CARDS_ONLY`/`TABLE_ONLY`
(`md:`), or `CARDS_ONLY_LG`/`TABLE_ONLY_LG` for ~8+ columns.

```tsx
// ✅ Two trees. A real <table> above, a semantic list below.
<div className={TABLE_ONLY}><Table>…</Table></div>
<div className={CARDS_ONLY}><CardList items={rows} fields={…} /></div>
```

Two trees, **not** a `display:block` reflow: the reflow strips the implicit table roles, so a screen reader
announces « Ben Salah 45,000 12/03 Payée » with no field names — across money and clinical data. The doubled
DOM is bounded because every list is paged. The `card-fallback` check derives the table surfaces rather than
listing them, so a new table with no card list fails the gate.

**A cell wraps; keep it that way.** `ui/table.tsx` deliberately does *not* set `whitespace-nowrap` — that
default made a table's width the sum of its longest unbreakable value, so one free-text column pushed the rest
out of the scrollport (`/caisse` hid 479 px on a 1440 px laptop). Reach for a per-cell override only with a
reason:

```tsx
<TableCell numeric>{formatDT(x)}</TableCell>                  // figures: right + tabular + nowrap
<TableCell className="whitespace-nowrap">{formatDateFr(d)}</TableCell>  // atomic: `d MMM yyyy`, a phone, `F-2026-0142`
<TableCell clamp title={note}>{note}</TableCell>              // free text: 2 lines, full value in the tooltip
```

⚠️ Never `truncate` in a cell — it hides the tail of a value on one line, which is the defect, not the fix.

Card content rule, in this order: **identity → status → money → date**, actions in one menu. A long primary
value truncates to one line with its full value on tap. **A field with no value is omitted, not rendered as
« — »** (`Email`/`PhoneNumber` are genuinely nullable). A `<TableCell colSpan={N}>` empty row has no meaning
in a card list — give it a card-list equivalent that keeps the filter-vs-empty distinction.

## § 7 The viewport is dynamic, and the bottom edge has one owner

- **`h-dvh` / `min-h-dvh`, never `h-screen` / `min-h-screen`.** `100vh` on iOS Safari is the *large* viewport,
  so the bottom of the page sits under the URL bar, unreachable (`viewport-height` check).
- A **fixed** element clears the bottom bar with **`--bottom-inset`** (bar height + `env(safe-area-inset-bottom)`)
  — never a hand-written `bottom-4`. Four things want that edge and they used to overlap. An element in the
  normal flow needs nothing: `<main>` shrinks around the bar, which is why the bar is not `fixed`.
- Use **`AppShell`** for a protected page. It owns the one gutter, the one content width, and the `pb-20`
  runway that keeps the AI launcher off the last row. Do not re-assemble `flex h-screen` + sidebar + header by
  hand — that copy-paste is how 24 pages drifted into three gutters and five content widths.

## § 8 11 px is the floor, and no size is a pixel

No text that carries information may render below **11 px** (`text-2xs`). No `text-[Npx]` anywhere in `app/` or
`components/` — a pixel value ignores the user's text-size setting and does not respond to zoom (`type-scale`
check). 121 of these shipped, 71 of them at 8–10.5 px: every tooth number, the « +N » counts, the
« non synchronisé » badge.

## § 9 Hover — two rules that look like one

They are different, and applying the first to a second-rule case makes the affordance **permanently
invisible on touch**:

1. **Movement** hovers (`hover:scale-*`, translate) → gate behind **`hover-hover:`**. A tap fires `:hover` and
   leaves it applied, so a transform reads as a stuck element (`hover-movement` check). A plain colour or
   background hover may stay ungated — a lingering tint reads as "selected", not as broken.
2. An affordance only **reachable** by hover (`opacity-0 group-hover:opacity-100`, a tooltip carrying the only
   explanation) → give it a **real touch path**. Never gate it behind `hover-hover:`. A file **delete** button
   that is invisible and un-tappable on touch is not a hover polish issue.

## § 10 Ungated grids and oversized popovers

```tsx
<div className="grid grid-cols-2">        // ❌ two columns at 320px.
<div className="grid gap-4 sm:grid-cols-2">  // ✅
<PopoverContent className="w-80" />       // ❌ 320px popover in a 320px viewport, no gutter.
<PopoverContent className="w-[min(20rem,calc(100vw-2rem))]" />  // ✅
```

## § 10.1 Two flex traps that cost real defects

```tsx
<div className="flex … sm:flex-row">        // ❌ on a `grid` base (CardHeader) this is inert — `grid` survives.
<div className="flex flex-col … sm:flex-row">  // ✅ an UNPREFIXED `flex` is what replaces the display.

<div className="w-full flex-1 sm:w-auto" />    // ❌ `flex-1` is `flex: 1 1 0%`; the 0% basis beats `w-full`,
                                               //    so this never triggers the wrap and collapses to ~0.
<div className="w-full sm:w-auto sm:flex-1" /> // ✅ basis `auto` below the hinge, so `w-full` takes the row.
```

Both are invisible to `tsc` and to the eye at the width you are working at. The first painted a segmented
control the full width of a dashboard card; the second left `/fournisseurs`' search box **4 px wide** on a
phone.

**And a control whose label cannot shrink is a layout decision too.** `Button` and `Badge` are
`whitespace-nowrap shrink-0`, so a long French label overflows its container rather than wrapping — wrapping
the *row* does not help when there is only one child. Either let that one child break its label
(`ui/empty-state.tsx`'s action row) or shorten the *visible* half and keep the full phrase in `aria-label`
(`odontogram.tsx`'s « Créer un plan »). Never truncate: the label is the control's name.

## § 11 Overflow scrolls in its own container

The page body **never** scrolls horizontally at 320 px. Wide content (a table, the agenda grid, a code block,
a diagram) scrolls inside its own `overflow-x-auto` container.

⚠️ **Never `flex justify-center` inside a horizontally scrolling container** (`arch-clipping` check). When the
content overflows, `justify-content: center` pushes the overflow to *both* sides and **the inline-start
overflow is not in the scrollable region** — teeth 18–15 were unreachable at 390 px by any means. Use
`justify-start` and centre with `mx-auto` on the inner track instead.

Never "fix" an overflow with `overflow-x-hidden`. That is not a fix; it is clipping the content and calling it
a layout.

## § 12 Logical directional utilities

Code you write or rewrite uses `ps-`/`pe-`/`ms-`/`me-`/`text-start`. RTL is out of scope, but not precluding it
costs nothing while the file is open and otherwise means a second sweep of the same files.

## § 13 The UX floor for any interactive surface

Not optional, and not separate from responsiveness — on a phone every one of these is the *only* feedback
channel:

- **In flight:** disabled, with a single effect on double-submit.
- **Success:** a French `sonner` toast (its container is the app's live region, so toasts announce).
- **Failure:** `showErrorToast`, the dialog **left open with its input intact**. Never close a form on error.
- **Labels:** a real `<Label htmlFor>`. A placeholder is not a label. `aria-label` on every icon-only control.
- **A clickable `Card`:** `role="button"` + `tabIndex={0}` + Enter/Space.
- **An inline async result:** `role="status"`.
- **Empty states via `ui/empty-state.tsx`**, and keep the three kinds apart: *nothing yet* (invite + the action
  that creates the first record) · *nothing matching the filter* (offer « Effacer les filtres », **never** an
  « Ajouter » — the record may exist and the user mistyped) · *failed to load*, which is not an empty state and
  gets a « Réessayer » banner.
- **A failed read must never render as empty data.** `.catch(() => [])` turns a network error into « Aucun
  antécédent médical » on the screen a dentist checks for allergies. Record which read failed and offer a
  retry beside the sections that did load.
- **An active filter is visible as a removable chip**, at every width. Nine dashboard links land on a
  *filtered* list, and a card list with no chip is a filtered list that looks unfiltered.
- **A loading skeleton distinct from empty.** A card list has no header row, so "empty", "loading" and "your
  filter is wrong" are otherwise the same blank rectangle.
- **A destructive confirm names what it destroys** (« Supprimer le brouillon de {patient} ? »), and its button
  is `variant="destructive"`. With three drafts open, « Êtes-vous sûr ? » cannot say which one you are losing.
- **No English string reaches a user**, and none mentions CORS. A connection loss is its own French, retryable
  state — distinct from a business refusal.

## § 14 The gate — run it, don't assume it

```bash
cd web
npm run check:responsive     # the mechanical half: one grep per class of invisible defect
npx tsc --noEmit
npm run build
```

⚠️ **`npm run lint` cannot be the gate**: `eslint` is in the `lint` script but **not in `devDependencies`**, so
it fails on a clean install. (It used to add « and `next.config.ts` sets `eslint.ignoreDuringBuilds` » — that key
is **gone since the Next 16 upgrade**, which removed the built-in ESLint integration outright, so the key is not a
valid `NextConfig` property any more and its absence changes nothing: Next 16 does not run ESLint during a build.)
There is **no test runner,
no visual-regression tooling and no CI** in `web/`. That is exactly why the three commands above and the eye
pass below are the whole gate — treat a missing runner as a fact to work with, not a gap to fill mid-feature.

Then **look at it**, at these widths: **320 / 390 / 820 / 1180 / 1440**, plus a landscape phone, plus with a
keyboard. Record the result in the feature's `progress.md`. The manual walk is the load-bearing half; nothing
in `web/` can assert a layout.

Adding a mechanical check: derive the surfaces (`card-fallback` derives its table list), never hand-maintain an
expectation list, and **never add a per-file exemption** — an allow-list that grows is a check that has stopped
working. **Then prove it fails**: feed it a deliberate violation in a throwaway file and confirm a red run before
you trust a green one. A too-tight check is noisy and you notice; a too-loose one is silent and indistinguishable
from passing. Every check is enforced on arrival — see § 15 on why the old `PENDING_PARTS` staging is gone.

## § 15 What has landed, and what is genuinely still open

`features/mobile-tablet-responsive` **P1–P8 have all landed**, and so has `mobile-native-shells` Part 1. This
section used to list three things as missing; all three now exist, so **use them** rather than working around them:

- **Printing is handled.** `globals.css` has a `@media print` block, and the shell elements carry `print:hidden`
  (rail, drawer, header, bottom bar, assistant launcher + panel). Hide a new piece of chrome by putting
  `print:hidden` **on the element** — the block in `globals.css` owns only what a class cannot reach (sonner's
  portal, releasing the `dvh`/`overflow` cage, the paper palette, `@page`).
- **The icons and the manifest are real.** All seven declared assets exist in `web/public/`, generated from
  `web/branding/icon.svg` by `scripts/generate-icons.mjs`. **Never hand-edit a PNG in `public/`** — replace the SVG
  master and re-run the script. `themeColor` lives on the **`viewport`** export in `layout.tsx`; a `theme_color` in
  `manifest.ts` alone emits no `<meta>` at all.
- **The LAN device-trust page exists** (P8, `TrustController` + `TrustPortGate` + the trust listener). Its AC-46
  physical-iPhone verification is the one part still owed, and that is a *verification* gap, not a missing feature.

**One way to deliver a file:** `lib/download.ts`. Never a hand-rolled `<a download>`, never `file-saver` — both are
ignored by iOS Safari for a `blob:` URL, so the file silently never arrives. The `blob-delivery` check fails on it.

**One way to write the Local session cookies:** `lib/auth/session-cookie.ts` (Part 2). `/bff/auth/token` re-sets the
session cookie on every refresh exchange, so `local-login` is no longer the only writer — and a second rule for
`Secure` there would replace a stored cookie with one the browser drops, i.e. silently end the session. No route
sets `local_session` or `local_must_change_password` by name; the two are written and cleared together.

**One way to build a request header for the clinic API:** `apiHeaders(token, contentType)` in `lib/api/client.ts`
(Part 3). It is not only the bearer any more — it also attaches **`X-Client-Version`** from
`window.__clinicShell?.version`, which is what lets the server refuse a native build below its floor with **426**.
Fourteen raw-`fetch` sites across eight modules used to hand-write the object, so a fifteenth would send the token
and silently omit the version and the floor would cover part of the app only. The `api-headers` check fails on any
`Authorization: … Bearer` outside that file; a Next **route handler** (`app/**/route.ts`) is outside the rule
because it runs server-side, has no bridge to read, and is explicitly exempt from the floor. A 426 is surfaced
through `onClientTooOld` and taken over by `<ClientVersionGate>` — it is **never** a sign-out.

**Two axes of connectivity, and an absent signal is not "offline"** (Part 7). `useConnectivity()` polls on **every**
deployment and returns `serverReachable` (did the server answer at all) separately from `internetReachable` (may the
egress-dependent features run), with `egressSignalAvailable` saying whether the second is a reading or an assumption.
Never gate a poll or a badge on `AUTH_MODE`: `mode === "local"` is true on a clinic's own PC **and** on the hosted
multi-tenant backend, where the probe 404s — reading that 404 as "offline" is what pinned a hosted clinic's Google
controls off permanently. **Only a 200 that says so means "no egress".** (Google Calendar is now the *only*
egress-dependent feature; the AI assistant that used to be the other one is deleted.)

**No user-facing string may name the « réseau local ».** The same server is reached over a LAN, over Wi-Fi and over a
mobile network, so that wording is false everywhere but the offline-LAN install and points a dentist at something
that is not there. Say « Vérifiez votre connexion ». The `local-network-wording` check fails the gate on it.

**An `<input type="file">` clears its own `value` before the upload runs**, or a failed upload cannot be retried with
the same file: the element still holds it, so re-picking fires no `change` event at all. Copy the `FileList` into an
array *first* — clearing `value` empties the live list. And declare an `accept` that mirrors the server's allow-list,
never a narrower guess: `image/*` on the patient-files input would hide the PDFs the server accepts.

**A `blob:` PDF in an `<iframe>` is not a preview on a finger.** Android WebView renders it **blank** and iOS Safari
as one non-scrollable page, so any embedded-PDF surface needs two trees behind `coarse:` with the file itself as the
second — `patient-file-pdf-preview.tsx` is the shape, and `document-editor-content.tsx`'s official forms follow it.
Where that surface is also the **print** path, printing *through* the frame prints nothing: check the frame is really
rendered (`offsetParent !== null`) and otherwise hand the file to the OS through `downloadBlob`, whose viewer prints.

**In a shell the inactivity limit pauses the session; in a browser it still ends it** (Part 7, AC-57…AC-60).
`LocalSessionProvider` branches on `canConfirmIdentityInShell()` and raises `components/session-lock-gate.tsx` —
an **opaque** takeover over the still-mounted app — instead of calling `logout()`. Do not make it translucent and
do not unmount `children`: the record must not be readable behind it, and resuming to the fiche that was open is
the whole point. The cookie is **never** cleared on the success path; three unsuccessful attempts (a dismissal
counts) fall back to the password screen, and `unavailable` falls back immediately with no error and no dead
control.

**The Android shell exists, and `mobile/shared/bridge.md` is now the bridge contract** (Part 4). `print()` and
`onPushToken()` are implemented there, and `window.print()` inside the shell is a shim routing to the OS print
service — so a print call site needs no shell branch of its own. Read `bridge.md` before adding a member to
`window.__clinicShell`; a change to the set edits that file **and** bumps the shell's version.

Still open, so do not write code that assumes it and do not claim it in a report: `features/mobile-native-shells`
**Parts 5 and 8** — the iOS shell and the two store listings. `window.__clinicShell` is **always** feature-detected;
with it absent, behaviour must be byte-identical to a plain browser, and AC-26 is verified by **deleting** the
object at runtime.

**A forced password change is a destination, not a message** (Part 4, AC-76). A 403 carrying
`code: "must_change_password"` is surfaced through `onMustChangePassword` in `lib/api/client.ts` and
`LocalSessionProvider` navigates to `/change-password`. It is the one place `client.ts` **replaces** a server
message: the backend sends an English sentence and `lib/errors.ts` hands it to the toast verbatim. The login path
was never the gap — the `local_must_change_password` cookie covers it; an admin resetting the password of somebody
already signed in is.

⚠️ **`items-center` inside an `overflow-y-auto` box is § 11's clipping trap on the vertical axis**, and it cost a
real defect in Part 3: `align-items: center` pushes overflow to *both* ends and the **top** overflow is outside the
scrollable region, so on a landscape phone the top of a centred card is unreachable by any means (measured: 354 px
of card in a 260 px box, `scrollHeight` 323). Centre with **`my-auto` on the child** instead — an auto margin
resolves to 0 when there is no free space, so it centres and then degrades to top-aligned.

⚠️ **There is no `PENDING_PARTS` any more.** Every check in `web/scripts/check-responsive.mjs` is enforced. The set
still held `P7`/`P8` long after no check declared either, which made it read as the source of truth for what was
enforced while being inert — so a new check is either written to pass or the defect is fixed, never parked.

## § 16 Where each authority lives — read, don't re-derive

| Question | Authority |
|---|---|
| Custom variants (`coarse:`, `hover-hover:`, `dark:`), tokens, `--bottom-inset`, `.touch-target` | `web/app/globals.css` (each with its reasoning inline) |
| Shell, gutter, content width, bottom bar | `components/app-shell.tsx`, `components/bottom-nav.tsx` |
| Table → cards | `components/ui/card-list.tsx` |
| Page title, zone colour, filter chips | `ui/page-header.tsx` (**its controls go in `actions`, never a flex row around it** — the zone wash is sized to the header's own box), `lib/zones.ts`, `ui/list-toolbar.tsx` |
| An empty state inside a wide table | `ui/table.tsx`'s `TableEmptyRow` — a `colSpan` cell is as wide as the table, not the screen |
| Empty / filtered / failed | `ui/empty-state.tsx` |
| Status colour vs. zone colour | `ui/status-tone.ts` (status) vs `lib/zones.ts` (place) — never interchange |
| Money, dates, file sizes | `lib/format.ts`. Never hand-format a dinar; a date input defaults to `todayLocalIso()`, never `toISOString().slice(0,10)` |
| What each screen and component is | `web/CLAUDE.md`, `web/components/CLAUDE.md` |
| Why a device decision was made | `features/mobile-tablet-responsive/spec.md` (AC-1 … AC-51) |

One fact, one home. If this file and the code disagree, the code wins — and this file gets fixed in the same
change.

# Bilan — the day board stops lying, and the second demi-écran is recut by question

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-17

> Approved in conversation, not through `/define-small-feature`: the brief arrived as a bug report from the running
> app plus « repense la moitié basse », and the design was settled against a rendered mockup
> (`features/dashboard-bilan/mockups/01-bilan.html`) with three explicit choices — **Option A**, the customiser
> trigger **stays top-right with its panel reordered to page order**, and the four day-board zones **stay
> non-toggleable**.

## Problem

Two unrelated things collided on `/`.

**The day board states three falsehoods.** All three were reported from a real screen at ~11:59 with one 09:00–10:00
patient whose visit was `AwaitingClosure`:

1. The greeting read **« 🌙 Journée terminée · Bonne soirée »** at midday. `resolveDayTier` picks the `over` tier from
   `summary.isOver` alone, and that whole bank is written in the evening register. `isOver` is
   `nowMinutes >= lastPatientEnds`, so it is true from **10:00**. The phrase bank never reads the clock.
2. The same visit still read **« Au fauteuil … depuis 2 h 59 »**. `chairClaim` counts `awaitingclosure` as *started*
   and carries a deliberate **no staleness cutoff**. That was correct when `InProgress` was the only signal;
   `appointment-elapsed-status` then added `AwaitingClosure`, whose meaning is precisely *the slot ended and nobody
   confirmed presence* — and its spec lists `DashboardActivityReader` among the « deliberately unchanged », so this
   surface was never enrolled. `fixes-dont-propagate`.
3. **« 1 patient vu aujourd'hui »** renders `summary.count` — patients *booked*. The seen figure is `doneCount`. With
   `count = 1` and `doneCount = 0` the same séance was announced *vue* in the headline and *au fauteuil* directly
   beneath it, which breaks the module's own charter: « the phrase must be a true statement about today, verifiable
   one line down ».

**The second demi-écran was designed when it was the whole screen.** `dashboard-redesign` (7 Aug) gave it a filled
accent hero for « Net »; `dashboard-day-first` (14 Aug) then put a whole day board above it. What is left is: two
filled accent surfaces on one page with the louder one below the fold, **two** section-header primitives drawing the
same band, a period control that never says which dates it covers, a six-month trend chart sitting *inside* a
period-scoped zone it does not obey, « Répartition des actes » (activity) paired with a money chart, and nine
figures of equal weight.

And **« Personnaliser » describes the previous screen**: its six « À traiter » entries carry KPI-card labels for
cards that became chips in the *top* half, the trigger is the first element in the DOM — above everything it
commands — and four of the six day-board zones are not listed at all.

## What changes

### The three false statements

- **`lib/dashboard/day-summary.ts`** — `chairClaim` gains `CHAIR_OVERRUN_GRACE_MINUTES` (30): a *started* slot keeps
  the chair only through a plausible overrun. New `DaySummary` fields: **`needsClosure`** (the earliest patient slot
  past `endMinutes + grace` that is still `inprogress`/`awaitingclosure`), **`unclosedCount`**, and
  **`openToMinutes`** (the clinic's closing minute, for the evening threshold). `isOver` additionally requires
  `current === null`, so « programme terminé » can never coexist with someone in the chair.
- **`lib/dashboard/day-phrases.ts`** — the `over` tier splits into **`done`** (the programme is finished, but it is
  still the working day) and **`evening`** (past the clinic's own closing time, else 18:00). `resolveDayTier` and
  `buildDayPhrase` take **`nowMinutes`**. The sub-line counts `doneCount`, and says « patients vus » only when
  `unclosedCount === 0` — otherwise « N séances terminées — M à clôturer ».
- **`components/dashboard/day/now-next-cards.tsx`** — a third card state, **« Séance à clôturer »**, on `--warning`
  tokens, linking to `/a-cloturer`, rendered only when nothing claims the chair. Nothing is hidden (§ 0).

### The recomposition (Option A)

- One **period band** — `« Sur cette période »` with `« 1 – 17 août · comparé au mois dernier »` — built from the
  bounds the **server** returned, through the same conversion `dashboard-links.ts` uses for its query params, so the
  label and the links cannot disagree.
- **`components/dashboard/dashboard-section.tsx`** becomes the single header primitive: it absorbs `SectionBar`'s
  `href`/`action`, gains a `control` slot, and takes the accent dot. `SectionBar` in `page.tsx` is deleted.
- **`L'argent`** = a `Card` (title + praticien filter) over a full-bleed hairline `KpiGrid` whose lead cell is
  « Net », beside **« Encaissé — 6 derniers mois »** in its own card, which states that its last month *is* the
  « Encaissé » figure next to it.
- **`L'activité`** = the same shape, lead cell « Rendez-vous honorés », beside **« Répartition des actes »**.
- **`kpi-card.tsx`** — `KpiEmphasis` becomes `"lead" | "default" | "compact"`; the dead `wide` and `sparkline` props
  go with the hero. **`hero-kpi.tsx` is deleted** (its only caller was `netCard`).
- **`period-selector.tsx`** — full-width 3-up track below `sm:`, short visible labels (« Jour / Semaine / Mois »)
  with the full French kept as the accessible name.

### The customiser

- **`lib/dashboard-blocks.ts`** — three groups in **page order**: `journee` · `money` · `activity`, and a new
  **`form`** per block (`chip` · `list` · `figure` · `chart`) rendered as each row's sub-line, so a row says what it
  commands and where. **No key changes** — the server validates keys, not groups.
- The four day-board zones stay **out** of the panel, deliberately: they are what makes the day board a day board.

## Acceptance criteria

- **AC-1** A patient visit whose slot ended more than 30 min ago and is `AwaitingClosure` does **not** read
  « Au fauteuil »; a « Séance à clôturer » card names that patient and links to `/a-cloturer`.
- **AC-2** The same visit 10 min after its slot ended **still** reads « Au fauteuil » — a visit running long is the
  ordinary case.
- **AC-3** With the programme finished during working hours, the greeting is in the daytime register: no 🌙, no
  « Bonne soirée ».
- **AC-4** Past the clinic's closing time, the greeting is 🌙 / « Bonne soirée ».
- **AC-5** « N patients vus » appears only when nothing is left to close; otherwise « N séances terminées — M à
  clôturer ».
- **AC-6** « Journée terminée » never renders while a visit claims the chair.
- **AC-7** The period band names the window in French, from the server's own bounds.
- **AC-8** One section-header primitive; `SectionBar` no longer exists.
- **AC-9** No filled accent surface below the fold; every figure is still a `Link` to the records it counted.
- **AC-10** The customiser lists three groups in page order and every row states its form; hiding a chip still
  removes it from the top half.
- **AC-11** Usable at 320 px: the period track is full width and 3-up, the lead figure spans the row, the two
  card/chart pairs stack.
- **AC-12** `check:responsive`, `tsc --noEmit` and `build` clean; no dead imports left in `page.tsx`.

## Deliberately unchanged

`lib/dashboard-links.ts`'s mapping, `DashboardKpiKey`, `use-dashboard.ts`, `lib/api/dashboard.ts`, the server's
`DashboardKpiKeys.All`, every backend file, and the day board's four other zones.

## Residual, stated not hidden

The day board still has **no notion of Tunisian time** — every clock read is the workstation's, while
`AwaitingClosure` is written server-side through `ClinicClock` (UTC+1), so on a machine set to another zone the two
disagree and the 30-minute boundary shifts with it. `/a-cloturer` documents accepting the same drift; this feature
does not close it.

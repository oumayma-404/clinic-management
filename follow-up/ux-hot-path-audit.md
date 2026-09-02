# Hot-path UX audit — walked as a dentist

**Date:** 2026-09-02 · **Method:** real browser, signed in, 820×1024 (tablet portrait — the device this app
is used on most). Every number below is measured in the running app, not estimated.

**Why this exists.** A practising dentist trialled the app and reported seven problems. Six of them were the
same defect — *the app assumed everything was correct and final* — and one was *"add medical record was not
intuitive at all."* All seven are fixed. This audit asks the next question: with those fixed, would a dentist
leaving paper choose this, and where does it still fight them?

**The lens.** Four questions at every step, not "is it pretty":

1. Can I do the job — and in how many clicks?
2. **When I get it wrong, can I fix it?**
3. What is required that needn't be?
4. Is it legible at a glance on a tablet?

---

## What the research says a dentist actually wants

Consistent across sources, and narrower than "more features":

- **Click count in charting is the #1 complaint.** Speed is literally measured in "clicks, tabs, or keystrokes
  to complete a standard charting workflow." The recurring criticism: software "was designed by engineers
  rather than clinicians, and by the time a note is documented the appointment is running late."
- **One screen for the clinical moment** — document, view history, see images without leaving it.
- **Graphical charting readable and updatable chairside.** The odontogram is the centre of gravity.
- **Specialised charting includes periodontal charts**, not only odontograms.
- **Documentation must flow downstream** to treatment planning and billing without re-entry.
- **Reminders / two-way comms** — the highest-ROI non-clinical feature.

What stops a paper dentist switching:

- The belief that *"a task I already do easily will get harder."* **Not cost — inertia and fear of slowdown.**
- Unclear ROI, and **data portability** (54% name it a major obstacle).
- Fear of unreliability mid-consultation.

**Consequence for positioning:** the pitch that beats paper is not "more features" — it is *"faster than your
paper chart on day one, and your data is always yours."* Paper is infinitely forgiving: you scribble, cross
out, write in the margin. **Software that refuses a correction feels worse than paper.**

---

## The structural finding

> **Nothing in this app has a notion of "above the fold."**

| Screen | Content height | Screens of scrolling |
|---|---|---|
| Dashboard | 5040px | **5.2** |
| À clôturer | 5089px | **5.3** |
| Patient file | 3530px | **3.6** |
| Caisse | 3449px | **3.6** |

Nothing collapses — all five patient-file sections report `ALWAYS EXPANDED`. The good work in this product is
real, and it is **below the fold**.

---

## Fixed during this audit

**`78203d6f` — the post-visit reminder stopped interrupting.** With 23 séances awaiting closure it appeared on
every load, re-armed every 60 s, and **opened on top of an open dialog** — covering the two required fields of
the booking form, three times in one attempt. Three causes:

- **The guard existed for the toast path and not the dialog path.** The toast's own comment states it as a hard
  constraint — *"It must not appear over an open dialog or sheet"* — and honours it. The `Dialog` path, which a
  mouse gets, honoured nothing. A correct, documented rule wired to one of two call sites: this repo's
  signature defect.
- `refetch` did `setDismissed(false)` **every 60 seconds**. Sound only when nothing is usually pending; with a
  queue there is always "a different one".
- « Plus tard » snoozed **one id for one hour**. It means *not now*, never *not this patient*.

⚠️ **And a trap for the next person:** `data-scroll-locked` is set by Radix for *any* modal — including this
one. Written `open={… && !bodyBusy}` the guard is self-referential: the prompt opens, sets the lock, sees the
lock, closes, and leaves its overlay in the DOM at `data-state="closed"` **intercepting every click on the
page**. The app looks alive and answers nothing. The guard must **latch on open**, never police it.

Verified with the snooze fully cleared: one prompt on load, booking form alone on screen for 160 s (≈3 poll
cycles), and after dismissal `dialogs=0 overlays=0 bodyLocked=false` with the page clickable again.

---

## The doctor's feedback — verified genuinely fixed

| His complaint | State now |
|---|---|
| Total in fiche dentaire not editable | Editable, and typing **re-prices the acts** via `distributeSessionTotal` |
| Couldn't delete a mis-booked appointment | « Supprimer » behind a confirmation, and deliberately **not** an annulation — *"which counts in the taux d'absence"*. Server refuses if a fiche exists; toast says it's recoverable in « À clôturer » |
| Add-record screen too complicated | **Zero required fields** in the whole fiche. The code states the principle: *"a required extra tap on every fiche is how a field gets ignored"* |

---

## Path 3 — Fiche de soins: the best screen in the product

Build the advertising on this one.

- **A complete fiche is 3 clicks** (act → tooth → save). A general act with no teeth: **2 clicks**.
- **Zero required fields.**
- **One click on the act auto-fills Payé *and* Total** from the catalogue — no typing.
- Searchable act list grouped by category **with prices**, live "34/34" count, keyboard hints
  `↑↓ parcourir · ↵ choisir`.
- One-tap tooth ranges: **Haut / Bas / Toute la bouche / Vider**.
- `forfait` vs `/ dent` pricing toggle. "Changer d'acte" to undo.
- Best microcopy in the app: *"aucune — tapez sur le schéma, ou laissez vide pour un acte général
  (détartrage, panoramique…)"* — says it is optional **and** gives examples.
- After the act is picked, **everything fits without scrolling** (737px in a 713px scroller), both arches and
  the legend visible.

Also genuinely good elsewhere: the dashboard greeting (*"Rideau pour aujourd'hui — 7 séances terminées, 5 à
clôturer. Bonne soirée."*), the day strip showing **"1 h 45 libre"**, the summary in real dental language
(*"6 h 40 au fauteuil · 83 % de la journée · fin prévue 16:45"*), **search by phone number works**, the
`À COMPLÉTER` card inside the patient file (the *right* way to surface a worklist), and accessibility labels
like *"Voir le lundi 31 août en vue Jour — 5 rendez-vous"* / *"cabinet fermé"*.

---

## What's wrong — the dentist's view

**1. My odontogram can't do periodontics.** `ToothState` holds `ToothNumber / Condition / Surfaces / Note /
TreatmentDate`. No pocket depths, no bleeding on probing, no recession, no mobility, no furcation. But
*"Traitement parodontal (surfaçage / curetage)"* is in the act catalogue and on today's schedule — **so perio
work is billed with no perio record behind it.** Can't justify it to the patient, can't show improvement at
recall, can't defend it if challenged. *(The `parodont|perio` grep hits were false positives — they match the
word "period" in `SubscriptionPeriod`.)*

**2. Planned and done can't be seen together.** The odontogram splits `Diagnostics` / `Actes réalisés` into
**tabs**. Presenting a treatment plan *is* showing "here's what's wrong, here's what we'll do" in one picture.
Tabs make it two. (`Créer un plan depuis l'odontogramme` is excellent — the view is wrong, not the wiring.)

**3. No tooth-level history.** The most frequent clinical question is "what's the history on 26?" Answering it
means reading six fiches, five per page, in a table whose action column is off-screen.

**4. The patient header omits the two things that matter most to an owner:** **what they owe** and **when
they're next in**. It has age, sex, phone, allergies. "Leila owes 340 DT" must be on screen *before* she
stands up, not derived from six `Reste` values across two pages.

**5. Allergies are the only prominent medical alert.** Anticoagulants, diabetes, endocarditis prophylaxis,
pregnancy, bisphosphonates change what can be done **today** — and sit at 2988px, three screens down, in a
section that never collapses.

**6. An empty slot is a dead end.** The dashboard computes **"1 h 45 libre"** — it has *identified lost
revenue* — and it is **plain text, not clickable** (verified). `Liste d'attente` sits in the sidebar with 4
people in it. The app knows there is a gap, knows who is waiting, and connects them with nothing.

**7. No no-show warning at booking.** `taux d'absence` is computed somewhere, but booking Nadia into a
45-minute slot says nothing about "missed 3 of her last 8".

---

## Measured defects

### Agenda
| # | Finding |
|---|---|
| 1 | **Colour encoding inverted.** Act type gets the strongest channel (full card colour) across **19 acts** — far past the ~8 perceptually distinguishable. Status (planned/confirmed/in-progress/done/cancelled/absent), which decides what to do next, gets a 3px right border. |
| 2 | **Filter badge cries wolf.** Shows "2" with both toggles **ON**, i.e. nothing hidden. A count on a filter icon reads as "you're not seeing everything." |
| 3 | **Half the week off-screen at 820px.** Day headers at x=821 (VEN), 941 (SAM), 1061 (DIM). Today's column splits per-practitioner, so names render "Y…", "A…" — illegible on the column that matters most. |
| 4 | **~40% of the grid is empty time** — appointments cluster 10:00–16:15 in a 09:00–20:00 grid. |
| 5 | **Sidebar takes 31% of a tablet screen** (255 of 820px), 18 items, clips "Abonnement". |

### Patient file
| # | Finding |
|---|---|
| 6 | **The `ACTIONS` column is clipped off-screen.** Table is 515px inside a 451px container. The buttons **are rendered** (`variant=ghost`, `justify-end`, 6 cells/row) and focusable — just pushed outside the visible width. **This is almost certainly the reported "edit medical record was not allowing edits."** |
| 7 | **Every amount struck through** on a column headed *"MONTANT PAYÉ"*. Intentional (`invoiced` → strikethrough) but strikethrough universally means **void/cancelled**. Its only explanation is a `title` tooltip — **which does not exist on touch**, i.e. on the tablet. |
| 8 | **Intake data owns the last third** (2408→3530px), nothing collapses, **81 visible buttons** on one page. |
| 9 | **5 fiches per page** of clinical history, inside a page already 3.6 screens tall. |
| 10 | **Three nested scrollbars at once**: page (3530/968), sidebar (966/910), À-compléter card (294/108). |

### Dashboard / À clôturer / Caisse
| # | Finding |
|---|---|
| 11 | **"À clôturer" counts three different things.** Greeting **5**, À TRAITER **62**, popup queue **23**. Reconciled against the database: 5 = today, 61 = past-without-fiche, 23 = `Status=4`, 178 = any-without-fiche. Two unqualified numbers on one screen that cannot be reconciled — a dentist will trust neither. |
| 12 | **À clôturer: 5.3 screens, 87 buttons** across 7 day-groups back to "IL Y A 7 JOURS". |
| 13 | Dashboard is 5.2 screens with **4 six-month charts** — a *reporting* dashboard where the morning question is "who's coming, what's outstanding". Mitigated by **"Personnaliser"**, which makes it opt-in. |

---

## Enhancements, by value

| Add | Why it earns its place |
|---|---|
| **Periodontal chart** (6 depths + BOP + recession per tooth) | The treatment is already sold; this is the record behind it. Clearest "more serious than paper" signal |
| **Gap → waiting list, one tap** | Direct revenue. Data already exists on both sides |
| **Balance + next visit on the patient header** | Stops money walking out of the door |
| **Tooth-level history** — tap 26, see everything ever done | The question dentists actually ask |
| **Planned + done in one odontogram view** (overlay, not tabs) | This *is* the treatment-plan conversation |
| **Persistent medical-alert strip** while charting | Allergies handled; the rest that change today's treatment are not |
| **No-show risk at booking time** | One number, large behavioural effect |

## Fix order

1. **Un-clip the `ACTIONS` column** — a reported bug still live, one line of layout.
2. **Strikethrough on money** → a badge that survives having no hover.
3. **Collapse the three `Informations` blocks** — 3.6 screens → ~1.5, odontogram at the top.
4. **One definition of "à clôturer."**
5. **Swap the agenda colour encoding** — status as fill, act as secondary.
6. **Make the day-gap actionable.**

---

## Not yet examined

Recorded honestly so nobody assumes coverage this audit does not have:

- **`Factures`** — never opened, and *"editing a facture did not work"* was one of the seven reported bugs.
- **Taking a payment** in la caisse, end to end.
- **Creating a patient** — required fields on the intake form, duplicate-phone detection.
- **Finishing a booking** — and the click count from the agenda to an open fiche mid-consultation, which is
  the real chairside number ("3 clicks" counts only from inside the modal).
- **Editing an existing fiche** — the clipped `ACTIONS` button was found but never clicked.
- **Day one on an empty database** — no patients, no history. This is the actual conversion path for a dentist
  leaving paper, and it has not been looked at.
- **390px (phone).** Only 820px was tested.
- Treatment plans / devis, Documents, Laboratoire, Stock, Rappels.

---

# Second pass — the pages the first pass skipped

Walked 2026-09-02, same method. **Corrections to the first pass are marked ⟲.**

## 🔴 Factures — the three headline numbers do not reconcile

```
TOTAL FACTURÉ      31 787 200 DT
TOTAL ENCAISSÉ     31 881 500 DT     ← 94 300 MORE than was ever invoiced
RESTE À RECOUVRER     395 700 DT     ← positive, though encaissé already exceeds facturé
```

The reconciliation hides in 8pt grey under the middle figure: *"paiements de notes **et échéances de devis**"*
— it includes treatment-plan instalments, which are not invoices. So three figures sit side by side inviting a
subtraction that is apples-minus-oranges, and the middle one is larger than the left.

**"Am I owed money?" is *the* owner question, and this page answers it with three numbers that contradict each
other.** Either label the scope of each figure on the tile, or show `facturé → encaissé → reste` over one
consistent scope and put plan instalments in their own tile.

Also on that page: the date filters default to **empty**, so every KPI is all-time — a meaningless basis for a
clinic. Default to the current month. And `RESTE 0,000 DT` prints on every `Payée` invoice, which is a wasted
line on the most common row.

## 🔴 The patient file's navigation is below the fold — and it changes the earlier diagnosis

⟲ **The first pass reported "3530px, 7 sections, nothing collapses" and called for collapsing the three
`Informations` blocks. That was measuring one tab and calling it the page.**

The patient file has **two** tablists:

| tablist | y | contents |
|---|---|---|
| 0 | 757px | `Diagnostics · Actes réalisés` (inside the odontogram) |
| 1 | **1308px ←— below the fold** | `Dossiers médicaux · Plan de traitement · Rendez-vous · Notes · Documents · Fichiers · Factures` |

A dentist opening a patient file sees a header, a worklist card and an odontogram, with **no indication that
seven sections exist**. I was hunting for structure on purpose and still missed it.

It compounds: the `créances` tombstone directs you to *"onglet « Factures »"* — a tab at 1308px on a page 3.6
screens tall.

**Fix: move tablist 1 directly under the patient header.** This supersedes "collapse the Informations blocks":
it reveals seven sections that already exist, puts the patient's balance one click away, lets the odontogram
own its own tab instead of dominating the default view, and cuts the default view from 3.6 screens to ~1.

## ⟲ Withdrawn: "orphaned pages"

`/creances` and `/recurring-series` are **deliberate, well-built tombstones**, not orphans:

> **Page retirée** — La planification de séries de rendez-vous a été retirée. Les rendez-vous se créent un par
> un depuis l'agenda. → *Retour à l'agenda*

> **Page retirée** — Le suivi des créances a été retiré. Le solde d'un patient reste consultable depuis sa
> fiche, onglet « Factures ». → *Retour à l'agenda*

Clean explanation, a way out, route kept so old links don't 404. `lib/nav.ts:173` records the reasoning. Good
practice — keep the pattern.

**But the two functional losses are real, and a dentist feels both:**

- **No clinic-wide receivables list.** Factures shows `RESTE À RECOUVRER 395 700 DT` as a single dead
  aggregate. To chase it you open patients one at a time. "Who owes me money" is a practice-level question.
- **No recurring series.** An orthodontic patient attends every 4–6 weeks for ~18 months (≈15 visits); perio
  maintenance is quarterly; a child's recall is 6-monthly. *"Les rendez-vous se créent un par un"* means 15×
  the booking work for exactly the patients who generate the most predictable revenue.

## Plans de traitement — strong page, three gaps

Best explanatory copy in the product, and it teaches the undo:

> *"Un acte passe à « Réalisé » à l'enregistrement de la fiche de soins liée — il n'y a pas de bascule
> manuelle. Un acte coché par erreur se corrige avec « Détacher la fiche », qui le ramène à « Prévu » et
> réouvre le devis si celui-ci s'était clos dessus."*

`Devis PDF` and `Envoyer par e-mail` at the top are exactly the two things done with a quote; `Total / Encaissé
/ Reste / Actes réalisés 0/3` are the right four figures; a cancelled plan shows its `Motif d'annulation`.

| Gap | Why it matters |
|---|---|
| **`ACTION` column is empty on every row** (verified: table fits, cells contain no controls — the plan is `Annulé`, so there are no actions). Defensible logic; a column headed ACTION containing nothing reads as broken. Hide it when no row has an action. |
| **No instalment schedule**, although la caisse's own subtitle proves `échéances de devis` exist in the money model. A 300 000 DT plan in Tunisia is commonly paid in instalments — the schedule belongs on the plan. |
| **No patient-facing view.** The devis conversation happens with the patient looking at the screen; turning it currently shows them internals like *"Motif d'annulation : Devis de test QA"*. A « présenter au patient » mode — big totals, no internals, the odontogram with planned work — would sell treatment. |

## 390px (phone) — the responsive contract holds

Measured on Agenda, Patient, Factures, Caisse: **`docOverflow = 0px` on all four** — nothing scrolls sideways,
and tables become cards. Sidebar → bottom nav (`Accueil · Agenda · Liste · Patients · Plus`), `Exporter`
correctly demoted to an icon (the *opposite* of the desktop complaint — the mobile hierarchy is right),
primary action full-width. This part is genuinely well done.

Two real issues:

- **The `À COMPLÉTER` card keeps its own nested scroller on a phone** (3 of 8 visible + `Tout afficher (8)`).
  Nested scrolling on a touch screen means swiping moves the wrong thing. Show all 8 and let the page scroll.
- **Act names truncate to ~8 characters** (`Couron…`, `Réparat…`) while the act name *is* the information.
  Wrap to two lines, or put the button beneath the row.

⟲ **Two findings withdrawn after verification** — recorded so nobody re-files them:

- The dark "N" disc overlapping `Accueil` in the bottom nav is `<nextjs-portal>`, the **Next dev-mode
  indicator**. Not in production.
- "62 of 67 touch targets under 40px" is a **measurement artefact** — it flags padded 36px icon buttons and
  inline text links. Scripted touch-target audits are ~99% false positives; judge these by eye.

## Still not examined

- **Taking a payment** in la caisse, end to end.
- **Creating a patient** — required fields, duplicate-phone detection.
- **Finishing a booking**, and the click count from agenda → open fiche mid-consultation (the "3 clicks" figure
  counts only from inside the modal).
- **Editing an existing fiche** — the clipped `ACTIONS` button was found but never clicked.
- **Day one on an empty database.** Still the biggest hole in this audit: it is the actual conversion path for
  a dentist leaving paper.
- Documents, Laboratoire, Stock, Rappels, Chèques.

---

# Third pass — `/patients`, `/waiting-list`, `/a-cloturer`

## 🔴🔴 `/a-cloturer` cannot clear its own backlog

The page's subtitle promises three dimensions: *"62 séances en attente d'une présence, **d'une fiche** ou d'un
encaissement"*, and the per-row chips are excellent — `● Venue` / `○ Fiche` / `○ Encaissement`, filled for
answered, hollow for pending. You can see at a glance what is missing.

Then the buttons. Tallied across the 25 rendered rows:

```
25 × Venu     [default = primary blue]
25 × Absent   [outline]
25 × Retirer  [ghost]

any button whose label mentions « fiche »: FALSE
```

**Actions exist for one dimension out of three, and it is the one already answered on every visible row.** The
primary blue call-to-action on all 25 rows is `Venu`, which the chip beside it reports as done. The two hollow
chips — the actual outstanding work — have no button at all.

To record a fiche the dentist must leave this page, find the patient, and add the record: **62 round trips for
a 62-item worklist.**

**This is the root cause behind the popup fixed in `78203d6f`.** 23 séances sat in `AwaitingClosure` long
enough to justify a once-a-minute interrupt *because the worklist that exists to clear them cannot clear
them.* Fixing the interrupt treated the symptom; this is the disease.

Two more, same page:

- The only bulk action is **`Retirer les 25 séances affichées`** — the destructive one got bulk treatment while
  the constructive one has none.
- ⟲ **Finding #11 sharpened.** The count discrepancy is now explained: the `Période` filter defaults to
  *"Toutes les dates"*, so this page says **62** while the dashboard greeting says **5** (today). Neither
  states its scope in its headline. Same word, two numbers, no way to reconcile them on screen.

## 🔴 The waiting list and the empty slot are one click apart and never meet

`/waiting-list` is the best-designed page in the product. Every entry carries exactly what gap-filling needs:

- **Priority** — `Haute` / `Normale` / `Basse`
- **CRÉNEAU SOUHAITÉ** — *"Cette semaine"*, *"Matin de préférence"*, *"Fin de mois"*
- **NOTE** — *"Douleur 36 — à caser dès qu'un créneau se libère"*, *"Sans téléphone — passe au cabinet"*
- **`Promouvoir en rendez-vous`** — one click per entry

Meanwhile the dashboard computes **"1 h 45 libre"** in the day strip, as plain text.

**So the app knows a patient is in pain and asking for the next free slot, knows there is a free slot, and
connects them with nothing.**

Verified that the link genuinely does not exist:

- `create-appointment-dialog.tsx` has an `onCreated` callback whose comment says *"e.g. waiting-list
  promote-and-book"* — **the wiring exists in the waiting-list → booking direction.**
- `appointment-calendar.tsx` and `app/appointments/page.tsx`: no reference to the waiting list.
- The dashboard's `libre` label links nowhere.

The missing piece is only the **entry point from the gap side**, which is how it happens in a clinic: a
cancellation lands, you look at your day, you see a hole, you want to know who to call. Cheap fix, direct
revenue.

## `/patients` — the list a practice lives in

Genuinely good: subtitle states both jobs (*"Rechercher un dossier, ou en créer un"*), **`Importer` beside
`Exporter`** (the portability answer 54% of dentists name as a blocker), search on *"Nom ou téléphone"* — and
the messaging icon is **crossed out for patients with no phone** and a chat bubble for those with one. The
affordance reflects actual capability; that is careful work.

| Finding | Evidence |
|---|---|
| **Tablet portrait gets the card layout, not the table.** A table exists at every width, but the CSS switch happens between 820 and 1180px: content is **2121px at 820px** vs **1146px at 1440px**. The one screen that most needs density gets ~2× the scroll, on the device the app is most used on. |
| **No sort control at any width.** `sort controls=[]` and `sortable headers=[]` measured at 390 / 820 / 1180 / 1440. A practice with 2 000 patients can search but never sort by name, last visit, or balance. |
| **`NAISSANCE 30/07/1989` and `37 ans` are the same fact twice**, using half the lines on every card — while **last visit** and **balance**, the two things a dentist actually scans for, are absent. |

## 🔴 `/api/connectivity` 404s every 15 seconds, by design

`POLL_INTERVAL_MS = 15_000`, and `ConnectivityController` gates on `ExposesTrustEndpoints`, so on
`HostedMultiTenant` the endpoint **is absent** and the probe 404s forever. Measured: 38 consecutive 404s in one
short session; a viewport resize alone produced 37.

- **240 red console errors per hour, per open tab** → ~1 900 per tab per clinic day
- ~1 900 pointless round trips a day per tab

The real cost is not bandwidth. **When a clinic reports a bug and support opens the console, they are looking
at a wall of 404s**, and a genuine error is invisible in it. The code comment defends the *request* ("it is
also the only thing that answers « is the clinic's server reachable at all »") and that is fair — but the
reachability probe does not need an endpoint that 404s. Point it at `/health` (public, exists on every
profile, outside `/api` and outside the rate limiter), or have `ConnectivityController` answer
`200 {internetReachable: null}` where it cannot report egress. Keeps the signal, loses the permanent red.

## Revised fix order

1. **Give `/a-cloturer` the two actions it is named for** — `Enregistrer la fiche` and `Encaisser` per row.
   This is the disease behind the interrupt already fixed, and it unblocks a 62-item backlog.
2. **Move the patient file's tab bar above the fold** (currently y=1308).
3. **Un-clip the `ACTIONS` column** in the patient file's fiches table (515px in 451px).
4. **Link the day-gap to the waiting list** — wiring already exists in one direction.
5. **Make Factures' three KPIs reconcile**, and default the period to this month.
6. **Strikethrough on money** → a badge that survives having no hover.
7. **One definition of "à clôturer"**, scope stated in every headline.
8. **`/api/connectivity`** — stop the 15-second 404.
9. **Patient list**: table at 820px, add sort, swap birthdate for last-visit + balance.
10. Agenda colour encoding, filter badge, tablet week view.

---

# Fourth pass — `/caisse`, empty states, day one

## La Caisse is much better than Factures — and that gap is the finding

Same product, two standards for the same job:

| | Caisse | Factures |
|---|---|---|
| Date range | **Pre-filled** `01/09 → 02/09`, `Aujourd'hui` button, stated in prose in the header | **Empty** → all-time totals |
| KPI honesty | the `NET` tile **states its own formula**: *"encaissé – avoirs – dépenses"* | three figures that don't reconcile |
| Payment modes | `DONT: Espèces / Chèque / Carte / Virement`, with amounts | — |

Whatever review produced la caisse should be run over Factures.

### 🔴 Strikethrough means two different things, both about money

`Extrait de caisse` states the convention outright:

> *"Un mouvement annulé reste visible, **barré**, et ne compte pas dans le solde."*

So in la caisse, **struck through = cancelled, excluded from the balance.** On the patient file, struck
through = *"Facturé — le montant est géré par la facture"* (finding #7). **The same mark means "void" in one
place and "billed elsewhere" in another, on money.** This is worse than the tooltip problem: even a user who
learns the convention in la caisse will misread the patient file.

Minor, same page: the only primary action is `Nouvelle dépense`. There is no way to take a payment from la
caisse — payments arrive via an invoice or a fiche. Defensible, but "someone walked in and paid" sends a
receptionist to la caisse first.

## Empty states — handled, with one missed opportunity

- **Empty agenda day:** grid drawn with hours plus *"Aucun rendez-vous"*. No blank void. Good.
- **Patient search, no match:** *"Aucun résultat pour « zzzzqqq » · Effacer la recherche · 0 patient"* — names
  the query, offers a way out. Good.

**The opportunity:** a receptionist searching for someone not yet in the system is *precisely* the moment to
offer creating them, with the name already typed. `Aucun résultat pour « Ben Ali »` should be followed by
**`+ Créer le dossier de Ben Ali`**. Saves a navigation and a re-type, on one of the most frequent actions in
a practice.

## ✅ Day one works — and it is advertisable

Every clinic in the database, **including the three with zero patients**, ships with:

| | new clinic |
|---|---|
| Dental act codes | **102** |
| Procedure types (with prices) | **19** |
| Medications | **25** |

**There is no empty-catalogue cliff.** A dentist can record a fiche and write a prescription on the first
morning, so the "3 clicks to a fiche" claim holds on a fresh install and not only on a demo database. This is
the direct answer to the literature's #1 adoption barrier — *"a task I already do easily will get harder"* —
and it should be said out loud in the marketing.

### `/setup` — well judged, two conversion levers missing

Good: *"Configurons votre cabinet en **3 étapes simples**"*; a stepper whose steps carry descriptions; **step 3
marked `facultatif` up front**; only three required fields (nom, gouvernorat, téléphone); a `+216 12 345 678`
placeholder; governorates as a select rather than free text; optional logo; and *"Vous avez déjà un code de
cabinet ? Rejoindre un cabinet →"* on the first screen for staff.

| Missing | Why it matters |
|---|---|
| **It never says what is already done.** Given 102 codes / 19 acts / 25 medications are pre-seeded, the wizard should say so — *"Votre catalogue d'actes, vos codes CNAM et vos médicaments sont déjà prêts."* Turns anxiety into confidence at the exact moment it is felt. A copy change. |
| **No "importer mes patients" step**, though `/patients` has an `Importer` button and 54% of dentists name portability as a blocker. An optional step 4 is a direct conversion lever. |
| **`Suivant` is clipped at y≈1015** on an 820×1024 screen — the primary action on the first screen a customer ever sees. |

## Method note for whoever continues this

Two traps cost time in this pass:

- **The session expires mid-walk** and the app redirects to `/login`; a scrape then silently captures Next's
  RSC payload from the login page and returns plausible-looking garbage. **Guard every probe** with
  `if (/\/login/.test(page.url())) throw` before reading the DOM, and cap output length.
- `refresh-session.mjs` + `browser_close` did **not** restore the session — the browser process is not
  restarted, only the page. Logging in through the form works; compute the TOTP **inside the page** with
  `crypto.subtle` (HMAC-SHA1) so there is no staleness window between generating the code and submitting it.

---

# Fifth pass — `/documents`, `/lab-orders`

## 🔴 The patient-first document flow was built for 1 template out of 6

`/documents` offers six templates, and they are the right six for Tunisia — with **`Arrêt de travail` on the
official `CNAM P 061` form** and **`Bulletin de soins CNAM` (BS1)** named explicitly. Naming the actual
Tunisian paperwork is a trust signal generic international software cannot match; say it in the marketing.

The patient file also has a `Documents` tab, and it is good:

> *"Documents médicaux — Ordonnances, certificats, lettres de liaison et bulletins CNAM enregistrés. Cliquez
> sur « **Ouvrir** » pour modifier ou réimprimer."*

Reprint and modify — the correctability principle again. Empty state is proper too.

**But the only action in that panel is `Nouvelle ordonnance`.** The panel *displays* all six document types and
can *create* one. For a dentist with the patient in the chair:

| From the chair, patient already open | |
|---|---|
| Ordonnance | ✅ patient-first, works |
| **Arrêt de travail** — asked for after most extractions | ❌ leave the patient → sidebar → pick template → find the patient again |
| **Bulletin de soins CNAM** — needed for most CNAM reimbursements | ❌ same detour |
| Lettre de liaison / Note d'honoraires / Certificat médical | ❌ same detour |

**The two most bureaucratically frequent documents in a Tunisian practice are on the wrong path**, and it is
the same defect shape as the popup guard: a correct pattern wired to one call site out of six. Fix: the patient
Documents panel offers all six, pre-filled with that patient.

Lesser point: `/documents` is a 2-up card grid, ~300px per card for a title, two lines and a `Créer >` text
link — a lot of screen for a six-item menu, and the card is not itself clickable.

## `/lab-orders` — the strongest secondary page

Genuinely good, and it earns a mention because a lab case blocking a fitting appointment is real money:

- **`En retard` badge** on an overdue case (`PRÉVU 26 août`, still `Envoyé`) — the single most valuable thing
  on a lab page, plus an aggregate count.
- **`STADE` as an inline dropdown on the card** — advance a case in one click, without opening anything.
- The fields a dentist actually tracks: `PROTHÉSISTE · DENT · COÛT · ENVOYÉ · PRÉVU · REÇU`, with `DENT`
  omitted rather than dashed for a full-arch appliance.
- Real prosthetic vocabulary: *Gouttière de contention maxillaire*, *Inlay-core coulé*, *Couronne zircone
  monolithique*.
- **A `Trier par` control** — which `/patients` does not have. Worth aligning: the lab page sorts, the patient
  list cannot.

Only real nit: patient names **are** links (`/patients/<guid>`, N13 holding) but are styled without underline
or colour, so they do not look clickable.

## ⚠️ Method note — my visual reads are wrong about 1 in 4 times

Five findings this audit were killed by verification *after* they looked certain from a screenshot:

1. `/recurring-series` "orphan page" → a deliberate, well-built tombstone.
2. The "N" disc over `Accueil` on mobile → `<nextjs-portal>`, the Next dev indicator.
3. "62 of 67 touch targets under 40px" → measurement artefact.
4. `/lab-orders` patient names "not clickable" → they are links.
5. `/lab-orders` "no aggregate en-retard count" → there is one.

Plus one bug I introduced and caught only by testing (the self-referential `data-scroll-locked` guard).

**Never file a finding from a screenshot alone.** Assert it against the DOM or the source first. The three
findings in this audit that matter most — the popup guard, the `/a-cloturer` missing actions, the patient-file
tab bar at y=1308 — all came from measurement, not from looking.

## Still not examined

- Create a patient; click the clipped `ACTIONS` edit button; take a payment end to end.
- A true empty-clinic sign-in (day one was inferred from seeded catalogues + empty states, not walked).
- `/dental-acts`, `/procedure-types`, `/medications`, `/settings`, `/securite`, `/users`, `/journal`,
  `/abonnement`, `/stock`, `/fournisseurs`, `/rappels`, `/cheques`, `/fichiers`, `/treatment-plans` (list).

---

# Sixth pass — the behavioural tests (edit a fiche, create a patient, take a payment)

## ⟲ CORRECTION: editing a fiche works. The bug is discoverability.

Earlier passes filed the clipped `ACTIONS` column as *"a reported bug still live, just relocated."* **That was
wrong.** Clicking through:

```
Dialog: "Modifier la fiche médicale"
Copy:   "Les sections qui portent une valeur sont déjà dépliées."
Inputs: 5 (4 editable, 1 locked)
Commit: "Enregistrer — 150,000 DT"
```

Two patterns here worth copying elsewhere: on edit, **only the sections holding a value are expanded**, and the
commit button **states the amount it will save.**

So the doctor's #1 complaint is genuinely fixed. What remains is that he almost certainly **never found the
button**:

- the table clips by **64px**, so the column is behind an undiscoverable horizontal scroll;
- the control is a **32×32 icon** whose only label is `title="Modifier le dossier"` — `aria-label` is null.

Both buttons do carry the `touch-target` class, so **no touch-target defect is claimed here.**

### 🔴 But the delete beside it is a real safety problem

`title="Supprimer la fiche de soins"`, `lucide-trash2`, 32px, `aria-label` null, **36px from the edit button.**
A destructive action on a clinical record, identified only by a hover tooltip — and **hover does not exist on
the tablet this app is used on.** Same failure mode as the strikethrough tooltip, but on a delete. Give it a
visible label, or move it behind the row's overflow menu.

## `Ajouter un patient` — the best "no unnecessary input" surface in the app

```
★ Prénom *                          ← required
★ Nom *                             ← required
  Numéro de téléphone (recommandé)
  Date de naissance (recommandé)
  E-mail (facultatif)
  Sexe (facultatif)
  Adressé par (facultatif)
```

**Two required fields, and three explicit tiers of optionality in the labels** — required / *recommandé* /
*facultatif* — plus the header line *"L'essentiel suffit à enregistrer le patient."* Verified: `Créer le
patient` is **enabled with only Prénom + Nom**. Advertise this.

Also good: abandoning the form raises *"Abandonner les modifications ? Ce que vous avez saisi n'a pas été
enregistré et sera perdu — **Continuer la saisie** / Abandonner"*, and no test row was persisted.

Minor: the dialog scrolls (**1101px in 722px**) for a form with two required fields; the *facultatif* block
could collapse behind « Plus de détails ».

### 🔴 No duplicate detection on the phone number

Typing `22334455` — which already belongs to Leila Gharbi — produces **no warning at all.**

Duplicate patient records are a clinical hazard, not an annoyance: the history splits, so the allergy, the
previous extraction and the outstanding balance are all invisible on the record being used. And reception
creates them constantly — patient rings, name is mis-spelled, nothing is found, a new record is made.

**The lookup already exists**: the header search finds patients by phone (verified). It simply is not run at
the moment of creation. **Fourth instance in this audit of a capability wired to one call site and not the
other** — the same shape as the popup guard, the patient-file documents panel, and this.

Fix: debounce the phone field against the existing search and offer
*"22334455 est déjà le numéro de Leila Gharbi — ouvrir sa fiche ?"*

## Money: the doctor's facture complaint is fixed, and the menu is contextual

⟲ **Withdrawn:** an earlier read of the invoice menu showed no payment action. That was a **paid** invoice.
The menu is contextual — on `2026-0091` (Reste 4,500 DT):

```
Voir le détail · Enregistrer un paiement · Corriger cette note · Établir un avoir · Télécharger le PDF · Envoyer par e-mail
```

A fully-paid invoice omits `Enregistrer un paiement` (nothing to collect) and instead offers, with the amount
and date **written into every button label**:

```
Télécharger le reçu du paiement de 90,000 DT du 2 sept. 2026
Envoyer par e-mail le reçu du paiement de 90,000 DT du 2 sept. 2026
Annuler le paiement de 90,000 DT du 2 sept. 2026
```

**`Corriger cette note` + `Établir un avoir` answers the reported "editing a facture did not work"** — two
distinct correction paths, and the credit note is the honest one for money already taken.

Remaining, and it is discoverability rather than capability: **la caisse has no payment entry point** (only
`Nouvelle dépense`). The capability correctly lives on the invoice, but "someone walked in and paid" sends a
receptionist to la caisse first.

## Near-miss tally: 6

Verification killed six findings that looked certain from a screenshot, plus one bug I introduced myself:

1. `/recurring-series` "orphan" → deliberate tombstone
2. mobile "N" overlapping `Accueil` → `<nextjs-portal>` dev indicator
3. "62/67 touch targets too small" → measurement artefact
4. `/lab-orders` names "not clickable" → they are links (N13 holding)
5. `/lab-orders` "no aggregate en-retard count" → there is one
6. invoices "cannot take a payment" → contextual menu, wrong invoice opened
7. (self-inflicted) the self-referential `data-scroll-locked` guard

Every finding that survived came from a DOM assertion, a source read, or a database query.

---

# Seventh pass — configuration & back-office (14 routes)

All 14 render, none are tombstones, no table is clipped.

| Route | Title | Screens | Primary action |
|---|---|---|---|
| `/procedure-types` | Types de procédures | 3.8 | Ajouter un type d'acte |
| `/dental-acts` | Actes dentaires | **6.4** | Ajouter un acte |
| `/medications` | Médicaments | 3.9 | Ajouter un médicament |
| `/rappels` | Rappels | 6.3 | Configurer les canaux |
| `/stock` | Stock | 1.9 | Ajouter un article |
| `/fournisseurs` | Fournisseurs | 1.2 | Nouveau fournisseur |
| `/cheques` | Chèques à encaisser | 5.1 | À encaisser / Encaissés |
| `/fichiers` | Fichiers | 1.4 | — |
| `/treatment-plans` | Plans de traitement | **1.0** | Nouveau plan |
| `/journal` | Journal d'activité | 4.5 | — |
| `/users` | Utilisateurs | **1.0** | Créer un compte |
| `/securite` | Sécurité | 3.8 | Régénérer les codes |
| `/abonnement` | Abonnement | 1.3 | — |
| `/settings` | Paramètres du cabinet | 4.5 | — |

`/dental-acts` at **6.4 screens** is the longest page in the product (102 seeded act codes).
`/treatment-plans`, `/users`, `/fournisseurs`, `/abonnement`, `/fichiers` all fit in ~1 screen — proof the
"everything is a long scroll" problem is not inherent to the design system.

## 🔴 CROSS-CUTTING: KPI tiles disagree with the lists beneath them

This is the third independent instance, so it is a theme rather than three bugs.

**`/rappels`** is the clearest case. On one screen:

```
subtitle:   "26 messages sur la période · SMS et WhatsApp"
ENVOYÉS  0  "aujourd'hui · filtre : toute la période"        ← self-contradictory
ÉCHECS   0  "7 derniers jours · filtre : toute la période"   ← self-contradictory
list below: "Échec" × 3
```

**A tile reads 0 failures while three failures are visible under it**, and each sub-label names two different
scopes at once. The intent is presumably *"the count is today's; the list filter is the whole period"* — but
written as one line it contradicts itself.

The other two instances:

- **`/a-cloturer`**: dashboard greeting says **5**, the page says **62** — neither headline states its scope
  (today vs. `Période = Toutes les dates`).
- **`/factures`**: `TOTAL FACTURÉ` 31 787 200 < `TOTAL ENCAISSÉ` 31 881 500, with `RESTE À RECOUVRER` still
  positive — three figures over three different scopes, inviting a subtraction that is invalid.

**Why it matters more than it looks:** this product leans heavily on summary tiles, and a number that
disagrees with the list beneath it does not just mislead about that number — it teaches the dentist to
distrust *every* number on the screen. For a clinic owner asking "am I owed money" and "did my reminders go
out", that is the difference between using the app and phoning the desk.

**Fix as one job:** every tile states its own scope, and the scope matches the list it sits above (or the tile
is explicitly labelled as a different period).

## `/rappels` — otherwise, some of the best work in the product

- **`BLOQUÉS 0 — un réglage à changer`**: names the cause *and* implies the fix, in four words.
- **The WhatsApp quota card is exemplary.** Badge *"WhatsApp n'est pas connecté"*, then: *"Ce mois-ci n'a pas
  encore été mesuré : nous n'avons pas de relevé pour votre cabinet. **Vos rappels ne sont pas bloqués pour
  autant** — contactez-nous si cela persiste."* and *"Ce forfait ne concerne que les rappels **WhatsApp**. Vos
  rappels SMS ne sont pas comptés et continuent normalement."* It distinguishes « we cannot measure » from
  « you are blocked », and separates the two channels. A lesser product shows "quota unavailable" and panics
  the dentist.
- **The best error copy in the app**: *"Fatma Zouari — Échec — **Rendez-vous déjà passé — rappel obsolète, non
  envoyé**"*. What failed, why, and that no harm was done.

Two nits: *"WhatsApp n'est pas connecté"* is a badge, not a link — it should be the way to fix it; and the
reminders feature is the highest-ROI item in the research, so its configuration deserves a place in `/setup`
rather than only here.

---

# Coverage — read this before trusting the completeness of the above

## Walked and challenged (screenshots + DOM/source/DB assertions)

`/appointments` · `/patients` · `/patients/[id]` · the fiche modal · the edit-fiche modal ·
`Ajouter un patient` · `/` (dashboard) · `/a-cloturer` · `/caisse` · `/factures` + invoice menus and detail ·
`/treatment-plans/[id]` · `/waiting-list` · `/documents` + the patient Documents panel · `/lab-orders` ·
`/rappels` · `/setup` · `/creances` + `/recurring-series` (tombstones) · empty states · 390 / 820 / 1180 / 1440 px

## Measured only — NOT looked at

**13 routes** carry metrics in the seventh-pass table (title, screens, primary action, table-fits) and nothing
more. Nobody opened them:

`/procedure-types` · `/dental-acts` · `/medications` · `/stock` · `/fournisseurs` · `/cheques` · `/fichiers` ·
`/treatment-plans` (list) · `/journal` · `/users` · `/securite` · `/abonnement` · `/settings`

⚠️ **This is the exact shortcut that hid the biggest finding in this audit.** `/a-cloturer` was first reported
from metrics as *"5.3 screens, 87 buttons"* — and only a later pass that actually looked at it found that the
page cannot clear its own backlog, which turned out to be the root cause of the interrupt fixed in `78203d6f`.
Treat the 13 routes above as unexamined. The likeliest yield is `/dental-acts` (6.4 screens — the longest page
in the product), `/settings` (4.5), `/cheques` (5.1) and `/journal` (4.5).

## Never tested at all

- **Changing a tarif.** Every dentist's prices differ from the seeded ones, so "set my own prices" is a day-one
  task on the conversion path — and no one has checked that editing a price works, how many clicks it takes, or
  whether a changed tarif reaches an already-booked appointment (the repo's own `agreed-cost-reaches-the-fiche`
  note says pricing flows are where this app has been bitten before).
- **A true empty-clinic sign-in.** Day one was inferred from seeded catalogues plus empty states on a
  populated clinic, never walked as a new user with 0 patients.
- **Saving a fiche end to end** (the fiche modal was filled and measured, never submitted).
- **Recording a payment end to end** (`Enregistrer un paiement` was found in the menu, never clicked).
- **Keyboard-only operation**, and screen-reader output.
- **`/join`, `/signup`, `/mot-de-passe-oublie`, `/reinitialiser-mot-de-passe`, `/change-password`** — the whole
  self-signup and password-recovery path.

## Small item not previously filed

**The notification bell reads `99+`.** A count that can only mean "too many to matter" trains the user to
ignore the bell permanently — and the bell is where this product puts things it has decided not to interrupt
for (including, deliberately, the post-visit reminders). Either scope it to something actionable (today,
unread-and-relevant) or drop the number and show a dot.

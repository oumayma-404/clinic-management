# Hot-path UX audit — after the devil's-advocate pass

**Date** 2026-09-02 · **Method** real browser at 820×1024 + 390/1180/1440, then **every finding re-checked
against source, DB or both**. The second pass is the one that matters: **of 34 bugs filed, 6 survived. Of 14
big wins, 3 survived.**

**Trigger** A practising dentist trialled the app: *"pas intuitive du tout"*. His 7 defects are fixed (§5).

> **Why so much died.** This repo writes its decisions into the source. Almost every finding I filed was
> already answered in a comment next to the code I was judging — a feature that ships, a feature deliberately
> withdrawn, a disclosure I read as the defect it was added to fix, or a sequencing rule I read as a missing
> button. See §3; the pattern is the most useful output of this audit.

---

# 1 · BUGS — verified, with the evidence that survived challenge

> **All six are fixed** (B6 by the owner's decision: skipped — its cause is the deliberate promotion of the
> odontogram to lead the page, and every fix for it fights that decision). Verified with `npx tsc --noEmit`,
> `npm run check:responsive` (29/29, two new checks **proven red** on a deliberate violation first) and a
> browser pass at 320 / 390 / 820 / 1180 / 1440.
>
> **Fixing them turned up four more defects of the same shape, all fixed too:**
>
> | Found while fixing | Defect |
> |---|---|
> | B24 | `stock-table.tsx`'s history button was unlabelled **between two siblings that were already labelled** — the propagation failure in miniature |
> | B24 | The ordonnance editor's medication-remove had *no* label on any channel — no text, no `title`, no `aria-label`. It announced « bouton » |
> | B4 | The patient file's private 4-entry label map had no `honoraires` and no `arret-travail`, so a saved **arrêt de travail displayed its raw key** `arret-travail` |
> | B7's new check | Two more tables past the threshold: `recurring-series` (8 columns) and `archive-grants-card` (5) |
>
> `.claude/rules/frontend-web.md` § 1 and § 6 were **wrong** and are corrected in the same change: they put the
> table/cards threshold at « roughly eight or more columns », a figure sized for a table at page level. Nested
> in a `Card` inside a `TabsContent` — which is what most of this app's tables are — the box is 451 px, not
> ~532 px, and the real threshold is **five**.


| # | Sev | Bug | Verified how |
|---|---|---|---|
| **B7** | 🔴 | **`ACTIONS` column pushed outside the visible width at 820 px** — edit + delete render, are focusable, and can't be seen. **This is the doctor's "editing didn't work"** (it works — §5) | `TABLE_ONLY` is `hidden md:block`, so the **table** form is chosen from 768 px up, inside a container of ~450–520 px once the 255 px rail is subtracted. Measured: 515 px table in 451 px, 64 px hidden |
| **B24** | 🟠 | **`Supprimer la fiche de soins` is an unlabelled 32 px trash icon, 36 px from edit** — destructive, on a clinical record, and its only explanation is a `title` (**no hover on a tablet**) | `patients/[id]/page.tsx:1636` — `title=` set, `aria-label` null, `lucide-trash2` |
| **B23** | 🟠 | **Strikethrough means "voided" everywhere except one place, where it means "billed successfully"** — a user who learns one **actively misreads** the other, on money | 9 `line-through` sites keyed on `isVoided`/`isCancelled`; `patients/[id]/page.tsx:1473` **and** `:1562` strike a *successful* billing (`title="Facturé — le montant est géré par la facture"`) |
| **B6** | 🟠 | **Nothing says the patient file has 7 more sections.** The tab bar sits below the fold at 820×1024, so the page reads as one long scroll — the balance, the plan and the invoices look absent | Odontogram `<Card>` at `:1283`, `<Tabs>` at `:1321`. ⚠️ **The cause is deliberate** — the odontogram was promoted to lead the page ("the chart the whole consultation is read off"). **Fix the signal, not the order** |
| **B4** | 🟠 | **Patient Documents panel creates 1 of 6 document types** (`Nouvelle ordonnance`). `Arrêt de travail` + `Bulletin CNAM` — the two most frequent in Tunisia — mean leaving the patient, then re-finding them | panel actions = `["Nouvelle ordonnance"]` → `/documents/prescription?patientId=…`. The `?patientId=` pattern already works, so the fix is a button + a route per type |
| **B29** | 🟡 | **The reachability probe polls a route that is absent on this deployment.** `/health` is public on every profile and answers the same question | `ConnectivityController` returns `NotFound()` unless `ExposesTrustEndpoints`; `connectivity.tsx` polls every 15 s regardless. **Demoted:** the request *must* keep firing (it is also the reachability probe) and the state machine is correct — this is console noise, not misbehaviour |

## 1.1 How each was fixed

| # | Fix |
|---|---|
| **B7** | The patient file's Dossiers médicaux (6 cols), Rendez-vous (7) and Fichiers (5) moved to the `_LG` hinge, so an 820 px tablet gets the card form — whose menu carries the same actions **with visible text labels**, better than the icon-only table it replaces. Documents (4 cols) measured 451/451 with nothing hidden and **stays** on `md:`. `table-hinge-fits-its-box` now holds the threshold repo-wide |
| **B24** | `aria-label` on all five icon-only buttons, each naming its object (« Supprimer la fiche de soins du 12/03/2026 ») the way the delete confirmation already did. `icon-button-is-named` holds it — deliberately conservative: a `<Button>` whose children include any `{expression}` is skipped, so it reports only what is certainly unnamed |
| **B23** | One `BilledAmount` component, used by both the card and the table, replacing the strikethrough with a muted figure plus a « facturé n° 2026-0089 » badge carrying the invoice's own number. `line-through` now means « annulé » and nothing else, across all nine remaining sites |
| **B4** | `web/lib/documents.ts` is the single registry. The patient panel keeps « Nouvelle ordonnance » as a one-tap primary (it is far the most frequent) and adds « Autre document » for the other five — verified end to end: two clicks from the patient file to the CNAM P 061 form with the patient already attached |
| **B29** | `ConnectivityController` answers **200 with `InternetReachable = null`** instead of 404. The client needed no change: a body whose `internetReachable` is not a boolean already resolves to « signal absent », AC-63's third row. ⚠️ **Not** `/health` instead — that route deliberately carries no `lb_try_duration`, so it would report the API down during every rolling deploy, the exact false alarm the deploy work removed |

**Also real, but a missing control rather than a bug — not done:** `patients-table.tsx:213` hardcodes
`sort: 'RecentlyAdded'`. The API supports sorting; the UI never exposes it. Left alone deliberately: only two
sort values exist (`Name`, `RecentlyAdded`) and `IPatientRepository`'s own enum documents `RecentlyAdded` as
*"what « Patients » itself shows, so the fiche just created is the first row rather than somewhere under Z"* —
a considered choice, not an oversight. (Was B9.)

---

# 2 · WINS — verified, and not already built

> **All three shipped.** I12 was B4's fix (`59b5392e`); I2 and I5 followed. `tsc` clean,
> `check:responsive` 30/30, and walked in a browser at 320 / 390 / 820 / 1180 / 1440.
>
> | # | What shipped |
> |---|---|
> | **I2** | The day ribbon grows a « Plages libres à combler : » row — one chip per gap, largest first, at most three, and **only gaps of 30 min or more** (the booking dialog's own default length; a shorter sliver cannot hold a visit). Each chip links to `/waiting-list?slotDate=&slotTime=&slotMinutes=`, which names the slot in a dismissible banner and opens « Promouvoir » **at that hour** with the patient already selected. Verified end to end: chip → banner → dialog at **13:00, 03/09/2026, Mehdi Bouazizi**, duration still 30 |
> | **I5** | « Solde dû » in the patient header — `totalOutstanding` and nothing else. Verified it **reconciles with the rows beneath it** (31,000 DT header = two × 15,500 DT fiches), that a patient with no invoices shows **no line at all** rather than « 0,000 DT », and that a failed read also shows nothing (seen for real when the API went down mid-check) |
>
> ⚠️ **A defect found while verifying, in how the booking dialog is called.** It reads the clock off
> `defaultDate` when present and falls back to `defaultTime` only when it is absent, so passing both — a local
> midnight plus « 13:00 » — silently books **00:00**. Measured, then fixed by passing one instant. Anything
> else calling `CreateAppointmentDialog` with a slot must do the same.
>
> ⚠️ **Where I2 deliberately did *not* go.** The gap chips sit **under** the ribbon, not on the hatched blocks
> themselves: that track is `role="img"`, which makes its subtree presentational, so the `SlotBlock` links
> already inside it are focusable but invisible to a screen reader — a pre-existing defect a gap link would
> have deepened. A gap is also a percentage of the track's width, so on a phone it is a few pixels: untappable
> however it is labelled.


| # | Add | What we gain | Why it survived |
|---|---|---|---|
| **I2** | **Day-gap → waiting list, one tap** | Direct revenue, cheap. Both halves exist and **nothing connects them** | Dashboard computes `1 h 45 libre` as plain text. `/waiting-list` holds priority + `CRÉNEAU SOUHAITÉ` + `Promouvoir en rendez-vous`. `appointment-calendar.tsx` contains **no reference to the waiting list** |
| **I5** | **One balance figure on the patient header** — « solde dû », nothing else | *"Leila owes 340 DT"* on screen **before she stands up** | ⚠️ Nearly died: a « Solde patient » card was **deliberately removed** for showing six figures, two contradictory. It survives *narrowed* — because the removal's stated fallback was *"one click away in « Créances », the Factures tab, and the plan card"*, and **« Créances » has since been retired**. One figure ≠ the six that were removed |
| **I12** | **All 6 document types from the patient panel** | Fixes B4 — same work item | see B4 |

## Open questions for the dentist — not wins until he answers

**These are the whole remaining wins list.** Nothing else in §2 is outstanding.

The code says these are absent. **Nothing says he wants them**, and my track record on guessing that is now
2 for 5 (§3).

| Question to ask him | Why it's a question, not a finding |
|---|---|
| **Do you chart periodontal pockets today, on paper?** | No perio model exists (`ToothState` = `ToothNumber/Condition/Surfaces/Note`), and `Traitement parodontal` is billed with no perio record behind it. But a large build for a minority of general practices |
| **Would "missed 3 of her last 8" change what you do at booking?** | `absenceRate` exists **clinic-wide only** — per-patient is a new computation, not a wiring job as I first claimed |
| **When you want to know what's been done on tooth 26, where do you look now?** | `OdontogramActsChart teeth records` already exists inside the odontogram — a per-tooth view may be a filter on something built, not a new feature. **Unverified** |

---

# 3 · KILLED — and why each one died

**Verified FALSE — the capability ships, or the behaviour is deliberate and documented**

| Filed | Reality |
|---|---|
| **B1** `/a-cloturer` can't clear its own backlog *(was my headline bug)* | **`Ajouter la fiche`, `Encaisser` and `Rien à facturer` all exist.** The row shows **only the next unanswered step**, documented with its reason: *"a séance with no fiche has no acts to price"*. Every row I sampled was at `nextStep === "Presence"` |
| **B2** B1 is the root cause of the popup interrupt | Causal story false with B1. The popup fix (`78203d6f`) stands on its own — §5.2 |
| **B3** Only bulk action is destructive | A bulk « Venu » asserts 25 patients attended. The file states the principle: offering a question on a row that isn't asking it is the defect |
| **B5** Caisse has no payment entry | My own row admitted *"the capability is correctly on the invoice"*. Cash with no invoice is the thing the money-integrity work exists to prevent |
| **B11** Sidebar eats 31 % of a tablet | **It collapses** — `useSidebar()` → `isCollapsed`, `toggleSidebar`, with collapsed tooltips and `sr-only` labels |
| **B26** No duplicate detection on the phone | **`PatientDuplicateIndex` refuses duplicates before anything is written**, with « Créer quand même » as the opt-out. It fires **on submit** — I typed a phone, expected an inline warning, and never submitted |
| **B13** 5 fiches per page | Deliberate and documented: page size 5, no selector, *"the pager hides itself entirely below six fiches, which is most patients"* |
| **B16** Factures' 3 KPIs don't reconcile | The source says *"« Total facturé » and « Reste à recouvrer » DO match the rows to the millime"* and carries a hint naming Encaissé's wider scope. **The disclosure exists; I filed its typography as arithmetic** |
| **B17** `/rappels` tiles contradict themselves | `filterMeta: "toute la période"` was **added to fix exactly the confusion I reported** (*"a tile reading « 0 · aujourd'hui » filtered the whole date range and returned 22 rows"*). **I filed the fix as the bug** |
| **B18** "À clôturer" counts 4 unlabelled things | The greeting scopes itself in its own sentence — *"Rideau pour **aujourd'hui** … 5 à clôturer"* — and the page is a backlog. Different questions, both labelled |
| **B21** Bell reads `99+` | Dev-data artefact: 692 `StaffNotifications` over 24 days of **seeded history plus my own audit traffic**. Real for this database; unprovable for a clinic |
| **I3** Recurring series | **Retired on purpose** — the owner's dentists call it useless. I killed the tombstone as a false positive in §6, then argued to build the feature back |
| **I4** Clinic-wide receivables list | **Built, shipped, deliberately withdrawn.** `/creances`'s own subtitle is verbatim my request: *"Qui doit combien — soldes dus par patient (factures + échéanciers), les plus élevés en tête"*. Code kept intact behind the tombstone |
| **I7** Planned + done in one odontogram | **Already one picture.** Its own description: *"Cliquez sur une dent pour noter un diagnostic (à traiter) ; les actes réalisés s'ajoutent automatiquement"* |
| **I8** Persistent medical-alert strip while charting | **`PatientAlertPanel` ships** — allergies + active flags + **`medicalHistory`** (antécédents), at the top of the fiche modal body, *extracted* so the document editor would get it too |
| **I10** Devis patient-facing presentation mode | **`downloadDevisPdf` ships**, plus per-instalment receipts. My evidence was a *cancelled test plan* correctly showing its cancellation reason |
| **I11** Devis instalment schedule | **Fully built**: `Installment` + `InstallmentPayment` entities, `revise-installments-modal`, `installment-payment-modal`, `plan-timeline`. I cited la caisse's *"échéances de devis"* as proof the model *could* support it — it was proof it **already does** |

**Killed as taste, not defect** — B10 (cards at 820 px **is** the device contract) · B12 · B19 · B22 · B25
(act-colour vs status-colour is a design choice) · B27 · B30 · B31 (an empty grid is where you find a slot to
book) · B32 (a birthdate identifies, an age is clinical) · B33 · B34 (self-mitigated by `Personnaliser`).
Small wins S2–S4, S6, S8, S9, S12, S13 died with their bugs; S5, S7, S15 are the surviving bugs' own fixes.

---

# 4 · UNVERIFIED — do not act on these

Filed from a screenshot, never re-checked. **Given the hit rate above, treat as unfiled.**

B8 (week off-screen at 820 — `appointment-calendar.tsx:186` shows the author already reasoned about tablet
name width) · B14 (`/documents` cards not clickable) · B15 (`/setup`'s `Suivant` clipped) · B20 (filter badge
reads 2 with both toggles on) · B28 (`/lab-orders` link styling) · S1 · S10 · S11 · S14 · I13 · I14.

**13 routes were measured but never opened**, and that shortcut is what produced B1: `/procedure-types` ·
`/dental-acts` · `/medications` · `/stock` · `/fournisseurs` · `/cheques` · `/fichiers` · `/treatment-plans`
(list) · `/journal` · `/users` · `/securite` · `/abonnement` · `/settings`.

**6 things were never tested.** The biggest is **changing a tarif** — every dentist's prices differ from the
19 seeded ones, so it's a day-one conversion task, and nobody has checked it works, its click count, or
whether a change reaches an already-booked appointment. Then: true empty-clinic sign-in · saving a fiche end
to end · recording a payment end to end · keyboard + screen reader · signup / password-recovery.

---

# 5 · WHAT WORKS — verified, and safe to advertise

## 5.1 His 7 defects — all fixed

| Reported | State now |
|---|---|
| Edit medical record wouldn't edit | **Works** — `"Modifier la fiche médicale"`, 4 editable inputs, commit reads `Enregistrer — 150,000 DT`. Only barrier is **B7**: he never found the button |
| Editing a facture didn't work | **`Corriger cette note` + `Établir un avoir`** — two distinct correction paths |
| Couldn't delete a mis-booked appointment | `Supprimer` behind confirmation, deliberately **not** an annulation (*"which counts in the taux d'absence"*). Refused if a fiche exists; toast says it's recoverable |
| Act tarif not editable at booking | `appointment-negotiated-price` shipped |
| Total in fiche not editable | Editable, and typing **re-prices the acts** (`distributeSessionTotal`) |
| Add-record screen too complicated | **Zero required fields.** *"a required extra tap on every fiche is how a field gets ignored"* |
| Google import wrecked data | Import retired (`58aa957d`) |

## 5.2 Fixed during this audit — `78203d6f`

Post-visit reminder stopped interrupting. Three real causes: the guard existed for the **toast** path and not
the **dialog** path · `refetch` did `setDismissed(false)` every 60 s · « Plus tard » snoozed one id for one hour.

⚠️ **Trap for the next person:** `data-scroll-locked` is set by Radix for *any* modal — **including this one**.
`open={… && !bodyBusy}` is self-referential: the prompt opens, sets the lock, sees it, closes, and leaves its
overlay at `data-state="closed"` **intercepting every click on the page**. The guard must **latch on open**.

Verified: 1 prompt on load · booking form alone for 160 s (≈3 poll cycles) · after dismissal
`dialogs=0 overlays=0 bodyLocked=false`.

## 5.3 Advertisable, measured

| Claim | Measured |
|---|---|
| **Fiche in 3 clicks** (act → tooth → save); **2** for a general act | 1 click on the act auto-fills `Payé` **and** `Total`. ⚠️ measured *up to* the save button, not through it |
| **Zero required fields** in the whole fiche | verified |
| **A patient is saved with a first name and a last name** | `Créer le patient` enabled on 2 fields; 3 tiers of labels + *"L'essentiel suffit"* |
| **Works on day one — no empty-catalogue cliff** | every clinic incl. the 3 with 0 patients: **102 act codes · 19 priced acts · 25 medications** |
| **Your Tunisian paperwork, by name** | `Arrêt de travail` on **CNAM P 061** · **BS1** `Bulletin de soins CNAM` |
| **Correct a total and the acts re-price** | `distributeSessionTotal` |
| **Your data is yours** | `Importer` + `Exporter` + archive — the 54 % portability blocker |
| **A duplicate patient is refused before it is written** | `PatientDuplicateIndex`, with « Créer quand même ». *Found by trying to prove the opposite* |

## 5.4 Craft worth copying

- **Decisions live next to the code.** Withdrawn features keep their route, their screen and their reason
  (`/creances`, `/recurring-series`) so a bookmark doesn't 404 and restoring is re-pointing an export.
  **This is what made the audit's second pass possible** — and it is not normal.
- **Caisse:** the `NET` tile **states its own formula**; `DONT` splits Espèces/Chèque/Carte/Virement; dates
  pre-filled. **Run this page's review over Factures.**
- **Rappels:** `BLOQUÉS 0 — un réglage à changer` (cause + fix in 4 words). Best error copy in the app:
  *"Rendez-vous déjà passé — rappel obsolète, non envoyé"*.
- **Fiche microcopy:** *"aucune — tapez sur le schéma, ou laissez vide pour un acte général (détartrage,
  panoramique…)"* — says it's optional **and** gives examples.
- **Money buttons state amount + date:** *"Annuler le paiement de 90,000 DT du 2 sept. 2026"*. Invoice menu is
  contextual — `Enregistrer un paiement` only when there's a balance.
- **`À clôturer`** — chips `● Venue ○ Fiche ○ Encaissement` **and** one action for the one open question.
- **Waiting list** is the best-designed page in the app. **Patient file's `À COMPLÉTER` card** is the right way
  to surface a worklist.
- **390 px holds:** `docOverflow=0` on all 4 pages tested; `Exporter` demoted to an icon — **better hierarchy
  than desktop**.

## 5.5 Positioning (research)

Dentists' #1 complaint is clicks-to-document ✅ · graphical charting ✅ · documentation→billing with no
re-entry ✅ · reminders ✅ · portability (**54 % name it a blocker**) ✅ · **perio charting ❌** (open question) ·
**one screen for the clinical moment ⚠️** (B6/B7).

Barriers to leaving paper are *not* cost: fear that *"a task I already do easily will get harder"*, unclear
ROI, portability, fear of unreliability mid-consultation. **Paper is infinitely forgiving — software that
refuses a correction feels worse than paper.**

Positioning: *"faster than your paper chart on day one, and your data is always yours."*

---

# 6 · Method — how to not repeat this

**Read the comment next to the code before filing anything.** 17 of 20 killed findings were answered in a
source comment within 40 lines of the thing I was judging. The failure modes, in order of how often they bit:

| Failure mode | Count | Discriminator |
|---|---|---|
| The feature already ships | 4 | `grep` the domain entity **and** the component directory before writing "add X" |
| Deliberately withdrawn | 2 | A tombstone is a **decision**. Read it, don't route around it |
| I filed the fix as the defect | 2 | Confusing copy next to a `git blame` reason usually *is* the remedy |
| A sequencing rule read as a missing button | 3 | Sample rows in **more than one state** — all 25 of mine were at step 1 |
| Dev-environment artefact | 2 | Count the rows in the DB before quoting a badge; a 15 s poll cannot fire 37× in a resize |
| Screenshot never re-checked | 5 | See §4 |

**Browser traps:** the session expires mid-walk and a scrape then silently captures Next's RSC payload from
`/login` — guard every probe with `if (/\/login/.test(page.url())) throw` · `refresh-session.mjs` +
`browser_close` does **not** restore a session (the process isn't restarted); log in through the form and
compute TOTP **in-page** · Radix dropdowns need real pointer events (`el.click()` in `evaluate` does nothing) ·
two tablists on the patient page — target by `aria-controls`, `querySelector` returns the odontogram's.

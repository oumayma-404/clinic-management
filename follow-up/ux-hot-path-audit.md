# Hot-path UX audit

**Date** 2026-09-02 · **Method** real browser, signed in, 820×1024 (tablet portrait) + 390/1180/1440 checks.
Every number measured in the running app. **18 surfaces walked, 13 measured only, 6 things untested** — see
[§4 Coverage](#4--coverage).

**Trigger** A practising dentist trialled the app: *"pas intuitive du tout"*. His 7 defects are fixed (§3.1).
This audit asks what still fights a dentist leaving paper.

---

# 1 · BUGS — fix what we have

Severity: 🔴 blocks or misleads · 🟠 costs real time · 🟡 polish.

## 1.1 Workflow

| # | Sev | Bug | Evidence |
|---|---|---|---|
| B1 | 🔴 | **`/a-cloturer` cannot clear its own backlog.** Subtitle promises 3 dimensions (`présence · fiche · encaissement`); chips show `● Venue ○ Fiche ○ Encaissement`. Buttons exist for **1 of 3 — the one already done** | 25×`Venu`[primary] 25×`Absent` 25×`Retirer`; **any button mentioning « fiche » = FALSE**. 62 séances → 62 round trips via the patient file |
| B2 | 🔴 | **Root cause of the interrupt already fixed in `78203d6f`** — 23 séances accumulated *because* B1 blocks clearing them | see §3.2 |
| B3 | 🟠 | **Only bulk action is destructive** — `Retirer les 25 séances affichées`. No bulk constructive action | `/a-cloturer` |
| B4 | 🟠 | **Patient Documents panel creates 1 of 6 document types** (`Nouvelle ordonnance` only). `Arrêt de travail` + `Bulletin CNAM` — the two most frequent — need: leave patient → sidebar → template → re-find patient | panel actions = `["Nouvelle ordonnance"]` |
| B5 | 🟠 | **Caisse has no payment entry** (only `Nouvelle dépense`). Capability is correctly on the invoice; "someone paid" sends reception to la caisse first | — |

## 1.2 Findability

| # | Sev | Bug | Evidence |
|---|---|---|---|
| B6 | 🔴 | **Patient file's tab bar is below the fold** — 7 sections invisible on load. Page reads as one long scroll | tablist y=**1308px**, viewport 1024. The `créances` tombstone even directs users to *"onglet Factures"* |
| B7 | 🔴 | **`ACTIONS` column clipped** — edit + delete rendered, focusable, pushed outside the visible width. **This is why the doctor thought editing was broken** (it works — §3.1) | table 515px in 451px; 64px hidden h-scroll |
| B8 | 🟠 | **Half the week off-screen at 820px**; today's column splits per-practitioner so names render `Y…`, `A…` | day headers x=821 (VEN) 941 (SAM) 1061 (DIM) |
| B9 | 🟠 | **No sort control on `/patients`** at any width | `sort controls=[]`, `sortable headers=[]` @ 390/820/1180/1440 |
| B10 | 🟠 | **Tablet portrait gets cards, not the table** — 2× the scroll on the densest list | 2121px @820 vs 1146px @1440 |
| B11 | 🟡 | Sidebar takes **31% of a tablet screen** (255/820), 18 items, clips `Abonnement` | — |
| B12 | 🟡 | **3 nested scrollbars at once** on the patient file | page 3530/968 · sidebar 966/910 · À-compléter card 294/108 |
| B13 | 🟡 | **5 fiches per page** of clinical history, inside a 3.6-screen page | `1–5 sur 6 · Page 1 sur 2` |
| B14 | 🟡 | `/documents` — ~300px per card for a 6-item menu; card not clickable, `Créer >` is a text link | — |
| B15 | 🟡 | `/setup` — **`Suivant` clipped** on the first screen a customer ever sees | y≈1015 of 1024 |

## 1.3 Numbers that lie

| # | Sev | Bug | Evidence |
|---|---|---|---|
| B16 | 🔴 | **Factures' 3 KPIs don't reconcile.** Encaissé **exceeds** facturé by 94 300, yet Reste is still positive. Scope difference hides in 8pt grey (*"et échéances de devis"*) | facturé 31 787 200 · encaissé 31 881 500 · reste +395 700 |
| B17 | 🔴 | **`/rappels` tiles contradict themselves and the list.** Sub-labels name two scopes at once; a tile reads 0 failures above 3 visible ones | `ENVOYÉS 0 "aujourd'hui · filtre : toute la période"` · `ÉCHECS 0 "7 derniers jours · filtre : toute la période"` · `"Échec"×3` below · subtitle `26 messages` |
| B18 | 🔴 | **"À clôturer" counts 4 different things, unlabelled.** Greeting **5**, page **62**, popup queue **23** | DB: today=5 · past-without-fiche=61 · `Status=4`=23 · any-without-fiche=178 |
| B19 | 🟠 | **Factures date filters default empty** → all-time totals, useless for a clinic. (La caisse does this right) | — |
| B20 | 🟡 | Agenda **filter badge shows "2" with both toggles ON** — nothing hidden, but it reads "you're not seeing everything" | — |
| B21 | 🟡 | Notification bell reads **`99+`** — can only mean "too many to matter"; trains users to ignore the one place the product puts non-interrupting news | — |
| B22 | 🟡 | `RESTE 0,000 DT` printed on every `Payée` invoice — a wasted line on the most common row | — |

## 1.4 Signals that mislead

| # | Sev | Bug | Evidence |
|---|---|---|---|
| B23 | 🔴 | **Strikethrough means two different things, both on money.** La caisse documents it as *"annulé … barré, ne compte pas dans le solde"*; the patient file uses it for *"Facturé — géré par la facture"*. A user who learns one **actively misreads** the other | 5/5 rows struck on a col headed `MONTANT PAYÉ` |
| B24 | 🔴 | **Explanation lives in a `title`** — no hover on a tablet. Applies to B23 **and** to `Supprimer la fiche de soins`: a destructive action on a clinical record, 32px, `aria-label` null, **36px from edit** | `title` set, `aria-label` null, `lucide-trash2` |
| B25 | 🟠 | **Inverted colour encoding on the agenda.** Act type gets the strongest channel across **19 acts** (>8 distinguishable); status — which decides what to do next — gets a 3px border | filter legend: 10 + `+9 autres` |
| B26 | 🟠 | **No duplicate detection on the phone at patient creation.** Split histories hide the allergy, the prior extraction, the balance. **The lookup already exists** — header search finds by phone | typing `22334455` (Leila Gharbi) → no warning |
| B27 | 🟡 | Treatment plan: **`ACTION` column empty on every row** (plan is `Annulé`, so no actions). Hide the column when no row has one | table fits; cells contain no controls |
| B28 | 🟡 | `/lab-orders` patient names **are** links but carry no underline/colour — don't look clickable | `→ /patients/<guid>` |

## 1.5 Waste

| # | Sev | Bug | Evidence |
|---|---|---|---|
| B29 | 🔴 | **`/api/connectivity` 404s every 15s by design.** Endpoint gated off on `HostedMultiTenant`; probe fires anyway. **When a clinic reports a bug, support opens the console to a wall of 404s and real errors are invisible.** Fix: probe `/health` (public, every profile) or return `200 {internetReachable:null}` | `POLL_INTERVAL_MS=15_000` → **240/hour/tab ≈ 1 900/day**. 38 in one session; 37 from a resize alone |
| B30 | 🟠 | **Nothing collapses.** Dashboard 5.2 screens · À clôturer 5.3 · Patient file 3.6 · Caisse 3.6 · `/dental-acts` **6.4** (longest page). All 5 patient-file sections report `ALWAYS EXPANDED`. **Not inherent** — `/treatment-plans`, `/users`, `/fournisseurs` fit in 1 screen | 81 visible buttons on the patient file |
| B31 | 🟡 | ~**40% of the agenda grid is empty time** (bookings cluster 10:00–16:15 in a 09:00–20:00 grid) | — |
| B32 | 🟡 | Patient list: `NAISSANCE 30/07/1989` + `37 ans` = same fact twice, half the lines on every card | — |
| B33 | 🟡 | Add-patient dialog **scrolls (1101px in 722px)** for a form with 2 required fields | — |
| B34 | 🟡 | Dashboard is 5.2 screens with 4 six-month charts — reporting, not a daily driver. Mitigated by `Personnaliser` (opt-in) | — |

## 1.6 Cross-cutting causes

| Theme | Instances |
|---|---|
| **A correct rule wired to one call site** | popup guard (toast ✔ / dialog ✘) · patient Documents (1 of 6) · phone search (search ✔ / create ✘) · `/a-cloturer` (1 of 3 dimensions) |
| **No notion of "above the fold"** | B6 B30 B31 B33 |
| **Tiles disagree with their lists** | B16 B17 B18 |
| **Explanation in a `title`** | B23 B24 |

---

# 2 · IMPROVEMENTS — what to add

## 2.1 Big wins

| # | Add | What we gain |
|---|---|---|
| I1 | **Periodontal chart** — 6 pocket depths, BOP, recession, mobility, furcation | `ToothState` holds only `ToothNumber/Condition/Surfaces/Note`. **`Traitement parodontal` is already billed with no perio record behind it** — can't justify to the patient, show improvement at recall, or defend it. Clearest "more serious than paper" signal |
| I2 | **Day-gap → waiting list, one tap** | Dashboard computes **`1 h 45 libre`** (plain text). Waiting list holds *Mehdi Bouazizi, `Haute`, "Douleur 36 — à caser dès qu'un créneau se libère", "Cette semaine"* + `Promouvoir en rendez-vous`. **Wiring exists one direction** (`onCreated` comment: *"e.g. waiting-list promote-and-book"*); the agenda has no reference. Direct revenue, cheap |
| I3 | **Recurring series** (retired) | Ortho ≈15 visits/18 months · perio quarterly · 6-month recalls. *"Les rendez-vous se créent un par un"* = 15× the booking work for the most predictable revenue in a practice |
| I4 | **Clinic-wide receivables list** (retired) | `RESTE À RECOUVRER 395 700 DT` is a dead aggregate — chasing it means opening patients one at a time. "Who owes me" is a practice-level question |
| I5 | **Balance + next visit on the patient header** | Currently age/sex/phone/allergy only. *"Leila owes 340 DT"* must be on screen **before she stands up**, not derived from 6 `Reste` values across 2 pages |
| I6 | **Tooth-level history** — tap 26, see everything ever done | The most frequent clinical question. Today: read 6 fiches, 5/page, in a table whose actions are off-screen |
| I7 | **Planned + done in one odontogram view** (overlay, not tabs) | Presenting a plan *is* "here's what's wrong, here's what we'll do" in **one** picture. Tabs make it two. `Créer un plan depuis l'odontogramme` already exists — the view is wrong, not the wiring |
| I8 | **Persistent medical-alert strip while charting** | Anticoagulants, diabetes, endocarditis prophylaxis, pregnancy, bisphosphonates change what's possible **today** — currently at y=2988 in a section that never collapses. Allergies already handled well |
| I9 | **No-show risk at booking** | `taux d'absence` is computed somewhere; booking says nothing about *"missed 3 of her last 8"*. One number, large behavioural effect |
| I10 | **Devis: patient-facing presentation mode** | The devis conversation happens with the patient looking at the screen. Turning it now shows internals — e.g. *"Motif d'annulation : Devis de test QA"* |
| I11 | **Devis: instalment schedule** | La caisse's own subtitle proves `échéances de devis` exist in the money model. A 300 000 DT plan in Tunisia is normally paid in instalments |
| I12 | **All 6 document types from the patient panel** | Fixes B4. `Arrêt de travail` + `Bulletin CNAM` are the two most frequent in a Tunisian practice |
| I13 | **`/setup`: "importer mes patients" step** | `/patients` already has `Importer`; **54% of dentists name portability a top blocker** |
| I14 | **`/setup`: say what's already done** | *"Votre catalogue d'actes, vos codes CNAM et vos médicaments sont déjà prêts."* Turns anxiety into confidence at the exact moment it's felt — a **copy change** |

## 2.2 Small wins

| # | Add / change | Gain |
|---|---|---|
| S1 | `+ Créer le dossier de « Ben Ali »` in the empty patient-search state | Name is already typed. One of the most frequent actions in a practice |
| S2 | Move the patient tab bar under the header (fixes B6) | Reveals 7 existing sections, balance 1 click away, default view 3.6 → ~1 screen. **Supersedes "collapse the Informations blocks"** |
| S3 | Filter badge counts only actual restrictions (B20) | Stops crying wolf |
| S4 | Every tile states its scope, matching the list beneath (B16–B18) | One job fixes three instances |
| S5 | Probe `/health` for connectivity (B29) | Keeps the signal, loses ~1 900 daily errors |
| S6 | Patient list: swap birthdate for **last visit + balance** (B32) | Two facts a dentist scans for, replacing one shown twice |
| S7 | Patient list: table at 820px + sort (B9, B10) | Usable at 2 000 patients |
| S8 | Collapse the `facultatif` block in add-patient (B33) | Fits without scrolling |
| S9 | Hide `ACTION` when no row has one (B27) | Stops reading as broken |
| S10 | Make *"WhatsApp n'est pas connecté"* the link that connects it | Badge → fix, in place |
| S11 | Reminders configuration inside `/setup` | Highest-ROI feature in the research, currently only in `/rappels` |
| S12 | Bell: scope the count or show a dot (B21) | Bell becomes worth looking at |
| S13 | Underline `/lab-orders` patient links (B28) | Looks like what it is |
| S14 | `En retard` → a filter, not just a badge | Lab delays block fitting appointments |
| S15 | Visible label on `Supprimer la fiche de soins`, or move it behind the row menu (B24) | Removes a destructive unlabelled control |

---

# 3 · WHAT ALREADY WORKS — the sales argument

## 3.1 His 7 defects — all fixed

| Reported | State now |
|---|---|
| Edit medical record wouldn't edit | **Works.** `"Modifier la fiche médicale"`, 4 editable inputs, commit reads `Enregistrer — 150,000 DT`. Only barrier is B7 (clipped) — he never found the button |
| Editing a facture didn't work | **`Corriger cette note` + `Établir un avoir`** — two distinct correction paths |
| Couldn't delete a mis-booked appointment | `Supprimer` behind confirmation, deliberately **not** an annulation — *"which counts in the taux d'absence"*. Server refuses if a fiche exists; toast says it's recoverable in « À clôturer » |
| Act tarif not editable at booking | `appointment-negotiated-price` shipped |
| Total in fiche not editable | Editable, and typing **re-prices the acts** via `distributeSessionTotal` |
| Add-record screen too complicated | **Zero required fields.** Code states the principle: *"a required extra tap on every fiche is how a field gets ignored"* |
| Google import wrecked data | Import retired (`58aa957d`) |

## 3.2 Fixed during this audit — `78203d6f`

Post-visit reminder stopped interrupting. Three causes:

- **Guard existed for the toast path, not the dialog path** — the toast's own comment states it as a hard constraint (*"It must not appear over an open dialog or sheet"*); `Dialog`, which a mouse gets, honoured nothing.
- `refetch` did `setDismissed(false)` **every 60 s** — sound only when nothing is usually pending.
- « Plus tard » snoozed **one id for one hour** — it means *not now*, never *not this patient*.

⚠️ **Trap for the next person:** `data-scroll-locked` is set by Radix for *any* modal — **including this one**. `open={… && !bodyBusy}` is self-referential: the prompt opens, sets the lock, sees it, closes, and leaves its overlay at `data-state="closed"` **intercepting every click on the page**. The guard must **latch on open**, never police it.

Verified, snooze fully cleared: 1 prompt on load · booking form alone for **160 s** (≈3 poll cycles) · after dismissal `dialogs=0 overlays=0 bodyLocked=false`, page clickable.

## 3.3 Advertisable, measured

| Claim | Measured |
|---|---|
| **Fiche in 3 clicks** (act → tooth → save); **2** for a general act | verified; 1 click on the act auto-fills `Payé` **and** `Total` |
| **Zero required fields** in the whole fiche | verified |
| **Patient saved with a first name and a last name** | `Créer le patient` enabled on 2 fields; 3 tiers in the labels (*required / recommandé / facultatif*) + *"L'essentiel suffit à enregistrer le patient"* |
| **Works on day one — no empty-catalogue cliff** | every clinic incl. the 3 with **0 patients**: **102 act codes · 19 priced acts · 25 medications** |
| **Your Tunisian paperwork, by name** | `Arrêt de travail` on **CNAM P 061**; **BS1** `Bulletin de soins CNAM` |
| **Correct a total and the acts re-price** | `distributeSessionTotal` |
| **Your data is yours** | `Importer` + `Exporter` + archive — the 54% blocker |

## 3.4 Craft worth keeping (and copying)

- **Fiche microcopy:** *"aucune — tapez sur le schéma, ou laissez vide pour un acte général (détartrage, panoramique…)"* — says it's optional **and** gives examples.
- **Devis footnote:** explains that `Réalisé` is automatic and that a mis-tick is undone with `Détacher la fiche`. Teaches the model **and** the undo.
- **Edit dialog:** *"Les sections qui portent une valeur sont déjà dépliées."* Commit button states the amount.
- **Money buttons state amount + date:** *"Annuler le paiement de 90,000 DT du 2 sept. 2026"*. Invoice menu is **contextual** — `Enregistrer un paiement` only when there's a balance.
- **Caisse:** `NET` tile **states its own formula** (*"encaissé – avoirs – dépenses"*); `DONT` splits Espèces/Chèque/Carte/Virement; dates pre-filled. **Run this page's review over Factures.**
- **Rappels:** `BLOQUÉS 0 — un réglage à changer` (cause + fix in 4 words). WhatsApp card distinguishes *"we cannot measure"* from *"you are blocked"* and separates SMS. Best error copy in the app: *"Rendez-vous déjà passé — rappel obsolète, non envoyé"*.
- **Lab orders:** `En retard` badge + aggregate · **`STADE` inline dropdown** (advance in 1 click) · has a `Trier par` (which `/patients` lacks).
- **Waiting list:** priority + `CRÉNEAU SOUHAITÉ` + note + `Promouvoir en rendez-vous`. Best-designed page.
- **À clôturer chips** `● Venue ○ Fiche ○ Encaissement` — correct diagnosis (the actions are the bug, B1).
- **Dashboard:** *"Rideau pour aujourd'hui — 7 séances terminées, 5 à clôturer"* · day strip showing `1 h 45 libre` · *"6 h 40 au fauteuil · 83 % de la journée · fin prévue 16:45"* · `Personnaliser`.
- **Patient file:** `À COMPLÉTER` card = the *right* way to surface a worklist (contrast with the popup).
- **Tombstones:** `/creances`, `/recurring-series` — explanation + way out + route kept so links don't 404.
- **Empty states:** agenda draws the grid + *"Aucun rendez-vous"*; search names the query + `Effacer la recherche`. Unsaved-changes guard says *"Continuer la saisie"*, not "Cancel".
- **Details:** messaging icon **crossed out** for patients with no phone. A11y labels like *"Voir le lundi 31 août en vue Jour — 5 rendez-vous"*, *"cabinet fermé"*.
- **390px holds:** `docOverflow=0` on all 4 pages tested · tables → cards · bottom nav · `Exporter` demoted to an icon (**better hierarchy than desktop**). Two issues: À-compléter keeps a **nested scroller on touch**; act names truncate to ~8 chars (`Couron…`).

## 3.5 What the research says (positioning)

| Dentists want | Have it? |
|---|---|
| Fewest clicks to document — **the #1 complaint**, measured in clicks/keystrokes | ✅ 3 clicks |
| Graphical charting, readable chairside | ✅ odontogram + legend + one-tap ranges |
| Documentation → billing, no re-entry | ✅ act → fiche → facture → caisse |
| **One screen for the clinical moment** | ❌ 3.6–6.4 screens everywhere (B30) |
| **Periodontal charts** | ❌ (I1) |
| Never blocks a correction | ⚠️ mostly fixed; B7/B24 hide the controls |
| Reminders to cut no-shows | ✅ built |
| Data portability — **54% name it a blocker** | ✅ import/export/archive |

**Barriers to leaving paper:** fear that *"a task I already do easily will get harder"* (**not cost**) · unclear ROI · portability · fear of unreliability mid-consultation.

**Positioning:** not "more features" — *"faster than your paper chart on day one, and your data is always yours."* Paper is infinitely forgiving; **software that refuses a correction feels worse than paper.**

---

# 4 · Coverage

| | Surfaces |
|---|---|
| **Walked + challenged** (screenshots + DOM/source/DB assertions) | `/appointments` · `/patients` · `/patients/[id]` · fiche modal · edit-fiche modal · `Ajouter un patient` · `/` · `/a-cloturer` · `/caisse` · `/factures` + menus + detail · `/treatment-plans/[id]` · `/waiting-list` · `/documents` + patient Documents panel · `/lab-orders` · `/rappels` · `/setup` · `/creances` + `/recurring-series` · empty states · 390/820/1180/1440 |
| **⚠️ Measured only — NOT looked at** | `/procedure-types` · `/dental-acts` · `/medications` · `/stock` · `/fournisseurs` · `/cheques` · `/fichiers` · `/treatment-plans` (list) · `/journal` · `/users` · `/securite` · `/abonnement` · `/settings` |
| **Never tested** | changing a tarif · true empty-clinic sign-in · saving a fiche end to end · recording a payment end to end · keyboard-only + screen reader · `/join` `/signup` `/mot-de-passe-oublie` `/reinitialiser-mot-de-passe` `/change-password` |

⚠️ **The "measured only" row is the risk.** `/a-cloturer` was first reported from metrics as *"5.3 screens, 87 buttons"* — a later pass that actually looked found **B1**, the root cause of the interrupt in `78203d6f`. Likeliest remaining yield: `/dental-acts` (6.4 screens), `/cheques` (5.1), `/settings` (4.5), `/journal` (4.5).

⚠️ **Changing a tarif is the biggest untested gap.** Every dentist's prices differ from the 19 seeded ones, so it's a **day-one conversion task** — and nobody has checked it works, its click count, or whether a changed tarif reaches an already-booked appointment. The repo's own `agreed-cost-reaches-the-fiche` guard (N14) exists because pricing propagation has bitten this codebase before.

**"3 clicks to a fiche" is measured up to the save button, not through it.**

---

# 5 · Method notes for whoever continues

**Never file a finding from a screenshot alone.** Six died on verification after looking certain:

| Filed | Reality |
|---|---|
| `/recurring-series` orphan page | deliberate, well-built tombstone |
| "N" disc over `Accueil` on mobile | `<nextjs-portal>` — Next **dev-mode** indicator |
| 62/67 touch targets under 40px | measurement artefact (padded icon buttons, inline links) |
| `/lab-orders` names not clickable | they are links (N13 holding) |
| `/lab-orders` no aggregate `en retard` | there is one |
| Invoices can't take a payment | contextual menu — wrong (paid) invoice opened |

Plus one self-inflicted: the self-referential `data-scroll-locked` guard (§3.2). **Every finding that survived came from a DOM assertion, a source read, or a DB query.**

**Browser traps:**

- **The session expires mid-walk** → the app redirects to `/login` and a scrape silently captures Next's RSC payload, returning plausible garbage. **Guard every probe:** `if (/\/login/.test(page.url())) throw`. Cap output length.
- `refresh-session.mjs` + `browser_close` does **not** restore the session (browser process isn't restarted). Log in through the form; compute TOTP **inside the page** with `crypto.subtle` (HMAC-SHA1) so there's no staleness window.
- Radix dropdowns need real pointer events — `el.click()` inside `evaluate` does nothing. Use Playwright's `locator.click()`.
- Two tablists on the patient page: `querySelector('[role=tablist]')` returns the **odontogram's**. Target by `aria-controls`.

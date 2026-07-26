# Expert Adoption Review — Clinic-Management (dental, Tunisia)

> Functional/UX audit from the perspective of a practicing Tunisian dentist + assistant
> evaluating whether to adopt the app. Every nav item and every clinical / financial /
> cross-cutting flow was traced end-to-end against the current codebase.

## Verdict up front: **Conditional yes — adopt after ~6 day-one fixes, not before.**

The foundation is genuinely worth adopting. This is **not** a generic foreign app bent toward
Tunisia — it's built for the market: French UI, CNAM BS1 on the real bundled bulletin, timbre
fiscal + matricule, and El Fatoora/TTN — which as of **1 Jan 2026 is legally mandatory for
liberal professions including dentists** (notes d'honoraires must go through TTN in TEIF +
XAdES-B; 100–500 TND penalty per paper invoice, though enforcement is deferred until the
platform is 100% operational). So the app is chasing a real, imminent legal need, not a gimmick.

**But I would not put it on the front desk today.** Three loops break in the *first hour* of
real use (search, patient→booking, document re-open), and two money screens disagree with each
other. For a tool whose whole job is trust in the schedule and trust in the numbers, those are
adoption-blockers — but they're shallow wiring bugs, not architectural ones. Fix the shortlist
in §5 and it's a confident yes.

---

## 1. Day-in-the-life scenarios — where the loops break

### Scenario A — Assistant, 8:30am: new patient walks in, wants an appointment
1. Patients → **Ajouter un patient** → fill → save. Works. *Minor friction:* it drops you back
   on the list instead of opening the new patient, so every registration is "save, then hunt
   for the row."
2. Assistant types the name in the header search to pull the patient up →
   **🔴 the search returns the ENTIRE patient list, unfiltered.** The box *looks* live (spinner,
   dropdown, debounce) but the server ignores the search term entirely. For a practice with more
   than ~20 patients this is unusable — the single most-used control on the header does nothing.
3. Assistant opens the patient → **"Planifier un rendez-vous"** →
   **🔴 lands on an empty calendar with no dialog and no patient selected.** The button hands off
   `?patientId=` but the calendar never reads it. You re-click "Nouveau RDV" and re-pick the
   patient from scratch.

→ The two things the front desk does *most* — find a patient, book them — are the two most
broken loops.

### Scenario B — Dentist, patient in the chair
1. Patient page → **Odontogramme** → chart the caries → **"Créer un plan depuis l'odontogramme."**
   The plan seeds one line per diagnosed tooth ✅ — but **🟡 with no prices**, so you retype every
   fee by hand. (Also the tab is labelled *"lecture seule"* while being fully interactive —
   confusing copy.)
2. Add the dental record → the odontogram auto-clears the diagnosis and the plan step
   auto-completes. ✅ This triangle (odontogram ↔ record ↔ plan ↔ appointment ↔ invoice) is
   genuinely well-wired and refreshes live across screens. **This is the app's best asset.**
3. Write the ordonnance → Documents → Ordonnance → print/PDF, auto-archived to the patient's
   files. ✅ … until you need to reissue it: **🔴 there is no list of saved documents and no way
   to reopen one.** You can only find the flat PDF. "Change last week's prescription" = start
   from a blank template.

### Scenario C — End of day, the money
1. Issue a note d'honoraires from the patient's **Factures** tab → issue → record payment → PDF.
   ✅ Solid, complete, correct VAT/timbre.
2. But if that care was quoted as a **devis with installments**, the devis and the invoice are
   **🟡 two parallel tracks that never reconcile** — the "Solde patient" can double-count the
   same treatment.
3. **🟡 The two cash screens disagree:** installment cash shows up in the Dashboard "Recettes"
   but is *omitted* from the daily **Caisse**. A clinic that runs payment plans will see two
   different "how much came in today" numbers.

### Scenario D — CNAM patient
1. Documents → **Bulletin de soins CNAM** → prefilled from the patient's records, stamped onto
   the genuine bundled BS1.pdf, printed for hand-signing. ✅ Genuinely useful and locally
   accurate.
2. *Caveat:* the seeded nomenclature/letter-values ship flagged **"provisional / à vérifier"** —
   an admin must confirm them before the reimbursement estimate can be trusted, and Code
   acte/Cotation are hand-filled per line.

---

## 2. The nav bar — yes, it's overloaded. Here's the diagnosis.

**21 flat items, no groups, no sections** (17 for everyone + up to 4 admin). The real problem
isn't the count — it's that **config/reference screens are mixed in with daily-use screens**,
and **several items are just read-only shortcuts into the patient page.**

**Bloat to remove from the top level:**
- **Config that belongs in Settings:** `Types de procédures`, `Nomenclature CNAM`, `Médicaments`,
  `Actes dentaires`, `Utilisateurs`. These are catalogs you touch during setup, not daily.
- **`Mon profil`** → belongs in the user-avatar dropdown (where "Paramètres" already lives), not
  the main rail.
- **Duplicate/read-only shortcuts:** `Dossiers médicaux` (/records) is a read-only peek into data
  the patient page already owns editably; `Fichiers` (/files) duplicates the per-patient files
  tab. Both are low-value as top-level entries.
- **Orphan:** `/recurring-series` ("Rendez-vous récurrents") is fully built but **unreachable —
  no nav entry, no link anywhere.** Either wire it under Rendez-vous or delete it.

**Proposed grouping (≈21 flat → ~12 visible, in 4 sections + a tucked-away config):**

| Section | Items |
|---|---|
| **Quotidien** | Tableau de bord · Rendez-vous · Salle d'attente · Patients |
| **Clinique** | Documents · Plans & Devis · Laboratoire |
| **Finances** | Factures · Caisse · Créances |
| **Gestion** | Stock · Relances |
| **Config** *(collapsed / in Settings)* | Types de procédures · Nomenclature CNAM · Médicaments · Actes dentaires · Utilisateurs · Paramètres |

That alone makes the app feel intentional instead of "everything the team ever built, stacked."

---

## 3. Feature reality check — keep / fix / cut

| Feature | Verdict | Note |
|---|---|---|
| Notification center (bell) | ✅ **Keep — best feature** | Live SignalR, per-user read state, working deep-links. Ship-quality. |
| Odontogram ↔ record ↔ plan ↔ invoice wiring | ✅ **Keep — differentiator** | The clinical spine; genuinely smart. |
| Invoicing + payments + PDF + balances | ✅ **Keep** | Complete, gapless numbering, correct fiscal math. |
| CNAM BS1 bulletin | ✅ **Keep — local moat** | Real printable official bulletin. Verify seeded nomenclature. |
| Patient AI summary | ✅ **Keep** | Clinically thoughtful French prompt; degrades gracefully offline. |
| Stock + low-stock alerts | ✅ **Keep** | Edge-triggered, wired to notifications. |
| Settings/admin + backup | ✅ **Keep** | Production-grade. |
| SMS/WhatsApp reminders | ⚠️ **Keep, but "batteries not included"** | Real pipeline; inert until a paid gateway/Meta number is wired. Set expectations. |
| El Fatoora / TTN | ⚠️ **Strategically essential, technically unproven** | Legally required 2026. Sandbox works; **Production TTN transport is explicitly unverified** and needs a signing cert. Finish, don't cut. |
| Dashboard KPIs | ⚠️ **Keep, make clickable** | Real numbers, but a dead scoreboard — "Urgents"/"Créances" should click through. |
| Global search | 🔧 **Fix (broken)** | Server ignores the query term. |
| AI chat assistant | ❌ **Hide or invest** | Small model (Phi-3-mini) doing NL intent parsing + **hard-coded English replies in a French app**. Gimmicky; faster to click the button. |
| /records + /files top-level | ✂️ **Demote/cut** | Read-only duplicates of the patient page. |

---

## 4. Broken loops, prioritized

| # | Severity | Loop | Root cause |
|---|---|---|---|
| 1 | 🔴 Blocker | Header search returns all patients, unfiltered | `GetPatientsQuery` ignores `searchTerm`/`limit` |
| 2 | 🔴 Blocker | "Planifier un RDV" from a patient → empty calendar, no preselect | appointments page reads only `appointmentId`, not `patientId` |
| 3 | 🔴 Major | Saved medical documents can't be listed or reopened | editor `?id=` load + `GET /medical-documents` have no UI caller |
| 4 | 🟡 Major | Caisse omits installment cash; Dashboard includes it | `GetCaisseSummaryQuery` only sums invoice collections |
| 5 | 🟡 Major | Devis + invoice double-count in "Solde patient" | two unreconciled money tracks, no link/dedup |
| 6 | 🟡 Major | El Fatoora "on" without a cert = stuck→Failed, not a clean no-op; button shows even when clinic toggle is off | `CanSubmitToElFatoora` checks fiscal status only |
| 7 | 🟡 Minor | Odontogram→plan seeds designation but no price | seed omits cost |
| 8 | 🟡 Minor | New patient doesn't open the patient page | list stays put after create |
| 9 | 🟡 Minor | AI chat replies in English | hard-coded strings in `AIActionService` |
| 10 | 🟡 Minor | Patient "Flags" UI is disabled ("pas encore pris en charge"); odontogram tab mislabeled "lecture seule" | inert/misleading UI |

---

## 5. What I'd fix before adoption (the shortlist)

1. **Make search actually search** (#1) — wire `searchTerm`/`limit` into `GetPatientsQuery`.
2. **Read `patientId` on the appointments page** (#2) — open the dialog with the patient
   preselected.
3. **Add a "Documents" tab on the patient page** listing saved medical documents with
   reopen/edit (#3) — the endpoint already exists.
4. **Reconcile the money** (#4, #5) — include installment cash in Caisse, and link/dedup
   devis↔invoice in "Solde patient." A billing tool that shows two different daily totals loses
   trust instantly.
5. **Regroup the nav** (§2) and remove the `/recurring-series` orphan.
6. **El Fatoora honesty** (#6) — gate the button on the clinic toggle and surface
   "certificat requis" up front; keep finishing Production TTN since it's legally due.

Everything on that list is shallow wiring, not redesign. None of it touches the strong clinical
spine.

---

## Bottom line

The clinical and fiscal foundation is real, locally-aware, and — with El Fatoora now mandatory —
well-timed. It is being held back by a handful of unfinished wires at the exact points a dentist
and assistant touch first. Close the §5 shortlist and this crosses from "impressive demo" to
"I'd run my practice on it." Today, as-is, the search and booking breaks alone would send the
front desk back to paper by lunchtime.

---

### Sources (El Fatoora / TTN 2026 mandate)
- Business News — <https://businessnews.com.tn/2025/12/30/facture-electronique-ce-qui-va-reellement-changer-a-partir-du-1er-janvier-2026/1380856/>
- Challenges TN — <https://www.challenges.tn/economie/tunisie-la-facturation-electronique-devient-obligatoire-pour-les-services-en-2026/>
- WebManagerCenter (guide complet) — <https://www.webmanagercenter.com/2026/01/31/560961/facturation-electronique-tunisie-2026-guide-complet/>

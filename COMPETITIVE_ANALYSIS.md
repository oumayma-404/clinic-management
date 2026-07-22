# Clinic-Management — Competitive & Gap Analysis (Tunisia)

_Devil's-advocate product review. Compiled July 2026 from a codebase audit (backend + frontend, verified against source) and web research on the Tunisian clinic-software market and its regulatory table-stakes. Confidence flags are explicit where research was thin._

---

## 1. Headline verdict

You are sitting on a **much stronger product than your own docs admit**, aimed at the **single most underserved gap** in the Tunisian market — but it is **unfinished in the exact places a practitioner judges you on**, and it carries **trust/compliance debt** that is dangerous for a *health* app.

- Billing, e-invoicing, CNAM nomenclature, and dental records are among the **most-built** parts of the system — not the afterthoughts the repo docs imply.
- The **offline-LAN Windows** direction lands precisely on the market gap nobody occupies.
- But dental depth (treatment plans, real odontogram, dental act codes) is **not yet competitive** with a dedicated dental product, and production e-invoicing / Cloud-mode security are **unverified or unsafe**.

---

## 2. Comparison matrix

Legend: ✅ strong / present · 🟡 partial / weak · ❌ absent

| Capability | **This app** | Medwin (incumbent) | Olycab (SaaS challenger) | DolIdentiste (dental SaaS) |
|---|:---:|:---:|:---:|:---:|
| **Deployment: offline-LAN / self-hosted** | ✅ Windows + LAN | ✅ desktop/LAN | ❌ cloud only | ❌ cloud only |
| **Modern UX** | ✅ React 19 / shadcn | ❌ dated | ✅ | ✅ |
| **AI (agentic / dictation)** | ✅ chat + agentic actions | ❌ | 🟡 | 🟡 |
| **CNAM depth** | 🟡 nomenclature + BS1 + estimate | ✅ deep (APCI, AP1/AP2) | ❌ not advertised | n/a (dental cash) |
| **El Fatoora / TTN e-invoice** | 🟡 built (sandbox), prod unverified | 🟡 via utility | ❌ | ❌ |
| **WhatsApp + SMS reminders** | ✅ both (built, unconfigured) | 🟡 limited | ❌ | 🟡 SMS/email |
| **Invoicing lifecycle + payments** | ✅ full | ✅ | ✅ | ✅ |
| **Treatment plan / devis / échéancier** | ❌ | 🟡 | 🟡 | ✅ |
| **True persistent odontogram** | 🟡 flat per-record tooth list | 🟡 | ❌ | ✅ 8 states, adult+child |
| **Dental act codes (STMDLP / DCH)** | ❌ | 🟡 | ❌ | ✅ 144 acts |
| **Imaging (panoramique / photos)** | ❌ generic blob only | 🟡 | ❌ | ✅ |
| **Waiting-room / queue board** | ❌ | ✅ | ✅ | ✅ |
| **Analytics / charts** | ❌ (recharts unused) | ✅ | ✅ | ✅ real-time CA |
| **Per-practitioner calendar** | ❌ single shared grid | 🟡 | ✅ | ✅ |
| **Patient portal / online booking** | ❌ | ❌ | 🟡 | ✅ |
| **Arabic / RTL** | ❌ (EN/FR mix) | ❌ | ❌ | ❌ |
| **Mobile-friendly** | ❌ desktop only | 🟡 Android companion | ✅ | ✅ |
| **Audit log / change history** | ❌ | 🟡 | 🟡 | 🟡 |
| **Google Calendar sync** | ✅ two-way | ❌ | ❌ | ❌ |
| **Price model** | undecided | ~300–600 TND one-time | 50 TND/mo | subscription |

**Read:** You beat the SaaS challengers on **offline + CNAM + WhatsApp**. You beat Medwin on **UX, AI, WhatsApp, e-invoicing**. But **DolIdentiste beats you on core dental workflow**, and **everyone beats you on waiting-room + analytics**.

---

## 3. Where you genuinely win

1. **Offline-LAN Windows deployment is the right moat.** The market splits into an offline desktop incumbent (Medwin — ~1 in 2 Tunisian physicians, but 30 years old, French-only, weak on WhatsApp/mobile) and cloud SaaS challengers (Olycab et al. — modern but cloud-only and weak on CNAM). **Nobody occupies offline-LAN + CNAM + WhatsApp/SMS + modern UX + local hosting at once.** That's exactly what you built — and INPDP data-sovereignty pressure reinforces it.
2. **Ahead of the 2026 e-invoicing curve.** El Fatoora / TTN (TEIF, XAdES signing, QR cachet) is mandatory for liberal professions since **1 Jan 2026** (Loi 17-2025 art. 53). You built a real engine — most rivals bolt it on via a paid utility. _(Sandbox works end-to-end; production client is unverified — see gaps.)_
3. **CNAM depth the SaaS players lack** — nomenclature, lettre-clé values, reimbursement estimation, real BS1 bulletin-de-soins PDF.
4. **WhatsApp + SMS** — WhatsApp reaches ~75% of Tunisians; the expected channel, with SMS fallback. Rare among rivals.
5. **Genuinely modern:** React 19 / shadcn, real-time SignalR, Google Calendar two-way sync, an **AI chat with agentic actions** (voice → create appointment / search patient), dual auth (Auth0 + local JWT), proper multi-tenancy.

---

## 4. What you're missing — ranked by how much it hurts

### Tier 1 — Breaks the "serious dental product" claim
1. **No treatment plans / devis / échéanciers.** Tunisian dentistry runs on quotes + installment payments — the core dental billing pattern. Your `DentalRecord` is per-past-intervention only. DolIdentiste and DentiSolution both have this. A dentist notices day one.
2. **Your odontogram isn't really an odontogram.** A flat FDI tooth-list per intervention — not a persistent per-tooth chart with state (caries / crown / extracted / planned-vs-done / surfaces). DolIdentiste ships 8 states, adult+child, click-to-update. Yours can't show "the mouth as it is today."
3. **No dental act codes (STMDLP / DCH).** You have CNAM *medical* nomenclature; dentists bill against STMDLP with official DCH codes (DolIdentiste pre-loads 144). Dental billing is manual without it.
4. **No imaging.** No panoramique / rétro-alvéolaire storage tied to the record beyond a generic file blob. Standard expectation in a cabinet dentaire.

### Tier 2 — Compliance & trust risk (dangerous for a health app)
5. 🔴 **Production e-invoicing is unverified — and the mandate is legally live.** `HttpTtnClient` is self-documented as speculative (transport an "Open Question," untested against real TTN). Sandbox is a deterministic fake. The one feature that's a 2026 legal requirement (penalties **500–5,000 TND per invoice**) may not work against real TTN. Verify it or explicitly gate it as sandbox-only.
6. 🔴 **Security debt on health data under INPDP.** Committed secrets in `appsettings.json`; in **Cloud mode** the auth fallback is null (a controller missing `[Authorize]` is anonymous), Hangfire authorizes everyone, OAuth `state` is unvalidated. Loi 2004-63 + INPDP treat health data as sensitive (authorization, role-based access, encryption, audit). You have **no audit log / change history** at all.
7. **Coarse permissions, and roles vanish in Cloud.** Role strings only, and roles aren't surfaced in Cloud mode — so every admin screen (Users, CNAM, Medications, Reminders) is effectively Local-only. A multi-practitioner Cloud clinic has no real access control.

### Tier 3 — Competitive polish gaps
8. **No per-practitioner calendar.** One shared grid, no per-doctor column/filter — yet you target multi-doctor clinics.
9. **No analytics.** `recharts` installed but renders nothing; "reporting" is six number cards. DolIdentiste has real-time CA / top-treatment stats.
10. **Desktop-only, French/English mix, zero Arabic/RTL.** No mobile nav (no hamburger/drawer). UI mixes EN (Dashboard/Patients/Stock) and FR (Documents/Factures/CNAM) — sloppy for a Tunisian product. `en-US` speech recognition in the AI chat is wrong for the audience.
11. **No waiting-room board.** Near-universal in local tools (Medwin, Olycab, MicroMedPro, Tunidoc) and culturally expected. (Patient portal / online booking / teleconsult are skippable — teleconsult is legally restricted since 2024 — but waiting-room is a table-stakes miss.)

### Tier 4 — Rot that signals "unfinished"
12. **`AI summary` endpoint is a string template, not AI** — misleadingly named; the real AI backend (`GoogleAIService`/Gemini) is dead code, never registered.
13. **`ValidationBehavior` is a no-op** (zero validators) and **domain events are raised but never dispatched** — cruft that bites when someone assumes they fire.
14. **Fake Working Hours save** (toasts success, persists nothing), **dead `notifications-list.tsx`** (hardcoded fake patients), **non-functional dashboard search bar**, hardcoded sidebar clinic hours, inert **`RecurringAppointment`** schema.

---

## 5. Prioritized next moves

1. **Finish the dental core** — treatment plans + devis + échéanciers, a real persistent odontogram, STMDLP/DCH act codes. Converts "generic clinic app" into "dental product a dentist buys."
2. **Verify or honestly gate production TTN e-invoicing** — 2026 legal requirement with per-invoice fines; sandbox-only-labeled-done is a liability.
3. **Close Cloud-mode security holes + add an audit log** — non-negotiable for INPDP-grade health data.
4. **Add a waiting-room board + per-practitioner calendar** — cheap, table-stakes, high perceived value.
5. **Clean the rot before any demo** — fake Working Hours, dead notifications-list, dead search bar, the misnamed "AI summary," the EN/FR mix.
6. **Decide the pricing story** — a one-time-ish licence undercuts SaaS and matches the offline positioning; it's a differentiator, not a detail.

---

## 6. Confidence notes / verify-before-relying

- **Exact current timbre-fiscal amount** (0.6 vs ~1.0 TND) and the **precise Tunisian VAT-exemption article** need confirming (search returned the French code by mistake).
- **How hard El Fatoora is actually enforced against small VAT-exempt cabinets in 2026 is uncertain** — vendors have incentive to overstate "mandatory now."
- CNAM has a provider portal, but **real-time electronic claim adjudication in private cabinets is not universal** — still largely paper, so your paper/PDF BS1 approach is correct for today.
- "Doctena" and "Vezeeta" were **not found operating in Tunisia** (medium-high confidence they're absent).

---

## 7. Competitor landscape (reference)

**Booking marketplaces (patient-facing, not EMR):** DabaDoc (Moroccan, cross-Maghreb, prepaid video teleconsult + SMS reminders), Tunidoc (Tunisian, AI voice dictation + connected waiting-room), Med.tn, Tobba.tn.

**Cabinet practice-management logiciels:** Medwin/WINMED (Tunisian incumbent, desktop/LAN, deep CNAM incl. APCI + AVICENNA 4,500-drug DB, one-time licence), Olycab (Tunisian cloud SaaS, 50 TND/mo, waiting-room, weak CNAM), MicroMedPro (desktop, imaging module), Kabinet+ (lifetime licence), Ophtalinea (ophthalmology, cloud/mobile/local, integrated imaging).

**Dental SaaS:** DolIdentiste (strongest dental — odontogram 8 states, 144 STMDLP acts, devis, 42 dental meds → PDF ordonnance), DentiSolution (tooth-by-tooth status, online booking + SMS).

**HIS/ERP (institutional):** Clinisys ERP (Tunisian, ~137 installations, full hospital modules incl. RIS/LIS + AI radiology dictation, FR/AR/EN).

**Regional comparator worth noting:** MediCore Africa (Moroccan) — explicit **offline-in-hybrid** + **FR/EN/AR with RTL** + WhatsApp + Mobile Money — the closest analog to an offline-LAN, multilingual value proposition.

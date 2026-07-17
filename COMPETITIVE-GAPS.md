# Competitive Gaps — Where We Lose

An honest, grounded list of features competitors offer that we currently **do not** (or only partially) have. Compiled from competitor marketing pages (Tunisian and international) against our real, working features — stubbed/dead features (email/SMS, AI patient-summary, Google→app import) were **not** counted in our favor.

> Use this to prioritize the roadmap. The 🔴 items are the ones actually blocking Tunisian clinics from switching.

## 🇹🇳 Tunisian market — where we lose

| Missing / weak | Who has it | Severity | Notes |
|---|---|---|---|
| **CNAM integration** (bordereaux AP1/AP2, APCI, filière privée) | Medwin, others | 🔴 **Critical** | *The* deal-breaker. Tunisian doctors depend on CNAM reimbursement paperwork. We only have generic "insurance fields" — not the actual CNAM billing/reimbursement workflow. Many clinics will not switch without it. |
| **SMS / WhatsApp appointment reminders** | CABISOFT, most others | 🔴 **Critical** | Ours is *in-app only*; email/SMS is stubbed/dead. Every competitor sends SMS/WhatsApp. Expected baseline in Tunisia. |
| **Real invoicing / facturation & revenue accounting** | Medwin, MedicalPlus, MediFlux | 🟠 **Major** | We track cost/amount-paid on dental records, but there's no proper invoicing module, receipts/cashier, or financial reports. |
| **Medication database with interaction checking** | Medwin (AVICENNA, 4,500+ drugs) | 🟠 **Major** | Our "ordonnance" is free-text. Competitors pick from a drug DB and flag interactions. |
| **Online patient self-booking (24/7)** | DentiSolution, Kabinet+ | 🟠 **Major** | Patients can't book themselves; staff must enter everything. |
| **Mobile app** | Medwin, Kabinet+ | 🟡 Medium | We're web-only. |
| **Lifetime license, no subscription** | Kabinet+ | 🟡 Medium | Pricing-model expectation we'll be compared against. |

## 🌍 International tier — additional gaps (Open Dental, Curve, Dentrix, Tebra)

Not really our market, but for completeness — all standard there, absent for us:

| Missing | Severity | Notes |
|---|---|---|
| **Integrated X-ray / imaging** | 🟠 Major | Big for dentistry; we have no imaging integration. |
| **Electronic insurance claims / ERA** | 🟠 Major | Electronic claim submission + remittance posting. |
| **Multi-phase treatment planning with financial estimates** | 🟡 Medium | We record work done, not planned treatment estimates. |
| **Patient portal + secure messaging** | 🟡 Medium | No patient-facing self-service portal. |
| **Telehealth / video consultation** | 🟡 Medium | Not offered. |
| **Automated recall** (recurring check-up reminders) | 🟡 Medium | We generate a 24h reminder only; no recall cadence. |
| **Standardized procedure codes** (e.g. CDT, updated yearly) | 🟡 Medium | Our procedure types are free-form, not a coded catalog. |

## Prioritized roadmap (recommended order)

1. **SMS / WhatsApp reminders** — make the existing stub real. Fastest path to baseline parity.
2. **CNAM billing workflow** — removes the #1 blocker for Tunisian adoption.
3. **Proper invoicing / receipts** — completes the money story.

Nail these three and our genuine advantages (AI action-assistant + voice, offline-first LAN deployment, modern UX, real-time sync) become compelling rather than "nice, but…".

---

*Sources: Medwin (medwin.tn), MedicalPlus (medicalplus.tn), Kabinet+ (kabinetplus.vercel.app), DentiSolution/Bees (bees-solution.com), CABISOFT (cabisoft.net), ClinikEHR & Curve Dental 2026 dental-software reviews, PracticeSuite practice-management feature guide.*

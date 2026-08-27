# Clinic Management — Feature Overview

**Audience:** marketing (engineering-literate)
**Source:** verified against the codebase (40 API controllers, 60 domain entities, 33 web routes), 2026-08-16.

---

## 1. What the product is

A full-stack **dental / medical practice management system** built for the Tunisian market: French UI throughout, Tunisian governorates, CNAM (national health insurance) forms and reimbursement rules, TND money handling, post-dated cheques, WhatsApp as a first-class patient channel.

It is **multi-tenant by clinic** and ships in **two deployment topologies from one codebase** — a clinic's own Windows PC on an offline LAN, and a hosted multi-tenant SaaS. Native shells (Windows desktop, Android, iOS) render the server's own web bundle.

**Stack:** Next.js 15 / React 19 / TypeScript / Tailwind v4 frontend · .NET 8 Clean Architecture + MediatR CQRS backend · PostgreSQL 16 (EF Core) · MinIO / local-disk object storage · SignalR realtime · Hangfire background jobs.

---

## 2. Overview — the twelve pillars

| # | Pillar | One line |
|---|--------|----------|
| 1 | **Agenda & scheduling** | Day/week/month calendar, drag-to-move, multi-act visits, recurring series, waiting list, Google Calendar two-way sync |
| 2 | **Patient records** | Full clinical file: odontogram, fiches de soins, antécédents, documents, file storage, flags, alerts |
| 3 | **Clinical documents** | 6 document types incl. **CNAM BS1** and **arrêt de travail P061** stamped onto the real official forms; PDF + Word + email delivery |
| 4 | **Billing & cash** | Notes d'honoraires, avoirs, la caisse with a full statement, cheque register, receivables, per-patient balance |
| 5 | **Treatment plans (devis)** | Ordered act plans, acceptance, amendment, instalment schedules, devis→facture bridge |
| 6 | **CNAM** | Nomenclature catalog, reimbursement estimates, annual ceiling tracking per patient |
| 7 | **Stock & suppliers** | Batches with expiry, movements, per-act material lists with auto-consumption, supplier directory with WhatsApp |
| 8 | **Lab orders** | Bons de prothèse with a 4-stage lifecycle, linked to the prosthetist's supplier record |
| 9 | **Patient communication** | SMS + WhatsApp reminders (multi-tier, quiet hours), recall/relance, in-app feed, OS push notifications |
| 10 | **Dashboard & worklists** | Period-comparable KPIs with drill-down, 6-month trend, act mix, customisable widgets, « À clôturer » worklist |
| 11 | **Administration & compliance** | Role-based access, audit journal, 2FA, backups, recovery points, per-clinic data archive/restore, CSV import/export |
| 12 | **Vendor operations** | Private platform console: portfolio, subscriptions, suspensions, WhatsApp messaging quotas |

---

## 3. Detail

### 3.1 Agenda & scheduling

- **Three calendar views** — Jour / Semaine / Mois — with a dedicated phone layout (week strip instead of a time grid on narrow viewports).
- **Drag-and-drop rescheduling** directly on the grid; touch gestures supported.
- **Multi-act appointments**: a visit holds several acts (« détartrage + 2 obturations »). Duration defaults to the sum; the agenda colour, the fiche proposal and the devis all follow.
- **Overlap protection** at the database level (PostgreSQL exclusion constraint, partial so cancelled slots stay bookable).
- **Status lifecycle**: Prévu → Confirmé → En cours → Terminé / Annulé / Absent. A background job auto-starts a visit when its slot begins; « Terminé » and « Absent » stay human decisions.
- **Recurring series** (daily/weekly/monthly) with occurrence / following / whole-series scoping.
- **Salle d'attente** — waiting list with priority levels and one-click promotion to a real appointment.
- **Google Calendar**, per clinic (each practice connects its own account): App→Google inline on every write, Google→App on demand, with an « non synchronisé » badge and a manual push.
- **CSV export** of the agenda window.

### 3.2 Patient records

- Demographics with **genuinely optional contact details**, Tunisian governorates, archiving instead of deletion (deletion is refused when anything is attached, naming the counts).
- **Duplicate detection on creation** (name+DOB · name · normalised phone) with an advisory « Créer quand même ».
- **Patient tabs**: Dossiers médicaux · Rendez-vous · Notes · Documents · Fichiers · Factures · Plans de traitement.
- **Interactive odontogram** — adult & child dentition, 9 tooth conditions (sain, carie, obturation, couronne, traitement de canal, bridge, implant, extrait/absent, à traiter), diagnosis vs. treatment sourcing.
- **Fiches de soins** (dental records) — per-tooth acts, costs, per-act catalog picker, linked to the visit they belong to.
- **Antécédents médicaux et familiaux**, allergies, notes importantes surfaced as an alert panel.
- **Patient flags**: priorité haute, condition spéciale, alerte, critique, allergie.
- **File storage per patient** with folders: a single server-side upload catalog keyed on extension (PDF, images, **STL / DICOM / PLY / OBJ** for 3D and imaging), per-format size caps, magic-byte signature checks, French refusal messages served by the API so the browser can never word a refusal differently. Files can be renamed, described and moved.
- **CSV import** with a full dry-run preview: delimiter, encoding (UTF-8 / Latin-1) and line-ending auto-detection, column mapping, eager duplicate matching, phone normalisation to +216 E.164. Row-atomic — never a silent partial commit.

### 3.3 Clinical documents

Six document types, produced as **PDF server-side**, with a live editor and preview:

| Type | Notes |
|---|---|
| **Ordonnance** | Medication catalog picker with active ingredients; Word (.docx) export |
| **Lettre de liaison** | Structured norms; Word export |
| **Certificat médical** | Word export |
| **Note d'honoraires** | Linked to the billing subsystem |
| **Bulletin de soins CNAM (BS1)** | Rendered as an **overlay on the genuine CNAM form**; live reimbursement estimate per act row |
| **Certificat d'arrêt de travail** | Overlay on the genuine **CNAM P 061** form; the motif is deliberately never printed |

- **Practitioner identity** (cachet, n° d'ordre CNOMDT) is snapshotted into the document, resolved from the *chosen* practitioner — so a document authored by reception still carries the right dentist.
- **Email delivery** of documents, with a queued outbox and delivery status.
- The two official forms are print-fidelity: iframe PDF preview, no Word export (a .docx of a pre-printed form is just letterhead).

### 3.4 Billing & cash

- **Notes d'honoraires**: Draft → Issued → Partiellement payée → Payée → Annulée. VAT, timbre fiscal, per-clinic billing settings, gapless per-year numbering.
- **Payments** in 4 methods (espèces, chèque, carte, virement). Cheques carry number, bank and due date, and can be **marked as banked**.
- **Voiding, not erasing**: a mis-keyed payment is voided with a motif, an actor and a moment; the row stays, struck through, and a reprinted receipt is stamped « REÇU ANNULÉ ».
- **Avoirs (credit notes)** — issuable, listable, printable, netted into every revenue read.
- **Two invoice bridges**: devis → facture (carrying collected payments across) and **fiche de soins → facture** (prices the session's acts, issues the note and records the payment in one transaction; re-saving a fiche tops the note up).
- **La caisse**: summary (encaissé / remboursements / dépenses / net), **cash-in split by payment method**, an **extrait de caisse** — every movement behind the totals with a running period balance — plus an expenses ledger. Day, range and month periods, all resolved on Tunisian time.
- **Registre des chèques**: every cheque the clinic holds across both payment ledgers, bucketed en retard / bientôt / plus tard / sans date.
- **Créances** (outstanding receivables) and a **per-patient balance** reachable by reception.
- **Receipt PDFs** for invoice payments and instalment payments.
- **CSV exports** on nine lists — always the whole filtered set, never the current page.

### 3.5 Treatment plans (devis)

- Ordered act lists seeded from the odontogram, with an act catalog or hand-typed lines.
- Lifecycle: Brouillon → Accepté → En cours → Terminé / Annulé, with **amendment after acceptance** (revision numbers) instead of cancel-and-retype.
- **Instalment schedules (échéancier)** with an event-sourced payment ledger — voidable, bankable, receipt-printable.
- A **plan workspace**: timeline, progress bar, per-act state derived from the appointments pointing at it, the invoice that bills it, and « Planifier ensemble / séparément » to book acts into one visit or several.
- Devis PDF.

### 3.6 CNAM

- **Nomenclature catalog** and letter values, per clinic and editable.
- **Reimbursement estimates**, batched so a multi-act bulletin computes live per row; the rate turns on the patient's age *at the care date*.
- **Annual ceiling tracking**: the dependants barème, the dedicated dental allowance, which categories are hors plafond, what this clinic has consumed this year and what remains — stated honestly as an estimate bounded to this clinic's own acts.
- **Dental act codes** catalog.

### 3.7 Stock & suppliers

- Items with unit, unit price, minimum quantity, **batches with expiry dates and batch numbers**, and a full movement history (consommation / réapprovisionnement / ajustement).
- **Material lists per procedure type**: saving a fiche de soins automatically draws the act's materials out of stock and records the movements — best-effort, so a stock failure never blocks the clinical record.
- **Low-stock** and **approaching-expiry** alerts (a daily job — expiry is crossed by time, not by a write).
- **Fournisseurs**: a real supplier record (nom, catégorie, téléphone, adresse, notes, actif) covering stock dépôts, laboratoires de prothèse, laboratoires d'analyses and technicians. A **WhatsApp action wherever a supplier's name appears** — the stock table, the lab board, the supplier list and the low-stock alert row — or « Ajouter un numéro » where none is on file.

### 3.8 Lab orders

Bons de prothèse with a 4-stage lifecycle (Envoyé → En cours → Reçu → Posé), linked to the prosthetist's supplier record while keeping the printed name, filterable by stage, exportable.

### 3.9 Patient communication

- **SMS and WhatsApp appointment reminders**: multiple lead-time tiers (e.g. 24 h and 6 h), a quiet-hours floor (21:00→08:00 clinic-local, moving a send earlier rather than later), per-clinic message wording, sender identity and gateway URLs — all admin-editable without touching server config.
- **A queue that cannot starve**: unsendable rows are parked with a machine-readable reason and returned to the queue the moment the channel becomes sendable; serving is per-clinic and fair-share.
- **Never announces a stale moment** — a visit moved in Google or in the app re-schedules its reminder, and the dispatcher re-checks before sending.
- **WhatsApp Embedded Signup** (Meta) for onboarding a clinic's own number, with template submission and status tracking (webhook + reconciling poll).
- **Rappels screen**: outbox status, blocked-row counter and filter, remaining allowance, 12-month history.
- **Relance / recall**: due-patient worklist with contacted / snooze / send actions and a configurable interval.
- **In-app notification centre**: 14 categories, per-user read state, deep links, live over SignalR.
- **OS push** (Android/iOS): five time-critical categories reach a locked phone (booking, cancellation, reschedule, the ~24 h reminder, post-visit review). **The push carries no patient data** — a category, a fixed French phrase and opaque routing ids; the content stays behind the app's own authentication.

### 3.10 Dashboard & worklists

- **Four sections**: Activité (RDV honorés, nouveaux patients, taux d'absence, devis acceptés), Argent (encaissé, facturé, remboursements, dépenses, net), À traiter (waiting list, devis brouillons, bons en retard, stock faible, stock expirant, visites à clôturer), and a **6-month collected trend**.
- **Period comparison** — Aujourd'hui / Semaine / Mois against the previous equivalent, with a real distinction between « 0 » and « undefined » (a period with no appointments has *no* absence rate, not 0 %).
- **Répartition des actes** — the period's work by act type.
- **Every figure links to the filtered records it counted.**
- **Customisable**: any KPI block can be hidden per user.
- **« À clôturer »** — a worklist of every visit past its slot still missing one of *est-il venu · qu'a-t-on fait · combien a-t-il payé*, surfaced as its own page, a strip above the agenda and a dashboard chip. Reachable by reception, who is the person who knows. Derived from absent records, so it cannot drift; « Rien à facturer » is an escape hatch requiring a motif.
- **Post-visit review prompt** deep-linking staff into recording a finished visit.

### 3.11 Administration & compliance

- **Roles**: admin · doctor · secretary, enforced server-side on every one of 32 controllers. The line is *per-patient money yes, clinic-wide money no* for reception, and *record yes, erase no* on the clinical file.
- **Audit journal** (`/journal`): one row per mutated aggregate — actor, clinic, entity, action, changed-field summary — written by an EF interceptor, so no write path can forget it. Admin-only, paged.
- **Two-factor authentication** (TOTP) with recovery codes, enrolment from a dedicated « Sécurité » screen, admin reset, and a **step-up challenge** guarding sensitive actions. Session-replay detection on refresh-token families.
- **User management**: invite, activate/deactivate, role change, password reset. Self-registration creates a *pending* account an admin must activate.
- **Public clinic self-signup** (hosted only): email-verified, single-use 32-byte token, neutral responses so the endpoint is not an enumeration oracle.
- **Backups**: an hourly job, `pg_restore --list` verification whose failure fails the backup, a run ledger with « dernière sauvegarde réussie », retention that never empties the folder, a pre-migration safety dump, and a `restore-backup` console verb. Tools are auto-discovered, not configured.
- **Daily recovery points** — per-clinic rows-only archives, seven kept, restorable in one click.
- **Per-clinic data archive**: a `.zip` of every row plus the blobs, downloadable by the practice, and a **restore that is additive and keyed on original ids** — a row still present is left alone, a row that differs is skipped and counted, a row that is gone is re-inserted. Total loss and partial loss are the same operation.
- **Schema and money verification console verbs** (`verify-schema`, `reconcile-money`) — read-only, exit-coded, diffable before and after a migration batch.
- **CSV export on nine lists**, UTF-8 BOM + `;` so Excel on a French Windows opens them correctly.

### 3.12 Vendor operations (platform console)

A **private back-office**, served on its own port behind an SSH tunnel, with its own identity population (mandatory TOTP) and a **read shape that structurally cannot return a patient's data**:

- **Portfolio** of every cabinet with real activity beside it — patients, staff accounts, RDV pris (30 j), enregistrements (7/30 j), jours actifs, last save, last login, what the cabinet collected this month — filterable (« dormant »), searchable, sortable, paged.
- **Cabinet file**: six-month activity trend, administrator contact, subscription state, payment history.
- **Subscriptions**: 30-day free trial, record a payment, cancel a mis-keyed entry (kept and struck through with a motif), **suspend / lift** independently of what has been paid.
- **Expiry makes a cabinet read-only, never blind** — every read, every CSV export and every PDF keep working; only writes are refused, with a French sentence naming the date. Backups and stock-expiry alerts keep running regardless.
- **WhatsApp messaging quotas**: a monthly allowance per cabinet, one unit per message actually sent; past it a reminder is **held, never dropped**, and goes out when the vendor tops the cabinet up. The practice sees « il vous reste N rappels » and is warned at 80 / 95 / 100 %.
- **Access journal**: every cabinet file opened and every write, append-only, with the console account named.
- **Clinic restore** from an archive — re-creating a cabinet that no longer exists, its accounts included.

---

## 4. Deployment & platforms

| Topology | What it is | Status |
|---|---|---|
| **SelfHostedLan** | The clinic's own Windows PC serving its LAN — its data, its disk, self-signed HTTPS, local accounts, **fully offline** | Built |
| **HostedMultiTenant** | One hosted backend serving many practices over the internet, on the product's own accounts | Built |

Twenty named capabilities decide every behavioural difference (self-registration, public signup, own backups, vendor messaging, OS push, second-factor requirement…), so a deployment difference is never an untracked `if`.

**Clients:**

- **Web** — responsive from **320 px** up, 44 px touch targets on coarse pointers, tables become cards, heavy dialogs become sheets. Dark mode.
- **Windows desktop** — WPF + WebView2 thin shell, with an Inno Setup installer bundling PostgreSQL 16, Node and a CA-trust import.
- **Android** — Kotlin + WebView shell, runtime-configurable server address, native save/print/push bridge, **biometric session resume** (a phone that has been in a pocket is unlocked, not signed out).
- **iOS** — Swift + WKWebView shell.
- **LAN device trust**: a `/api/trust` page serving the CA certificate, an Apple `.mobileconfig` and a QR of the server address, so a phone can trust the install before it can log in.

**Offline behaviour:** internet reachability is judged by the *server* (LAN clients may have no egress). The one internet-dependent feature — Google Calendar — visibly disables and auto re-enables. Everything else works with no internet at all.

---

## 5. Engineering points worth selling

These are real, verifiable properties — useful in a technical pitch:

- **Optimistic concurrency across all 38 entities**, mapped onto PostgreSQL's `xmin` with no schema cost. A losing write is an HTTP 409 with a French message, not a silent overwrite.
- **Money is correctable, never erasable.** Every payment and instalment can be voided with a motif and an actor; avoirs are first-class. Five independent money reads (caisse, extrait, dashboard, patient balance, vendor console) are held equal by automated tests.
- **Derived reads instead of denormalised state.** « À clôturer », the caisse statement and the plan act states are computed from records that already exist — a table written by each write path is a table that drifts the day one write site forgets.
- **One authority per rule.** File upload policy, CNAM reimbursement, period arithmetic, duplicate matching, cheque validity, phone deliverability — each exists once and is served to the client, so the browser and the server can never word or compute things differently.
- **Tunisian time is a first-class concern.** A single clock abstraction owns UTC+1; a payment taken at 00:30 books to the right day, and a document number issued at 00:30 on 1 January lands in the right fiscal year.
- **Tenant isolation in two layers**: EF Core global query filters on 21 aggregate roots fed by a three-valued tenant scope (an unscoped path reads *nothing*, not everything), plus a per-handler re-check. Every blob key is `clinics/{id}/…`, enforced by the storage signature rather than by convention.
- **Realtime everywhere for free**: every mutating command broadcasts to the clinic's SignalR group, with the two sides held equal by a contract test — a new command live-refreshes peers with no extra work.
- **A schema gate that no test suite can replace** (`verify-schema`): it diffs the EF model against PostgreSQL's own catalog and asserts what the model cannot express — partial constraints, decimal precisions, backfill row counts, ledger-vs-snapshot agreement.
- **~2 800 backend unit tests**, plus derived guards that fail on an *unclassified* new endpoint, an undeclared realtime key, or a filter parameter that was dropped between the controller and the handler.

---

*Generated from the code, not from documentation. Anything above can be traced to a controller, an entity or a route.*

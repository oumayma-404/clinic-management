# Feature Specification: Facturation électronique TTN / TEIF (« El Fatoora »)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Feature:** Turn an issued invoice (note d'honoraires) into a legally-compliant Tunisian electronic invoice — generate TEIF XML, sign it (XAdES), submit it to the TTN « El Fatoora » platform, track its status, and carry the returned unique identifier + QR cachet back onto the invoice and its PDF.

> **Scope note:** This is a full, compliance-driven feature (delivered via the full pipeline: spec → challenge → test plans → plan → stories). It builds on the shipped `facturation-note-honoraires` feature. Extends the invoicing line; PRs target `feature/windows-desktop-app`.

## Overview
Tunisia's 2026 Finance Law (Art. 53) makes electronic invoicing via TTN's **El Fatoora** platform mandatory for service providers. A compliant e-invoice requires four cumulative things: (1) a **TEIF** XML document (Tunisian Electronic Invoice Format, XML per the official XSD), (2) an **electronic signature** (XAdES/XMLDSig, RSA-SHA256) using a **qualified certificate** from ANCE/TUNTRUST, (3) a **QR "cachet électronique visible"**, and (4) **submission to TTN**, which co-signs as trusted third party and returns a **unique identifier** (+ status). This feature adds all four to the existing invoice, designed to work in the app's **offline/Local LAN** installs via an outbox that submits when internet is available.

## User Stories

- **US-1 (Submit):** As a clinic user, I can send an issued invoice to El Fatoora with one action, so it becomes a legally-registered electronic invoice.
- **US-2 (Offline outbox):** As a user of an offline/LAN install, invoices I choose to submit while offline are queued and sent automatically when the server regains internet, without me re-doing anything.
- **US-3 (Status):** As a clinic user, I can see each invoice's e-invoicing status (not sent / queued / signed / submitted / validating / valid / rejected) and the TTN reference once validated.
- **US-4 (Receipt & QR):** As a clinic user, once an invoice is validated I can download the signed TEIF XML + TTN receipt, and the note-d'honoraires PDF shows the QR cachet + TTN reference.
- **US-5 (Errors & retry):** As a clinic user, when a submission is rejected or fails I see a clear reason and can correct/retry.
- **US-6 (Settings):** As an admin, I can configure the clinic's TTN credentials, qualified certificate, and target environment (test/sandbox vs production).

## Functional Requirements

### FR-1 — TEIF XML generation
- Generate a TEIF-format XML (target the current TTN-published version, ~v1.8.x — **confirm exact version**) from an **issued** invoice, mapping: document header (number, issue date/time, `InvoiceTypeCode` = 380 invoice, currency = TND, due date), **seller party** (clinic name, address, `MatriculeFiscal` as `TN_MF`), **buyer party** (see FR-6, B2C), **lines** (designation, quantity + unit, unit price HT, line HT), **tax totals** (VAT rate/amount, exoneration when applicable), **legal monetary totals** (total HT, total VAT, stamp duty, total TTC), consistent with the frozen invoice amounts.
- The generated XML must validate against the official TEIF XSD.

### FR-2 — Electronic signature (in-process)
- Sign the TEIF XML with the clinic's **qualified certificate** (XAdES/XMLDSig, RSA-SHA256) **inside the app** before submission.
- The certificate + its secret are stored in the per-install secret store (the gitignored `.local/` mechanism already used for HTTPS certs and Google tokens), never in committed config or the DB in plaintext.

### FR-3 — TTN El Fatoora submission
- Submit the signed TEIF to El Fatoora through a **provider-abstracted client interface** (a test/sandbox implementation must be selectable so the feature is exercisable without hitting production TTN). *(Transport — REST+OAuth2 vs SOAP — to be confirmed against official TTN docs; abstracted behind the interface either way — see Open Questions.)*
- On acceptance, capture the **TTN unique identifier**, the **validation status**, and the **receipt/acknowledgement**.

### FR-4 — Offline outbox + auto-submit
- Submission is an **explicit per-invoice action** ("Envoyer à El Fatoora"). If the server has no internet, the request is **queued** (outbox) and submitted automatically once internet is reachable, using the existing server-side connectivity signal (`IInternetProbe`).
- The outbox retries transient failures with backoff; permanent rejections stop and surface the reason. Submission-related UI is connectivity-aware (mirrors AI chat + Google Calendar gating).

### FR-5 — Status lifecycle & persistence
- Each invoice carries an e-invoicing status: `NotSubmitted → Queued → Signed → Submitted → Validating → Valid`, plus `Rejected` / `Failed`.
- Persist on the invoice: TTN unique id, status, signed XML, TTN receipt/ack, QR cachet payload, submission timestamps, and last error message. Persistence is clinic-scoped like the invoice.

### FR-6 — Buyer party (B2C / final consumer)
- Support a **final-consumer** buyer without a matricule fiscal (patient mapped as individual: name + optional national ID) per TEIF consumer rules, and also a buyer **with** a `TN_MF` when present. The chosen mapping must satisfy TEIF validation for both cases.

### FR-7 — QR cachet + PDF
- Once validated, generate/store the QR "cachet électronique visible" (encodes the TTN id, seller MF, timestamp, total, control hash) and include it — with the TTN reference — on the **note-d'honoraires PDF** (extends the existing `IPdfGenerationService` output). Pre-validation PDFs render as today (no QR).

### FR-8 — Settings
- Admin-configurable per clinic: TTN credentials/endpoint, qualified certificate upload, and environment (test vs production). Config lives with the existing clinic/billing settings + the per-install secret store; secrets are never echoed back.

### FR-9 — Access control
- Submitting/retrying is available to authenticated clinic users (consistent with issue/record-payment). *(Confirm whether it should be admin/doctor-restricted like cancellation — see Open Questions.)*

## Data (functional)
- Invoice gains e-invoicing state: `EInvoiceStatus`, `TtnIdentifier`, `SignedXml` (or storage key), `TtnReceipt` (or storage key), `QrPayload`, `SubmittedAt`/`ValidatedAt`, `LastError`. (Large blobs — signed XML/receipt — may live in file storage rather than a column; decided at plan time.)
- Outbox/queue record for pending submissions (invoice ref, attempt count, next-attempt time, last error).
- Clinic gains TTN settings (endpoint/env + non-secret identifiers); certificate + credentials in the `.local/` secret store.

## Scope

### In scope
- TEIF XML generation for **type 380** invoices, in-process XAdES signing, TTN submission via an abstracted client (with a sandbox impl), status tracking, offline outbox + auto-submit, QR + TTN reference on the PDF, signed-XML/receipt download, TTN settings, and error/retry UX.

### Out of scope
- **Credit notes / avoirs (type 381)** e-invoicing — depends on the not-yet-built avoirs feature (separate follow-up).
- Dedicated **B2G** flows beyond what standard TEIF submission covers.
- **Bulk historical backfill** of previously-issued invoices.
- Obtaining/renewing the ANCE qualified certificate (an operator/legal process; the app only consumes a provided cert).
- Changes to invoice numbering, issuance, payment, or cancellation behavior (unchanged).

## Edge Cases
- **Offline at submit time:** queued; auto-submitted when internet returns; status reflects "queued".
- **TTN rejection (bad data / schema):** status `Rejected` with the TTN reason; user corrects (may require cancel + re-issue, since issued invoices are immutable) and resubmits.
- **Transient network / TTN outage:** retried by the outbox with backoff; never blocks the core invoice.
- **Certificate missing/expired/invalid:** submission fails fast with a clear operator message; does not corrupt invoice state.
- **Cancelled invoice:** a cancelled invoice is not submitted; behavior of an already-validated invoice that is later cancelled must follow TTN rules (likely requires an avoir — out of scope; block or warn).
- **Duplicate submission:** an invoice already Valid/Submitted is not re-sent; the action is idempotent per invoice.
- **B2C buyer with no MF:** maps to final-consumer; must still pass TEIF validation.
- **Multi-tenant isolation:** each clinic signs with its own certificate and submits under its own TTN credentials; no cross-clinic leakage.

## Non-Functional Hints
- **Security/compliance:** qualified private key + TTN credentials are sensitive — stored only in the per-install secret store, never committed, never returned by the API, never logged. Signed XML is a legal record — store immutably.
- **Reliability:** submission must never roll back or corrupt the underlying invoice (best-effort, decoupled — mirrors the notification-generator/outbox posture).
- **Offline-first:** works in Local/LAN installs; internet only needed at actual submit time.

## Dependencies
- Existing `Invoice` aggregate + `Clinic.MatriculeFiscal`/VAT/stamp settings (shipped in `facturation-note-honoraires`).
- Connectivity awareness (`IInternetProbe` / `useConnectivity`), the `.local/` secret store (cert/token precedent), background-job infra (Hangfire) for the outbox, and `IPdfGenerationService` for the QR-on-PDF.
- **External:** official TTN El Fatoora API docs + sandbox credentials; the current TEIF XSD; a qualified ANCE/TUNTRUST certificate.

## Open Questions
1. **TTN transport & API:** confirm REST (`api.elfatoora.tn` + OAuth2) vs SOAP, exact endpoints, auth, and the status vocabulary from the official TTN integration docs / sandbox.
2. **TEIF version:** confirm the exact TTN-mandated TEIF version currently in production (~1.8.7/1.8.8) and obtain its XSD.
3. **Signature specifics:** exact XAdES profile (e.g. XAdES-B/BES) and canonicalization TTN requires; whether TTN's own co-signature is automatic on submit.
4. **Access control:** submit/retry open to any authenticated clinic user, or restricted to admin/doctor?
5. **Cert provisioning UX:** upload via settings vs operator drops the PFX into `.local/` — and how the key password is supplied.
6. **Storage of legal artifacts:** signed XML + receipt in the DB vs file storage.

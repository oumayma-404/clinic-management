# Feature Review: facturation-einvoicing-ttn

**Status:** INCOMPLETE
**Challenged:** No
**Date:** 2026-07-17
**Parent Branch:** feature/windows-desktop-app
**Merge Base:** f630ca6
**Reviewed Range:** `15511dd..HEAD` (the feature's own 2 commits — impl `3cffe27` + tests `061832f`). The
out-of-scope commit `15511dd` (patients/AI-summary) and the generated migration `.Designer.cs` +
`ApplicationDbContextModelSnapshot.cs` + `features/**` docs were excluded.
**Files Reviewed:** 50 files (+2354, −6) — 44 source + 6 test files.
**Review method:** 6 parallel agents adapted to the stack — Code Quality, Error Handling (`Result<T>`, not
ROP), Business Logic, Breaking Changes, Security (added — crypto/secrets), Frontend (added — React/TS surface).

## Findings

### Finding 1
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Application/Features/Invoices/Commands/SubmitInvoiceToElFatooraCommand.cs
- **Line:** 72
- **Anchor:** SubmitInvoiceToElFatooraCommandHandler.Handle
- **Comment:** The per-clinic FR-8 toggle `Clinic.TtnEInvoicingEnabled` is never enforced server-side — neither here nor in `EInvoiceService.ProcessAsync`. Any clinic (even one that never enabled e-invoicing or configured a cert) can `POST /invoices/{id}/e-invoice/submit` and get the invoice queued/signed/"validated" (the UI only hides the button). The toggle is decorative. Fix: load the clinic and return `Result.Failure` (e.g. "La facturation électronique n'est pas activée pour ce cabinet.") when `!clinic.TtnEInvoicingEnabled`, and/or short-circuit in `ProcessAsync`. *(Flagged by 3 agents — Code Quality, Business Logic, Frontend. The frontend also renders the El Fatoora column/button for every non-draft invoice regardless of the clinic toggle — gate the UI on `clinic.ttnEInvoicingEnabled` once the server enforces it.)*

### Finding 2
- **Severity:** Major
- **Category:** Error Handling
- **File:** api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs
- **Line:** 99
- **Anchor:** EInvoiceService.ProcessAsync
- **Comment:** The `IEInvoiceService` contract (and both callers' comments) promise this method is best-effort and "NEVER throws back," but exceptions can escape: (a) the three repo loads (`GetByIdAsync` clinic/patient/invoice) run BEFORE the `try`; (b) the `UpdateAsync` + `SaveChangesAsync` tail runs OUTSIDE the `try/catch`. A DB/transport/concurrency failure in either propagates. Concrete data effect on the inline path: if TTN already `Validated` (real submission succeeded) and the tail `SaveChangesAsync` then throws, the row stays `Queued` and the next outbox tick re-submits → duplicate TTN registration. Fix: wrap the entire body (loads + dispatch + persist tail); on a tail-save failure log and swallow.

### Finding 3
- **Severity:** Major
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs
- **Line:** 67
- **Anchor:** EInvoiceService.ProcessAsync
- **Comment:** The "only dispatch if still `Queued`" guard is a non-atomic read with no optimistic-concurrency token or conditional state transition. Two workers can both observe `Queued` and both submit the same fiscal document to TTN: (a) the minutely `EInvoiceOutboxJob` selects Queued+due invoices at the same moment the inline submit path runs — `[DisableConcurrentExecution]` only serializes the job against itself, not against the command; or two rapid submit clicks from separate tabs. (b) A crash after `client.SubmitAsync` succeeds but before the single `SaveChangesAsync` leaves the row `Queued`, so the outbox re-submits. Both double-register at TTN. Fix: claim the row atomically before signing/submitting (rowversion, or a conditional `UPDATE … WHERE EInvoiceStatus = Queued` / a `Validating` claim state).

### Finding 4
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Services/HttpTtnClient.cs
- **Line:** 33
- **Anchor:** HttpTtnClient.SubmitAsync / HttpTtnClient.AcquireTokenAsync
- **Comment:** The TTN base URL and token URL are used verbatim from config with no scheme check. If an operator (or a tampered `.local`/config) sets an `http://` URL, the OAuth `client_secret` (form-posted in `AcquireTokenAsync`), the resulting bearer token, and the signed legal TEIF are all sent in cleartext — the most sensitive credentials in the feature. Fix: validate the resolved URLs are absolute with `Uri.Scheme == "https"` before sending and fail closed (`TtnSubmissionResult.Transient(...)`) otherwise; never fall back to plaintext.

### Finding 5
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.API/Controllers/ClinicsController.cs
- **Line:** 185
- **Anchor:** ClinicsController.UpdateClinic
- **Comment:** `UpdateClinic` now accepts `TtnEInvoicingEnabled` / `TtnEnvironment` but is gated only by class-level `[Authorize]` (any authenticated clinic user), whereas the comparable-privilege `regenerate-code` is `AuthorizationPolicies.AdminOnly`. Any staff member can flip the clinic to `Production`, which causes real, legally-binding e-invoices to be signed with the qualified cert and submitted to the government platform. Fix: gate the e-invoicing settings change (at minimum the switch to `Production`) behind `AdminOnly`, consistent with `regenerate-code`. *(The Submit/download endpoints are correctly clinic-scoped — no IDOR/path-traversal: storage keys are server-generated `{clinicId}/e-invoices/...` and `GetEInvoiceArtifactQueryHandler` checks `invoice.ClinicId`.)*

### Finding 6
- **Severity:** Major
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Services/XadesEInvoiceSigner.cs
- **Line:** 39
- **Anchor:** XadesEInvoiceSigner.Sign
- **Comment:** The signer resolves one per-install certificate + password (`.local/teif-signing.pfx`) with no clinic dimension, so in a multi-clinic install every clinic's e-invoices are signed with the same qualified identity (`DispatchAsync` never scopes the key to `invoice.ClinicId`). This violates the spec's "each clinic signs with its own certificate" intent, and a cert compromise is install-wide. This is the known single-cert-per-install simplification (documented) — resolve before Production by either enforcing one clinic per install (document the constraint) or keying the cert/password lookup by clinic.

### Finding 7
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.Application/Features/Invoices/Queries/GetInvoicePdfQuery.cs
- **Line:** 76
- **Anchor:** GetInvoicePdfQueryHandler.Handle
- **Comment:** The QR generation (`_qrCodeGenerator.GeneratePng(invoice.QrPayload)`) is inside the handler's outer `try/catch`, so a QR render failure (e.g. QRCoder `DataTooLongException` — the `QrPayload` column allows up to 2000 chars) fails the ENTIRE PDF with the generic error, blocking the note-d'honoraires download for exactly the legally-important *validated* invoices. Fix: wrap QR generation in its own try/catch, log, and render the PDF without the cachet rather than failing the whole document.

### Finding 8
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs
- **Line:** 125
- **Anchor:** EInvoiceService.DispatchAsync (Rejected branch)
- **Comment:** In the `Rejected` branch `await StoreReceiptAsync(...)` uploads the rejection receipt but discards the returned key, and `MarkEInvoiceRejected(reason)` stores no key. The blob is orphaned in file storage — no code can ever retrieve it (`HasTtnReceipt` stays false). Either don't store a receipt for rejections, or extend `MarkEInvoiceRejected` to persist the key (mirroring `MarkEInvoiceValidated`).

### Finding 9
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.API/Controllers/InvoicesController.cs
- **Line:** 193
- **Anchor:** InvoicesController.GetSignedTeif / GetTtnReceipt
- **Comment:** Both artifact endpoints funnel every failure through `HandleFailure(result, StatusCodes.Status404NotFound)`, so the handler's catch-all IO/storage error ("Erreur lors du téléchargement du document.") — a 500-class fault — is reported to the client as 404, hiding real infrastructure failures. Distinguish the genuine not-found/unavailable cases (→404) from the download/IO error (→500/default) — the handler currently collapses all reasons into one string, so it needs a distinct error kind to differentiate.

### Finding 10
- **Severity:** Minor
- **Category:** Error Handling
- **File:** api/ClinicManagement.Infrastructure/Services/HttpTtnClient.cs
- **Line:** 100
- **Anchor:** HttpTtnClient.AcquireTokenAsync
- **Comment:** On a non-success token response the method does `return null` with no logging, and the caller maps null → `Transient("Authentification TTN indisponible.")`. A permanent 401 (bad `client_id`/`client_secret`) is thus silently classified as transient — it burns the whole retry budget to `Failed` and leaves the operator no diagnostic. Log the token-endpoint status code (never the secret/body) at Warning so a credential misconfiguration is diagnosable.

### Finding 11
- **Severity:** Minor
- **Category:** Business Logic
- **File:** api/ClinicManagement.Infrastructure/Services/EInvoiceService.cs
- **Line:** 178
- **Anchor:** EInvoiceService.BuildInput
- **Comment:** `BuildInput` hardcodes `BuyerNationalId = null` and `BuyerMatriculeFiscal = null`, so `TeifXmlGenerator`'s B2C-vs-B2B branch always resolves to B2C and every TEIF emits a blank buyer `PartnerIdentifier`. `Patient` has no MF/CIN field, so always-B2C is defensible, but the B2B path + buyer-identifier fields are then unreachable dead configuration. Either drop the B2B branch as intentionally unsupported, or source a buyer CIN; at minimum confirm a blank `I-03` buyer identifier is accepted by TTN before relying on it.

### Finding 12
- **Severity:** Minor
- **Category:** Security
- **File:** api/ClinicManagement.Infrastructure/Services/XadesEInvoiceSigner.cs
- **Line:** 44
- **Anchor:** XadesEInvoiceSigner.Sign
- **Comment:** The private key is loaded with `X509KeyStorageFlags.Exportable`, unnecessary for a signing-only operation — it permits the private key to be marshalled out in-process. `EphemeralKeySet` is correct; drop `Exportable` and load with `EphemeralKeySet` only. (Disposal + RSA-SHA256/SHA-256 are correct.)

### Finding 13
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/TtnConfig.cs
- **Line:** 79
- **Anchor:** TtnConfig.IsProduction
- **Comment:** `"Production"` is a hardcoded magic string here while the canonical `Clinic.TtnEnvironmentProduction` const already exists and is used elsewhere. Infrastructure depends on Domain — reference `Clinic.TtnEnvironmentProduction` / `Clinic.TtnEnvironmentSandbox` to avoid drift.

### Finding 14
- **Severity:** Minor
- **Category:** Breaking Change
- **File:** api/ClinicManagement.Application/Features/Clinics/Commands/RegenerateClinicCodeCommand.cs
- **Line:** 74
- **Anchor:** RegenerateClinicCodeCommandHandler.Handle
- **Comment:** This site builds `ClinicDto` without the two new TTN fields, so it always returns `TtnEInvoicingEnabled=false` / `TtnEnvironment="Sandbox"` regardless of the clinic's real settings. Unlike Create/Join (brand-new clinics where the defaults are correct), this runs on an existing clinic that may have Production enabled, so the response is stale. Not a regression (fields are new) and the authoritative read (`GetUserStatusQuery`) is correct, so the settings screen stays accurate — impact limited to the regenerate-code response. Add `TtnEInvoicingEnabled`/`TtnEnvironment` here (and, for consistency, at Create/Join).

### Finding 15
- **Severity:** Minor
- **Category:** Code Quality
- **File:** api/ClinicManagement.Infrastructure/Services/TeifXmlGenerator.cs
- **Line:** 119
- **Anchor:** TeifXmlGenerator.BuildMonetaryAmounts
- **Comment:** Leftover stream-of-consciousness comment committed as-is: `// I-176 total HT, I-180 total VAT, I-161 stamp duty, I-180... TTC I-180? Use I-179 for TTC.` Replace with a clean statement of the final mapping (I-176/I-180/I-161/I-179) or drop it.

### Finding 16
- **Severity:** Minor
- **Category:** Frontend
- **File:** web/lib/api/invoices.ts
- **Line:** 89
- **Anchor:** invoicesApi.downloadEInvoiceArtifact
- **Comment:** The raw-fetch path throws `new Error(text)` with the verbatim body, bypassing `client.ts`'s `{ error }` flattening — so a 404 like `{"error":"Document indisponible…"}` is shown to the user as raw JSON in the toast. Mirrors the existing `downloadPdf` shortcut, but the `{ error }` contract makes it more visible. Fix: parse the body and extract `.error`/`.title` before throwing, or route through shared error handling.

### Finding 17
- **Severity:** Suggestion
- **Category:** Error Handling
- **File:** api/ClinicManagement.API/Controllers/InvoicesController.cs
- **Line:** 177
- **Anchor:** InvoicesController.SubmitToElFatoora
- **Comment:** `SubmitToElFatoora` uses the default `HandleFailure(result)` (400), so a not-found invoice ("Facture introuvable.") returns 400 instead of 404, inconsistent with the repo's not-found→404 convention. The command mixes not-found and validation failures into one string; split the not-found case if you want the 404 distinction.

### Finding 18
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.API/BackgroundJobs/EInvoiceOutboxJob.cs
- **Line:** 3
- **Anchor:** EInvoiceOutboxJob
- **Comment:** The API-layer job reaches into `ClinicManagement.Infrastructure.Services` only for `TtnConfig.DispatchBatchSize(...)`, coupling it to a concrete Infrastructure class while its other deps are Application/Domain abstractions. Read the batch size via the injected `IConfiguration` directly, or expose it through an Application-layer options abstraction.

### Finding 19
- **Severity:** Suggestion
- **Category:** Code Quality
- **File:** api/ClinicManagement.Application/Common/Models/EInvoiceModels.cs
- **Line:** 48
- **Anchor:** SignedEInvoiceResult
- **Comment:** The XML doc says "The signed TEIF XML plus the digest, for logging/traceability," but the type carries only `SignedXml` — no digest member. Update the comment to match (or add the digest if intended).

### Finding 20
- **Severity:** Suggestion
- **Category:** Frontend
- **File:** web/components/factures/invoices-table.tsx
- **Line:** 139
- **Anchor:** handleSubmitEInvoice
- **Comment:** When online, an inline dispatch that hits a transient failure leaves the invoice `Queued` with `eInvoiceLastError` set, but the `else` branch still fires the green `toast.success("Envoi à El Fatoora en cours…")` — an optimistic success even though the last attempt errored (the error is only visible via the badge tooltip). Optional: if `updated.eInvoiceStatus === "Queued" && updated.eInvoiceLastError`, use `toast.warning` with the error.

## Review Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| Major | 6 |
| Minor | 10 |
| Suggestion | 4 |
| **Total** | 20 |

### Verified clean (no findings)
- Migration ↔ snapshot ↔ entity configs: exact match on every new column (names/types/nullability/defaults/index); additive + safe for existing rows.
- Multi-tenant isolation on submit/download handlers (`ClinicId` checks mirror existing invoice handlers); no IDOR / path traversal (server-generated storage keys).
- Retry/backoff math (exactly `maxAttempts`, no off-by-one/stuck/forever-retry); connectivity gating; QR-only-when-Valid PDF path (pre-validation + medical-doc PDFs unaffected).
- DI lifetimes + the two-`ITtnClient` `IEnumerable` resolution; Hangfire per-job scope; `Program.cs` job registration additive.
- No secrets logged/returned/committed; TEIF built via `XDocument` (no injection); `XmlResolver` null (no XXE); HTTP timeouts set; sandbox `SecurityElement.Escape` correct.
- Frontend: types match backend DTOs; `busyId` disabling; blob URL revoked; all 8 status values have label+badge; TTN settings load/edit/cancel/save symmetric.

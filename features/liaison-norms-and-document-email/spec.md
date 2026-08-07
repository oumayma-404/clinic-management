# Feature Specification: Lettre de liaison aux normes + envoi de tout document par email

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-31
**Scope:** Full
**Feature:** Bring the lettre de liaison in line with the medical norms while letting the doctor write freely, and let any generated document be emailed from the app.

> **Scope note (user-affirmed).** This is larger than `/define-small-feature` normally covers — the email half
> adds an outbound channel (SMTP seam, per-clinic settings + encrypted secret, outbox, dispatcher job, send
> history) across six document kinds. The user chose to keep both halves in one pass with nothing deferred.

## Overview
The lettre de liaison renders exactly five guided headings (`LiaisonContent.cs:13-20`) and its free-text body is
rendered **only** when no guided field is filled — so a new letter cannot carry prose, and the doctor is forced
into a form. It is also missing items the norms require. Part A makes free text the primary body, keeps every
section optional, and adds the missing norm fields. Part B adds « Envoyer par email » to every document the app
generates, as a queued outbox so it works on an offline LAN install.

**Norms applied** (décret n° 2016-995 du 20/07/2016; HAS; dental-correspondence practice): identity of the
patient *and* of the professionals involved (rédacteur, médecin traitant, praticien adresseur), motif de la
liaison, synthèse clinique, traitement en cours + allergies connues, prescriptions, résultats d'examens en
attente, consignes de suivi / ce qui est attendu du correspondant, pièces jointes, signature + cachet.

## What Changes

### Part A — Lettre de liaison
- The `content` key becomes the letter's **primary free-text body** (« Corps de la lettre / Synthèse clinique »),
  rendered as one unlabelled prose block. The "only when no guided field is present" condition is **removed** —
  that condition is the defect: it made prose and structure mutually exclusive.
- Guided sections remain, all **optional**, rendered only when filled, in norm reading order: Motif → *(free-text
  body)* → Examen clinique → Examen radiologique → Actes réalisés → **Traitement en cours et allergies connues** →
  Prescriptions → **Résultats d'examens en attente** → **Consignes de suivi / avis attendu** → **Pièces jointes**.
- New optional identity field **« Médecin traitant / praticien adresseur »**, rendered under the patient identity
  block, not as a body section — the norms place professional identity with the patient's, not in the synthèse.
- The only required field stays the confrère destinataire. No new field is ever required.
- The editor's liaison tab is reordered to match: destinataire, motif, one large free-text body, then the
  complementary sections in a collapsed « Sections complémentaires (optionnel) » group.

### Part B — Envoi par email
- One generic send endpoint takes a **document kind + id**, re-renders the PDF **server-side**, and queues an
  email. A client-supplied attachment is never accepted (same rule as `generate-pdf-download`'s cachet).
- Covers all six kinds the app generates: `medical-document` (ordonnance, liaison, certificat, bulletin CNAM),
  `invoice`, `credit-note` (avoir), `treatment-plan` (devis), `invoice-payment-receipt`,
  `installment-payment-receipt`.
- Recipient is prefilled — patient email when present, or the confrère's email for a liaison — and freely
  editable; subject and message body are prefilled in French and editable.
- Sends are queued and dispatched by a new minutely, connectivity-gated `DocumentEmailJob` with bounded retry, so
  an offline LAN install queues instead of failing. Each document shows its send history (recipient, moment,
  status, failure reason).
- SMTP settings are **per-clinic**, mirroring the reminder-channel pattern: host/port/TLS/username + an encrypted
  password on the existing `ClinicReminderSettings` row, falling back to the per-install `Notification:Smtp:*`
  config, editable in `reminder-settings.tsx` with the same `configured` / `not_configured` badge.

## Acceptance Criteria
- **AC-1:** A liaison with only free-text body and no guided field renders that prose as the body (no heading,
  no empty headings) — on screen and in the PDF.
- **AC-2:** A liaison with **both** free text and guided fields renders both, free text first among the body
  sections, each guided section under its heading, in the order listed above.
- **AC-3:** An existing (legacy) liaison whose `ContentJson` carries only `content` renders unchanged.
- **AC-4:** Saving a liaison with only a destinataire and a free-text body succeeds; no new field is required.
- **AC-5:** Each of the four new liaison fields (traitement/allergies, examens en attente, consignes de suivi,
  pièces jointes) and « Médecin traitant » round-trips through `ContentJson` and appears on the PDF when filled,
  and is entirely absent when empty.
- **AC-6:** « Médecin traitant » renders in the identity block, not as a body section.
- **AC-7:** `POST /api/document-emails` with a valid kind + id queues a send and returns it; the queued row names
  the recipient, the kind and the id, and holds **no** attachment bytes.
- **AC-8:** The endpoint refuses a document belonging to another clinic (404, tenant-checked like every other
  document read) and an invalid recipient address (400, French message).
- **AC-9:** `DocumentEmailJob` re-renders the PDF at send time, attaches it, and marks the row `Sent`; a
  transient SMTP failure keeps the row queued and retries up to the configured maximum, then marks it `Failed`
  with the reason. It sends nothing when the server has no internet (Local mode) and consumes no retry.
- **AC-10:** With no SMTP configured for the clinic, the send is refused up-front with a French message naming
  the settings — never queued into a row that can only fail.
- **AC-11:** Each of the six document kinds can be emailed from its own screen, and each shows its send history.
- **AC-12:** The email settings screen shows `configured` / `not_configured` per the resolved (clinic-else-install)
  settings, and the stored password is encrypted at rest via `IReminderSecretProtector`.

## API Contract
### POST /api/document-emails
Request: `{ documentKind: string, documentId: guid, recipientEmail: string, subject: string, body: string }`
*(`installment-payment-receipt` additionally carries `installmentId` + `paymentId`; `invoice-payment-receipt`
carries `paymentId` — the receipt renderers are keyed by those ids, not by the parent alone.)*
Response 201: `{ id, documentKind, documentId, recipientEmail, subject, status, queuedAt, sentAt, failureReason }`
Errors: `400 — adresse email invalide.` · `400 — l'envoi par email n'est pas configuré pour ce cabinet …` ·
`404 — document introuvable.`

### GET /api/document-emails?documentKind=&documentId=
Response 200: `DocumentEmailDto[]` (newest first — the send history for one document)

### GET/PUT /api/clinics/reminder-settings
Extended with `smtpHost`, `smtpPort`, `smtpUseTls`, `smtpUsername`, `smtpPassword` (write-only),
`smtpFromAddress`, `smtpFromName`, and a per-channel `email` entry in the existing `effectiveStatus`.

## Data / Schema Changes
- **New `DocumentEmail` aggregate** (clinic-owned): `ClinicId`, `DocumentKind`, `DocumentId`, plus the optional
  `InstallmentId`/`PaymentId` render keys, `RecipientEmail`, `Subject`, `Body`, `Status`
  (`Queued|Sent|Failed`), `Attempts`, `QueuedAt`, `SentAt`, `FailureReason`, `RequestedByUserId`. Index on
  `(Status, QueuedAt)` for the dispatcher scan and on `(ClinicId, DocumentKind, DocumentId)` for the history read.
  ⚠️ Deliberately **not** rows on the existing `Notification` outbox: those carry appointment/recall semantics
  the dispatcher branches on (still-active re-check, `ClearRecallSnooze`) and a reminder retention purge, none of
  which apply to a document. It reuses the *pattern* (connectivity gate, per-row commit, bounded retry, batch
  cap, terminal-row purge) and the settings/secret infrastructure, not the table.
- **`ClinicReminderSettings`** gains the SMTP fields above (password encrypted via `IReminderSecretProtector`).
  Extending this row rather than adding a parallel settings aggregate + provider + protector for one channel.
- **`MedicalDocument`**: no schema change — the new liaison fields are `ContentJson` keys
  (`traitementEnCours`, `examensEnAttente`, `consignesSuivi`, `piecesJointes`, `medecinTraitant`,
  `recipientEmail`), like the existing guided fields.
- Both new indexes and the new table must be reflected by `dotnet run -- verify-schema` (the only gate for a
  schema change — nothing in the test project touches a database).
- The new `documentemails` realtime resource key must be added to `web/lib/realtime/clinic-hub.ts`;
  `RealtimeResourceResolverTests` fails the build in both directions otherwise.

## Out of Scope
- Reading inbound email, or any reply/threading.
- Emailing anything that is not one of the six generated document kinds (no ad-hoc attachments, no patient files).
- Sending to more than one recipient per send, CC/BCC, or a per-clinic email signature/template editor beyond the
  prefilled subject/body.
- Patient-facing consent capture for emailing PHI, and end-to-end/PDF encryption or password-protected PDFs.
- Reviving the dormant email `Notification` path for appointment reminders (SMS/WhatsApp stay as they are).

## Edge Cases (Critical only)
- A liaison whose free text **and** every guided field are empty: the body renders empty rather than with stray
  headings, and saving is still allowed (only the destinataire is required).
- Patient has no email (`Email` is nullable): the dialog opens with an empty, required recipient — never a
  sentinel address.
- A document deleted after its send was queued: the dispatcher cannot render it, so the row goes `Failed` with a
  French reason and is not retried forever.
- An avoir/reçu is a money document: the emailed PDF must be the same bytes the download produces, including the
  « REÇU ANNULÉ » stamp on a voided receipt.
- SMTP credentials that no longer decrypt (rotated key): treated as **not configured** — the send is refused
  up-front, matching how the reminder channels already degrade.

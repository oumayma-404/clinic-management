# Exploration — copie du cabinet sur un PC (hosted)

> 2026-10-06 · `/think-solution` (6 passes) + `/define-feature` (8 passes). Facts only; decisions are in
> `blueprint.md` and `spec.md`.

## 1. What exists today

- **No link between a hosted clinic and any PC copy.** Local and hosted are separate products
  (`windows-desktop-app/spec.md:37,214`). Full two-way sync was refused (`multi-tenant-cloud/plan.md:443`):
  gapless per-year note / devis / avoir numbers cannot survive two writers.
- **One-way pulls only**: the Windows app copies the clinic archive every 7 days (keeps 4) and mirrors patient
  files, both through a device grant (« Postes autorisés », `archive-grants-card.tsx:152`). Archive download is
  rate-limited to 3 per 10 min (`RateLimiting.cs:115`) — not usable as a live feed.
- **No replication, no change log**: no `changed since` read, hard deletes without tombstones, the audit ledger
  cannot replay a change (`AuditEntry.cs:20-23`).
- **Archive**: a JSON-per-table zip keyed by EF property names, tenant scope **derived from the EF model**
  (`ClinicArchiveScope.Resolve`, Self / Direct / Child, FK-ordered). Restore is additive (insert missing rows
  only). Excludes `User`, audit, outboxes, `DataProtectionKey`, grants, subscription rows.
- **Keys**: GUIDs made by the app (no collisions across DBs); exceptions `PatientPhoneNumbers` (DB int) and
  `DataProtectionKey`. Numbers (note, devis, avoir) are MAX+1 per clinic per year with a unique index.
- **Key rings differ per install**: hosted = certificate-protected; LAN = DPAPI machine. TOTP secrets
  (`User.ProtectedTotpSecret`, purpose `ClinicManagement.User.TotpSecret.v1`) cannot be read on another install.
- **Accounts**: one account = one clinic (`User.ClinicId`), emails unique across clinics. Roles admin / doctor /
  secretary. Each install signs its own tokens. Admin 2FA mandatory on hosted.
- **No offline cache** anywhere in `web/`, Windows or Android apps.

## 2. Volume (dev DB, one realistic clinic)

| Measure | Value |
|---|---|
| Patients | ~2 230 |
| Appointments / fiches per day | ~20 / ~20 (median) |
| Clinic rows | ~30 MB (audit ledger largest, 23 rows per appointment) |
| Patient files | ~50 MB here; quota 10 GB per clinic; one file up to 150 MB |

## 3. What users see today when things fail

- Save with the server gone → toast « Impossible de joindre le serveur. Vérifiez votre connexion, puis
  réessayez. », form stays open; reload loses typing. Header badge « Serveur injoignable ».
- Windows / Android app: the whole app is replaced by « Impossible de joindre le serveur du cabinet » +
  « Réessayer » / « Changer de serveur » when the page itself fails to load.
- 409 → « Cet enregistrement a été modifié par quelqu'un d'autre… Rechargez… » via `useConflict`.
- Sessions survive an outage shorter than their window (12 h, or 30 days with « Rester connecté »).
- Reminders: created at booking (24 h + 6 h before); with no internet they wait; a passed visit fails them
  silently (« Rendez-vous déjà passé — rappel obsolète, non envoyé »).
- Google Agenda: a skipped push is never retried; the appointment shows « non synchronisé »; admin « Envoyer ».
- The only read-only state: expired subscription → 402 + banner « Abonnement expiré le … — Vos données restent
  consultables… » (`subscription-banner.tsx`, mounted `app-shell.tsx:102`).

## 4. UI patterns to reuse

- Paramètres is one stack of admin-gated cards (`clinic-settings.tsx`); backup cards at `:1508`.
- Banner on every screen: `SubscriptionBanner` (wash colour + icon + words, 320 px handled).
- Progress: `upload-queue.tsx:414` (`Progress` + « N % »).
- Step-up: `step-up-dialog.tsx` « Confirmer votre identité » (authenticator code for `show-money`).
- Ask-later pattern: `post-visit-review-popup.tsx` (localStorage snooze). **No « never ask again » exists.**
- Errors: server sends `{ error, code }` with the French sentence; client shows it as is.

## 5. Words already taken

| Word | Means today |
|---|---|
| « Hors ligne » | the server has no internet |
| « Serveur injoignable » | the server cannot be reached |
| « Serveur du cabinet » / « PC serveur » / « installé sur un PC du cabinet » | the self-hosted LAN install |
| « Poste » | a client machine (installer role, archive grants, coffre) |
| « Copie » | the archive zip / file mirror |
| « Brouillon » | a draft note / devis |
| « Relais » | free (only « relais SMTP ») |
| « Mode cabinet » | free |

## 6. Install and devices

- Installer roles: « Le serveur du cabinet » / « Un poste de travail ». Server role turns sleep off when plugged
  in; no free-disk check. Updates = re-run installer. Needs admin rights.
- Windows app trusts the cabinet CA via the Windows store (installer imports it). Android: user installs the CA
  by hand (Paramètres → Sécurité). Nothing tells a device a LAN server's address; nothing detects a battery.

## 7. Vendor side

- Console clinic list has no health column; **no alert path exists** (no email except signup/reset).
  `server-loss-recovery` Part 3 plans a daily email to console accounts — share it.

# Feature Specification: WhatsApp Embedded Signup — Connect Flow (P1)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** Full
**Feature:** Cloud-mode admins connect their clinic's own WhatsApp Business number via Meta's Embedded Signup popup, and the backend auto-provisions and stores the per-clinic WhatsApp credentials — no manual token/Phone-ID pasting.

> Slice of the larger multi-phase sketch (see `sketch.md`). This spec covers **Phase 1 only**: the Cloud Embedded-Signup connect/disconnect flow + connection-status surfacing. Automated per-WABA template creation, billing-state health, delivery webhooks, and re-consent (P2–P4) stay out of scope.

## Overview
Today a Cloud admin must hand-paste a WhatsApp Phone-Number-ID, template, and access token into the reminder settings. This feature adds a **"Connecter WhatsApp"** button (Cloud mode only) that launches Meta's hosted Embedded Signup. On completion the frontend returns `{ code, waba_id, phone_number_id }`; the backend exchanges the code for a business token, subscribes the platform app to the WABA, registers the phone number, and stores the encrypted credentials on the clinic's existing `ClinicReminderSettings`. Reminders then send unchanged via the current per-clinic `WhatsAppSender`.

## Assumptions / Prerequisites (external, not implemented here)
- Requires Meta **Phase-0** setup to work end-to-end: a Meta app with the WhatsApp product + Facebook Login for Business, an Embedded-Signup **`config_id`**, Advanced Access for `whatsapp_business_management` + `whatsapp_business_messaging`, and Business Verification. Until these exist the flow can only be exercised with mocked Graph calls — real connect testing is deferred to a verified Meta app.
- A **reminder template** is still created manually per clinic (existing manual fields) — automated template creation is P2.

## What Changes
- Cloud-mode reminder settings gain a **"Connecter WhatsApp"** button that runs Meta Embedded Signup (Meta JS SDK + `config_id`) and captures `{ code, waba_id, phone_number_id }`.
- New **`POST /api/clinics/whatsapp/connect`** (AdminOnly): exchange code → subscribe app → register phone → encrypt & store token + phone-number-id + WABA-id on the caller's `ClinicReminderSettings`, set `WhatsAppEnabled = true`, status `Connected`. **Atomic** — on any step failure nothing is stored, status stays `NotConnected`, a specific French error is returned.
- New **`DELETE /api/clinics/whatsapp/connect`** (AdminOnly): clear the stored WhatsApp phone-id/WABA-id/token, set `WhatsAppEnabled = false`, status `NotConnected`; best-effort app-unsubscribe (a failure there still disconnects locally).
- `ClinicReminderSettings` gains connection metadata (WABA id, status, last error, connected-at); reminder settings screen shows a **status badge + masked number + last error + Disconnect** when connected.
- Both connect/disconnect endpoints are **Cloud-only** (return 404 in Local mode, mirroring the Local-only `Connectivity` endpoint) — Local keeps the manual path as its only mechanism, so the Local attack surface is unchanged.

## Acceptance Criteria
- **AC-1:** In Cloud mode, an admin on the reminder-settings screen sees a "Connecter WhatsApp" button; in Local mode the button is absent and only the manual fields show.
- **AC-2:** A successful Embedded-Signup run POSTs `{ code, wabaId, phoneNumberId }` to `/connect`; the backend performs exchange → subscribe → register → store and returns the secret-masked settings with status `Connected`, a masked phone number, and `WhatsAppEnabled = true`.
- **AC-3:** If any step (exchange / subscribe / register) fails, **no credentials are persisted**, status stays `NotConnected`, `WhatsAppEnabled` is unchanged, and the caller gets a distinct French message (Meta-connection failure vs. number-already-registered vs. WABA-not-eligible).
- **AC-4:** The user closing/abandoning the Meta popup is a no-op ("Connexion annulée.") — no request is sent, nothing changes.
- **AC-5:** `DELETE /connect` clears the stored WhatsApp credentials, sets status `NotConnected` and `WhatsAppEnabled = false`; app-unsubscribe failure is logged but does not fail the disconnect.
- **AC-6:** Both endpoints are AdminOnly and clinic-scoped (act only on the caller's clinic); in Local mode both return 404.
- **AC-7:** The stored access token is encrypted at rest via the existing `IReminderSecretProtector`; the connect response never returns the token in plaintext (only `whatsAppAccessTokenConfigured` / masked values).
- **AC-8:** The reminder-settings screen shows a connection status badge (Connecté / Non connecté / Erreur) with the masked number and last error when present.

## API Contract
### POST /api/clinics/whatsapp/connect  (AdminOnly; Cloud-only, 404 in Local)
Request: `{ code: string, wabaId: string, phoneNumberId: string }`
Response 200: `ReminderSettingsDto` extended with `{ whatsAppBusinessAccountId: string | null, whatsAppConnectionStatus: "NotConnected" | "Connected" | "Error", whatsAppLastError: string | null, whatsAppConnectedAt: string | null }` (secret-masked; token never returned)
Errors: `400 — "Échec de la connexion à Meta, réessayez."` (code→token) · `400 — number already registered / needs migration (Meta reason surfaced)` · `400 — WABA non éligible (vérification requise)` · `403 — non-admin` · `404 — Local mode`

### DELETE /api/clinics/whatsapp/connect  (AdminOnly; Cloud-only, 404 in Local)
Request: (none)
Response 200: extended `ReminderSettingsDto` with status `NotConnected`
Errors: `403 — non-admin` · `404 — Local mode`

## Data / Schema Changes
- `ClinicReminderSettings` — add nullable columns, all additive (existing rows default to not-connected):
  - `WhatsAppBusinessAccountId` (string?, the WABA id)
  - `WhatsAppConnectionStatus` (enum stored as int: `NotConnected` = 0 / `Connected` / `Error`; default `NotConnected`)
  - `WhatsAppLastError` (string?, drives the badge)
  - `WhatsAppConnectedAt` (DateTime?, UTC)
- New domain method(s) on `ClinicReminderSettings` to apply/clear connection state (mirrors the existing intention-revealing setters; token still goes through `SetWhatsAppAccessTokenEncrypted`).
- Additive EF migration.
- `ReminderSettingsDto` (+ FE `ReminderSettingsDto`/`reminderSettingsApi`) gain the four new read-only fields.

## Config / Env (names only)
- Server (`Meta` section): `Meta:AppId`, `Meta:AppSecret` (env/secret only, never committed), `Meta:GraphApiVersion` (or reuse the existing `Reminders:WhatsApp:ApiUrl` Graph base). Graph calls go to `graph.facebook.com/v{version}`: `/oauth/access_token` (exchange), `/{wabaId}/subscribed_apps`, `/{phoneNumberId}/register`.
- Frontend (public, non-secret): `NEXT_PUBLIC_META_APP_ID`, `NEXT_PUBLIC_META_CONFIG_ID` — to init the Meta JS SDK / Embedded Signup.

## New backend seam
- `IWhatsAppOnboardingService` / `WhatsAppOnboardingService` (Infrastructure): `ExchangeCodeAsync(code)` → business token; `SubscribeAppAsync(wabaId, token)`; `RegisterPhoneAsync(phoneNumberId, token)` (generates a registration PIN as needed; PIN not persisted in P1). Uses `IHttpClientFactory` + the `Meta` config. Injected by a new `WhatsAppOnboardingController` (or a `ClinicsController` action group).

## Out of Scope
- Automated per-WABA reminder-template creation + status polling (P2).
- Billing-not-set health state + "add billing" link (P2).
- Delivery/status **webhooks** (`GET`/`POST /api/whatsapp/webhook`, signature verification) + `StaffNotification` failure alerts + token re-consent (P3).
- Manual-token validation-on-save warning, ongoing status polling, quality-rating surfacing, multi-number (P2–P4).
- SMS, inbound/two-way chat, marketing broadcasts, consolidated billing.

## Edge Cases (critical only)
- **Popup abandoned** → no request, "Connexion annulée." (AC-4).
- **Partial provisioning** (e.g. exchange succeeds but subscribe fails) → persist nothing, stay `NotConnected` (AC-3) — never leave half-stored creds.
- **Re-connect over an existing connection** → overwrite phone-id/WABA-id/token and re-stamp `WhatsAppConnectedAt` (last write wins).
- **Local mode** → connect/disconnect endpoints 404; button not rendered (AC-1, AC-6).

# Feature Sketch: Platform-Owned WhatsApp via Embedded Signup

**Status:** DRAFT (sketch)
**Challenged:** Yes
**Type:** Feature (multi-phase; external Meta dependencies)
**Goal:** Let a clinic connect its own WhatsApp Business number to the platform in a self-serve popup — no dev console, no manual token/Phone-ID pasting — so appointment reminders send from *that clinic's* WhatsApp identity, billed to *that clinic*.

## Overview

Today WhatsApp reminders work per-clinic, but an admin must manually paste a Phone-Number-ID, template name, and access token into Settings (`ClinicReminderSettings`). This feature adds **Meta Embedded Signup** for the **Cloud (hosted, multi-tenant) deployment**: a "Connect WhatsApp" button launches Meta's hosted flow; on completion the backend auto-provisions and stores the clinic's WhatsApp credentials. The platform is a **Meta Tech Provider**; each clinic keeps its own WABA + billing under the platform's app.

## Deployment model (decided)

Embedded Signup has two steps that **cannot run on an offline-LAN box**: the OAuth **code→token exchange** (needs the Meta **app secret**, which must never ship to on-prem installs) and Meta **delivery webhooks** (need a **public HTTPS URL** a LAN install doesn't have). Therefore:

| | **Cloud** (hosted, multi-tenant) | **Local** (offline-LAN, per-install) |
|---|---|---|
| Onboarding | **Embedded Signup** (one-click popup, auto-provision) | **Manual entry** (paste Phone-Number-ID + token) — *already built* |
| App secret / code exchange | server-side (hosted) | n/a (no Embedded Signup) |
| Delivery/status | **webhooks** (public endpoint) | **polling** template/message status when online; no webhooks |
| Sending | `WhatsAppSender` (unchanged, per-clinic) | `WhatsAppSender` (unchanged, per-clinic) |

Both deployments send reminders identically via the existing per-clinic pipeline; only **onboarding + status** differ. No vendor relay is introduced. The manual path stays the supported Local mechanism (and a Cloud fallback).

## Current state (reused, not rebuilt)

- `ClinicReminderSettings` (1:1 with `Clinic`, shared PK) — stores `WhatsAppPhoneNumberId`, `WhatsAppTemplateName`, `WhatsAppTemplateLanguage`, `WhatsAppAccessTokenEncrypted` (Data-Protection, write-only), `WhatsAppEnabled`.
- `ReminderSettingsProvider` — merges per-clinic override → per-install default; endpoint URL stays per-install.
- `WhatsAppSender` — already sends per-clinic (reads resolved settings). Works unchanged.
- `IReminderSecretProtector` — encrypts secrets at rest.
- Admin write path: `UpdateClinicReminderSettingsCommand` + `ClinicsController` + `reminder-settings.tsx`.

**Gap:** no Embedded Signup flow, no OAuth code exchange, no phone-number register, no webhooks, no automated per-WABA template creation.

## Phase 0 — Meta platform prerequisites (one-time, external; days–weeks)

- Meta Business Manager + **Business Verification**.
- App with **WhatsApp** product + **Facebook Login for Business**.
- Configure **Embedded Signup** → obtain a **`config_id`**.
- **App Review / Advanced Access** for `whatsapp_business_management` + `whatsapp_business_messaging`.
- Platform **System User** + long-lived token (used for app-level calls: subscribe apps, code exchange).
- Register as **Tech Provider**; decide billing model (client-paid vs consolidated credit line).

## Architecture / flow

```
Clinic admin → [Connect WhatsApp] (FB JS SDK, config_id, Embedded Signup)
   → Meta popup: login, create/select WABA + phone number, grant app access
   → SDK returns { code, waba_id, phone_number_id } to the frontend
   → POST /api/clinics/whatsapp/connect { code, wabaId, phoneNumberId }
       backend:
         1) exchange code → business access token (GET /oauth/access_token, app creds)
         2) POST /{waba_id}/subscribed_apps        (subscribe our app to the WABA)
         3) POST /{phone_number_id}/register        (set a 2FA PIN; if required)
         4) encrypt token → ClinicReminderSettings (phoneNumberId, wabaId, token), WhatsAppEnabled=true
         5) (optional) create reminder template in the WABA
   → later: POST /api/whatsapp/webhook receives message-status + template/account updates
```

## Backend work (`api/`)

**New endpoints (ClinicsController or a new `WhatsAppOnboardingController`):**
- `POST /api/clinics/whatsapp/connect` (AdminOnly) — body `{ code, wabaId, phoneNumberId }`. Does the exchange → subscribe → register → store. Returns connection status (masked).
- `DELETE /api/clinics/whatsapp/connect` (AdminOnly) — disconnect: clear stored creds, `WhatsAppEnabled=false`, unsubscribe app.
- `GET /api/whatsapp/webhook` — Meta verify challenge (hub.challenge). **Cloud-only** (like `Connectivity` is Local-only): returns 404 in Local mode. `[AllowAnonymous]`, but Meta-authenticated via signature (below).
- `POST /api/whatsapp/webhook` — receives events; **must verify `X-Hub-Signature-256`** against the app secret. Cloud-only. Persist message status; refresh template status.
- ⚠️ **Local fail-closed allow-list:** these anonymous endpoints must be added to the pinned anonymous allow-list (`ControllerAuthorizationCoverageTests`) **and gated Cloud-only** so they don't widen the Local attack surface. In Local the whole webhook controller is absent/404 — nothing to allow-list there. Signature verification is the real auth (they're anonymous to the framework but authenticated by Meta's HMAC).
- (Optional) `POST /api/clinics/whatsapp/template` (AdminOnly) — create/submit the reminder template in the clinic's WABA; `GET` its status.

**New service — `IWhatsAppOnboardingService` / `WhatsAppOnboardingService` (Infrastructure):**
- `ExchangeCodeAsync(code)` → token; `SubscribeAppAsync(wabaId, token)`; `RegisterPhoneAsync(phoneNumberId, pin, token)`; `CreateReminderTemplateAsync(wabaId, token, …)`.
- Uses the platform app id/secret (config) + the returned code. All calls to `graph.facebook.com/v{version}`.

**Config (per-install, `Reminders:WhatsApp` / new `Meta` section):** `AppId`, `AppSecret` (env), `ConfigId`, `GraphApiVersion`, `WebhookVerifyToken`, default template name/language.

## Data model changes

- `ClinicReminderSettings`: add **`WhatsAppBusinessAccountId`** (WABA id), **`WhatsAppConnectionStatus`** (`NotConnected`/`Connected`/`Error`), **`WhatsAppLastError`** (nullable string — drives the badge + in-app alert), `WhatsAppConnectedAt`. Migration (additive, nullable).
- Token stored in existing `WhatsAppAccessTokenEncrypted` (now populated automatically). Consider token type: Embedded Signup yields a business integration token — store per clinic; add a refresh/re-consent path if it can expire.
- (Optional) a `WhatsAppMessageStatus` table if you want delivery/read receipts from webhooks persisted.

## Frontend work (`web/`)

- Load the **Meta JS SDK**; add a **"Connecter WhatsApp"** button in `reminder-settings.tsx` — **shown only in Cloud mode** (`useSession().mode === 'cloud'`). In Local mode the existing **manual** Phone-ID/token fields remain the only path. In Cloud, manual entry stays available as an advanced fallback (see precedence below).
- Launch Embedded Signup with `config_id`; capture `{ code, waba_id, phone_number_id }` from the `message` event; POST to `/connect`.
- Show connection status (Connected + masked number, template status, Disconnect). French labels.
- CSP/allowed-origins: permit the Meta SDK + popup.

## Template management

- Templates are **per-WABA**. On connect (or a follow-up action) create the clinic's reminder template (`rdv_rappel`-style, one `{{1}}` body var) via `POST /{waba_id}/message_templates`; poll status; store name + language + status in `ClinicReminderSettings`.
- The existing `WhatsAppSender` (1 body param) already matches a 1-variable template — no sender change needed for prod (the `TemplateHasBodyParam` test flag stays `true`).

## Billing (decided: client-paid, own WABA)

- Each clinic adds a **payment method to their own WABA** (Meta requires it to send beyond the free window). No cost or credit exposure to the platform.
- Post-connect UI shows an **"add billing in Meta"** step + link; until done, the clinic is *connected but can't send* — surfaced as a **health-error state** (see below), not a silent failure.
- Consolidated/partner credit-line billing is **out of scope** (possible later).

## Connection lifecycle, health & failure surfacing

**Connect-flow failure states (Cloud Embedded Signup) — atomic, no partial creds stored.** Each maps to a clear French message + a safe retry; on any failure the clinic stays `NotConnected`:
- **Popup closed / abandoned** → no-op, "Connexion annulée."
- **Code→token exchange failed** → "Échec de la connexion à Meta, réessayez."
- **WABA not verified / ineligible** → explain business verification is required (link).
- **Phone number already registered elsewhere / needs migration** → surface Meta's reason + migration note.
- **Template creation rejected** (P2) → connection succeeds but reminders can't send yet → health-error "modèle non approuvé."

**Ongoing health (both deployments).** Reminder sends are best-effort (errors swallowed → row `Failed` after retries), so a broken connection would otherwise stop reminders **silently**. Mitigate:
- Persist a **connection status + last error** on `ClinicReminderSettings` (`Connected` / `Error` / `NotConnected`), updated by webhooks (Cloud) or a lightweight periodic status poll / on-send-failure detection (Local).
- Show a **status badge + last-error** in the reminder-settings screen.
- **Emit an in-app `StaffNotification`** (reuse the built notification center) when a clinic's reminders transition into a failing state (invalid/expired token, number quality-flagged, template paused, **billing not set**). Best-effort, post-detection — never blocks the pipeline.

## Onboarding precedence & manual-token validity (Cloud)

- **Embedded Signup is the primary** Cloud path. Manual entry is an explicit **"advanced override"**: when a manual credential is saved it **wins until cleared**; clearing it reverts to the Embedded-Signup-provisioned values.
- On **manual save**, validate the token with a lightweight Graph call (e.g. `GET /{phone_number_id}`); **warn** if it fails or looks short-lived (a 24h temp token), so a silently-expiring token isn't stored unnoticed.
- Local mode only ever uses the manual path (with the same validation + warning).

## Security

- App secret + platform token from **env/secret store**, never committed.
- Webhook signature verification (`X-Hub-Signature-256`).
- Per-clinic token encrypted at rest (existing `IReminderSecretProtector`).
- The `/connect` endpoint is AdminOnly + clinic-scoped (caller's clinic only).

## Out of scope

- SMS (separate paid channel; unaffected).
- Two-way WhatsApp / inbound chat handling (only outbound reminders here).
- Marketing broadcasts; only Utility appointment reminders.
- Consolidated billing (phase-later).

## Phasing

1. **P1 (core):** Cloud Embedded Signup button + `/connect` (exchange, subscribe, register, store) with **atomic failure states**; status **badge + last-error** in Settings; manual **advanced-override + token validation** (both modes). Template still created once manually per clinic.
2. **P2:** automated per-WABA template creation + status polling; **billing-not-set** health state + "add billing" link.
3. **P3:** Cloud webhooks (delivery/read status, template/account updates) + **in-app `StaffNotification` on failure transitions** + re-consent on token expiry.
4. **P4:** polish (quality-rating surfacing, multi-number, etc.).

## Open questions

- **Embedded Signup token type/lifetime** — confirm whether the code-exchange token is long-lived or needs periodic refresh/re-consent; if it can expire, the re-consent path reuses the same `Error` health state + in-app alert. (Manual tokens are already validated on save.)
- One **shared template design** across clinics (created per WABA) vs per-clinic customizable wording?
- **New number per clinic vs migrating** an existing WhatsApp number (migration has extra steps/downtime — one of the enumerated connect-flow failure states).
- Meta **verification/app-review lead time** — gates any real Cloud launch; start early.

*Resolved during challenge:* deployment split (Cloud = Embedded Signup + webhooks; Local = manual + polling) · billing (client-paid, own WABA) · connection health surfacing (badge + in-app alert) · connect-flow error enumeration (atomic) · onboarding precedence (Embedded primary, manual = validated override).

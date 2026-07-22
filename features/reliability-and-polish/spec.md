# Feature Specification: Reminder Reliability & Product Polish

**Status:** DRAFT
**Type:** Small
**Created:** 2026-07-22
**Scope:** Full
**Feature:** Make reminders actually deliverable and observable, stop silent phone-number failures, wire or remove the dead header search and working-hours settings, finish the French localization, and clean up duplicate/dead code — the layer that separates "impressive demo" from "trustworthy daily tool".

## Overview
Several things look finished but silently don't work, and the French/Tunisian product still leaks English. Reminders can show "Connecté" yet never send because the gateway URL is server-config-only and there's no delivery-status surface; inline new-patient bookings capture no phone so reminders fail invisibly, and patient phone validation accepts numbers the reminder engine later rejects. The global header search input has no handlers, and the Working Hours settings card saves nothing. The sidebar and onboarding wizards are English in a French app. And the tree carries duplicate/dead code (two "summary" buttons, four tooth-chart components, unused services). This feature closes those reliability and polish gaps. Builds on `sms-whatsapp-reminders`, `per-clinic-reminder-channels`, and `graceful-error-handling` (which already toast-ified some `alert()`s).

## What Changes

### Reminders: complete the config + show delivery status
- The per-clinic reminder settings UI (`reminder-settings.tsx`) exposes the fields that were previously per-install-only or code-only: **provider gateway/API URL** (SMS endpoint, WhatsApp Graph base), **send timing / lead-time hours**, and the **message template body / wording** — so an admin can fully turn a channel on without an operator editing server config.
- When a channel is toggled on but its effective settings still resolve to `NotConfigured` (e.g. missing URL/secret), the UI shows a clear **warning** instead of a green "Connecté" — connection status reflects *actually sendable*, not just "OAuth done".
- A **delivery-status surface**: recent reminder outbox rows (`Notification`) with their state (Envoyé / En attente / Échec + reason) are visible to the admin, so a failed reminder is noticed instead of vanishing into the table.

### Phones: capture and validate consistently
- The create-appointment **inline new-patient** path captures a **phone number** (so reminders can be sent), not just first/last name.
- Patient phone validation is unified to the reminder engine's rule (`ReminderPhone.ToE164` — Tunisian 8-digit / +216 E.164) across patient create/edit and the inline path, with an inline French error; a number that would fail `ToE164` is rejected at entry rather than accepted then silently failing at dispatch.

### Dead UI: wire or remove
- The global header **search** is wired to a real patient search (type → results → navigate to the patient), reusing the existing patient search used by the appointment picker. (If search is deemed out of scope by the reviewer, the input is removed rather than left dead.)
- The **Working Hours** settings card **persists** to the clinic (add clinic working-hours to the clinic update path) and loads from it; the hardcoded hours shown in the sidebar footer are driven by the saved value (no second, contradictory hardcoded set).

### French localization pass
- `<html lang="fr">`; translate the main **sidebar** labels, the **setup/join onboarding wizards**, and the **AI chat** greeting/header/placeholder (and set speech recognition `lang` to `fr-FR`).
- Replace remaining native English `alert()`/`confirm()` dialogs (file deletion in `patient-files-manager`/`files/page`, Google-sync `alert()`s in `appointments/page`) with the app's French toast / `AlertDialog` pattern.

### Consolidate duplicates / remove dead code
- Remove the orphan `notifications-list.tsx`; remove the duplicate static **"Patient Summary"** button (keep the real AI summary); migrate `patient-summary-modal` off `dental-chart.tsx` and remove that fourth, redundant tooth-chart component.
- Remove confirmed-dead backend: `PatientSummaryService` + `AISummaryJob` (never run), `GoogleAIService` (never registered), and the email `NotificationService` **only after** confirming zero consumers; delete `GOOGLE_AI_SETUP.md` or mark it not-wired.
- Refresh the stale root `CLAUDE.md` claims (patient AI summary is real; reminders are live; new feature areas) so docs match reality.

## Acceptance Criteria
- **AC-1:** An admin can set the SMS gateway URL, WhatsApp Graph base URL, lead-time hours, and message body in the reminder settings UI; with those + credentials set, a reminder actually dispatches (no server-config edit required).
- **AC-2:** A channel toggled on whose effective settings resolve to `NotConfigured` shows a warning state (not "Connecté"); a fully-configured channel shows connected.
- **AC-3:** The admin can view recent reminder outbox entries with status (sent / pending / failed + reason).
- **AC-4:** Creating an appointment with a new inline patient captures a phone number; that patient can receive reminders.
- **AC-5:** Patient phone validation matches `ReminderPhone.ToE164`; a number that would fail it is rejected at entry with a French message, in both the patient edit form and the inline path.
- **AC-6:** The header search returns patients matching the typed query and navigates to the selected patient — or, if descoped, the dead input is removed (no non-functional search box ships).
- **AC-7:** Working hours saved in settings persist to the backend, reload on revisit, and are the single source for the hours shown in the sidebar (no divergent hardcoded set).
- **AC-8:** `<html lang="fr">`; the sidebar, setup/join wizards, and AI chat contain no English UI strings; AI voice recognition uses `fr-FR`.
- **AC-9:** No native `alert()`/`confirm()` remains for user-facing feedback; those paths use French toasts / `AlertDialog`.
- **AC-10:** `notifications-list.tsx`, `dental-chart.tsx`, the duplicate "Patient Summary" button, `PatientSummaryService`, `AISummaryJob`, and `GoogleAIService` are removed; the email `NotificationService` is removed if it has no consumers; the app builds with 0 warnings; root `CLAUDE.md` reflects current reality.

## API Contract
### Changed — reminder settings (`GET/PUT /api/clinics/reminder-settings`)
Request/response gain per-clinic `smsApiUrl`, `whatsAppApiUrl`, `leadTimeHours: int[]`, `messageTemplateBody: string` (URLs and body non-secret; secrets stay write-only as today). GET adds a per-channel `effectiveStatus: "configured" | "not_configured"`.

### New — reminder delivery status
`GET /api/clinics/reminder-status?take=N` → `[{ id, channel, recipientMasked, status, failureReason?, scheduledAt, sentAt? }]` (admin-only, clinic-scoped).

### Changed — clinic settings (working hours)
Clinic update request/response gain a `workingHours` structure (per-day open/close/closed). Existing clinic settings endpoint, extended.

### Changed — create appointment (inline patient)
The inline new-patient payload gains `phone: string` (validated E.164/TN).

## Data / Schema Changes
- **`ClinicReminderSettings`** gains `SmsApiUrl`, `WhatsAppApiUrl` (nullable strings) and `LeadTimeHours` / `MessageTemplateBody` (nullable) — per-clinic overrides of the previously per-install-only values; resolver prefers per-clinic ?? per-install. Migration.
- **`Clinic.WorkingHours`** — persisted working-hours structure (JSON column or a small owned type). Migration.
- Reminder-status endpoint is a read over existing `Notification` rows (masking the recipient) — no schema change.
- Removals (dead code) carry no schema change except dropping the unused email-notification code path.

## Out of Scope
- Delivery-status **webhooks** from providers (status is derived from the outbox row state only), two-way messaging, and per-patient opt-out (candidate follow-up).
- Full app-wide i18n framework / language switcher — this is a targeted French-string cleanup of the named surfaces, not a translation infrastructure.
- Reworking Google Calendar sync direction/labeling (tracked separately in `fix-appointment-google-sync`).
- Provider onboarding (WhatsApp template approval, SMS sender-ID registration).
- Redesigning the reminder scheduler beyond exposing lead-time (still one reminder per computed tier).

## Edge Cases (Critical only)
- **Backward compatibility:** a clinic that set only credentials before (URL from install config) keeps working — resolver falls back to per-install URL when the per-clinic URL is blank.
- **"Connecté" downgrade:** an existing clinic showing green that is actually `NotConfigured` after this change now shows the warning — that's the intended correction, not a regression.
- **Phone validation tightening:** existing patients with non-conforming stored numbers are not retro-invalidated on read; the rule applies on create/edit save (document that a legacy bad number surfaces its error the next time that patient is edited).
- **`NotificationService` removal guard:** remove only after a repo-wide check confirms no injection/callsite; if any consumer exists, leave it and note it (do not break a live path).
- **Working-hours load for existing clinics:** clinics with no saved hours fall back to the current sensible default rather than showing empty fields.

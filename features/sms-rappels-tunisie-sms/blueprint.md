# Blueprint — Rappels SMS via Tunisie SMS

> Status: **design approved 2026-10-20**, not started. Produced by `/think-solution`.
>
> **Blocked before code (not code):** Tunisie SMS API doc (request shape · phone format · where the key goes) ·
> sender `APEXA` approved by the operators · their answer on whether a cabinet-edited text passes the declared
> « message type ».

## Decisions taken with the owner

| # | Decision |
|---|---|
| 1 | **Opt-in per cabinet.** SMS is off until the cabinet turns it on in Paramètres. One vendor account (Tunisie SMS, sender `APEXA`) for every cabinet |
| 2 | **Patient consent is ON by default** — already true: `ReminderConsent.NotRecorded` receives. Only `Refused` blocks |
| 3 | **Tunisian mobiles only** (+216, libphonenumber type `MOBILE`). WhatsApp keeps every country |
| 4 | **The doctor picks the timing**: up to 2 from 48 h · 24 h · 12 h · 6 h · 3 h · 2 h. A new opt-in starts on 24 h only |
| 5 | **`{acte}` variable**, **not in the default text** (the SMS shows on a lock screen); one click adds it |
| 6 | **The doctor can rewrite the text** with variables `{patient}` `{date}` `{acte}` `{clinic}`; `{date}` is required. **Free text anywhere** (e.g. « Merci de venir 10 min en avance », « Venez à jeun ») and **line breaks** allowed — each line break counts as 1 of the 157 |
| 7 | **An SMS never exceeds 157 characters** (one SMS at Tunisie SMS). Accents outside GSM-7 are replaced (ê→e, ç→c, ô→o) so one accent does not drop the limit to 70 |
| 8 | **The text is written at SEND time**, from the live appointment — always the current date, acte and wording |
| 9 | **1 SMS = 1 message of the cabinet's monthly forfait**, the same forfait as WhatsApp (reverses AC-4.6 « SMS is never counted ») |
| 10 | **Hosted only.** The vendor's key cannot sit on a clinic PC; `SelfHostedLan` keeps « own gateway » fields and stays off by default |

---

## Summary

The reminder row stops carrying a frozen sentence. At enqueue it records **when** to send and **which moment it was
for** (`Notification.AppointmentMomentUtc`); at dispatch the job renders the text from the live appointment, patient,
clinic name and the cabinet's current template through **one renderer**, `ReminderText`, which owns the variables,
the GSM-7 normalisation and the 157-character budget. The rendered text is written back onto the row before the send,
so the row records exactly what left. A dedicated `TunisieSmsSender` replaces the generic `HttpSmsSender`, reads the
provider's status code **inside a 200**, and trips a short breaker on « no credit / sender not approved » instead of
hammering. SMS joins the WhatsApp forfait gate and counter. The cabinet sees one « Rappels SMS » card in
`/settings` (switch · timing chips · editor with live server preview and « 112 / 157 »), and `/rappels` is un-parked
for the delivery log.

---

## Files to create

| File | Purpose |
|---|---|
| `api/ClinicManagement.Application/Features/Messaging/ReminderText.cs` | **The one renderer**: variables, GSM-7 normalisation, 157 budget, shrink order. Owns the date format (moved from `ReminderMessage.FormatAppointmentMoment`, which now delegates) |
| `api/ClinicManagement.Application/Features/Messaging/ReminderTemplateRules.cs` | Save-time validation of a template + lead times (known variables, `{date}` required, worst-case fits) — same budget maths as `ReminderText` |
| `api/ClinicManagement.Application/Features/Clinics/Queries/PreviewReminderTextQuery.cs` | `POST /clinics/reminder-settings/preview` → `{ text, length, max, fits }` with a sample patient |
| `api/ClinicManagement.Infrastructure/Services/TunisieSmsSender.cs` | Replaces `HttpSmsSender`: provider request shape + status-code classification |
| `api/ClinicManagement.Infrastructure/Services/SmsProviderBreaker.cs` | Singleton: « stop calling the provider until T » after 401/402/420/442 |
| `api/ClinicManagement.Infrastructure/Migrations/<ts>_AddNotificationAppointmentMoment.cs` | One nullable column, **no backfill** |
| `web/components/reminder-sms-card.tsx` | The « Rappels SMS » card in `/settings` |
| `api/ClinicManagement.UnitTests/...` | See § Tests |

## Files to modify

### Domain
- `ValueObjects/PhoneNumber.cs` — add `IsTunisianMobile` (libphonenumber `GetRegionCodeForNumber == "TN"` and
  `GetNumberType == MOBILE`). **One owner of the rule** (`phone-rule-has-one-owner`); no regex anywhere else.
- `Entities/Patient.cs` — add `SmsNumberE164()`: the primary if Tunisian mobile, else the first
  `AdditionalPhoneNumbers` entry that is, else null.
- `Entities/Appointment.cs` — add `ActsLabel()`: `Procedures` by `SequenceNumber`, `ProcedureName`, distinct,
  joined « + » (server twin of `web/components/appointment-labels.ts` `appointmentActsSummary`).
- `Entities/Notification.cs` — add `DateTime? AppointmentMomentUtc` (set by the constructor for appointment rows) and
  `RecordRenderedMessage(string text)`.
- `Entities/ClinicReminderSettings.cs` — `LeadTimeHours` parse stays; validation lives in `ReminderTemplateRules`.

### Application
- `Features/Clinics/Commands/UpdateClinicReminderSettingsCommand.cs`
  - when `SellsVendorMessaging`: refuse `SmsApiUrl` / `SmsSenderId` / `SmsApiKey` with a new
    `MessagingRefusals.ManualSms` + `ManualSmsCode` (twin of `ManualWhatsApp`, :85-103).
  - validate through `ReminderTemplateRules`; codes `reminder_template_unknown_variable`,
    `reminder_template_needs_date`, `reminder_template_too_long`, `reminder_lead_time_invalid`. French sentences.
- `Features/Messaging/OutboxMessagingGate.cs` — gate **every vendor-sold channel** (drop `channel != WhatsApp`, :86);
  the template-approval check stays **WhatsApp-only**; cache key becomes **(clinic, channel)** (see Pitfalls).
- `Features/Messaging/MessagingRefusals.cs` — `ManualSms`.
- `DTOs/ReminderSettingsDto.cs` — add `SmsVendorManaged`, `SmsMaxLength` (157), `AllowedLeadTimeHours`.
- `Common/Interfaces/IReminderScheduler.cs` — unchanged signature (rendering left the scheduler).

### Infrastructure
- `Services/ReminderScheduler.cs`
  - `ReachabilityOfAsync` becomes **per channel**: SMS needs `patient.SmsNumberE164()`, WhatsApp keeps `E164`.
    Consent still asked first, in this one method (`ReminderConsentCoverageTests`).
  - store `AppointmentMomentUtc`; `Message` gets a **preview** rendered by `ReminderText` (overwritten at send).
  - delete `BuildMessage`; `BuildRecallMessage` output goes through `ReminderText.Normalise` + cap (one rule for
    every SMS, even on the UI-less recall path).
  - recall exhaustion check (:142-151): « every sendable channel is metered and exhausted », not « WhatsApp only ».
- `Services/ReminderSettingsProvider.cs` — when `SellsVendorMessaging`, `ClaimsItsOwnSms` is **false** whatever the
  row holds (a stale clinic SMS field must never detach a cabinet from the vendor account).
- `Services/HttpReminderChannelSender.cs` — split `PostJsonAsync` into a request builder + shared
  `SendAndClassifyAsync(HttpRequestMessage)`; add `protected virtual ReminderSendResult ClassifySuccess(string body)`
  (default `Sent`). Body still **never** reaches the result (D-8).
- `Services/ReminderMessage.cs` — `AnnouncesStaleMoment` kept for **legacy rows only** (null `AppointmentMomentUtc`).
- `Services/RemindersConfig.cs` — `SmsMaxLength` (default 157); fix the stale « largest tier wins » comment (:103).
- `Extensions.cs:418` — register `TunisieSmsSender` **instead of** `HttpSmsSender` (`NotificationJob` builds
  `ToDictionary(s => s.Channel)` — two SMS senders throw at construction); `SmsProviderBreaker` as singleton.
- `Repositories/NotificationRepository.cs:231-246` — forfait-held counts cover SMS rows.

### API
- `BackgroundJobs/NotificationJob.cs` (per-row order, :342-475)
  - after the patient load: **consent re-check** → Failed « Le patient refuse les rappels » (closes the gap where a
    refusal saved after booking still let queued rows go).
  - phone: SMS uses `patient.SmsNumberE164()` (null → Failed « Pas de mobile tunisien »), WhatsApp `E164`.
  - moved appointment: `AppointmentMomentUtc` set and ≠ live moment → Failed (same outcome as today's stale check,
    read from a column instead of parsing the body). Null → legacy `AnnouncesStaleMoment` on the frozen body.
  - render: `ReminderText.Render(...)` from the live appointment (`GetByIdAsync` already includes `Procedures`),
    the loaded patient, the clinic name (cache per tick) and `settings.MessageTemplateBody`;
    `notification.RecordRenderedMessage(text)` **before** `sender.SendAsync`. Legacy rows send their frozen body.
  - `EnsureCountingRowAsync` (:642): count SMS too.
- `Controllers/ClinicsController.cs` — `POST reminder-settings/preview` (AdminOnly).
- `Controllers/OutboxController.cs` — `GET /api/outbox` **mirrors the dispatcher's filters clause for clause**
  (multi-tenant notes): add consent, SMS number, moved-moment.

### Web
- `web/app/settings/page.tsx` / `components/clinic-settings.tsx` — mount `ReminderSmsCard` (admin edits, others
  read-only), near `LetterheadCard`.
- `web/components/reminder-sms-card.tsx`
  - Switch « Envoyer des rappels SMS aux patients » (`smsEnabled`: `null` reads as off).
  - Timing chips, multi-select, max 2, from `allowedLeadTimeHours` (« La veille », « 2 h avant »…).
  - Editor: textarea (free text + Entrée = line break, `maxLength={157}` as a hard stop) + 4 variable chips
    inserting at the caret; phone-bubble preview (`whitespace-pre-line`, so line breaks show) + « 112 / 157 » from
    the **server preview** (debounced 300 ms) — no TS copy of the budget maths. Counter turns red and Enregistrer is
    disabled over 157.
  - Save errors through `FormErrorBanner`; 409 through `useConflict`.
  - One line « N messages restants ce mois » linking to `/rappels`.
- `web/components/reminder-settings.tsx` — hide the SMS gateway URL / sender / key when `smsVendorManaged`
  (twin of `whatsAppVendorManaged`, :525-615); fix the stale « mounted in ClinicSettings » comment (:55).
- `web/app/rappels/page.tsx` — un-park: render `RappelsScreen`. WhatsApp connect card + WhatsApp settings stay
  hidden until WhatsApp is contracted; forfait wording says « messages (SMS + WhatsApp) ».
- `web/lib/api/reminder-settings.ts` + `types.ts` — new fields + `preview()`.

### Deploy / docs
- `deploy/docker-compose.hosted.yml`, `.env.hosted.example` — `Reminders__Sms__ApiUrl`, `Reminders__Sms__SenderId=APEXA`,
  `Reminders__Sms__ApiKey` (secret). `Reminders__Channels` **stays empty** — that is what makes it opt-in.
- `deploy/README.md:633`, `.env.hosted.example:286` — « SMS is never counted » becomes « SMS counts 1 ».
- `features/vendor-whatsapp-messaging-quota/notes.md` — record the AC-4.6 reversal.
- `web/components/CLAUDE.md`, `web/CLAUDE.md:150` — `/rappels` no longer parked; card location.
- `features/sms-rappels-tunisie-sms/notes.md` when shipped.

---

## Key signatures

```csharp
// Application/Features/Messaging/ReminderText.cs
public static class ReminderText
{
    public const string DefaultTemplate = "Rappel : {patient}, vous avez un rendez-vous le {date} chez {clinic}.";
    public static readonly IReadOnlyList<string> Variables = ["{patient}", "{date}", "{acte}", "{clinic}"];

    public static string FormatMoment(DateTime appointmentUtc);          // "21/10/2026 à 14:30", clinic-local
    public static string Normalise(string text);                         // GSM-7 basic set only
    public static RenderedReminder Render(ReminderTextInput input, int maxLength);
}

public sealed record ReminderTextInput(
    string? Template, string FirstName, string LastName, DateTime AppointmentUtc,
    string? ActsLabel, string ClinicName);

public sealed record RenderedReminder(string Text, int Length, bool Fits);

// Domain
public bool IsTunisianMobile { get; }                 // PhoneNumber
public string? SmsNumberE164();                        // Patient
public string ActsLabel();                             // Appointment
public DateTime? AppointmentMomentUtc { get; }         // Notification
public void RecordRenderedMessage(string text);        // Notification

// Infrastructure
public sealed class SmsProviderBreaker { bool IsOpen(DateTime nowUtc); void Trip(TimeSpan for_, string reason); }
public class TunisieSmsSender : HttpReminderChannelSender, IReminderChannelSender
{
    protected override ReminderSendResult ClassifySuccess(string body);   // reads <status_code>
}
```

### `ReminderText.Render` — budget and shrink order
1. `Normalise` the template and every value (ê→e, ç→c, ô→o, œ→oe, ’→', « »→", –/—→-, …→...; drop GSM-7
   **extension** characters `€ [ ] { } ~ ^ | \`, which count double; drop anything unmappable).
   **Line breaks**: CRLF / CR → LF (one GSM-7 character, counts 1); at most 2 consecutive; trimmed at both ends.
   Tabs → space. Values (name, acte, clinic) never carry a line break.
2. Fixed part = template without variables (**the doctor's free text included**) + `{date}` (18 chars) +
   `{clinic}` (full). Room = 157 − fixed.
3. Fill `{acte}`: full label → first act + « +N » → truncated with « . ».
4. Fill `{patient}`: « Prénom Nom » → « Prénom N. » → « Prénom » → truncated.
5. Last resort: truncate the clinic name. **Never cut `{date}` and never cut the doctor's free text** — save-time
   validation already guaranteed it fits. Collapse « , , » / double spaces left by an empty value (**spaces only —
   never collapse the line breaks**). Assert `Length <= 157`.
6. **Per channel**: SMS keeps the line breaks; **WhatsApp flattens them** to « · » — Meta refuses a template body
   parameter containing a newline (error 132018).

### Save-time worst case (`ReminderTemplateRules`)
Worst case = fixed part (free text + line breaks) + date 18 + clinic name + 10 reserved for `{patient}` + 12
reserved for `{acte}` when present. Over 157 → refused with `reminder_template_too_long` and the sentence
« Le message dépasse 157 caractères (N). Raccourcissez le texte. » The free text can therefore use everything the
variables do not need, and nothing more.

### Tunisie SMS status codes
| Code | Result |
|---|---|
| 200 | `Sent` |
| 401 · 402 · 442 | `Throttled` + breaker 15 min + **Error** log (vendor problem: key, credit, sender) |
| 420 | `Throttled` + breaker 1 h (daily quota) |
| 441 | `RecipientUnreachable` |
| 400 · 430 · 431 · 440 | immediate Failed (our bug; 440 cannot happen under the 157 cap) |
| other / no code | `Transient` |

`Throttled` = stays Pending, spends no retry, no save — rows wait for the credit and are drained by the existing
« appointment already started → Failed » rule, so nothing hammers and nothing piles up for ever.

---

## Potential pitfalls

- **Unloaded collections are empty, not stale.** `ActsLabel()` needs `Procedures`; `SmsNumberE164()` needs
  `AdditionalPhoneNumbers` (owned, loads with the patient). Assert both in a test using the real repository shape.
- **Gate cache per clinic only would hold SMS rows behind a WhatsApp template under review.** Key the decision on
  (clinic, channel); keep `MessagingTemplateNotReady` WhatsApp-only.
- **Two SMS senders throw at job construction** (`ToDictionary`). Replace, don't add.
- **If Tunisie SMS takes the key in the query string**, `IHttpClientFactory` logs the full URL at Information →
  the key lands in the logs. Use a named client with request logging off, or a redacting handler.
- **A browser textarea sends CRLF on some platforms** — normalise before counting, or one line break counts 2 and
  the preview disagrees with what is billed.
- **How Tunisie SMS wants a line break** (raw LF in JSON, `%0A` in a query string, or not at all) — confirm from
  their doc; a wrong encoding prints a literal « \n » to the patient.
- **WhatsApp refuses a newline in a template parameter** — the per-channel flatten in `Render` step 6 is mandatory.
- **Legacy rows** (null `AppointmentMomentUtc`) keep the frozen body + `AnnouncesStaleMoment`. Do not backfill.
- **Migration**: check the scaffold for the `AddColumn<uint>("xmin")` trap; run `verify-schema` before/after.
- **`GET /api/outbox` mirrors the dispatcher** — every new dispatch filter goes there too.
- **Existing clinic rows with SMS fields set** would have owned the channel. Read-only SQL before deploy; the provider
  now ignores them when vendor-managed.
- **Consent derived guard** (`ReminderConsentCoverageTests`) expects one patient load in the scheduler; the dispatcher
  check is a second consumer — extend the guard, don't bypass `Patient.AcceptsReminders`.
- **Log hygiene** (`LogTemplateCoverageTests`): never the patient name; phone via `ReminderPhone.Mask`.
- **New endpoint** must satisfy `ControllerAuthorizationCoverageTests` and `SubscriptionExemptionCoverageTests`.
- **Enums on the wire as strings** (`ReminderConsentWireShapeTests`) if any is added to a DTO.
- **Two act labels exist (C# + TS).** They may differ on purpose (the SMS one shrinks); say so at both definitions.
- **Prod**: env vars + deploy need the owner's explicit OK (`never-touch-prod-without-consent`).

---

## Tests

**Unit (api)**
- `ReminderTextTests` — default fits; 40-char name shrinks in order; 3 acts → « +2 »; accents normalised; extension
  chars dropped; `{date}` never cut; free text never cut; empty name leaves no « , , »; CRLF counts 1; 3 blank
  lines become 2; WhatsApp output has no newline; property test: random inputs never exceed 157.
- `ReminderTemplateRulesTests` — unknown variable, missing `{date}`, free text + line breaks pushing the worst case
  over 157, lead times outside the set or more than 2 → each refused with its code; a template exactly at 157 accepted.
- `PhoneNumber` corpus — +21698…/+21655…/+21629… mobile; +21671… fixed refused; +33… refused; legacy rows.
- `Patient.SmsNumberE164` — primary landline + extra mobile → the mobile.
- `TunisieSmsSenderTests` — every row of the code table, incl. **HTTP 200 carrying 402**; body never in the result;
  breaker open → no HTTP call.
- `ReminderSchedulerTests` — SMS skipped for landline / foreign, WhatsApp still queued; `AppointmentMomentUtc` set.
- `NotificationJobTests` — refusal after enqueue → not sent; moved (column) → Failed; legacy null → body check;
  text rendered from the **current** acts and template; rendered text recorded on the row.
- `NotificationJobMessagingTests` / `OutboxMessagingGateTests` — SMS counts 1 unit; exhausted forfait holds an SMS row;
  WhatsApp template under review does **not** hold the SMS row. (Two existing assertions flip on purpose: :277, :101.)
- `UpdateClinicReminderSettings…Tests` — `ManualSms` on hosted; accepted on `SelfHostedLan`.

**Browser** (`/test-in-browser`)
- `/settings` card at 320 / 390 / 820 / 1440: switch, 2-chip limit, chip inserts at caret, counter turns red over 157,
  save → reload keeps everything; 44 px targets on coarse pointer.
- `/rappels` un-parked: log + forfait line render; WhatsApp blocks hidden.

**Gate**: full `dotnet test -c Release` · `check:responsive` · `tsc --noEmit` · `npm run build` ·
`verify-schema` around the migration.

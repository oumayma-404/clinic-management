# Rappels SMS via Tunisie SMS — what shipped

Blueprint: [`blueprint.md`](blueprint.md) · impact list: [`blast-radius.md`](blast-radius.md).

## A cabinet opts in, and the vendor's account sends

- **Off until a cabinet turns it on.** `Reminders__Channels` stays empty on the hosted deployment, so
  `ClinicReminderSettings.SmsEnabled = null` reads as off; the « Rappels SMS » card in « Paramètres » writes `true`.
- **One Tunisie SMS account for every cabinet** (`Reminders__Sms__*`, `deploy/.env.hosted.example`).
  ⚠️ Where `SellsVendorMessaging` is true a cabinet **cannot** type a gateway: `UpdateClinicReminderSettingsCommand`
  refuses `SmsApiUrl` / `SmsSenderId` / `SmsApiKey` with `messaging_sms_is_vendor_managed`, and
  `ReminderSettingsProvider` ignores any such field already stored — a stale clinic field must never detach a cabinet
  from the vendor account. `SelfHostedLan` keeps its « own gateway » fields.

## The text is written at SEND time

- `ReminderText` (Application/Features/Messaging) is **the one renderer**: variables `{patient}` `{date}` `{acte}`
  `{clinic}`, the date format, the GSM-7 alphabet, the 157-character budget. The dispatcher, the scheduler's preview,
  the editor's preview endpoint and the save-time rules all use it.
- `NotificationJob` renders from the **live** appointment (its acts via `Appointment.BookedActNames()`), the patient,
  the clinic's name and the cabinet's **current** wording, then `RecordRenderedMessage` so the row says what was sent.
- ⚠️ **« moved » is a column now, not a parse of the body**: `Notification.AppointmentMomentUtc` records the moment a
  row was queued for, and a row whose appointment has since moved is failed as before. Rows queued before the column
  (null) keep their frozen body and `ReminderMessage.AnnouncesStaleMoment`. No backfill.
- ⚠️ **The date was not substituted in the first cut** — `Shape.Of` replaced `{date}` with a placeholder to *measure*
  and composed from the un-dated template. Caught by `ReminderTextTests`; the shape now stores the dated template.

## One SMS, never two

- **157 characters** (`ReminderText.SmsMaxLength`, the owner's figure for one SMS at Tunisie SMS).
- Characters outside GSM-7 are mapped (ê→e, ç→c, ô→o, « »→", …); the GSM-7 *extension* characters (`[ ] { } ~ ^ \ |
  €`), which cost two, are mapped too; anything unmappable (Arabic script, emoji) is dropped. One stray accent would
  otherwise switch the whole message to UCS-2 and a 70-character limit.
- Line breaks are allowed and count one; CRLF → LF; at most one empty line.
- **Save-time** (`ReminderTemplateRules`): known variables only, `{date}` required, and the worst case — the doctor's
  text + date + clinic name + 10 reserved for the name + 12 for the acts when `{acte}` is used — must fit.
- **Send-time** shrink order: the acts (« A + B » → « A +1 » → cut), then the name (« Sami Ben Ali » → « Sami B. » →
  « Sami » → cut), then the clinic name (only after a rename). The date and the doctor's own text are never cut. A
  template saved before the rule is hard-cut as the last resort — never more than one SMS.
- `TunisieSmsSender` refuses anything over 157 as a last line before money is spent.
- WhatsApp gets the same text with its lines joined by « · » — Meta refuses a newline in a template parameter.

## Who receives it

- **Tunisian mobiles only** (`PhoneNumber.IsTunisianMobile`, libphonenumber's own numbering plan). `Patient.SmsNumberE164`
  takes the primary if it is one, else the first extra number that is. WhatsApp keeps any country.
- **Consent is asked again at send time** — a refusal saved after the row was queued used to let it go out.

## Tunisie SMS

- ⚠️ **It answers 200 and states its verdict in the body** (`<status_code>`). The generic sender trusted the status,
  so « crédit insuffisant » would have been recorded « Envoyé ». `HttpReminderChannelSender.ClassifySuccess` is the new
  hook; `TunisieSmsSender` maps 400/401/402/442 → pause 15 min (`SmsProviderBreaker`) and Throttled, 420 → pause 1 h,
  441 → recipient unreachable, 430/431/440 → failed, other → transient. Throttled rows stay Pending and spend nothing.
- ⚠️ **The request shape is provisional** (`{ sender, to: "216…", message }` + bearer) until the provider's API
  document is in hand; only `TunisieSmsSender.BuildRequest` should have to change. If the key goes in a query string,
  turn off request-URL logging for that client or the key lands in the logs. Ask them how a line break is encoded.
- It **replaced** `HttpSmsSender`: `NotificationJob` keys senders by channel, so two SMS senders throw.

## Money: one SMS = one message of the forfait

- Reverses `vendor-whatsapp-messaging-quota` AC-4.6. `OutboxMessagingGate.IsMetered` covers SMS and WhatsApp; the
  template term stays WhatsApp-only and the gate's cache is keyed **(clinic, channel)** so a WhatsApp template under
  review never holds the same cabinet's SMS rows. The counter (`EnsureCountingRowAsync`) counts SMS.
- The recall path refuses a « Relancer » whenever every sendable channel is metered and the forfait is spent.
- Screen wording: « Forfait de rappels », « Chaque SMS et chaque message WhatsApp envoyé compte pour un rappel. »

## Screens

- **`/settings` → « Rappels SMS »** (`reminder-sms-card.tsx`, admins): the switch, up to 2 send times from 48/24/12/6/3/2 h,
  the message with variable buttons, a live server preview and « 112 / 157 caractères ».
- **`/rappels` is no longer parked**; its header links « Message et horaires » to the card.
- The channels sheet (`reminder-settings.tsx`) no longer edits SMS on/off, the times or the wording, and has no SMS
  block at all where the vendor provides the gateway. ⚠️ It still **sends** those fields, from the settings as read
  (`updateRequestFrom`), because the PUT replaces every field — the card does the mirror image.

## Not done here

- The `TunisieSmsSender` request shape (waiting on their API document).
- The hosted env values themselves (the owner's OK is needed to touch production).

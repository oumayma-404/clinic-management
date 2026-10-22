# QA plan — sms-rappels-tunisie-sms

Stack: branch API `http://localhost:5099` + branch web `http://localhost:3099` (isolated; peer stack on 5000/3000
untouched), shared dev database. Account: Salma Ben Youssef (admin), clinic `f64a8a75-…` (its name is **blank** in
the dev data — landing captures).

Baseline before the run (SQL): `SmsEnabled` null · `WhatsAppEnabled` true · no SMS gateway fields · `LeadTimeHours`
null · `MessageTemplateBody` null. Restored to this after the run.

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| P1 | A | api | `POST /clinics/reminder-settings/preview` with `template: null` | 200; `text` starts « Rappel : Mohamed Ben Abdallah, vous avez un rendez-vous le » and holds « à 14:30 »; `usedLength` ≤ 157; `maxLength` 157; `refusal` null |
| P2 | B | api | preview with `{nom}` | `refusalCode` = `reminder_template_unknown_variable` |
| P3 | B | api | `PUT /clinics/reminder-settings` with `smsApiUrl` set (hosted) | 400, `code` = `messaging_sms_is_vendor_managed` |
| P4 | B | api | `PUT` with `leadTimeHours: [24,12,6]` | 400, `code` = `reminder_lead_time_invalid` |
| A1 | A | browser | `/settings` → « Rappels SMS » card | badge « Désactivés »; expanded: switch off, « La veille » pressed, textarea = default wording, preview bubble text from server, count « N / 157 caractères » |
| A2 | A | browser+sql | switch on, add « 2 h avant », insert « + Acte » at the end, Enregistrer | toast « Rappels SMS activés »; badge « Activés »; SQL: `SmsEnabled` t, `LeadTimeHours` `24,2`, template contains `{acte}` |
| A3 | A | browser | `/rappels` | the log screen (no « Bientôt disponible »); header link « Message et horaires » |
| A4 | A | browser | « Message et horaires » | lands on `/settings#rappels-sms` with the card **expanded** |
| A5 | A | browser | forfait card on `/rappels` | title « Forfait de rappels »; text « Chaque SMS et chaque message WhatsApp envoyé compte pour un rappel. »; no « ne concerne que les rappels WhatsApp » |
| B1 | B | browser | with 2 times selected | the 4 others are disabled |
| B2 | B | browser | append 160 « x » | count turns red (> 157), refusal sentence « dépasse 157 caractères » shown, Enregistrer disabled |
| B3 | B | browser | remove `{date}` | refusal « doit contenir {date} », Enregistrer disabled |
| B4 | B | browser | type a line break + « Merci de venir en avance. » | preview bubble shows it on its own line |
| B5 | B | browser | deselect every time | « Choisissez au moins un moment d'envoi. », Enregistrer disabled |
| B6 | B | browser+sql | « Message par défaut », Enregistrer | textarea = default wording; SQL `MessageTemplateBody` null |
| C1 | C | browser | card expanded at 320 / 390 / 820 / 1440 and 1536×730 | no horizontal page overflow; chips wrap; Enregistrer reachable (screenshots read) |
| C2 | C | browser | save, reload, save again without changes | second save succeeds (no 409 banner) |
| D1 | D | browser+sql | `/rappels` « Configurer les canaux » sheet | no SMS block, no « Programmation & message »; « Enregistrer » there keeps `SmsEnabled`/`LeadTimeHours`/`MessageTemplateBody` as the card saved them (SQL) |
| D2 | D | browser | `/rappels` counters + log | the four counters and the log table/cards render (blast row 17/19) |
| D3 | D | browser | `/settings` other cards | « En-tête des documents » card still renders (blast row 20) |
| X1 | A | — | an SMS actually rendered at send time | ⏭ needs an appointment created on the shared DB (clinical write); covered by `NotificationJobRenderAtSendTests` |

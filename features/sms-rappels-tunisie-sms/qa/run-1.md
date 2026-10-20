# QA run 1 — sms-rappels-tunisie-sms

**Environment:** branch API `:5099` (`bin/Debug`, started 2026-10-20 16:48, restarted 17:1x with `FrontendUrl=:3097`) ·
branch web `next dev :3097` (fresh `.next`) · shared Postgres (migration `AddNotificationAppointmentMoment` applied) ·
own headless Chrome (playwright-core). Peer stack on 5000/3000 untouched. Peers live: clinic-management-b5 / -11 / -51
(idle), anakin-c2 (busy, other repo), anakin-79 (idle); `:3099` is held by a staff-chat worktree server from
2026-10-15 — **the first drive hit it by mistake** (my `next dev` had failed with EADDRINUSE), so every row of that
drive is void; the environment was fixed and the walk re-driven. Two further probe fixes (strict-mode selectors on
« Canaux de rappel » and « Configurer les canaux »), re-driven in the same run. Settings reset to baseline by SQL
before each re-drive (my own artefacts).

**Widths looked at:** 320×740 · 390×844 · 820×1180 · 1440×900 · 1536×730 (screenshots read: 320, 1536×730, the
/rappels page and the channels sheet at 1440).

| ID | Tier | Result | Evidence |
|---|---|---|---|
| P1 | A | ✅ | `{"text":"Rappel : Mohamed Ben Abdallah, vous avez un rendez-vous le 21/10/2026 à 14:30 chez .","usedLength":74,"maxLength":157,"refusal":null}` |
| P2 | B | ✅ | `refusalCode: reminder_template_unknown_variable` |
| P3 | B | ✅ | 400 `messaging_sms_is_vendor_managed` |
| P4 | B | ✅ | 400 `reminder_lead_time_invalid` |
| A1 | A | ✅ | « Désactivés », switch off, « La veille » pressed, default wording, preview + « 74 / 157 caractères » |
| A2 | A | ✅ | toast « Rappels SMS activés », badge « Activés », SQL `true · 24,2 · {acte} + line break` |
| A3 | A | ✅ | log screen, « Message et horaires » link (`shots/A3-rappels-1440.png`) |
| A4 | A | ✅ | `/settings#rappels-sms`, card expanded |
| A5 | A | ✅ | « Forfait de rappels » + « Chaque SMS et chaque message WhatsApp envoyé compte pour un rappel. » |
| B1 | B | ✅ | the 4 other chips disabled at 2 |
| B2 | B | ✅ | « 179 / 157 caractères » red, refusal shown, Enregistrer disabled |
| B3 | B | ✅ | missing `{date}` refused, Enregistrer disabled |
| B4 | B | ✅ | preview shows the second line |
| B5 | B | ✅ | « Choisissez au moins un moment d'envoi. », Enregistrer disabled |
| B6 | B | ✅ | default wording restored; SQL template `null` |
| C1 | C | ✅ | no horizontal overflow and Enregistrer on screen at all five sizes |
| C2 | C | ✅ | reopened with everything; second save OK, no 409 |
| D1 | D | ✅ | sheet: no SMS block, no « Programmation » (`shots/D1-sheet-1440.png`); sheet save left SQL unchanged |
| D2 | D | ✅ | four counters render |
| D3 | D | ✅ | « En-tête des documents » still on /settings |
| X1 | A | ⏭ | needs an appointment on the shared DB (clinical write) — covered by `NotificationJobRenderAtSendTests` |

## Findings

### F1 — major — `/rappels` says « Ni SMS ni WhatsApp n'est configuré » right after SMS was switched on
- **Repro:** switch « Rappels SMS » on in /settings and save → open /rappels (no reminder sent yet).
- **Expected:** the empty log does not claim nothing is configured, and its action leads to where SMS is switched on.
- **Actual:** « Les rappels ne sont pas encore activés — Ni SMS ni WhatsApp n'est configuré, donc aucun message ne
  part. Renseignez un canal… » with « Configurer les canaux », which opens the sheet that no longer holds SMS
  (`shots/A3-rappels-1440.png`, taken with SMS on).
- **Owner:** `web/app/rappels/rappels-screen.tsx:234` (`looksUnconfigured` infers « unconfigured » from an empty log),
  `web/components/rappels/reminder-log-table.tsx:115`. It was a harmless guess while nothing could send; it is the
  first screen a cabinet sees after opting in now.

### F2 — minor — « + Acte » glues the variable to the previous word
- **Repro:** caret at the end of « …en avance. », press « + Acte ».
- **Actual:** « Merci de venir en avance.{acte} » → preview « …en avance.Détartrage + Obturation composite »
  (`shots/C1-card-1536x730.png`).
- **Expected:** a space is added when the character before the caret is not a space or a line break.
- **Owner:** `web/components/reminder-sms-card.tsx` `insertVariable`.

## Observations (off-plan)
- The dev clinic's name is blank (landing captures), so the preview reads « chez . ». Production clinics always have a
  name, but the renderer could fall back to « votre cabinet » as the old scheduler did for a missing clinic.

**Status: RED (2 findings — 1 major, 1 minor)**

## Fixes applied (cycle 1)

| Finding | Verdict | Root cause | Files | Blast radius |
|---------|---------|------------|-------|--------------|
| F1 | confirmed | the page inferred « no channel » from an empty log; the empty state's action opened the sheet that no longer holds SMS | `GetClinicReminderLogQuery.cs`, `ReminderLogDto.cs` (+ `SendingChannels`), `ResolvedReminderSettings.CanSend`, `rappels-screen.tsx`, `reminder-log-table.tsx`, `lib/api/reminder-settings.ts` | `ReminderLogDto` built in one place (no test constructs it); `CanSend` is now the one predicate the scheduler and the dispatcher delegate to (their private `IsSendable` copies call it) |
| F2 | confirmed | `insertVariable` spliced the token with no separator | `reminder-sms-card.tsx` | one function, one caller |
| Obs. blank clinic name | observation | dev data only | — | not fixed (off-plan); renderer could fall back to « votre cabinet » |

**Guards added:** `ReminderLogSendingChannelsTests` (enabled+configured / configured only / enabled only).
**Gates after the last edit:** check:responsive 77 ✅ · tsc ✅ · build ✅ · dotnet test 5 071 ✅
**Re-run:** run-2.md — GREEN

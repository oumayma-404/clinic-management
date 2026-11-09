# Feature Specification: Nouveautés par e-mail depuis la console

**Status:** APPROVED
**Type:** Small
**Created:** 2026-11-06
**Scope:** Full (API + console)
**Feature:** From the vendor console, type the app's news as a bullet list and e-mail it to the admin of every cabinet in trial or active.

## Overview
The vendor has no way to tell practices what changed in Apexa. A new « Nouveautés » screen in the console lets
her write a subject and a few bullet points (each with an emoji), see the e-mail exactly as it will arrive, send
herself a test, then send it. It goes only to **admin accounts** of cabinets that are **en essai or actif** — never
to expired, suspended or entitlement-less cabinets, never to doctors or secretaries.

## What Changes
- Console header gets a third link, « Nouveautés », beside « Journal des accès ».
- « Nouveautés » screen: subject (prefilled « Les nouveautés Apexa ✨ »), a list of news rows (emoji picker + one
  line of text, add / remove / reorder), a live preview of the e-mail, « M'envoyer un test », « Envoyer ».
- « Envoyer » opens a confirm stating the audience: « N cabinets · M adresses · K cabinets sans adresse admin ».
- Sending runs in the background; one failing address never stops the others.
- Below the form: history of past sends — date, subject, who sent it, envoyés / échecs, and which cabinets failed.
- E-mail layout (shared by every transactional e-mail) gains an optional emoji bullet list section.

## The e-mail (fixed frame, her content in the middle)
| Part | Text |
|---|---|
| Subject | what she typed |
| Opening line | « Happy Apexing ! 🦷✨ » |
| Greeting | « Bonjour Docteur <Nom>, » (existing greeting rule) |
| Intro | « Voici les nouveautés d'Apexa : » |
| List | her rows, one emoji + one line each |
| Button | « Ouvrir Apexa » → the app's public URL |
| Closing | « Apexa, toujours à vos côtés 💙 » then « L'équipe Apexa » |
| Small print | « Vous recevez cet e-mail en tant qu'administrateur du cabinet <Cabinet>. » |

## Acceptance Criteria
- **AC-1:** Recipients = users with role **Admin**, **active**, with an e-mail, in cabinets whose subscription is
  covered today and not suspended (the portfolio's « Actif » filter, which includes trial). Expired, suspended and
  no-entitlement cabinets get nothing; Doctor and Secretary accounts get nothing.
- **AC-2:** A cabinet with two admins → both receive it. One address shared by two accounts → one e-mail.
- **AC-3:** The audience is fixed at the click: the confirm's counts equal what the history then reports as attempted.
- **AC-4:** Double-click or a retried request → **one** send, one history row.
- **AC-5:** « M'envoyer un test » sends the same e-mail (greeting with her name) to the signed-in console account's
  address only, and writes no history row.
- **AC-6:** The preview, the test and the real e-mail are rendered by the same code — no second copy of the layout.
- **AC-7:** Both HTML and plain-text versions carry the bullets with their emojis; text she typed is escaped (`<b>` prints as text).
- **AC-8:** SMTP not configured → refused before anything is recorded, with a French sentence saying so.
- **AC-9:** Refused with a French message: empty subject, no row with text, subject > 120 chars, a row > 300 chars,
  more than 12 rows.
- **AC-10:** Every existing transactional e-mail (signup, password reset, 2FA reset…) renders byte-identical to
  today when it sets no bullet list.
- **AC-11:** At 320 px the screen has no horizontal scroll, the preview stacks under the form, and every control
  (emoji picker, add/remove row, both send buttons) is ≥ 44 px on a coarse pointer.

## API Contract
All under `/api/platform`, console policy, console port only.
### GET /api/platform/news/audience
Response 200: `{ clinics: int, recipients: int, clinicsWithoutAdminEmail: int }`
### POST /api/platform/news/test
Request: `{ subject: string, items: [{ emoji: string, text: string }] }`
Response 200: `{ sentTo: string }` · Errors: `400 news-invalid` · `503 email-not-configured`
### POST /api/platform/news
Request: `{ requestId: guid, subject: string, items: [{ emoji, text }] }`
Response 202: `{ broadcastId, clinics, recipients }` (same `requestId` again → same 202, nothing re-sent)
Errors: `400 news-invalid` · `400 news-no-audience` · `503 email-not-configured`
### GET /api/platform/news
Response 200: `[{ id, subject, sentAt, sentBy, clinics, recipients, sent, failed, inProgress, failedClinics: [name] }]`

## Data / Schema Changes
- New platform-level record of each send: subject, rows, request id (unique), who, when, audience counts, and
  each recipient's outcome (sent / failed + reason). No clinic scope — it belongs to the vendor. One migration.

## Device Behaviour
- **Leading device:** desk (console over the SSH tunnel).
- **Narrow width (< 640):** form full width, preview below it; history table becomes cards; confirm becomes a bottom sheet.
- **Touch:** emoji picker is a tap-open grid, no hover; floor per `~/.claude/skills/DEVICE-CONTRACT.md`.

## Out of Scope
- Unsubscribe link, scheduling for later, images / attachments, rich text, Arabic version.
- Targeting one cabinet or one plan; resending to failures only.
- In-app (bell) notification; reaching self-hosted LAN installs (not in the hosted database).

## Edge Cases (Critical only)
- A cabinet expiring mid-send still gets it (audience fixed at the click — AC-3).
- An admin deactivated after the click still gets it; one created after does not.
- Zero eligible recipients → « Envoyer » disabled and the server refuses (`news-no-audience`).

---

## Part 2 — « Nouveautés » dans l'app (owner's request, 2026-11-06, design on the « Nouveautés dans l'app » canvas)

Every news the console sends is also kept on a page of the app, for the cabinet's **administrators only**.

- **Where:** a « Nouveautés » row at the foot of the rail (and of the phone menu), with the count of unread news;
  a dot on the phone bar's « Plus ». The page `/nouveautes`: a dated timeline of cards, newest first, « Nouveau » on
  the unread ones, an index of every news by month beside it on a desktop, « Nouveautés plus anciennes » past ten.
- **Which news:** every news sent and not hidden, including those sent before the cabinet joined.
- **Unread:** sent after the admin's read marker — or, with none yet, after the account was created (a new admin
  sees the whole history without a badge claiming years of news). Opening the page moves the marker to the newest
  news **the page showed** (never past the newest sent), so a news sent while the page was open stays unread, and a
  second visit writes nothing. The marker only moves forward (one upsert, `GREATEST`), so two tabs never collide.
- **A news shown again keeps the marker's verdict:** hidden then shown, it comes back without « Nouveau » if the
  admin has since read past its send date — the marker is a date, not a list of what was seen. Accepted on purpose.
- **The rail's count** is the summary's answer, replaced by the page's own as soon as the page has read the feed
  (then 0 once marked); a failed summary shows no row and is asked again by the next screen.
- **Security:** `api/news` is `AdminOnly` server-side; all three routes **404 before the mediator** where the
  deployment serves no console (a cabinet's own PC) — the browser shows no row on that 404.
- **The e-mail's « Ouvrir Apexa »** now opens `/nouveautes` (through the login when signed out).
- **Console:** « Envois précédents » lists **every** send (not the 20 latest) and gains « Dans l'app » — Visible /
  Masquée and one button, reversible, the e-mails already sent untouched. No button while a send is still going out:
  the server refuses it then (`news_in_progress`, 409), since the dispatcher holds the same row. Who hid or showed a
  news is logged with the console account's id.
- **PC de secours:** `POST api/news/read` stays open on a cloud fenced for a cut and is `[OnlineOnly]` on the PC —
  the marker is never carried.

### Acceptance criteria (Part 2)
- AC-2.1 An admin of a cabinet sees the row and the page; a secretary or a doctor sees neither (403 server-side).
- AC-2.2 The count equals the news sent after the admin's marker (else after the account's creation).
- AC-2.3 Opening the page clears the count on every surface showing it, and keeps « Nouveau » on the cards for that visit.
- AC-2.4 A news hidden in the console disappears from the page and the count; shown again, it comes back.
- AC-2.5 On a deployment with no console, no row and no request beyond the one 404.
- AC-2.6 The page works at 320 px (timeline collapses, the date moves into the card) and the index is desktop only.

### Data (Part 2)
- `PlatformNewsBroadcasts.HiddenFromAppAtUtc` (nullable, no default).
- `PlatformNewsReadMarkers (ClinicId, UserId, ReadUpToUtc)` — its own table, **not a `Users` column**: a read must not
  age the account's `xmin` or write a « Utilisateur modifié » row in the cabinet's journal (`StaffChatReadMarker`'s
  shape). Tenant-filtered, cascade with the clinic, excluded from the archive and the PC de secours.
- Migration `AddPlatformNewsInApp`, purely additive.

## Part 3 — the console lists who a send reaches (owner's request, 2026-11-09)

> « in console, can i have always a list for who the mail is gonna reach? just for me to do a sanity check »

- AC-3.1 Under the send buttons, always shown: « Destinataires · N adresses · M cabinets », then one row per address —
  the cabinet, the admin's name, the address.
- AC-3.2 The rows are **the very rows a send records** (`PlatformNewsAudiencePlan.Recipients`, same order, same
  de-duplication) — no second audience rule. `PlatformNewsCommandTests.The_Audience_List_Is_Exactly_The_Rows_A_Send_Records_In_The_Same_Order`.
- AC-3.3 An eligible cabinet with no admin address is named in one line under the list (« ne reçoit rien »).
- AC-3.4 Long names and addresses wrap at 320 px; the list scrolls on its own only from `lg:`, beside the sticky preview.
- No journal row: the portfolio already lists each cabinet's admin address unjournaled; AC-7.3 journals a single
  cabinet's detail read only. `PlatformReadShape` gains `RecipientList` / `SkippedClinics`; the row fields reuse the
  portfolio's `ClinicName` / `AdminName` / `AdminEmail`. Blast radius: `blast-radius-recipient-list.md`.

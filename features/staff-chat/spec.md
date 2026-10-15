# Feature Specification: Messagerie interne du cabinet

**Status:** APPROVED
**Challenged:** Yes (2026-10-15)
**Created:** 2026-10-15
**Feature:** A one-click intercom between the dentists and the secretariat, reachable from every screen, built for
group practices.

## Overview

In a dental practice the dentist is at the chair with gloved hands and the secretary is at the desk; today they
shout down the corridor, walk over, or text each other on WhatsApp — with patient names in a personal phone. This
feature gives every account of the cabinet a « Messages » panel that opens from any screen without leaving it, with
one-click **messages rapides** for the dozen things a practice says all day (« Patient arrivé », « Faites entrer le
suivant », « Besoin d'aide au fauteuil »), each person's most-sent ones first.

In a **cabinet de groupe**, a message can go to the whole secretariat at once, and « Je m'en occupe » tells the
others it is handled. « Signaler l'arrivée » on an appointment tells **that appointment's dentist** — not every
dentist in the practice. Alerts reach the person on another screen or with the window minimised: a sound, an in-app
notice with one-tap replies, a Windows notification and a flashing taskbar icon.

Sources: `blueprint.md` (option chosen 2026-10-15), `exploration.md`.

## User Stories

### US-1: Open the messages from anywhere
As **any member of the cabinet** (admin, dentist, secretary), I want the messages one click away on every screen, so
that I never leave the fiche or the agenda to talk to a colleague.

**Acceptance Criteria:**
- **AC-1.1:** Every signed-in screen shows a « Messages » control with the number of unread messages (`99+` above 99);
  it is marked distinctly, not by colour alone, when an unread message is urgent.
- **AC-1.2:** Pressing it opens the panel **without leaving the current screen**: the page behind stays as it was,
  including anything typed in it.
- **AC-1.3:** The panel lists the conversations — « Tout le cabinet », the secretariat threads, the colleagues —
  ordered by last message; colleagues never written to are listed below, A→Z.
- **AC-1.4:** A colleague connected right now shows « En ligne » (a dot **and** text available to assistive tech).
- **AC-1.5:** The panel closes with ✕ or Escape; focus returns to the « Messages » control.
- **AC-1.6:** An incoming message never opens the panel by itself.

### US-2: Send a message in one click
As **any member**, I want to send a quick message or a short text, optionally about a patient, so that the right
person knows in seconds.

**Acceptance Criteria:**
- **AC-2.1:** Under the conversation, the quick messages that fit the recipient are shown as buttons (to a dentist /
  to the secretariat / replies); one press sends it.
- **AC-2.2:** Each person's quick messages are ordered by how often **they** sent each one over the last 60 days;
  never-sent ones follow in the cabinet's order.
- **AC-2.3:** A free text of 1–1000 characters can be sent; line breaks are kept; blank text cannot be sent.
- **AC-2.4:** « + Patient » attaches a patient (search) and, optionally, one of their appointments of the day; the
  message shows it as a link that opens the patient file or the appointment.
- **AC-2.5:** A quick message that is about a patient (e.g. « Préparez l'encaissement ») offers the patient picker
  but can be sent without one.
- **AC-2.6:** While sending, the message shows « Envoi… »; on failure it shows « Non envoyé · Réessayer », the text is
  kept, and retrying never creates a second copy.

### US-3: Announce a patient's arrival
As a **secretary**, I want to tell the dentist a patient has arrived from the appointment itself, so that I don't
have to find the dentist or type the name.

**Acceptance Criteria:**
- **AC-3.1:** « Signaler l'arrivée » is in the agenda block's « ⋯ » menu and in the appointment dialog, for
  appointments **of today** that are not cancelled.
- **AC-3.2:** It posts « Patient arrivé » with the appointment to **that dentist** — into their secretariat thread
  when a secretary sends it, into the two-person conversation with them when anyone else does — and confirms
  « Arrivée signalée au Dr X ».
- **AC-3.3:** If that appointment was already announced (by anyone, in any conversation), the action reads
  « Signalé à 09:32 par Salma » and asks before sending again.
- **AC-3.4:** When it cannot be sent, the action stays visible and says why: no dentist on the appointment · the
  dentist has no account in the app · the dentist is no longer active · it is your own appointment.

### US-4: Be alerted wherever I am
As **any member**, I want to notice a message meant for me even on another screen or with the window minimised, so
that a patient is never left waiting.

**Acceptance Criteria:**
- **AC-4.1:** A message meant for me plays a short sound and shows a notice with the sender and the quick message
  (or « Nouveau message »), plus « Ouvrir » and my first two reply messages in my own order (by default « J'arrive »,
  « 2 min »). A « Patient arrivé » notice also shows the appointment time (« Patient arrivé · RDV 09:30 »), never
  the patient's name.
- **AC-4.2:** The notice never takes focus and never closes or covers the primary action of an open dialog; a reply
  from it is sent without opening anything.
- **AC-4.3:** In the Windows app (updated version), when its window is not in front: a Windows notification with the
  same content and a flashing taskbar icon; clicking it brings the window forward with that conversation open.
- **AC-4.4:** In a browser, when the tab is hidden and the person allowed it (asked from a button in the panel,
  never on load): a browser notification with the same content.
- **AC-4.5:** « Urgence en salle d'attente » and « Besoin d'aide au fauteuil » are urgent: a distinct sound, the
  notice stays until opened or closed, and urgent messages ring even when sound is muted.
- **AC-4.6:** Sound can be muted per person, on that device; the panel says « Son bloqué — activer » when the browser
  is preventing it.
- **AC-4.7:** No notice is shown for the conversation already open and visible. While the session lock screen is
  up, no in-app notice is drawn — the badge and the sound only; Windows and browser notifications still appear (they
  carry no patient data).
- **AC-4.8:** Messages that arrived while I was away, offline or signed out update the badge and play **at most one**
  sound; no burst of notices.

### US-5: Share the desk in a group practice
As a **secretary in a cabinet de groupe**, I want to take a dentist's request and have my colleagues see it, so that
nobody does the job twice and nobody forgets it.

**Acceptance Criteria:**
- **AC-5.1:** A dentist's message to the secretariat reaches every secretary; « Je m'en occupe » on it marks it
  « Pris par Salma » for everyone and closes the others' notices.
- **AC-5.2:** A second « Je m'en occupe » is refused with « Déjà pris par Salma ». The person who took it can release
  it (« Je ne peux plus »).
- **AC-5.3:** A secretary's reply in that thread alerts the dentist only; the other secretaries see it as unread in
  that thread, without a sound or a notice and without raising the « Messages » count.
- **AC-5.4:** Under the last message of a two-person or secretariat conversation: « Vu », « Vu par Salma, Amel ».

### US-6: Adapt the quick messages to the cabinet
As **any member**, I want to adjust the cabinet's quick messages, so that the buttons match how this practice talks.

**Acceptance Criteria:**
- **AC-6.1:** « Modifier les messages rapides » in the panel lets anyone add, rename, reorder and remove them, and
  choose who each is for (dentiste / secrétariat / réponse) and whether it asks for a patient.
- **AC-6.2:** « Patient arrivé », « Urgence en salle d'attente » and « Besoin d'aide au fauteuil » can be renamed but
  not removed, and their urgency cannot be changed.
- **AC-6.3:** « Rétablir les messages par défaut » restores the 17 defaults.
- **AC-6.4:** A message already sent keeps the text and urgency it was sent with.
- **AC-6.5:** If a colleague saved the list meanwhile, the save is refused with « Recharger », never overwritten
  silently.

## Functional Requirements

### FR-1: Conversations and who is in them
- **Deux personnes**: any two active members of the cabinet.
- **Secrétariat de [personne]**: one per member who is not a secretary; members = that person + every current
  secretary. Not shown while the cabinet has no secretary account. Always titled « Secrétariat » (never a
  secretary's name), because whoever joins the desk later reads its history; the private two-person conversation
  with a given secretary is always offered beside it.
- **Tout le cabinet**: every member; shown from three accounts up (with two it would duplicate the two-person
  conversation).
- Membership follows the **current** role and status: a secretary who changes role leaves the secretariat threads;
  a new secretary sees their history, already read.
- A two-person conversation is private to its two members; no role, admin included, can read it in the app.

### FR-2: Who is alerted
| Conversation | Sent by | Alerted (sound + notice) | Unread, silent |
|---|---|---|---|
| Deux personnes | either | the other | — |
| Secrétariat de X | X | every secretary | — |
| Secrétariat de X | a secretary | X | the other secretaries |
| Tout le cabinet | anyone | everyone else | — |
- The sender is never alerted or counted unread for their own message.
- A new account sees every message older than the account as already read (the bell's rule).
- A message is **read** when its conversation is visible in a window in front; reading on one device clears the
  badge and notices on the person's other devices.

### FR-3: Message content
- Free text: trimmed, 1–1000 characters (the counter and the server count the same way), line breaks kept, emoji
  allowed. Enter sends on a keyboard device (Shift+Enter = new line); on touch, the send button sends.
- Optional: one patient, one appointment (of that patient). Later changes show on the link: archived patient →
  « Patient archivé » (still opens the file) · cancelled appointment → « RDV annulé » (opens the agenda on its day)
  · removed record → « introuvable », not clickable. A chat link never prevents deleting or archiving a patient.
- Messages cannot be edited or deleted.
- Times read « 14:32 » (today), « Hier 14:32 », « 12/10 14:32 », 24-hour clock, Tunisian day.

### FR-4: Quick messages
- Defaults (17):

| For | Label | Asks for a patient | Urgent |
|---|---|---|---|
| dentiste | Patient arrivé | yes (appointment) | |
| dentiste | Patient en retard (10 min) | yes | |
| dentiste | Le patient a annulé | yes | |
| dentiste | Le labo au téléphone | | |
| dentiste | Appel en attente pour vous | | |
| dentiste | Urgence en salle d'attente | | ✅ |
| secrétariat | Faites entrer le patient suivant | | |
| secrétariat | Besoin d'aide au fauteuil | | ✅ |
| secrétariat | Préparez l'encaissement | yes | |
| secrétariat | RDV de contrôle dans 1 semaine | yes | |
| secrétariat | Appelez le labo | | |
| secrétariat | Apportez-moi le dossier / la radio | yes | |
| secrétariat | J'ai 10 min de retard | | |
| réponse | OK · J'arrive · 2 min · Pas maintenant | | |

- « dentiste » = a member who is the dentist of the practice's roster (an admin who is a dentist included).
- Which buttons a conversation shows: writing to a dentist (or in a dentist's secretariat thread, as a secretary) →
  « dentiste » · writing to a secretary or the secretariat → « secrétariat » · « Tout le cabinet » or anyone else →
  all · « réponse » always.
- One list per cabinet. Label 1–40 characters, unique in the list; at most 30 items; « for » required.
- Edits to the list appear in « Journal d'activité ».

### FR-5: Arrival
- Allowed on appointments of the current Tunisian day, not cancelled, whose dentist has an active account, and not
  by that dentist themselves. An appointment marked absent can still be announced — the patient came late.
- Goes to that dentist with the appointment attached (AC-3.2); counts as a « Patient arrivé » send for the ordering
  of FR-4.
- Writes nothing on the appointment itself: no status, no time. The agenda is unchanged.

### FR-6: « Je m'en occupe »
- Offered on a message sent by the owner of a secretariat thread, to the secretaries of that thread.
- First taker wins; the taker can release it; the owner sees who took it.

### FR-7: Drafts
- One draft per conversation, kept when the panel closes, the conversation changes, the device rotates or the width
  crosses a breakpoint, and after re-signing-in in the same tab; cleared once sent.

### FR-8: Data, retention and privacy
- Messages are **never deleted automatically**; they are deleted only with the cabinet itself.
- Not part of « Archive du cabinet » (the cabinet's downloadable copy); the quick-message list is. **Carried on the
  PC de secours** like the bell, so the chat keeps working during an internet cut and what was sent there returns to
  the cloud.
- Not written to « Journal d'activité » (the quick-message list is).
- Never shown in the vendor's console.
- Notices outside the panel (in-app, Windows, browser) never show a patient name or free text.
- A chat message never appears in the bell, and « Signaler l'arrivée » adds no bell row.

### FR-9: Accounts and subscription
- A deactivated colleague cannot be written to; their conversations stay readable, read-only, with their name.
  Reactivated → writable again.
- Expired subscription: reading and marking as read work; sending, arrival and « Je m'en occupe » are refused with the
  cabinet's usual subscription message.

### FR-10: Connection
- Works on the offline LAN install (no internet needed) and on the hosted install.
- After a lost connection, the panel and badge catch up on reconnect (AC-4.8 rules apply).
- A browser left idle signs out after its usual delay (30 min; 8 h on a trusted device); from then on no alerts and
  « En ligne » goes off. A received message does not count as activity.
- Closing the Windows app (not minimising it) quits it: no alerts on that PC and « En ligne » goes off.
- Phones get alerts only while the app is open.

## API Endpoints

All under `AnyClinicRole`. Errors use the app's usual body `{ "error": "<phrase française>", "code": "<code>" }`.
Another cabinet's ids answer **404**, as the bell does.

```
GET  /api/staff-chat/conversations
200: [{ key, kind: "direct"|"desk"|"clinic", title, members: [{ userId, name, isDentist, isSecretary, online, active }],
        lastMessage: Message | null, unreadCount, hasUrgentUnread, readOnly }]

GET  /api/staff-chat/colleagues
200: [{ userId, name, isDentist, isSecretary, online, active }]

GET  /api/staff-chat/messages?conversation={key}&before={cursor}&take=50
200: { items: Message[] (newest first), nextCursor: string | null }
Message = { id, conversation, sender: { userId, name }, sentAt, body: string | null,
            quickMessage: { key, label, urgent } | null,
            patient: { id, name } | null, appointment: { id, startsAt, dentistName, cancelled } | null,
            claimedBy: { userId, name, at } | null, seenBy: [{ userId, name }] }

POST /api/staff-chat/messages
{ conversation: string, body: string | null, quickMessageKey: string | null,
  patientId: uuid | null, appointmentId: uuid | null }
201: Message            (a retry carries the same Idempotency-Key header every write already sends, and the
                         server replays the first answer — never a copy; no field of its own)
400: staffchat-empty · staffchat-too-long · staffchat-unknown-quick-message
404: staffchat-conversation-not-found · staffchat-patient-not-found
409: staffchat-recipient-inactive
402: subscription

POST   /api/staff-chat/messages/{id}/claim     200: Message   409: staffchat-already-claimed { claimedBy }
                                               400: staffchat-not-claimable
DELETE /api/staff-chat/messages/{id}/claim     200: Message   403: staffchat-not-your-claim

POST /api/staff-chat/conversations/read        { conversation, upToMessageId }   204   (allowed when expired)

GET  /api/staff-chat/arrivals/today            200: [{ appointmentId, sentAt, sentBy: { userId, name } }]
                                               (what « Signalé à 09:32 par Salma » is read from)

POST /api/staff-chat/arrivals                  { appointmentId, resend: bool }
201: Message
409: staffchat-arrival-already-sent { sentAt, sentBy }   (when resend = false)
400: staffchat-no-dentist · staffchat-dentist-no-account · staffchat-dentist-inactive · staffchat-own-appointment
     · staffchat-appointment-not-today · staffchat-appointment-cancelled

GET    /api/staff-chat/quick-messages          200: { version, items: [{ key, label, for, asksForPatient, urgent,
                                                                         builtIn, order }] }   (ordered for the caller)
PUT    /api/staff-chat/quick-messages          { version, items: [...] }   200 | 400 (validation) | 409 (changed)
DELETE /api/staff-chat/quick-messages          200   (restores the defaults)

GET  /api/staff-chat/unread
200: { total, hasUrgent, recent: Message[] }   (what the badge and the notices are built from)
```

## Device & Interface Behaviour

**Leading device:** the **desk PC** (secretary, Windows app) and the **dentist's PC or chairside tablet** — both
equal. The phone is a full participant, not a viewer. Inherits `~/.claude/skills/DEVICE-CONTRACT.md` and
`.claude/rules/frontend-web.md`.

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| « Messages » control | A « Messages » tab with its badge in the bottom bar, wherever that bar shows (below 768 px — the header has no room at 320 px) | Icon with badge beside the bell from 768 px | Same |
| Panel | Full screen; header with ✕ (44 px); composer pinned above the keyboard, quick-message buttons in one scrolling row above it | ~380 px panel over the right edge, page behind unchanged, no dark overlay | Same |
| Conversation list ↔ thread | One at a time, « ← » back to the list | Same | Same |
| Quick-message editor | Full-screen sheet; « Enregistrer » visible with the keyboard open | Dialog | Dialog |
| Notice | Top of the screen, full width minus the gutter — the bottom is where a sheet's « Enregistrer » sits | Top-centre | Top-right |

- **Touch paths:** message time, « Je m'en occupe », « Vu par », « En ligne » are always visible — nothing on hover
  only. Quick-message buttons and replies are 44 px tall on a coarse pointer and wrap or scroll, never shrink.
- **200 % zoom / 380 px tall:** the panel goes full screen; the composer and its send button stay reachable.
- **Crossing a breakpoint / rotating:** the panel stays open on the same conversation, draft intact (FR-7).
- **While a dialog is open** (e.g. the fiche de soins): the notice and its replies work; « Ouvrir » opens the panel
  once the dialog is closed — the chat never closes a dialog or loses what is typed in it.
- **Named exceptions:** none.

## Scope

### In Scope
- The panel, conversations, quick messages, patient/appointment links, arrival, « Je m'en occupe », « Vu », presence,
  alerts (sound, in-app notice with replies, Windows notification + taskbar flash, browser notification), mute,
  quick-message editor, drafts.
- The Windows app update that adds its notification (all deployments).
- Carrying the chat on the PC de secours.

### Out of Scope
- Photos, files, voice notes · typing indicator · search in messages · editing or deleting a message.
- « Ne pas déranger » / « au fauteuil » status.
- An « arrivé » status, arrival time or waiting-room view on the agenda.
- Phone notifications when the app is closed (no phone push exists yet).
- Patient-facing messages (SMS/WhatsApp reminders are a separate feature).
- Chat between two different cabinets.

## Edge Cases

### EC-1: One-person cabinet
- **Scenario:** a solo dentist with no other account.
- **Expected Behavior:** the panel shows « Aucun collègue pour l'instant » with « Créer un compte » (to the users
  screen) for an admin; « Signaler l'arrivée » explains it is your own appointment.

### EC-2: No secretary
- **Scenario:** two dentists, no secretary account.
- **Expected Behavior:** no secretariat threads; two-person and « Tout le cabinet » conversations work.

### EC-3: Lost connection while sending
- **Scenario:** the LAN server or internet drops as « Envoyer » is pressed.
- **Expected Behavior:** « Non envoyé · Réessayer », text kept; a retry after a lost answer shows the message once.

### EC-4: Two secretaries take the same request
- **Expected Behavior:** one gets « Pris par … »; the other is told « Déjà pris par Salma »; no double claim.

### EC-5: Arrival announced twice
- **Expected Behavior:** the second person sees « Signalé à 09:32 par Salma » and must confirm to send again.

### EC-6: Appointment without a usable dentist
- **Expected Behavior:** imported appointment with no dentist, dentist without an account, retired dentist →
  « Signaler l'arrivée » stays, disabled, with the reason (AC-3.4).

### EC-7: Colleague deactivated mid-conversation
- **Expected Behavior:** sending is refused with « Ce compte est désactivé »; the conversation turns read-only; the
  draft is kept.

### EC-8: Linked patient or appointment removed
- **Expected Behavior:** the link shows « introuvable » / « RDV annulé », not clickable; the message stays.

### EC-9: Two tabs or two devices
- **Expected Behavior:** one sound per message per device; reading anywhere clears the others.

### EC-10: Old Windows app (before the update)
- **Expected Behavior:** sound + in-app notice only; the alert settings in the panel say the Windows app needs
  updating for Windows notifications.

### EC-11: Expired subscription
- **Expected Behavior:** history readable, « Vu » still updates; send / arrival / « Je m'en occupe » refused with the
  usual banner; the draft is kept.

### EC-12: Back after a night or an internet cut
- **Expected Behavior:** badge shows everything missed, at most one sound, no notices for old messages; urgent ones
  are marked in the badge and the list.

### EC-13: The session lock screen is up
- **Expected Behavior:** no in-app notice drawn over it; badge and sound; Windows/browser notifications as usual.

### EC-15: A dentist announces another dentist's patient
- **Scenario:** in a group practice with no secretary at the desk, Dr B presses « Signaler l'arrivée » on Dr A's
  appointment.
- **Expected Behavior:** it goes into the B ↔ A conversation; a secretary pressing it afterwards sees « Signalé à
  … par Dr B ».

### EC-14: Role change
- **Expected Behavior:** secretary → dentist: leaves every secretariat thread, gains their own; dentist → secretary:
  their own thread stops, they join the others' (history shown as read).

## Non-Functional Hints

- **Performance:** a sent message appears for its recipient within about a second on the LAN; a message must not
  make other colleagues' screens reload anything.
- **Security:** messages stay inside the cabinet; two-person conversations are private in the app. Whoever holds
  the database (the cabinet's server PC on the LAN install, the vendor's server when hosted) can technically read
  it — the same as every patient record.
- **Accessibility:** incoming messages are announced politely (urgent: assertively); the « Messages » control is
  named « Messages, 3 non lus »; « En ligne » and « Urgent » exist as text, not only colour; full keyboard path
  (open, choose a conversation, quick message, send, close); no movement under `prefers-reduced-motion`.
- **Scalability:** a cabinet de groupe of up to ~15 accounts; history grows forever (FR-8), so it loads 50 messages
  at a time, older ones on scroll-up.

## Dependencies

- **Build from `origin/main`**, not from `feature/security-remediation`: the PC de secours (needed by FR-8) is merged
  there and absent from the current branch.
- Notification center (`features/notification-center/spec.md`) — the rules this matches: clinic-scoped, actor never
  alerted, per-person read state, `99+`.
- PC de secours (`features/clinic-pc-copy`) — the chat is added to what it carries.
- Windows app release (« Client release » workflow) — the Windows notification ships with a new app version; pushing
  it needs the owner's OK.

## Open Questions

None blocking. Decided during this spec (2026-10-15):
- ~~Retention~~ — **never deleted** (owner).
- ~~Notice content~~ — sender + quick-message label, never a patient name or free text, everywhere outside the panel.
- ~~Phone placement~~ — a bottom-bar tab under 640 px; the header has no room at 320 px.
- ~~Export vs PC de secours~~ — out of the export, carried on the PC de secours.
- ~~Can the admin read others' private conversations~~ — no.

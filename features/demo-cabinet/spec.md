# Feature Specification: Public demo cabinet

**Status:** APPROVED (2026-11-03)
**Challenged:** No
**Created:** 2026-11-03
**Feature:** A read-only APEXA cabinet full of realistic work, opened from apexa.tn with one click, no account.

## Overview

Of 12 self-serve trials, 5 got in and entered nothing: they met an empty dashboard, alone, in the evening, with no
patient in front of them. A demo answers « what does this do for me? » **before** sign-up.

Any visitor of apexa.tn clicks « Essayer la démo » and lands, in a new tab, inside a real APEXA cabinet —
« Cabinet Dentaire Démo », the same cabinet as the « Premiers pas » videos — already full of patients, rendez-vous,
fiches, devis, payments and documents dated around today. They walk every screen as the dentist. Nothing they do is
saved; each attempt to save tells them so kindly and offers to create their own cabinet.

It runs at **demo.apexa.tn**, on the same server as production but as a fully separate copy of the app: its own
database, keys and file storage. No path leads from the demo to a real patient.

### Owner decisions (2026-11-03)

| Question | Answer |
|---|---|
| Address | `demo.apexa.tn` |
| Server | same VPS, isolated by its own database, role, keys and storage bucket |
| Visitor's view | dentist (cabinet admin) only; secretary view is a later phase |
| Saving | read-only now; a private writable copy per visitor is a later phase |

## User Stories

### US-1: Enter the demo with one click
As a dentist visiting apexa.tn, I want to open a working cabinet without creating an account so that I can judge
the product before giving my e-mail.

**Acceptance Criteria:**
- **AC-1.1:** Opening `https://demo.apexa.tn` with no session shows one entry screen: the APEXA name, « Cabinet de
  démonstration », one line saying nothing is saved, and one button « Entrer dans la démo ».
- **AC-1.2:** Pressing the button lands on the cabinet's Tableau de bord, signed in as Dr Monica Geller, in under
  3 seconds on an ordinary connection. No password, e-mail, security code or forced password change is ever asked.
- **AC-1.3:** The entry screen also offers « Créer mon cabinet » → `https://apexa.tn/#telecharger`.
- **AC-1.4:** Two visitors entering at the same time each get their own session; one leaving or expiring never
  signs the other out.
- **AC-1.5:** Opening `demo.apexa.tn/login` (or any sign-in, password-reset or sign-up page) leads to the entry
  screen, never to a login form.
- **AC-1.6:** A visitor already inside who reopens `demo.apexa.tn` goes straight to the Tableau de bord.

### US-2: Walk a cabinet that looks lived-in
As a visiting dentist, I want every screen to show realistic work dated around today so that I can picture my own
cabinet in it.

**Acceptance Criteria:**
- **AC-2.1:** Every screen in the side menu opens with content, none with an empty state (except « Rappels », which
  keeps its « Bientôt disponible » page).
- **AC-2.2:** The agenda shows the past two weeks done (each visit with its fiche), yesterday a full day, today a
  light day (about 3 visits per dentist) that moves with the clock, and the next two weeks booked.
- **AC-2.3:** Today's rendez-vous change status with the clock during the visit (« En cours » at its start, then
  « Séance passée » at its end), as in a real cabinet.
- **AC-2.4:** The content matches the scenario in FR-6, on any day of the year the visitor comes.
- **AC-2.5:** Every figure agrees with every other: a patient's balance, « À clôturer », la caisse, the dashboard and
  the devis tell one story.

### US-3: Be told kindly when something cannot be saved
As a visiting dentist, I want a clear, friendly answer when I try to save so that I understand it is a demo, not a
broken app.

**Acceptance Criteria:**
- **AC-3.1:** Any save, create, edit, delete, payment, upload, import, send or settings change is refused with one
  message: « Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données. » and a
  « Créer mon cabinet » action → `https://apexa.tn/#telecharger` (new tab).
- **AC-3.2:** After the refusal the dialog or form stays open with everything typed still there.
- **AC-3.3:** The refusal never looks like an error: no red error banner, no « Erreur », no « Vous n'avez pas les
  droits ».
- **AC-3.3b:** Inside a dialog or sheet, the message shows in the dialog itself (neutral tone, with the link), so it
  is read by screen readers and reachable by keyboard. Outside a dialog it is a toast.
- **AC-3.3c:** A control that changes on press before saving (agenda drag, a tooth mark, a status quick action,
  stock +/−, a dashboard toggle) returns to its stored state after the refusal.
- **AC-3.3d:** Repeated refusals show one message at a time, never a stack.
- **AC-3.4:** Opening any form, dialog or editor to look at it is allowed; only the final save is refused.
- **AC-3.5:** Nothing a visitor does changes what another visitor sees.

### US-4: Always know it is a demo, and how to start for real
As a visiting dentist, I want a permanent reminder that I am in a demo with a way to create my cabinet so that the
next step is always one click away.

**Acceptance Criteria:**
- **AC-4.1:** A slim band sits at the top of every screen inside the demo: « Cabinet de démonstration · rien n'est
  enregistré » + « Créer mon cabinet gratuitement » → `https://apexa.tn/#telecharger` (new tab).
- **AC-4.2:** At phone width the band shortens to « Démo · rien n'est enregistré » + « Créer mon cabinet », stays on
  one or two lines, and never covers a control.
- **AC-4.3:** The band cannot be closed (it is the one thing every visitor must know).
- **AC-4.4:** The band does not print.

### US-5: Look at and print documents
As a visiting dentist, I want to open, download and print the cabinet's ordonnances, certificats and notes
d'honoraires so that I can judge what my patients would receive.

**Acceptance Criteria:**
- **AC-5.1:** Every saved document opens in its preview, and « Télécharger » / « Imprimer » produce the real PDF on
  the cabinet's letterhead with its cachet.
- **AC-5.2:** The aperçu of an ordonnance inside a fiche de soins works.
- **AC-5.3:** Files (the panoramique, photos) open full size and in their viewers.
- **AC-5.4:** A document's « Modifier » opens the editor; saving it is refused as in US-3.
- **AC-5.5:** Every PDF from the demo carries a light diagonal « SPÉCIMEN — DÉMONSTRATION » mark and a footer line
  « Document de démonstration, sans valeur », so a stamped ordonnance or arrêt de travail cannot be passed off as
  real.
- **AC-5.6:** PDF generation and the aperçu are limited per session, so the demo is not a free PDF printer.

### US-6: Reach the demo from the website
As a visitor of apexa.tn, I want an obvious « Essayer la démo » so that I can try before signing up.

**Acceptance Criteria:**
- **AC-6.1:** « Essayer la démo » appears in the hero (beside « Essayer gratuitement »), in the phone menu, and as
  the call to action of the odontogramme page.
- **AC-6.2:** It opens `https://demo.apexa.tn` in a new tab.
- **AC-6.3:** Each click is counted as the visit event `essayer-demo`, like the download clicks.
- **AC-6.4:** The top bar keeps a single button (« Essayer gratuitement »); « Essayer la démo » joins it there only
  as a text link, if the bar has room at desktop width.

### US-7: The vendor trusts that the demo is safe and current
As the owner, I want the demo to share nothing with real cabinets and to always show the current product so that it
can run unattended.

**Acceptance Criteria:**
- **AC-7.1:** No SMS, WhatsApp, e-mail, push notification or Google Calendar event ever leaves the demo.
- **AC-7.2:** A session obtained on the demo is refused by app.apexa.tn, and the reverse.
- **AC-7.3:** The demo never reads or writes production's database or file storage.
- **AC-7.4:** Every production deploy also updates the demo to the same version.
- **AC-7.5:** The cabinet is rebuilt every night (03:00 Tunis time) with dates moved to the new « today ».
- **AC-7.6:** After each rebuild the money and schema checks pass; if not, the previous night's cabinet stays up and
  the failure is visible to the operator.
- **AC-7.7:** Search engines do not index the demo.

## Functional Requirements

### FR-1: A separate kind of deployment
- The demo shall run the same version of the app as production, configured as a distinct deployment kind with its
  own explicit answer for every existing deployment capability.
- It shall have its own database, database login, signing key, data-protection keys and file-storage bucket.
- It shall have no SMTP, SMS, WhatsApp, Meta, Google or push configuration.
- It shall not offer: public sign-up, joining a clinic, password reset by e-mail, the vendor console,
  subscriptions, vendor messaging, backups, the PC de secours feed or relay, Meta onboarding.
- It shall keep the clock-driven appointment statuses (AC-2.3) and nothing else that runs on a timer and writes
  clinic data (no stock alerts, monthly expenses, activity counting, recovery points, subscription warnings).

### FR-2: Read-only
- Every action that would change stored data, or hand data out of the demo (CSV and dossier exports, the archive —
  even when they are reads), shall be refused with the demo answer (API contract below). Classification is per
  action, not per HTTP method.
- These shall stay allowed: entering the demo, keeping the session alive, leaving it, generating a document's PDF
  for download/print, the ordonnance aperçu, and the browser's own security reports.
- Records the server writes by itself while a visitor reads (the « consulté » audit trail, the clock-driven
  statuses) shall stay allowed.
- The refusal shall happen before anything is written, including retry/idempotency bookkeeping.
- Every action the app exposes shall be classified once — refused, or allowed with a reason — and a new action
  shall not ship unclassified.

### FR-3: The demo session
- Entering shall create a new, separate session for the demo dentist each time.
- A session shall last at most 2 hours; after 30 minutes without activity it ends as any untrusted session does.
- When a session ends (time limit, idle, nightly rebuild), the visitor shall land on the entry screen with « La
  démo a expiré. Entrez à nouveau pour continuer. » — never on a login form.
- Entering shall be limited per network address — the visitor's real address, not the web server's — with a burst
  limit generous enough for a cabinet or a classroom behind one address.
- Entering shall write only the new session, never the demo dentist's own record, so simultaneous entries cannot
  collide.
- The demo dentist's account shall never require a password change or a security code.
- A visitor inside when the nightly rebuild finishes keeps the cabinet they entered until their session ends; the
  next entry lands in the new one.

### FR-4: Screens adapted for the demo

| Surface | Demo behaviour |
|---|---|
| « Séance terminée » popup | never appears |
| Abonnement | absent (no subscription in the demo) |
| Sécurité | page visible; every action (codes, authenticator) refused per US-3; « Mes appareils » lists only the visitor's own session, with no « Déconnecter » (never other visitors' devices) |
| Any action that asks for a security code or password first (exports, « Afficher l'argent », TOTP reset of a user, recovery codes, PC de secours, archive restore) | refused per US-3 **before** the prompt — the demo dentist has no authenticator |
| Changer le mot de passe | form visible; saving refused per US-3 (not a generic error) |
| Se déconnecter | ends the session → entry screen |
| Mode discret | the switch is visible; using it is refused per US-3. The money is always shown |
| Exports (patients CSV, dossier, agenda CSV, archive) | refused per US-3 **before** any security-code prompt |
| Any upload (Fichiers, letterhead, CSV import, archive restore) | refused per US-3 the moment a file is picked or dropped, before it is read |
| Dashboard « Personnaliser » | opens; toggling refused per US-3 and the dashboard stays as it was |
| Notifications bell | readable; marking read refused quietly; « Supprimer » / « Tout effacer » refused per US-3 (the row comes back with the message, never silently) |
| Messages (staff chat) | readable; sending refused per US-3; marking read refused quietly; visitors arriving or leaving are not broadcast as presence |
| Booking prompts that come from the save (« créneau occupé », « hors horaires », « patient en double ») | never shown — the save is refused first; only prompts the browser raises itself (e.g. « heure passée ») may show |
| Installing the app (phone / desktop) | not offered; the tab name is « APEXA Démo » |
| Rappels | its existing « Bientôt disponible » page |
| Fichiers of a patient with no folders | shows the folders without trying to create them, or the patient already has them |
| Notifications push permission | never asked |

- Anything that writes on its own while browsing (not on a press) shall fail silently, never with a message.

### FR-5: The band
- Per US-4. It shall sit above the page header on every in-app screen, at every width, and respect the phone's
  safe areas.

### FR-6: The cabinet's content (rebuilt nightly, dated relative to today in Tunisia)

| Area | Content |
|---|---|
| Cabinet | « Cabinet Dentaire Démo », Avenue des Jasmins, Tunis; letterhead and cachet from the videos; hours with a lunch break |
| Team | Dr Monica Geller (admin, dentist — the visitor), Dr Chandler Bing (dentist), Rachel Green (secrétaire) |
| Patients | ~40 with « Friends » names, adults and children (one « denture mixte »); antécédents and allergies on a few; Ross Geller with his pénicilline allergy and asthma as in the videos; no real-looking phone numbers |
| Agenda | per AC-2.2, both dentists; one congé day for Dr Bing; a recurring series; a waiting-list entry |
| Clinical | odontograms with caries, obturations, couronnes, a bridge (piliers + pontique), an implant, absent teeth, « à traiter » marks; fiches on every past visit |
| Treatments | devis at every state: followed draft, accepted with échéancier, a multi-séance couronne in progress, an implant waiting osseointegration, completed, stopped |
| Money | cash / cheque / card payments over the month; one cheque to deposit; notes d'honoraires; monthly expenses; one patient owing (« Reste à payer »); one avoir |
| Documents | ordonnances (médicaments and a separate demande d'examens), a certificat, an arrêt de travail, on the letterhead |
| Files | the panoramique and a few intra-oral photos |
| Stock and lab | one item low, one lot expiring, two fournisseurs, one prothèse late at the lab |
| Worklists | 1–2 past séances without a fiche at rebuild, plus today's visits as they end (« À clôturer » peaks around 8 by evening — realistic); a « suite à planifier »; someone to « relancer » |
| Journal d'activité | the seeded actions, attributed to the three team members, at their scenario dates; visitors' own « consultation » rows are added under Dr Geller as they browse (accepted: it looks like a real cabinet) |

- Content shall be built through the app's own business rules (numbering, money, chart, notes d'honoraires), so it
  reads exactly like a real cabinet's.
- Business dates carry the scenario's dates: visits, fiches, payments, expenses, documents, échéances, lab dates.
  Visits fall on working days and inside the cabinet's hours, so the agenda, la caisse, late échéances and
  « relancer » read as in real life.
- **Creation stamps read the rebuild day** (owner, 2026-11-03: « we can live with it »): a patient's « créé le », a
  devis's acceptance date, a note d'honoraires's issue date, the journal's times, « nouveaux patients » on the
  dashboard. The app has no clock that can be set back, and making one was judged not worth ~170 files.
- Actions are attributed to the team member who would have done them (journal author), not all to Dr Geller.
- Shown claims shall respect the website's banned list: no CNAM screen, no working SMS/WhatsApp reminders.

### FR-7: Nightly rebuild
- At 03:00 Tunis time the cabinet shall be replaced by a fresh one dated to the new day.
- The new cabinet shall be built aside and becomes the one new visitors enter only once it is complete and checked;
  visitors never see a half-built cabinet, and nobody inside is interrupted.
- Yesterday's cabinet shall be removed once no session can still be using it.
- « La démo se met à jour, revenez dans une minute. » shows only when no complete cabinet exists at all (first
  start of a new deployment).
- After the rebuild the money check and the schema check shall run; a failure keeps the previous cabinet and alerts
  the operator.

### FR-8: Website entry
- Per US-6. The website changes ship only with the owner's OK (two pushes).

## API Endpoints

### Capability read (existing, extended)
```
GET /api/auth/mode
Authorization: none

Response 200 (new optional fields; absent = false, as for every flag here):
{
  ...existing fields...,
  "isDemo": true,
  "demoSignupUrl": "https://apexa.tn/#telecharger"
}
```

### Enter the demo (new)
```
POST /api/auth/demo-session
Authorization: none
Request: {}  (no body fields)

Response 200: same shape as a successful login (access + refresh token, user, expiry ≤ 2 h)

Response 404: the deployment is not the demo (the endpoint does not exist there)
Response 429: { "error": "Trop de visites depuis cette adresse. Réessayez dans quelques minutes.", "code": "demo_rate_limited" }
Response 503: { "error": "La démo se met à jour, revenez dans une minute.", "code": "demo_rebuilding" }
```
Reached by the browser through the web app's own sign-in relay, which sets the same cookies as a login.

### The refusal (every refused request)
```
Response 403:
{
  "error": "Démonstration : rien n'est enregistré. Créez votre cabinet pour essayer avec vos données.",
  "code": "demo_read_only"
}
```

## Device & Interface Behaviour

**Leading device:** a dentist's laptop in the evening (the trial drop-off pattern) and a phone opened from the
website or a Facebook post. Both must work fully; the owner's own laptop is 1536×864 (~730 px of page).

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| Entry screen | one column, button full width, ≥ 44 px, nothing scrolls sideways at 320 px | centred card | centred card |
| Band | short text, one or two lines, CTA a ≥ 44 px target on a coarse pointer, below the status bar / notch | full text, one line | full text, one line, spans the content column |
| Refusal message | toast above the bottom bar, CTA reachable with a thumb; does not cover the dialog's save button permanently | toast | toast |
| Every in-app screen | unchanged from the product, minus the band's height | unchanged | unchanged |
| « Expirée » / « Mise à jour » screens | same layout as the entry screen | same | same |

- **Touch paths:** nothing in this feature relies on hover.
- **Named exceptions:** none. The post-visit popup is withdrawn on every device (FR-4), not hidden by width.

## Scope

### In Scope
- The demo deployment kind, read-only behaviour, demo session, entry/expiry/rebuild screens, band, refusal message.
- The adapted surfaces of FR-4.
- The cabinet content and its nightly rebuild with checks.
- Compose, reverse-proxy, DNS and deploy pieces — **prepared locally and shown to the owner before anything touches
  the VPS or DNS**.
- The website button and click count (owner's OK, two pushes).
- Operator notes in `deploy/README.md`, and `features/demo-cabinet/notes.md` once shipped.

### Out of Scope
- Secretary view / persona choice (phase 2).
- A private writable copy per visitor (phase 2).
- The Windows app, Android or iOS shells pointing at the demo.
- The vendor console seeing the demo.
- Languages other than French.
- Measuring what visitors do inside the demo (only the website click is counted).
- The « Premiers pas » guided first days (separate feature); if it ships first, it shall not appear in the demo.

## Edge Cases

### EC-1: Session ends mid-dialog
- **Scenario:** a visitor's 2 h limit or idle limit hits while a dialog is open.
- **Expected:** the next action lands on the entry screen with « La démo a expiré… »; one click puts them back on
  the dashboard.

### EC-2: Visitor present at 03:00
- **Scenario:** the nightly rebuild runs while someone is inside.
- **Expected:** they keep walking yesterday's cabinet until their session ends (≤ 2 h); the next entry opens the new
  one. They are never shown a half-built cabinet.

### EC-3: Many visitors at once (a Facebook post)
- **Scenario:** dozens enter within minutes.
- **Expected:** each gets a session; the shared cabinet does not change; no visitor's arrival makes another
  visitor's screen reload; production is unaffected (the demo cannot take the server's memory or CPU from
  production).

### EC-4: A class or a cabinet behind one address
- **Scenario:** 10 people on one Wi-Fi enter within a minute.
- **Expected:** all get in; only an abusive burst gets « Trop de visites… ».

### EC-5: Visitor copies the demo link into app.apexa.tn
- **Scenario:** a demo session cookie or token is replayed against production.
- **Expected:** refused as an unknown session.

### EC-6: Save that would normally ask a question first
- **Scenario:** booking on an occupied slot, outside hours, or for a likely duplicate patient.
- **Expected:** those questions come from the save itself, so they are not shown; the save is refused per US-3 at
  once and the dialog stays open. A question the browser asks on its own (« heure passée ») may show first.

### EC-7: The rebuild fails
- **Scenario:** the nightly rebuild errors or a check reports drift.
- **Expected:** yesterday's cabinet stays up (dates one day old beats an empty or wrong demo) and the operator is
  alerted.

### EC-8: A day without a working day
- **Scenario:** « today » is a Sunday or a holiday.
- **Expected:** the agenda still shows recent done work and upcoming bookings; today simply has no slots, like a real
  cabinet; the dashboard still has content.

### EC-9: Search engine or link preview
- **Scenario:** a crawler or a Facebook link preview fetches demo.apexa.tn.
- **Expected:** not indexed; the entry screen gives a clean title and description for the preview.

## Non-Functional Hints

- **Performance:** entering in under 3 s; screens as fast as production.
- **Security:** no route from the internet to real patient data; demo credentials and keys distinct from
  production's; entering rate-limited; no outbound messages; demo resource use capped so it cannot starve production.
- **Accessibility:** the band and the refusal are announced to screen readers once (not on every page change); the
  CTA is a real link with a clear name; contrast meets the product's tokens in light and dark.
- **Scalability:** dozens of concurrent visitors on a shared read-only cabinet.

## Dependencies

- Production deploy workflow (the demo must follow its image tag).
- DNS (OVH, owner's account), reverse proxy on the VPS — owner's OK for each step.
- The website's click-count script, today only on `feature/security-remediation` (`587ad9ea`), not on `main`.
- « Premiers pas » assets in `C:\dev\clinic-film` (letterhead, cachet) and `site/src/img/pano.jpg`.
- `features/multi-tenant-cloud/notes.md` (deployment capabilities), `handoff.md`, `exploration.md`.

## Open Questions

- [ ] None blocking. Placement of « Essayer la démo » on the website is decided in US-6; the owner reviews it with
      the site preview before the push.

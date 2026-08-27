# Open after `vendor-whatsapp-messaging-quota` (Part 5, § 43)

Captured 2026-08-12 at Part 5, the feature's closing part. None of these blocks the feature — it is complete and
verified — and every one has its remedy already chosen so the next reader does not re-do the analysis.

Three of them are **facts about Meta that no amount of reading this repository can settle**: they need a Meta
account, a rate card, or a page behind a login. Story 0 opened them and is why they are stated precisely rather
than vaguely.

---

## 1. The server's Graph API version is five releases behind (`R-2a`)

`Meta:GraphApiVersion` and the browser SDK's `NEXT_PUBLIC_META_GRAPH_VERSION` both resolve to **`v21.0`**; Meta's
own Embedded Signup sample recommends **`v26.0`**. Story 0 step 4 made the two derive from a single
`META_GRAPH_API_VERSION` key in `deploy/`, so the bump is now a **one-key change** — which is exactly why it was
kept out of Part 4 § 31.

**Why deferred, not applied.** That one key moves **every server Graph call at once**: the Embedded-Signup token
exchange, the app subscription, the phone registration, the template submission, the template read-back and every
WhatsApp send. A version bump is not a refactor — Meta changes field names and error codes between versions — so it
needs its own test pass against a real Meta app, which this deployment does not have (see item 4).

**Chosen remedy.** Bump `META_GRAPH_API_VERSION` in `deploy/.env.hosted` **once Meta credentials exist**, then walk:
connect a cabinet, submit the template, receive one `message_template_status_update` webhook, send one reminder.
Reject the alternative of bumping only the server or only the browser — Meta versions them as one release, and
Story 0's whole point was that they had silently drifted apart.

---

## 2. Does Embedded Signup **v3** carry its own end date?

Part 4 § 31 migrated the connect flow from v3 to **v4** (dropping `extras.sessionInfoVersion`). Story 0 established
that the 15 October 2026 deprecation names **v2 only** — so we were a version behind current, never on the condemned
one. What was **not** read is Meta's « Versions » section, which would say whether v3 itself has an announced end.

**Why deferred.** It changes nothing about what § 31 did — only whether that work was *optional* — and the page was
not retrieved. Recording the question is worth more than guessing at it.

**Chosen remedy.** Read the « Versions » section of
`developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/implementation` and record the
answer in this file, then delete the item. Nothing in the code changes either way.

---

## 3. Two Meta rules the spike could not settle

**(a) WhatsApp message-template edit caps.** The page supplied to Story 0 was the *Messenger Platform* templates
doc (Button / Generic / Receipt / Coupon…), not WhatsApp's. What *was* found is a different limit worth knowing —
**display name** changes are capped at 10 per 30 days, and after approval the number must be re-registered within
14 days or the name goes back for review (`phone_number_name_update` webhook).

*Why it matters here:* `WhatsAppReminderTemplate` is the one definition of the reminder template's name, language,
category and body. If edits are capped, changing the wording is a rationed operation and the product should say so
before an admin edits it — today nothing does, because nothing knows the cap.

**(b) Can one payment instrument serve several WhatsApp Business accounts?** Not directly stated anywhere read.
Supporting evidence points to yes and stops short of saying it: Business Manager supports a business-owned line of
credit with **Dynamic Credit Allocation** across accounts, and pricing aggregates across **all WABAs in one
portfolio**.

*Why it matters here:* it is the vendor's own operating model — one credit line behind N cabinets — and FR-2's
« the vendor's money is never the clinic's » assumes it. If it turns out to be false, each cabinet needs its own
instrument and the vendor's billing story changes (the *product* does not).

**Chosen remedy for both.** Ask a Meta representative or read them out of Billing Hub once the vendor has a real
Meta Business account. Neither blocks anything shipped.

---

## 4. Nothing in this feature has met Meta

The template has never been submitted, the webhook has never been called by Meta, the v4 popup has never opened in
a browser, and **no `Meta:AppId`/`Meta:AppSecret` exists on any deployment** — Story 0's 🔴, still open. Everything
is verified against the documented payload shapes and the unit suite (61 tests over the Meta surface alone).

`deploy/docker-compose.hosted.yml` and `.env.hosted.example` now carry the five keys the walk needs
(`META_APP_ID`, `META_APP_SECRET`, `META_WEBHOOK_VERIFY_TOKEN`, `META_CONFIG_ID`, `META_GRAPH_API_VERSION`), so the
configuration half is done and only the account is missing.

**Chosen remedy.** The operator walk in [`deploy/README.md`](../deploy/README.md) § « Forfait de rappels WhatsApp »,
run once against a real Meta Business account. Items 1 and 3 fall out of the same session.

---

## 5. Cabinets already using their own WhatsApp credentials

AC-1.7 closes the manual credential fields where the vendor manages WhatsApp — but a cabinet that configured its
**own** `WhatsApp:ApiUrl` / `PhoneNumberId` / `AccessToken` before this feature keeps them, and
`ClaimsItsOwnWhatsApp` still reads exactly those columns, so it goes on sending on its own account and its own bill.

**Why deferred, not applied.** It is the right behaviour today: those cabinets are *not* spending vendor capacity,
so metering them would be wrong, and silently moving them onto the vendor's account would move their bill without
asking. `deploy/` has no such cabinet — this is a hosted-deployment question that arises the first time one exists.

**Chosen remedy.** When it does: leave the credentials in place, and add a **`messaging-report` bucket** (« cabinet
sur ses propres identifiants ») so the vendor can see who is outside the forfait rather than reading their absence
from the exhausted list. Reject a migration that clears the columns — it would take a working practice off the air
at whatever moment it ran.

---

## 6. The responsive eye pass and the AC-6.9 deactivated-account walk

Two verifications this feature has owed since Parts 2–4, both blocked on tooling rather than on a decision.

- **The eye pass** at 320 / 390 / 820 / 1180 / 1440 px plus a landscape phone, over `/rappels` (Parts 2 and 4) and
  the console's two new sheets (Part 3). The mechanical gates pass — `check:responsive` 15/15 in `web/` and 14/14 in
  `console/`, `tsc` and both builds clean — and a diff re-read against `DEVICE-CONTRACT.md` § 1 caught one real
  defect in Part 2 (two overlapping `.touch-target` contact links). ⚠️ Another author added `web/scripts/shots.mjs`
  mid-feature, which should make this cheap.
- **AC-6.9**, confirmed *by trying it*: sign in to the console, run `platform-account --deactivate`, call both
  messaging writes with the same token. Asked for explicitly rather than inherited, because
  `PlatformAccountStateMiddleware` was **inert in production for the whole life of `platform-console`** while every
  layer reported it present (Part 7 of that feature). It needs a running console listener and an SSH tunnel.

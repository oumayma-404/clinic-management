# Making the WhatsApp reminder forfait actually work — what you have to do

**What it is, in one line:** you buy WhatsApp messages from Meta, each cabinet gets a monthly allowance of
appointment reminders, and when a cabinet runs out its reminders are **held** (not lost) until you top it up.

**Where it works:** only on the **hosted** deployment (`DEPLOYMENT_PROFILE=HostedMultiTenant`). On a clinic's own
PC or the Auth0 cloud setup the whole feature is invisible — that is deliberate, not a bug.

**Status right now:** all the code is written, tested and committed. **Nothing has ever talked to Meta.** Steps 1–3
below are the part that has never been done.

---

## Step 1 — Create the Meta account (the big one, ~1–2 hours)

- Go to **developers.facebook.com** and create a **Meta app** of type *Business*.
- Add the **WhatsApp** product to it.
- Create a **Business portfolio** if you don't have one, and add a payment method — **this is the card Meta bills
  for every reminder sent.**
- Set up **Embedded Signup** and create a **configuration** — this is the flow a dentist clicks through to connect
  their own WhatsApp number.
- Write down four things:
  - **App ID**
  - **App Secret**
  - **Configuration ID** (from Embedded Signup)
  - the **Graph API version** you're on (we currently use `v21.0`)

> ⚠️ Meta's approval can take a few days. Start here, not last.

## Step 2 — Tell Meta where to send updates

- In the app's WhatsApp → **Webhooks** section, set the callback URL to:
  `https://VOTRE-DOMAINE/api/meta/webhook`
- Invent a random **verify token** (e.g. run `openssl rand -hex 24`) and paste it into Meta's form.
- Subscribe to these two fields:
  - `account_update` — **required**, or Embedded Signup won't work at all
  - `message_template_status_update` — this is how a cabinet's message template gets approved

## Step 3 — Put the values in `deploy/.env`

Copy them out of `deploy/.env.hosted.example`. The ones that matter:

```
META_APP_ID=...              # from step 1
META_CONFIG_ID=...           # from step 1
META_APP_SECRET=...          # from step 1 — secret, never shared
META_WEBHOOK_VERIFY_TOKEN=... # the random string from step 2
META_GRAPH_API_VERSION=v21.0

MESSAGING_DEFAULT_MESSAGES_PER_MONTH=200   # what each NEW cabinet starts with
MESSAGING_CONTACT_EMAIL=votre@email.tn     # shown to a cabinet that runs out
MESSAGING_CONTACT_PHONE=+216 ...           # same
```

Then restart **with a rebuild**, not a plain restart:

```bash
docker compose -f docker-compose.hosted.yml up -d --build
```

> ⚠️ **Why `--build` matters:** `META_APP_ID` and `META_CONFIG_ID` get baked into the website when it is built. A
> normal restart leaves the browser using the old values and nothing tells you.

## Step 4 — Check it came up correctly

```bash
docker exec clinic-api-prod dotnet ClinicManagement.API.dll verify-schema
```

Look at the **« Messaging allowances »** section. You want three green lines. If it says *« not applicable — this
deployment does not sell vendor messaging »*, your `DEPLOYMENT_PROFILE` is not `HostedMultiTenant`.

## Step 5 — Connect one real cabinet (the first true test)

- Log in as that cabinet, go to **« Rappels »**.
- Press **« Connecter WhatsApp »** — a Meta popup opens and asks them to pick/create a WhatsApp number.
- After it closes, the card should say the template is **« en attente de validation »**.
- Wait for Meta to approve the template (usually minutes to a few hours). The card then reads **« prêt »**.
- Book a test appointment and confirm the reminder actually arrives on a phone.

> This is the step that proves the whole chain. Until a real reminder lands on a real phone, treat the feature as
> unverified.

---

## Day-to-day: the three commands you'll use

Run them all as `docker exec clinic-api-prod dotnet ClinicManagement.API.dll <command>`.

- **See who needs attention** — run this weekly:
  `messaging-report`
  Exits with code 2 if anything needs you. It groups cabinets into: *aucun forfait* (worst — you never gave them
  one), *épuisé* (out of messages), *non mesuré*, and *template changed*.

- **Reconcile against Meta's bill** for a finished month:
  `messaging-report --month 2026-07`

- **Set a cabinet's monthly allowance:**
  `messaging-grant --clinic owner@cabinet.tn --per-month 500`
  ⚠️ Raising it works **today**. Lowering it takes effect **next month** — that's on purpose, so nobody loses
  reminders they were counting on. A value of `0` is a lowering too.

- **Sell them extra messages for one month:**
  `messaging-grant --clinic owner@cabinet.tn --top-up 300 --month 2026-08 --amount 45.000 --method Transfer`

- **Fix a mistake:**
  `messaging-report --clinic owner@cabinet.tn` (prints the allocation ids — the only place they appear), then
  `messaging-cancel --clinic owner@cabinet.tn --entry <id> --reason "..."`
  Nothing is deleted: the row stays, struck through, with your reason on it.

---

## Things that will confuse you if nobody says them

- **A cabinet that runs out doesn't lose reminders.** They're held. Top the cabinet up and they go out.
- **SMS is never counted.** Only WhatsApp reminders spend the allowance.
- **« aucun forfait » ≠ « épuisé ».** The first means *we* never gave them an allowance (your fault, fix with
  `--per-month`); the second means they used theirs up.
- **« non mesuré » ≠ « 0 envoyé ».** The first means we didn't count; the second means a genuinely quiet month.
- **Changing `MESSAGING_DEFAULT_MESSAGES_PER_MONTH` does not move existing cabinets** — only new ones. Use
  `messaging-grant` for cabinets that already exist.
- **A cabinet using its own WhatsApp credentials keeps using them** and spends nothing of yours.

---

## Still not done (and not blocking anything)

Full detail in [`follow-up/vendor-messaging-open-questions.md`](../../follow-up/vendor-messaging-open-questions.md):

- The real Meta walk above — **steps 1–5 have never been performed**.
- Bumping the Graph API version `v21.0` → `v26.0` (one `.env` key, but it moves every Meta call at once — do it
  *after* step 5 works, then re-run step 5).
- Checking the screens at phone/tablet/desktop widths.
- Two questions only Meta can answer: how often a message template may be edited, and whether one payment method
  can cover several WhatsApp accounts.

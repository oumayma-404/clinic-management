# Go-live manual — what only a human can do

Everything below is **manual**: a decision, a purchase, an account, a key, or a physical device. Code work is
tracked separately; this file is the list no amount of coding removes.

Costs are **approximate and must be re-checked** — they change, and none of them were verified today.

**Order matters.** §1 blocks §5 and §6. §2 has the longest lead times, so start it on day one and let it run in
parallel with everything else.

---

## 0. Close out what is already written (today, free)

- [ ] Commit the staged work — `HEAD` is behind and ~80 files are staged.
- [ ] Fix `AuditInterceptorTests.The_Exclusion_List_Is_Still_Only_The_Two_Documented_Types` — the
      `clinic-self-signup` change added `ClinicSignup` to the interceptor's exclusion list without updating the test.
- [ ] Apply the new migration to a real database and run **`dotnet run -- verify-schema`**.
      The `AddClinicIdToClinicalChildren` backfill has never touched a database. Confirm
      `clinical-child-clinic-matches-patient` reports **0**. Exit code 0 = clean, 2 = drift.
- [ ] Push and read the new CI (`.github/workflows/ci.yml`). `next build` has never run honestly — the local dev
      server masks it.

---

## 1. Decisions only you can make (free, minutes — but they block the store work)

- [x] **Bundle identifier — SETTLED** as `com.clinicmanagement.shell`, in *both*
      `mobile/android/app/build.gradle.kts` (`applicationId`) and `mobile/ios/project.yml`
      (`PRODUCT_BUNDLE_IDENTIFIER`). ⚠️ **It cannot be changed after the first store submission**, and it must move
      on both platforms in one commit or the two stores hold two different products. Play does not require you to
      own a matching domain. *(This entry used to call the value a « placeholder » and invite changing it; both
      `build.gradle.kts` and `mobile/README.md` had already recorded it as settled.)*
- [x] **Public app name — SETTLED** as « APEXA », on every platform. (Was « Gestion Clinique »; the bundle
  identifier `com.clinicmanagement.shell` was deliberately left alone — see `mobile/README.md`.)
- [ ] **The domain** the hosted backend runs on, e.g. `app.yourclinic.tn`. Buy it if you have not.
- [ ] **Who owns the accounts** — Apple, Google Play and the domain should be registered to the *company*, never a
      personal account. Moving them later is painful or impossible.

---

## 2. Purchases and accounts (start now — lead time is the constraint)

| # | Item | Approx. cost | Lead time | Needed for |
|---|---|---|---|---|
| 2.1 | **Apple Developer Program** | ~$99 / year | hours–days (can be longer for companies: D-U-N-S number required) | iPhone shell, at all |
| 2.2 | **Google Play Console — ⚠️ ORGANIZATION account, see below** | ~$25 one-time **+ a legal entity** | **days–weeks** (D-U-N-S + document verification) | Android shell **on Play** — the sideloaded APK needs none of this |
| 2.3 | **Windows code signing** — ⚠️ **deferrable, see below** | ~$10 / mo (Azure Trusted Signing) or ~$200–600 / yr (OV/EV) | days–weeks (identity vetting) | Desktop installer without a SmartScreen warning |
| 2.4 | **Server hosting** (≥4 GB RAM) — ⚠️ **free for the pilot, see below** | **$0** (Oracle Always Free) → ~**€4.35 / mo** (Hetzner CX22) | minutes–days | The hosted backend |
| 2.5 | **Domain name** | ~$10–40 / year | minutes | TLS, store listings, email links |
| 2.6 | **Off-site backup storage** (S3-compatible, for WAL-G PITR) | **$0** — Oracle Object Storage 20 GB, or Cloudflare R2 10 GB | minutes | `WALG_S3_*` — off-machine recovery |
| 2.7 | **Transactional email sender** (SMTP) | **$0** — Brevo free: 300/day (~9 k/month) | hours (domain verification: SPF + DKIM) | Clinic self-signup verification emails |
| 2.8 | A **physical Android phone** and a **physical iPhone** | — | — | The hardware walks in §5/§6 that no CI replaces |
| 2.9 | A **Mac**, or a paid macOS CI runner | — | — | iOS archive + upload (unsigned CI build is not enough) |

⚠️ **2.9:** `mobile/ios/` has **never been compiled**. Get `.github/workflows/ios-shell.yml` green *before* spending
anything on 2.1 — a green build is the cheapest proof the Swift is real.

### ⚠️ 2.2 in detail — Play needs a company, and § 5 no longer waits for it

**Google Play will not accept this app from a personal developer account.** Health and medical apps must come from
a **verified Organization** account: a registered legal entity, a D-U-N-S number (free, instant to ~30 days),
government-issued business registration documents, proof of address and the representative's ID. Individual
accounts were barred from the Medical/Health categories, existing health apps forced to migrate by 28 January 2026.
There is no « submit now, paperwork later » path.

Two things soften it:

1. **Organization accounts are exempt from the 12-testers-for-14-days rule** that binds personal accounts created
   after 13 November 2023 — so the harder account also removes a two-week gate.
2. **The entity was already on this list.** § 7 needs it for INPDP, the hosting DPA and invoicing clinics. Play
   surfaces the requirement; it does not add it. And a **sole proprietorship** may be enough — D&B issues D-U-N-S to
   one, and a Tunisian *entreprise individuelle* / *patente* is government-issued business registration. Unconfirmed
   for Tunisia; confirm against the Play Console signup flow. Apple is stricter and rejects sole traders.

**Apple has no equivalent rule** — an *individual* Apple Developer account needs no D-U-N-S and may publish health
apps. So § 6 is blocked on a Mac and on the Swift compiling, not on a company.

**Meanwhile, distribution needs neither store, and § 5 is written to start today:** a **sideloaded APK** on Android
(push included — FCM needs Play *Services* on the phone, not Play *Store* distribution) and the **installable web
app** on both platforms. Full checklist: [`mobile/STORE-SUBMISSION.md`](mobile/STORE-SUBMISSION.md).

### ⚠️ 2.4 in detail — start free, move to ~€5/month before real patients

**Oracle Cloud Always Free** is the one free option that genuinely fits: **2 OCPU / 12 GB RAM (ARM)**, 200 GB block
storage, never expires, commercial use allowed. Note it was **4 OCPU / 24 GB until Oracle halved it on 15 June 2026
with no announcement** — 12 GB still comfortably exceeds what this stack needs.

Three caveats before relying on it:

1. **It is ARM (aarch64).** Every image must have an arm64 variant. .NET 8, Node, Postgres, Caddy and MinIO do —
   ⚠️ **verify WAL-G**, which is the one to check. Cheap to test, and the only real technical risk.
2. **"Out of host capacity"** is common for ARM in busy regions. Frankfurt and Singapore reportedly provision in
   minutes; US East can take days.
3. **Oracle can reclaim idle Always Free instances.** A two-clinic pilot may look idle.

A credit card is required for identity verification, and Oracle has just shown it will cut free limits silently.

**Compare: Hetzner CX22 is ~€4.35/month** (2 vCPU / 4 GB, x86) — no ARM question, no capacity lottery, no
reclamation. So the real choice is *free-with-friction* versus *~€5/month with none*.

**Recommendation: Oracle free for the § 3 deploy rehearsal, restore drill and CSP walk. Move to Hetzner before the
first real clinic** — patient data on an instance that may be reclaimed is not worth €5.

⚠️ **Free does not dodge § 7.** Neither provider has a Tunisian region, so either way you are choosing a
jurisdiction (EU, most likely). Settle **data residency first** — it can override both options.

#### If you have no credit card

Oracle, AWS, GCP, Azure and Fly.io all require a card for identity verification, and **no free multi-container
cloud host fits this stack**: Render's free tier sleeps on inactivity (which alone kills the reminder dispatcher,
the backup job and the push queue), Koyeb's gives one web service plus one Postgres, and Back4app one container.
This stack is five containers plus minutely Hangfire jobs.

**The cardless path is to self-host and tunnel** — which suits this product, since it already ships a
`SelfHostedLan` profile designed to run on a clinic's own Windows PC:

1. Run `deploy/docker-compose.hosted.yml` on a machine you own (Docker is already installed for dev).
2. Expose it with **Tailscale Funnel** — free, **no card, no domain**, valid HTTPS on
   `machine.<tailnet>.ts.net`, works behind NAT with no port forwarding.
   (Cloudflare Tunnel is the alternative, but reports conflict on whether it requires a card, and a custom
   hostname needs a domain you own.)

⚠️ Three caveats: **Tailscale's free tier is personal-use** — fine for the § 3 rehearsal, **not** for serving real
clinics; the machine must stay powered on; and **the tunnel terminates TLS**, so Caddy must not also request an
ACME certificate — serve plain HTTP behind the tunnel and add the tunnel hop to `Security__TrustedProxies`, or
every address-keyed rate limit collapses into a single bucket.

⚠️ **Linode/Akamai's "$100 / 60-day trial" is NOT cardless** — Akamai requires a valid card or PayPal to activate
the credit, and **charges it automatically once the credit expires or runs out**. Widely repeated as "no credit
card required"; it is not. And with a payment method in hand, **Oracle Always Free beats it anyway** — permanent
$0 versus a 60-day credit with a billing cliff.

⚠️ **Worth asking your bank about a Tunisian « carte technologique »** (the prepaid card with an annual
foreign-currency allowance for online tech purchases), or any virtual prepaid card. **Unverified — confirm with the
bank.** It would unlock Oracle Always Free permanently and Hetzner later, and turn this whole section back into the
normal path.

### ⚠️ 2.3 in detail — you probably do not need this yet

Windows does not *require* a signed binary. Unsigned, the cost is friction, and how much friction depends entirely
on **who runs the installer**:

| Distribution model | Certificate needed? |
|---|---|
| You install on-site during onboarding (the pilot model) | **No.** Click through SmartScreen's *More info → Run anyway*. |
| Clinics download it themselves from a link | **Yes, effectively.** A security warning at first contact kills adoption. |

⚠️ **The real risk is Smart App Control, not SmartScreen.** SAC blocks unsigned, no-reputation binaries **outright**
— there is no "run anyway" — and it is on by default on clean Windows 11 installs. (It is on this development
machine, which is exactly why `dotnet test` needs a redirected output path.) A clinic on a new Win 11 PC may be
unable to launch the app at all, so **test on a clean Win 11 machine with SAC on before assuming unsigned is fine**.

Before buying, check two things:

1. **Azure Trusted Signing** — Microsoft's own service, roughly **$10/month** rather than $200–600/year, and it
   signs from CI with no hardware token. ⚠️ Eligibility has been region- and organisation-age restricted; **confirm
   it is open to a Tunisian entity** before planning around it.
2. **Since 2023, OV and EV certificates must live on a hardware token or HSM**, which makes CI signing much harder.

⚠️ And note **OV does not remove the warning immediately** — SmartScreen reputation accrues with downloads over
time; only **EV** grants it instantly. An OV certificate bought today still shows warnings for a while, which is a
further argument for deferring rather than rushing.

**Recommendation: skip 2.3 for the pilot clinics you onboard yourself. Buy before self-service distribution.**

---

## 3. Deploy the hosted backend (half a day, once §2.4–2.7 exist)

Operator guide: [`deploy/README.md`](deploy/README.md). Compose file: `deploy/docker-compose.hosted.yml`.

- [ ] Point the domain's **A record** at the server. Caddy issues TLS automatically from `DOMAIN` + `ACME_EMAIL`.
- [ ] Copy `deploy/.env.hosted.example` → `deploy/.env` and fill **every** value. Generate real secrets
      (`openssl rand -base64 48`), never reuse the examples:
      `DOMAIN` · `ACME_EMAIL` · `POSTGRES_*` · `MINIO_ROOT_*` · **`AUTH_LOCAL_SIGNING_KEY`** ·
      `BACKUP_*` · `WALG_S3_*` · `INTERNAL_SUBNET`. Optional: `GOOGLE_*`, `HUGGINGFACE_API_KEY`.
- [ ] ⚠️ **`AUTH_LOCAL_SIGNING_KEY` must be set and must persist.** Absent, a key is generated onto the container's
      ephemeral layer: every redeploy signs the whole fleet out mid-shift, with nothing in any log naming the cause.
- [ ] ⚠️ **`DataProtection__KeyRingPath` must sit on a durable volume.** Startup fails without the setting, but a
      path with no volume behind it fails *silently* — every clinic's encrypted reminder and TTN credentials become
      undecryptable after the first redeploy. Verify the volume, not just the variable.
- [ ] **Add SMTP.** ⚠️ Known gap: `.env.hosted.example` and the compose file carry **no SMTP variables**, yet
      `HostedMultiTenant` is the one profile with public clinic signup enabled — so verification emails cannot send
      as shipped. Add to the `api` service:
      `Notification__Smtp__Server`, `__Port`, `__UseTls`, `__Username`, `__Password`, and set `FrontendUrl` to
      `https://<your domain>` (it is what builds the verification link).
- [ ] `docker compose -f deploy/docker-compose.hosted.yml up -d --build`
- [ ] Verify: `GET /health` returns 200, `GET /api/outbox` (admin) shows no ageing backlog, sign up a test clinic
      end-to-end and confirm the email arrives.
- [ ] **Run a restore drill before any real patient data exists.** ⚠️ `restore-backup` refuses to run in the hosted
      profile by design (its safety interlock looks for a listener on the same machine), so **write down and
      rehearse the container-based restore procedure**. A backup you have never restored is not a backup.
- [ ] ✅ **Done for the compose deployments** — `Security__EnforceCsp: "true"` ships in *both*
      `docker-compose.hosted.yml` and `docker-compose.prod.yml` (`hosted-security-hardening` Part D walked 30
      routes with 0 violations). Still owed **per deployment** that does not use those files: walk every page,
      then set the flag and walk them again, because what makes enforcing safe is that somebody clicked through
      the app *in this deployment*. ⚠️ Note the flag constrains resource **origins** only — `script-src` carries
      `'unsafe-inline'`, so it is not XSS protection; see `SECURITY_ARCHITECTURE.md` § 9.5.
- [ ] Set up **uptime monitoring** on `/health` and an alert on it.

---

## 4. Desktop app (half a day — does **not** wait for 2.3)

- [ ] Install **Inno Setup 6** on the build machine (`packaging/publish-server.ps1` locates `ISCC.exe` itself).
- [ ] Run `packaging/publish-server.ps1` — it publishes the WebView2 shell and builds **both** installers
      (`packaging/server/clinic-server.iss`, `packaging/client/clinic-client.iss`).
- [ ] *(Optional, when 2.3 exists)* **Sign** `ClinicManagement.DesktopShell.exe` *and* the installer with
      `signtool`. Shipping unsigned is a valid pilot choice — see § 2.3.
- [ ] Install on a **clean Windows 11 PC with Smart App Control ON** (not the dev machine) and confirm the app
      actually launches unsigned. This is the go/no-go test for deferring 2.3. Also confirm: WebView2 runtime installs silently, and
      typing the bare hosted domain — **no port** — connects. That last one is the new `ServerProbe` 443-before-5001
      path and it has never been exercised against a real hosted server.
- [ ] Decide how clinics get updates: an auto-updater, or a download page plus a "new version" notice.

---

## 5. Android shell — sideload now (needs only 2.8), Play later (needs 2.2)

**Done already** (2026-08-19), so this section starts further along than it reads:

- [x] `applicationId` settled — see §1.
- [x] **`targetSdk` + `compileSdk` 36.** Play refuses new submissions below API 36 from **31 August 2026**. Landed
      with the toolchain chain it forces: Gradle 8.13 / AGP 8.13.0 / Kotlin 2.2.20, SDK platform + build-tools 36.
- [x] **`signingConfigs.release` wired**, reading a git-ignored `keystore.properties`; absent, the release still
      builds *unsigned* so R8 stays exercisable on a machine with no key.
      *(This entry used to claim the module « can only produce debug-signed builds ». That was never true — the
      `-Pandroid.injected.signing.*` form works with no `signingConfigs` block at all.)*
- [x] `versionCode` 3. Lint clean under `warningsAsErrors` at the new toolchain; release APK (~130 KB after R8,
      from 2.4 MB debug) and AAB both produced.

Still to do:

- [ ] Create an **upload keystore** (`keytool -genkeypair`, RSA 4096, ≥25 years) and fill
      `mobile/android/keystore.properties` from `keystore.properties.example`. **Back the `.jks` up in two places
      that are not the build machine** — losing it means you can never update the app under the same listing, and
      the passwords are not recoverable either.
- [ ] **Install the signed release APK on a physical phone and do the hardware walk** — owed since the shell was
      written, and the release build has never run anywhere. Full list in
      [`mobile/STORE-SUBMISSION.md`](mobile/STORE-SUBMISSION.md): rotation, Split View, the gesture bar, camera
      upload, print, the biometric resume, still-signed-in after force-quit, a **portless** hosted domain, the
      untrusted-certificate message, and `delete window.__clinicShell`.
      ⚠️ The `@JavascriptInterface` keep rule has never run on a device; if the bridge fails only in release, start there.
- [ ] **Ship it to the pilot clinics as a file.** No store, no account, no company. Decide the update channel — a
      « nouvelle version » notice or a download page — the same open decision as §4's.
- [ ] Verify the **installable web app** on a real Android phone and a real iPhone (`web/app/manifest.ts` is
      complete, but there is no service worker, so Chrome may make a shortcut rather than a full WebAPK).
- [ ] *(Needs 2.2)* Play Console: listing, screenshots, **feature graphic 1024×500**, **privacy policy URL**,
      **Health apps declaration**, the **« not a medical device » disclaimer**, and the **Data safety** form —
      answer it honestly; this app handles health data.
- [ ] *(Needs 2.2 + §3)* **App access for the reviewer.** The first screen asks for a server address, so a reviewer
      with no domain sees a dead end. Needs a live domain, a demo clinic with a **granted subscription**, and a
      demo user of role **`doctor`** — never `admin`, who is forced into TOTP on the hosted profile. Details in
      `mobile/STORE-SUBMISSION.md`.
- [ ] *(Needs 2.2)* Enable **Play App Signing**, then release to the **internal testing** track first.

---

## 6. iPhone shell (2–4 days, needs §1 + 2.1 + 2.8 + 2.9)

- [ ] **Get `ios-shell.yml` green first.** The Swift has never been compiled by anything.
- [ ] Set the final `PRODUCT_BUNDLE_IDENTIFIER` in `mobile/ios/project.yml` — identical to Android's.
- [ ] Create the **app icon** — there is none, and App Store Connect rejects builds without one.
- [ ] Register the App ID, create signing certificates and provisioning profiles.
- [ ] Archive on a Mac (or paid macOS runner), upload to **TestFlight**, install on a **real iPhone**.
      ⚠️ Do not accept a simulator as verification: it does not faithfully exercise persistent cookies, print or
      biometrics — three of the four reasons this shell exists.
- [ ] App Store listing, privacy policy, **App Privacy** questionnaire (health data).
- [ ] Submit for review. Budget **1–3 days** per submission, and expect at least one rejection round.

---

## 7. Legal and compliance — Tunisia (start early; longest lead time of all)

Not legal advice. Get a Tunisian lawyer or DPO; this is patient health data on a hosted server.

- [ ] **INPDP** (Instance Nationale de Protection des Données à Caractère Personnel) — determine whether your
      processing requires **declaration or authorisation**. Health data is a special category; assume the stricter
      path until told otherwise.
- [ ] **Data residency**: decide whether patient data may leave Tunisia. This can dictate your hosting provider —
      settle it *before* §2.4.
- [ ] **Data Processing Agreement** with the hosting provider, and with the SMTP/SMS providers.
- [ ] Publish a **privacy policy** and **terms of service** (French). Both stores require the privacy URL.
- [ ] Define **retention and deletion** — note the product has **no patient merge and no soft delete**; deleting a
      patient is refused when records are attached, and archiving is the escape hatch. Your policy has to match what
      the software actually does.
- [ ] Patient **consent** wording for storing and processing their record.
- [ ] Decide what a clinic gets on **offboarding** — the CSV export exists; make it a written commitment.
- [ ] If clinics will use **CNAM e-invoicing (TTN « El Fatoora »)**: each clinic needs its **own** qualified signing
      certificate. ⚠️ There is **no admin screen** for it yet — it is installed by hand into four `Clinic` columns,
      and `verify-schema`'s `ttn-identity-is-complete` is the only guard.

---

## 8. Before the first real clinic (ongoing)

- [ ] **Restore drill, again**, with real-shaped data. Then schedule it quarterly.
- [ ] Watch `GET /api/outbox` for the first weeks — reminders, e-invoices and document emails all drain through it,
      and `/hangfire` is loopback-only so this endpoint is your only window.
- [ ] Agree a **support channel** and who answers it.
- [ ] Write the **admin recovery** runbook: `reset-admin-password` is a console verb, not a web page.
- [ ] Provision the first clinic yourself and sit with them for a day. Nothing else finds what is actually missing.

---

## Known gaps worth deciding about explicitly

Each is a conscious "not yet", not an oversight:

- ~~**No MFA** on staff accounts.~~ ✅ **Closed by `hosted-security-hardening` Part A**: TOTP is **mandatory for
  clinic administrators** on `HostedMultiTenant` (`DeploymentProfile.RequiresAdminSecondFactor`, decided by the
  deployment kind and by no operator setting), optional for doctors and secretaries, with 8 single-use recovery
  codes and three documented ways back. `SelfHostedLan` is deliberately ✗ — an administrator locked out on a
  clinic's own offline PC with no vendor to call is worse than the threat.
- **No auto-update** for the desktop app.
- **No TTN admin surface** (§7).
- **SignalR hub methods run with no tenant scope** — safe today only because `ClinicHub` reads an unfiltered table;
  the next hub method that reads a filtered entity must set a scope explicitly.
- **No database-backed integration tests** — tenant isolation is verified against mocks plus `verify-schema`, and
  the unit suite is the backend's only automated check.

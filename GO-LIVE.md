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

- [ ] **Bundle identifier.** Currently the placeholder `com.clinicmanagement.shell` in *both*
      `mobile/android/app/build.gradle.kts` (`applicationId`) and `mobile/ios/project.yml`
      (`PRODUCT_BUNDLE_IDENTIFIER`). ⚠️ **It cannot be changed after the first store submission.** Use a domain you
      own, e.g. `tn.<yourbrand>.clinic`. Keep Android and iOS identical.
- [ ] **Public app name** (store listing + install name).
- [ ] **The domain** the hosted backend runs on, e.g. `app.yourclinic.tn`. Buy it if you have not.
- [ ] **Who owns the accounts** — Apple, Google Play and the domain should be registered to the *company*, never a
      personal account. Moving them later is painful or impossible.

---

## 2. Purchases and accounts (start now — lead time is the constraint)

| # | Item | Approx. cost | Lead time | Needed for |
|---|---|---|---|---|
| 2.1 | **Apple Developer Program** | ~$99 / year | hours–days (can be longer for companies: D-U-N-S number required) | iPhone shell, at all |
| 2.2 | **Google Play Console** | ~$25 one-time | hours–days | Android shell |
| 2.3 | **Windows code signing** — ⚠️ **deferrable, see below** | ~$10 / mo (Azure Trusted Signing) or ~$200–600 / yr (OV/EV) | days–weeks (identity vetting) | Desktop installer without a SmartScreen warning |
| 2.4 | **Server hosting** (VPS, ≥4 GB RAM, backed-up disk) | ~$20–60 / month | minutes | The hosted backend |
| 2.5 | **Domain name** | ~$10–40 / year | minutes | TLS, store listings, email links |
| 2.6 | **Off-site backup storage** (S3-compatible bucket, for WAL-G PITR) | a few $ / month | minutes | `WALG_S3_*` — off-machine recovery |
| 2.7 | **Transactional email sender** (SMTP: Brevo / Postmark / SES) | free tier–$15 / month | hours (domain verification: SPF + DKIM) | Clinic self-signup verification emails |
| 2.8 | A **physical Android phone** and a **physical iPhone** | — | — | The hardware walks in §5/§6 that no CI replaces |
| 2.9 | A **Mac**, or a paid macOS CI runner | — | — | iOS archive + upload (unsigned CI build is not enough) |

⚠️ **2.9:** `mobile/ios/` has **never been compiled**. Get `.github/workflows/ios-shell.yml` green *before* spending
anything on 2.1 — a green build is the cheapest proof the Swift is real.

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
- [ ] Walk every page, then set **`Security__EnforceCsp=true`** and walk them again. It ships report-only precisely
      because only a human who has clicked through the app can say enforcing is safe.
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

## 5. Android shell (1–2 days, needs §1 + 2.2 + 2.8)

- [ ] Set the final `applicationId` from §1 in `mobile/android/app/build.gradle.kts`.
- [ ] Create an **upload keystore** (`keytool -genkeypair`, RSA 2048, ≥25 years). **Back it up in two places** —
      losing it means you can never update the app under the same listing.
- [ ] Add a `signingConfigs.release` block and wire it to the `release` build type — there is **none** today, so the
      module can only produce debug-signed builds.
- [ ] Store the keystore + passwords as GitHub secrets (base64) if you want CI to sign.
- [ ] Build a **release** AAB (`./gradlew bundleRelease`) and install the **release APK on a physical phone** — R8
      shrinking has never been run on a device.
- [ ] Hardware walk (owed since the shell was written): rotation, Split View, the gesture bar, camera upload,
      print, the biometric resume, and the address screen with a portless hosted domain.
- [ ] Play Console: listing, screenshots, **privacy policy URL** (mandatory), and the **Data safety** form —
      answer it honestly; this app handles health data.
- [ ] Enable **Play App Signing**, then release to the **internal testing** track first.

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

- **No MFA** on staff accounts.
- **No auto-update** for the desktop app.
- **No TTN admin surface** (§7).
- **SignalR hub methods run with no tenant scope** — safe today only because `ClinicHub` reads an unfiltered table;
  the next hub method that reads a filtered entity must set a scope explicitly.
- **No database-backed integration tests** — tenant isolation is verified against mocks plus `verify-schema`, and
  the unit suite is the backend's only automated check.
